using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 본문의 로드 시점 정적 검사 (spec-gaps G96·G102). 파서는 유효한 파이썬을 모두 읽으므로, 로직에서 허용하지 않는
    /// 연산자(<c>**</c>, 비트 연산, <c>@</c>)와 지역 값을 고치는 것(항목 대입, G104), 직접 재귀를 여기서 막는다.
    /// 속성 대입(엔티티 쓰기)이 허용되는 자리는 <see cref="FunctionEffects"/>가 본다 (G117).
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
        public const string CostName = "cost";
        public const string ChooseName = "choose";
        public const string WhereName = "where";

        /// <summary><c>from</c>은 파이썬 예약어라 밑줄을 붙인다 (spec S2).</summary>
        public const string FromName = "from_";

        /// <summary><c>with preconditions:</c> 안에서만 쓰는 이름들 (spec S2).</summary>
        public static readonly string[] PreconditionCalls = { RequireName, CostName, ChooseName };

        /// <summary>
        /// 함수 본문을 검사한다. 데코레이터가 붙은 함수는 커맨드로 보고, 첫 문장의 <c>with preconditions:</c>를
        /// 허용한다 (spec-gaps G111). 데코레이터 자체의 검사는 인터프리터가 한다.
        /// </summary>
        public static void Check(FunctionDef def, CallContext context)
        {
            var walker = new Walker(context, def.Name);
            bool isCommand = def.IsCommand;
            for (int i = 0; i < def.Body.Count; i++)
            {
                if (i == 0 && isCommand && def.Body[0] is WithStmt with) walker.Preconditions(with, def.Parameters);
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
            public void Preconditions(WithStmt with, IReadOnlyList<Parameter>? parameters = null)
            {
                if (!(with.Context is NameExpr context) || context.Name != PreconditionsName)
                {
                    _context.Error(Codes.CmdPreconditions, "a command can only open 'with preconditions:'", with.Context.Span);
                    return;
                }
                var chosen = new HashSet<string>(System.StringComparer.Ordinal);
                foreach (var stmt in with.Body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es when IsPreconditionCall(es.Expr, out var name):
                            var call = (CallExpr)es.Expr;
                            if (name == RequireName) CheckRequire(call);
                            else if (name == CostName) CheckCost(call);
                            else CheckChoose(call, parameters, chosen);
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
                if (parameters != null) CheckChooseOrder(with, parameters);
            }

            /// <summary>
            /// 블록에 적힌 순서가 고르는 순서다 (G188): <c>choose(P)</c>의 후보 식은 자기 매개변수와 아래쪽 <c>choose</c>가 정하는
            /// 매개변수를 읽을 수 없다.
            /// </summary>
            private void CheckChooseOrder(WithStmt with, IReadOnlyList<Parameter> parameters)
            {
                var chooses = ChooseDependencies.Of(with, parameters);
                for (int i = 0; i < chooses.Count; i++)
                {
                    var c = chooses[i];
                    if (c.Needs.Contains(c.Parameter))
                    {
                        _context.Error(Codes.CmdPreconditions,
                            "the candidates of '" + c.Parameter + "' read '" + c.Parameter + "' itself", c.Call.Arguments[1].Span,
                            "the candidates are listed before '" + c.Parameter + "' is picked (spec-gaps G188)");
                        continue;
                    }
                    for (int j = i + 1; j < chooses.Count; j++)
                    {
                        var later = chooses[j].Parameter;
                        if (!c.Needs.Contains(later)) continue;
                        _context.Error(Codes.CmdPreconditions,
                            "the candidates of '" + c.Parameter + "' read '" + later + "', which is chosen below",
                            c.Call.Arguments[1].Span,
                            "move choose(" + later + ", ...) above choose(" + c.Parameter + ", ...): parameters are picked in " +
                            "the order of the block (spec-gaps G188)");
                        break;
                    }
                }
            }

            /// <summary><c>require(조건, text("키", 이름=값, ...))</c>: 위치 인자 둘, 키워드 인자 없음 (G113).</summary>
            private void CheckRequire(CallExpr call)
            {
                const string usage = "write require(condition, text(\"msg.key\", name=value, ...))";
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name == null) continue;
                    _context.Error(Codes.CmdPreconditions, "require() takes no keyword arguments", arg.Span,
                        "message arguments go inside text(...): " + usage);
                    return;
                }
                if (call.Arguments.Count != 2)
                {
                    _context.Error(Codes.CmdPreconditions,
                        "require() takes a condition and a message, got " + call.Arguments.Count + " argument" +
                        (call.Arguments.Count == 1 ? "" : "s"), call.Span, usage);
                    return;
                }
                if (call.Arguments[1].Value is StringLiteral key)
                {
                    _context.Error(Codes.CmdPreconditions, "the message of require() must be a text(...) value",
                        key.Span, "write text(\"" + key.Value + "\")");
                }
            }

            /// <summary>
            /// <c>cost(칸, 금액)</c>: 위치 인자 둘, 키워드 인자 없음, 첫 자리는 <c>party.Treasury.gold</c> 같은 속성 (G186).
            /// 속성 이름과 깊이는 보지 않는다(<c>t.gold</c>도 된다): 그 칸이 <c>int</c>·<c>fixed</c> <c>field</c>인지는 실행 때 본다 (G108).
            /// </summary>
            private void CheckCost(CallExpr call)
            {
                const string usage = "write cost(party.Treasury.gold, amount)";
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name == null) continue;
                    _context.Error(Codes.CmdPreconditions, "cost() takes no keyword arguments", arg.Span, usage);
                    return;
                }
                if (call.Arguments.Count != 2)
                {
                    _context.Error(Codes.CmdPreconditions,
                        "cost() takes a field and an amount, got " + call.Arguments.Count + " argument" +
                        (call.Arguments.Count == 1 ? "" : "s"), call.Span, usage);
                    return;
                }
                if (!(call.Arguments[0].Value is AttributeExpr))
                {
                    _context.Error(Codes.CmdPreconditions, "the first argument of cost() must be a field such as party.Treasury.gold",
                        call.Arguments[0].Value.Span,
                        "cost() takes the amount out of that field, so it needs the field itself, not a value (spec-gaps G186)");
                }
            }

            /// <summary>
            /// <c>choose(매개변수, where=함수)</c>나 <c>choose(매개변수, from_=목록)</c> (G183): 첫 자리는 커맨드 매개변수의 이름,
            /// 키워드 인자는 둘 중 하나만, 매개변수마다 한 번. <c>where=</c>의 매개변수가 <c>Ref[...]</c>인지는 타입을 푼 뒤
            /// <c>CommandSignatures</c>가 본다.
            /// </summary>
            private void CheckChoose(CallExpr call, IReadOnlyList<Parameter>? parameters, HashSet<string> chosen)
            {
                const string usage = "write choose(unit, where=lambda u: ...) or choose(dest, from_=[...])";
                int positional = 0, keywords = 0;
                foreach (var arg in call.Arguments)
                {
                    if (arg.Name == null)
                    {
                        positional++;
                        continue;
                    }
                    keywords++;
                    if (arg.Name != WhereName && arg.Name != FromName)
                    {
                        _context.Error(Codes.CmdPreconditions, "choose() has no argument '" + arg.Name + "'", arg.Span,
                            usage + " (spec-gaps G183)");
                        return;
                    }
                }
                if (positional != 1 || keywords != 1 || call.Arguments[0].Name != null)
                {
                    _context.Error(Codes.CmdPreconditions,
                        "choose() takes a parameter and either where= or from_=", call.Span, usage + " (spec-gaps G183)");
                    return;
                }
                if (!(call.Arguments[0].Value is NameExpr target))
                {
                    _context.Error(Codes.CmdPreconditions, "the first argument of choose() must be a parameter of the command",
                        call.Arguments[0].Value.Span, usage);
                    return;
                }
                if (parameters != null)
                {
                    bool found = false;
                    foreach (var p in parameters) found |= p.Name == target.Name;
                    if (!found)
                    {
                        _context.Error(Codes.CmdPreconditions, "'" + target.Name + "' is not a parameter of the command",
                            target.Span, "choose() gives the candidates of a parameter (spec-gaps G183)");
                        return;
                    }
                }
                if (!chosen.Add(target.Name))
                {
                    _context.Error(Codes.CmdPreconditions, "the candidates of '" + target.Name + "' are already chosen above",
                        call.Span, "write one choose() per parameter; put every condition in its where= (spec-gaps G183)");
                }
            }

            private void Parts(ComprehensionExpr comp)
            {
                foreach (var (part, _) in comp.Parts()) Expr(part);
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
                        // 속성 대입은 엔티티 상태 쓰기다: 어디서 부를 수 있는지는 모드 이름이 다 정해진 뒤 FunctionEffects가 본다 (G117).
                        // 지역 값은 불변이라 항목 대입은 안 된다 (G104)
                        if (!(assign.Target is NameExpr) && !(assign.Target is AttributeExpr))
                        {
                            Report("a logic function can only assign to local variables and entity values", assign.Target.Span,
                                "write name = ... or unit.Health.current = ...; lists and dicts cannot be changed in place (spec-gaps G104)");
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
                        foreach (var arg in call.Arguments)
                        {
                            // 제너레이터 식은 호출의 인자로만 온다 (G189)
                            if (arg.Value is ComprehensionExpr gen && gen.Kind == ComprehensionKind.Generator) Parts(gen);
                            else Expr(arg.Value);
                        }
                        break;

                    case ComprehensionExpr comp:
                        if (comp.Kind == ComprehensionKind.Generator)
                        {
                            Report("a generator expression can only be an argument of a call", comp.Span,
                                "write sum(x for x in xs), or [x for x in xs] for a list (spec-gaps G189)");
                        }
                        Parts(comp);
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
