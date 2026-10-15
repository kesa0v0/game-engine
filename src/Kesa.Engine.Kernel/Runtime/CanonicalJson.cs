using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 저장 파일의 JSON 노드 (spec S8, spec-gaps G140). 수는 정수(<c>long</c>)뿐이다: 부동소수점을 거치지 않는다(P2).
    /// 객체의 키는 적은 순서 그대로다. 정규형(키 순서)은 쓰는 쪽이 정한다.
    /// </summary>
    internal abstract class JsonNode
    {
        public virtual string Kind => GetType().Name;
    }

    internal sealed class JsonNull : JsonNode
    {
        public static readonly JsonNull Instance = new JsonNull();
        public override string Kind => "null";
    }

    internal sealed class JsonBool : JsonNode
    {
        public JsonBool(bool value) => Value = value;
        public bool Value { get; }
        public override string Kind => "bool";
    }

    internal sealed class JsonNumber : JsonNode
    {
        public JsonNumber(long value) => Value = value;
        public long Value { get; }
        public override string Kind => "number";
    }

    internal sealed class JsonString : JsonNode
    {
        public JsonString(string value) => Value = value;
        public string Value { get; }
        public override string Kind => "string";
    }

    internal sealed class JsonArray : JsonNode
    {
        public List<JsonNode> Items { get; } = new List<JsonNode>();
        public JsonArray Add(JsonNode item)
        {
            Items.Add(item);
            return this;
        }
        public override string Kind => "array";
    }

    internal sealed class JsonObject : JsonNode
    {
        public List<KeyValuePair<string, JsonNode>> Members { get; } = new List<KeyValuePair<string, JsonNode>>();

        public JsonObject Add(string key, JsonNode value)
        {
            Members.Add(new KeyValuePair<string, JsonNode>(key, value));
            return this;
        }

        public JsonNode? Get(string key)
        {
            foreach (var m in Members)
                if (m.Key == key) return m.Value;
            return null;
        }

        public override string Kind => "object";
    }

    /// <summary>저장 파일이 깨졌거나 이 엔진의 형식이 아니다.</summary>
    internal sealed class JsonFormatException : Exception
    {
        public JsonFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// 정규형 JSON 쓰기와 읽기. 쓰기는 들여쓰기 두 칸, 줄바꿈 <c>\n</c>, 키 뒤 <c>": "</c>로 고정이고 같은 노드는 같은 바이트가 된다 (C4).
    /// 문자열은 따옴표·역슬래시·제어 문자만 이스케이프하고 나머지(한글 포함)는 UTF-8 그대로 쓴다.
    /// </summary>
    internal static class CanonicalJson
    {
        public static byte[] Write(JsonNode node)
        {
            var sb = new StringBuilder();
            Write(sb, node, 0);
            sb.Append('\n');
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        private static void Write(StringBuilder sb, JsonNode node, int depth)
        {
            switch (node)
            {
                case JsonNull _:
                    sb.Append("null");
                    return;
                case JsonBool b:
                    sb.Append(b.Value ? "true" : "false");
                    return;
                case JsonNumber n:
                    sb.Append(n.Value.ToString(CultureInfo.InvariantCulture));
                    return;
                case JsonString s:
                    WriteString(sb, s.Value);
                    return;
                case JsonArray a:
                    if (a.Items.Count == 0)
                    {
                        sb.Append("[]");
                        return;
                    }
                    sb.Append("[\n");
                    for (int i = 0; i < a.Items.Count; i++)
                    {
                        Indent(sb, depth + 1);
                        Write(sb, a.Items[i], depth + 1);
                        sb.Append(i + 1 < a.Items.Count ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append(']');
                    return;
                case JsonObject o:
                    if (o.Members.Count == 0)
                    {
                        sb.Append("{}");
                        return;
                    }
                    sb.Append("{\n");
                    for (int i = 0; i < o.Members.Count; i++)
                    {
                        Indent(sb, depth + 1);
                        WriteString(sb, o.Members[i].Key);
                        sb.Append(": ");
                        Write(sb, o.Members[i].Value, depth + 1);
                        sb.Append(i + 1 < o.Members.Count ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append('}');
                    return;
            }
            throw new InvalidOperationException("unknown JSON node " + node.Kind);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
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

        public static JsonNode Read(byte[] bytes)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                throw new JsonFormatException("the save is not UTF-8 text");
            }
            int pos = 0;
            var node = Parse(text, ref pos);
            SkipSpace(text, ref pos);
            if (pos != text.Length) throw Fail(text, pos, "unexpected text after the end");
            return node;
        }

        private static JsonFormatException Fail(string text, int pos, string message)
        {
            int line = 1;
            for (int i = 0; i < pos && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return new JsonFormatException("the save is not valid JSON (line " + line.ToString(CultureInfo.InvariantCulture) + "): " + message);
        }

        private static void SkipSpace(string text, ref int pos)
        {
            while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\n' || text[pos] == '\r' || text[pos] == '\t')) pos++;
        }

        private static JsonNode Parse(string text, ref int pos)
        {
            SkipSpace(text, ref pos);
            if (pos >= text.Length) throw Fail(text, pos, "unexpected end");
            char c = text[pos];
            switch (c)
            {
                case '{':
                {
                    pos++;
                    var obj = new JsonObject();
                    SkipSpace(text, ref pos);
                    if (pos < text.Length && text[pos] == '}')
                    {
                        pos++;
                        return obj;
                    }
                    while (true)
                    {
                        SkipSpace(text, ref pos);
                        if (pos >= text.Length || text[pos] != '"') throw Fail(text, pos, "expected a key");
                        string key = ParseString(text, ref pos);
                        SkipSpace(text, ref pos);
                        if (pos >= text.Length || text[pos] != ':') throw Fail(text, pos, "expected ':'");
                        pos++;
                        obj.Add(key, Parse(text, ref pos));
                        SkipSpace(text, ref pos);
                        if (pos < text.Length && text[pos] == ',')
                        {
                            pos++;
                            continue;
                        }
                        if (pos < text.Length && text[pos] == '}')
                        {
                            pos++;
                            return obj;
                        }
                        throw Fail(text, pos, "expected ',' or '}'");
                    }
                }
                case '[':
                {
                    pos++;
                    var array = new JsonArray();
                    SkipSpace(text, ref pos);
                    if (pos < text.Length && text[pos] == ']')
                    {
                        pos++;
                        return array;
                    }
                    while (true)
                    {
                        array.Add(Parse(text, ref pos));
                        SkipSpace(text, ref pos);
                        if (pos < text.Length && text[pos] == ',')
                        {
                            pos++;
                            continue;
                        }
                        if (pos < text.Length && text[pos] == ']')
                        {
                            pos++;
                            return array;
                        }
                        throw Fail(text, pos, "expected ',' or ']'");
                    }
                }
                case '"':
                    return new JsonString(ParseString(text, ref pos));
            }
            if (Word(text, ref pos, "null")) return JsonNull.Instance;
            if (Word(text, ref pos, "true")) return new JsonBool(true);
            if (Word(text, ref pos, "false")) return new JsonBool(false);
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = pos;
                if (c == '-') pos++;
                while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9') pos++;
                if (pos < text.Length && (text[pos] == '.' || text[pos] == 'e' || text[pos] == 'E'))
                    throw Fail(text, pos, "only whole numbers are allowed");
                if (!long.TryParse(text.Substring(start, pos - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                    throw Fail(text, start, "the number is out of range");
                return new JsonNumber(n);
            }
            throw Fail(text, pos, "unexpected '" + c + "'");
        }

        private static bool Word(string text, ref int pos, string word)
        {
            if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) return false;
            pos += word.Length;
            return true;
        }

        private static string ParseString(string text, ref int pos)
        {
            pos++; // 여는 따옴표
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= text.Length) throw Fail(text, pos, "unterminated string");
                char c = text[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (pos >= text.Length) throw Fail(text, pos, "unterminated string");
                char e = text[pos++];
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
                        if (pos + 4 > text.Length ||
                            !int.TryParse(text.Substring(pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw Fail(text, pos, "bad \\u escape");
                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default:
                        throw Fail(text, pos, "bad escape '\\" + e + "'");
                }
            }
        }
    }
}
