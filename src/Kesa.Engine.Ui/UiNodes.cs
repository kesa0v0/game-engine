using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    internal enum RuntimeBindingKind
    {
        Property,
        ClassToggle,
        ClassNames,
        RepeatSource,
        IncludeArgument,
    }

    /// <summary>함수 값 하나를 요소에 대는 연결. <see cref="Key"/>가 세션의 영수증 키다 (G246·G345).</summary>
    internal sealed class RuntimeBinding
    {
        public RuntimeBinding(string key, RuntimeBindingKind kind, UiNode owner, UiScope scope, string name, UiExpr expression, string label)
        {
            Key = key;
            Kind = kind;
            Owner = owner;
            Scope = scope;
            Name = name;
            Expression = expression;
            Label = label;
        }

        public string Key { get; }

        public RuntimeBindingKind Kind { get; }

        public UiNode Owner { get; }

        /// <summary>식을 계산할 이름 범위. 포함의 인자는 부모 쪽 범위다.</summary>
        public UiScope Scope { get; }

        /// <summary>요소 속성 이름, 클래스 이름, 포함의 인자 이름.</summary>
        public string Name { get; }

        public UiExpr Expression { get; }

        /// <summary>오류 메시지에 붙일 <c>bind-text="…"</c> 꼴.</summary>
        public string Label { get; }

        /// <summary><see cref="RuntimeBindingKind.ClassNames"/>가 지금 붙여 둔 클래스들.</summary>
        public SortedSet<string> AppliedClasses { get; } = new SortedSet<string>(System.StringComparer.Ordinal);
    }

    /// <summary>반복의 항목 하나: 키, 값, 항목 변수를 담은 범위, 자식 템플릿에서 만든 요소들 (G338·G339).</summary>
    internal sealed class ItemGroup
    {
        public ItemGroup(string key, BridgeValue item, UiScope scope)
        {
            Key = key;
            Item = item;
            Scope = scope;
        }

        public string Key { get; }

        public BridgeValue Item { get; set; }

        public UiScope Scope { get; }

        public List<UiNode> Roots { get; } = new List<UiNode>();
    }

    /// <summary>올려 둔 요소 하나의 상태. <see cref="Id"/>가 null이면 요소 없이 파일 하나를 끼운 <c>Include</c>다.</summary>
    internal sealed class UiNode
    {
        public UiNode(UiElement definition, ElementId? id, UiNode? parent, UiScope scope, string path)
        {
            Definition = definition;
            Id = id;
            Parent = parent;
            Scope = scope;
            Path = path;
        }

        public UiElement Definition { get; }

        public ElementId? Id { get; }

        public UiNode? Parent { get; }

        public UiScope Scope { get; }

        public string Path { get; }

        public bool Alive { get; set; } = true;

        /// <summary>마지막으로 넣은 <c>display</c>(창이 보이는지는 이것과 모든 윗 노드의 것으로 본다, G354).</summary>
        public bool Displayed { get; set; } = true;

        public List<RuntimeBinding> Bindings { get; } = new List<RuntimeBinding>();

        public List<UiNode> Children { get; } = new List<UiNode>();

        public List<ItemGroup>? Groups { get; set; }

        /// <summary><c>Include</c>가 끼운 파일의 맨 위 요소와 그 범위.</summary>
        public UiNode? Inner { get; set; }

        public UiScope? InnerScope { get; set; }

        /// <summary>렌더러에 내놓은 요소. <c>Include</c>는 끼운 파일의 맨 위 요소다.</summary>
        public ElementId Handle => Id ?? Inner!.Handle;
    }
}
