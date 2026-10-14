using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>진단을 이미 보고하고 현재 문장을 중단할 때 던진다.</summary>
    public sealed class ScriptError : Exception
    {
    }

    /// <summary><c>if mod_loaded("m")</c>가 거짓이라 실행하지 않은 가지 안의 <c>patch.*</c> 호출 수 (모드 m 하나당).</summary>
    public sealed class SkippedGuard
    {
        public SkippedGuard(SourceSpan first) => First = first;

        public int Count { get; internal set; }

        /// <summary>처음 건너뛴 가지의 <c>if</c>/<c>elif</c> 위치.</summary>
        public SourceSpan First { get; }
    }

    /// <summary>내장 함수가 진단을 내는 통로. 진단에는 항상 실행 중인 모드가 붙는다.</summary>
    public sealed class CallContext
    {
        public CallContext(string mod, DiagnosticBag diagnostics, IDictionary<string, SkippedGuard>? skipped = null)
        {
            Mod = mod;
            Diagnostics = diagnostics;
            Skipped = skipped ?? new SortedDictionary<string, SkippedGuard>(StringComparer.Ordinal);
        }

        public string Mod { get; }
        public DiagnosticBag Diagnostics { get; }

        /// <summary>
        /// 지금 실행 중인 가지가 참이라고 가정하는 모드들 (바깥 가드부터, spec-gaps G46).
        /// <c>patch.*</c> 호출이 기록할 때 함께 저장한다.
        /// </summary>
        public List<string> Guards { get; } = new List<string>();

        /// <summary>실행하지 않은 가드 가지의 패치 호출 수 (거짓인 모드 id별).</summary>
        public IDictionary<string, SkippedGuard> Skipped { get; }

        public void Error(string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            Diagnostics.Error(code, message, span, Mod, hint, related);

        /// <summary>오류를 보고하고 현재 문장을 중단한다.</summary>
        public ScriptError Fail(string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null)
        {
            Error(code, message, span, hint, related);
            return new ScriptError();
        }
    }

    /// <summary>내장 이름의 출처. 단계(스키마 파일 / Def 파일)마다 다르다.</summary>
    public interface IBuiltins
    {
        Value? Lookup(string name);

        /// <summary>"혹시 …?" 후보.</summary>
        IEnumerable<string> Names { get; }

        /// <summary>
        /// 이 단계에서 쓸 수 없지만 다른 단계에서는 뜻이 있는 이름이면 그 이유 (예: 스키마 파일에서 Def 생성자).
        /// 진단 코드와 메시지를 돌려준다.
        /// </summary>
        (string Code, string Message)? Explain(string name);
    }

    /// <summary>
    /// 모드 하나의 최상위 이름들. 한 모드의 파일들은 실행 순서대로 같은 이름 공간을 쓰고,
    /// 이름은 한 번만 묶을 수 있다 (spec-gaps G20).
    /// </summary>
    public sealed class ModScope
    {
        private readonly Dictionary<string, (Value Value, SourceSpan Span)> _bindings =
            new Dictionary<string, (Value, SourceSpan)>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public bool TryGet(string name, out Value value, out SourceSpan span)
        {
            if (_bindings.TryGetValue(name, out var b))
            {
                value = b.Value;
                span = b.Span;
                return true;
            }
            value = NoneValue.Instance;
            span = SourceSpan.None;
            return false;
        }

        public void Bind(string name, Value value, SourceSpan span)
        {
            _bindings.Add(name, (value, span));
            _order.Add(name);
        }

        /// <summary>묶인 순서대로의 이름.</summary>
        public IReadOnlyList<string> Names => _order;
    }

    /// <summary>
    /// 로드 시점 평가기 (spec S2의 부분집합 중 선언에 필요한 것만).
    /// 최상위 문장: 호출, <c>name = 식</c>, <c>def</c>. 식: 리터럴, 리스트, 맵, 이름, 호출, 타입 표기의 <c>[...]</c>, 수 앞의 부호,
    /// 람다(값으로만 묶고 본문은 평가하지 않음).
    /// 그 밖의 식은 로직 단계에서 지원하며 지금은 <see cref="Codes.EvlUnsupported"/>.
    /// 오류가 난 문장은 건너뛰고 다음 문장을 계속 실행해 오류를 모은다 (spec S1 오류 정책).
    /// </summary>
    public sealed class Interpreter
    {
        public const string InheritName = "inherit";
        public const string ModLoadedName = "mod_loaded";
        public const string PatchName = "patch";

        private readonly ModScope _scope;
        private readonly IBuiltins _builtins;
        private readonly CallContext _context;
        private readonly bool _allowConditions;

        /// <param name="allowConditions">false면 <c>if</c>를 거부한다 (스키마 파일, spec-gaps G46)</param>
        public Interpreter(ModScope scope, IBuiltins builtins, CallContext context, bool allowConditions = true)
        {
            _scope = scope;
            _builtins = builtins;
            _context = context;
            _allowConditions = allowConditions;
        }

        public void Run(Module module) => RunBlock(module.Body);

        private void RunBlock(IEnumerable<Stmt> body)
        {
            foreach (var stmt in body)
            {
                try
                {
                    Execute(stmt);
                }
                catch (ScriptError)
                {
                    // 진단은 이미 보고됨. 다음 문장으로.
                }
            }
        }

        private void Execute(Stmt stmt)
        {
            switch (stmt)
            {
                case IfStmt ifs:
                    ExecuteIf(ifs);
                    return;

                case ExprStmt es:
                    Evaluate(es.Expr);
                    return;

                case AssignStmt assign:
                    if (assign.Op != "=" || !(assign.Target is NameExpr target))
                    {
                        throw _context.Fail(Codes.EvlUnsupported,
                            "only 'name = value' assignments are allowed at the top level of a content file", assign.Span);
                    }
                    var value = Evaluate(assign.Value);
                    Bind(target.Name, value, target.Span);
                    return;

                case FunctionDef def:
                    Bind(def.Name, new FunctionValue(def, _context.Mod), def.Span);
                    return;

                case UnsupportedStmt _:
                    return; // 파서가 이미 보고함
            }
            throw _context.Fail(Codes.EvlUnsupported, "statement not supported at load time", stmt.Span);
        }

        /// <summary>
        /// <c>if</c>/<c>elif</c>/<c>else</c> (spec-gaps G46). 조건은 bool을 돌려주는 호출 하나다.
        /// <c>mod_loaded</c>를 조건으로 쓰면 가드이고, 가드의 모든 가지(실행되지 않는 가지도)는 <c>patch.*</c> 호출과
        /// 중첩된 <c>if</c>만 담을 수 있다: 설치 상태에 따라 Def나 이름이 있기도 없기도 하면 안 되기 때문이다.
        /// 이 검사는 어느 가지가 실행되는지와 무관하게 하므로 <c>check</c>의 결과가 설치 상태와 무관하다.
        /// </summary>
        private void ExecuteIf(IfStmt ifs)
        {
            if (!_allowConditions)
                throw _context.Fail(Codes.EvlUnsupported, "if statements are not supported in schema files", ifs.Span);
            bool guarded = false;
            foreach (var branch in ifs.Branches)
                if (IsGuardCondition(branch.Condition)) guarded = true;
            if (guarded && !CheckGuardBodies(ifs)) throw new ScriptError();

            foreach (var branch in ifs.Branches)
            {
                string? guardMod = GuardModOf(branch.Condition);
                if (!EvaluateCondition(branch.Condition))
                {
                    // 거짓이라고 평가된 가드만 센다 (앞 가지가 참이어서 평가하지 않은 가지는 모른다)
                    if (guardMod != null) CountSkipped(guardMod, branch);
                    continue;
                }

                if (guardMod != null) _context.Guards.Add(guardMod);
                try
                {
                    RunBlock(branch.Body);
                }
                finally
                {
                    if (guardMod != null) _context.Guards.RemoveAt(_context.Guards.Count - 1);
                }
                return;
            }
            if (ifs.OrElse != null) RunBlock(ifs.OrElse);
        }

        /// <summary><c>mod_loaded("m")</c> 조건이면 m (인자가 문자열 리터럴일 때만).</summary>
        private static string? GuardModOf(Expr condition)
        {
            if (condition is CallExpr call && call.Callee is NameExpr name && name.Name == ModLoadedName &&
                call.Arguments.Count == 1 && call.Arguments[0].Name == null &&
                call.Arguments[0].Value is StringLiteral literal)
                return literal.Value;
            return null;
        }

        private void CountSkipped(string mod, IfBranch branch)
        {
            int n = CountPatchCalls(branch.Body);
            if (n == 0) return;
            if (!_context.Skipped.TryGetValue(mod, out var skipped))
            {
                skipped = new SkippedGuard(branch.Span);
                _context.Skipped.Add(mod, skipped);
            }
            skipped.Count += n;
        }

        private static int CountPatchCalls(IReadOnlyList<Stmt> body)
        {
            int n = 0;
            foreach (var stmt in body)
            {
                if (stmt is ExprStmt es && IsPatchCall(es.Expr)) n++;
                else if (stmt is IfStmt nested)
                {
                    foreach (var branch in nested.Branches) n += CountPatchCalls(branch.Body);
                    if (nested.OrElse != null) n += CountPatchCalls(nested.OrElse);
                }
            }
            return n;
        }

        private bool EvaluateCondition(Expr condition)
        {
            if (!(condition is CallExpr))
            {
                throw _context.Fail(Codes.EvlBadCondition,
                    "a load-time condition must be a call that returns a bool", condition.Span,
                    hint: "write mod_loaded(\"other_mod\")");
            }
            var value = Evaluate(condition);
            if (value is BoolValue b) return b.Value;
            throw _context.Fail(Codes.EvlBadCondition, "a condition must be a bool, not " + value.KindName, condition.Span);
        }

        private static bool IsGuardCondition(Expr condition) =>
            condition is CallExpr call && call.Callee is NameExpr name && name.Name == ModLoadedName;

        private bool CheckGuardBodies(IfStmt ifs)
        {
            bool ok = true;
            foreach (var branch in ifs.Branches) ok &= CheckGuardBody(branch.Body);
            if (ifs.OrElse != null) ok &= CheckGuardBody(ifs.OrElse);
            return ok;
        }

        private bool CheckGuardBody(IReadOnlyList<Stmt> body)
        {
            bool ok = true;
            foreach (var stmt in body)
            {
                switch (stmt)
                {
                    case UnsupportedStmt _:
                        break; // 파서가 이미 보고함
                    case IfStmt nested:
                        foreach (var branch in nested.Branches) ok &= CheckGuardBody(branch.Body);
                        if (nested.OrElse != null) ok &= CheckGuardBody(nested.OrElse);
                        break;
                    case ExprStmt es when IsPatchCall(es.Expr):
                        break;
                    default:
                        _context.Error(Codes.EvlGuardBody,
                            "inside an 'if " + ModLoadedName + "(...)' block only patch.* calls and nested if statements are allowed",
                            stmt.Span,
                            hint: "move definitions and assignments outside the guard, so the mod's names do not depend on which mods are installed");
                        ok = false;
                        break;
                }
            }
            return ok;
        }

        private static bool IsPatchCall(Expr expr) =>
            expr is CallExpr call && call.Callee is AttributeExpr attr && attr.Target is NameExpr target &&
            target.Name == PatchName;

        private void Bind(string name, Value value, SourceSpan span)
        {
            if (name == InheritName || _builtins.Lookup(name) != null || _builtins.Explain(name) != null)
            {
                throw _context.Fail(Codes.EvlNameTaken, "'" + name + "' is a built-in name and cannot be redefined", span);
            }
            if (_scope.TryGet(name, out _, out var first))
            {
                throw _context.Fail(Codes.EvlNameTaken, "'" + name + "' is already defined in this mod", span,
                    related: new[] { first });
            }
            _scope.Bind(name, value, span);
        }

        public Value Evaluate(Expr expr)
        {
            switch (expr)
            {
                case IntLiteral i: return new IntValue(i.Value);
                case FixedLiteral f: return new FixedValue(f.Value);
                case StringLiteral s: return new StrValue(s.Value, s.Span);
                case BoolLiteral b: return b.Value ? BoolValue.True : BoolValue.False;
                case NoneLiteral _: return NoneValue.Instance;
                case ListExpr list: return EvaluateList(list);
                case DictExpr dict: return EvaluateDict(dict);
                case NameExpr name: return Lookup(name);
                case CallExpr call: return EvaluateCall(call);
                case IndexExpr index: return EvaluateIndex(index);
                case UnaryExpr unary: return EvaluateUnary(unary);
                case LambdaExpr lambda: return new FunctionValue(lambda, _context.Mod);
                case UnsupportedExpr _: throw new ScriptError(); // 파서가 이미 보고함
                case AttributeExpr attr:
                    if (ContainsInherit(attr))
                        throw _context.Fail(Codes.EvlUnsupported, "'inherit' can only be used in a Def field value", attr.Span);
                    var owner = Evaluate(attr.Target);
                    if (owner is IAttributeSource source) return source.GetAttribute(_context, attr.Span, attr.Name);
                    throw _context.Fail(Codes.EvlUnsupported, "attribute access is not supported at load time yet", attr.Span);
                case BinaryExpr bin:
                    throw _context.Fail(Codes.EvlUnsupported, "operator '" + bin.Op + "' is not supported at load time yet",
                        bin.Span);
            }
            throw _context.Fail(Codes.EvlUnsupported, "expression not supported at load time", expr.Span);
        }

        private Value EvaluateList(ListExpr list)
        {
            var items = new List<Value>(list.Items.Count);
            foreach (var item in list.Items) items.Add(Evaluate(item));
            return new ListValue(items);
        }

        private Value EvaluateDict(DictExpr dict)
        {
            var items = new List<DictItem>(dict.Entries.Count);
            var seenStr = new Dictionary<string, SourceSpan>(StringComparer.Ordinal);
            var seenInt = new Dictionary<long, SourceSpan>();
            foreach (var entry in dict.Entries)
            {
                var key = Evaluate(entry.Key);
                SourceSpan first;
                bool duplicate;
                switch (key)
                {
                    case StrValue s:
                        duplicate = seenStr.TryGetValue(s.Value, out first);
                        if (!duplicate) seenStr.Add(s.Value, entry.Key.Span);
                        break;
                    case IntValue n:
                        duplicate = seenInt.TryGetValue(n.Value, out first);
                        if (!duplicate) seenInt.Add(n.Value, entry.Key.Span);
                        break;
                    default:
                        throw _context.Fail(Codes.EvlType, "dict keys must be str or int, not " + key.KindName,
                            entry.Key.Span);
                }
                if (duplicate)
                {
                    throw _context.Fail(Codes.EvlType, "duplicate dict key", entry.Key.Span, related: new[] { first });
                }
                items.Add(new DictItem(key, Evaluate(entry.Value), entry.Key.Span));
            }
            return new DictValue(items);
        }

        private Value Lookup(NameExpr name)
        {
            if (_scope.TryGet(name.Name, out var value, out _)) return value;
            var builtin = _builtins.Lookup(name.Name);
            if (builtin != null) return builtin;

            if (name.Name == InheritName)
                throw _context.Fail(Codes.EvlUnsupported, "'inherit' can only be used in a Def field value", name.Span);

            var explained = _builtins.Explain(name.Name);
            if (explained != null) throw _context.Fail(explained.Value.Code, explained.Value.Message, name.Span);

            var candidates = new List<string>(_scope.Names);
            candidates.AddRange(_builtins.Names);
            throw _context.Fail(Codes.EvlUnknownName, "unknown name '" + name.Name + "'", name.Span,
                hint: Suggest.Hint(name.Name, candidates));
        }

        private Value EvaluateCall(CallExpr call)
        {
            var callee = Evaluate(call.Callee);
            if (!(callee is CallableValue callable))
            {
                throw _context.Fail(Codes.EvlType,
                    callee is FunctionValue
                        ? "functions cannot be called at load time"
                        : "a " + callee.KindName + " value is not callable", call.Span);
            }

            var args = new List<ArgValue>(call.Arguments.Count);
            foreach (var arg in call.Arguments)
            {
                Value value = callable.AcceptsDeferred && ContainsInherit(arg.Value)
                    ? new DeferredValue(arg.Value)
                    : Evaluate(arg.Value);
                args.Add(new ArgValue(arg.Name, value, arg.Name == null ? arg.Value.Span : arg.Span));
            }
            return callable.Call(_context, call.Span, args);
        }

        private Value EvaluateIndex(IndexExpr index)
        {
            var target = Evaluate(index.Target);
            if (target is ISubscriptable subscriptable)
            {
                var indices = new List<Value>(index.Indices.Count);
                foreach (var i in index.Indices) indices.Add(Evaluate(i));
                return subscriptable.Subscript(_context, index.Span, indices);
            }
            throw _context.Fail(Codes.EvlUnsupported, "indexing a " + target.KindName + " is not supported at load time yet",
                index.Span);
        }

        private Value EvaluateUnary(UnaryExpr unary)
        {
            var operand = Evaluate(unary.Operand);
            if (unary.Op == "-" || unary.Op == "+")
            {
                bool negate = unary.Op == "-";
                switch (operand)
                {
                    case IntValue i:
                        if (!negate) return i;
                        if (i.Value == long.MinValue)
                            throw _context.Fail(Codes.EvlType, "integer out of range", unary.Span);
                        return new IntValue(-i.Value);
                    case FixedValue f:
                        if (!negate) return f;
                        if (f.Value.Raw == long.MinValue)
                            throw _context.Fail(Codes.EvlType, "fixed value out of range", unary.Span);
                        return new FixedValue(Fixed.FromRaw(-f.Value.Raw));
                }
                throw _context.Fail(Codes.EvlType, "unary '" + unary.Op + "' needs a number, not " + operand.KindName,
                    unary.Span);
            }
            throw _context.Fail(Codes.EvlUnsupported, "operator '" + unary.Op + "' is not supported at load time yet",
                unary.Span);
        }

        /// <summary>식 안에 <c>inherit</c> 이름이 쓰였는가.</summary>
        public static bool ContainsInherit(Expr expr)
        {
            switch (expr)
            {
                case NameExpr n: return n.Name == InheritName;
                case AttributeExpr a: return ContainsInherit(a.Target);
                case IndexExpr i:
                    if (ContainsInherit(i.Target)) return true;
                    foreach (var x in i.Indices)
                        if (ContainsInherit(x)) return true;
                    return false;
                case CallExpr c:
                    if (ContainsInherit(c.Callee)) return true;
                    foreach (var a in c.Arguments)
                        if (ContainsInherit(a.Value)) return true;
                    return false;
                case UnaryExpr u: return ContainsInherit(u.Operand);
                case BinaryExpr b: return ContainsInherit(b.Left) || ContainsInherit(b.Right);
                case ConditionalExpr c:
                    return ContainsInherit(c.Condition) || ContainsInherit(c.WhenTrue) || ContainsInherit(c.WhenFalse);
                case ListExpr l:
                    foreach (var x in l.Items)
                        if (ContainsInherit(x)) return true;
                    return false;
                case DictExpr d:
                    foreach (var e in d.Entries)
                        if (ContainsInherit(e.Key) || ContainsInherit(e.Value)) return true;
                    return false;
            }
            return false;
        }
    }
}
