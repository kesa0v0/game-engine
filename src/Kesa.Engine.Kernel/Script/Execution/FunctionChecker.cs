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

        public const string PreconditionsName = "preconditions";
        public const string RequireName = "require";

        /// <summary><c>with preconditions:</c> 안에서만 쓰는 이름들 (spec S2). <c>cost</c>·<c>choose</c>는 아직 미지원이다.</summary>
        public static readonly string[] PreconditionCalls = { RequireName, "cost", "choose" };

        /// <summary>
        /// 함수 본문을 검사한다. 데코레이터가 붙은 함수는 커맨드로 보고, 첫 문장의 <c>with preconditions:</c>를
        /// 허용한다 (spec-gaps G111). 데코레이터 자체의 검사는 인터프리터가 한다.
        /// </summary>
        public static void Check(FunctionDef def, CallContext context)
        {
            var walker = new Walker(context, def.Name);
            bool isCommand = def.Decorators.Count > 0;
            for (int i = 0; i < def.Body.Count; i++)
            {
                if (i == 0 && isCommand && def.Body[0] is WithStmt with) walker.Preconditions(with);
                else walker.Statement(def.Body[i]);
            }
        }

        public static void Check(LambdaExpr lambda, CallContext context)
        {
            new Walker(context, null).Expr(lambda.Body);
        }

        /// <summary>
        /// <c>require(...)</c>·<c>cost(...)</c>·<c>choose(...)</c> 모양의 호출인가 (이름만 본다). 모드가 그 이름을 덮어썼는지는
        /// 모드 평가가 끝난 뒤 <see cref="FunctionNameChecker"/>가 본다 (G112).
        /// </summary>
        public static bool IsPreconditionCall(Expr expr, out string name)
        {
            name = "";
            if (!(expr is CallExpr call) || !(call.Callee is NameExpr callee)) return false;
            name = callee.Name;
            return System.Array.IndexOf(PreconditionCalls, name) >= 0;
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

            /// <summary>
            /// 커맨드 첫 문장의 <c>with preconditions:</c> (G111). 안에는 <c>require</c> 호출과 <c>이름 = 식</c>만 온다.
            /// </summary>
            public void Preconditions(WithStmt with)
            {
                if (!(with.Context is NameExpr context) || context.Name != PreconditionsName)
                {
                    _context.Error(Codes.CmdPreconditions, "a command can only open 'with preconditions:'", with.Context.Span);
                    return;
                }
                foreach (var stmt in with.Body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es when IsPreconditionCall(es.Expr, out var name):
                            var call = (CallExpr)es.Expr;
                            if (name != RequireName)
                            {
                                Report("'" + name + "' is not supported yet", call.Span,
                                    "this slice supports require(...) only");
                                break;
                            }
                            CheckRequire(call);
                            foreach (var arg in call.Arguments) Expr(arg.Value);
                            break;

                        case AssignStmt assign when assign.Op == "=" && assign.Target is NameExpr:
                            Expr(assign.Value);
                            break;

                        default:
                            _context.Error(Codes.CmdPreconditions,
                                "'with preconditions:' can only hold require(...) calls and 'name = value' assignments",
                                stmt.Span, "the block is checked without running the command, so it cannot change anything (spec-gaps G111)");
                            break;
                    }
                }
            }

            /// <summary><c>require(조건, "메시지 키", 이름=값, ...)</c>: 위치 인자는 정확히 둘이다.</summary>
            private void CheckRequire(CallExpr call)
            {
                int positional = 0;
                foreach (var arg in call.Arguments)
                    if (arg.Name == null) positional++;
                if (positional != 2)
                {
                    _context.Error(Codes.CmdPreconditions,
                        "require() takes a condition and a message key, got " + positional + " positional argument" +
                        (positional == 1 ? "" : "s"), call.Span,
                        "write require(condition, \"msg.key\", name=value, ...)");
                }
            }

            public void Statement(Stmt stmt)
            {
                switch (stmt)
                {
                    case ExprStmt es:
                        Expr(es.Expr);
                        break;

                    case WithStmt with:
                        _context.Error(Codes.CmdPreconditions,
                            "'with' can only be the first statement of a @command function", with.Span,
                            "write 'with preconditions:' as the first statement of the command (spec-gaps G111)");
                        Preconditions(with); // 안의 require가 "블록 밖의 require"로 한 번 더 보고되지 않게
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
                        // 블록 밖의 require 등은 모드가 그 이름을 덮어썼는지 알아야 하므로 FunctionNameChecker가 본다 (G112)
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
