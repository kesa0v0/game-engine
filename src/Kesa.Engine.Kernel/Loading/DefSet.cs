using System;
using System.Collections.Generic;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

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

    /// <summary>
    /// 동결된 Def 집합 (G70): <c>abstract</c>가 아닌 Def만, 등록 순서로. 아무도 가리킬 수 없는 템플릿(G66)은 여기 없다.
    /// </summary>
    public sealed class DefSet
    {
        private readonly Dictionary<string, FrozenDef> _byId = new Dictionary<string, FrozenDef>(StringComparer.Ordinal);
        private readonly List<FrozenDef> _list = new List<FrozenDef>();

        private DefSet() { }

        public IReadOnlyList<FrozenDef> Defs => _list;

        public bool TryGet(string id, out FrozenDef def) => _byId.TryGetValue(id, out def!);

        /// <summary>그 타입의 Def들 (등록 순서).</summary>
        public IEnumerable<FrozenDef> OfType(string typeName)
        {
            foreach (var d in _list)
                if (d.Type.Name == typeName) yield return d;
        }

        /// <summary>검증을 마친 <see cref="ResolvedDefs"/>를 동결한다.</summary>
        internal static DefSet Freeze(ResolvedDefs resolved)
        {
            var set = new DefSet();
            foreach (var def in resolved.Defs)
            {
                if (def.IsAbstract) continue;
                var frozen = Freeze(def);
                frozen.Owner = set;
                set._byId.Add(frozen.Id, frozen);
                set._list.Add(frozen);
            }
            return set;
        }

        /// <summary>적히지 않은 줄(<c>keys="all"</c>)의 기본값 줄: <c>const</c> 멤버만 스키마 기본값으로 채운다 (G77).</summary>
        internal static ResolvedRow DefaultRow(ResolvedDef def, ComponentType component)
        {
            var row = new ResolvedRow();
            FillMembers(def, component, null, row.Members);
            return row;
        }

        private static FrozenDef Freeze(ResolvedDef def)
        {
            var frozen = new FrozenDef(def);
            foreach (var decl in def.Type.Consts)
            {
                if (def.Consts.TryGet(decl.Name, out var value)) frozen.Consts.Set(decl.Name, value);
                else
                {
                    var filled = Fill(def, decl, null, null);
                    if (filled != null) frozen.Consts.Set(decl.Name, filled);
                }
            }

            foreach (var slot in def.Type.Components)
            {
                def.Components.TryGet(slot.Component.Name, out var rc);
                frozen.Components.Set(slot.Component.Name, FreezeComponent(def, slot.Component, rc));
            }
            foreach (var name in def.Components.Keys)
            {
                if (frozen.Components.Contains(name)) continue;
                def.Components.TryGet(name, out var rc);
                frozen.Components.Set(name, FreezeComponent(def, rc.Component, rc));
            }
            return frozen;
        }

        private static ResolvedComponent FreezeComponent(ResolvedDef def, ComponentType component, ResolvedComponent? source)
        {
            var copy = new ResolvedComponent(component) { AttachedBy = source?.AttachedBy };
            if (component.IsKeyed)
            {
                if (source == null) return copy;
                foreach (var key in source.Rows.Keys)
                {
                    source.Rows.TryGet(key, out var row);
                    var frozenRow = new ResolvedRow { AttachedBy = row.AttachedBy };
                    FillMembers(def, component, row.Members, frozenRow.Members);
                    copy.Rows.Set(key, frozenRow);
                }
                return copy;
            }
            FillMembers(def, component, source?.Members, copy.Members);
            return copy;
        }

        /// <summary><c>const</c> 멤버는 기본값까지 채우고, 그 밖의 멤버는 적힌 값만 옮긴다.</summary>
        private static void FillMembers(ResolvedDef def, ComponentType component, OrderedMap<ResolvedValue>? given,
            OrderedMap<ResolvedValue> target)
        {
            foreach (var member in component.Members)
            {
                if (given != null && given.TryGet(member.Name, out var value))
                {
                    target.Set(member.Name, value);
                    continue;
                }
                if (member.Kind != MemberKind.Const) continue;
                var filled = Fill(def, member, component, given);
                if (filled != null) target.Set(member.Name, filled);
            }
        }

        /// <summary>값이 없는 <c>const</c>의 기본값. 기본값이 없는 필수 값은 검증이 이미 잡았으므로 (추상이 아니면) 여기 오지 않는다.</summary>
        private static ResolvedValue? Fill(ResolvedDef def, MemberDecl decl, ComponentType? component,
            OrderedMap<ResolvedValue>? siblings)
        {
            var value = MemberDefaults.Of(component, decl, siblings);
            if (value == null)
            {
                if (decl.Spec.Type is ListTypeRef) value = new ListValue(new List<Value>());
                else if (decl.Spec.Type is DictTypeRef) value = new DictValue(new List<DictItem>());
            }
            return value == null ? null : new ResolvedValue(value, ValueOrigin.Default, def.Record, decl.Span);
        }
    }
}
