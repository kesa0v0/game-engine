using Kesa.Engine.Schema;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 동결된 Def 하나 (spec S1 ⑥, spec-gaps G69·G70): 상속·패치·검증을 마친 최종 값이다. 모든 <c>const</c>(컴포넌트 안의 것 포함)는
    /// 스키마 기본값까지 채워져 있어(출처 <see cref="ValueOrigin.Default"/>) 읽는 쪽이 스키마를 다시 찾지 않는다.
    /// 런타임 칼럼 <c>field</c>는 값이 적혀 있을 때만 있다. 변경하는 방법이 없다.
    /// </summary>
    public sealed class FrozenDef
    {
        internal FrozenDef(ResolvedDef source)
        {
            Source = source;
            Id = source.Id;
            Type = source.Type;
            Mod = source.Mod;
            ParentId = source.Record.Parent?.Name;
        }

        /// <summary>상속을 펼친 값과 출처 (덤프용).</summary>
        public ResolvedDef Source { get; }

        public string Id { get; }
        public DefType Type { get; }
        public string Mod { get; }

        /// <summary>부모의 id. 부모는 <c>abstract</c>일 수 있어 동결 집합에 없으므로 id로 가리킨다.</summary>
        public string? ParentId { get; }

        /// <summary>모든 <c>const</c> 값 (이름 → 값).</summary>
        public OrderedMap<ResolvedValue> Consts { get; } = new OrderedMap<ResolvedValue>();

        /// <summary>이 Def가 가진 컴포넌트 (타입에 붙은 것은 항상 있다).</summary>
        public OrderedMap<ResolvedComponent> Components { get; } = new OrderedMap<ResolvedComponent>();

        internal DefSet? Owner { get; set; }

        /// <summary>
        /// 키 컴포넌트의 줄을 읽는다 (spec-gaps G77). 적힌 줄이면 그 줄이고, <c>keys="all"</c>이면 키 타입의 구체 Def마다 줄이 있으므로
        /// 적히지 않은 줄은 기본값으로 채운 줄을 돌려준다. <c>keys="listed"</c>의 없는 줄과 유효하지 않은 키는 false다.
        /// </summary>
        public bool TryGetRow(string componentName, string key, out ResolvedRow row)
        {
            row = null!;
            if (!Components.TryGet(componentName, out var rc)) return false;
            var component = rc.Component;
            if (!component.IsKeyed || component.KeyKind != KeyKind.Def) return false;
            if (rc.Rows.TryGet(key, out row)) return true;
            if (component.Keys != KeysMode.All || Owner == null || !Owner.TryGet(key, out var keyDef) || keyDef.Type != component.Key)
                return false;
            row = DefSet.DefaultRow(Source, component);
            return true;
        }
    }
}
