using System;
using System.Collections.Generic;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 그림 요소 종류와 공간 종류를 등록하는 자리 (G390·G394·G413). <see cref="Standard"/>가 기본 종류(<c>Sprite</c>·<c>Group</c>·<c>grid</c>)를
    /// 넣은 것을 주고 게임은 여기에 제 종류를 더한다. 해석기는 기본 종류를 따로 알지 않는다.
    /// </summary>
    public sealed class LookKinds
    {
        private readonly SortedDictionary<string, LookElementKind> _elements = new SortedDictionary<string, LookElementKind>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, SpaceKind> _spaces = new SortedDictionary<string, SpaceKind>(StringComparer.Ordinal);

        /// <summary>모든 요소가 받는 속성 (G413). 묶음(<c>Group</c>) 안에서는 부모 기준이다.</summary>
        public static readonly IReadOnlyDictionary<string, LookAttributeType> Common = new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
        {
            ["layer"] = LookAttributeType.Integer,
            ["offset"] = LookAttributeType.Vector,
        };

        public IEnumerable<string> ElementNames => _elements.Keys;

        public IEnumerable<string> SpaceNames => _spaces.Keys;

        /// <summary>기본 종류를 넣은 등록 자리.</summary>
        public static LookKinds Standard()
        {
            var kinds = new LookKinds();
            kinds.Add(new LookElementKind("Sprite", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["src"] = LookAttributeType.Path,
                ["size"] = LookAttributeType.Vector2,
                ["tint"] = LookAttributeType.Color,
            }, acceptsChildren: false));
            kinds.Add(new LookElementKind("Text", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal)
            {
                ["text"] = LookAttributeType.Text,
                ["size"] = LookAttributeType.Number,
                ["color"] = LookAttributeType.Color,
            }, acceptsChildren: false));
            kinds.Add(new LookElementKind("Group", new SortedDictionary<string, LookAttributeType>(StringComparer.Ordinal), acceptsChildren: true));
            kinds.Add(new GridSpace());
            return kinds;
        }

        /// <summary>같은 이름이 있거나 공통 속성 이름을 쓰면 던진다(게임 코드의 실수).</summary>
        public void Add(LookElementKind kind)
        {
            if (_elements.ContainsKey(kind.Name)) throw new ArgumentException("the look element '" + kind.Name + "' is already registered");
            if (kind.Name == "Look" || kind.Name == "Looks" || kind.Name == "Include")
                throw new ArgumentException("'" + kind.Name + "' is reserved in look files");
            foreach (var attribute in kind.Attributes.Keys)
                if (Common.ContainsKey(attribute) || attribute == "for" || attribute.StartsWith("bind-", StringComparison.Ordinal))
                    throw new ArgumentException("the look element '" + kind.Name + "' cannot declare '" + attribute + "' (the interpreter handles it)");
            _elements.Add(kind.Name, kind);
        }

        public void Add(SpaceKind kind)
        {
            if (_spaces.ContainsKey(kind.Name)) throw new ArgumentException("the space kind '" + kind.Name + "' is already registered");
            _spaces.Add(kind.Name, kind);
        }

        public bool TryGetElement(string name, out LookElementKind kind) => _elements.TryGetValue(name, out kind!);

        public bool TryGetSpace(string name, out SpaceKind kind) => _spaces.TryGetValue(name, out kind!);
    }
}
