using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>세션의 커맨드 실행: <c>with preconditions:</c> 블록, <c>require</c>·<c>cost</c>·<c>choose</c>, 비용 빼기와 <c>pay</c> (G110·G183~G187).</summary>
    internal sealed partial class Session
    {
        /// <summary>
        /// 커맨드의 <c>with preconditions:</c> 블록만 실행한다 (spec-gaps G110·G111). 인자는 매개변수 이름으로 받는다.
        /// <c>require</c>는 모두 위에서부터 평가하고, 앞에서 실패한 <c>require</c>가 있은 뒤의 실행 오류는 그 문장을
        /// "평가 안 됨"으로 만든다 (대입이면 그 변수가 비어, 그 변수를 읽는 뒤 문장도 평가 안 됨이 된다).
        /// 앞에 실패가 없을 때의 오류와 단계 예산·순환 오류는 그대로 던진다.
        /// </summary>
        public List<RequireOutcome> Preconditions(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            SourceSpan site) => Command(command, args, site, runBody: false);

        /// <summary>
        /// 사전 조건을 평가하고, <paramref name="runBody"/>이고 실패한 <c>require</c>가 없으면 같은 지역 변수로 본문을 이어서
        /// 실행한다 (G132). 본문 실행 중에만 상태 쓰기가 열린다. 본문 안에서 읽는 <c>derived</c>는 따로 도는 순수 실행이다.
        /// </summary>
        public List<RequireOutcome> Command(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            SourceSpan site, bool runBody, Action? bodyStarts = null)
        {
            int depth = _stack.Count;
            var frame = Enter(command, null, Array.Empty<Value>(), args);
            frame.CallSite = site;
            frame.Result = ResultTo.Discard;
            _stack.Add(frame);
            var results = new List<RequireOutcome>();
            try
            {
                var body = command.Definition!.Body;
                bool hasBlock = body.Count > 0 && body[0] is WithStmt;
                bool failed = hasBlock && EvaluateBlock((WithStmt)body[0], frame, results);
                if (runBody && !failed)
                {
                    bodyStarts?.Invoke(); // 사전 조건을 통과해 본문이 시작된다: 새 Flow (난수열을 연다, G182)
                    frame.Writable = true;
                    PayCosts();
                    frame.Blocks.Add(BodyBlock(command.Definition!, frame.Layout, hasBlock ? 1 : 0));
                    RunUntil(depth);
                    if (depth == 0) Detach();
                }
                return results;
            }
            finally
            {
                Truncate(depth);
            }
        }

        public Value ChooseOption(FunctionValue command, IReadOnlyDictionary<string, Value> args, ChooseInfo choose, SourceSpan site)
        {
            int depth = _stack.Count;
            var frame = Enter(command, null, Array.Empty<Value>(), args);
            frame.CallSite = site;
            frame.Result = ResultTo.Discard;
            _stack.Add(frame);
            try
            {
                var block = ((WithStmt)command.Definition!.Body[0]).Body;
                foreach (int i in choose.Statements)
                {
                    Exec(block[i], frame);
                    RunUntil(depth + 1);
                }
                return Eval(choose.Call.Arguments[1].Value, frame);
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, choose.Call.Span);
                throw;
            }
            finally
            {
                Truncate(depth);
            }
        }

        /// <summary><c>with preconditions:</c> 블록 안의 문장들. 실패한 <c>require</c>가 있으면 true.</summary>
        private bool EvaluateBlock(WithStmt with, Frame frame, List<RequireOutcome> results)
        {
            bool failed = false;
            int depth = _stack.Count;
            foreach (var stmt in with.Body)
            {
                try
                {
                    if (stmt is AssignStmt)
                    {
                        Exec(stmt, frame);
                        RunUntil(depth); // x = f()로 들어간 함수를 끝까지
                        continue;
                    }
                    var statement = (CallExpr)((ExprStmt)stmt).Expr;
                    var outcome = IsCost(statement) ? Cost(statement, frame)
                        : IsChoose(statement) ? Choose(statement, frame)
                        : Require(statement, frame);
                    if (outcome.Status == RequireStatus.Failed) failed = true;
                    results.Add(outcome);
                }
                catch (ScriptRuntimeException e) when (failed && e.Code != Codes.RunBudget && e.Code != Codes.RunCycle)
                {
                    Truncate(depth);
                    if (stmt is AssignStmt assign)
                    {
                        frame.Locals.Remove(((NameExpr)assign.Target).Name);
                        continue;
                    }
                    var call = (CallExpr)((ExprStmt)stmt).Expr;
                    results.Add(IsCost(call)
                        ? RequireOutcome.Cost(RequireStatus.NotEvaluated, null, null, null, call.Span)
                        : IsChoose(call)
                            ? RequireOutcome.Choose(RequireStatus.NotEvaluated, ((NameExpr)call.Arguments[0].Value).Name, call.Span)
                            : new RequireOutcome(RequireStatus.NotEvaluated, LiteralKey(call), null, call.Span));
                }
            }
            return failed;
        }

        /// <summary>
        /// <c>require(조건, text("키", ...))</c>: 조건의 참/거짓(G104)을 보고, 메시지는 조건이 거짓일 때만 평가한다
        /// (파이썬 <c>assert</c>의 메시지와 같다, G113). 통과한 <c>require</c>의 키는 리터럴로 적혔을 때만 있다.
        /// 위치 인자가 둘이고 키워드 인자가 없다는 것은 로드 시점에 보았다.
        /// </summary>
        private RequireOutcome Require(CallExpr call, Frame frame)
        {
            try
            {
                Tick();
                var condition = Eval(call.Arguments[0].Value, frame);
                if (PureOps.Truthy(condition))
                    return new RequireOutcome(RequireStatus.Passed, LiteralKey(call), null, call.Span);
                var message = Eval(call.Arguments[1].Value, frame);
                if (!(message is TextValue text))
                {
                    throw Fail(Codes.RunType, "the message of require() must be a text(...) value, not " + message.KindName,
                        "write require(condition, text(\"msg.key\", name=value, ...))");
                }
                return new RequireOutcome(RequireStatus.Failed, text.Key, text.Args, call.Span);
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, call.Span);
                throw;
            }
        }

        /// <summary>블록 안의 문장은 덮어쓰지 않은 내장이라는 것을 로드 때 보았다 (G112): 이름만 본다.</summary>
        private static bool IsCost(CallExpr call) =>
            call.Callee is NameExpr name && name.Name == FunctionChecker.CostName;

        /// <summary>
        /// <c>cost(칸, 금액)</c> (G185·G186): 첫 자리는 값이 아니라 칸이다. 마지막 이름 앞까지만 계산해 칸을 찾고, 그 칸의 값이
        /// 블록에서 같은 칸에 앞서 나온 금액과 이 금액을 더한 것 이상이면 통과다. 빼는 것은 본문이 시작될 때다 (<see cref="PayCosts"/>).
        /// 모양(위치 인자 둘, 첫 자리는 속성)은 로드 때 보았다.
        /// </summary>
        private RequireOutcome Cost(CallExpr call, Frame frame)
        {
            try
            {
                Tick();
                var slot = (AttributeExpr)call.Arguments[0].Value;
                var owner = SlotOwner("cost", slot, frame);
                var have = owner.ReadSlot(slot.Name);
                var amount = Amount("cost", have, Eval(call.Arguments[1].Value, frame));
                var need = amount;
                foreach (var earlier in _costs)
                {
                    if (earlier.Name == slot.Name && PureOps.ValuesEqual((Value)earlier.Owner, (Value)owner))
                        need = PureOps.Arithmetic(BinaryOp.Add, need, earlier.Amount);
                }
                _costs.Add(new CostEntry(owner, slot.Name, amount, call.Span));
                var status = PureOps.CompareNumbers(have, need) >= 0 ? RequireStatus.Passed : RequireStatus.Failed;
                return RequireOutcome.Cost(status, owner.SlotName(slot.Name), have, need, call.Span);
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, call.Span);
                throw;
            }
        }

        private static bool IsChoose(CallExpr call) =>
            call.Callee is NameExpr name && name.Name == FunctionChecker.ChooseName;

        /// <summary>
        /// <c>choose(매개변수, where=함수)</c>·<c>choose(매개변수, from_=목록)</c>의 검사 (G183): 제출된 인자가 후보인가.
        /// <c>where=</c>의 후보는 그 타입의 살아 있는 엔티티를 함수로 거른 것인데, 인자가 그 타입의 살아 있는 엔티티라는 것은
        /// 인자 검사(G115)가 보았으므로 인자 하나에만 함수를 부르면 된다. <c>from_=</c>는 목록에 같은 값이 있는가다.
        /// 후보를 펼치는 것은 호스트 API의 일이다 (G184).
        /// </summary>
        private RequireOutcome Choose(CallExpr call, Frame frame)
        {
            try
            {
                Tick();
                string parameter = ((NameExpr)call.Arguments[0].Value).Name;
                var value = Eval(call.Arguments[0].Value, frame);
                var option = call.Arguments[1];
                var given = Eval(option.Value, frame);
                bool candidate;
                if (option.Name == FunctionChecker.WhereName)
                {
                    candidate = !(value is NoneValue) &&
                                PureOps.Truthy(Invoke(given, new[] { value }, NoKeywords, option.Span));
                }
                else
                {
                    candidate = given is ListValue || given is IRuntimeCollection
                        ? PureOps.Contains(given, value)
                        : throw Fail(Codes.RunType, "from_= of choose() must be a list, not " + given.KindName,
                            "write choose(" + parameter + ", from_=[...]) (spec-gaps G183)");
                }
                return RequireOutcome.Choose(candidate ? RequireStatus.Passed : RequireStatus.Failed, parameter, call.Span);
            }
            catch (ScriptRuntimeException e)
            {
                Attach(e, call.Span);
                throw;
            }
        }

        public static readonly IReadOnlyDictionary<string, Value> NoKeywords =
            new SortedDictionary<string, Value>(StringComparer.Ordinal);

        /// <summary><c>cost</c>·<c>pay</c>의 칸을 가진 값: 칸 식의 마지막 이름 앞까지를 계산한다 (G186).</summary>
        private IRuntimeSlots SlotOwner(string function, AttributeExpr slot, Frame frame)
        {
            var owner = Eval(slot.Target, frame);
            if (owner is IRuntimeSlots slots) return slots;
            throw Fail(Codes.RunType, owner is NoneValue
                    ? function + "() cannot take '" + slot.Name + "' from None"
                    : function + "() takes a member of a component, not '" + slot.Name + "' of " + owner.KindName,
                owner is NoneValue
                    ? "the component or row is missing; check it with 'is not None' first"
                    : "write " + function + "(party.Treasury.gold, n) (spec-gaps G186)");
        }

        /// <summary>
        /// <c>cost</c>·<c>pay</c>의 금액: <c>int</c>나 <c>fixed</c>이고 음수가 아니다. <c>fixed</c> 칸의 <c>int</c> 금액은 그대로 두면
        /// 계산에서 올라가고, <c>int</c> 칸에 <c>fixed</c> 금액은 오류다 (인자의 G95와 같다).
        /// </summary>
        private Value Amount(string function, Value have, Value amount)
        {
            if (!PureOps.IsNumber(amount))
            {
                throw Fail(Codes.RunType, "the amount of " + function + "() must be int or fixed, not " + amount.KindName);
            }
            if (have is IntValue && amount is FixedValue)
            {
                throw Fail(Codes.RunType, "the amount of " + function + "() is fixed, but the field is an int",
                    "a fixed value is not turned into an int; use floor(...), ceil(...) or round(...)");
            }
            if (PureOps.CompareNumbers(amount, new IntValue(0)) < 0)
            {
                string shown = amount is IntValue i
                    ? i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : ((FixedValue)amount).Value.ToString();
                throw Fail(Codes.RunRange, "the amount of " + function + "() is " + shown + ", which is negative",
                    "to give something back, add to the field in the body (spec-gaps G185)");
            }
            return amount;
        }

        /// <summary>
        /// 사전 조건을 모두 통과해 본문이 시작될 때 블록의 <c>cost</c>를 적힌 순서대로 뺀다 (G185). 금액은 블록에서 계산한 그대로다.
        /// 쓰기는 본문의 대입과 같은 길이라 같은 트랜잭션에 들고, 본문이 실패하면 함께 되돌아간다 (C8).
        /// </summary>
        private void PayCosts()
        {
            foreach (var cost in _costs)
            {
                try
                {
                    var have = cost.Owner.ReadSlot(cost.Name);
                    ((IRuntimeAssignable)cost.Owner).SetAttribute(cost.Name, PureOps.Arithmetic(BinaryOp.Subtract, have, cost.Amount));
                }
                catch (ScriptRuntimeException e)
                {
                    Attach(e, cost.Span);
                    throw;
                }
            }
            _costs.Clear();
        }

        /// <summary>
        /// 본문 중간의 비용 <c>pay(칸, 금액)</c> (G187): 칸의 값이 금액 이상이면 빼고 <c>True</c>, 모자라면 아무것도 바꾸지 않고
        /// <c>False</c>. 첫 자리는 <c>cost</c>처럼 칸 자체다(G186). 문장 수준의 쓰기라 자리 규칙은 다른 쓰는 내장과 같다 (G117·G135).
        /// </summary>
        private void Pay(CallExpr call, Frame frame, string? target)
        {
            if (!frame.Writable)
            {
                throw Fail(Codes.RunNotWritable,
                    "pay() " + PureBuiltins.EffectPhrase(PureBuiltins.PayName) + ", so it cannot be called here",
                    "call it on its own line (or as 'ok = pay(...)') in a @command body or in a function that changes " +
                    "the world called on its own line from it (spec-gaps G117, G187)");
            }
            // 함수 값으로 받은 pay(f = pay; f(...))는 로드 때 모양을 보지 못했다
            if (call.Arguments.Count != 2 || call.Arguments[0].Name != null || call.Arguments[1].Name != null ||
                !(call.Arguments[0].Value is AttributeExpr slot))
            {
                throw Fail(Codes.RunScript, "pay() takes a field and an amount, such as pay(party.Treasury.gold, n)",
                    "the first argument is the field itself (spec-gaps G186, G187)");
            }
            var owner = SlotOwner("pay", slot, frame);
            var have = owner.ReadSlot(slot.Name);
            var amount = Amount("pay", have, Eval(call.Arguments[1].Value, frame));
            bool enough = PureOps.CompareNumbers(have, amount) >= 0;
            if (enough) ((IRuntimeAssignable)owner).SetAttribute(slot.Name, PureOps.Arithmetic(BinaryOp.Subtract, have, amount));
            if (target != null) frame.Locals[target] = enough ? BoolValue.True : BoolValue.False;
        }

        /// <summary>
        /// 평가하지 않은 메시지의 키: <c>text("키", ...)</c>로 적혀 있고 그 <c>text</c>가 내장일 때만 (모드가 덮어썼으면 모른다).
        /// </summary>
        private string? LiteralKey(CallExpr call)
        {
            if (call.Arguments.Count < 2 || !(call.Arguments[1].Value is CallExpr text) ||
                !(text.Callee is NameExpr name) || name.Name != PureBuiltins.TextName ||
                _owner.Globals.Lookup(_stack[_stack.Count - 1].Function.Mod, PureBuiltins.TextName) != null ||
                _stack[_stack.Count - 1].Layout.Contains(PureBuiltins.TextName))
                return null;
            return text.Arguments.Count > 0 && text.Arguments[0].Name == null && text.Arguments[0].Value is StringLiteral s
                ? s.Value
                : null;
        }
    }
}
