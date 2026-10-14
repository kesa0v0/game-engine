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
    /// 호스트가 읽기 도중 다시 <see cref="Run"/>을 불러도 된다: 호출마다 단계와 호출 체인이 새로 시작한다.
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

        /// <summary>함수 값(<c>def</c>나 람다)을 위치 인자로 부른다.</summary>
        public Value Run(Value function, IReadOnlyList<Value> args, SourceSpan callSite = default)
        {
            var session = new Session(this);
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

        private enum Flow
        {
            Normal,
            Break,
            Continue,
            Return,
        }

        private sealed class Frame
        {
            public Frame(FunctionValue function, SortedDictionary<string, Value> locals, SourceSpan callSite)
            {
                Function = function;
                Locals = locals;
                CallSite = callSite;
            }

            public FunctionValue Function { get; }
            public SortedDictionary<string, Value> Locals { get; }
            public SourceSpan CallSite { get; }
        }

        private sealed class Session
        {
            private readonly PureEvaluator _owner;
            private readonly List<Frame> _stack = new List<Frame>();
            private int _steps;
            private Value _returned = NoneValue.Instance;

            public Session(PureEvaluator owner) => _owner = owner;

            // ---- 오류 ----

            private static bool HasSpan(ScriptRuntimeException e) => e.Span.Line > 0;

            private ScriptRuntimeException Fail(string code, string message, string? hint = null) =>
                new ScriptRuntimeException(code, message, hint);

            private void Attach(ScriptRuntimeException e, SourceSpan span)
            {
                e.Span = span;
                var trace = new List<string>();
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    var frame = _stack[i];
                    trace.Add(frame.CallSite.Line > 0
                        ? frame.Function.Name + " (called at " + frame.CallSite + ")"
                        : frame.Function.Name);
                }
                e.Trace = trace;
            }

            private void Tick()
            {
                if (++_steps > _owner._budget)
                {
                    throw Fail(Codes.RunBudget, "the function used more than " + _owner._budget + " evaluation steps",
                        "a loop may be too large or endless");
                }
            }

            // ---- 호출 ----

            public Value Invoke(Value function, IReadOnlyList<Value> positional,
                IReadOnlyDictionary<string, Value> keywords, SourceSpan site)
            {
                switch (function)
                {
                    case FunctionValue f: return CallFunction(f, null, positional, keywords, site);
                    case ClosureValue c: return CallFunction(c.Function, c.Captured, positional, keywords, site);
                    case PureBuiltin b:
                        if (keywords.Count > 0) throw Fail(Codes.RunScript, b.Name + "() does not take keyword arguments");
                        return b.Body(positional);
                }
                throw Fail(Codes.RunScript, "'" + function.KindName + "' is not callable");
            }

            private static bool SameFunction(FunctionValue a, FunctionValue b) =>
                ReferenceEquals(a.Definition, b.Definition) && ReferenceEquals(a.Lambda, b.Lambda);

            private Value CallFunction(FunctionValue fn, IReadOnlyDictionary<string, Value>? captured,
                IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords, SourceSpan site)
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

                var newFrame = new Frame(fn, locals, site);
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
                catch (ScriptRuntimeException e) when (!HasSpan(e))
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
                        Eval(es.Expr, frame);
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
                var right = Eval(assign.Value, frame);
                string op = assign.Op.Substring(0, assign.Op.Length - 1);
                frame.Locals[target.Name] = Binary(op, current, right);
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
                catch (ScriptRuntimeException e) when (!HasSpan(e))
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
                        return new ClosureValue(new FunctionValue(lambda, frame.Function.Mod), frame.Locals);
                }
                throw Fail(Codes.RunScript, "this expression is not supported in a logic function");
            }

            private Value LookupName(string name, Frame frame)
            {
                if (frame.Locals.TryGetValue(name, out var local)) return local;
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

            private Value EvalCall(CallExpr call, Frame frame)
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
                return Invoke(callee, positional, keywords, call.Span);
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
