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
        public static void Check(ModFunctions functions, ModScope scope, Func<string, bool> isLoadTimeName,
            CallContext context)
        {
            var globals = new SortedSet<string>(scope.Names, StringComparer.Ordinal);
            globals.UnionWith(functions.TopLevelNames);
            foreach (var function in functions.Functions)
            {
                var walker = new Walker(globals, isLoadTimeName, context);
                var locals = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var p in function.Parameters) locals.Add(p.Name);
                if (function.Definition != null)
                {
                    CollectLocals(function.Definition.Body, locals);
                    walker.Block(function.Definition.Body, locals);
                }
                else
                {
                    walker.Expr(function.Lambda!.Body, locals);
                }
            }
        }

        /// <summary>본문 어디서든 대입하거나 <c>for</c>로 묶는 이름 (파이썬의 지역 변수 규칙).</summary>
        private static void CollectLocals(IReadOnlyList<Stmt> body, SortedSet<string> locals)
        {
            foreach (var stmt in body)
            {
                switch (stmt)
                {
                    case AssignStmt assign when assign.Target is NameExpr name:
                        locals.Add(name.Name);
                        break;
                    case ForStmt loop:
                        locals.Add(loop.Target.Name);
                        CollectLocals(loop.Body, locals);
                        break;
                    case IfStmt ifs:
                        foreach (var branch in ifs.Branches) CollectLocals(branch.Body, locals);
                        if (ifs.OrElse != null) CollectLocals(ifs.OrElse, locals);
                        break;
                }
            }
        }

        private sealed class Walker
        {
            private readonly SortedSet<string> _globals;
            private readonly Func<string, bool> _isLoadTimeName;
            private readonly CallContext _context;

            public Walker(SortedSet<string> globals, Func<string, bool> isLoadTimeName, CallContext context)
            {
                _globals = globals;
                _isLoadTimeName = isLoadTimeName;
                _context = context;
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
                            // 이름 대상은 지역 변수다. 그 밖의 대상은 FunctionChecker가 이미 거부했다
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
                        case ReturnStmt ret:
                            if (ret.Value != null) Expr(ret.Value, locals);
                            break;
                    }
                }
            }

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
                        Expr(call.Callee, locals);
                        foreach (var arg in call.Arguments) Expr(arg.Value, locals);
                        break;
                    case AttributeExpr attr:
                        Expr(attr.Target, locals);
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
                }
            }

            private void Name(NameExpr name, SortedSet<string> locals)
            {
                string n = name.Name;
                if (locals.Contains(n) || _globals.Contains(n) || PureBuiltins.IsRuntimeName(n)) return;

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
                _context.Error(Codes.EvlUnknownName, "name '" + n + "' is not defined", name.Span,
                    hint: Suggest.Hint(n, candidates));
            }
        }
    }
}
