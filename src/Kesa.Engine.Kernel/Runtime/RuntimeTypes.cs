using System;
using System.Globalization;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 엔티티 id (spec S4). 월드마다 1부터 올라가고 재사용하지 않아 id 순 = 생성 순이다 (spec-gaps G87).
    /// 세대 칸은 없다: 죽은 id는 영원히 죽은 채라 생존 여부가 세대의 일을 한다.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public EntityId(long value) => Value = value;

        public long Value { get; }

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public override string ToString() => "#" + Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
    }

    /// <summary>엔티티를 가리키는 값: Ref 칼럼의 값, 엔티티 키 (spec S3).</summary>
    public sealed class EntityValue : Value, IValueEquality, IEntityReference
    {
        public EntityValue(EntityId id) => Id = id;
        public EntityId Id { get; }
        public override string KindName => "entity";

        EntityId IEntityReference.EntityId => Id;

        public bool ValueEquals(Value other) => other is IEntityReference e && e.EntityId == Id;
    }

    /// <summary>엔티티를 가리키는 값이면 무엇이든(<see cref="EntityValue"/>, 함수 본문에서 보이는 엔티티) 같은 엔티티끼리 같다.</summary>
    internal interface IEntityReference
    {
        EntityId EntityId { get; }
    }

    /// <summary>
    /// 이름을 한 번 풀어 둔 값 자리 (spec P6 "로드 시점에 슬롯 핸들로 바인딩"). 컴포넌트의 멤버이거나,
    /// Def 타입 바로 아래의 <c>const</c>다(<see cref="Component"/>가 null).
    /// </summary>
    public sealed class MemberHandle
    {
        internal MemberHandle(ComponentType? component, DefType? defType, MemberDecl member)
        {
            Component = component;
            DefType = defType;
            Member = member;
        }

        /// <summary>멤버가 든 컴포넌트. Def 타입의 <c>const</c>면 null.</summary>
        public ComponentType? Component { get; }

        /// <summary>Def 타입의 <c>const</c>면 그 타입.</summary>
        public DefType? DefType { get; }

        public MemberDecl Member { get; }
        public MemberKind Kind => Member.Kind;

        /// <summary><c>Health.current</c> 또는 <c>Unit.cost</c>.</summary>
        public string Display => (Component?.Name ?? DefType!.Name) + "." + Member.Name;

        public override string ToString() => Display;
    }

    /// <summary>생성·부착 때 주는 처음 값 하나 (spec-gaps G91·G93).</summary>
    public readonly struct Init
    {
        public Init(MemberHandle member, Value value)
        {
            Member = member;
            Value = value;
        }

        public MemberHandle Member { get; }
        public Value Value { get; }
    }

    /// <summary>
    /// 월드 API의 런타임 오류 (spec-gaps G92 등). 코드는 <c>RUN</c> 영역이다. 스크립트에서 일어나면 실행기가 위치를 붙여 진단으로 바꾼다.
    /// 실패한 호출은 월드를 바꾸지 않는다.
    /// </summary>
    public sealed class WorldException : Exception
    {
        public WorldException(string code, string message, string? hint = null, Exception? inner = null) : base(message, inner)
        {
            Code = code;
            Hint = hint;
        }

        public string Code { get; }
        public string? Hint { get; }

        public override string ToString() => Code + ": " + Message + (Hint == null ? "" : " (" + Hint + ")");
    }
}
