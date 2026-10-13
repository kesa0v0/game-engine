using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>스키마 <c>default</c>와 <c>default_from</c>으로 값이 없는 칸의 기본값을 찾는다 (spec-gaps G53·G58).</summary>
    internal static class MemberDefaults
    {
        /// <param name="component">컴포넌트 값이면 그 컴포넌트, const면 null</param>
        /// <param name="siblings">같은 컴포넌트(줄)의 이미 정해진 값들. <c>default_from</c>이 읽는다</param>
        public static Value? Of(ComponentType? component, MemberDecl member, OrderedMap<ResolvedValue>? siblings)
        {
            var spec = member.Spec;
            if (spec.Default != null) return spec.Default;
            if (spec.DefaultFrom is NameRef from && component != null)
            {
                if (siblings != null && siblings.TryGet(from.Name, out var given)) return given.Value;
                if (component.TryGetMember(from.Name, out var source)) return source.Spec.Default;
            }
            return null;
        }

        /// <summary>
        /// 스키마 선언의 <c>default</c> 값 하나를 ⑤ 링크와 ⑥ 검증이 보게 하는 출처. 책임은 그 값을 선언한 모드이고 위치는 선언의
        /// 키워드 인자다 (spec-gaps G73). Def가 없으므로 Def 자리는 선언 모드와 위치만 채운 자리표시다.
        /// </summary>
        public static ResolvedValue DeclaredOrigin(MemberDecl decl)
        {
            var holder = new DefRecord(null!, "<default of " + decl.Name + ">", decl.Mod, decl.Span, null, false, false,
                new System.Collections.Generic.List<DefField>());
            return new ResolvedValue(decl.Spec.Default!, ValueOrigin.Own, holder, decl.Span);
        }

        /// <summary>기본값이 선언된 <c>const</c>·<c>field</c> 멤버들 (Def 타입의 const, 컴포넌트의 값). 선언마다 한 번.</summary>
        public static System.Collections.Generic.IEnumerable<MemberDecl> DeclaredDefaults(SchemaSet schema)
        {
            foreach (var type in schema.Types)
                foreach (var decl in type.Consts)
                    if (decl.Spec.Default != null) yield return decl;
            foreach (var component in schema.Components)
                foreach (var decl in component.Members)
                {
                    if (decl.Spec.Default == null) continue;
                    // field의 함수 기본값은 엔티티를 만들 때 계산하는 식이라 값 검사 대상이 아니다 (G33)
                    if (decl.Kind == MemberKind.Field && decl.Spec.Default is FunctionValue) continue;
                    if (decl.Kind == MemberKind.Const || decl.Kind == MemberKind.Field) yield return decl;
                }
        }
    }
}
