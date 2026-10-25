using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// 경계 값의 정규형 JSON (spec-gaps G326). 저장 형식의 값 규칙(<c>World.Save</c>)에 엔티티의 Def와 숨김을 더한 것이다:
    /// <c>null</c>, bool, 수(int), 문자열, 배열(리스트), <c>{"fixed": raw}</c>, <c>{"entity": id, "def": "모드:Def"}</c>(Def를 모르면
    /// <c>{"entity": id}</c>), <c>{"hidden": true}</c>, <c>{"dict": [[키, 값], ...]}</c>(넣은 순), <c>{"text": 키, "args": {...}}</c>(인자 이름 순),
    /// <c>{"option": 값, "label": …, "desc": …, "style": …, "data": …}</c>. 빈칸 없이 쓰고 같은 값은 같은 글자다.
    /// </summary>
    public static class BridgeJson
    {
        public static string Write(BridgeValue value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, BridgeValue value)
        {
            switch (value)
            {
                case BridgeNone _: sb.Append("null"); break;
                case BridgeBool b: sb.Append(b.Value ? "true" : "false"); break;
                case BridgeInt i: sb.Append(i.Value.ToString(CultureInfo.InvariantCulture)); break;
                case BridgeStr s: Quote(sb, s.Value); break;
                case BridgeFixed f: sb.Append("{\"fixed\":").Append(f.Raw.ToString(CultureInfo.InvariantCulture)).Append('}'); break;
                case BridgeHidden _: sb.Append("{\"hidden\":true}"); break;
                case BridgeEntity e:
                    sb.Append("{\"entity\":").Append(e.Id.ToString(CultureInfo.InvariantCulture));
                    if (e.Def != null)
                    {
                        sb.Append(",\"def\":");
                        Quote(sb, e.Def);
                    }
                    sb.Append('}');
                    break;
                case BridgeList l:
                    sb.Append('[');
                    for (int k = 0; k < l.Items.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        Write(sb, l.Items[k]);
                    }
                    sb.Append(']');
                    break;
                case BridgeDict d:
                    sb.Append("{\"dict\":[");
                    for (int k = 0; k < d.Items.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append('[');
                        Write(sb, d.Items[k].Key);
                        sb.Append(',');
                        Write(sb, d.Items[k].Value);
                        sb.Append(']');
                    }
                    sb.Append("]}");
                    break;
                case BridgeText t:
                    sb.Append("{\"text\":");
                    Quote(sb, t.Key);
                    sb.Append(",\"args\":{");
                    for (int k = 0; k < t.Args.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        Quote(sb, t.Args[k].Key);
                        sb.Append(':');
                        Write(sb, t.Args[k].Value);
                    }
                    sb.Append("}}");
                    break;
                case BridgeOption o:
                    sb.Append("{\"option\":");
                    Write(sb, o.Value);
                    sb.Append(",\"label\":");
                    Optional(sb, o.Label);
                    sb.Append(",\"desc\":");
                    Optional(sb, o.Desc);
                    sb.Append(",\"style\":");
                    if (o.Style == null) sb.Append("null");
                    else Quote(sb, o.Style);
                    sb.Append(",\"data\":");
                    Optional(sb, o.Data);
                    sb.Append('}');
                    break;
                default:
                    throw new InvalidOperationException("unknown bridge value " + value.Kind);
            }
        }

        private static void Optional(StringBuilder sb, BridgeValue? value)
        {
            if (value == null) sb.Append("null");
            else Write(sb, value);
        }

        /// <summary>커널 보고서와 같은 이스케이프: <c>"</c>, <c>\</c>, 줄바꿈·탭, 그 밖의 제어 문자는 <c>\u00XX</c>.</summary>
        private static void Quote(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- 읽기 ----

        /// <summary>정규형이 아니어도(빈칸 등) 읽는다. 모양이 틀리면 <see cref="FormatException"/>(위치와 함께).</summary>
        public static BridgeValue Read(string json)
        {
            var reader = new Reader(json);
            var value = reader.Value();
            reader.SkipSpace();
            if (!reader.AtEnd) throw reader.Error("unexpected text after the value");
            return value;
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _at;

            public Reader(string text) => _text = text;

            public bool AtEnd => _at >= _text.Length;

            public FormatException Error(string message) =>
                new FormatException(message + " at " + _at.ToString(CultureInfo.InvariantCulture));

            public void SkipSpace()
            {
                while (!AtEnd && (_text[_at] == ' ' || _text[_at] == '\n' || _text[_at] == '\r' || _text[_at] == '\t')) _at++;
            }

            private char Peek()
            {
                SkipSpace();
                if (AtEnd) throw Error("unexpected end");
                return _text[_at];
            }

            private void Expect(char c)
            {
                if (Peek() != c) throw Error("expected '" + c + "'");
                _at++;
            }

            private bool Take(char c)
            {
                if (Peek() != c) return false;
                _at++;
                return true;
            }

            private bool Word(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0) return false;
                _at += word.Length;
                return true;
            }

            public BridgeValue Value()
            {
                char c = Peek();
                if (c == '"') return new BridgeStr(String());
                if (c == '[')
                {
                    var items = new List<BridgeValue>();
                    _at++;
                    if (!Take(']'))
                    {
                        do items.Add(Value()); while (Take(','));
                        Expect(']');
                    }
                    return new BridgeList(items);
                }
                if (c == '{') return Tagged();
                if (Word("null")) return BridgeNone.Instance;
                if (Word("true")) return BridgeBool.True;
                if (Word("false")) return BridgeBool.False;
                if (c == '-' || (c >= '0' && c <= '9')) return new BridgeInt(Number());
                throw Error("expected a value");
            }

            private long Number()
            {
                int start = _at;
                if (_text[_at] == '-') _at++;
                while (!AtEnd && _text[_at] >= '0' && _text[_at] <= '9') _at++;
                if (!AtEnd && (_text[_at] == '.' || _text[_at] == 'e' || _text[_at] == 'E'))
                    throw Error("only whole numbers (a fixed value is {\"fixed\": raw})");
                if (!long.TryParse(_text.Substring(start, _at - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
                    throw Error("a number out of range");
                return n;
            }

            private string String()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = _text[_at++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("unterminated string");
                    char e = _text[_at++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_at + 4 > _text.Length ||
                                !int.TryParse(_text.Substring(_at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Error("a bad \\u escape");
                            sb.Append((char)code);
                            _at += 4;
                            break;
                        default: throw Error("a bad escape");
                    }
                }
            }

            /// <summary>객체는 모두 태그가 붙은 값이다. 첫 칸의 이름이 종류를 정한다.</summary>
            private BridgeValue Tagged()
            {
                Expect('{');
                string tag = String();
                Expect(':');
                BridgeValue result;
                switch (tag)
                {
                    case "fixed":
                        result = new BridgeFixed(NumberValue());
                        break;
                    case "hidden":
                        if (!Word("true")) throw Error("expected {\"hidden\": true}");
                        result = BridgeHidden.Instance;
                        break;
                    case "entity":
                        long id = NumberValue();
                        string? def = null;
                        if (Take(','))
                        {
                            Field("def");
                            def = String();
                        }
                        result = new BridgeEntity(id, def);
                        break;
                    case "dict":
                        var items = new List<KeyValuePair<BridgeValue, BridgeValue>>();
                        Expect('[');
                        if (!Take(']'))
                        {
                            do
                            {
                                Expect('[');
                                var key = Value();
                                if (key.Kind != BridgeKind.Str && key.Kind != BridgeKind.Int) throw Error("a dict key must be a string or an int");
                                Expect(',');
                                items.Add(new KeyValuePair<BridgeValue, BridgeValue>(key, Value()));
                                Expect(']');
                            } while (Take(','));
                            Expect(']');
                        }
                        result = new BridgeDict(items);
                        break;
                    case "text":
                        string key2 = String();
                        Expect(',');
                        Field("args");
                        var args = new List<KeyValuePair<string, BridgeValue>>();
                        Expect('{');
                        if (!Take('}'))
                        {
                            do
                            {
                                string name = String();
                                Expect(':');
                                args.Add(new KeyValuePair<string, BridgeValue>(name, Value()));
                            } while (Take(','));
                            Expect('}');
                        }
                        result = new BridgeText(key2, args);
                        break;
                    case "option":
                        var value = Value();
                        Expect(',');
                        Field("label");
                        var label = OptionalValue();
                        Expect(',');
                        Field("desc");
                        var desc = OptionalValue();
                        Expect(',');
                        Field("style");
                        string? style = Word("null") ? null : String();
                        Expect(',');
                        Field("data");
                        var data = OptionalValue();
                        result = new BridgeOption(value, label, desc, style, data);
                        break;
                    default:
                        throw Error("unknown value tag '" + tag + "'");
                }
                Expect('}');
                return result;
            }

            private long NumberValue()
            {
                Peek();
                return Number();
            }

            private BridgeValue? OptionalValue()
            {
                Peek();
                return Word("null") ? null : Value();
            }

            private void Field(string name)
            {
                if (String() != name) throw Error("expected the field '" + name + "'");
                Expect(':');
            }
        }
    }
}
