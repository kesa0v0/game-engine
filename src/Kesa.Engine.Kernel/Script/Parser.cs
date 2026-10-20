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
    /// 지금 읽는 문장: 식, 대입, <c>def</c>(본문 포함), <c>if</c>/<c>elif</c>/<c>else</c>, <c>for</c>, <c>while</c>,
    /// <c>with</c>, <c>match</c>(<c>if</c>로 바꿔 넣음, G253), <c>return</c>, <c>pass</c>, <c>break</c>, <c>continue</c>, 조건식 (spec-gaps G102). 함수 밖의 <c>return</c>과 반복 밖의
    /// <c>break</c>/<c>continue</c>는 파이썬에서도 오류라 구문 오류다.
    /// </summary>
    public sealed partial class Parser
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
        private int _matchCount; // 함수 안에서 바꿔 넣은 match의 수: 숨은 대상 변수의 번호 (G253)

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

        /// <summary>
        /// 호스트 함수의 서명 한 줄 <c>def path.find(start: Ref[Tile], max_steps: int = 50) -> list[Ref[Tile]]</c>을 읽는다
        /// (spec S7, spec-gaps G215). 이름은 점으로 이은 경로이고 매개변수 기본값과 반환 타입을 쓸 수 있으며(반환 타입은 필수),
        /// 본문(<c>:</c> 뒤)은 없다. 읽지 못하면 진단을 남기고 null.
        /// </summary>
        internal static FunctionDef? ParseHostHeader(string text, string file, DiagnosticBag diagnostics)
        {
            var tokens = Lexer.Tokenize(text, file, diagnostics);
            if (diagnostics.HasErrors) return null;
            var header = new Parser(tokens, file, diagnostics).HostHeader();
            // 매개변수 목록의 일부 오류는 진단만 남기고 계속 읽는다
            return diagnostics.HasErrors ? null : header;
        }

        private FunctionDef? HostHeader()
        {
            try
            {
                if (!Cur.IsName("def"))
                    throw Fail("a host function signature starts with 'def'" + Describe(Cur), Cur.Span);
                var keyword = Advance();
                var path = new List<string>();
                while (true)
                {
                    var name = Cur;
                    if (name.Kind != TokenKind.Name || Keywords.Contains(name.Text))
                        throw Fail("expected a name" + Describe(name), name.Span);
                    path.Add(name.Text);
                    Advance();
                    if (!Accept(".")) break;
                }
                Expect("(");
                var parameters = ParseParameters(")", allowAnnotations: true, allowDefaults: true);
                Expect(")");
                if (!Cur.IsOp("->"))
                    throw Fail("a host function signature needs a return type" + Describe(Cur), Cur.Span);
                Advance();
                var returns = ParseExpression();
                while (Cur.Kind == TokenKind.Newline) Advance();
                if (Cur.Kind != TokenKind.EndOfFile)
                    throw Fail("a host function signature is one 'def' line without a body" + Describe(Cur), Cur.Span);
                return new FunctionDef(string.Join(".", path), parameters, returns, new List<Stmt>(), keyword.Span);
            }
            catch (ParseError)
            {
                return null;
            }
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
                body.Add(ParseDecorated());
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

            if (Cur.IsName("while"))
            {
                body.Add(ParseWhile());
                return;
            }

            if (Cur.IsName("with"))
            {
                body.Add(ParseWith());
                return;
            }

            if (Cur.IsName("elif") || Cur.IsName("else"))
            {
                _diagnostics.Error(Codes.ParSyntax, "'" + Cur.Text + "' without a matching 'if'", Cur.Span);
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return;
            }

            if (Cur.IsName("match") && IsSoftKeywordBlock())
            {
                body.AddRange(ParseMatch());
                return;
            }

            if (Cur.Kind == TokenKind.Name && CompoundKeywords.Contains(Cur.Text))
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
    }
}
