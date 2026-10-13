using Kesa.Engine.Schema;

namespace Kesa.Engine.Loading
{
    public enum PatchTargetKind
    {
        /// <summary>Def 타입 바로 아래의 <c>const</c> (<c>move</c>).</summary>
        Const,

        /// <summary>컴포넌트 전체 (<c>Morale</c>): 붙이기·떼기의 대상.</summary>
        Component,

        /// <summary>컴포넌트의 값 하나 (<c>Health.base_max</c>).</summary>
        Member,

        /// <summary>키 컴포넌트의 줄 하나 (<c>Skill.Medicine</c>): 붙이기·떼기의 대상.</summary>
        Row,

        /// <summary>키 컴포넌트 줄 안의 값 하나 (<c>Skill.Medicine.level</c>).</summary>
        RowMember,
    }

    /// <summary>
    /// 패치 경로가 그 Def 타입의 스키마에서 가리키는 곳 (spec-gaps G34·G47). 타입만 본 결과이고 값은 아직 보지 않는다:
    /// 그 칸에 값이 있는지(물려받은 값 포함)는 ④에서 판정한다 (G42).
    /// </summary>
    public sealed class PatchTarget
    {
        internal PatchTarget(PatchTargetKind kind, DefType defType)
        {
            Kind = kind;
            DefType = defType;
        }

        public PatchTargetKind Kind { get; }
        public DefType DefType { get; }

        /// <summary>Const, Member, RowMember의 값 선언.</summary>
        public MemberDecl? Member { get; internal set; }

        public ComponentType? Component { get; internal set; }

        /// <summary>그 컴포넌트가 Def 타입의 슬롯인가 (모든 Def가 가짐, G44). 아니면 패치로 붙이는 컴포넌트다.</summary>
        public bool IsTypeSlot { get; internal set; }

        /// <summary>Row, RowMember의 줄 키(Def id)와 그 Def.</summary>
        public string? RowKey { get; internal set; }

        public DefRecord? RowDef { get; internal set; }

        /// <summary>
        /// 경로가 끝나는 곳의 값 타입. 값 안으로 들어갔으면(<c>Cost.Wood</c>) 안쪽 타입이다.
        /// Component, Row는 값이 아니라 null.
        /// </summary>
        public TypeRef? ValueType { get; internal set; }

        /// <summary>경로가 맵의 항목에서 끝나면 그 맵의 타입과 키 (<c>Cost.Wood</c>의 <c>Cost</c>). 아니면 null.</summary>
        public DictTypeRef? Entry { get; internal set; }

        public PathSegment? EntryKey { get; internal set; }
    }

    /// <summary>③의 경로 검사를 통과한 패치 호출과 그 대상.</summary>
    public sealed class ResolvedPatch
    {
        internal ResolvedPatch(PatchCall call, DefRecord record, PatchTarget target)
        {
            Call = call;
            Record = record;
            Target = target;
        }

        public PatchCall Call { get; }

        /// <summary>패치가 놓이는 Def (id로 찾은 현재 Def, 교체본, G43).</summary>
        public DefRecord Record { get; }

        public PatchTarget Target { get; }
    }
}
