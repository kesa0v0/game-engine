using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>칸의 값이 어디서 왔는가.</summary>
    public enum ValueOrigin
    {
        /// <summary>그 Def가 직접 적었다.</summary>
        Own,

        /// <summary>부모 Def에서 물려받았다.</summary>
        Inherited,

        /// <summary>어느 모드의 패치가 마지막으로 바꿨다 (<see cref="ResolvedValue.Patch"/>).</summary>
        Patched,

        /// <summary>값이 없어 스키마 <c>default</c>(리스트·맵은 빈 컬렉션)로 채웠다 (동결본에만 있다, G69).</summary>
        Default,
    }

    /// <summary>
    /// ④가 만든 최종 값 한 칸과 그 출처 (spec-gaps G52). <see cref="Source"/>는 값을 처음 적은 Def이고,
    /// 물려받은 값이면 부모(또는 그 조상)다.
    /// </summary>
    public sealed class ResolvedValue
    {
        internal ResolvedValue(Value value, ValueOrigin origin, DefRecord source, SourceSpan span, PatchLogEntry? patch = null)
        {
            Value = value;
            Origin = origin;
            Source = source;
            Span = span;
            Patch = patch;
        }

        /// <summary>
        /// 이 값을 마지막으로 바꾼 패치. 패치가 바꾼 값을 자식이 물려받아도 남는다 (그때 <see cref="Origin"/>은 Inherited).
        /// </summary>
        public PatchLogEntry? Patch { get; }

        /// <summary>평가가 끝난 데이터 값 (<see cref="DeferredValue"/>는 남지 않는다).</summary>
        public Value Value { get; }

        public ValueOrigin Origin { get; }
        public DefRecord Source { get; }

        /// <summary>값을 적은 곳 (키워드 인자 이름의 위치).</summary>
        public SourceSpan Span { get; }

        internal ResolvedValue AsInherited() =>
            Origin == ValueOrigin.Inherited ? this : new ResolvedValue(Value, ValueOrigin.Inherited, Source, Span, Patch);
    }

    /// <summary>적힌 순서를 지키는 이름 → 값 맵. 순회 순서가 결정적이어야 해서 Dictionary를 직접 순회하지 않는다.</summary>
    public sealed class OrderedMap<T> where T : class
    {
        private readonly Dictionary<string, T> _items = new Dictionary<string, T>(StringComparer.Ordinal);
        private readonly List<string> _keys = new List<string>();

        public int Count => _keys.Count;

        /// <summary>들어간 순서의 키.</summary>
        public IReadOnlyList<string> Keys => _keys;

        public bool Contains(string key) => _items.ContainsKey(key);

        public bool TryGet(string key, out T value) => _items.TryGetValue(key, out value!);

        internal void Set(string key, T value)
        {
            if (!_items.ContainsKey(key)) _keys.Add(key);
            _items[key] = value;
        }

        internal bool Remove(string key)
        {
            if (!_items.Remove(key)) return false;
            _keys.Remove(key);
            return true;
        }
    }

    /// <summary>
    /// 컴포넌트 하나의 최종 값. 키 컴포넌트가 아니면 <see cref="Members"/>가 값 이름 → 값이고,
    /// 키 컴포넌트면 <see cref="Rows"/>가 줄 키(Def id) → 그 줄의 값들이다 (G58).
    /// </summary>
    public sealed class ResolvedComponent
    {
        internal ResolvedComponent(ComponentType component) => Component = component;

        public ComponentType Component { get; }
        public OrderedMap<ResolvedValue> Members { get; } = new OrderedMap<ResolvedValue>();
        public OrderedMap<ResolvedRow> Rows { get; } = new OrderedMap<ResolvedRow>();

        /// <summary>패치가 붙인 컴포넌트면 그 패치 (타입에 붙은 컴포넌트는 null).</summary>
        public PatchLogEntry? AttachedBy { get; internal set; }

        internal ResolvedComponent CloneInherited()
        {
            var copy = new ResolvedComponent(Component) { AttachedBy = AttachedBy };
            foreach (var k in Members.Keys)
            {
                Members.TryGet(k, out var v);
                copy.Members.Set(k, v.AsInherited());
            }
            foreach (var k in Rows.Keys)
            {
                Rows.TryGet(k, out var row);
                copy.Rows.Set(k, row.CloneInherited());
            }
            return copy;
        }
    }

    /// <summary>키 컴포넌트의 줄 하나.</summary>
    public sealed class ResolvedRow
    {
        internal ResolvedRow() { }

        public OrderedMap<ResolvedValue> Members { get; } = new OrderedMap<ResolvedValue>();

        /// <summary>패치가 붙인 줄이면 그 패치.</summary>
        public PatchLogEntry? AttachedBy { get; internal set; }

        internal ResolvedRow CloneInherited()
        {
            var copy = new ResolvedRow { AttachedBy = AttachedBy };
            foreach (var k in Members.Keys)
            {
                Members.TryGet(k, out var v);
                copy.Members.Set(k, v.AsInherited());
            }
            return copy;
        }
    }

    /// <summary>
    /// ④가 상속을 펼친 Def 하나의 값 (spec-gaps G52). <see cref="DefRecord"/>(적힌 그대로)는 건드리지 않고 따로 만든다.
    /// 값은 상속된 것까지 합쳤고 <c>inherit</c> 식은 모두 평가되어 있다. 스키마 <c>default</c>는 채우지 않는다
    /// (값이 없는 칸은 비어 있고, 기본값은 읽는 쪽이 스키마에서 찾는다).
    /// </summary>
    public sealed class ResolvedDef
    {
        internal ResolvedDef(DefRecord record, ResolvedDef? parent)
        {
            Record = record;
            Parent = parent;
        }

        public DefRecord Record { get; }
        public string Id => Record.Id;
        public DefType Type => Record.Type;
        public string Mod => Record.Mod;

        public ResolvedDef? Parent { get; }
        public bool IsAbstract => Record.IsAbstract;

        /// <summary>const 값 (이름 → 값).</summary>
        public OrderedMap<ResolvedValue> Consts { get; } = new OrderedMap<ResolvedValue>();

        /// <summary>붙어 있는 컴포넌트 (컴포넌트 이름 → 값).</summary>
        public OrderedMap<ResolvedComponent> Components { get; } = new OrderedMap<ResolvedComponent>();
    }

    /// <summary>상속을 펼친 Def들 (④ 끝, 등록 순서).</summary>
    public sealed class ResolvedDefs
    {
        private readonly Dictionary<string, ResolvedDef> _byId = new Dictionary<string, ResolvedDef>(StringComparer.Ordinal);
        private readonly List<ResolvedDef> _list = new List<ResolvedDef>();

        public IReadOnlyList<ResolvedDef> Defs => _list;

        public bool TryGet(string id, out ResolvedDef def) => _byId.TryGetValue(id, out def!);

        internal void Add(ResolvedDef def)
        {
            _byId.Add(def.Id, def);
            _list.Add(def);
        }
    }
}
