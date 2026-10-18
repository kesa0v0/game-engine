using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 로드 시점 평가기 (spec S2의 부분집합 중 선언에 필요한 것만).
    /// 최상위 문장: 호출, <c>name = 식</c>, <c>def</c>. 식: 리터럴, 리스트, 맵, 이름, 호출, 타입 표기의 <c>[...]</c>, 수 앞의 부호,
    /// 람다(값으로만 묶고 본문은 평가하지 않음).
    /// 그 밖의 식은 로직 단계에서 지원하며 지금은 <see cref="Codes.EvlUnsupported"/>.
    /// 오류가 난 문장은 건너뛰고 다음 문장을 계속 실행해 오류를 모은다 (spec S1 오류 정책).
    /// </summary>
    public sealed partial class Interpreter
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
                    _context.Functions.TopLevelNames.Add(target.Name);
                    var value = Evaluate(assign.Value);
                    Bind(target.Name, value, target.Span);
                    return;

                case FunctionDef def:
                    FunctionChecker.Check(def, _context); // 쓸 수 없는 연산자·재귀는 정의할 때 알린다 (G96·G102)
                    var function = new FunctionValue(def, _context.Mod);
                    _context.Functions.Functions.Add(function); // 이름은 모드 평가가 끝난 뒤에 본다 (G108)
                    _context.Functions.TopLevelNames.Add(def.Name);
                    // 데코레이터가 잘못돼도 함수는 묶는다: 원인 오류 하나가 "정의되지 않은 이름"으로 번지지 않게.
                    // 커맨드는 이름이 묶인 뒤에 등록해 같은 id가 둘 생기지 않게 한다
                    Bind(def.Name, function, def.Span);
                    if (def.Decorators.Count > 0) DeclareCommand(def, function);
                    return;

                case UnsupportedStmt _:
                    return; // 파서가 이미 보고함

                case WithStmt with:
                    throw _context.Fail(Codes.CmdPreconditions,
                        "'with' can only be the first statement of a @command function", with.Span);
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
        private string? GuardModOf(Expr condition)
        {
            if (condition is CallExpr call && call.Callee is NameExpr name && name.Name == ModLoadedName &&
                IsBuiltin(ModLoadedName) &&
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

        private int CountPatchCalls(IReadOnlyList<Stmt> body)
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

        private bool IsGuardCondition(Expr condition) =>
            condition is CallExpr call && call.Callee is NameExpr name && name.Name == ModLoadedName &&
            IsBuiltin(ModLoadedName);

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

        private bool IsPatchCall(Expr expr) =>
            expr is CallExpr call && call.Callee is AttributeExpr attr && attr.Target is NameExpr target &&
            target.Name == PatchName && IsBuiltin(PatchName);

        /// <summary>
        /// 이 이름이 아직 내장의 뜻인가. 엔진이 주는 이름은 덮어쓸 수 있는 내장이라 (spec-gaps G112), 모드가 그 이름을
        /// 묶은 뒤로는 그 모드 안에서 모드의 값이다 (파이썬의 builtins와 같다).
        /// </summary>
        private bool IsBuiltin(string name) => !_scope.TryGet(name, out _, out _);

        /// <summary>식 안의 <c>inherit</c>가 내장의 뜻인가 (모드가 <c>inherit</c>를 묶었으면 평범한 이름이다, G112).</summary>
        private bool UsesInherit(Expr expr) => IsBuiltin(InheritName) && ContainsInherit(expr);

        /// <summary>
        /// 최상위 이름을 묶는다. 엔진이 주는 이름(<c>field</c>, <c>patch</c>, <c>list</c>, Def 타입 이름 등)도 묶을 수 있고
        /// 그 뒤로 이 모드 안에서는 모드의 값이다 (spec-gaps G112). 같은 모드 안에서 두 번 묶는 것만 오류다 (G20).
        /// </summary>
        private void Bind(string name, Value value, SourceSpan span)
        {
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
                case LambdaExpr lambda:
                    FunctionChecker.Check(lambda, _context);
                    var lambdaValue = new FunctionValue(lambda, _context.Mod);
                    _context.Functions.Functions.Add(lambdaValue);
                    return lambdaValue;
                case UnsupportedExpr _: throw new ScriptError(); // 파서가 이미 보고함
                case ComprehensionExpr comp:
                    // 최상위의 for·조건식과 같다 (G102·G189)
                    throw _context.Fail(Codes.EvlUnsupported, "comprehensions can only be used inside a function", comp.Span,
                        "content files list their values directly; compute values in a def or lambda (spec-gaps G189)");
                case AttributeExpr attr:
                    if (UsesInherit(attr))
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
                Value value = callable.AcceptsDeferred && UsesInherit(arg.Value)
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
