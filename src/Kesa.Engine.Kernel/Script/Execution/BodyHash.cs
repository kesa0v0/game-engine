using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 본문 해시 (저장, spec S8·C5, spec-gaps G144). 멈춘 Flow는 프레임의 함수와 람다가 든 함수의 해시를 적고,
    /// 불러올 때 지금 함수의 해시가 다르면 거부한다.
    /// 해시는 위치 정보를 뺀 구조로만 정해진다: 매개변수(기본값·타입 표기)·반환 표기·본문의 문장과 식, 이름, 연산자, 상수.
    /// 공백·주석·줄 위치·데코레이터는 들어가지 않는다. 노드마다 종류 표시를 먼저 적고 문자열·목록은 길이를 먼저 적어 이어 붙인 결과가 모호하지 않다.
    /// </summary>
    internal static class BodyHash
    {
        public static string Of(FunctionValue function)
        {
            var w = new Writer();
            if (function.Definition != null)
            {
                var def = function.Definition;
                w.Tag("def");
                w.Parameters(def.Parameters);
                w.Optional(def.Returns);
                w.Block(def.Body);
            }
            else
            {
                w.Tag("lambda");
                w.Parameters(function.Lambda!.Parameters);
                w.Expr(function.Lambda.Body);
            }
            return w.Finish();
        }

        private sealed class Writer
        {
            private readonly MemoryStream _bytes = new MemoryStream();

            public string Finish()
            {
                byte[] digest;
                using (var sha = SHA256.Create()) digest = sha.ComputeHash(_bytes.ToArray());
                var hex = new StringBuilder(32);
                for (int i = 0; i < 16; i++) hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }

            public void Tag(string tag) => Text(tag);

            private void Number(long value)
            {
                for (int i = 0; i < 8; i++) _bytes.WriteByte(unchecked((byte)(value >> (8 * i))));
            }

            private void Text(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                Number(bytes.Length);
                _bytes.Write(bytes, 0, bytes.Length);
            }

            private void OptionalText(string? value)
            {
                Number(value == null ? 0 : 1);
                if (value != null) Text(value);
            }

            public void Optional(Expr? expr)
            {
                Number(expr == null ? 0 : 1);
                if (expr != null) Expr(expr);
            }

            public void Parameters(IReadOnlyList<Parameter> parameters)
            {
                Number(parameters.Count);
                foreach (var p in parameters)
                {
                    Text(p.Name);
                    Optional(p.Annotation);
                    Optional(p.Default);
                }
            }

            public void Block(IReadOnlyList<Stmt> body)
            {
                Number(body.Count);
                foreach (var stmt in body) Stmt(stmt);
            }

            private void Stmt(Stmt stmt)
            {
                switch (stmt)
                {
                    case ExprStmt s:
                        Tag("expr");
                        Expr(s.Expr);
                        return;
                    case AssignStmt s:
                        Tag("assign");
                        Text(s.Op);
                        Expr(s.Target);
                        Expr(s.Value);
                        return;
                    case WithStmt s:
                        Tag("with");
                        Expr(s.Context);
                        Block(s.Body);
                        return;
                    case ReturnStmt s:
                        Tag("return");
                        Optional(s.Value);
                        return;
                    case ForStmt s:
                        Tag("for");
                        Text(s.Target.Name);
                        Expr(s.Iterable);
                        Block(s.Body);
                        return;
                    case IfStmt s:
                        Tag("if");
                        Number(s.Branches.Count);
                        foreach (var branch in s.Branches)
                        {
                            Expr(branch.Condition);
                            Block(branch.Body);
                        }
                        Number(s.OrElse == null ? 0 : 1);
                        if (s.OrElse != null) Block(s.OrElse);
                        return;
                    case PassStmt _:
                        Tag("pass");
                        return;
                    case BreakStmt _:
                        Tag("break");
                        return;
                    case ContinueStmt _:
                        Tag("continue");
                        return;
                    case FunctionDef s:
                        Tag("function");
                        Text(s.Name);
                        Number(s.Decorators.Count);
                        foreach (var d in s.Decorators) Expr(d);
                        Parameters(s.Parameters);
                        Optional(s.Returns);
                        Block(s.Body);
                        return;
                    case UnsupportedStmt s:
                        Tag("unsupported");
                        Text(s.Keyword);
                        return;
                    default:
                        throw new System.InvalidOperationException("engine: no body hash for " + stmt.GetType().Name);
                }
            }

            public void Expr(Expr expr)
            {
                switch (expr)
                {
                    case NameExpr e:
                        Tag("name");
                        Text(e.Name);
                        return;
                    case IntLiteral e:
                        Tag("int");
                        Number(e.Value);
                        return;
                    case FixedLiteral e:
                        Tag("fixed");
                        Number(e.Value.Raw);
                        return;
                    case StringLiteral e:
                        Tag("str");
                        Text(e.Value);
                        return;
                    case BoolLiteral e:
                        Tag(e.Value ? "true" : "false");
                        return;
                    case NoneLiteral _:
                        Tag("none");
                        return;
                    case ListExpr e:
                        Tag("list");
                        Number(e.Items.Count);
                        foreach (var item in e.Items) Expr(item);
                        return;
                    case DictExpr e:
                        Tag("dict");
                        Number(e.Entries.Count);
                        foreach (var entry in e.Entries)
                        {
                            Expr(entry.Key);
                            Expr(entry.Value);
                        }
                        return;
                    case CallExpr e:
                        Tag("call");
                        Expr(e.Callee);
                        Number(e.Arguments.Count);
                        foreach (var arg in e.Arguments)
                        {
                            OptionalText(arg.Name);
                            Expr(arg.Value);
                        }
                        return;
                    case AttributeExpr e:
                        Tag("attr");
                        Expr(e.Target);
                        Text(e.Name);
                        return;
                    case IndexExpr e:
                        Tag("index");
                        Expr(e.Target);
                        Number(e.Indices.Count);
                        foreach (var index in e.Indices) Expr(index);
                        return;
                    case UnaryExpr e:
                        Tag("unary");
                        Text(e.Op);
                        Expr(e.Operand);
                        return;
                    case BinaryExpr e:
                        Tag("binary");
                        Text(e.Op);
                        Expr(e.Left);
                        Expr(e.Right);
                        return;
                    case ConditionalExpr e:
                        Tag("conditional");
                        Expr(e.Condition);
                        Expr(e.WhenTrue);
                        Expr(e.WhenFalse);
                        return;
                    case LambdaExpr e:
                        Tag("lambda");
                        Parameters(e.Parameters);
                        Expr(e.Body);
                        return;
                    case ComprehensionExpr e:
                        Tag("comprehension");
                        Number((int)e.Kind);
                        Expr(e.Element);
                        if (e.Value != null) Expr(e.Value);
                        Number(e.Clauses.Count);
                        foreach (var clause in e.Clauses)
                        {
                            Text(clause.Variable);
                            Expr(clause.Iterable);
                            Number(clause.Conditions.Count);
                            foreach (var condition in clause.Conditions) Expr(condition);
                        }
                        return;
                    case UnsupportedExpr e:
                        Tag("unsupported");
                        Text(e.What);
                        return;
                    default:
                        throw new System.InvalidOperationException("engine: no body hash for " + expr.GetType().Name);
                }
            }
        }
    }
}
