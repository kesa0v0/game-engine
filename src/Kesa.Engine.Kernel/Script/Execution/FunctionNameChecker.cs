using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 모드가 정의한 로직 함수와 최상위에서 묶으려 한 이름 (spec-gaps G108). 인터프리터가 모드의 파일을 실행하며 채우고,
    /// 모드 평가가 끝난 뒤 <see cref="FunctionNameChecker"/>가 본다.
    /// </summary>
    public sealed class ModFunctions
    {
        /// <summary>정의된 순서대로의 <c>def</c>와 최상위에서 만든 람다. 함수 안의 람다는 바깥 함수와 함께 본다.</summary>
        public List<FunctionValue> Functions { get; } = new List<FunctionValue>();

        /// <summary>
        /// 최상위 대입·<c>def</c>가 묶으려 한 이름. 그 문장이 오류로 실패해 이름 공간에 없어도 들어 있어,
        /// 원인 오류 하나가 함수 본문의 "정의되지 않은 이름" 오류로 번지지 않게 한다.
        /// </summary>
        public SortedSet<string> TopLevelNames { get; } = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary><c>@command</c>로 선언한 커맨드, 정의된 순서대로 (spec-gaps G109).</summary>
        public List<CommandDecl> Commands { get; } = new List<CommandDecl>();

        /// <summary>
        /// <c>@submit_policy</c>(G122)와 <c>@view_policy</c>(G197)로 선언한 정책 함수, 두 종류가 함께 든다(<see cref="PolicyDecl.Kind"/>).
        /// 모드 안에서 같은 종류가 둘 이상이면 로드 오류다.
        /// </summary>
        public List<PolicyDecl> Policies { get; } = new List<PolicyDecl>();

        /// <summary><c>@on(신호)</c>로 붙인 핸들러, 정의된 순서대로 (G145).</summary>
        public List<HandlerDecl> Handlers { get; } = new List<HandlerDecl>();

        /// <summary><c>@when(조건)</c> 반응, 정의된 순서대로 (G169).</summary>
        public List<WhenDecl> Whens { get; } = new List<WhenDecl>();

        /// <summary>
        /// 함수 본문에서 내장 <c>spawn("Def id", ...)</c>에 리터럴로 적은 Def id (G134). 이름 검사가 모으고 ⑤ 링크가 본다.
        /// </summary>
        public List<KeyValuePair<string, SourceSpan>> SpawnedDefs { get; } = new List<KeyValuePair<string, SourceSpan>>();

        /// <summary>
        /// 함수 본문의 <c>query(...)</c> 호출마다 리터럴로 적은 출처 이름 (G192). 이름 검사가 모으고 ⑤ 링크가 본다.
        /// </summary>
        public List<List<KeyValuePair<string, SourceSpan>>> QueriedNames { get; } = new List<List<KeyValuePair<string, SourceSpan>>>();
    }

    /// <summary>
    /// 함수 본문의 이름 검사 (spec-gaps G108). 본문의 이름은 실행 중에 지역 변수 → 같은 모드의 최상위 이름 →
    /// 런타임 내장(<see cref="PureBuiltins.RuntimeNames"/>) 순으로 찾으므로, 그 어디에도 없는 이름은 언젠가 반드시
    /// 실행 오류가 된다. 모드의 최상위 이름은 파일 뒤쪽이나 다른 파일에서 정의될 수 있어 모드 평가가 끝난 뒤에 본다.
    /// 지역 변수는 파이썬처럼 함수 안 어디서든 대입하거나 <c>for</c>로 묶으면 지역 변수이고, 대입 전에 읽는지는 보지 않는다.
    /// 람다는 바깥 함수의 지역 변수를 본다. 속성 이름(<c>h.Armor</c>)은 이름이 아니므로 보지 않는다.
    /// </summary>
    public static class FunctionNameChecker
    {
        /// <param name="isLoadTimeName">콘텐츠 파일에서만 뜻이 있는 이름인가 (<c>field</c>, <c>patch</c>, Def 생성자 등)</param>
        /// <param name="isSchemaRuntimeName">로직에서도 쓰는 스키마 이름인가 (이 모드에 보이는 컴포넌트의 값 생성자, G134)</param>
        /// <param name="notVisible">다른 모드의 것이라 보이지 않는 이름이면 그 안내 (SCH003)</param>
        public static void Check(ModFunctions functions, ModScope scope, Func<string, bool> isLoadTimeName,
            CallContext context, Func<string, bool>? isSchemaRuntimeName = null, Func<string, string?>? notVisible = null,
            IEnumerable<string>? schemaRuntimeNames = null, HostNames? hosts = null)
        {
            var globals = new SortedSet<string>(scope.Names, StringComparer.Ordinal);
            globals.UnionWith(functions.TopLevelNames);
            foreach (var function in functions.Functions)
            {
                var walker = new Walker(globals, isLoadTimeName, context, isSchemaRuntimeName ?? (_ => false),
                    notVisible ?? (_ => null), functions.SpawnedDefs, schemaRuntimeNames ?? Array.Empty<string>(),
                    functions.QueriedNames, hosts ?? HostNames.None);
                if (function.Definition != null)
                    walker.Block(function.Definition.Body, FunctionLocals.Of(function.Definition));
                else
                    walker.Expr(function.Lambda!.Body, FunctionLocals.Parameters(function.Parameters));
            }
        }

        private sealed class Walker
        {
            private readonly SortedSet<string> _globals;
            private readonly Func<string, bool> _isLoadTimeName;
            private readonly CallContext _context;

            private readonly Func<string, bool> _isSchemaRuntimeName;
            private readonly Func<string, string?> _notVisible;
            private readonly List<KeyValuePair<string, SourceSpan>> _spawned;
            private readonly List<List<KeyValuePair<string, SourceSpan>>> _queried;
            private readonly IEnumerable<string> _schemaRuntimeNames;
            private readonly HostNames _hosts;

            public Walker(SortedSet<string> globals, Func<string, bool> isLoadTimeName, CallContext context,
                Func<string, bool> isSchemaRuntimeName, Func<string, string?> notVisible,
                List<KeyValuePair<string, SourceSpan>> spawned, IEnumerable<string> schemaRuntimeNames,
                List<List<KeyValuePair<string, SourceSpan>>> queried, HostNames hosts)
            {
                _queried = queried;
                _hosts = hosts;
                _globals = globals;
                _isLoadTimeName = isLoadTimeName;
                _context = context;
                _isSchemaRuntimeName = isSchemaRuntimeName;
                _notVisible = notVisible;
                _spawned = spawned;
                _schemaRuntimeNames = schemaRuntimeNames;
            }

            public void Block(IReadOnlyList<Stmt> body, SortedSet<string> locals)
            {
                foreach (var stmt in body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es:
                            Expr(es.Expr, locals);
                            break;
                        case AssignStmt assign:
                            // 이름 대상은 지역 변수다. 속성 대상(상태 쓰기, G117)은 그 안의 이름을 본다
                            if (!(assign.Target is NameExpr)) Expr(assign.Target, locals);
                            Expr(assign.Value, locals);
                            break;
                        case IfStmt ifs:
                            foreach (var branch in ifs.Branches)
                            {
                                Expr(branch.Condition, locals);
                                Block(branch.Body, locals);
                            }
                            if (ifs.OrElse != null) Block(ifs.OrElse, locals);
                            break;
                        case ForStmt loop:
                            Expr(loop.Iterable, locals);
                            Block(loop.Body, locals);
                            break;
                        case WhileStmt loop:
                            Expr(loop.Condition, locals);
                            Block(loop.Body, locals);
                            break;
                        case WithStmt with:
                            // 자리와 모양은 FunctionChecker가 본다 (G111). preconditions는 이름이 아니라 표지다
                            if (!(with.Context is NameExpr context && context.Name == FunctionChecker.PreconditionsName))
                                Expr(with.Context, locals);
                            PreconditionBlock(with.Body, locals);
                            break;
                        case ReturnStmt ret:
                            if (ret.Value != null) Expr(ret.Value, locals);
                            break;
                    }
                }
            }

            /// <summary>
            /// <c>with preconditions:</c>의 문장. <c>require(...)</c> 문장은 내장 <c>require</c>여야 한다: 모드나 함수가 그 이름을
            /// 덮어썼으면 이 자리에서 쓸 수 없다 (G112). 인자는 평범한 식으로 본다.
            /// </summary>
            private void PreconditionBlock(IReadOnlyList<Stmt> body, SortedSet<string> locals)
            {
                foreach (var stmt in body)
                {
                    if (!(stmt is ExprStmt es) || !FunctionChecker.IsPreconditionCall(es.Expr, out var name))
                    {
                        // 블록에 올 수 없는 문장은 FunctionChecker가 알렸다: 그 안의 require를 한 번 더 알리지 않는다.
                        // 허용되는 대입(x = require(...))의 require는 알린다
                        bool allowed = stmt is AssignStmt assign && assign.Op == "=" && assign.Target is NameExpr;
                        if (allowed)
                        {
                            Block(new[] { stmt }, locals);
                            continue;
                        }
                        _quietPreconditionNames++;
                        try
                        {
                            Block(new[] { stmt }, locals);
                        }
                        finally
                        {
                            _quietPreconditionNames--;
                        }
                        continue;
                    }
                    var call = (CallExpr)es.Expr;
                    if (IsShadowed(name, locals))
                    {
                        _context.Error(Codes.CmdPreconditions,
                            "'" + name + "' here is " + ShadowKind(name, locals) +
                            ", so the built-in " + name + "() cannot be used", call.Callee.Span,
                            hint: "rename it (spec-gaps G112)");
                    }
                    foreach (var arg in call.Arguments) Expr(arg.Value, locals);
                }
            }

            /// <summary><c>pay(칸, 금액)</c>: 위치 인자 둘, 첫 자리는 <c>party.Treasury.gold</c> 같은 속성 (G187). 칸의 종류는 실행 때 본다.</summary>
            private void CheckPay(CallExpr call)
            {
                const string usage = "write ok = pay(party.Treasury.gold, amount)";
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name == null) continue;
                    _context.Error(Codes.EvlArguments, "pay() takes no keyword arguments", arg.Span, usage);
                    return;
                }
                if (call.Arguments.Count != 2)
                {
                    _context.Error(Codes.EvlArguments,
                        "pay() takes a field and an amount, got " + call.Arguments.Count + " argument" +
                        (call.Arguments.Count == 1 ? "" : "s"), call.Span, usage);
                    return;
                }
                if (!(call.Arguments[0].Value is AttributeExpr))
                {
                    _context.Error(Codes.EvlArguments, "the first argument of pay() must be a field such as party.Treasury.gold",
                        call.Arguments[0].Value.Span,
                        "pay() takes the amount out of that field, so it needs the field itself, not a value (spec-gaps G186)");
                }
            }

            /// <summary><c>query</c>는 위치 인자로 출처 이름을 하나 이상 받는다. 리터럴 이름은 모아서 ⑤ 링크가 본다 (G192).</summary>
            private void CheckQuery(CallExpr call)
            {
                const string usage = "write query(\"Unit\"), query(\"Wounded\") or query(\"Unit\", \"Wounded\")";
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name == null) continue;
                    _context.Error(Codes.EvlArguments, "query() takes no keyword arguments", arg.Span, usage);
                    return;
                }
                if (call.Arguments.Count == 0)
                {
                    _context.Error(Codes.EvlArguments, "query() needs at least one Def type or component name", call.Span, usage);
                    return;
                }
                var names = new List<KeyValuePair<string, SourceSpan>>();
                foreach (var arg in call.Arguments)
                    if (arg.Value is StringLiteral s) names.Add(new KeyValuePair<string, SourceSpan>(s.Value, s.Span));
                if (names.Count > 0) _queried.Add(names);
            }

            private int _quietPreconditionNames;

            /// <summary>
            /// 지역 변수, 모드가 묶은 이름, 이 모드에 보이는 컴포넌트·신호 이름(G230)이면 같은 이름의 내장을 가린다.
            /// 엔진 신호(<c>Tick</c>)는 스키마 이름이지만 엔진의 것이라 가리지 않는다.
            /// </summary>
            private bool IsShadowed(string name, SortedSet<string> locals) =>
                locals.Contains(name) || _globals.Contains(name) || IsSchemaName(name);

            private bool IsSchemaName(string name) => !_globals.Contains(name) && !Schema.EngineSignals.IsEngine(name) && _isSchemaRuntimeName(name);

            private string ShadowKind(string name, SortedSet<string> locals) =>
                locals.Contains(name) ? "a local variable" : IsSchemaName(name) ? "a component or signal this mod sees" : "this mod's own name";

            public void Expr(Expr expr, SortedSet<string> locals)
            {
                switch (expr)
                {
                    case NameExpr name:
                        Name(name, locals);
                        break;
                    case BinaryExpr bin:
                        Expr(bin.Left, locals);
                        Expr(bin.Right, locals);
                        break;
                    case UnaryExpr unary:
                        Expr(unary.Operand, locals);
                        break;
                    case CallExpr call:
                        // 덮어쓰지 않은 spawn에 리터럴로 적은 Def id는 ⑤ 링크가 본다 (G134)
                        if (call.Callee is NameExpr callee && callee.Name == PureBuiltins.SpawnName &&
                            !IsShadowed(callee.Name, locals) && call.Arguments.Count > 0 &&
                            call.Arguments[0].Name == null && call.Arguments[0].Value is StringLiteral id)
                            _spawned.Add(new KeyValuePair<string, SourceSpan>(id.Value, id.Span));
                        // 덮어쓰지 않은 query에 리터럴로 적은 출처 이름은 ⑤ 링크가 본다 (G192)
                        if (call.Callee is NameExpr query && query.Name == PureBuiltins.QueryName && !IsShadowed(query.Name, locals))
                            CheckQuery(call);
                        // 엔진 신호의 값은 스크립트가 만들 수 없다 (G156)
                        if (call.Callee is NameExpr signal && Schema.EngineSignals.IsEngine(signal.Name) && !IsShadowed(signal.Name, locals))
                            _context.Error(Codes.CmdWriteEffect, Schema.EngineSignals.Why(signal.Name), call.Span);
                        // 덮어쓰지 않은 pay의 첫 자리는 칸 자체다 (G186·G187)
                        if (call.Callee is NameExpr pay && pay.Name == PureBuiltins.PayName && !IsShadowed(pay.Name, locals))
                            CheckPay(call);
                        Expr(call.Callee, locals);
                        foreach (var arg in call.Arguments) Expr(arg.Value, locals);
                        break;
                    case AttributeExpr attr:
                        if (!HostChain(attr, locals)) Expr(attr.Target, locals);
                        break;
                    case IndexExpr index:
                        Expr(index.Target, locals);
                        foreach (var i in index.Indices) Expr(i, locals);
                        break;
                    case ListExpr list:
                        foreach (var item in list.Items) Expr(item, locals);
                        break;
                    case DictExpr dict:
                        foreach (var entry in dict.Entries)
                        {
                            Expr(entry.Key, locals);
                            Expr(entry.Value, locals);
                        }
                        break;
                    case ConditionalExpr cond:
                        Expr(cond.Condition, locals);
                        Expr(cond.WhenTrue, locals);
                        Expr(cond.WhenFalse, locals);
                        break;
                    case LambdaExpr lambda:
                        var inner = new SortedSet<string>(locals, StringComparer.Ordinal);
                        foreach (var p in lambda.Parameters) inner.Add(p.Name);
                        Expr(lambda.Body, inner);
                        break;
                    case ComprehensionExpr comp:
                        foreach (var (part, bound) in comp.Parts())
                        {
                            var scope = new SortedSet<string>(locals, StringComparer.Ordinal);
                            scope.UnionWith(bound);
                            Expr(part, scope);
                        }
                        break;
                }
            }

            /// <summary>
            /// 맨 앞이 보이는 호스트 이름 공간인 속성 줄(<c>core.path.find</c>)을 본다 (G218): 함수까지 이어져야 하고, 이름 공간 자체는
            /// 값이 아니며, 함수에는 속성이 없다. 호스트 이름 공간이 아니면(지역·최상위 이름이 가렸거나 다른 이름) false.
            /// </summary>
            private bool HostChain(AttributeExpr attr, SortedSet<string> locals)
            {
                if (!HostNames.TryChain(attr, out var root, out var attributes)) return false;
                if (locals.Contains(root.Name) || _globals.Contains(root.Name) || !_hosts.IsRoot(root.Name)) return false;
                var r = _hosts.Resolve(root.Name, attributes);
                if (r.Missing != null)
                {
                    _context.Error(Codes.EvlUnknownName, r.Prefix + " has no host function '" + r.Missing + "'",
                        attributes[r.Used].Span, Suggest.Hint(r.Missing, _hosts.Children(r.Prefix)));
                }
                else if (r.Function == null)
                    NotAValue(r.Prefix, attr.Span);
                else if (r.Used < attributes.Count)
                {
                    _context.Error(Codes.HostName,
                        r.Function + " is a host function, so it has no attribute '" + attributes[r.Used].Name + "'",
                        attributes[r.Used].Span);
                }
                return true;
            }

            private void NotAValue(string prefix, SourceSpan span) =>
                _context.Error(Codes.HostName, prefix + " is a group of host functions, not a value", span,
                    "call one of them, such as " + _hosts.FirstFunctionUnder(prefix) + "(...) (spec-gaps G218)");

            private void Name(NameExpr name, SortedSet<string> locals)
            {
                string n = name.Name;
                if (locals.Contains(n) || _globals.Contains(n) || PureBuiltins.IsRuntimeName(n) || _isSchemaRuntimeName(n)) return;
                if (_hosts.IsRoot(n))
                {
                    NotAValue(n, name.Span);
                    return;
                }
                if (_hosts.IsHidden(n))
                {
                    _context.Error(Codes.SchNotVisible,
                        "the host functions of mod '" + n + "' can only be used by mods that depend on it", name.Span,
                        "add \"" + n + "\" to dependencies in mod.py (spec-gaps G218)");
                    return;
                }
                var hidden = _notVisible(n);
                if (hidden != null)
                {
                    _context.Error(Codes.SchNotVisible, hidden, name.Span);
                    return;
                }
                // 덮어쓰지 않은 require 등이 블록의 문장이 아닌 자리에 나왔다 (G111·G112)
                if (Array.IndexOf(FunctionChecker.PreconditionCalls, n) >= 0)
                {
                    if (_quietPreconditionNames > 0) return;
                    _context.Error(Codes.CmdPreconditions,
                        n + "() can only be used as a statement inside 'with preconditions:'", name.Span);
                    return;
                }

                if (_isLoadTimeName(n))
                {
                    _context.Error(Codes.EvlUnknownName,
                        "'" + n + "' is only available while content files load, not inside a logic function", name.Span,
                        hint: "logic functions can use their parameters, local variables, this mod's top-level names and " +
                              string.Join(", ", PureBuiltins.RuntimeNames));
                    return;
                }

                var candidates = new List<string>(locals);
                candidates.AddRange(_globals);
                candidates.AddRange(PureBuiltins.RuntimeNames);
                candidates.AddRange(_schemaRuntimeNames);
                candidates.AddRange(_hosts.Roots);
                _context.Error(Codes.EvlUnknownName, "name '" + n + "' is not defined", name.Span,
                    hint: Suggest.Hint(n, candidates));
            }
        }
    }
}
