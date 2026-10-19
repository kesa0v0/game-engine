using Kesa.Engine.Schema;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 속성 이름 검사(spec-gaps G228)가 아는 값의 타입. 엔티티와 그 위의 보기(컴포넌트, 키 줄 묶음, Def 상수)와 엔티티 묶음만 구분하고
    /// 나머지는 모두 <see cref="Unknown"/>이다. C# <c>null</c>은 "아직 정보 없음"(대입을 모으는 중)이다.
    /// </summary>
    internal abstract class StaticType
    {
        /// <summary>모르는 값: 속성 이름을 보지 않는다.</summary>
        public static readonly StaticType Unknown = new UnknownType();

        /// <summary><c>None</c>: 합치면 다른 쪽이 남는다 (None에서 속성을 읽는 것은 오타가 아니라 다른 실수다).</summary>
        public static readonly StaticType None = new NoneType();

        public abstract bool Same(StaticType other);

        /// <summary>두 갈래(대입 둘, 조건식의 두 쪽, 문맥 둘)의 타입을 합친다.</summary>
        public static StaticType? Join(StaticType? a, StaticType? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            if (a == Unknown || b == Unknown) return Unknown;
            if (a == None) return b;
            if (b == None) return a;
            if (a.Same(b)) return a;
            if (a is EntityType ea && b is EntityType eb)
                return new EntityType(ea.Type == eb.Type ? ea.Type : null, ea.Self && eb.Self);
            if (a is ComponentStatic ca && b is ComponentStatic cb && ca.Component == cb.Component && ca.Owner == cb.Owner &&
                ca.Row == cb.Row)
                return new ComponentStatic(ca.Component, ca.Owner, ca.Row, ca.Self && cb.Self);
            return Unknown;
        }

        private sealed class UnknownType : StaticType
        {
            public override bool Same(StaticType other) => other == this;
        }

        private sealed class NoneType : StaticType
        {
            public override bool Same(StaticType other) => other == this;
        }
    }

    /// <summary>엔티티. Def 타입을 모르면(<c>query("Wounded")</c>, 역방향 컬렉션의 원소) <see cref="Type"/>이 null이다.</summary>
    internal sealed class EntityType : StaticType
    {
        public EntityType(DefType? type, bool self = false)
        {
            Type = type;
            Self = self;
        }

        public DefType? Type { get; }

        /// <summary>계산 중인 <c>derived</c>의 엔티티 자신인가 (<c>h.entity</c>, 순환 검사 G229).</summary>
        public bool Self { get; }

        public override bool Same(StaticType other) => other is EntityType e && e.Type == Type && e.Self == Self;
    }

    /// <summary>
    /// 컴포넌트 하나(<c>u.Food</c>), 키 컴포넌트면 줄 하나(<c>u.Skill.Medicine</c>, <c>derived</c>의 인자). 엔티티의 Def 타입을 알면
    /// <see cref="Owner"/>가 있다.
    /// </summary>
    internal sealed class ComponentStatic : StaticType
    {
        public ComponentStatic(ComponentType component, DefType? owner, bool row, bool self = false)
        {
            Component = component;
            Owner = owner;
            Row = row;
            Self = self;
        }

        public ComponentType Component { get; }
        public DefType? Owner { get; }

        /// <summary>키 컴포넌트의 줄 하나인가 (<c>key</c>를 읽을 수 있다).</summary>
        public bool Row { get; }

        /// <summary>
        /// 계산 중인 <c>derived</c>의 엔티티 자신의 것인가 (인자 <c>h</c>와 그 형제, 줄이면 같은 줄). 순환 검사(G229)가 이것만 따라간다.
        /// </summary>
        public bool Self { get; }

        public override bool Same(StaticType other) =>
            other is ComponentStatic c && c.Component == Component && c.Owner == Owner && c.Row == Row && c.Self == Self;
    }

    /// <summary>키 컴포넌트의 줄 묶음 (<c>u.Skill</c>). 속성은 줄 키라 이름을 보지 않는다.</summary>
    internal sealed class RowsType : StaticType
    {
        public RowsType(ComponentType component, DefType? owner)
        {
            Component = component;
            Owner = owner;
        }

        public ComponentType Component { get; }
        public DefType? Owner { get; }
        public override bool Same(StaticType other) => other is RowsType r && r.Component == Component && r.Owner == Owner;
    }

    /// <summary>엔티티의 Def 타입 이름으로 읽는 그 타입의 <c>const</c>들 (<c>s.Spell.effect</c>, G107).</summary>
    internal sealed class DefConstsType : StaticType
    {
        public DefConstsType(DefType type) => Type = type;
        public DefType Type { get; }
        public override bool Same(StaticType other) => other is DefConstsType d && d.Type == Type;
    }

    /// <summary>
    /// 컴포넌트·신호의 생성자 (<c>Health</c>, <c>Damaged</c>): 모드가 묶지 않은 보이는 스키마 이름이다(G230). 속성이 없다.
    /// </summary>
    internal sealed class ConstructorType : StaticType
    {
        public ConstructorType(string name, string kind)
        {
            Name = name;
            Kind = kind;
        }

        public string Name { get; }

        /// <summary><c>component</c>나 <c>signal</c>.</summary>
        public string Kind { get; }

        public override bool Same(StaticType other) => other is ConstructorType c && c.Name == Name;
    }

    /// <summary>돌면 원소가 나오는 묶음: <c>query(...)</c>, 역방향 컬렉션, <c>list[Ref[X]]</c>, 리스트 컴프리헨션.</summary>
    internal sealed class GroupType : StaticType
    {
        public GroupType(StaticType element) => Element = element;
        public StaticType Element { get; }
        public override bool Same(StaticType other) => other is GroupType g && g.Element.Same(Element);
    }
}
