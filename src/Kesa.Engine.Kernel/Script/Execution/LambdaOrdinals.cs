using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 본문 안의 람다 식을 나오는 순서대로 (저장, spec-gaps G143). 저장은 람다를 (그 람다가 든 함수, 이 목록의 번호)로 적는다.
    /// 순서는 구조로만 정해진다: 문장 순서, 문장 안에서는 왼쪽에서 오른쪽, 람다는 자기를 먼저 세고 그 안을 센다.
    /// 공백·주석이 바뀌어도 번호는 같고, 본문 구조가 바뀌는 것은 저장의 본문 해시가 막는다.
    /// </summary>
    internal static class LambdaOrdinals
    {
        public static List<LambdaExpr> Of(FunctionValue function)
        {
            var found = new List<LambdaExpr>();
            if (function.Definition != null)
            {
                Parameters(function.Definition.Parameters, found);
                Block(function.Definition.Body, found);
            }
            else
            {
                Parameters(function.Lambda!.Parameters, found);
                Expr(function.Lambda.Body, found);
            }
            return found;
        }

        private static void Parameters(IReadOnlyList<Parameter> parameters, List<LambdaExpr> found)
        {
            foreach (var p in parameters)
                if (p.Default != null) Expr(p.Default, found);
        }

        private static void Block(IReadOnlyList<Stmt> body, List<LambdaExpr> found)
        {
            foreach (var stmt in body) Stmt(stmt, found);
        }

        private static void Stmt(Stmt stmt, List<LambdaExpr> found)
        {
            switch (stmt)
            {
                case ExprStmt s:
                    Expr(s.Expr, found);
                    return;
                case AssignStmt s:
                    Expr(s.Target, found);
                    Expr(s.Value, found);
                    return;
                case WithStmt s:
                    Expr(s.Context, found);
                    Block(s.Body, found);
                    return;
                case ReturnStmt s:
                    if (s.Value != null) Expr(s.Value, found);
                    return;
                case ForStmt s:
                    Expr(s.Iterable, found);
                    Block(s.Body, found);
                    return;
                case IfStmt s:
                    foreach (var branch in s.Branches)
                    {
                        Expr(branch.Condition, found);
                        Block(branch.Body, found);
                    }
                    if (s.OrElse != null) Block(s.OrElse, found);
                    return;
            }
        }

        private static void Expr(Expr expr, List<LambdaExpr> found)
        {
            switch (expr)
            {
                case LambdaExpr e:
                    found.Add(e);
                    Parameters(e.Parameters, found);
                    Expr(e.Body, found);
                    return;
                case ListExpr e:
                    foreach (var item in e.Items) Expr(item, found);
                    return;
                case DictExpr e:
                    foreach (var entry in e.Entries)
                    {
                        Expr(entry.Key, found);
                        Expr(entry.Value, found);
                    }
                    return;
                case CallExpr e:
                    Expr(e.Callee, found);
                    foreach (var arg in e.Arguments) Expr(arg.Value, found);
                    return;
                case AttributeExpr e:
                    Expr(e.Target, found);
                    return;
                case IndexExpr e:
                    Expr(e.Target, found);
                    foreach (var index in e.Indices) Expr(index, found);
                    return;
                case UnaryExpr e:
                    Expr(e.Operand, found);
                    return;
                case BinaryExpr e:
                    Expr(e.Left, found);
                    Expr(e.Right, found);
                    return;
                case ConditionalExpr e:
                    Expr(e.Condition, found);
                    Expr(e.WhenTrue, found);
                    Expr(e.WhenFalse, found);
                    return;
                case ComprehensionExpr e:
                    foreach (var (part, _) in e.Parts()) Expr(part, found);
                    return;
            }
        }
    }
}
