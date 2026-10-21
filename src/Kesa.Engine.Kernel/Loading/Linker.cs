using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ⑤ 링크 (spec S1, spec-gaps G63~G66): ④의 최종 값에서 Def를 가리키는 id 문자열을 찾아 검사한다. 값은 바꾸지 않는다.
    /// 대상은 <c>DefId["X"]</c> 타입 자리(리스트·맵 안, 맵 키 포함)와 키 컴포넌트의 줄 키다.
    /// 검사는 존재, X 타입, 보이는 범위(가드가 연 <c>optional</c> 모드 포함), <c>abstract</c> 금지다.
    /// 오류는 값을 처음 적은 모드의 것이다(물려받은 값은 부모 쪽, 패치가 쓴 값은 패치 모드).
    /// 값의 모양이 타입과 맞지 않는 것은 ⑥이 보므로 여기서는 건너뛴다.
    /// </summary>
    internal sealed class Linker
    {
        private readonly DefRegistry _defs;
        private readonly SchemaSet _schema;
        private readonly ModVisibility _visibility;
        private readonly Func<string, ModManifest?> _manifest;
        private readonly StageDiagnostics _stage;

        // 같은 값을 물려받은 자식마다 같은 오류가 반복되지 않게 한다
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>지금 검사하는 값이 Def에 적은 컴포넌트 값이면 그 Def 타입 (선언 보너스를 드는 엔티티의 타입, G300). 아니면 null.</summary>
        private DefType? _holder;

        private Linker(DefRegistry defs, SchemaSet schema, ModVisibility visibility, Func<string, ModManifest?> manifest,
            StageDiagnostics stage)
        {
            _defs = defs;
            _schema = schema;
            _visibility = visibility;
            _manifest = manifest;
            _stage = stage;
        }

        public static void Check(ResolvedDefs resolved, DefRegistry defs, SchemaSet schema, ModVisibility visibility,
            Func<string, ModManifest?> manifest, StageDiagnostics stage, IReadOnlyList<CommandDecl>? commands = null,
            IEnumerable<KeyValuePair<string, ModFunctions>>? functions = null)
        {
            var linker = new Linker(defs, schema, visibility, manifest, stage);
            // spawn("Def id")에 리터럴로 적은 id: 엔티티가 되는 Def여야 한다. 책임은 함수를 쓴 모드다 (G134)
            if (functions != null)
            {
                foreach (var pair in functions)
                foreach (var spawned in pair.Value.SpawnedDefs)
                    linker.CheckId(new ValueBlame(pair.Key, spawned.Value, Array.Empty<string>()), DefIds.Resolve(spawned.Key, pair.Key), null,
                        spawning: true);
                // defid("Def id"): 어떤 타입이든 보이는 구체 Def여야 한다 (G266)
                foreach (var pair in functions)
                foreach (var literal in pair.Value.DefIdLiterals)
                    linker.CheckId(new ValueBlame(pair.Key, literal.Value, Array.Empty<string>()), DefIds.Resolve(literal.Key, pair.Key), null);
                foreach (var pair in functions)
                foreach (var names in pair.Value.QueriedNames)
                    linker.CheckQuery(pair.Key, names);
                // defs("타입"): 보이는 Def 타입이어야 한다 (G305)
                foreach (var pair in functions)
                foreach (var literal in pair.Value.DefTypeLiterals)
                    linker.CheckDefType(pair.Key, literal.Key, literal.Value);
                // submit("커맨드", ...)에 리터럴로 적은 커맨드: 있고 보이고 넘긴 이름이 매개변수와 맞아야 한다 (G278)
                var byId = new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);
                foreach (var command in commands ?? Array.Empty<CommandDecl>()) byId[command.Id] = command;
                foreach (var pair in functions)
                foreach (var use in pair.Value.SubmitUses)
                    linker.CheckSubmit(pair.Key, use, byId);
            }
            foreach (var def in resolved.Defs) linker.CheckDef(def);
            // 스키마가 선언한 기본값도 Def 값과 같은 규칙으로 본다. 책임은 선언한 모드다 (G73)
            foreach (var decl in MemberDefaults.DeclaredDefaults(schema))
                linker.CheckValue(MemberDefaults.DeclaredOrigin(decl), decl.Spec.Default!, decl.Spec.Type, decl.Span);
            // 커맨드 매개변수의 기본값도 같다. 책임은 커맨드를 쓴 모드다 (G116)
            if (commands == null) return;
            foreach (var command in commands)
            foreach (var p in command.Parameters)
            {
                if (p.Default == null) continue;
                string mod = command.Mod;
                linker.CheckValue((literal, fallback) =>
                        new ValueBlame(mod, literal.File.Length > 0 ? literal : fallback, Array.Empty<string>()),
                    p.Default, p.Type, p.Span);
            }
            // DefId 매개변수의 choose(p, from_=[...])에 리터럴로 적은 후보도 같다 (G261)
            foreach (var command in commands) linker.CheckChoiceLiterals(command);
        }

        private void CheckSubmit(string mod, SubmitUse use, SortedDictionary<string, CommandDecl> commands)
        {
            string id = use.Command.IndexOf('.') >= 0 ? use.Command : mod + "." + use.Command;
            if (!commands.TryGetValue(id, out var command) || command.IsUiAction)
            {
                var names = new List<string>();
                foreach (var c in commands.Values)
                    if (!c.IsUiAction) names.Add(c.Mod == mod ? c.Function.Name : c.Id);
                _stage.Error(mod, Codes.CmdSubmit,
                    command != null ? "'" + use.Command + "' is a @ui_action, so it cannot be submitted"
                        : "submit() names '" + use.Command + "', but there is no command '" + id + "'",
                    use.Span, command != null ? "a @ui_action runs on the screen; submit a @command (spec-gaps G278)"
                        : Suggest.Hint(use.Command, names));
                return;
            }
            if (!_visibility.CanSee(mod, command.Mod))
            {
                _stage.Error(mod, Codes.LnkNotVisible,
                    "command '" + id + "' belongs to mod '" + command.Mod + "', which mod '" + mod + "' does not depend on", use.Span,
                    "add \"" + command.Mod + "\" to dependencies in mod.py");
                return;
            }
            var parameters = new List<string>();
            foreach (var p in command.Parameters) parameters.Add(p.Name);
            for (int i = 0; i < use.ArgumentNames.Count; i++)
            {
                string name = use.ArgumentNames[i];
                if (parameters.Contains(name)) continue;
                _stage.Error(mod, Codes.CmdSubmit, "command '" + id + "' has no parameter '" + name + "'", use.ArgumentSpans[i],
                    Suggest.Hint(name, parameters) ?? (parameters.Count == 0 ? "it takes no arguments"
                        : "its parameters are " + string.Join(", ", parameters)));
            }
            var passed = new HashSet<string>(use.ArgumentNames, StringComparer.Ordinal);
            foreach (var p in command.Parameters)
            {
                if (p.Default != null || passed.Contains(p.Name)) continue;
                _stage.Error(mod, Codes.CmdSubmit, "submit() of '" + id + "' does not pass '" + p.Name + "', which has no default",
                    use.Span, "write " + p.Name + "=... (spec-gaps G278)");
            }
        }

        private void CheckChoiceLiterals(CommandDecl command)
        {
            var body = command.Function.Definition?.Body;
            if (body == null || body.Count == 0 || !(body[0] is WithStmt with)) return;
            foreach (var stmt in with.Body)
            {
                if (!(stmt is ExprStmt es) || !FunctionChecker.IsPreconditionCall(es.Expr, out var name) ||
                    name != FunctionChecker.ChooseName)
                    continue;
                var call = (CallExpr)es.Expr;
                if (call.Arguments.Count != 2 || !(call.Arguments[0].Value is NameExpr target) ||
                    call.Arguments[1].Name != FunctionChecker.FromName || !(call.Arguments[1].Value is ListExpr list))
                    continue;
                CommandParameter? parameter = null;
                foreach (var p in command.Parameters)
                    if (p.Name == target.Name) parameter = p;
                var type = parameter?.Type is OptionalTypeRef optional ? optional.Inner : parameter?.Type;
                if (!(type is DefIdTypeRef defId)) continue;
                foreach (var item in list.Items)
                    if (item is StringLiteral s)
                        CheckId(new ValueBlame(command.Mod, s.Span, Array.Empty<string>()), DefIds.Resolve(s.Value, command.Mod),
                            defId.Target.Name);
            }
        }

        private void CheckDef(ResolvedDef def)
        {
            foreach (var decl in def.Type.Consts)
            {
                if (def.Consts.TryGet(decl.Name, out var value)) CheckValue(value, value.Value, decl.Spec.Type, value.Span);
            }
            foreach (var name in def.Components.Keys)
            {
                def.Components.TryGet(name, out var rc);
                var component = rc.Component;
                _holder = def.Type; // 이 컴포넌트 값의 선언 보너스는 이 타입의 엔티티가 든다 (G300의 to= 검사)
                CheckMembers(rc.Members, component);
                _holder = null;
                foreach (var key in rc.Rows.Keys)
                {
                    rc.Rows.TryGet(key, out var row);
                    CheckRowKey(def, component, key, row);
                    CheckMembers(row.Members, component);
                }
            }
        }

        private void CheckMembers(OrderedMap<ResolvedValue> members, ComponentType component)
        {
            foreach (var name in members.Keys)
            {
                members.TryGet(name, out var value);
                if (component.TryGetMember(name, out var member))
                    CheckValue(value, value.Value, member.Spec.Type, value.Span);
            }
        }

        /// <summary>키 컴포넌트의 줄 키는 그 컴포넌트의 키 타입 Def의 id다. 줄 안의 값에서 가장 가까운 출처로 책임 모드를 정한다.</summary>
        private void CheckRowKey(ResolvedDef def, ComponentType component, string key, ResolvedRow row)
        {
            if (component.Key == null) return;
            ResolvedValue? carrier = null;
            foreach (var n in row.Members.Keys)
            {
                row.Members.TryGet(n, out var v);
                carrier = v;
                break;
            }
            // 줄에 값이 없으면 그 Def 자신이 적었다고 본다
            var origin = carrier ?? new ResolvedValue(NoneValue.Instance, ValueOrigin.Own, def.Record, def.Record.Span);
            CheckId(origin, key, component.Key.Name, SourceSpan.None, origin.Span);
        }

        private void CheckValue(ResolvedValue origin, Value value, TypeRef type, SourceSpan fallback) =>
            CheckValue((literal, at) => ValueBlame.Of(origin, literal, at), value, type, fallback);

        /// <param name="blame">리터럴 위치와 대신 쓸 위치로 책임 모드와 위치를 정한다</param>
        private void CheckValue(Func<SourceSpan, SourceSpan, ValueBlame> blame, Value value, TypeRef type, SourceSpan fallback)
        {
            switch (type)
            {
                case DefIdTypeRef d:
                    if (value is StrValue s) CheckId(blame(s.Origin, fallback), s.Value, d.Target.Name);
                    return;
                case ContribTypeRef _:
                    if (value is ContribValue c) CheckContrib(blame(c.Span, fallback), c);
                    return;
                case OptionalTypeRef optional:
                    CheckValue(blame, value, optional.Inner, fallback);
                    return;
                case ListTypeRef list:
                    if (value is ListValue items)
                        foreach (var item in items.Items) CheckValue(blame, item, list.Element, fallback);
                    return;
                case DictTypeRef dict:
                    if (value is DictValue map)
                    {
                        foreach (var item in map.Items)
                        {
                            CheckValue(blame, item.Key, dict.Key, item.Span);
                            CheckValue(blame, item.Value, dict.Value, item.Span);
                        }
                    }
                    return;
            }
        }

        /// <summary>
        /// 선언 보너스의 대상과 키 (G295·G297): 보이는 컴포넌트의 <c>derived</c>여야 하고(G98), 키 컴포넌트면 Def 키 컴포넌트이고 그 키 타입의
        /// Def를 <c>key=</c>로 가리켜야 한다. 키 없는 컴포넌트는 <c>key=</c>를 받지 않는다.
        /// </summary>
        private void CheckContrib(ValueBlame blame, ContribValue c)
        {
            string mod = blame.Mod;
            var span = blame.Span;

            void Fail(string code, string message, string? hint = null)
            {
                if (!_reported.Add(mod + "\n" + span + "\n" + code + "\n" + c.Target)) return;
                _stage.Error(mod, code, message, span, hint);
            }

            if (c.To != null) CheckContribTo(blame, c);
            if (!_schema.TryGetComponent(c.Component, out var component))
            {
                var names = new List<string>();
                foreach (var x in _schema.Components) names.Add(x.Name);
                Fail(Codes.LnkNotFound, "contrib() targets '" + c.Target + "', but there is no component '" + c.Component + "'",
                    Suggest.Hint(c.Component, names));
                return;
            }
            if (!_visibility.CanSee(mod, component.Mod))
            {
                Fail(Codes.LnkNotVisible,
                    "component '" + c.Component + "' belongs to mod '" + component.Mod + "', which mod '" + mod + "' does not depend on",
                    "add \"" + component.Mod + "\" to dependencies in mod.py");
                return;
            }
            if (!component.TryGetMember(c.Member, out var member) || member.Kind != MemberKind.Derived)
            {
                if (member != null)
                {
                    Fail(Codes.LnkWrongType,
                        "contrib() targets '" + c.Target + "', which is not a derived value; contributions only target derived values",
                        "spec-gaps G98");
                    return;
                }
                var derived = new List<string>();
                foreach (var m in component.Members)
                    if (m.Kind == MemberKind.Derived) derived.Add(m.Name);
                Fail(Codes.LnkNotFound, "component '" + c.Component + "' has no derived value '" + c.Member + "'",
                    Suggest.Hint(c.Member, derived));
                return;
            }
            if (!component.IsKeyed)
            {
                if (c.Key != null)
                    Fail(Codes.LnkWrongType, "'" + c.Component + "' is not a keyed component, so contrib() takes no key=");
                return;
            }
            if (component.KeyKind != KeyKind.Def)
            {
                Fail(Codes.LnkWrongType,
                    "'" + c.Component + "' is keyed by " + component.KeyDisplay + "; a contrib() written in a Def can only target rows " +
                    "keyed by a Def", "add one while the game runs with add_contrib (spec-gaps G297, G299)");
                return;
            }
            if (c.Key == null)
            {
                Fail(Codes.LnkWrongType, "'" + c.Component + "' is a keyed component; say which row with key=\"...\"",
                    "write contrib(\"" + c.Target + "\", \"" + c.Op + "\", ..., key=\"" + component.Key!.Name + " id\") (spec-gaps G297)");
                return;
            }
            CheckId(blame, c.Key, component.Key!.Name);
        }

        /// <summary>
        /// <c>to=</c> (G300): <c>"C.r"</c>면 키 없는 컴포넌트 C의 Ref 칼럼 r(역방향 색인이 있는 것), 아니면 어느 Def 타입의 역방향 컬렉션 이름.
        /// 선언을 든 타입을 알면(Def에 적은 컴포넌트 값) 역방향 컬렉션은 그 타입의 것이어야 한다. 컴포넌트는 런타임에 붙일 수 있어
        /// 닿는 엔티티가 대상 컴포넌트를 가질지는 보지 않는다.
        /// </summary>
        private void CheckContribTo(ValueBlame blame, ContribValue c)
        {
            string mod = blame.Mod;
            var span = c.ToSpan.File.Length > 0 ? c.ToSpan : blame.Span;
            string to = c.To!;

            void Fail(string code, string message, string? hint = null)
            {
                if (!_reported.Add(mod + "\n" + span + "\n" + code + "\nto=" + to)) return;
                _stage.Error(mod, code, message, span, hint);
            }

            int dot = to.IndexOf('.');
            if (dot >= 0)
            {
                string componentName = to.Substring(0, dot), refName = to.Substring(dot + 1);
                if (!_schema.TryGetComponent(componentName, out var component))
                {
                    var names = new List<string>();
                    foreach (var x in _schema.Components) names.Add(x.Name);
                    Fail(Codes.LnkNotFound, "contrib(to=\"" + to + "\"): there is no component '" + componentName + "'",
                        Suggest.Hint(componentName, names));
                    return;
                }
                if (!_visibility.CanSee(mod, component.Mod))
                {
                    Fail(Codes.LnkNotVisible,
                        "component '" + componentName + "' belongs to mod '" + component.Mod + "', which mod '" + mod + "' does not depend on",
                        "add \"" + component.Mod + "\" to dependencies in mod.py");
                    return;
                }
                if (!component.TryGetMember(refName, out var reference) || reference.Kind != MemberKind.Ref)
                {
                    var refs = new List<string>();
                    foreach (var m in component.Members)
                        if (m.Kind == MemberKind.Ref) refs.Add(m.Name);
                    Fail(reference == null ? Codes.LnkNotFound : Codes.LnkWrongType,
                        "contrib(to=\"" + to + "\"): '" + componentName + "' has no ref column '" + refName + "'",
                        refs.Count == 0 ? "to= follows a ref(...) column (spec-gaps G300)" : Suggest.Hint(refName, refs));
                    return;
                }
                if (component.IsKeyed)
                    Fail(Codes.LnkWrongType, "contrib(to=\"" + to + "\"): '" + componentName + "' is a keyed component",
                        "to= follows a ref column of a component without key= (spec-gaps G300)");
                return;
            }

            var owners = new List<DefType>();
            var all = new List<string>();
            foreach (var type in _schema.Types)
            {
                foreach (var inverse in type.Inverses)
                {
                    all.Add(inverse.Name);
                    if (inverse.Name == to && _visibility.CanSee(mod, inverse.Mod)) owners.Add(type);
                }
            }
            if (owners.Count == 0)
            {
                Fail(Codes.LnkNotFound, "contrib(to=\"" + to + "\"): there is no inverse collection '" + to + "'",
                    Suggest.Hint(to, all) ?? "to= is \"Component.ref\" or the name of an inverse collection (spec-gaps G300)");
                return;
            }
            if (_holder != null && !owners.Contains(_holder))
            {
                var mine = new List<string>();
                foreach (var inverse in _holder.Inverses) mine.Add(inverse.Name);
                Fail(Codes.LnkNotFound,
                    "contrib(to=\"" + to + "\") is declared on a " + _holder.Name + ", which has no inverse collection '" + to + "'",
                    Suggest.Hint(to, mine) ?? "the step starts from the entity that declares the contribution (spec-gaps G300)");
            }
        }

        private void CheckId(ResolvedValue origin, string id, string typeName, SourceSpan literal, SourceSpan fallback) =>
            CheckId(ValueBlame.Of(origin, literal, fallback), id, typeName);

        /// <param name="typeName">기대하는 Def 타입. null이면 엔티티가 되는 Def 아무것이다 (<c>spawn</c>의 Def id, G134)</param>
        /// <param name="typeName">기대하는 Def 타입. null이면 어떤 타입이든 되고, <paramref name="spawning"/>이면 엔티티가 되는 타입이어야 한다</param>
        private void CheckId(ValueBlame blame, string id, string? typeName, bool spawning = false)
        {
            string mod = blame.Mod;
            var guards = blame.Guards;
            var span = blame.Span;

            void Fail(string code, string message, string? hint = null, IReadOnlyList<SourceSpan>? related = null)
            {
                if (!_reported.Add(mod + "\n" + span + "\n" + code + "\n" + id)) return;
                _stage.Error(mod, code, message, span, hint, related);
            }

            if (!_defs.TryGet(id, out var record))
            {
                var ids = new List<string>();
                foreach (var d in _defs.Defs)
                    if (typeName == null ? (!spawning || !d.Type.IsDataOnly) && !d.IsAbstract : d.Type.Name == typeName) ids.Add(d.Id);
                Fail(Codes.LnkNotFound, "there is no " + (typeName ?? "Def") + " '" + id + "'", DefIds.Hint(id, ids));
                return;
            }

            if (typeName == null)
            {
                if (spawning && record.Type.IsDataOnly)
                {
                    Fail(Codes.LnkWrongType,
                        "'" + id + "' is a " + record.Type.Name + ", a data-only Def type with no components, so it cannot be spawned",
                        related: new[] { record.Span });
                    return;
                }
                typeName = record.Type.Name;
            }

            if (record.Type.Name != typeName)
            {
                Fail(Codes.LnkWrongType,
                    "'" + id + "' is a " + record.Type.Name + ", but a " + typeName + " is expected here",
                    related: new[] { record.Span });
                return;
            }

            string? hiddenOwner = null;
            string? optionalOwner = null;
            bool visible = false;
            for (var r = record; r != null; r = r.Replaced)
            {
                if (_visibility.CanSee(mod, r.Mod)) visible = true;
                else if (IsOptional(mod, r.Mod) && Contains(guards, r.Mod)) visible = true;
                else if (hiddenOwner == null)
                {
                    hiddenOwner = r.Mod;
                    if (IsOptional(mod, r.Mod)) optionalOwner = r.Mod;
                }
            }
            if (!visible)
            {
                Fail(Codes.LnkNotVisible,
                    typeName + " '" + id + "' belongs to mod '" + hiddenOwner + "', which mod '" + mod + "' does not depend on",
                    optionalOwner != null
                        ? "a patch can use it inside: if mod_loaded(\"" + optionalOwner + "\"):"
                        : "add \"" + hiddenOwner + "\" to dependencies in mod.py",
                    new[] { record.Span });
                return;
            }

            if (record.IsAbstract)
            {
                Fail(Codes.LnkAbstract, "'" + id + "' is abstract: a template cannot be referenced",
                    "use a concrete " + typeName + " (a Def without abstract=True)", new[] { record.Span });
            }
        }

        /// <summary>
        /// <c>query(...)</c>에 리터럴로 적은 출처 이름 (G192): 엔티티가 되는 Def 타입이나 컴포넌트여야 하고, 그 모드에 보여야 하며,
        /// 한 호출에 Def 타입은 하나까지다. 책임은 함수를 쓴 모드다.
        /// </summary>
        private void CheckQuery(string mod, List<KeyValuePair<string, SourceSpan>> names)
        {
            string? typeName = null;
            foreach (var pair in names)
            {
                string name = pair.Key;
                var span = pair.Value;
                string owner;
                if (_schema.TryGetComponent(name, out var component)) owner = component.Mod;
                else if (_schema.TryGetType(name, out var type))
                {
                    if (type.IsDataOnly)
                    {
                        _stage.Error(mod, Codes.LnkWrongType,
                            "'" + name + "' is a data-only Def type with no components, so it has no entities to query", span);
                        continue;
                    }
                    if (typeName != null && typeName != name)
                    {
                        _stage.Error(mod, Codes.LnkWrongType,
                            "query() was given two Def types ('" + typeName + "' and '" + name + "'); an entity has only one", span,
                            "give one Def type and any number of components");
                        continue;
                    }
                    typeName = name;
                    owner = type.Mod;
                }
                else
                {
                    var candidates = new List<string>();
                    foreach (var t in _schema.Types)
                        if (!t.IsDataOnly) candidates.Add(t.Name);
                    foreach (var c in _schema.Components) candidates.Add(c.Name);
                    string what = _schema.TryGetSignal(name, out _) ? "'" + name + "' is a signal, not a Def type or component"
                        : "there is no Def type or component '" + name + "'";
                    _stage.Error(mod, Codes.LnkNotFound, what, span, Suggest.Hint(name, candidates));
                    continue;
                }
                if (!_visibility.CanSee(mod, owner))
                    _stage.Error(mod, Codes.LnkNotVisible,
                        "'" + name + "' belongs to mod '" + owner + "', which mod '" + mod + "' does not depend on", span,
                        "add \"" + owner + "\" to dependencies in mod.py");
            }
        }

        /// <summary><c>defs(City)</c>에 리터럴로 적은 타입 이름: 이 모드에 보이는 Def 타입이어야 한다 (G305). 데이터 전용 타입도 된다.</summary>
        private void CheckDefType(string mod, string name, SourceSpan span)
        {
            if (!_schema.TryGetType(name, out var type))
            {
                var candidates = new List<string>();
                foreach (var t in _schema.Types) candidates.Add(t.Name);
                string what = _schema.TryGetComponent(name, out _) ? "'" + name + "' is a component, not a Def type"
                    : "there is no Def type '" + name + "'";
                _stage.Error(mod, Codes.LnkNotFound, what, span, Suggest.Hint(name, candidates));
                return;
            }
            if (!_visibility.CanSee(mod, type.Mod))
                _stage.Error(mod, Codes.LnkNotVisible,
                    "'" + name + "' belongs to mod '" + type.Mod + "', which mod '" + mod + "' does not depend on", span,
                    "add \"" + type.Mod + "\" to dependencies in mod.py");
        }

        private static bool Contains(IReadOnlyList<string> list, string item)
        {
            foreach (var x in list)
                if (x == item) return true;
            return false;
        }

        private bool IsOptional(string mod, string owner)
        {
            var manifest = _manifest(mod);
            if (manifest == null) return false;
            foreach (var o in manifest.Optional)
                if (o.Id == owner) return true;
            return false;
        }
    }
}
