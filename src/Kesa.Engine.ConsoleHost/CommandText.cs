using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 번호 대신 적어 내는 커맨드 한 줄 <c>[연결:] 이름(인자=값, ...)</c> (G492). 값은 파이썬 리터럴 꼴에 엔티티 <c>#id</c>를 더한 것이다.
    /// </summary>
    public sealed class CommandText
    {
        private CommandText(string? seat, string name, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            Seat = seat;
            Name = name;
            Arguments = arguments;
        }

        /// <summary>앞에 적은 연결. 없으면 null.</summary>
        public string? Seat { get; }

        public string Name { get; }

        public IReadOnlyDictionary<string, BridgeValue> Arguments { get; }

        /// <summary>읽지 못하면 null과 이유(영어, 개발자용).</summary>
        public static CommandText? Parse(string line, out string error)
        {
            var reader = new Reader(line);
            try
            {
                var command = reader.Line();
                error = "";
                return command;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s;

            public CommandText Line()
            {
                string? seat = null;
                Space();
                string name = Name("a command name");
                Space();
                if (Peek() == ':')
                {
                    _i++;
                    seat = name;
                    Space();
                    name = Name("a command name");
                    Space();
                }
                Expect('(');
                var arguments = new Dictionary<string, BridgeValue>(StringComparer.Ordinal);
                Space();
                if (Peek() != ')')
                {
                    while (true)
                    {
                        Space();
                        string key = Name("an argument name");
                        Space();
                        Expect('=');
                        if (arguments.ContainsKey(key)) throw Fail("'" + key + "' is given twice");
                        arguments[key] = Value();
                        Space();
                        if (Peek() == ',')
                        {
                            _i++;
                            Space();
                            if (Peek() == ')') break;
                            continue;
                        }
                        break;
                    }
                }
                Expect(')');
                Space();
                if (_i < _s.Length) throw Fail("unexpected '" + _s.Substring(_i) + "' after ')'");
                return new CommandText(seat, name, arguments);
            }

            private BridgeValue Value()
            {
                Space();
                char c = Peek();
                if (c == '[')
                {
                    _i++;
                    var items = new List<BridgeValue>();
                    Items(']', () => items.Add(Value()));
                    return new BridgeList(items);
                }
                if (c == '{')
                {
                    _i++;
                    var items = new List<KeyValuePair<BridgeValue, BridgeValue>>();
                    Items('}', () =>
                    {
                        var key = Value();
                        Space();
                        Expect(':');
                        items.Add(new KeyValuePair<BridgeValue, BridgeValue>(key, Value()));
                    });
                    return new BridgeDict(items);
                }
                if (c == '"' || c == '\'') return BridgeValue.Of(Quoted(c));
                if (c == '#')
                {
                    _i++;
                    if (!char.IsDigit(Peek())) throw Fail("an entity is # and its id, like #12");
                    return new BridgeEntity(Digits(), null);
                }
                if (c == '-' || char.IsDigit(c)) return Number();
                string word = Name("a value");
                switch (word)
                {
                    case "True": return BridgeValue.Of(true);
                    case "False": return BridgeValue.Of(false);
                    case "None": return BridgeNone.Instance;
                }
                throw Fail("'" + word + "' is not a value; quote a string: \"" + word + "\"");
            }

            private void Items(char close, Action item)
            {
                Space();
                if (Peek() == close)
                {
                    _i++;
                    return;
                }
                while (true)
                {
                    item();
                    Space();
                    if (Peek() == ',')
                    {
                        _i++;
                        Space();
                        if (Peek() == close) break;
                        continue;
                    }
                    break;
                }
                Expect(close);
            }

            /// <summary>정수는 int, 점이 있으면 소수 여섯 자리까지의 fixed.</summary>
            private BridgeValue Number()
            {
                bool negative = Peek() == '-';
                if (negative) _i++;
                if (!char.IsDigit(Peek())) throw Fail("a number is expected after '-'");
                long whole = Digits();
                if (Peek() != '.') return BridgeValue.Of(negative ? -whole : whole);
                _i++;
                int start = _i;
                long fraction = char.IsDigit(Peek()) ? Digits() : 0;
                int places = _i - start;
                if (places > 6) throw Fail("a fixed value has at most 6 decimal places");
                for (int p = places; p < 6; p++) fraction *= 10;
                long raw;
                try
                {
                    raw = checked(whole * 1_000_000 + fraction);
                }
                catch (OverflowException)
                {
                    throw Fail("the number is too large");
                }
                return new BridgeFixed(negative ? -raw : raw);
            }

            private long Digits()
            {
                int start = _i;
                while (char.IsDigit(Peek())) _i++;
                if (!long.TryParse(_s.Substring(start, _i - start), NumberStyles.None, CultureInfo.InvariantCulture, out long n))
                    throw Fail("the number is too large");
                return n;
            }

            private string Quoted(char quote)
            {
                _i++;
                var text = new System.Text.StringBuilder();
                while (_i < _s.Length && _s[_i] != quote)
                {
                    if (_s[_i] == '\\' && _i + 1 < _s.Length) _i++;
                    text.Append(_s[_i++]);
                }
                if (_i >= _s.Length) throw Fail("the string has no closing " + quote);
                _i++;
                return text.ToString();
            }

            private string Name(string what)
            {
                int start = _i;
                // 커맨드는 모드 id가 붙고(skirmish.Start) 연결 이름에는 하이픈이 올 수 있다
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || (_s[_i] == '-' || _s[_i] == '.') && _i > start)) _i++;
                if (_i == start || char.IsDigit(_s[start])) throw Fail(what + " is expected at '" + Rest() + "'");
                return _s.Substring(start, _i - start);
            }

            private void Expect(char c)
            {
                Space();
                if (Peek() != c) throw Fail("'" + c + "' is expected at '" + Rest() + "'");
                _i++;
            }

            private void Space()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private char Peek() => _i < _s.Length ? _s[_i] : '\0';

            private string Rest() => _i < _s.Length ? _s.Substring(_i) : "end of line";

            private static FormatException Fail(string message) => new FormatException(message);
        }
    }
}
