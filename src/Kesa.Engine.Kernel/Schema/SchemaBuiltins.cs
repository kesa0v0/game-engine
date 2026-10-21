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
        public const string Signal = "signal";
        public const string Stage = "stage";
        public const string UiState = "ui_state";

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
            Add(new BuiltinFunction(Signal, DeclareSignal));
            Add(new BuiltinFunction(Stage, DeclareStage));
            Add(new BuiltinFunction(UiState, DeclareUiState));
            Add(new BuiltinFunction("const", MakeConst));
            Add(new BuiltinFunction("field", MakeField));
            Add(new BuiltinFunction("derived", MakeDerived));
            Add(new BuiltinFunction("ref", MakeRef));
            Add(ContribFunction.Instance); // const의 기본값에 쓴다 (G295). Def 파일에서도 보인다
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
                yield return Signal;
                yield return Stage;
                yield return UiState;
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
            yield return new KeyValuePair<string, Value>("Contrib", ContribTypeRef.Instance);
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
                    case PrimitiveTypeRef p when ReferenceEquals(p, PrimitiveTypeRef.Int):
                        // 이름 없는 값의 순서 있는 목록 (G129)
                        keyKind = KeyKind.Int;
                        break;
                    default:
                        throw ctx.Fail(Codes.EvlType,
                            "key must be a Def type name, an entity reference or int, not " + keyArg.Value.KindName,
                            keyArg.Span, hint: "write key=\"SkillDef\" (a row per Def), key=Ref[\"Faction\"] (a row per entity) " +
                                               "or key=int (an ordered list of values)");
                }
            }

            // 엔티티 키와 int 키는 항상 listed다: 줄이 런타임에 생기므로 "모든 키의 줄"이 없다
            bool runtimeKeys = keyKind != KeyKind.Def;
            var keys = runtimeKeys ? KeysMode.Listed : KeysMode.All;
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
                if (keyArg == null)
                    throw ctx.Fail(Codes.SchBadDeclaration, "keys= is only meaningful on a keyed component",
                        keysArg.Span, hint: "add key=\"DefType\"");
                if (runtimeKeys && keys == KeysMode.All)
                    throw ctx.Fail(Codes.SchBadDeclaration,
                        "a component keyed by " + (keyKind == KeyKind.Int ? "int" : "entities") + " is always keys=\"listed\"",
                        keysArg.Span, hint: "remove keys=\"all\"");
            }

            var members = new List<MemberDecl>();
            foreach (var arg in r.Rest())
            {
                if (!(arg.Value is MemberSpec spec))
                    throw NotADeclaration(ctx, arg);
                if (spec.Kind == MemberKind.Field && (spec.Type is ListTypeRef || spec.Type is DictTypeRef))
                {
                    throw ctx.Fail(Codes.SchBadDeclaration, "runtime fields cannot hold lists or maps", spec.TypeSpan,
                        hint: "use element entities with ref(..., inverse=...) or a keyed component (spec S4)");
                }
                RejectEntityKeys(ctx, spec.Type, spec.TypeSpan);
                // 키 줄의 보너스는 키 Def가 든다 (G295): 줄마다의 칸에 두면 어느 줄이 낸 것인지 겹친다
                if (keyArg != null && IsContribList(spec.Type))
                    throw ctx.Fail(Codes.SchBadDeclaration, "a keyed component cannot declare contributions in its rows",
                        spec.TypeSpan, hint: "a Def-keyed row takes the contributions declared on its key Def (spec-gaps G295)");
                members.Add(new MemberDecl(arg.Name!, arg.Span, ctx.Mod, spec));
            }
            _target.Components.Add(new ComponentDecl(name.Name, name.Span, ctx.Mod, key, keyKind, keys, members));
            return NoneValue.Instance;
        }

        /// <summary>
        /// <c>ui_state("Selection", unit=ref("Unit", optional=True), panel=field(bool, default=False))</c> (spec-gaps G277·G279).
        /// 칸은 <c>field</c>(리스트·사전, 엔티티 키도 됨)와 <c>optional=True</c>인 <c>ref</c>뿐이고 모두 기본값이 있어야 한다:
        /// 엔티티의 <c>spawn</c>처럼 처음 값을 줄 때가 없다.
        /// </summary>
        private Value DeclareUiState(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            const string hint = "a UI state is never created like an entity, so every value starts from its default (spec-gaps G279)";
            var r = new ArgReader(ctx, UiState, span, args);
            r.ExpectPositional(1, "ui_state(\"Name\", value=field(int, default=0), unit=ref(\"Unit\", optional=True))");
            var name = ReadTypeName(ctx, r, "UI state name");
            var members = new List<MemberDecl>();
            foreach (var arg in r.Rest())
            {
                if (!(arg.Value is MemberSpec spec))
                    throw ctx.Fail(Codes.SchBadDeclaration,
                        "'" + arg.Name + "' must be declared with field(...) or ref(...), not a " + arg.Value.KindName, arg.Span);
                switch (spec.Kind)
                {
                    case MemberKind.Field:
                        if (spec.DefaultFrom != null)
                            throw ctx.Fail(Codes.SchBadDeclaration, "'" + arg.Name + "' cannot use default_from in a UI state", arg.Span,
                                hint: "there is no Def to read it from; write default=... (spec-gaps G279)");
                        if (spec.Default is FunctionValue)
                            throw ctx.Fail(Codes.SchBadDeclaration, "'" + arg.Name + "' cannot have a function default in a UI state",
                                arg.Span, hint: "write a value: default=0, default=[] (spec-gaps G279)");
                        if (spec.Default == null)
                            throw ctx.Fail(Codes.SchBadDeclaration, "'" + arg.Name + "' needs a default", arg.Span, hint: hint);
                        break;
                    case MemberKind.Ref:
                        if (spec.Inverse != null)
                            throw ctx.Fail(Codes.SchBadDeclaration, "a UI state's ref cannot have inverse=: the entity does not see " +
                                                                    "who points at it from a screen", arg.Span);
                        if (!spec.Optional)
                            throw ctx.Fail(Codes.SchBadDeclaration, "'" + arg.Name + "' must be ref(..., optional=True)", arg.Span,
                                hint: "it starts as None, and it reads as None again when its entity is gone (spec-gaps G279)");
                        break;
                    default:
                        throw ctx.Fail(Codes.SchBadDeclaration,
                            "'" + arg.Name + "': a UI state holds field(...) and ref(...) only, not " +
                            spec.Kind.ToString().ToLowerInvariant() + " values", arg.Span,
                            hint: "read Def values and derived values from the game where you need them (spec-gaps G277)");
                }
                members.Add(new MemberDecl(arg.Name!, arg.Span, ctx.Mod, spec));
            }
            _target.UiStates.Add(new UiStateDecl(name.Name, name.Span, ctx.Mod, members));
            return NoneValue.Instance;
        }

        /// <summary>엔티티를 키로 쓰는 사전은 ui_state의 칸에서만 된다 (G277): 저장되는 값과 신호의 키는 str·int·DefId다.</summary>
        private static void RejectEntityKeys(CallContext ctx, TypeRef type, SourceSpan span)
        {
            switch (type)
            {
                case DictTypeRef d:
                    if (d.Key is EntityRefTypeRef)
                        throw ctx.Fail(Codes.SchBadDeclaration, "map keys must be str, int or DefId[...], not " + d.Key.Display, span,
                            hint: "entity keys are only for ui_state fields (spec-gaps G277)");
                    RejectEntityKeys(ctx, d.Value, span);
                    break;
                case ListTypeRef l:
                    RejectEntityKeys(ctx, l.Element, span);
                    break;
            }
        }

        /// <summary>
        /// <c>signal("Damaged", target=Ref[Unit], amount=int)</c> (spec S5, spec-gaps G145). 이름은 Def 타입·컴포넌트와 한 이름
        /// 공간이고 인자는 <c>이름=타입</c>이다 (모두 필수, 기본값 없음).
        /// </summary>
        private Value DeclareSignal(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, Signal, span, args);
            r.ExpectPositional(1, "signal(\"Name\", target=Ref[\"Unit\"], amount=int)");
            var name = ReadTypeName(ctx, r, "signal name");
            var fields = new List<SignalField>();
            foreach (var arg in r.Rest())
            {
                if (!(arg.Value is TypeRef type))
                    throw ctx.Fail(Codes.EvlType, "a signal value is declared with its type, like " + arg.Name + "=int, not " +
                                                  arg.Value.KindName, arg.Span,
                        hint: "signal values have no defaults: every emit gives all of them (spec-gaps G145)");
                if (ContainsFunctionType(type))
                    throw ctx.Fail(Codes.SchBadDeclaration, "a signal cannot carry function values", arg.Span);
                RejectEntityKeys(ctx, type, arg.Span);
                fields.Add(new SignalField(arg.Name!, type, arg.Span));
            }
            _target.Signals.Add(new SignalDecl(name.Name, name.Span, ctx.Mod, fields));
            return NoneValue.Instance;
        }

        /// <summary>
        /// <c>stage("core.economy", after=[...], before=[...], optional_after=[...], optional_before=[...])</c> (spec S5, spec-gaps G152).
        /// id는 이 모드의 접두사가 붙어야 한다. 참조 풀이와 정렬은 <see cref="SchemaBuilder"/>가 한다.
        /// </summary>
        private Value DeclareStage(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            const string usage = "stage(\"mod.name\", after=[\"other.stage\"], before=[...])";
            var r = new ArgReader(ctx, Stage, span, args);
            r.ExpectPositional(1, usage);
            var idArg = r.Positional[0];
            string id = r.String(idArg, "the stage id");
            var idSpan = ArgReader.SpanOf(idArg);
            string prefix = ctx.Mod + ".";
            if (!id.StartsWith(prefix, StringComparison.Ordinal) || id.Length == prefix.Length)
                throw ctx.Fail(Codes.SchBadDeclaration, "the stage id '" + id + "' must start with this mod's id: '" + prefix + "…'", idSpan,
                    hint: "a mod declares only its own stages, and puts them between others with after= and before= (spec-gaps G152)");

            List<NameRef> List(string name)
            {
                var result = new List<NameRef>();
                var arg = r.Take(name);
                if (arg == null) return result;
                foreach (var (s, sSpan) in r.StringList(arg, name)) result.Add(new NameRef(s, sSpan));
                return result;
            }
            var after = List("after");
            var before = List("before");
            var optionalAfter = List("optional_after");
            var optionalBefore = List("optional_before");
            r.RejectRest(new[] { "after", "before", "optional_after", "optional_before" });
            _target.Stages.Add(new StageDecl(id, idSpan, ctx.Mod, after, before, optionalAfter, optionalBefore));
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
            // 커널이 모으는 모양은 하나다 (G295): 다른 모양이면 조용히 안 모이는 대신 여기서 막는다
            if (ContainsContrib(type) && !IsContribList(type))
                throw ctx.Fail(Codes.SchBadDeclaration, "Contrib values are collected only from const(list[Contrib]), not " +
                                                        type.Display, typeArg.Span, hint: "spec-gaps G295");
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
            // 리스트·사전은 ui_state의 칸에서만 된다 (G277). 컴포넌트의 칸이면 component()가 막는다
            if (type is EntityRefTypeRef entity)
            {
                // 엔티티 Ref 칼럼을 선언하는 방법은 하나로 둔다 (inverse 등 옵션은 ref()만 받는다)
                throw ctx.Fail(Codes.SchBadDeclaration, "declare entity reference columns with ref(...)", typeArg.Span,
                    hint: "write ref(\"" + entity.Target.Name + "\")");
            }
            RejectFunctionType(ctx, type, typeArg);
            var spec = new MemberSpec(MemberKind.Field, type, span) { TypeSpan = typeArg.Span };
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
            RejectEntityKeys(ctx, type, typeArg.Span);
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
            // DefId 기본값도 선언한 모드 기준 전체 이름이다 (G261)
            if (d != null) spec.Default = Loading.DefIds.Normalize(d.Value, spec.Type, ctx.Mod);
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

        /// <summary>함수 값과 선언 보너스(<c>Contrib</c>)는 Def(const)에만 둔다 (spec S2, spec-gaps G295).</summary>
        private static void RejectFunctionType(CallContext ctx, TypeRef type, ArgValue arg)
        {
            if (ContainsFunctionType(type))
                throw ctx.Fail(Codes.SchBadDeclaration, "function values can only be const values of a Def", arg.Span);
            if (ContainsContrib(type))
                throw ctx.Fail(Codes.SchBadDeclaration, "Contrib values can only be const values", arg.Span,
                    hint: "declare them as const(list[Contrib]); to add a contribution while the game runs, use add_contrib (spec-gaps G295, G299)");
        }

        /// <summary>커널이 모으는 선언 보너스 칸의 타입 <c>list[Contrib]</c>인가 (G295).</summary>
        public static bool IsContribList(TypeRef type) => type is ListTypeRef l && l.Element is ContribTypeRef;

        private static bool ContainsContrib(TypeRef type)
        {
            switch (type)
            {
                case ContribTypeRef _: return true;
                case ListTypeRef l: return ContainsContrib(l.Element);
                case DictTypeRef d: return ContainsContrib(d.Key) || ContainsContrib(d.Value);
            }
            return false;
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
}
