using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kesa.Engine.Reporting
{
    /// <summary>
    /// 덤프용 최소 JSON 값. 객체의 키 순서는 넣은 순서이고, 같은 구조면 같은 문자열이 나온다.
    /// 라이브러리를 쓰지 않는다(Unity 소스 패키지, P1a).
    /// </summary>
    internal abstract class JsonNode
    {
        public static readonly JsonNode Null = new JsonLiteral("null");
        public static readonly JsonNode True = new JsonLiteral("true");
        public static readonly JsonNode False = new JsonLiteral("false");

        public static JsonNode Bool(bool value) => value ? True : False;
        public static JsonNode Number(long value) => new JsonLiteral(value.ToString(CultureInfo.InvariantCulture));
        public static JsonNode String(string? value) => value == null ? Null : new JsonLiteral(CheckReport.Quote(value));

        /// <summary>한 줄에 쓸 수 있는 값 (스칼라)인가.</summary>
        public abstract bool IsScalar { get; }

        public abstract void Write(StringBuilder sb, int indent);

        public string ToText()
        {
            var sb = new StringBuilder();
            Write(sb, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        protected static void Indent(StringBuilder sb, int indent) => sb.Append(' ', indent);
    }

    internal sealed class JsonLiteral : JsonNode
    {
        private readonly string _text;
        public JsonLiteral(string text) => _text = text;
        public override bool IsScalar => true;
        public override void Write(StringBuilder sb, int indent) => sb.Append(_text);
    }

    internal sealed class JsonArray : JsonNode
    {
        private readonly List<JsonNode> _items = new List<JsonNode>();

        public JsonArray Add(JsonNode item)
        {
            _items.Add(item);
            return this;
        }

        public int Count => _items.Count;
        public override bool IsScalar => false;

        public override void Write(StringBuilder sb, int indent)
        {
            if (_items.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            bool inline = true;
            foreach (var item in _items)
                if (!item.IsScalar) inline = false;
            if (inline)
            {
                sb.Append('[');
                for (int i = 0; i < _items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    _items[i].Write(sb, indent);
                }
                sb.Append(']');
                return;
            }
            sb.Append("[\n");
            for (int i = 0; i < _items.Count; i++)
            {
                Indent(sb, indent + 2);
                _items[i].Write(sb, indent + 2);
                sb.Append(i + 1 < _items.Count ? ",\n" : "\n");
            }
            Indent(sb, indent);
            sb.Append(']');
        }
    }

    internal sealed class JsonObject : JsonNode
    {
        private readonly List<KeyValuePair<string, JsonNode>> _pairs = new List<KeyValuePair<string, JsonNode>>();

        public JsonObject Add(string key, JsonNode value)
        {
            _pairs.Add(new KeyValuePair<string, JsonNode>(key, value));
            return this;
        }

        public int Count => _pairs.Count;
        public override bool IsScalar => false;

        public override void Write(StringBuilder sb, int indent)
        {
            if (_pairs.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append("{\n");
            for (int i = 0; i < _pairs.Count; i++)
            {
                Indent(sb, indent + 2);
                sb.Append(CheckReport.Quote(_pairs[i].Key)).Append(": ");
                _pairs[i].Value.Write(sb, indent + 2);
                sb.Append(i + 1 < _pairs.Count ? ",\n" : "\n");
            }
            Indent(sb, indent);
            sb.Append('}');
        }
    }
}
