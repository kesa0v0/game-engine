using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>모드 이름 공간(<see cref="ModScope"/>)들을 함수 본문의 전역 이름 조회로 보여 주는 어댑터.</summary>
    public sealed class ScopeGlobals : IScriptGlobals
    {
        private readonly IReadOnlyDictionary<string, ModScope> _scopes;

        public ScopeGlobals(IReadOnlyDictionary<string, ModScope> scopes) => _scopes = scopes;

        public Value? Lookup(string mod, string name) =>
            _scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var value, out _) ? value : null;
    }

    /// <summary>
    /// 순수 함수 실행기 (spec S2·S4, spec-gaps G95~G103). 함수 본문을 평가해 값을 돌려준다.
    /// 읽기만 하고(대입은 지역 변수뿐), 평가 노드마다 단계를 세어 한 번의 <see cref="Run"/>이
    /// 예산을 넘으면 오류를 낸다(G101). 함수 호출 체인에 같은 함수가 다시 나오면 재귀 오류다(G102).
    /// 오류는 <see cref="ScriptRuntimeException"/>이고 오류가 난 식의 위치와 호출 흔적이 붙는다.
    /// 호스트가 읽기 도중 다시 <see cref="Run"/>을 불러도 된다: 호출 체인은 새로 시작하고, 단계는 넘겨준
    /// <see cref="StepMeter"/>가 있으면 이어서 센다.
    /// </summary>
    public sealed class PureEvaluator
    {
        /// <summary>한 번의 실행에 주는 기본 단계 수. 정할 근거 자료가 아직 없어 넉넉하게 둔다 (G101).</summary>
        public const int DefaultStepBudget = 100_000;

        private readonly IScriptGlobals _globals;
        private readonly IReadOnlyDictionary<string, Value> _builtins;
        private readonly int _budget;

        public PureEvaluator(IScriptGlobals globals, IReadOnlyDictionary<string, Value> builtins,
            int stepBudget = DefaultStepBudget)
        {
            _globals = globals;
            _builtins = builtins;
            _budget = stepBudget;
        }

        /// <summary>
        /// 함수 값(<c>def</c>나 람다)을 위치 인자로 부른다. <paramref name="meter"/>가 없으면 이 실행만의 예산을 새로 주고,
        /// 있으면 그 계량기의 남은 단계를 쓴다 (최상위 읽기 하나에 예산 하나, G101).
        /// </summary>
        public Value Run(Value function, IReadOnlyList<Value> args, SourceSpan callSite = default, StepMeter? meter = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            try
            {
                return session.Invoke(function, args, new SortedDictionary<string, Value>(StringComparer.Ordinal), callSite);
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite; // 호출 자체의 오류(인자 수 등)는 부른 위치를 가리킨다
                throw;
            }
        }

        /// <summary>
        /// 커맨드의 <c>with preconditions:</c>만 평가해 <c>require</c>마다 결과를 돌려준다 (spec-gaps G110·G111).
        /// 인자는 매개변수 이름으로 받는다. 블록이 없으면 빈 목록이다.
        /// </summary>
        public List<RequireOutcome> RunPreconditions(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            SourceSpan callSite = default, StepMeter? meter = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            try
            {
                return session.Preconditions(command, args, callSite);
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite;
                throw;
            }
        }

        /// <summary>
        /// 커맨드를 실행한다: 사전 조건을 평가하고 실패한 <c>require</c>가 없으면 같은 지역 변수로 본문을 이어서 실행한다
        /// (spec-gaps G132). 본문의 속성 대입은 상태 쓰기다. 결과는 <c>require</c>마다의 결과이고, 실패가 있으면 본문은 돌지 않았다.
        /// </summary>
        public List<RequireOutcome> RunCommand(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            SourceSpan callSite = default, StepMeter? meter = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            try
            {
                return session.Command(command, args, callSite, runBody: true);
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite;
                throw;
            }
        }

        private enum Flow
        {
            Normal,
            Break,
            Continue,
            Return,
        }

        /// <summary><c>def</c>마다 지역 변수 이름 (한 번 계산해 둔다).</summary>
        private readonly Dictionary<FunctionDef, SortedSet<string>> _localNames = new Dictionary<FunctionDef, SortedSet<string>>();

        private SortedSet<string> LocalNamesOf(FunctionDef def)
        {
            if (!_localNames.TryGetValue(def, out var names))
            {
                names = FunctionLocals.Of(def);
                _localNames.Add(def, names);
            }
            return names;
        }

        private sealed class Frame
        {
            public Frame(FunctionValue function, SortedDictionary<string, Value> locals, SortedSet<string> localNames)
            {
                Function = function;
                Locals = locals;
                LocalNames = localNames;
            }

            public FunctionValue Function { get; }
            public SortedDictionary<string, Value> Locals { get; }

            /// <summary>
            /// 이 함수의 지역 변수 이름 전체 (파이썬 규칙, G104). 여기 든 이름은 값이 아직 없어도 전역·내장으로 넘어가지 않는다.
            /// </summary>
            public SortedSet<string> LocalNames { get; }

            public SourceSpan CallSite { get; set; }

            /// <summary>
            /// 쓰기 모드 (G117 런타임 안전망). 커맨드 본문, 그리고 쓰기 모드 프레임이 문장으로 부른 쓰기 함수만 참이다.
            /// 순수 함수에서 시작한 호출, 식 안의 호출, <c>derived</c>·사전 조건·<c>Cond</c>는 커맨드 실행 중이라도 순수다.
            /// </summary>
            public bool Writable { get; set; }
        }

        private sealed class Session
        {
            private readonly PureEvaluator _owner;
            private readonly List<Frame> _stack = new List<Frame>();
            private readonly StepMeter _meter;
            private Value _returned = NoneValue.Instance;

            public Session(PureEvaluator owner, StepMeter meter)
            {
                _owner = owner;
                _meter = meter;
            }

            // ---- 오류 ----

            private static bool HasSpan(ScriptRuntimeException e) => e.Span.Line > 0;

            private ScriptRuntimeException Fail(string code, string message, string? hint = null) =>
                new ScriptRuntimeException(code, message, hint);

            /// <summary>
            /// 이 실행 단위의 프레임을 오류의 흔적에 한 번 덧붙인다. 위치가 아직 없으면 이 식이 오류 위치다.
            /// 이미 있으면 오류는 안쪽 실행(이 식이 읽은 다른 <c>derived</c>)에서 났으므로, 이 식의 위치는 가장 안쪽
            /// 프레임에 "at …"으로 붙여 어디서 읽었는지 남긴다.
            /// </summary>
            private void Attach(ScriptRuntimeException e, SourceSpan span)
            {
                if (ReferenceEquals(e.TracedBy, this)) return;
                e.TracedBy = this;
                bool nested = HasSpan(e);
                if (!nested) e.Span = span;
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    var frame = _stack[i];
                    string line = frame.Function.Name;
                    if (nested && i == _stack.Count - 1) line += " at " + span;
                    if (frame.CallSite.Line > 0) line += " (called at " + frame.CallSite + ")";
                    e.AddTrace(line);
                }
            }

            private void Tick()
            {
                if (++_meter.Used > _meter.Budget)
                {
                    throw Fail(Codes.RunBudget, "the function used more than " + _meter.Budget + " evaluation steps",
                        "a loop may be too large or endless; the budget is shared by everything one read computes, " +
                        "including the derived values it reads");
                }
            }

            // ---- 호출 ----

            /// <param name="writable">
            /// 부른 쪽이 쓰기 모드 프레임이고 이 호출이 문장일 때 참. 그래도 부르는 함수가 쓰기 함수여야 쓰기 모드가 된다 (G117).
            /// </param>
            public Value Invoke(Value function, IReadOnlyList<Value> positional,
                IReadOnlyDictionary<string, Value> keywords, SourceSpan site, bool writable = false)
            {
                switch (function)
                {
                    case FunctionValue f:
                        return CallFunction(f, null, null, positional, keywords, site, writable);
                    case ClosureValue c:
                        return CallFunction(c.Function, c.Captured, c.CapturedLocals, positional, keywords, site, writable);
                    case PureBuiltin b:
                        if (b.KeywordBody != null) return b.KeywordBody(positional, keywords);
                        if (keywords.Count > 0) throw Fail(Codes.RunScript, b.Name + "() does not take keyword arguments");
                        return b.Body(positional);
                }
                throw Fail(Codes.RunScript, "'" + function.KindName + "' is not callable");
            }

            private static bool SameFunction(FunctionValue a, FunctionValue b) =>
                ReferenceEquals(a.Definition, b.Definition) && ReferenceEquals(a.Lambda, b.Lambda);

            private Value CallFunction(FunctionValue fn, IReadOnlyDictionary<string, Value>? captured,
                SortedSet<string>? capturedLocals, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords, SourceSpan site,
                bool writable)
            {
                // 커맨드는 제출로만 실행된다 (G118). 이름으로 잡히지 않은 길(함수 값)의 마지막 방어선
                if (fn.IsCommand)
                {
                    throw Fail(Codes.RunScript, "'" + fn.Name + "' is a @command, so logic cannot call it",
                        "a command only runs when it is submitted; move the shared part into a plain function (spec-gaps G118)");
                }
                var newFrame = Enter(fn, captured, capturedLocals, positional, keywords);
                newFrame.CallSite = site;
                newFrame.Writable = writable && fn.Writes;
                _stack.Add(newFrame);
                try
                {
                    if (fn.Lambda != null) return Eval(fn.Lambda.Body, newFrame);
                    var flow = ExecBlock(fn.Definition!.Body, newFrame);
                    return flow == Flow.Return ? _returned : NoneValue.Instance;
                }
                finally
                {
                    _stack.RemoveAt(_stack.Count - 1);
                }
            }

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
                SourceSpan site, bool runBody)
            {
                var frame = Enter(command, null, null, Array.Empty<Value>(), args);
                frame.CallSite = site;
                _stack.Add(frame);
                var results = new List<RequireOutcome>();
                try
                {
                    var body = command.Definition!.Body;
                    bool hasBlock = body.Count > 0 && body[0] is WithStmt;
                    bool failed = hasBlock && EvaluateBlock((WithStmt)body[0], frame, results);
                    if (runBody && !failed)
                    {
                        frame.Writable = true;
                        for (int i = hasBlock ? 1 : 0; i < body.Count; i++)
                            if (Exec(body[i], frame) == Flow.Return) break;
                    }
                    return results;
                }
                finally
                {
                    _stack.RemoveAt(_stack.Count - 1);
                }
            }

            /// <summary><c>with preconditions:</c> 블록 안의 문장들. 실패한 <c>require</c>가 있으면 true.</summary>
            private bool EvaluateBlock(WithStmt with, Frame frame, List<RequireOutcome> results)
            {
                bool failed = false;
                foreach (var stmt in with.Body)
                {
                    try
                    {
                        if (stmt is AssignStmt)
                        {
                            Exec(stmt, frame);
                            continue;
                        }
                        var outcome = Require((CallExpr)((ExprStmt)stmt).Expr, frame);
                        if (outcome.Status == RequireStatus.Failed) failed = true;
                        results.Add(outcome);
                    }
                    catch (ScriptRuntimeException e) when (failed && e.Code != Codes.RunBudget && e.Code != Codes.RunCycle)
                    {
                        if (stmt is AssignStmt assign)
                        {
                            frame.Locals.Remove(((NameExpr)assign.Target).Name);
                            continue;
                        }
                        var call = (CallExpr)((ExprStmt)stmt).Expr;
                        results.Add(new RequireOutcome(RequireStatus.NotEvaluated, LiteralKey(call), null, call.Span));
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

            /// <summary>
            /// 평가하지 않은 메시지의 키: <c>text("키", ...)</c>로 적혀 있고 그 <c>text</c>가 내장일 때만 (모드가 덮어썼으면 모른다).
            /// </summary>
            private string? LiteralKey(CallExpr call)
            {
                if (call.Arguments.Count < 2 || !(call.Arguments[1].Value is CallExpr text) ||
                    !(text.Callee is NameExpr name) || name.Name != PureBuiltins.TextName ||
                    _owner._globals.Lookup(_stack[_stack.Count - 1].Function.Mod, PureBuiltins.TextName) != null ||
                    _stack[_stack.Count - 1].LocalNames.Contains(PureBuiltins.TextName))
                    return null;
                return text.Arguments.Count > 0 && text.Arguments[0].Name == null && text.Arguments[0].Value is StringLiteral s
                    ? s.Value
                    : null;
            }

            /// <summary>재귀를 검사하고 인자를 매개변수에 묶은 새 프레임을 만든다 (스택에는 아직 넣지 않는다).</summary>
            private Frame Enter(FunctionValue fn, IReadOnlyDictionary<string, Value>? captured,
                SortedSet<string>? capturedLocals, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
            {
                foreach (var frame in _stack)
                {
                    if (!SameFunction(frame.Function, fn)) continue;
                    var chain = new List<string>();
                    bool inChain = false;
                    foreach (var f in _stack)
                    {
                        if (SameFunction(f.Function, fn)) inChain = true;
                        if (inChain) chain.Add(f.Function.Name);
                    }
                    chain.Add(fn.Name);
                    throw Fail(Codes.RunCycle, "function '" + fn.Name + "' calls itself: " + string.Join(" -> ", chain),
                        "recursion is not allowed in logic functions");
                }

                var parameters = fn.Parameters;
                if (positional.Count > parameters.Count)
                {
                    throw Fail(Codes.RunScript, fn.Name + "() takes " + parameters.Count + " argument" +
                                                (parameters.Count == 1 ? "" : "s") + ", got " + positional.Count);
                }

                var locals = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                if (captured != null)
                    foreach (var pair in captured) locals[pair.Key] = pair.Value;

                var bound = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < positional.Count; i++)
                {
                    locals[parameters[i].Name] = positional[i];
                    bound.Add(parameters[i].Name);
                }
                foreach (var pair in keywords)
                {
                    bool known = false;
                    foreach (var p in parameters) known |= p.Name == pair.Key;
                    if (!known) throw Fail(Codes.RunScript, fn.Name + "() has no parameter '" + pair.Key + "'");
                    if (!bound.Add(pair.Key))
                        throw Fail(Codes.RunScript, fn.Name + "() got several values for '" + pair.Key + "'");
                    locals[pair.Key] = pair.Value;
                }
                foreach (var p in parameters)
                {
                    if (!bound.Contains(p.Name))
                        throw Fail(Codes.RunScript, fn.Name + "() is missing the argument '" + p.Name + "'");
                }

                SortedSet<string> localNames;
                if (fn.Definition != null)
                {
                    localNames = _owner.LocalNamesOf(fn.Definition);
                }
                else
                {
                    // 람다는 대입할 수 없으니 지역 변수는 매개변수와, 만든 함수의 지역 변수다
                    localNames = FunctionLocals.Parameters(fn.Parameters);
                    if (capturedLocals != null) localNames.UnionWith(capturedLocals);
                }

                return new Frame(fn, locals, localNames);
            }

            // ---- 문장 ----

            private Flow ExecBlock(IReadOnlyList<Stmt> body, Frame frame)
            {
                foreach (var stmt in body)
                {
                    var flow = Exec(stmt, frame);
                    if (flow != Flow.Normal) return flow;
                }
                return Flow.Normal;
            }

            private Flow Exec(Stmt stmt, Frame frame)
            {
                try
                {
                    Tick();
                    return ExecCore(stmt, frame);
                }
                catch (ScriptRuntimeException e)
                {
                    Attach(e, stmt.Span);
                    throw;
                }
            }

            private Flow ExecCore(Stmt stmt, Frame frame)
            {
                switch (stmt)
                {
                    case ExprStmt es:
                        if (es.Expr is CallExpr statementCall) EvalCall(statementCall, frame, statement: true);
                        else Eval(es.Expr, frame);
                        return Flow.Normal;

                    case AssignStmt assign:
                        ExecAssign(assign, frame);
                        return Flow.Normal;

                    case IfStmt ifs:
                        foreach (var branch in ifs.Branches)
                        {
                            if (PureOps.Truthy(Eval(branch.Condition, frame)))
                                return ExecBlock(branch.Body, frame);
                        }
                        return ifs.OrElse != null ? ExecBlock(ifs.OrElse, frame) : Flow.Normal;

                    case ForStmt loop:
                        return ExecFor(loop, frame);

                    case ReturnStmt ret:
                        _returned = ret.Value == null ? NoneValue.Instance : Eval(ret.Value, frame);
                        return Flow.Return;

                    case PassStmt _: return Flow.Normal;
                    case BreakStmt _: return Flow.Break;
                    case ContinueStmt _: return Flow.Continue;
                }
                throw Fail(Codes.RunScript, "this statement is not supported in a logic function");
            }

            private void ExecAssign(AssignStmt assign, Frame frame)
            {
                if (assign.Target is AttributeExpr attr)
                {
                    if (frame.Writable)
                    {
                        ExecWrite(assign, attr, frame);
                        return;
                    }
                    // 커맨드 실행 중인데 이 프레임이 순수 모드다: 이름으로 판정할 수 없던 길로 들어왔다 (G117 런타임 안전망)
                    if (_stack.Exists(f => f.Writable))
                    {
                        throw Fail(Codes.RunNotWritable,
                            "'" + frame.Function.Name + "' cannot change the world here: it was reached through a function " +
                            "that only reads, or called inside an expression",
                            "only a @command body, and functions that change the world called on their own line from it, " +
                            "can write (spec-gaps G117)");
                    }
                }
                if (!(assign.Target is NameExpr target))
                    throw Fail(Codes.RunScript, "a logic function can only assign to local variables");

                if (assign.Op == "=")
                {
                    frame.Locals[target.Name] = Eval(assign.Value, frame);
                    return;
                }

                if (!frame.Locals.TryGetValue(target.Name, out var current))
                {
                    throw Fail(Codes.RunScript, "local variable '" + target.Name + "' is used before it is assigned");
                }
                // 파이썬의 리스트 +=는 그 자리에서 고쳐 별칭에도 보인다. 순수 함수의 값은 불변이므로 막는다 (G104)
                if (assign.Op == "+=" && current is ListValue)
                {
                    throw Fail(Codes.RunScript, "'+=' cannot be used on a list in a logic function",
                        "write " + target.Name + " = " + target.Name + " + [...]: values in logic functions never change in place");
                }
                var right = Eval(assign.Value, frame);
                string op = assign.Op.Substring(0, assign.Op.Length - 1);
                frame.Locals[target.Name] = Binary(op, current, right);
            }

            /// <summary>
            /// 커맨드 본문의 상태 쓰기 <c>u.Moves.left = 값</c> (G117·G132). 파이썬처럼 대상 객체(<c>u.Moves</c>)는 한 번만 평가한다.
            /// </summary>
            private void ExecWrite(AssignStmt assign, AttributeExpr attr, Frame frame)
            {
                var owner = Eval(attr.Target, frame);
                if (!(owner is IRuntimeAssignable target))
                {
                    throw Fail(Codes.RunScript, owner is NoneValue
                            ? "cannot set '" + attr.Name + "' on None"
                            : "cannot set '" + attr.Name + "' on " + owner.KindName,
                        owner is NoneValue
                            ? "the component or row is missing; check it with 'is not None' first"
                            : "only members of a component or of a row can be written (spec-gaps G117)");
                }

                Value value;
                if (assign.Op == "=")
                {
                    value = Eval(assign.Value, frame);
                }
                else
                {
                    // 상태 칸에는 리스트·맵이 없으므로(SCH004, G129) 리스트 +=의 제자리 수정 문제(G104)는 생기지 않는다
                    var current = ((IRuntimeAttributes)owner).GetAttribute(attr.Name);
                    var right = Eval(assign.Value, frame);
                    value = Binary(assign.Op.Substring(0, assign.Op.Length - 1), current, right);
                }
                target.SetAttribute(attr.Name, value);
            }

            private Flow ExecFor(ForStmt loop, Frame frame)
            {
                var iterable = Eval(loop.Iterable, frame);
                long count;
                Func<long, Value> at;
                switch (iterable)
                {
                    case ListValue list:
                        count = list.Items.Count;
                        at = i => list.Items[(int)i];
                        break;
                    case DictValue dict:
                        count = dict.Items.Count;
                        at = i => dict.Items[(int)i].Key;
                        break;
                    case RangeValue range:
                        count = range.Count;
                        at = i => new IntValue(range.At(i));
                        break;
                    case IRuntimeIterable iterable2:
                        var items = iterable2.Iterate();
                        count = items.Count;
                        at = i => items[(int)i];
                        break;
                    default:
                        throw Fail(Codes.RunScript, "'" + iterable.KindName + "' is not iterable",
                            "loop over a list, a dict or range(...)");
                }

                for (long i = 0; i < count; i++)
                {
                    Tick();
                    frame.Locals[loop.Target.Name] = at(i);
                    var flow = ExecBlock(loop.Body, frame);
                    if (flow == Flow.Break) break;
                    if (flow == Flow.Return) return Flow.Return;
                }
                return Flow.Normal;
            }

            // ---- 식 ----

            private Value Eval(Expr expr, Frame frame)
            {
                try
                {
                    Tick();
                    return EvalCore(expr, frame);
                }
                catch (ScriptRuntimeException e)
                {
                    Attach(e, expr.Span);
                    throw;
                }
            }

            private Value EvalCore(Expr expr, Frame frame)
            {
                switch (expr)
                {
                    case IntLiteral i: return new IntValue(i.Value);
                    case FixedLiteral f: return new FixedValue(f.Value);
                    case StringLiteral s: return new StrValue(s.Value);
                    case BoolLiteral b: return b.Value ? BoolValue.True : BoolValue.False;
                    case NoneLiteral _: return NoneValue.Instance;
                    case NameExpr name: return LookupName(name.Name, frame);

                    case ListExpr list:
                        var items = new List<Value>(list.Items.Count);
                        foreach (var item in list.Items) items.Add(Eval(item, frame));
                        return new ListValue(items);

                    case DictExpr dict: return EvalDict(dict, frame);

                    case AttributeExpr attr:
                        var owner = Eval(attr.Target, frame);
                        if (owner is IRuntimeAttributes source) return source.GetAttribute(attr.Name);
                        throw Fail(Codes.RunScript, "'" + owner.KindName + "' has no attribute '" + attr.Name + "'");

                    case IndexExpr index:
                        if (index.Indices.Count != 1) throw Fail(Codes.RunScript, "an index takes one value");
                        return Index(Eval(index.Target, frame), Eval(index.Index, frame));

                    case CallExpr call: return EvalCall(call, frame);

                    case UnaryExpr unary: return EvalUnary(unary, frame);
                    case BinaryExpr bin: return EvalBinary(bin, frame);

                    case ConditionalExpr cond:
                        return PureOps.Truthy(Eval(cond.Condition, frame))
                            ? Eval(cond.WhenTrue, frame)
                            : Eval(cond.WhenFalse, frame);

                    case LambdaExpr lambda:
                        // 만든 함수의 지역 변수 자체를 들고 있다가 부를 때 읽는다: 파이썬처럼 늦게 묶인다
                        return new ClosureValue(new FunctionValue(lambda, frame.Function.Mod), frame.Locals, frame.LocalNames);
                }
                throw Fail(Codes.RunScript, "this expression is not supported in a logic function");
            }

            private Value LookupName(string name, Frame frame)
            {
                if (frame.Locals.TryGetValue(name, out var local)) return local;
                // 함수 안 어딘가에서 대입하는 이름은 값이 없어도 지역 변수다: 같은 이름의 전역·내장으로 넘어가지 않는다 (G104)
                if (frame.LocalNames.Contains(name))
                    throw Fail(Codes.RunScript, "local variable '" + name + "' is used before it is assigned",
                        "it is assigned somewhere in this function, so it is local everywhere in it; " +
                        "assign it on every path first, or use a different name");
                var global = _owner._globals.Lookup(frame.Function.Mod, name);
                if (global != null) return global;
                if (_owner._builtins.TryGetValue(name, out var builtin)) return builtin;

                var candidates = new List<string>(frame.Locals.Keys);
                foreach (var key in _owner._builtins.Keys) candidates.Add(key);
                throw Fail(Codes.RunScript, "name '" + name + "' is not defined", Suggest.Hint(name, candidates));
            }

            private Value EvalDict(DictExpr dict, Frame frame)
            {
                var items = new List<DictItem>(dict.Entries.Count);
                var result = new DictValue(items);
                foreach (var entry in dict.Entries)
                {
                    var key = Eval(entry.Key, frame);
                    if (!(key is StrValue) && !(key is IntValue))
                        throw Fail(Codes.RunScript, "dict keys must be str or int, not " + key.KindName);
                    if (PureOps.FindKey(result, key) != null) throw Fail(Codes.RunScript, "duplicate dict key");
                    items.Add(new DictItem(key, Eval(entry.Value, frame), entry.Key.Span));
                }
                return result;
            }

            private Value Index(Value target, Value key)
            {
                switch (target)
                {
                    case ListValue list:
                        if (!(key is IntValue n)) throw Fail(Codes.RunScript, "list indices must be int, not " + key.KindName);
                        long position = n.Value < 0 ? n.Value + list.Items.Count : n.Value;
                        if (position < 0 || position >= list.Items.Count)
                            throw Fail(Codes.RunScript, "list index " + n.Value + " is out of range (length " + list.Items.Count + ")");
                        return list.Items[(int)position];

                    case DictValue dict:
                        var found = PureOps.FindKey(dict, key);
                        if (found == null) throw Fail(Codes.RunScript, "the dict has no such key");
                        return found;

                    case IRuntimeIndexable indexable:
                        return indexable.GetItem(key);
                }
                throw Fail(Codes.RunScript, "'" + target.KindName + "' cannot be indexed");
            }

            /// <param name="statement">이 호출이 문장 하나 전체인가 (쓰기 함수는 문장으로만 부른다, G117).</param>
            private Value EvalCall(CallExpr call, Frame frame, bool statement = false)
            {
                var callee = Eval(call.Callee, frame);
                var positional = new List<Value>();
                var keywords = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                foreach (var arg in call.Arguments)
                {
                    var value = Eval(arg.Value, frame);
                    if (arg.Name == null) positional.Add(value);
                    else keywords[arg.Name] = value;
                }
                return Invoke(callee, positional, keywords, call.Span, statement && frame.Writable);
            }

            private Value EvalUnary(UnaryExpr unary, Frame frame)
            {
                var operand = Eval(unary.Operand, frame);
                switch (unary.Op)
                {
                    case "-": return PureOps.Negate(operand);
                    case "+": return PureOps.Plus(operand);
                    case "not": return PureOps.Truthy(operand) ? BoolValue.False : BoolValue.True;
                }
                throw Fail(Codes.RunScript, "operator '" + unary.Op + "' is not supported in a logic function");
            }

            private Value EvalBinary(BinaryExpr bin, Frame frame)
            {
                // and / or는 파이썬처럼 결정한 쪽의 피연산자 값을 그대로 돌려주고, 오른쪽은 필요할 때만 평가한다 (G104).
                if (bin.Op == "and")
                {
                    var left = Eval(bin.Left, frame);
                    return PureOps.Truthy(left) ? Eval(bin.Right, frame) : left;
                }
                if (bin.Op == "or")
                {
                    var left = Eval(bin.Left, frame);
                    return PureOps.Truthy(left) ? left : Eval(bin.Right, frame);
                }
                return Binary(bin.Op, Eval(bin.Left, frame), Eval(bin.Right, frame));
            }

            private Value Binary(string op, Value left, Value right)
            {
                switch (op)
                {
                    case "==": return PureOps.ValuesEqual(left, right) ? BoolValue.True : BoolValue.False;
                    case "!=": return PureOps.ValuesEqual(left, right) ? BoolValue.False : BoolValue.True;
                    case "<":
                    case "<=":
                    case ">":
                    case ">=":
                        return PureOps.Ordering(op, left, right);
                    case "in": return PureOps.Contains(right, left) ? BoolValue.True : BoolValue.False;
                    case "not in": return PureOps.Contains(right, left) ? BoolValue.False : BoolValue.True;
                    case "is": return right is NoneValue && left is NoneValue ? BoolValue.True : BoolValue.False;
                    case "is not": return right is NoneValue && left is NoneValue ? BoolValue.False : BoolValue.True;
                    case "+":
                    case "-":
                    case "*":
                    case "/":
                    case "//":
                    case "%":
                        return PureOps.Arithmetic(op, left, right);
                }
                throw Fail(Codes.RunScript, "operator '" + op + "' is not supported in a logic function",
                    "see spec-gaps G96");
            }
        }
    }
}
