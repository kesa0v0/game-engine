using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>복합문 읽기: <c>if</c>·<c>for</c>·<c>while</c>·<c>with</c>·<c>def</c>·데코레이터, 매개변수, 미지원 블록 건너뛰기 (G102·G195).</summary>
    public sealed partial class Parser
    {
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

        /// <summary><c>while 식:</c> 블록. <c>else</c> 절은 미지원으로 보고한다 (spec-gaps G195).</summary>
        private Stmt ParseWhile()
        {
            var keyword = Advance();
            try
            {
                var condition = ParseExpression();
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
                    _diagnostics.Error(Codes.ParUnsupported, "'else' on a 'while' loop is not supported", Cur.Span);
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                }
                return new WhileStmt(condition, body, keyword.Span);
            }
            catch (ParseError)
            {
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return new UnsupportedStmt("while", keyword.Span);
            }
        }

        /// <summary>
        /// <c>with 식:</c> 블록 (spec-gaps G111). <c>as</c>와 여러 항목(<c>with a, b:</c>)은 미지원으로 보고한다.
        /// 어디에 올 수 있는지는 로드 시점 검사가 본다.
        /// </summary>
        private Stmt ParseWith()
        {
            var keyword = Advance();
            try
            {
                var context = ParseExpression();
                if (Cur.IsName("as") || Cur.IsOp(","))
                {
                    _diagnostics.Error(Codes.ParUnsupported,
                        Cur.IsOp(",") ? "several items in a 'with' statement are not supported" : "'with ... as' is not supported",
                        Cur.Span);
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                    return new UnsupportedStmt("with", keyword.Span);
                }
                Expect(":");
                return new WithStmt(context, ParseSuite(), keyword.Span);
            }
            catch (ParseError)
            {
                SkipToNewline();
                if (Cur.Kind == TokenKind.Indent) SkipBlock();
                return new UnsupportedStmt("with", keyword.Span);
            }
        }

        /// <summary>
        /// <c>def</c>의 머리부와 본문. 매개변수는 이름과 어노테이션만 지원한다 (기본값, <c>*args</c>, <c>/</c> 등은 미지원).
        /// 함수 안의 <c>def</c>는 미지원이다 (spec-gaps G102).
        /// </summary>
        private Stmt ParseFunctionDef(IReadOnlyList<Expr>? decorators = null)
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
                var parameters = ParseParameters(")", allowAnnotations: true,
                    allowDefaults: decorators != null && decorators.Count > 0);
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
                return new FunctionDef(name.Text, parameters, returns, body, keyword.Span, decorators);
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
        /// 이름과 (def만) 어노테이션을 지원한다. 기본값은 데코레이터가 붙은 def(커맨드)에만 받고(G116, 무엇이 허용되는지는
        /// 로더가 본다), 그 밖의 기본값과 <c>*args</c>·<c>/</c> 등은 미지원으로 보고한다.
        /// </summary>
        private List<Parameter> ParseParameters(string close, bool allowAnnotations, bool allowDefaults = false)
        {
            var parameters = new List<Parameter>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool sawDefault = false;
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
                    Expr? @default = null;
                    if (Cur.IsOp("="))
                    {
                        var equals = Advance();
                        var value = ParseExpression();
                        if (allowDefaults) @default = value;
                        else
                            _diagnostics.Error(Codes.ParUnsupported,
                                "default parameter values are only supported on @command functions", equals.Span,
                                hint: "spec-gaps G116");
                    }
                    if (@default != null) sawDefault = true;
                    else if (sawDefault && allowDefaults)
                        _diagnostics.Error(Codes.ParSyntax, "parameter '" + p.Text + "' without a default follows one with a default",
                            p.Span, hint: "move the parameters with defaults to the end, as in Python");
                    if (!seen.Add(p.Text))
                        _diagnostics.Error(Codes.ParSyntax, "duplicate parameter name: " + p.Text, p.Span);
                    parameters.Add(new Parameter(p.Text, annotation, p.Span, @default));
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

        /// <summary>
        /// 데코레이터 줄들(<c>@식</c>)과 그 아래의 <c>def</c>. 데코레이터는 식으로 읽기만 하고, 무엇이 허용되는지는
        /// 인터프리터가 본다 (spec-gaps G109). <c>class</c> 앞의 데코레이터는 <c>class</c>와 함께 미지원으로 보고된다.
        /// </summary>
        private Stmt ParseDecorated()
        {
            var at = Cur;
            var decorators = new List<Expr>();
            try
            {
                while (Cur.IsOp("@"))
                {
                    Advance();
                    decorators.Add(ParseExpression());
                    if (Cur.Kind != TokenKind.Newline) throw Fail("expected end of line after a decorator" + Describe(Cur), Cur.Span);
                    Advance();
                }
            }
            catch (ParseError)
            {
                SkipToNewline();
                while (Cur.IsOp("@")) SkipToNewline();
                if (Cur.IsName("def") || Cur.IsName("class"))
                {
                    SkipToNewline();
                    if (Cur.Kind == TokenKind.Indent) SkipBlock();
                }
                return new UnsupportedStmt("@", at.Span);
            }

            if (Cur.IsName("def") && _functionDepth == 0) return ParseFunctionDef(decorators);
            if (Cur.IsName("def")) return ParseFunctionDef(); // 함수 안의 def는 ParseFunctionDef가 미지원으로 보고한다
            if (Cur.IsName("class")) return SkipUnsupportedCompound();
            _diagnostics.Error(Codes.ParSyntax, "a decorator must be followed by def or class", Cur.Span);
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
    }
}
