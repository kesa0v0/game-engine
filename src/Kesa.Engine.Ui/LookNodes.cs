using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>판 위에 보이는 물체 하나 (<see cref="LookStage"/>).</summary>
    internal sealed class ShownObject
    {
        public ShownObject(ElementId handle, BridgeLook look, LookDefinition? definition, UiScope scope)
        {
            Handle = handle;
            Look = look;
            Definition = definition;
            Scope = scope;
        }

        public ElementId Handle { get; }
        public BridgeLook Look { get; set; }

        /// <summary>정의가 없으면 null(대체 그림).</summary>
        public LookDefinition? Definition { get; }

        /// <summary>그림 값의 키들.</summary>
        public UiScope Scope { get; }

        public List<LookNode> Roots { get; } = new List<LookNode>();
        public LookPosition? Position { get; set; }
        public long Unit { get; set; }
    }

    /// <summary>만든 요소 하나와 그 묶인 값의 직전 값.</summary>
    internal sealed class LookNode
    {
        public LookNode(LookElement definition, ElementId handle, UiScope scope)
        {
            Definition = definition;
            Handle = handle;
            Scope = scope;
        }

        public LookElement Definition { get; }
        public ElementId Handle { get; }
        public UiScope Scope { get; }

        /// <summary>묶음마다 직전에 넣은 값(같으면 다시 넣지 않는다).</summary>
        public Dictionary<LookBinding, LookAttributeValue> Applied { get; } = new Dictionary<LookBinding, LookAttributeValue>();

        public List<LookNode> Children { get; } = new List<LookNode>();

        /// <summary><c>for</c>가 있으면 항목마다의 묶음.</summary>
        public List<LookGroup>? Groups { get; set; }
    }

    /// <summary><c>for</c>의 항목 하나와 그 자식들.</summary>
    internal sealed class LookGroup
    {
        public LookGroup(string key, BridgeValue item, UiScope scope)
        {
            Key = key;
            Item = item;
            Scope = scope;
        }

        public string Key { get; }
        public BridgeValue Item { get; set; }
        public UiScope Scope { get; }
        public List<LookNode> Roots { get; } = new List<LookNode>();
    }
}
