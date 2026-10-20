using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ③: ②b가 기록한 패치 호출을 검사한다 (spec-gaps G35). 지금은 대상 Def가 있는지와 보이는 범위(G38),
    /// 경로가 그 Def 타입의 스키마에서 유효한지(G34·G47)까지다. 오류는 호출한 줄을 가리키고, 모두 그 모드 자신의 오류다.
    /// 값에 의존하는 검사(그 칸에 값이 있는지, 컴포넌트가 이미 있는지)는 ④에서 한다 (G42).
    /// </summary>
    internal static class PatchResolver
    {
        /// <param name="mods">살아남은 모드와 그 호출 (모드 로드 순서)</param>
        /// <returns>검사를 통과한 호출과 그 대상 (같은 순서)</returns>
        public static List<ResolvedPatch> Resolve(IReadOnlyList<KeyValuePair<ModManifest, IReadOnlyList<PatchCall>>> mods,
            DefRegistry defs, SchemaSet schema, ModVisibility visibility, StageDiagnostics stage)
        {
            var resolved = new List<ResolvedPatch>();
            foreach (var entry in mods)
            {
                foreach (var call in entry.Value)
                {
                    var record = CheckTarget(entry.Key, call, defs, visibility, stage);
                    if (record == null) continue;
                    var resolver = new PathResolver(entry.Key, call, record, defs, schema, visibility, stage);
                    var target = resolver.Resolve();
                    if (target == null) continue;
                    call.Path = resolver.NormalizedPath();
                    if (call.Value != null) call.Value = NormalizeValue(call, target, entry.Key.Id);
                    var patch = new ResolvedPatch(call, record, target);
                    if (PatchOperationCheck.Check(entry.Key, patch, stage)) resolved.Add(patch);
                }
            }
            return resolved;
        }

        /// <summary>
        /// 패치 값의 Def id를 패치한 모드 기준 전체 이름으로 (G261). <c>remove</c>의 셋째 인자는 리스트면 원소, 맵이면 키다.
        /// 컴포넌트 값(<c>Morale(...)</c>)은 만들 때 바꿨다.
        /// </summary>
        private static Value NormalizeValue(PatchCall call, PatchTarget target, string mod)
        {
            var value = call.Value!;
            var type = target.ValueType;
            if (type == null || value is ComponentValue) return value;
            if (call.Op == PatchOp.Remove)
            {
                if (type is ListTypeRef list) return DefIds.Normalize(value, list.Element, mod);
                if (type is DictTypeRef dict) return DefIds.Normalize(value, dict.Key, mod);
                return value;
            }
            return DefIds.Normalize(value, type, mod);
        }

        /// <summary>
        /// 대상은 보이는 범위(자기 + 필수 의존의 닫힘)의 Def이거나, <c>optional</c>로 선언한 모드의 Def를 그 모드의
        /// <c>mod_loaded</c> 가드 안에서 쓰는 경우다 (G38). 패치는 id로 찾은 현재 Def(교체본)에 적용되고 (G43),
        /// 교체된 Def는 교체한 모드나 원래 정의한 모드 중 하나라도 보이면 된다.
        /// </summary>
        private static DefRecord? CheckTarget(ModManifest mod, PatchCall call, DefRegistry defs, ModVisibility visibility,
            StageDiagnostics stage)
        {
            if (!defs.TryGet(call.TargetId, out var record))
            {
                stage.Error(mod.Id, Codes.PatTargetNotFound, "there is no Def '" + call.TargetId + "' to patch",
                    call.TargetSpan, hint: DefIds.Hint(call.TargetId, defs.Ids));
                return null;
            }

            string? optionalOwner = null;
            for (var r = record; r != null; r = r.Replaced)
            {
                if (visibility.CanSee(mod.Id, r.Mod)) return record;
                if (optionalOwner == null && IsOptional(mod, r.Mod)) optionalOwner = r.Mod;
            }

            if (optionalOwner != null)
            {
                if (IsGuarded(call, optionalOwner)) return record;
                stage.Error(mod.Id, Codes.PatNeedsGuard,
                    "Def '" + call.TargetId + "' belongs to mod '" + optionalOwner + "', which is optional for mod '" + mod.Id +
                    "'; patches of its Defs must be inside a guard",
                    call.TargetSpan, hint: "wrap the patch in: if mod_loaded(\"" + optionalOwner + "\"):",
                    related: new[] { record.Span });
                return null;
            }

            stage.Error(mod.Id, Codes.PatTargetNotVisible,
                "Def '" + call.TargetId + "' belongs to mod '" + record.Mod + "', which mod '" + mod.Id + "' does not depend on",
                call.TargetSpan, hint: "add \"" + record.Mod + "\" to dependencies in mod.py", related: new[] { record.Span });
            return null;
        }

        private static bool IsGuarded(PatchCall call, string owner)
        {
            foreach (var g in call.Guards)
                if (g == owner) return true;
            return false;
        }

        private static bool IsOptional(ModManifest mod, string owner)
        {
            foreach (var o in mod.Optional)
                if (o.Id == owner) return true;
            return false;
        }

        /// <summary>
        /// 경로 하나를 Def 타입의 스키마에서 따라간다 (spec-gaps G34·G47). 첫 조각은 그 타입의 <c>const</c>나 컴포넌트이고,
        /// 이름은 쓰는 모드에 보이는 것만 허용한다 (G25와 같은 범위). 값 안으로는 맵의 키로만 들어간다.
        /// </summary>
        private sealed class PathResolver
        {
            private readonly ModManifest _mod;
            private readonly PatchCall _call;
            private readonly DefType _type;
            private readonly DefRegistry _defs;
            private readonly SchemaSet _schema;
            private readonly ModVisibility _visibility;
            private readonly StageDiagnostics _stage;
            private readonly IReadOnlyList<PathSegment> _segments;

            public PathResolver(ModManifest mod, PatchCall call, DefRecord record, DefRegistry defs, SchemaSet schema,
                ModVisibility visibility, StageDiagnostics stage)
            {
                _mod = mod;
                _call = call;
                _type = record.Type;
                _defs = defs;
                _schema = schema;
                _visibility = visibility;
                _stage = stage;
                _segments = call.Path.Segments;
                _normalized = new List<PathSegment>(_segments);
            }

            // Def id 키 조각을 쓰는 모드 기준 전체 이름으로 바꾼 경로 (G261)
            private readonly List<PathSegment> _normalized;

            public PatchPath NormalizedPath() => _call.Path.WithSegments(_normalized);

            private string DefKey(int index)
            {
                string key = DefIds.Resolve(_segments[index].Text!, _mod.Id);
                _normalized[index] = PathSegment.StringKey(key);
                return key;
            }

            private PatchTarget? Fail(string code, string message, string? hint = null,
                IReadOnlyList<SourceSpan>? related = null)
            {
                _stage.Error(_mod.Id, code, message, _call.PathSpan, hint, related);
                return null;
            }

            public PatchTarget? Resolve()
            {
                string name = _segments[0].Text!;

                foreach (var c in _type.Consts)
                {
                    if (c.Name != name) continue;
                    if (!CanUse(c.Mod)) return NotVisible(name, c.Mod, c.Span);
                    var target = new PatchTarget(PatchTargetKind.Const, _type) { Member = c };
                    return Navigate(target, 1, c.Spec.Type);
                }

                if (_schema.TryGetComponent(name, out var component))
                {
                    if (!CanUse(component.Mod)) return NotVisible(name, component.Mod, component.Decl.Span);
                    return component.IsKeyed ? ResolveKeyed(component) : ResolveComponent(component);
                }

                foreach (var inverse in _type.Inverses)
                {
                    if (inverse.Name != name) continue;
                    return Fail(Codes.PatBadField, "'" + name + "' is a relation derived from other Defs and cannot be patched");
                }

                if (name == "parent" || name == "abstract" || name == "override")
                {
                    return Fail(Codes.PatBadField, "'" + name + "' describes the Def itself and cannot be patched",
                        hint: "write the Def with the parent you want");
                }

                return Fail(Codes.PatBadField, "Def type '" + _type.Name + "' has no value or component '" + name + "'",
                    hint: Suggest.Hint(name, Candidates()));
            }

            /// <summary>
            /// 이름을 쓸 수 있는 모드인가: 보이는 범위이거나, <c>optional</c>로 선언한 모드를 그 가드 안에서 쓰는 경우.
            /// 가드가 열어 주는 것은 패치 경로 문자열의 이름뿐이다 (spec-gaps G38).
            /// </summary>
            private bool CanUse(string owner) =>
                _visibility.CanSee(_mod.Id, owner) || (IsOptional(_mod, owner) && IsGuarded(_call, owner));

            private PatchTarget? NotVisible(string name, string owner, SourceSpan at) =>
                Fail(IsOptional(_mod, owner) ? Codes.PatNeedsGuard : Codes.SchNotVisible,
                    "'" + name + "' belongs to mod '" + owner + "', which " +
                    (IsOptional(_mod, owner) ? "is optional for" : "does not belong to the dependencies of") + " mod '" + _mod.Id + "'",
                    hint: IsOptional(_mod, owner)
                        ? "wrap the patch in: if mod_loaded(\"" + owner + "\"):"
                        : "add \"" + owner + "\" to dependencies in mod.py",
                    related: new[] { at });

            private IEnumerable<string> Candidates()
            {
                foreach (var c in _type.Consts)
                    if (CanUse(c.Mod)) yield return c.Name;
                foreach (var c in _schema.Components)
                    if (CanUse(c.Mod)) yield return c.Name;
            }

            private bool IsSlot(ComponentType component)
            {
                foreach (var slot in _type.Components)
                    if (slot.Component == component) return true;
                return false;
            }

            private PatchTarget? ResolveComponent(ComponentType component)
            {
                bool slot = IsSlot(component);
                if (_segments.Count == 1)
                    return new PatchTarget(PatchTargetKind.Component, _type) { Component = component, IsTypeSlot = slot };

                var memberSegment = _segments[1];
                if (memberSegment.IsNumber || memberSegment.Bracketed)
                {
                    return Fail(Codes.PatBadPathStep,
                        "component '" + component.Name + "' is not keyed, so it has no rows; write " + component.Name + ".value");
                }
                var target = Member(component, memberSegment, PatchTargetKind.Member);
                if (target == null) return null;
                target.IsTypeSlot = slot;
                return Navigate(target, 2, target.Member!.Spec.Type);
            }

            private PatchTarget? ResolveKeyed(ComponentType component)
            {
                bool slot = IsSlot(component);
                if (_segments.Count == 1)
                {
                    return Fail(Codes.PatBadPathStep,
                        "component '" + component.Name + "' is keyed; patch one row, for example " + component.Name + ".Key",
                        hint: "write " + component.Name + "['id'] for an id with dots");
                }
                var keySegment = _segments[1];
                if (component.KeyKind != KeyKind.Def)
                {
                    return Fail(Codes.PatBadPathStep,
                        "rows of '" + component.Name + "' are keyed by " + (component.KeyKind == KeyKind.Int ? "int" : "entities") +
                        " and exist only at runtime; they cannot be patched");
                }
                if (keySegment.IsNumber)
                    return Fail(Codes.PatBadPathStep, "the key of '" + component.Name + "' is a Def id, not an integer");

                string key = DefKey(1);
                if (!_defs.TryGet(key, out var keyDef) || keyDef.Type != component.Key)
                {
                    var ids = new List<string>();
                    foreach (var d in _defs.Defs)
                        if (d.Type == component.Key) ids.Add(d.Id);
                    return Fail(Codes.PatBadField,
                        "there is no " + component.Key!.Name + " '" + key + "' to use as the key of '" + component.Name + "'",
                        hint: DefIds.Hint(key, ids));
                }

                if (_segments.Count == 2)
                {
                    return new PatchTarget(PatchTargetKind.Row, _type)
                    {
                        Component = component,
                        IsTypeSlot = slot,
                        RowKey = key,
                        RowDef = keyDef,
                    };
                }

                var memberSegment = _segments[2];
                if (memberSegment.IsNumber || memberSegment.Bracketed)
                {
                    return Fail(Codes.PatBadPathStep,
                        "expected a value name after " + component.Name + "." + key + ", for example " + component.Name + "." + key + ".value");
                }
                var target = Member(component, memberSegment, PatchTargetKind.RowMember);
                if (target == null) return null;
                target.IsTypeSlot = slot;
                target.RowKey = key;
                target.RowDef = keyDef;
                return Navigate(target, 3, target.Member!.Spec.Type);
            }

            private PatchTarget? Member(ComponentType component, PathSegment segment, PatchTargetKind kind)
            {
                string name = segment.Text!;
                if (!component.TryGetMember(name, out var member))
                {
                    var names = new List<string>();
                    foreach (var m in component.Members)
                        if (m.Kind != MemberKind.Derived) names.Add(m.Name);
                    return Fail(Codes.PatBadField, "component '" + component.Name + "' has no value '" + name + "'",
                        hint: Suggest.Hint(name, names));
                }
                if (member.Kind == MemberKind.Derived)
                {
                    return Fail(Codes.PatBadField,
                        "'" + component.Name + "." + name + "' is a derived value; it is computed and cannot be patched",
                        hint: "patch the values it is computed from", related: new[] { member.Span });
                }
                return new PatchTarget(kind, _type) { Component = component, Member = member };
            }

            /// <summary>
            /// 값의 타입 안으로 <paramref name="index"/>번째 조각부터 들어간다. 맵의 키로만 들어갈 수 있고,
            /// 리스트는 통째로 다룬다 (<c>set</c>, <c>append</c>, <c>remove</c>). 값이 있는지는 ④에서 본다.
            /// </summary>
            private PatchTarget? Navigate(PatchTarget target, int index, TypeRef type)
            {
                int nameKeyStart = -1;
                for (int i = index; i < _segments.Count; i++)
                {
                    var segment = _segments[i];
                    switch (type)
                    {
                        case DictTypeRef dict:
                            if (!KeyFits(dict.Key, segment))
                            {
                                return Fail(Codes.PatBadPathStep,
                                    "a key of " + dict.Display + " must be " + KeyDescription(dict.Key) + ", not " + Describe(segment));
                            }
                            if (!segment.Bracketed && nameKeyStart < 0) nameKeyStart = i;
                            else if (segment.Bracketed) nameKeyStart = -1;
                            if (dict.Key is DefIdTypeRef) DefKey(i);
                            target.Entry = dict;
                            target.EntryKey = _normalized[i];
                            type = dict.Value;
                            break;

                        case ListTypeRef _:
                            return Fail(Codes.PatBadPathStep,
                                "a list cannot be indexed in a patch path: patch the whole list with set, or use append and remove");

                        default:
                            return Fail(Codes.PatBadPathStep,
                                "a value of type " + type.Display + " has no '" + Describe(segment) + "' inside it",
                                hint: DottedKeyHint(nameKeyStart, i));
                    }
                }

                target.ValueType = type;
                // 경로가 맵 항목에서 끝나지 않았으면 (이름·컴포넌트 값에서 끝남) Entry는 비운다
                if (_segments.Count == index) target.Entry = null;
                return target;
            }

            private static bool KeyFits(TypeRef key, PathSegment segment)
            {
                if (key is PrimitiveTypeRef p)
                    return p.IsNumeric ? segment.IsNumber && ReferenceEquals(p, PrimitiveTypeRef.Int) : !segment.IsNumber;
                if (key is DefIdTypeRef) return !segment.IsNumber;
                return false;
            }

            private static string KeyDescription(TypeRef key) =>
                key is PrimitiveTypeRef p && p.IsNumeric ? "an integer, written [3]" : "a string (a name or ['text'])";

            private static string Describe(PathSegment segment) =>
                segment.IsNumber ? "[" + segment.Number + "]" : segment.Bracketed ? "['" + segment.Text + "']" : segment.Text!;

            /// <summary>
            /// 점이 든 맵 키를 점 표기로 쓴 것 같으면 대괄호로 쓰라고 안내한다:
            /// <c>Cost.core.Wood</c>에서 <c>Cost</c> 뒤의 <c>core.Wood</c>가 한 키라면 <c>Cost['core.Wood']</c>.
            /// </summary>
            private string? DottedKeyHint(int nameKeyStart, int failedAt)
            {
                if (nameKeyStart < 1) return null;
                var key = new StringBuilder();
                for (int i = nameKeyStart; i < _segments.Count; i++)
                {
                    var s = _segments[i];
                    if (s.Bracketed) return null;
                    if (key.Length > 0) key.Append('.');
                    key.Append(s.Text);
                }
                var prefix = new StringBuilder();
                for (int i = 0; i < nameKeyStart; i++) prefix.Append(_segments[i].Canonical(i == 0));
                return "if the key is \"" + key + "\", write " + prefix + "['" + key + "']";
            }
        }
    }
}
