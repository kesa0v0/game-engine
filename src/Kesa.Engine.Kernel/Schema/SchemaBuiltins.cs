using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Schema
{
    /// <summary>
    /// 스키마 파일(②a)에서 쓰는 내장 이름 (spec S2 스키마 선언, schema-design 3.2).
    /// 선언은 <see cref="ModSchema"/>에 모으기만 하고, 이름 풀이와 충돌 검사는 모든 모드의 스키마 파일을
    /// 실행한 뒤 <see cref="SchemaBuilder"/>가 한다 (스키마 안의 이름은 문자열이라 선언 순서와 무관).
    /// </summary>
    public sealed class SchemaBuiltins : IBuiltins
    {
        public const string DefType = "deftype";
        public const string Component = "component";
        public const string Extend = "extend";

        /// <summary>Def 생성자 키워드 중 필드 이름으로 쓸 수 없는 것 (spec S1 상속·중복 ID).</summary>
        public static readonly IReadOnlyList<string> ReservedDefKeywords = new[] { "abstract", "override", "parent" };

        private static readonly string[] ConstOptions = { "default", "max", "merge", "min" };
        private static readonly string[] FieldOptions = { "default", "default_from", "max", "min" };
        private static readonly string[] RefOptions = { "inverse", "optional", "order_by" };

        private readonly Dictionary<string, Value> _names = new Dictionary<string, Value>(StringComparer.Ordinal);
        private readonly List<string> _nameList = new List<string>();
        private readonly ModSchema _target;
        private readonly Func<string, bool> _isDeclaredType;
        private readonly Func<string, bool> _isDeclaredComponent;

        /// <param name="target">이 모드의 선언을 모을 곳</param>
        /// <param name="isDeclaredType">지금까지 어느 모드든 선언한 Def 타입 이름인가 (진단 메시지용)</param>
        /// <param name="isDeclaredComponent">지금까지 어느 모드든 선언한 컴포넌트 이름인가 (진단 메시지용)</param>
        public SchemaBuiltins(ModSchema target, Func<string, bool> isDeclaredType, Func<string, bool> isDeclaredComponent)
        {
            _target = target;
            _isDeclaredType = isDeclaredType;
            _isDeclaredComponent = isDeclaredComponent;

            Add(new BuiltinFunction(DefType, DeclareType));
            Add(new BuiltinFunction(Component, DeclareComponent));
            Add(new BuiltinFunction(Extend, DeclareExtension));
            Add(new BuiltinFunction("const", MakeConst));
            Add(new BuiltinFunction("field", MakeField));
            Add(new BuiltinFunction("derived", MakeDerived));
            Add(new BuiltinFunction("ref", MakeRef));
            foreach (var pair in TypeNames()) Add(pair.Key, pair.Value);
        }

        /// <summary>스키마 파일 전용 이름 전체. Def 파일에서 쓰면 이유를 알려 주는 데 쓴다.</summary>
        public static IEnumerable<string> AllNames
        {
            get
            {
                yield return DefType;
                yield return Component;
                yield return Extend;
                yield return "const";
                yield return "field";
                yield return "derived";
                yield return "ref";
                foreach (var pair in TypeNames()) yield return pair.Key;
            }
        }

        private static IEnumerable<KeyValuePair<string, Value>> TypeNames()
        {
            yield return new KeyValuePair<string, Value>("int", PrimitiveTypeRef.Int);
            yield return new KeyValuePair<string, Value>("fixed", PrimitiveTypeRef.Fixed);
            yield return new KeyValuePair<string, Value>("bool", PrimitiveTypeRef.Bool);
            yield return new KeyValuePair<string, Value>("str", PrimitiveTypeRef.Str);
            yield return new KeyValuePair<string, Value>("TextKey", PrimitiveTypeRef.TextKey);
            yield return new KeyValuePair<string, Value>("list", GenericType.List);
            yield return new KeyValuePair<string, Value>("dict", GenericType.Dict);
            yield return new KeyValuePair<string, Value>("Ref", GenericType.Ref);
            yield return new KeyValuePair<string, Value>("DefId", GenericType.DefId);
            yield return new KeyValuePair<string, Value>("Cond", GenericType.Cond);
            yield return new KeyValuePair<string, Value>("Action", GenericType.Action);
        }

        private void Add(BuiltinFunction f) => Add(f.Name, f);

        private void Add(string name, Value value)
        {
            _names.Add(name, value);
            _nameList.Add(name);
        }

        public Value? Lookup(string name) => _names.TryGetValue(name, out var v) ? v : null;

        public IEnumerable<string> Names => _nameList;

        public (string Code, string Message)? Explain(string name)
        {
            if (name == Interpreter.PatchName)
                return (Codes.SchDefInSchemaFile,
                    "patch.* calls belong in Def files; schema files only declare types");
            if (name == Interpreter.ModLoadedName)
                return (Codes.SchModLoadedInSchemaFile,
                    "mod_loaded() cannot be used in schema files: types must not depend on which mods are installed");
            if (_isDeclaredType(name))
                return (Codes.SchDefInSchemaFile,
                    "Defs cannot be created in schema files; move '" + name + "(...)' to a Def file outside schema/");
            if (_isDeclaredComponent(name))
                return (Codes.SchDefInSchemaFile,
                    "component values belong in Def files; schema files only declare types");
            return null;
        }

        // ---- 선언 ----

        private Value DeclareType(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, DefType, span, args);
            r.ExpectPositional(1, "deftype(\"Name\", components=[...], field=const(...))");
            var name = ReadTypeName(ctx, r, "Def type name");
            var components = ReadComponentList(ctx, r, DefType);
            var consts = new List<MemberDecl>();
            foreach (var arg in r.Rest()) consts.Add(ReadConstMember(ctx, arg, "a Def type"));
            _target.Types.Add(new DefTypeDecl(name.Name, name.Span, ctx.Mod, consts, components));
            return NoneValue.Instance;
        }

        private Value DeclareComponent(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, Component, span, args);
            r.ExpectPositional(1, "component(\"Name\", value=field(...))");
            var name = ReadTypeName(ctx, r, "component name");
            // 컴포넌트 이름은 Def 리터럴의 키워드 인자가 된다 (Unit("A", Health=Health(...)))
            RejectReserved(ctx, name.Name, name.Span);

            // 키 종류는 표기로 가른다: 문자열은 Def 키, Ref["X"]는 엔티티 키 (spec-gaps G31)
            NameRef? key = null;
            var keyKind = KeyKind.Def;
            var keyArg = r.Take("key");
            RejectReservedOption(ctx, keyArg, Component);
            if (keyArg != null)
            {
                switch (keyArg.Value)
                {
                    case StrValue _:
                        key = new NameRef(r.String(keyArg, "key"), ArgReader.SpanOf(keyArg));
                        break;
                    case EntityRefTypeRef entity:
                        key = entity.Target;
                        keyKind = KeyKind.Entity;
                        break;
                    default:
                        throw ctx.Fail(Codes.EvlType,
                            "key must be a Def type name or an entity reference, not " + keyArg.Value.KindName,
                            keyArg.Span, hint: "write key=\"SkillDef\" (a row per Def) or key=Ref[\"Faction\"] (a row per entity)");
                }
            }

            // 엔티티 키는 항상 listed다: 엔티티는 런타임에 생기므로 "모든 키의 줄"이 없다
            var keys = keyKind == KeyKind.Entity ? KeysMode.Listed : KeysMode.All;
            var keysArg = r.Take("keys");
            RejectReservedOption(ctx, keysArg, Component);
            if (keysArg != null)
            {
                switch (r.String(keysArg, "keys"))
                {
                    case "all": keys = KeysMode.All; break;
                    case "listed": keys = KeysMode.Listed; break;
                    default:
                        throw ctx.Fail(Codes.SchBadDeclaration, "keys must be \"all\" or \"listed\"", keysArg.Span);
                }
                if (key == null)
                    throw ctx.Fail(Codes.SchBadDeclaration, "keys= is only meaningful on a keyed component",
                        keysArg.Span, hint: "add key=\"DefType\"");
                if (keyKind == KeyKind.Entity && keys == KeysMode.All)
                    throw ctx.Fail(Codes.SchBadDeclaration, "a component keyed by entities is always keys=\"listed\"",
                        keysArg.Span, hint: "remove keys=\"all\"");
            }

            var members = new List<MemberDecl>();
            foreach (var arg in r.Rest())
            {
                if (!(arg.Value is MemberSpec spec))
                    throw NotADeclaration(ctx, arg);
                members.Add(new MemberDecl(arg.Name!, arg.Span, ctx.Mod, spec));
            }
            _target.Components.Add(new ComponentDecl(name.Name, name.Span, ctx.Mod, key, keyKind, keys, members));
            return NoneValue.Instance;
        }

        private Value DeclareExtension(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, Extend, span, args);
            r.ExpectPositional(1, "extend(\"Type\", components=[...], field=const(...))");
            var targetArg = r.Positional[0];
            var target = new NameRef(r.String(targetArg, "the extended type"), ArgReader.SpanOf(targetArg));
            var components = ReadComponentList(ctx, r, Extend);
            var consts = new List<MemberDecl>();
            foreach (var arg in r.Rest()) consts.Add(ReadConstMember(ctx, arg, "an extended Def type"));
            if (components.Count == 0 && consts.Count == 0)
                ctx.Error(Codes.SchBadDeclaration, "extend() adds nothing", span);
            _target.Extensions.Add(new ExtensionDecl(target, span, ctx.Mod, consts, components));
            return NoneValue.Instance;
        }

        private NameRef ReadTypeName(CallContext ctx, ArgReader r, string what)
        {
            var arg = r.Positional[0];
            var name = r.String(arg, what);
            var nameSpan = ArgReader.SpanOf(arg);
            if (!IsIdentifier(name) || Parser.IsKeyword(name))
                throw ctx.Fail(Codes.SchBadDeclaration, what + " must be an identifier, not '" + name + "'", nameSpan,
                    hint: "names become constructors in Def files, e.g. Unit(\"Scout\")");
            foreach (var builtin in AllNames)
            {
                if (builtin == name)
                    throw ctx.Fail(Codes.SchBadDeclaration, "'" + name + "' is a built-in name", nameSpan);
            }
            return new NameRef(name, nameSpan);
        }

        private static List<NameRef> ReadComponentList(CallContext ctx, ArgReader r, string callee)
        {
            var result = new List<NameRef>();
            var arg = r.Take("components");
            RejectReservedOption(ctx, arg, callee);
            if (arg == null) return result;
            foreach (var (name, span) in r.StringList(arg, "components")) result.Add(new NameRef(name, span));
            return result;
        }

        /// <summary>Def 타입 바로 아래에는 const만 온다 (spec S4).</summary>
        private static MemberDecl ReadConstMember(CallContext ctx, ArgValue arg, string where)
        {
            if (!(arg.Value is MemberSpec spec)) throw NotADeclaration(ctx, arg);
            if (spec.Kind != MemberKind.Const)
            {
                throw ctx.Fail(Codes.SchBadDeclaration,
                    "'" + arg.Name + "': " + where + " holds const values only; " +
                    (spec.Kind == MemberKind.Ref ? "ref" : spec.Kind.ToString().ToLowerInvariant()) +
                    " values belong in a component", arg.Span);
            }
            RejectReserved(ctx, arg.Name!, arg.Span);
            return new MemberDecl(arg.Name!, arg.Span, ctx.Mod, spec);
        }

        /// <summary>
        /// 선언 함수의 옵션 이름(<c>components</c>, <c>key</c>, <c>keys</c>)에 값 선언이 오면, 모더는 그 이름의 멤버를
        /// 만들려던 것이다. 옵션 값이 틀렸다는 엉뚱한 오류 대신 이름이 예약되었다고 알린다 (spec-gaps G25).
        /// </summary>
        private static void RejectReservedOption(CallContext ctx, ArgValue? arg, string callee)
        {
            if (arg?.Value is MemberSpec)
                throw ctx.Fail(Codes.SchBadDeclaration,
                    "'" + arg.Name + "' is reserved in " + callee + "(); choose another name for this value", arg.Span);
        }

        /// <summary>Def 리터럴의 키워드 인자가 되는 이름(const, 컴포넌트)은 Def 메타 정보 이름과 겹칠 수 없다 (spec-gaps G25).</summary>
        private static void RejectReserved(CallContext ctx, string name, SourceSpan span)
        {
            foreach (var reserved in ReservedDefKeywords)
            {
                if (name == reserved)
                    throw ctx.Fail(Codes.SchBadDeclaration, "'" + reserved + "' is reserved for Def inheritance and overrides",
                        span);
            }
        }

        private static ScriptError NotADeclaration(CallContext ctx, ArgValue arg) =>
            ctx.Fail(Codes.SchBadDeclaration,
                "'" + arg.Name + "' must be declared with const(...), field(...), derived(...) or ref(...), not a " +
                arg.Value.KindName, arg.Span);

        // ---- 값 선언 ----

        private static TypeRef ReadType(CallContext ctx, ArgValue arg)
        {
            if (arg.Value is TypeRef t) return t;
            throw ctx.Fail(Codes.EvlType, "expected a type such as int, fixed or list[str], not " + arg.Value.KindName,
                arg.Span);
        }

        private static Value MakeConst(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, "const", span, args);
            r.ExpectPositional(1, "const(int, default=0)");
            var typeArg = r.Positional[0];
            var type = ReadType(ctx, typeArg);
            if (ContainsEntityRef(type))
            {
                // Def 값은 로드 시점에 정해지는데 그때 엔티티는 없다
                throw ctx.Fail(Codes.SchBadDeclaration, "a const value cannot hold entities", typeArg.Span,
                    hint: "to refer to a Def, write DefId[\"X\"] (spec-gaps G32)");
            }
            var spec = new MemberSpec(MemberKind.Const, type, span);
            ReadDefaultMinMax(ctx, r, spec);

            var merge = r.Take("merge");
            if (merge != null)
            {
                switch (r.String(merge, "merge"))
                {
                    case "replace": spec.Merge = MergeMode.Replace; break;
                    case "append":
                        if (!(spec.Type is ListTypeRef))
                            throw ctx.Fail(Codes.SchBadDeclaration, "merge=\"append\" needs a list type, not " +
                                                                    spec.Type.Display, merge.Span);
                        spec.Merge = MergeMode.Append;
                        break;
                    default:
                        throw ctx.Fail(Codes.SchBadDeclaration, "merge must be \"replace\" or \"append\"", merge.Span);
                }
            }
            r.RejectRest(ConstOptions);
            return spec;
        }

        private static Value MakeField(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, "field", span, args);
            r.ExpectPositional(1, "field(int, default=0)");
            var typeArg = r.Positional[0];
            var type = ReadType(ctx, typeArg);
            if (type is ListTypeRef || type is DictTypeRef)
            {
                throw ctx.Fail(Codes.SchBadDeclaration, "runtime fields cannot hold lists or maps", typeArg.Span,
                    hint: "use element entities with ref(..., inverse=...) or a keyed component (spec S4)");
            }
            if (type is EntityRefTypeRef entity)
            {
                // 엔티티 Ref 칼럼을 선언하는 방법은 하나로 둔다 (inverse 등 옵션은 ref()만 받는다)
                throw ctx.Fail(Codes.SchBadDeclaration, "declare entity reference columns with ref(...)", typeArg.Span,
                    hint: "write ref(\"" + entity.Target.Name + "\")");
            }
            RejectFunctionType(ctx, type, typeArg);
            var spec = new MemberSpec(MemberKind.Field, type, span);
            ReadDefaultMinMax(ctx, r, spec);

            var from = r.Take("default_from");
            if (from != null)
            {
                if (spec.Default != null)
                    throw ctx.Fail(Codes.SchBadDeclaration, "default and default_from cannot both be given", from.Span);
                spec.DefaultFrom = new NameRef(r.String(from, "default_from"), ArgReader.SpanOf(from));
            }
            r.RejectRest(FieldOptions);
            return spec;
        }

        private static Value MakeDerived(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, "derived", span, args);
            r.ExpectPositional(2, "derived(int, function_name)");
            var typeArg = r.Positional[0];
            var type = ReadType(ctx, typeArg);
            RejectFunctionType(ctx, type, typeArg);
            var fnArg = r.Positional[1];
            if (!(fnArg.Value is FunctionValue fn))
                throw ctx.Fail(Codes.EvlType, "derived() needs a function (def or lambda), not " + fnArg.Value.KindName,
                    fnArg.Span);
            r.RejectRest(Array.Empty<string>());
            return new MemberSpec(MemberKind.Derived, type, span) { Function = fn };
        }

        private static Value MakeRef(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, "ref", span, args);
            r.ExpectPositional(1, "ref(\"Type\", inverse=\"name\")");
            var targetArg = r.Positional[0];
            var target = new NameRef(r.String(targetArg, "the referenced type"), ArgReader.SpanOf(targetArg));
            var spec = new MemberSpec(MemberKind.Ref, new EntityRefTypeRef(target), span);

            var inverse = r.Take("inverse");
            if (inverse != null)
            {
                var name = r.String(inverse, "inverse");
                if (!IsIdentifier(name) || Parser.IsKeyword(name))
                    throw ctx.Fail(Codes.SchBadDeclaration, "inverse must be an identifier, not '" + name + "'",
                        ArgReader.SpanOf(inverse));
                spec.Inverse = new NameRef(name, ArgReader.SpanOf(inverse));
            }
            var optional = r.Take("optional");
            if (optional != null) spec.Optional = r.Bool(optional, "optional");
            var orderBy = r.Take("order_by");
            if (orderBy != null)
            {
                if (spec.Inverse == null)
                    throw ctx.Fail(Codes.SchBadDeclaration, "order_by orders the inverse collection; add inverse=",
                        orderBy.Span);
                spec.OrderBy = new NameRef(r.String(orderBy, "order_by"), ArgReader.SpanOf(orderBy));
            }
            r.RejectRest(RefOptions);
            return spec;
        }

        private static void ReadDefaultMinMax(CallContext ctx, ArgReader r, MemberSpec spec)
        {
            var d = r.Take("default");
            if (d != null) spec.Default = d.Value;
            spec.Min = ReadBound(ctx, r, spec, "min");
            spec.Max = ReadBound(ctx, r, spec, "max");
        }

        private static Value? ReadBound(CallContext ctx, ArgReader r, MemberSpec spec, string name)
        {
            var arg = r.Take(name);
            if (arg == null) return null;
            if (!HoldsNumbers(spec.Type))
            {
                throw ctx.Fail(Codes.SchBadDeclaration,
                    name + "= needs an int or fixed type (or a list or map of them), not " + spec.Type.Display, arg.Span);
            }
            if (!(arg.Value is IntValue) && !(arg.Value is FixedValue))
                throw ctx.Fail(Codes.EvlType, name + " must be a number, not " + arg.Value.KindName +
                    ". A bound that follows another value (such as a derived max) is not supported: " +
                    "store the damage and derive the current value, or clamp in a reaction (spec-gaps G99)", arg.Span);
            return arg.Value;
        }

        /// <summary>
        /// <c>min</c>/<c>max</c>를 줄 수 있는 타입: 숫자이거나, 숫자를 원소·값으로 갖는 리스트·맵이다 (spec-gaps G68).
        /// </summary>
        private static bool HoldsNumbers(TypeRef type)
        {
            switch (type)
            {
                case PrimitiveTypeRef p: return p.IsNumeric;
                case ListTypeRef list: return HoldsNumbers(list.Element);
                case DictTypeRef dict: return HoldsNumbers(dict.Value);
            }
            return false;
        }

        /// <summary>함수 값은 Def(const)에만 둔다 (spec S2).</summary>
        private static void RejectFunctionType(CallContext ctx, TypeRef type, ArgValue arg)
        {
            if (ContainsFunctionType(type))
                throw ctx.Fail(Codes.SchBadDeclaration, "function values can only be const values of a Def", arg.Span);
        }

        private static bool ContainsEntityRef(TypeRef type)
        {
            switch (type)
            {
                case EntityRefTypeRef _: return true;
                case ListTypeRef l: return ContainsEntityRef(l.Element);
                case DictTypeRef d: return ContainsEntityRef(d.Key) || ContainsEntityRef(d.Value);
            }
            return false;
        }

        private static bool ContainsFunctionType(TypeRef type)
        {
            switch (type)
            {
                case FunctionTypeRef _: return true;
                case ListTypeRef l: return ContainsFunctionType(l.Element);
                case DictTypeRef d: return ContainsFunctionType(d.Key) || ContainsFunctionType(d.Value);
            }
            return false;
        }

        /// <summary>ASCII 식별자 (spec-gaps G14).</summary>
        public static bool IsIdentifier(string name)
        {
            if (name.Length == 0) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (i > 0 && c >= '0' && c <= '9');
                if (!ok) return false;
            }
            return true;
        }
    }

    /// <summary><c>list</c>, <c>dict</c>, <c>Ref</c>, <c>Cond</c>, <c>Action</c>: <c>[...]</c>로 타입을 만든다.</summary>
    public sealed class GenericType : Value, ISubscriptable
    {
        public static readonly GenericType List = new GenericType("list");
        public static readonly GenericType Dict = new GenericType("dict");
        public static readonly GenericType Ref = new GenericType("Ref");
        public static readonly GenericType DefId = new GenericType("DefId");
        public static readonly GenericType Cond = new GenericType("Cond");
        public static readonly GenericType Action = new GenericType("Action");

        private GenericType(string name) => Name = name;

        public string Name { get; }
        public override string KindName => "generic type '" + Name + "' (write " + Name + "[...])";

        public Value Subscript(CallContext ctx, SourceSpan span, IReadOnlyList<Value> indices)
        {
            switch (Name)
            {
                case "list":
                    Expect(ctx, span, indices, 1, "list[str]");
                    return new ListTypeRef(AsType(ctx, span, indices[0]));
                case "dict":
                    Expect(ctx, span, indices, 2, "dict[str, int]");
                    var key = AsType(ctx, span, indices[0]);
                    // Def 리터럴의 맵 키는 str·int뿐이다 (spec-gaps G22). DefId는 리터럴에서 id 문자열로 적는다.
                    if (!ReferenceEquals(key, PrimitiveTypeRef.Str) && !ReferenceEquals(key, PrimitiveTypeRef.Int) &&
                        !(key is DefIdTypeRef))
                    {
                        throw ctx.Fail(Codes.SchBadDeclaration, "map keys must be str, int or DefId[...], not " + key.Display,
                            span);
                    }
                    return new DictTypeRef(key, AsType(ctx, span, indices[1]));
                case "Ref":
                    Expect(ctx, span, indices, 1, "Ref[\"Type\"]");
                    return new EntityRefTypeRef(AsName(ctx, span, indices[0]));
                case "DefId":
                    Expect(ctx, span, indices, 1, "DefId[\"Type\"]");
                    return new DefIdTypeRef(AsName(ctx, span, indices[0]));
                case "Cond":
                    Expect(ctx, span, indices, 1, "Cond[\"Type\"]");
                    return new FunctionTypeRef(FunctionSlotKind.Cond, AsName(ctx, span, indices[0]));
                default:
                    Expect(ctx, span, indices, 1, "Action[\"Type\"]");
                    return new FunctionTypeRef(FunctionSlotKind.Action, AsName(ctx, span, indices[0]));
            }
        }

        private void Expect(CallContext ctx, SourceSpan span, IReadOnlyList<Value> indices, int count, string usage)
        {
            if (indices.Count != count)
                throw ctx.Fail(Codes.EvlType, Name + "[...] takes " + count + " type argument" + (count == 1 ? "" : "s"),
                    span, hint: "write " + usage);
        }

        private TypeRef AsType(CallContext ctx, SourceSpan span, Value v)
        {
            if (v is TypeRef t) return t;
            throw ctx.Fail(Codes.EvlType, "expected a type inside " + Name + "[...], not " + v.KindName, span,
                hint: v is StrValue s
                    ? "write DefId[\"" + s.Value + "\"] for a Def or Ref[\"" + s.Value + "\"] for an entity"
                    : null);
        }

        private NameRef AsName(CallContext ctx, SourceSpan span, Value v)
        {
            if (v is StrValue s) return new NameRef(s.Value, s.Origin.File.Length == 0 ? span : s.Origin);
            throw ctx.Fail(Codes.EvlType, Name + "[...] takes a type name as a string", span,
                hint: "write " + Name + "[\"Type\"] (schema-design 3.2)");
        }
    }
}
