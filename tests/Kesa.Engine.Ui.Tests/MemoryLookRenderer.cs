using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>판 위 그림을 메모리 트리로 만드는 시험용 렌더러 (G412). <see cref="Dump"/>가 물체 하나를 글로 보인다.</summary>
    public sealed class MemoryLookRenderer : ILookRenderer
    {
        public sealed class Node
        {
            public string Kind = "";
            public long? Parent;
            public readonly List<long> Children = new List<long>();
            public readonly SortedDictionary<string, string> Attributes = new SortedDictionary<string, string>();
            public bool Visible = true;
            public bool Missing;
            public LookPosition? Position;
            public LookFrame Frame;
            public long Opacity = Bridge.BridgeFixed.Scale;
            public long Unit;
        }

        private long _next;
        public readonly Dictionary<long, Node> Nodes = new Dictionary<long, Node>();
        public readonly SortedDictionary<string, long> Objects = new SortedDictionary<string, long>();

        /// <summary>렌더러가 받은 호출 수(바뀐 것만 넣는지 본다).</summary>
        public int Sets;

        public ElementId CreateObject(string key)
        {
            long id = ++_next;
            Nodes[id] = new Node { Kind = "object" };
            Objects[key] = id;
            return new ElementId(id);
        }

        public void PlaceObject(ElementId obj, LookPosition position, LookFrame frame, long unit)
        {
            Nodes[obj.Value].Position = position;
            Nodes[obj.Value].Frame = frame;
            Nodes[obj.Value].Unit = unit;
        }

        public void ShowMissing(ElementId obj) => Nodes[obj.Value].Missing = true;

        public void SetOpacity(ElementId obj, long opacity) => Nodes[obj.Value].Opacity = opacity;

        public ElementId Create(string kind, ElementId parent, int index)
        {
            long id = ++_next;
            Nodes[id] = new Node { Kind = kind, Parent = parent.Value };
            Nodes[parent.Value].Children.Insert(index, id);
            return new ElementId(id);
        }

        public void Set(ElementId element, string attribute, LookAttributeValue value)
        {
            Sets++;
            Nodes[element.Value].Attributes[attribute] = value.ToString();
        }

        public void SetVisible(ElementId element, bool visible) => Nodes[element.Value].Visible = visible;

        public void Move(ElementId element, ElementId parent, int index)
        {
            var siblings = Nodes[parent.Value].Children;
            siblings.Remove(element.Value);
            siblings.Insert(index, element.Value);
        }

        public void Remove(ElementId element)
        {
            var node = Nodes[element.Value];
            foreach (var child in node.Children.ToList()) Remove(new ElementId(child));
            if (node.Parent != null) Nodes[node.Parent.Value].Children.Remove(element.Value);
            Nodes.Remove(element.Value);
            foreach (var pair in Objects.Where(p => p.Value == element.Value).ToList()) Objects.Remove(pair.Key);
        }

        /// <summary>
        /// 물체 하나: <c>@위치 Kind[속성=값,…]{자식…}</c>, 공간 단위가 1이 아니면 위치 뒤에 <c>x단위</c>, 투명도가 1이 아니면 <c>a투명도</c>, 숨긴 요소는 앞에 <c>-</c>,
    /// 대체 그림은 <c>MISSING</c>.
        /// </summary>
        public string Dump(string key)
        {
            var node = Nodes[Objects[key]];
            var sb = new StringBuilder();
            sb.Append('@').Append(node.Position?.ToString() ?? "-");
            if (node.Unit != Bridge.BridgeFixed.Scale) sb.Append(" x").Append(new Bridge.BridgeFixed(node.Unit).Decimal);
            if (node.Opacity != Bridge.BridgeFixed.Scale) sb.Append(" a").Append(new Bridge.BridgeFixed(node.Opacity).Decimal);
            if (node.Missing) sb.Append(" MISSING");
            foreach (var child in node.Children) sb.Append(' ').Append(DumpNode(child));
            return sb.ToString();
        }

        private string DumpNode(long id)
        {
            var node = Nodes[id];
            var sb = new StringBuilder();
            if (!node.Visible) sb.Append('-');
            sb.Append(node.Kind);
            if (node.Attributes.Count > 0) sb.Append('[').Append(string.Join(",", node.Attributes.Select(a => a.Key + "=" + a.Value))).Append(']');
            if (node.Children.Count > 0) sb.Append('{').Append(string.Join(" ", node.Children.Select(DumpNode))).Append('}');
            return sb.ToString();
        }
    }
}
