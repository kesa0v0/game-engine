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
    }
}
