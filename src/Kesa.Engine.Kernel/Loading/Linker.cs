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
                    linker.CheckId(new ValueBlame(pair.Key, spawned.Value, Array.Empty<string>()), spawned.Key, null);
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
                CheckMembers(rc.Members, component);
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

        private void CheckId(ResolvedValue origin, string id, string typeName, SourceSpan literal, SourceSpan fallback) =>
            CheckId(ValueBlame.Of(origin, literal, fallback), id, typeName);

        /// <param name="typeName">기대하는 Def 타입. null이면 엔티티가 되는 Def 아무것이다 (<c>spawn</c>의 Def id, G134)</param>
        private void CheckId(ValueBlame blame, string id, string? typeName)
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
                    if (typeName == null ? !d.Type.IsDataOnly && !d.IsAbstract : d.Type.Name == typeName) ids.Add(d.Id);
                Fail(Codes.LnkNotFound, "there is no " + (typeName ?? "Def") + " '" + id + "'", Suggest.Hint(id, ids));
                return;
            }

            if (typeName == null)
            {
                if (record.Type.IsDataOnly)
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
