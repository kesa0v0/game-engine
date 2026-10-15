using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Schema
{
    /// <summary>문자열로 적은 이름 하나와 그 위치. 스키마 안의 다른 타입 이름은 문자열이다 (schema-design 3.2).</summary>
    public readonly struct NameRef
    {
        public NameRef(string name, SourceSpan span)
        {
            Name = name;
            Span = span;
        }

        public string Name { get; }
        public SourceSpan Span { get; }
    }

    // ---- 타입 표기 (spec S2: 파이썬 표기 그대로) ----

    /// <summary>스키마 안의 타입 표기. 스크립트에서는 <c>int</c>, <c>list[str]</c> 같은 값으로 보인다.</summary>
    public abstract class TypeRef : Value
    {
        public override string KindName => "type";

        /// <summary>파이썬 표기 (<c>dict[Ref["Resource"], fixed]</c>).</summary>
        public abstract string Display { get; }

        public override string ToString() => Display;
    }

    public sealed class PrimitiveTypeRef : TypeRef
    {
        public static readonly PrimitiveTypeRef Int = new PrimitiveTypeRef("int");
        public static readonly PrimitiveTypeRef Fixed = new PrimitiveTypeRef("fixed");
        public static readonly PrimitiveTypeRef Bool = new PrimitiveTypeRef("bool");
        public static readonly PrimitiveTypeRef Str = new PrimitiveTypeRef("str");
        public static readonly PrimitiveTypeRef TextKey = new PrimitiveTypeRef("TextKey");

        private PrimitiveTypeRef(string name) => Name = name;
        public string Name { get; }
        public override string Display => Name;
        public bool IsNumeric => ReferenceEquals(this, Int) || ReferenceEquals(this, Fixed);
    }

    public sealed class ListTypeRef : TypeRef
    {
        public ListTypeRef(TypeRef element) => Element = element;
        public TypeRef Element { get; }
        public override string Display => "list[" + Element.Display + "]";
    }

    public sealed class DictTypeRef : TypeRef
    {
        public DictTypeRef(TypeRef key, TypeRef value)
        {
            Key = key;
            Value = value;
        }

        public TypeRef Key { get; }
        public TypeRef Value { get; }
        public override string Display => "dict[" + Key.Display + ", " + Value.Display + "]";
    }

    /// <summary>
    /// <c>Ref["X"]</c>: Def 타입 X의 <b>엔티티</b>를 가리킨다. 어느 자리에서나 같은 뜻이다 (spec-gaps G32).
    /// 스키마에서 엔티티 Ref 칼럼은 <c>ref("X", ...)</c>로 선언하고, 이 표기는 <c>key=</c>와 <c>derived</c> 타입에 쓴다.
    /// </summary>
    public sealed class EntityRefTypeRef : TypeRef
    {
        public EntityRefTypeRef(NameRef target) => Target = target;
        public NameRef Target { get; }
        public override string Display => "Ref[\"" + Target.Name + "\"]";
    }

    /// <summary><c>DefId["X"]</c>: Def 타입 X의 <b>Def</b>를 id로 가리킨다. Def 값과 런타임 칼럼 모두에 쓴다 (spec-gaps G32).</summary>
    public sealed class DefIdTypeRef : TypeRef
    {
        public DefIdTypeRef(NameRef target) => Target = target;
        public NameRef Target { get; }
        public override string Display => "DefId[\"" + Target.Name + "\"]";
    }

    /// <summary>
    /// <c>T | None</c>: None 또는 T. 커맨드 매개변수에만 쓴다 (spec-gaps G114). 스키마의 칼럼은 <c>optional=True</c>로 적는다.
    /// </summary>
    public sealed class OptionalTypeRef : TypeRef
    {
        public OptionalTypeRef(TypeRef inner) => Inner = inner;
        public TypeRef Inner { get; }
        public override string Display => Inner.Display + " | None";
    }

    public enum FunctionSlotKind
    {
        /// <summary>확인용(순수) 함수 자리.</summary>
        Cond,

        /// <summary>바꾸는 함수 자리 (Flow 본문으로 실행).</summary>
        Action,
    }

    /// <summary>
    /// <c>Cond["X"]</c> / <c>Action["X"]</c>: 함수 값을 담는 Def 필드 (spec S2, schema-design 3.3.3).
    /// 타입 이름은 spec에서 미정이라 잠정 (spec-gaps G24).
    /// </summary>
    public sealed class FunctionTypeRef : TypeRef
    {
        public FunctionTypeRef(FunctionSlotKind kind, NameRef target)
        {
            Kind = kind;
            Target = target;
        }

        public FunctionSlotKind Kind { get; }
        public NameRef Target { get; }
        public override string Display => Kind + "[\"" + Target.Name + "\"]";
    }

    // ---- 값 선언 (spec S4: const / derived / field, S3: ref) ----

    public enum MemberKind
    {
        Const,
        Field,
        Derived,

        /// <summary><c>ref("X", ...)</c>: Ref 칼럼. 저장되는 칼럼이라 field의 한 종류다.</summary>
        Ref,
    }

    /// <summary>상속 병합 규칙 (spec S1 리스트 병합). const에만 의미가 있다.</summary>
    public enum MergeMode
    {
        Replace,
        Append,
    }

    /// <summary>
    /// <c>const(...)</c>/<c>field(...)</c>/<c>derived(...)</c>/<c>ref(...)</c>의 결과. 이름은 아직 없고,
    /// <c>deftype</c>/<c>component</c>/<c>extend</c>의 키워드 인자가 될 때 이름이 붙는다.
    /// </summary>
    public sealed class MemberSpec : Value
    {
        public MemberSpec(MemberKind kind, TypeRef type, SourceSpan span)
        {
            Kind = kind;
            Type = type;
            Span = span;
        }

        public MemberKind Kind { get; }

        /// <summary>값의 타입. Ref 칼럼이면 <see cref="EntityRefTypeRef"/>.</summary>
        public TypeRef Type { get; }

        /// <summary>선언 호출의 위치.</summary>
        public SourceSpan Span { get; }

        public Value? Default { get; internal set; }
        public NameRef? DefaultFrom { get; internal set; }
        public Value? Min { get; internal set; }
        public Value? Max { get; internal set; }
        public MergeMode Merge { get; internal set; } = MergeMode.Replace;

        /// <summary>derived의 계산 함수.</summary>
        public FunctionValue? Function { get; internal set; }

        // Ref 칼럼 전용
        public NameRef? Inverse { get; internal set; }
        public NameRef? OrderBy { get; internal set; }
        public bool Optional { get; internal set; }

        public override string KindName => Kind.ToString().ToLowerInvariant() + " declaration";
    }

    /// <summary>이름이 붙은 값 선언 하나.</summary>
    public sealed class MemberDecl
    {
        public MemberDecl(string name, SourceSpan span, string mod, MemberSpec spec)
        {
            Name = name;
            Span = span;
            Mod = mod;
            Spec = spec;
        }

        public string Name { get; }

        /// <summary>키워드 인자 이름의 위치.</summary>
        public SourceSpan Span { get; }

        /// <summary>선언한 모드 (<c>extend</c>로 더했으면 확장한 모드).</summary>
        public string Mod { get; }

        public MemberSpec Spec { get; }
        public MemberKind Kind => Spec.Kind;
    }

    // ---- 스키마 파일이 모은 선언 (②a 실행 결과, 아직 이름을 풀지 않음) ----

    public enum KeysMode
    {
        /// <summary>없는 키를 읽으면 기본값으로 채운 줄 (기본).</summary>
        All,

        /// <summary>없는 키를 읽으면 None.</summary>
        Listed,
    }

    /// <summary>키 컴포넌트의 키 종류 (spec S4, spec-gaps G31).</summary>
    public enum KeyKind
    {
        /// <summary><c>key="SkillDef"</c>: 그 타입의 Def마다 한 줄.</summary>
        Def,

        /// <summary><c>key=Ref["Faction"]</c>: 그 타입의 엔티티마다 한 줄. 항상 listed, 줄은 런타임에만 생긴다.</summary>
        Entity,

        /// <summary>
        /// <c>key=int</c>: 이름 없는 값의 순서 있는 목록 (spec-gaps G129). 항상 listed, 줄은 런타임에만 생기고
        /// <c>replace_rows</c>·<c>append_row</c>로 리스트처럼 쓴다.
        /// </summary>
        Int,
    }

    public sealed class ComponentDecl
    {
        internal ComponentDecl(string name, SourceSpan span, string mod, NameRef? key, KeyKind keyKind, KeysMode keys,
            IReadOnlyList<MemberDecl> members)
        {
            Name = name;
            Span = span;
            Mod = mod;
            Key = key;
            KeyKind = keyKind;
            Keys = keys;
            Members = members;
        }

        public string Name { get; }
        public SourceSpan Span { get; }
        public string Mod { get; }

        /// <summary>키 컴포넌트의 키 타입 (<c>key="SkillDef"</c> 또는 <c>key=Ref["Faction"]</c>). <c>key=int</c>면 없다.</summary>
        public NameRef? Key { get; }

        public KeyKind KeyKind { get; }

        public KeysMode Keys { get; }
        public IReadOnlyList<MemberDecl> Members { get; }
    }

    public sealed class DefTypeDecl
    {
        internal DefTypeDecl(string name, SourceSpan span, string mod, IReadOnlyList<MemberDecl> consts,
            IReadOnlyList<NameRef> components)
        {
            Name = name;
            Span = span;
            Mod = mod;
            Consts = consts;
            Components = components;
        }

        public string Name { get; }
        public SourceSpan Span { get; }
        public string Mod { get; }
        public IReadOnlyList<MemberDecl> Consts { get; }
        public IReadOnlyList<NameRef> Components { get; }
    }

    /// <summary><c>extend("Unit", components=[...], x=const(...))</c> (schema-design 4.2).</summary>
    public sealed class ExtensionDecl
    {
        internal ExtensionDecl(NameRef target, SourceSpan span, string mod, IReadOnlyList<MemberDecl> consts,
            IReadOnlyList<NameRef> components)
        {
            Target = target;
            Span = span;
            Mod = mod;
            Consts = consts;
            Components = components;
        }

        public NameRef Target { get; }
        public SourceSpan Span { get; }
        public string Mod { get; }
        public IReadOnlyList<MemberDecl> Consts { get; }
        public IReadOnlyList<NameRef> Components { get; }
    }

    /// <summary>모드 하나의 스키마 파일들이 선언한 것 (실행 순서).</summary>
    public sealed class ModSchema
    {
        public ModSchema(string mod) => Mod = mod;

        public string Mod { get; }
        public List<DefTypeDecl> Types { get; } = new List<DefTypeDecl>();
        public List<ComponentDecl> Components { get; } = new List<ComponentDecl>();
        public List<ExtensionDecl> Extensions { get; } = new List<ExtensionDecl>();
    }

    // ---- 완성된 스키마 (②a 끝) ----

    public sealed class ComponentType
    {
        private readonly Dictionary<string, MemberDecl> _members = new Dictionary<string, MemberDecl>(StringComparer.Ordinal);

        internal ComponentType(ComponentDecl decl)
        {
            Decl = decl;
            foreach (var m in decl.Members) _members[m.Name] = m;
        }

        public ComponentDecl Decl { get; }
        public string Name => Decl.Name;
        public string Mod => Decl.Mod;
        public IReadOnlyList<MemberDecl> Members => Decl.Members;
        public KeysMode Keys => Decl.Keys;

        /// <summary>키 컴포넌트면 키 타입 (<c>key=int</c>면 없다).</summary>
        public DefType? Key { get; internal set; }

        public bool IsKeyed => Decl.Key != null || Decl.KeyKind == KeyKind.Int;
        public KeyKind KeyKind => Decl.KeyKind;

        /// <summary>메시지에 쓰는 키 이름: 키 타입 이름, 또는 <c>int</c>.</summary>
        public string KeyDisplay => KeyKind == KeyKind.Int ? "int" : Key?.Name ?? Decl.Key?.Name ?? "?";

        public bool TryGetMember(string name, out MemberDecl member) => _members.TryGetValue(name, out member!);
    }

    /// <summary>Def 타입에 붙은 컴포넌트 하나와 붙인 모드.</summary>
    public sealed class ComponentSlot
    {
        internal ComponentSlot(ComponentType component, string mod, SourceSpan span)
        {
            Component = component;
            Mod = mod;
            Span = span;
        }

        public ComponentType Component { get; }

        /// <summary>붙인 모드: 타입을 선언한 모드이거나 <c>extend</c>한 모드.</summary>
        public string Mod { get; }

        public SourceSpan Span { get; }
    }

    /// <summary>Ref 칼럼의 <c>inverse=</c>로 대상 타입에 생기는 역방향 컬렉션 (spec S3).</summary>
    public sealed class InverseRelation
    {
        internal InverseRelation(string name, ComponentType source, MemberDecl member, NameRef span)
        {
            Name = name;
            Source = source;
            Member = member;
            Span = span.Span;
        }

        public string Name { get; }

        /// <summary>Ref 칼럼이 든 컴포넌트 (원소 쪽).</summary>
        public ComponentType Source { get; }

        public MemberDecl Member { get; }
        public string Mod => Member.Mod;
        public SourceSpan Span { get; }
    }

    public sealed class DefType
    {
        internal DefType(DefTypeDecl decl) => Decl = decl;

        public DefTypeDecl Decl { get; }
        public string Name => Decl.Name;
        public string Mod => Decl.Mod;

        /// <summary>선언의 const와 <c>extend</c>로 더한 const (로드 순서).</summary>
        public List<MemberDecl> Consts { get; } = new List<MemberDecl>();

        /// <summary>선언의 컴포넌트와 <c>extend</c>로 붙인 컴포넌트 (로드 순서).</summary>
        public List<ComponentSlot> Components { get; } = new List<ComponentSlot>();

        public List<InverseRelation> Inverses { get; } = new List<InverseRelation>();

        /// <summary>컴포넌트가 없으면 데이터 전용 (엔티티가 되지 않음, spec S4).</summary>
        public bool IsDataOnly => Components.Count == 0;
    }

    /// <summary>②a가 끝난 뒤의 스키마. 이후 단계에서 타입은 바뀌지 않는다.</summary>
    public sealed class SchemaSet
    {
        private readonly Dictionary<string, DefType> _types = new Dictionary<string, DefType>(StringComparer.Ordinal);
        private readonly Dictionary<string, ComponentType> _components =
            new Dictionary<string, ComponentType>(StringComparer.Ordinal);
        private readonly List<DefType> _typeList = new List<DefType>();
        private readonly List<ComponentType> _componentList = new List<ComponentType>();

        /// <summary>로드 순서, 같은 모드 안에서는 선언 순서.</summary>
        public IReadOnlyList<DefType> Types => _typeList;

        public IReadOnlyList<ComponentType> Components => _componentList;

        public bool TryGetType(string name, out DefType type) => _types.TryGetValue(name, out type!);

        public bool TryGetComponent(string name, out ComponentType component) =>
            _components.TryGetValue(name, out component!);

        internal void Add(DefType type)
        {
            _types.Add(type.Name, type);
            _typeList.Add(type);
        }

        internal void Add(ComponentType component)
        {
            _components.Add(component.Name, component);
            _componentList.Add(component);
        }
    }
}
