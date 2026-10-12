using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 제한된 파이썬 부분집합의 렉서 (spec S2).
    /// 파이썬이 받아들이지 않는 입력을 받아들이지 않는다. 지원하지 않는 파이썬 구문은 별도 진단으로 거부한다.
    /// 줄바꿈은 \r\n, \r, \n 모두 한 줄로 센다. 열은 1부터, UTF-16 코드 단위.
    /// </summary>
    public sealed class Lexer
    {
        private static readonly string[] Operators =
        {
            "**=", "//=", ">>=", "<<=",
            "->", "**", "//", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "@=", ":=", "<<", ">>",
            "(", ")", "[", "]", "{", "}", ",", ":", ".", ";", "@", "=", "+", "-", "*", "/", "%", "<", ">", "&", "|", "^", "~",
        };

        private readonly string _text;
        private readonly string _file;
        private readonly DiagnosticBag _diagnostics;
        private readonly List<Token> _tokens = new List<Token>();
        private readonly List<int> _indents = new List<int> { 0 };
        private readonly List<Token> _openers = new List<Token>();

        private int _pos;
        private int _line = 1;
        private int _lineStart;
        private bool _atLineStart = true;

        private Lexer(string text, string file, DiagnosticBag diagnostics)
        {
            _text = text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
            _file = file;
            _diagnostics = diagnostics;
        }

        public static IReadOnlyList<Token> Tokenize(string text, string file, DiagnosticBag diagnostics)
        {
            var lexer = new Lexer(text, file, diagnostics);
            lexer.Run();
            return lexer._tokens;
        }

        private char Cur => _pos < _text.Length ? _text[_pos] : '\0';
        private char Peek(int offset) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';
        private bool AtEnd => _pos >= _text.Length;
        private SourceSpan Here => new SourceSpan(_file, _line, _pos - _lineStart + 1);

        private static bool IsNewline(char c) => c == '\n' || c == '\r';

        private void ConsumeNewline()
        {
            if (Cur == '\r' && Peek(1) == '\n') _pos += 2;
            else _pos += 1;
            _line++;
            _lineStart = _pos;
        }

        private void Run()
        {
            while (true)
            {
                if (_atLineStart && _openers.Count == 0)
                {
                    if (!HandleIndentation()) continue;
                }

                if (AtEnd) break;

                char c = Cur;
                if (c == ' ' || c == '\t' || c == '\f')
                {
                    _pos++;
                    continue;
                }

                if (c == '#')
                {
                    SkipComment();
                    continue;
                }

                if (IsNewline(c))
                {
                    if (_openers.Count == 0)
                    {
                        Emit(TokenKind.Newline, "\\n", Here);
                        _atLineStart = true;
                    }
                    ConsumeNewline();
                    continue;
                }

                if (c == '\\' && IsNewline(Peek(1)))
                {
                    _pos++;
                    ConsumeNewline();
                    continue;
                }

                if (IsIdentStart(c) || IsNonAsciiNameChar(c))
                {
                    LexNameOrPrefixedString();
                    continue;
                }

                if (IsDigit(c) || (c == '.' && IsDigit(Peek(1))))
                {
                    LexNumber();
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    LexString(Here, null);
                    continue;
                }

                if (!LexOperator())
                {
                    _diagnostics.Error(Codes.LexUnexpectedChar, "unexpected character '" + c + "'", Here);
                    _pos++;
                }
            }

            var end = Here;
            if (_tokens.Count > 0 && _tokens[_tokens.Count - 1].Kind != TokenKind.Newline &&
                _tokens[_tokens.Count - 1].Kind != TokenKind.Dedent)
            {
                Emit(TokenKind.Newline, "\\n", end);
            }

            foreach (var opener in _openers)
            {
                _diagnostics.Error(Codes.LexUnbalanced, "'" + opener.Text + "' was never closed", opener.Span);
            }

            while (_indents.Count > 1)
            {
                _indents.RemoveAt(_indents.Count - 1);
                Emit(TokenKind.Dedent, "", end);
            }

            Emit(TokenKind.EndOfFile, "", end);
        }

        /// <summary>줄 시작의 들여쓰기를 처리한다. 빈 줄·주석 줄이면 false를 돌려 다음 줄로 넘어간다.</summary>
        private bool HandleIndentation()
        {
            int width = 0;
            while (Cur == ' ' || Cur == '\t' || Cur == '\f')
            {
                if (Cur == '\t')
                {
                    _diagnostics.Error(Codes.LexTab, "tab in indentation; use spaces", Here);
                }
                if (Cur == ' ' || Cur == '\t') width++;
                _pos++;
            }

            if (AtEnd)
            {
                _atLineStart = false;
                return true;
            }

            if (Cur == '#')
            {
                SkipComment();
                if (!AtEnd) ConsumeNewline();
                return false;
            }

            if (IsNewline(Cur))
            {
                ConsumeNewline();
                return false;
            }

            _atLineStart = false;
            int top = _indents[_indents.Count - 1];
            var span = new SourceSpan(_file, _line, 1);
            if (width > top)
            {
                _indents.Add(width);
                Emit(TokenKind.Indent, "", span);
            }
            else if (width < top)
            {
                while (_indents.Count > 1 && _indents[_indents.Count - 1] > width)
                {
                    _indents.RemoveAt(_indents.Count - 1);
                    Emit(TokenKind.Dedent, "", span);
                }
                if (_indents[_indents.Count - 1] != width)
                {
                    _diagnostics.Error(Codes.LexBadIndent, "unindent does not match any outer indentation level", span);
                }
            }
            return true;
        }

        private void SkipComment()
        {
            while (!AtEnd && !IsNewline(Cur)) _pos++;
        }

        private void Emit(TokenKind kind, string text, SourceSpan span, long intValue = 0, string? stringValue = null)
        {
            _tokens.Add(new Token(kind, text, span, intValue, stringValue));
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        // 식별자는 ASCII만 (spec-gaps G14). 파이썬 식별자의 부분집합이며, ASCII는 NFKC 정규화에도 변하지 않으므로
        // 나중에 파이썬 규칙(XID + NFKC)으로 넓혀도 기존 이름의 뜻이 바뀌지 않는다.
        private static bool IsIdentStart(char c) => c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        private static bool IsIdentPart(char c) => IsIdentStart(c) || IsDigit(c);

        /// <summary>파이썬이라면 식별자에 쓸 수 있었을 비 ASCII 문자 (오류 메시지를 고르는 데만 쓴다).</summary>
        private static bool IsNonAsciiNameChar(char c) => c > 0x7F && (char.IsLetterOrDigit(c) || char.IsSurrogate(c) ||
                                                                       char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark ||
                                                                       char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.SpacingCombiningMark);

        private void LexNameOrPrefixedString()
        {
            var span = Here;
            int start = _pos;
            bool nonAscii = false;
            while (!AtEnd && (IsIdentPart(Cur) || IsNonAsciiNameChar(Cur)))
            {
                if (!IsIdentPart(Cur)) nonAscii = true;
                _pos++;
            }
            string name = _text.Substring(start, _pos - start);

            if (nonAscii)
            {
                _diagnostics.Error(Codes.LexNonAsciiIdentifier, "identifier '" + name + "' contains non-ASCII characters",
                    span, hint: "identifiers must be ASCII; put display text in strings or text keys");
                Emit(TokenKind.Name, name, span);
                return;
            }

            if ((Cur == '"' || Cur == '\'') && IsStringPrefix(name))
            {
                LexString(span, name);
                return;
            }

            Emit(TokenKind.Name, name, span);
        }

        private static bool IsStringPrefix(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "u":
                case "r":
                case "b":
                case "f":
                case "rb":
                case "br":
                case "fr":
                case "rf":
                    return true;
                default:
                    return false;
            }
        }

        private void LexString(SourceSpan span, string? prefix)
        {
            if (prefix != null && !string.Equals(prefix, "u", System.StringComparison.OrdinalIgnoreCase))
            {
                _diagnostics.Error(Codes.LexUnsupportedString,
                    "string prefix '" + prefix + "' is not supported", span,
                    hint: prefix.IndexOf('f') >= 0 || prefix.IndexOf('F') >= 0 ? "f-strings are not supported yet" : null);
            }

            char quote = Cur;
            bool triple = Peek(1) == quote && Peek(2) == quote;
            int start = span.Col - 1 + _lineStart;
            _pos += triple ? 3 : 1;
            var sb = new StringBuilder();

            while (true)
            {
                if (AtEnd)
                {
                    _diagnostics.Error(Codes.LexUnterminatedString, "unterminated string literal", span);
                    break;
                }

                char c = Cur;
                if (triple)
                {
                    if (c == quote && Peek(1) == quote && Peek(2) == quote)
                    {
                        _pos += 3;
                        break;
                    }
                }
                else if (c == quote)
                {
                    _pos++;
                    break;
                }

                if (IsNewline(c))
                {
                    if (!triple)
                    {
                        _diagnostics.Error(Codes.LexUnterminatedString, "unterminated string literal", span);
                        break;
                    }
                    sb.Append('\n');
                    ConsumeNewline();
                    continue;
                }

                if (c == '\\')
                {
                    LexEscape(sb);
                    continue;
                }

                sb.Append(c);
                _pos++;
            }

            Emit(TokenKind.String, _text.Substring(start, _pos - start), span, stringValue: sb.ToString());
        }

        private void LexEscape(StringBuilder sb)
        {
            var span = Here;
            char e = Peek(1);
            if (IsNewline(e))
            {
                _pos++;
                ConsumeNewline();
                return;
            }

            _pos += 2;
            switch (e)
            {
                case '\\': sb.Append('\\'); return;
                case '\'': sb.Append('\''); return;
                case '"': sb.Append('"'); return;
                case 'n': sb.Append('\n'); return;
                case 'r': sb.Append('\r'); return;
                case 't': sb.Append('\t'); return;
                case '0' when !IsDigit(Cur): sb.Append('\0'); return;
                case 'x': AppendHex(sb, 2, span); return;
                case 'u': AppendHex(sb, 4, span); return;
                case 'U': AppendHex(sb, 8, span); return;
                default:
                    _diagnostics.Error(Codes.LexBadEscape, "unsupported escape sequence '\\" + e + "'", span);
                    return;
            }
        }

        private void AppendHex(StringBuilder sb, int digits, SourceSpan span)
        {
            int value = 0;
            for (int i = 0; i < digits; i++)
            {
                int d = HexValue(Cur);
                if (d < 0)
                {
                    _diagnostics.Error(Codes.LexBadEscape, "truncated escape sequence", span);
                    return;
                }
                value = value * 16 + d;
                _pos++;
            }

            if (value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF))
            {
                _diagnostics.Error(Codes.LexBadEscape, "invalid code point in escape sequence", span);
                return;
            }
            sb.Append(char.ConvertFromUtf32(value));
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private void LexNumber()
        {
            var span = Here;
            int start = _pos;

            if (Cur == '0' && (Peek(1) == 'x' || Peek(1) == 'X' || Peek(1) == 'o' || Peek(1) == 'O' ||
                               Peek(1) == 'b' || Peek(1) == 'B'))
            {
                _pos += 2;
                while (!AtEnd && IsIdentPart(Cur)) _pos++;
                _diagnostics.Error(Codes.LexBadNumber, "hex/octal/binary literals are not supported", span);
                Emit(TokenKind.Int, _text.Substring(start, _pos - start), span);
                return;
            }

            string intPart = ReadDigits(span);
            string? fracPart = null;
            if (Cur == '.' && !IsIdentStart(Peek(1)))
            {
                _pos++;
                fracPart = IsDigit(Cur) ? ReadDigits(span) : "";
            }

            if (Cur == 'e' || Cur == 'E' || Cur == 'j' || Cur == 'J' || IsIdentStart(Cur))
            {
                bool exponent = Cur == 'e' || Cur == 'E';
                while (!AtEnd && (IsIdentPart(Cur) || ((Cur == '+' || Cur == '-') && exponent))) _pos++;
                _diagnostics.Error(Codes.LexBadNumber,
                    exponent ? "exponent notation is not supported" : "invalid number literal", span);
                Emit(TokenKind.Int, _text.Substring(start, _pos - start), span);
                return;
            }

            string text = _text.Substring(start, _pos - start);

            if (fracPart == null)
            {
                if (intPart.Length > 1 && intPart[0] == '0' && intPart.TrimStart('0').Length > 0)
                {
                    _diagnostics.Error(Codes.LexBadNumber, "leading zeros in a decimal integer literal are not permitted", span);
                }
                Emit(TokenKind.Int, text, span, ParseDigits(intPart, span));
                return;
            }

            string frac = fracPart;
            if (frac.Length > Fixed.Decimals)
            {
                if (frac.Substring(Fixed.Decimals).TrimEnd('0').Length > 0)
                {
                    _diagnostics.Error(Codes.LexFixedPrecision,
                        "fixed literal has more than " + Fixed.Decimals.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " decimal places", span);
                }
                frac = frac.Substring(0, Fixed.Decimals);
            }
            frac = frac.PadRight(Fixed.Decimals, '0');

            long whole = intPart.Length == 0 ? 0 : ParseDigits(intPart, span);
            long fracValue = ParseDigits(frac, span);
            long raw;
            try
            {
                raw = checked(whole * Fixed.Scale + fracValue);
            }
            catch (System.OverflowException)
            {
                _diagnostics.Error(Codes.LexIntOverflow, "fixed literal is out of range", span);
                raw = 0;
            }
            Emit(TokenKind.Fixed, text, span, raw);
        }

        /// <summary>숫자와 밑줄 구분자를 읽고 밑줄을 뺀 숫자열을 돌려준다.</summary>
        private string ReadDigits(SourceSpan span)
        {
            var sb = new StringBuilder();
            bool lastUnderscore = false;
            bool any = false;
            while (!AtEnd && (IsDigit(Cur) || Cur == '_'))
            {
                if (Cur == '_')
                {
                    if (!any || lastUnderscore)
                        _diagnostics.Error(Codes.LexBadNumber, "invalid underscore in number literal", span);
                    lastUnderscore = true;
                }
                else
                {
                    sb.Append(Cur);
                    lastUnderscore = false;
                    any = true;
                }
                _pos++;
            }
            if (lastUnderscore) _diagnostics.Error(Codes.LexBadNumber, "invalid underscore in number literal", span);
            return sb.ToString();
        }

        private long ParseDigits(string digits, SourceSpan span)
        {
            long value = 0;
            try
            {
                foreach (char ch in digits) value = checked(value * 10 + (ch - '0'));
            }
            catch (System.OverflowException)
            {
                _diagnostics.Error(Codes.LexIntOverflow, "integer literal is out of 64-bit range", span);
                return 0;
            }
            return value;
        }

        private bool LexOperator()
        {
            foreach (var op in Operators)
            {
                if (string.CompareOrdinal(_text, _pos, op, 0, op.Length) != 0) continue;
                var span = Here;
                _pos += op.Length;
                var token = new Token(TokenKind.Op, op, span);
                _tokens.Add(token);
                TrackBrackets(token);
                return true;
            }
            return false;
        }

        private void TrackBrackets(Token token)
        {
            switch (token.Text)
            {
                case "(":
                case "[":
                case "{":
                    _openers.Add(token);
                    return;
                case ")":
                case "]":
                case "}":
                    string expected = token.Text == ")" ? "(" : token.Text == "]" ? "[" : "{";
                    if (_openers.Count == 0)
                    {
                        _diagnostics.Error(Codes.LexUnbalanced, "unmatched '" + token.Text + "'", token.Span);
                        return;
                    }
                    var top = _openers[_openers.Count - 1];
                    if (top.Text != expected)
                    {
                        _diagnostics.Error(Codes.LexUnbalanced,
                            "closing '" + token.Text + "' does not match opening '" + top.Text + "'", token.Span,
                            related: new[] { top.Span });
                    }
                    _openers.RemoveAt(_openers.Count - 1);
                    return;
            }
        }
    }
}
