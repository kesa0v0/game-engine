using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 제한된 파이썬 부분집합의 파서 (spec S2).
    /// 원칙: 유효한 파이썬이 아닌 것은 받아들이지 않는다. 유효하지만 아직 지원하지 않는 구문은
    /// <see cref="Codes.ParUnsupported"/>로 거부하고 구문 오류와 구분한다.
    /// 지금 읽는 문장: 식, 대입, <c>def</c>(본문 포함), <c>if</c>/<c>elif</c>/<c>else</c>, <c>for</c>, <c>return</c>,
    /// <c>pass</c>, <c>break</c>, <c>continue</c>, 조건식 (spec-gaps G102). 함수 밖의 <c>return</c>과 반복 밖의
    /// <c>break</c>/<c>continue</c>는 파이썬에서도 오류라 구문 오류다.
    /// </summary>
    public sealed class Parser
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue",
            "def", "del", "elif", "else", "except", "finally", "for", "from", "global", "if", "import", "in",
            "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield",
        };

        private static readonly HashSet<string> CompoundKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "def", "if", "elif", "else", "while", "with", "class", "try", "except", "finally", "async",
        };

        private static readonly HashSet<string> AssignOps = new HashSet<string>(StringComparer.Ordinal)
        {
            "=", "+=", "-=", "*=", "/=", "//=", "%=", "**=", "&=", "|=", "^=", "<<=", ">>=", "@=",
        };

        public static bool IsKeyword(string name) => Keywords.Contains(name);

        private readonly IReadOnlyList<Token> _tokens;
        private readonly string _file;
        private readonly DiagnosticBag _diagnostics;
        private int _pos;
        private int _functionDepth;
        private int _loopDepth;

        private Parser(IReadOnlyList<Token> tokens, string file, DiagnosticBag diagnostics)
        {
            _tokens = tokens;
            _file = file;
            _diagnostics = diagnostics;
        }

        public static Module Parse(string text, string file, DiagnosticBag diagnostics)
        {
            var tokens = Lexer.Tokenize(text, file, diagnostics);
            var parser = new Parser(tokens, file, diagnostics);
            return parser.ParseModule();
        }

        private Token Cur => _tokens[_pos];
        private Token PeekToken(int offset) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

        private Token Advance()
        {
            var t = _tokens[_pos];
            if (_pos < _tokens.Count - 1) _pos++;
            return t;
        }

        private bool Accept(string op)
        {
            if (!Cur.IsOp(op)) return false;
            Advance();
            return true;
        }

        private sealed class ParseError : Exception
        {
        }

        private ParseError Fail(string message, SourceSpan span)
        {
            _diagnostics.Error(Codes.ParSyntax, message, span);
            return new ParseError();
        }

        private Token Expect(string op)
        {
            if (Cur.IsOp(op)) return Advance();
            throw Fail("expected '" + op + "'" + Describe(Cur), Cur.Span);
        }

        private static string Describe(Token t)
        {
            switch (t.Kind)
            {
                case TokenKind.Newline: return " before end of line";
                case TokenKind.EndOfFile: return " before end of file";
                case TokenKind.Indent: return ", found unexpected indent";
                case TokenKind.Dedent: return ", found end of block";
                default: return ", found '" + t.Text + "'";
            }
        }

        private Module ParseModule()
        {
            var body = new List<Stmt>();
            while (Cur.Kind != TokenKind.EndOfFile)
            {
                if (Cur.Kind == TokenKind.Newline)
                {
                    Advance();
                    continue;
                }

                if (Cur.Kind == TokenKind.Indent)
                {
                    _diagnostics.Error(Codes.ParSyntax, "unexpected indent", Cur.Span);
                    SkipBlock();
                    continue;
                }

                if (Cur.Kind == TokenKind.Dedent)
                {
                    Advance();
                    continue;
                }

                ParseStatementLine(body);
            }
            return new Module(_file, body);
        }

        /// <summary>한 논리 줄(세미콜론으로 나뉜 단순 문장들 또는 복합 문장 하나)을 읽는다.</summary>
        private void ParseStatementLine(List<Stmt> body)
        {
            if (Cur.IsOp("@"))
            {
                body.Add(SkipDecorated());
                return;
            }

            if (Cur.IsName("def"))
            {
                body.Add(ParseFunctionDef());
                return;
            }

            if (Cur.IsName("if"))
            {
                body.Add(ParseIf());
                return;
            }

            if (Cur.IsName("for"))
            {
                body.Add(ParseFor());
                return;
            }

            if (Cur.IsName("elif") || Cur.IsName("else"))
            {
                _diagnostics.Error(Codes.ParSyntax, "'" + Cur.Text + "' without a matching 'if'", Cur.Span);
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return;
            }

            if (Cur.Kind == TokenKind.Name &&
                (CompoundKeywords.Contains(Cur.Text) || (Cur.Text == "match" && IsSoftKeywordBlock())))
            {
                body.Add(SkipUnsupportedCompound());
                return;
            }

            ParseSimpleLine(body);
        }

        /// <summary>세미콜론으로 나뉜 단순 문장들을 줄 끝까지 읽는다. 구문 오류는 보고하고 그 줄을 건너뛴다.</summary>
        private void ParseSimpleLine(List<Stmt> body)
        {
            try
            {
                while (true)
                {
                    body.Add(ParseSimpleStatement());
                    if (Accept(";"))
                    {
                        if (Cur.Kind == TokenKind.Newline) break;
                        continue;
                    }
                    break;
                }

                if (Cur.Kind != TokenKind.Newline && Cur.Kind != TokenKind.EndOfFile)
                {
                    throw Fail("expected end of statement" + Describe(Cur), Cur.Span);
                }
                if (Cur.Kind == TokenKind.Newline) Advance();
            }
            catch (ParseError)
            {
                SkipToNewline();
            }
        }

        /// <summary><c>if</c> / <c>elif</c>... / <c>else</c> 전체를 읽는다 (spec-gaps G46).</summary>
        private Stmt ParseIf()
        {
            var start = Cur;
            var branches = new List<IfBranch>();
            List<Stmt>? orElse = null;
            try
            {
                branches.Add(ParseIfBranch());
                while (Cur.IsName("elif")) branches.Add(ParseIfBranch());
                if (Cur.IsName("else"))
                {
                    Advance();
                    Expect(":");
                    orElse = ParseSuite();
                }
                return new IfStmt(branches, orElse, start.Span);
            }
            catch (ParseError)
            {
                // 오류 뒤에 이어지는 elif/else는 조용히 건너뛴다 (연쇄 오류를 내지 않는다)
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                while (Cur.IsName("elif") || Cur.IsName("else"))
                {
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                }
                return new UnsupportedStmt("if", start.Span);
            }
        }

        private IfBranch ParseIfBranch()
        {
            var keyword = Advance();
            var condition = ParseExpression();
            Expect(":");
            return new IfBranch(condition, ParseSuite(), keyword.Span);
        }

        /// <summary>
        /// ':' 뒤의 블록: 같은 줄의 단순 문장들이거나, 줄바꿈 뒤 들여쓴 문장들. 들여쓴 블록 안에는 다시 <c>if</c>를 쓸 수 있다.
        /// </summary>
        private List<Stmt> ParseSuite()
        {
            var body = new List<Stmt>();
            if (Cur.Kind != TokenKind.Newline)
            {
                ParseSimpleLine(body);
                return body;
            }

            Advance();
            if (Cur.Kind != TokenKind.Indent) throw Fail("expected an indented block", Cur.Span);
            Advance();
            while (Cur.Kind != TokenKind.Dedent && Cur.Kind != TokenKind.EndOfFile)
            {
                if (Cur.Kind == TokenKind.Newline)
                {
                    Advance();
                    continue;
                }

                if (Cur.Kind == TokenKind.Indent)
                {
                    _diagnostics.Error(Codes.ParSyntax, "unexpected indent", Cur.Span);
                    SkipBlock();
                    continue;
                }

                ParseStatementLine(body);
            }
            if (Cur.Kind == TokenKind.Dedent) Advance();
            return body;
        }

        /// <summary>
        /// <c>for 이름 in 식:</c> 블록. 풀어 받기(<c>for k, v in</c>)와 <c>else</c> 절은 미지원으로 보고한다.
        /// </summary>
        private Stmt ParseFor()
        {
            var keyword = Advance();
            try
            {
                var target = Cur;
                if (target.IsOp("(") || target.IsOp("[") || target.IsOp("*"))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "unpacking in a 'for' target is not supported", target.Span);
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                    return new UnsupportedStmt("for", keyword.Span);
                }
                if (target.Kind != TokenKind.Name || Keywords.Contains(target.Text))
                    throw Fail("expected a loop variable after 'for'" + Describe(target), target.Span);
                Advance();
                if (Cur.IsOp(","))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "unpacking in a 'for' target is not supported", Cur.Span);
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                    return new UnsupportedStmt("for", keyword.Span);
                }
                if (!Cur.IsName("in")) throw Fail("expected 'in'" + Describe(Cur), Cur.Span);
                Advance();
                var iterable = ParseExpression();
                Expect(":");

                List<Stmt> body;
                _loopDepth++;
                try
                {
                    body = ParseSuite();
                }
                finally
                {
                    _loopDepth--;
                }

                if (Cur.IsName("else"))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "'else' on a 'for' loop is not supported", Cur.Span);
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                }
                return new ForStmt(new NameExpr(target.Text, target.Span), iterable, body, keyword.Span);
            }
            catch (ParseError)
            {
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return new UnsupportedStmt("for", keyword.Span);
            }
        }

        /// <summary>
        /// <c>def</c>의 머리부와 본문. 매개변수는 이름과 어노테이션만 지원한다 (기본값, <c>*args</c>, <c>/</c> 등은 미지원).
        /// 함수 안의 <c>def</c>는 미지원이다 (spec-gaps G102).
        /// </summary>
        private Stmt ParseFunctionDef()
        {
            if (_functionDepth > 0)
            {
                _diagnostics.Error(Codes.ParUnsupported, "function definitions inside functions are not supported", Cur.Span);
                var nested = Cur;
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return new UnsupportedStmt("def", nested.Span);
            }

            var keyword = Advance();
            try
            {
                var name = Cur;
                if (name.Kind != TokenKind.Name || Keywords.Contains(name.Text))
                    throw Fail("expected function name after 'def'" + Describe(name), name.Span);
                Advance();
                Expect("(");
                var parameters = ParseParameters(")", allowAnnotations: true);
                Expect(")");

                Expr? returns = Accept("->") ? ParseExpression() : null;
                Expect(":");

                List<Stmt> body;
                int savedLoopDepth = _loopDepth;
                _functionDepth++;
                _loopDepth = 0;
                try
                {
                    body = ParseSuite();
                }
                finally
                {
                    _functionDepth--;
                    _loopDepth = savedLoopDepth;
                }
                return new FunctionDef(name.Text, parameters, returns, body, keyword.Span);
            }
            catch (ParseError)
            {
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return new UnsupportedStmt("def", keyword.Span);
            }
        }

        /// <summary>
        /// 매개변수 목록을 <paramref name="close"/> 앞까지 읽는다 (<c>def</c>는 <c>)</c>, 람다는 <c>:</c>).
        /// 이름과 (def만) 어노테이션을 지원하고, 기본값·<c>*args</c>·<c>/</c> 등은 미지원으로 보고한다.
        /// </summary>
        private List<Parameter> ParseParameters(string close, bool allowAnnotations)
        {
            var parameters = new List<Parameter>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!Cur.IsOp(close))
            {
                if (Cur.IsOp("*") || Cur.IsOp("**") || Cur.IsOp("/"))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "'" + Cur.Text + "' in parameter lists is not supported",
                        Cur.Span);
                    while (!Cur.IsOp(",") && !Cur.IsOp(close) && Cur.Kind != TokenKind.Newline &&
                           Cur.Kind != TokenKind.EndOfFile) Advance();
                }
                else
                {
                    var p = Cur;
                    if (p.Kind != TokenKind.Name || Keywords.Contains(p.Text))
                        throw Fail("expected parameter name" + Describe(p), p.Span);
                    Advance();
                    Expr? annotation = allowAnnotations && Accept(":") ? ParseExpression() : null;
                    if (Cur.IsOp("="))
                    {
                        _diagnostics.Error(Codes.ParUnsupported, "default parameter values are not supported", Cur.Span);
                        Advance();
                        ParseExpression();
                    }
                    if (!seen.Add(p.Text))
                        _diagnostics.Error(Codes.ParSyntax, "duplicate parameter name: " + p.Text, p.Span);
                    parameters.Add(new Parameter(p.Text, annotation, p.Span));
                }
                if (!Accept(",")) break;
            }
            return parameters;
        }

        private Stmt SkipUnsupportedCompound()
        {
            var keyword = Cur;
            _diagnostics.Error(Codes.ParUnsupported, "'" + keyword.Text + "' statements are not supported yet", keyword.Span);
            SkipToNewline();
            if (Cur.Kind == TokenKind.Indent) SkipBlock();
            return new UnsupportedStmt(keyword.Text, keyword.Span);
        }

        /// <summary>데코레이터 줄들과 그 아래의 def/class 블록을 건너뛴다.</summary>
        private Stmt SkipDecorated()
        {
            var at = Cur;
            _diagnostics.Error(Codes.ParUnsupported, "decorators are not supported yet", at.Span);
            while (Cur.IsOp("@")) SkipToNewline();
            if (Cur.IsName("def") || Cur.IsName("class") || Cur.IsName("async"))
            {
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
            }
            else if (Cur.Kind != TokenKind.EndOfFile)
            {
                _diagnostics.Error(Codes.ParSyntax, "a decorator must be followed by def or class", Cur.Span);
            }
            return new UnsupportedStmt("@", at.Span);
        }

        /// <summary>
        /// 소프트 키워드(<c>match</c>)가 문장을 여는지: 논리 줄이 ':'로 끝나고 다음이 들여쓰기면 블록이다.
        /// 그 밖에는 평범한 이름이다 (<c>match = 1</c>, <c>match(x)</c>).
        /// </summary>
        private bool IsSoftKeywordBlock()
        {
            int i = _pos;
            while (i < _tokens.Count && _tokens[i].Kind != TokenKind.Newline && _tokens[i].Kind != TokenKind.EndOfFile) i++;
            if (i >= _tokens.Count || _tokens[i].Kind != TokenKind.Newline || i == _pos) return false;
            if (!_tokens[i - 1].IsOp(":")) return false;
            return i + 1 < _tokens.Count && _tokens[i + 1].Kind == TokenKind.Indent;
        }

        private void SkipToNewline()
        {
            while (Cur.Kind != TokenKind.Newline && Cur.Kind != TokenKind.EndOfFile) Advance();
            if (Cur.Kind == TokenKind.Newline) Advance();
        }

        /// <summary>현재 Indent부터 짝이 맞는 Dedent까지 건너뛴다.</summary>
        private void SkipBlock()
        {
            int depth = 0;
            while (Cur.Kind != TokenKind.EndOfFile)
            {
                var t = Advance();
                if (t.Kind == TokenKind.Indent) depth++;
                else if (t.Kind == TokenKind.Dedent && --depth <= 0) return;
            }
        }

        private Stmt ParseSimpleStatement()
        {
            var start = Cur;
            if (start.Kind == TokenKind.Name)
            {
                switch (start.Text)
                {
                    case "pass":
                        Advance();
                        return new PassStmt(start.Span);

                    case "break":
                    case "continue":
                        if (_loopDepth == 0)
                            throw Fail("'" + start.Text + "' outside a loop", start.Span);
                        Advance();
                        return start.Text == "break" ? (Stmt)new BreakStmt(start.Span) : new ContinueStmt(start.Span);

                    case "return":
                        if (_functionDepth == 0)
                            throw Fail("'return' outside a function", start.Span);
                        Advance();
                        if (Cur.Kind == TokenKind.Newline || Cur.Kind == TokenKind.EndOfFile || Cur.IsOp(";"))
                            return new ReturnStmt(null, start.Span);
                        var returned = ParseExpression();
                        if (Cur.IsOp(","))
                        {
                            _diagnostics.Error(Codes.ParUnsupported, "returning several values is not supported", Cur.Span);
                            while (Cur.Kind != TokenKind.Newline && Cur.Kind != TokenKind.EndOfFile && !Cur.IsOp(";")) Advance();
                            return new UnsupportedStmt("return", start.Span);
                        }
                        return new ReturnStmt(returned, start.Span);
                }
            }

            if (start.Kind == TokenKind.Name && Keywords.Contains(start.Text) &&
                start.Text != "True" && start.Text != "False" && start.Text != "None" &&
                start.Text != "not" && start.Text != "lambda" && start.Text != "await")
            {
                _diagnostics.Error(Codes.ParUnsupported, "'" + start.Text + "' statements are not supported yet", start.Span);
                while (Cur.Kind != TokenKind.Newline && Cur.Kind != TokenKind.EndOfFile && !Cur.IsOp(";")) Advance();
                return new UnsupportedStmt(start.Text, start.Span);
            }

            var expr = ParseExpression();

            if (Cur.IsOp(":"))
            {
                _diagnostics.Error(Codes.ParUnsupported, "annotated assignments are not supported yet", Cur.Span);
                while (Cur.Kind != TokenKind.Newline && Cur.Kind != TokenKind.EndOfFile) Advance();
                return new UnsupportedStmt(":", expr.Span);
            }

            if (Cur.Kind == TokenKind.Op && AssignOps.Contains(Cur.Text))
            {
                var op = Advance();
                if (!(expr is NameExpr || expr is AttributeExpr || expr is IndexExpr))
                {
                    _diagnostics.Error(Codes.ParSyntax, "cannot assign to expression here", expr.Span);
                }
                var value = ParseExpression();
                if (Cur.IsOp("="))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "chained assignment is not supported", Cur.Span);
                    while (Cur.Kind != TokenKind.Newline && Cur.Kind != TokenKind.EndOfFile) Advance();
                }
                return new AssignStmt(expr, op.Text, value, op.Span);
            }

            return new ExprStmt(expr);
        }

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
                        _diagnostics.Error(Codes.ParUnsupported, "generator expressions are not supported", Cur.Span);
                        SkipExpressionTail();
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
                _diagnostics.Error(Codes.ParUnsupported, "generator expressions are not supported", Cur.Span);
                SkipExpressionTail();
            }
            Expect(")");
            return inner;
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

                if (Cur.IsName("for"))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "comprehensions are not supported yet", Cur.Span);
                    SkipExpressionTail();
                    Expect("]");
                    return new UnsupportedExpr("comprehension", open.Span);
                }

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
                        _diagnostics.Error(Codes.ParUnsupported, "comprehensions are not supported yet", Cur.Span);
                        SkipExpressionTail();
                        Expect("}");
                        return new UnsupportedExpr("comprehension", open.Span);
                    }
                    throw Fail("expected ':' in dict literal" + Describe(Cur), Cur.Span);
                }
                Advance();
                var value = ParseExpression();
                entries.Add(new DictEntry(key, value));

                if (Cur.IsName("for"))
                {
                    _diagnostics.Error(Codes.ParUnsupported, "comprehensions are not supported yet", Cur.Span);
                    SkipExpressionTail();
                    Expect("}");
                    return new UnsupportedExpr("comprehension", open.Span);
                }

                if (!Accept(",")) break;
            }
            Expect("}");
            return new DictExpr(entries, open.Span);
        }
    }
}
