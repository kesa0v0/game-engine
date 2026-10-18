using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>식 읽기: 우선순위 낮은 것부터(<c>or</c> → 비교 → 산술 → 후위 → 원자), 인자, 리스트·딕트·컴프리헨션 (G189).</summary>
    public sealed partial class Parser
    {
        // ---- 식: 우선순위 낮은 것부터 ----

        private Expr ParseExpression()
        {
            if (Cur.IsName("lambda"))
            {
                // lambda params: body. 람다에는 어노테이션이 없고, 본문은 조건식을 포함한 식 하나다.
                var t = Advance();
                var parameters = ParseParameters(":", allowAnnotations: false);
                Expect(":");
                return new LambdaExpr(parameters, ParseExpression(), t.Span);
            }

            var expr = ParseOr();
            if (Cur.IsName("if"))
            {
                Advance();
                var condition = ParseOr();
                if (!Cur.IsName("else")) throw Fail("expected 'else' in conditional expression" + Describe(Cur), Cur.Span);
                Advance();
                var whenFalse = ParseExpression();
                return new ConditionalExpr(condition, expr, whenFalse, expr.Span);
            }
            return expr;
        }

        /// <summary>지원하지 않는 식의 나머지를 괄호 균형을 지키며 건너뛴다.</summary>
        private void SkipExpressionTail()
        {
            int depth = 0;
            while (Cur.Kind != TokenKind.EndOfFile && Cur.Kind != TokenKind.Newline)
            {
                if (Cur.Kind == TokenKind.Op)
                {
                    if (Cur.Text == "(" || Cur.Text == "[" || Cur.Text == "{") depth++;
                    else if (Cur.Text == ")" || Cur.Text == "]" || Cur.Text == "}")
                    {
                        if (depth == 0) return;
                        depth--;
                    }
                    else if (Cur.Text == "," && depth == 0) return;
                }
                Advance();
            }
        }

        private Expr ParseOr()
        {
            var left = ParseAnd();
            while (Cur.IsName("or"))
            {
                var op = Advance();
                left = new BinaryExpr("or", left, ParseAnd(), op.Span);
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseNot();
            while (Cur.IsName("and"))
            {
                var op = Advance();
                left = new BinaryExpr("and", left, ParseNot(), op.Span);
            }
            return left;
        }

        private Expr ParseNot()
        {
            if (Cur.IsName("not"))
            {
                var op = Advance();
                return new UnaryExpr("not", ParseNot(), op.Span);
            }
            return ParseComparison();
        }

        private Expr ParseComparison()
        {
            var left = ParseBitOr();
            string? op = ComparisonOp(out int width);
            if (op == null) return left;

            var opToken = Cur;
            for (int i = 0; i < width; i++) Advance();
            var right = ParseBitOr();
            var result = new BinaryExpr(op, left, right, opToken.Span);

            if (ComparisonOp(out _) != null)
            {
                _diagnostics.Error(Codes.ParUnsupported, "chained comparisons are not supported", Cur.Span);
                while (ComparisonOp(out int w) != null)
                {
                    for (int i = 0; i < w; i++) Advance();
                    ParseBitOr();
                }
            }
            return result;
        }

        private string? ComparisonOp(out int width)
        {
            width = 1;
            var t = Cur;
            if (t.Kind == TokenKind.Op)
            {
                switch (t.Text)
                {
                    case "==":
                    case "!=":
                    case "<":
                    case ">":
                    case "<=":
                    case ">=":
                        return t.Text;
                }
                return null;
            }

            if (t.IsName("in")) return "in";
            if (t.IsName("not") && PeekToken(1).IsName("in"))
            {
                width = 2;
                return "not in";
            }
            if (t.IsName("is"))
            {
                if (PeekToken(1).IsName("not"))
                {
                    width = 2;
                    return "is not";
                }
                return "is";
            }
            return null;
        }

        private Expr ParseBitOr() => ParseLeftAssoc(ParseBitXor, "|");
        private Expr ParseBitXor() => ParseLeftAssoc(ParseBitAnd, "^");
        private Expr ParseBitAnd() => ParseLeftAssoc(ParseShift, "&");
        private Expr ParseShift() => ParseLeftAssoc(ParseArith, "<<", ">>");
        private Expr ParseArith() => ParseLeftAssoc(ParseTerm, "+", "-");
        private Expr ParseTerm() => ParseLeftAssoc(ParseFactor, "*", "/", "//", "%", "@");

        private Expr ParseLeftAssoc(Func<Expr> next, params string[] ops)
        {
            var left = next();
            while (Cur.Kind == TokenKind.Op && Array.IndexOf(ops, Cur.Text) >= 0)
            {
                var op = Advance();
                left = new BinaryExpr(op.Text, left, next(), op.Span);
            }
            return left;
        }

        private Expr ParseFactor()
        {
            if (Cur.IsOp("-") || Cur.IsOp("+") || Cur.IsOp("~"))
            {
                var op = Advance();
                return new UnaryExpr(op.Text, ParseFactor(), op.Span);
            }
            return ParsePower();
        }

        private Expr ParsePower()
        {
            var left = ParsePostfix();
            if (Cur.IsOp("**"))
            {
                var op = Advance();
                return new BinaryExpr("**", left, ParseFactor(), op.Span);
            }
            return left;
        }

        private Expr ParsePostfix()
        {
            var expr = ParseAtom();
            while (true)
            {
                if (Cur.IsOp("("))
                {
                    var open = Advance();
                    var args = ParseArguments();
                    expr = new CallExpr(expr, args, open.Span);
                }
                else if (Cur.IsOp("."))
                {
                    Advance();
                    var name = Cur;
                    if (name.Kind != TokenKind.Name || Keywords.Contains(name.Text))
                        throw Fail("expected attribute name after '.'" + Describe(name), name.Span);
                    Advance();
                    expr = new AttributeExpr(expr, name.Text, name.Span);
                }
                else if (Cur.IsOp("["))
                {
                    var open = Advance();
                    if (Cur.IsOp(":") || Cur.IsOp("]"))
                        throw Fail("expected index expression" + Describe(Cur), Cur.Span);
                    var indices = new List<Expr> { ParseExpression() };
                    if (Cur.IsOp(":"))
                    {
                        _diagnostics.Error(Codes.ParUnsupported, "slices are not supported", Cur.Span);
                        while (!Cur.IsOp("]") && Cur.Kind != TokenKind.EndOfFile && Cur.Kind != TokenKind.Newline) Advance();
                    }
                    else
                    {
                        while (Accept(","))
                        {
                            if (Cur.IsOp("]")) break;
                            indices.Add(ParseExpression());
                        }
                    }
                    Expect("]");
                    expr = new IndexExpr(expr, indices, open.Span);
                }
                else
                {
                    return expr;
                }
            }
        }

        private List<Argument> ParseArguments()
        {
            var args = new List<Argument>();
            var seenKeywords = new Dictionary<string, SourceSpan>(StringComparer.Ordinal);
            bool sawKeyword = false;

            while (!Cur.IsOp(")"))
            {
                if (Cur.IsOp("*") || Cur.IsOp("**"))
                {
                    var star = Advance();
                    _diagnostics.Error(Codes.ParUnsupported, "argument unpacking is not supported", star.Span);
                    var unpacked = ParseExpression();
                    args.Add(new Argument(null, new UnsupportedExpr(star.Text, star.Span), unpacked.Span));
                }
                else if (Cur.Kind == TokenKind.Name && PeekToken(1).IsOp("=") && !Keywords.Contains(Cur.Text))
                {
                    var name = Advance();
                    Advance();
                    var value = ParseExpression();
                    if (seenKeywords.TryGetValue(name.Text, out var first))
                    {
                        _diagnostics.Error(Codes.ParDuplicateKeyword, "keyword argument repeated: " + name.Text, name.Span,
                            related: new[] { first });
                    }
                    else
                    {
                        seenKeywords.Add(name.Text, name.Span);
                    }
                    args.Add(new Argument(name.Text, value, name.Span));
                    sawKeyword = true;
                }
                else
                {
                    var value = ParseExpression();
                    if (Cur.IsName("for"))
                    {
                        // f(x for x in xs): 괄호 없는 제너레이터 식은 유일한 인자일 때만 (파이썬과 같다, G189)
                        var forSpan = Cur.Span;
                        var clauses = ParseComprehensionClauses();
                        value = clauses == null
                            ? new UnsupportedExpr("comprehension", value.Span)
                            : new ComprehensionExpr(ComprehensionKind.Generator, value, null, clauses, value.Span);
                        if (args.Count > 0 || Cur.IsOp(","))
                            _diagnostics.Error(Codes.ParSyntax, "a generator expression must be parenthesized unless it is the only argument",
                                forSpan);
                    }
                    if (sawKeyword)
                    {
                        _diagnostics.Error(Codes.ParPositionalAfterKeyword, "positional argument follows keyword argument",
                            value.Span);
                    }
                    args.Add(new Argument(null, value, value.Span));
                }

                if (!Accept(",")) break;
            }

            Expect(")");
            return args;
        }

        private Expr ParseAtom()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case TokenKind.Int:
                    Advance();
                    return new IntLiteral(t.IntValue, t.Span);

                case TokenKind.Fixed:
                    Advance();
                    return new FixedLiteral(Fixed.FromRaw(t.IntValue), t.Span);

                case TokenKind.String:
                {
                    // 인접 문자열 리터럴은 이어 붙인다 (파이썬 규칙)
                    var sb = new System.Text.StringBuilder();
                    while (Cur.Kind == TokenKind.String) sb.Append(Advance().StringValue);
                    return new StringLiteral(sb.ToString(), t.Span);
                }

                case TokenKind.Name:
                    switch (t.Text)
                    {
                        case "True":
                            Advance();
                            return new BoolLiteral(true, t.Span);
                        case "False":
                            Advance();
                            return new BoolLiteral(false, t.Span);
                        case "None":
                            Advance();
                            return new NoneLiteral(t.Span);
                    }
                    if (Keywords.Contains(t.Text)) throw Fail("invalid syntax: unexpected '" + t.Text + "'", t.Span);
                    Advance();
                    return new NameExpr(t.Text, t.Span);

                case TokenKind.Op:
                    if (t.Text == "(") return ParseParen();
                    if (t.Text == "[") return ParseList();
                    if (t.Text == "{") return ParseDict();
                    break;
            }

            throw Fail("invalid syntax" + Describe(t), t.Span);
        }

        private Expr ParseParen()
        {
            var open = Advance();
            if (Cur.IsOp(")"))
            {
                Advance();
                _diagnostics.Error(Codes.ParUnsupported, "tuples are not supported", open.Span);
                return new UnsupportedExpr("tuple", open.Span);
            }

            var inner = ParseExpression();
            if (Cur.IsOp(","))
            {
                _diagnostics.Error(Codes.ParUnsupported, "tuples are not supported", open.Span);
                while (Accept(","))
                {
                    if (Cur.IsOp(")")) break;
                    ParseExpression();
                }
                Expect(")");
                return new UnsupportedExpr("tuple", open.Span);
            }
            if (Cur.IsName("for"))
            {
                // (x for ...): 호출 인자 자리인지는 FunctionChecker가 본다 (G189)
                var clauses = ParseComprehensionClauses();
                Expect(")");
                return clauses == null
                    ? new UnsupportedExpr("comprehension", open.Span)
                    : new ComprehensionExpr(ComprehensionKind.Generator, inner, null, clauses, open.Span);
            }
            Expect(")");
            return inner;
        }

        /// <summary>
        /// <c>for 변수 in 식 (if 식)*</c> 절들 (spec-gaps G189). 파이썬처럼 반복할 것과 조건은 조건식·람다가 아닌 식(<c>or</c> 수준)이다.
        /// 대상 풀기(<c>for k, v in ...</c>)는 받지 않는다: 진단을 내고 식의 나머지를 건너뛰어 null을 돌려준다.
        /// </summary>
        private List<ComprehensionClause>? ParseComprehensionClauses()
        {
            var clauses = new List<ComprehensionClause>();
            while (Cur.IsName("for"))
            {
                Advance();
                var target = Cur;
                if (target.Kind != TokenKind.Name || Keywords.Contains(target.Text))
                    throw Fail("expected a loop variable after 'for'" + Describe(target), target.Span);
                Advance();
                if (Cur.IsOp(","))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "unpacking in a comprehension target is not supported", Cur.Span);
                    while (Accept(",")) SkipExpressionTail();
                    return null;
                }
                if (!Cur.IsName("in")) throw Fail("expected 'in'" + Describe(Cur), Cur.Span);
                Advance();
                var iterable = ParseOr();
                var conditions = new List<Expr>();
                while (Cur.IsName("if"))
                {
                    Advance();
                    conditions.Add(ParseOr());
                }
                clauses.Add(new ComprehensionClause(target.Text, target.Span, iterable, conditions));
            }
            return clauses;
        }

        private Expr ParseList()
        {
            var open = Advance();
            var items = new List<Expr>();
            while (!Cur.IsOp("]"))
            {
                if (Cur.IsOp("*"))
                {
                    var star = Advance();
                    _diagnostics.Error(Codes.ParUnsupported, "unpacking in list literals is not supported", star.Span);
                    ParseExpression();
                }
                else
                {
                    items.Add(ParseExpression());
                }

                if (Cur.IsName("for") && items.Count == 1)
                {
                    var clauses = ParseComprehensionClauses();
                    Expect("]");
                    return clauses == null
                        ? new UnsupportedExpr("comprehension", open.Span)
                        : new ComprehensionExpr(ComprehensionKind.List, items[0], null, clauses, open.Span);
                }
                if (Cur.IsName("for")) throw Fail("a comprehension takes one element before 'for'", Cur.Span);

                if (!Accept(",")) break;
            }
            Expect("]");
            return new ListExpr(items, open.Span);
        }

        private Expr ParseDict()
        {
            var open = Advance();
            var entries = new List<DictEntry>();
            while (!Cur.IsOp("}"))
            {
                if (Cur.IsOp("**"))
                {
                    var star = Advance();
                    _diagnostics.Error(Codes.ParUnsupported, "unpacking in dict literals is not supported", star.Span);
                    ParseExpression();
                    if (!Accept(",")) break;
                    continue;
                }

                var key = ParseExpression();
                if (!Cur.IsOp(":"))
                {
                    if (Cur.IsOp(",") || Cur.IsOp("}"))
                    {
                        _diagnostics.Error(Codes.ParUnsupported, "set literals are not supported", open.Span);
                        while (Accept(","))
                        {
                            if (Cur.IsOp("}")) break;
                            ParseExpression();
                        }
                        Expect("}");
                        return new UnsupportedExpr("set", open.Span);
                    }
                    if (Cur.IsName("for"))
                    {
                        _diagnostics.Error(Codes.ParUnsupported, "set comprehensions are not supported", Cur.Span);
                        SkipExpressionTail();
                        Expect("}");
                        return new UnsupportedExpr("set", open.Span);
                    }
                    throw Fail("expected ':' in dict literal" + Describe(Cur), Cur.Span);
                }
                Advance();
                var value = ParseExpression();
                entries.Add(new DictEntry(key, value));

                if (Cur.IsName("for") && entries.Count == 1)
                {
                    var clauses = ParseComprehensionClauses();
                    Expect("}");
                    return clauses == null
                        ? new UnsupportedExpr("comprehension", open.Span)
                        : new ComprehensionExpr(ComprehensionKind.Dict, key, value, clauses, open.Span);
                }
                if (Cur.IsName("for")) throw Fail("a dict comprehension takes one 'key: value' before 'for'", Cur.Span);

                if (!Accept(",")) break;
            }
            Expect("}");
            return new DictExpr(entries, open.Span);
        }
    }
}
