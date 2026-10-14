using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 본문의 로드 시점 정적 검사 (spec-gaps G96·G102). 파서는 유효한 파이썬을 모두 읽으므로, 로직에서 허용하지 않는
    /// 연산자(<c>**</c>, 비트 연산, <c>@</c>)와 순수 함수가 할 수 없는 것(속성·항목 대입), 직접 재귀를 여기서 막는다.
    /// 값 식만 본다: 매개변수·반환 어노테이션은 <c>X | None</c> 같은 표기를 쓰므로 검사하지 않는다.
    /// 간접 재귀(a가 b를, b가 a를 부름)는 실행 중 호출 체인으로 잡는다.
    /// </summary>
    public static class FunctionChecker
    {
        private static readonly HashSet<string> AllowedAssign = new HashSet<string>
        {
            "=", "+=", "-=", "*=", "/=", "//=", "%=",
        };

        public static void Check(FunctionDef def, CallContext context)
        {
            var walker = new Walker(context, def.Name);
            walker.Block(def.Body);
        }

        public static void Check(LambdaExpr lambda, CallContext context)
        {
            new Walker(context, null).Expr(lambda.Body);
        }

        private sealed class Walker
        {
            private readonly CallContext _context;
            private readonly string? _selfName;

            public Walker(CallContext context, string? selfName)
            {
                _context = context;
                _selfName = selfName;
            }

            private void Report(string message, SourceSpan span, string? hint = null) =>
                _context.Error(Codes.EvlUnsupported, message, span, hint);

            public void Block(IReadOnlyList<Stmt> body)
            {
                foreach (var stmt in body) Statement(stmt);
            }

            private void Statement(Stmt stmt)
            {
                switch (stmt)
                {
                    case ExprStmt es:
                        Expr(es.Expr);
                        break;

                    case AssignStmt assign:
                        if (!(assign.Target is NameExpr))
                        {
                            Report("a logic function can only assign to local variables", assign.Target.Span,
                                "it cannot write to attributes or items: logic functions only read");
                        }
                        if (!AllowedAssign.Contains(assign.Op))
                        {
                            Report("operator '" + assign.Op + "' is not supported in logic functions", assign.Span,
                                "see spec-gaps G96");
                        }
                        Expr(assign.Value);
                        break;

                    case IfStmt ifs:
                        foreach (var branch in ifs.Branches)
                        {
                            Expr(branch.Condition);
                            Block(branch.Body);
                        }
                        if (ifs.OrElse != null) Block(ifs.OrElse);
                        break;

                    case ForStmt loop:
                        Expr(loop.Iterable);
                        Block(loop.Body);
                        break;

                    case ReturnStmt ret:
                        if (ret.Value != null) Expr(ret.Value);
                        break;
                }
            }

            public void Expr(Expr expr)
            {
                switch (expr)
                {
                    case BinaryExpr bin:
                        switch (bin.Op)
                        {
                            case "**":
                            case "&":
                            case "|":
                            case "^":
                            case "<<":
                            case ">>":
                            case "@":
                                Report("operator '" + bin.Op + "' is not supported in logic functions", bin.Span,
                                    "use + - * / // % and comparisons (spec-gaps G96)");
                                break;
                            case "is":
                            case "is not":
                                if (!(bin.Right is NoneLiteral))
                                {
                                    Report("'" + bin.Op + "' can only compare with None", bin.Span, "use == or !=");
                                }
                                break;
                        }
                        Expr(bin.Left);
                        Expr(bin.Right);
                        break;

                    case UnaryExpr unary:
                        if (unary.Op == "~")
                        {
                            Report("operator '~' is not supported in logic functions", unary.Span, "see spec-gaps G96");
                        }
                        Expr(unary.Operand);
                        break;

                    case CallExpr call:
                        if (_selfName != null && call.Callee is NameExpr callee && callee.Name == _selfName)
                        {
                            Report("function '" + _selfName + "' calls itself", call.Span,
                                "recursion is not allowed in logic functions");
                        }
                        Expr(call.Callee);
                        foreach (var arg in call.Arguments) Expr(arg.Value);
                        break;

                    case AttributeExpr attr:
                        Expr(attr.Target);
                        break;

                    case IndexExpr index:
                        Expr(index.Target);
                        foreach (var i in index.Indices) Expr(i);
                        break;

                    case ListExpr list:
                        foreach (var item in list.Items) Expr(item);
                        break;

                    case DictExpr dict:
                        foreach (var entry in dict.Entries)
                        {
                            Expr(entry.Key);
                            Expr(entry.Value);
                        }
                        break;

                    case ConditionalExpr cond:
                        Expr(cond.Condition);
                        Expr(cond.WhenTrue);
                        Expr(cond.WhenFalse);
                        break;

                    case LambdaExpr lambda:
                        Expr(lambda.Body);
                        break;
                }
            }
        }
    }
}
