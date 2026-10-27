using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>UXML 속성의 식 (spec-gaps G332·G344): 모드 함수 호출 하나나 변수 하나. 호출 중첩과 연산자는 없다.</summary>
    public abstract class UiExpr
    {
    }

    /// <summary>변수와, 사전의 문자열 키를 점으로 읽는 길(<c>row.stats.hp</c>): <see cref="Name"/>이 변수, <see cref="Keys"/>가 키들.</summary>
    public sealed class VarExpr : UiExpr
    {
        public VarExpr(string name, IReadOnlyList<string> keys)
        {
            Name = name;
            Keys = keys;
        }

        public string Name { get; }
        public IReadOnlyList<string> Keys { get; }

        public override string ToString() => Keys.Count == 0 ? Name : Name + "." + string.Join(".", Keys);
    }

    /// <summary>호출의 인자로만 쓰는 고정값: 숫자, 작은따옴표 글자, <c>True</c>·<c>False</c>·<c>None</c>.</summary>
    public sealed class LiteralExpr : UiExpr
    {
        public LiteralExpr(BridgeValue value)
        {
            Value = value;
        }

        public BridgeValue Value { get; }

        public override string ToString() => Value.ToString();
    }

    /// <summary><c>모드.함수(인자, …)</c>. 인자는 <see cref="VarExpr"/>나 <see cref="LiteralExpr"/>이다.</summary>
    public sealed class CallExpr : UiExpr
    {
        public CallExpr(string function, IReadOnlyList<UiExpr> arguments)
        {
            Function = function;
            Arguments = arguments;
        }

        public string Function { get; }
        public IReadOnlyList<UiExpr> Arguments { get; }

        public override string ToString() => Function + "(" + string.Join(", ", Arguments) + ")";
    }

    /// <summary>식을 읽다가 틀린 곳. <see cref="Column"/>은 0부터 센 글자 위치다.</summary>
    public sealed class UiSyntaxException : Exception
    {
        public UiSyntaxException(string message, int column) : base(message + " (at column " + (column + 1) + ")")
        {
            Column = column;
        }

        public int Column { get; }
    }

    /// <summary>식 파서 (G332·G344). 속성 값은 XML 큰따옴표 안이라 글자 고정값은 작은따옴표다.</summary>
    public static class UiExprParser
    {
        private const long FixedScale = BridgeFixed.Scale;

        /// <summary>틀리면 <see cref="UiSyntaxException"/>.</summary>
        public static UiExpr Parse(string text)
        {
            var p = new Parser(text);
            var expr = p.ParseTop();
            return expr;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
            }

            public UiExpr ParseTop()
            {
                SkipSpace();
                int start = _i;
                var path = ReadPath();
                if (path == null)
                {
                    if (Peek() == '\0') throw Fail("expected a function call or a variable, got an empty value");
                    throw Fail("expected a function call or a variable");
                }
                SkipSpace();
                UiExpr result;
                if (Peek() == '(')
                {
                    result = ParseCall(path, start);
                }
                else
                {
                    if (IsLiteralName(path)) throw FailAt("a binding needs a function call or a variable, not the value '" + string.Join(".", path) + "'", start);
                    result = new VarExpr(path[0], Rest(path));
                }
                SkipSpace();
                if (Peek() != '\0') throw Fail("unexpected '" + Peek() + "'; use one function call or one variable (nesting and operators are not allowed)");
                return result;
            }

            private UiExpr ParseCall(List<string> path, int start)
            {
                if (path.Count < 2) throw FailAt("a function is written 'mod.function(...)', got '" + path[0] + "'", start);
                Expect('(');
                var args = new List<UiExpr>();
                SkipSpace();
                if (Peek() == ')')
                {
                    _i++;
                    return new CallExpr(string.Join(".", path), args);
                }
                while (true)
                {
                    args.Add(ParseArgument());
                    SkipSpace();
                    if (Peek() == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (Peek() == ')')
                    {
                        _i++;
                        return new CallExpr(string.Join(".", path), args);
                    }
                    throw Fail("expected ',' or ')'");
                }
            }

            private UiExpr ParseArgument()
            {
                SkipSpace();
                char c = Peek();
                if (c == '\'') return new LiteralExpr(new BridgeStr(ReadString()));
                if (c == '-' || (c >= '0' && c <= '9')) return new LiteralExpr(ReadNumber());
                int start = _i;
                var path = ReadPath();
                if (path == null) throw Fail("expected an argument (a variable or a value)");
                SkipSpace();
                if (Peek() == '(') throw FailAt("a call cannot be an argument of a call; make one function do it", start);
                if (path.Count == 1)
                {
                    if (path[0] == "True") return new LiteralExpr(BridgeBool.True);
                    if (path[0] == "False") return new LiteralExpr(BridgeBool.False);
                    if (path[0] == "None") return new LiteralExpr(BridgeNone.Instance);
                }
                return new VarExpr(path[0], Rest(path));
            }

            private static bool IsLiteralName(List<string> path) =>
                path.Count == 1 && (path[0] == "True" || path[0] == "False" || path[0] == "None");

            private static List<string> Rest(List<string> path) => path.GetRange(1, path.Count - 1);

            private List<string>? ReadPath()
            {
                if (!IsIdentStart(Peek())) return null;
                var parts = new List<string>();
                while (true)
                {
                    int start = _i;
                    while (IsIdentPart(Peek())) _i++;
                    parts.Add(_s.Substring(start, _i - start));
                    if (Peek() == '.' && IsIdentStart(PeekAt(_i + 1)))
                    {
                        _i++;
                        continue;
                    }
                    return parts;
                }
            }

            private string ReadString()
            {
                int start = _i;
                _i++;
                int begin = _i;
                while (_i < _s.Length && _s[_i] != '\'') _i++;
                if (_i >= _s.Length) throw FailAt("the quoted text is not closed", start);
                string value = _s.Substring(begin, _i - begin);
                _i++;
                return value;
            }

            private BridgeValue ReadNumber()
            {
                int start = _i;
                bool negative = false;
                if (Peek() == '-')
                {
                    negative = true;
                    _i++;
                }
                int digitsAt = _i;
                while (Peek() >= '0' && Peek() <= '9') _i++;
                if (_i == digitsAt) throw FailAt("expected a number", start);
                string whole = _s.Substring(digitsAt, _i - digitsAt);
                string fraction = "";
                if (Peek() == '.' && PeekAt(_i + 1) >= '0' && PeekAt(_i + 1) <= '9')
                {
                    _i++;
                    int fractionAt = _i;
                    while (Peek() >= '0' && Peek() <= '9') _i++;
                    fraction = _s.Substring(fractionAt, _i - fractionAt);
                }
                if (!long.TryParse(whole, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long w))
                    throw FailAt("the number is too large", start);
                if (fraction.Length == 0) return new BridgeInt(negative ? -w : w);
                if (fraction.Length > 6) throw FailAt("a decimal has at most 6 digits after the point", start);
                long f = long.Parse(fraction.PadRight(6, '0'), System.Globalization.CultureInfo.InvariantCulture);
                long raw;
                try
                {
                    raw = checked(w * FixedScale + f);
                }
                catch (OverflowException)
                {
                    throw FailAt("the number is too large", start);
                }
                return new BridgeFixed(negative ? -raw : raw);
            }

            private void Expect(char c)
            {
                if (Peek() != c) throw Fail("expected '" + c + "'");
                _i++;
            }

            private void SkipSpace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private char Peek() => PeekAt(_i);

            private char PeekAt(int i) => i < _s.Length ? _s[i] : '\0';

            private static bool IsIdentStart(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';

            private static bool IsIdentPart(char c) => IsIdentStart(c) || (c >= '0' && c <= '9');

            private UiSyntaxException Fail(string message) => new UiSyntaxException(message, _i);

            private static UiSyntaxException FailAt(string message, int column) => new UiSyntaxException(message, column);
        }
    }
}
