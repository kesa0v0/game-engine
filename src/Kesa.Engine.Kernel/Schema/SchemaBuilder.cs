using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;

namespace Kesa.Engine.Schema
{
    /// <summary>
    /// ②a의 끝: 모든 모드의 스키마 선언을 모아 타입을 완성한다 (spec S1 ②a, schema-design 4).
    /// 하는 일: 이름 중복, 문자열 이름 풀이(컴포넌트 목록, <c>Ref["X"]</c>, <c>ref("X")</c>, <c>key=</c>),
    /// 보이는 범위, <c>extend</c> 적용과 충돌, <c>inverse</c> 등록.
    /// 값과 타입의 일치(기본값의 타입 등)는 검증 단계(⑥)가 한다.
    /// 진단은 모드별로 나눠 담아, 호출자가 실패한 모드를 가려낼 수 있게 한다.
    /// </summary>
    public static class SchemaBuilder
    {
        /// <param name="mods">스키마를 실행한 모드들 (로드 순서)</param>
        public static SchemaSet Build(IReadOnlyList<ModSchema> mods, ModVisibility visibility,
            StageDiagnostics diagnostics) =>
            new Builder(mods, visibility, diagnostics).Run();

        private sealed class Builder
        {
            private readonly IReadOnlyList<ModSchema> _mods;
            private readonly ModVisibility _visibility;
            private readonly StageDiagnostics _diagnostics;
            private readonly SchemaSet _set = new SchemaSet();
            private readonly HashSet<string> _ambiguous = new HashSet<string>(StringComparer.Ordinal);

            public Builder(IReadOnlyList<ModSchema> mods, ModVisibility visibility, StageDiagnostics diagnostics)
            {
                _mods = mods;
                _visibility = visibility;
                _diagnostics = diagnostics;
            }

            private void Error(string mod, string code, string message, SourceSpan span, string? hint = null,
                IReadOnlyList<SourceSpan>? related = null) =>
                _diagnostics.Error(mod, code, message, span, hint, related);

            public SchemaSet Run()
            {
                RegisterNames();
                ResolveKeys();
                ResolveTypeComponents();
                ApplyExtensions();
                CheckMembers();
                RegisterInverses();
                return _set;
            }

            // 1. Def 타입과 컴포넌트는 한 이름 공간이다: 둘 다 Def 파일에서 생성자 이름이 된다.
            private void RegisterNames()
            {
                var seen = new Dictionary<string, List<(string Mod, SourceSpan Span)>>(StringComparer.Ordinal);
                void Note(string name, string mod, SourceSpan span)
                {
                    if (!seen.TryGetValue(name, out var list)) seen[name] = list = new List<(string, SourceSpan)>();
                    list.Add((mod, span));
                }

                foreach (var m in _mods)
                {
                    foreach (var t in m.Types) Note(t.Name, t.Mod, t.Span);
                    foreach (var c in m.Components) Note(c.Name, c.Mod, c.Span);
                }

                bool Unique(string name, string mod, SourceSpan span)
                {
                    var list = seen[name];
                    if (list.Count == 1) return true;
                    _ambiguous.Add(name);
                    var related = new List<SourceSpan>();
                    var owners = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var (otherMod, otherSpan) in list)
                    {
                        owners.Add(otherMod);
                        if (!otherSpan.Equals(span)) related.Add(otherSpan);
                    }
                    const string hint = "Def types and components share one namespace";
                    if (owners.Count == 1)
                        Error(mod, Codes.SchDuplicateName, "'" + name + "' is declared more than once", span, hint, related);
                    else
                        _diagnostics.ConflictError(mod, Codes.SchDuplicateName,
                            "'" + name + "' is declared by more than one mod (" + string.Join(", ", owners) + ")", span,
                            hint, related);
                    return false;
                }

                foreach (var m in _mods)
                {
                    foreach (var t in m.Types)
                        if (Unique(t.Name, t.Mod, t.Span)) _set.Add(new DefType(t));
                    foreach (var c in m.Components)
                        if (Unique(c.Name, c.Mod, c.Span)) _set.Add(new ComponentType(c));
                }
            }

            private void ResolveKeys()
            {
                foreach (var c in _set.Components)
                {
                    if (c.Decl.Key is NameRef key) c.Key = ResolveType(key, c.Mod);
                }
            }

            private void ResolveTypeComponents()
            {
                foreach (var t in _set.Types)
                {
                    t.Consts.AddRange(t.Decl.Consts);
                    foreach (var r in t.Decl.Components)
                    {
                        var c = ResolveComponent(r, t.Mod);
                        if (c == null) continue;
                        if (TryFindMember(t, c.Name, out var mod, out var first))
                        {
                            Error(t.Mod, Codes.SchMemberConflict, "'" + t.Name + "' already has '" + c.Name + "'", r.Span,
                                related: new[] { first });
                            continue;
                        }
                        t.Components.Add(new ComponentSlot(c, t.Mod, r.Span));
                    }
                    // const 이름과 컴포넌트 이름은 Def 생성자의 키워드 인자로 함께 쓰인다
                    foreach (var k in t.Decl.Consts)
                    {
                        foreach (var slot in t.Components)
                        {
                            if (slot.Component.Name == k.Name)
                                Error(t.Mod, Codes.SchMemberConflict,
                                    "'" + k.Name + "' is both a const and a component of '" + t.Name + "'", k.Span,
                                    related: new[] { slot.Span });
                        }
                    }
                }
            }

            // 2. extend는 로드 순서대로 적용된다. 결과는 순서와 무관하다: 같은 이름 충돌은 순서로 풀지 않고 오류다.
            private void ApplyExtensions()
            {
                foreach (var m in _mods)
                {
                    foreach (var ext in m.Extensions)
                    {
                        var t = ResolveType(ext.Target, ext.Mod);
                        if (t == null) continue;

                        foreach (var r in ext.Components)
                        {
                            var c = ResolveComponent(r, ext.Mod);
                            if (c == null) continue;
                            if (TryFindMember(t, c.Name, out var mod, out var first))
                            {
                                Conflict(t, c.Name, ext.Mod, r.Span, mod, first);
                                continue;
                            }
                            t.Components.Add(new ComponentSlot(c, ext.Mod, r.Span));
                        }

                        foreach (var k in ext.Consts)
                        {
                            if (TryFindMember(t, k.Name, out var mod, out var first))
                            {
                                Conflict(t, k.Name, ext.Mod, k.Span, mod, first);
                                continue;
                            }
                            t.Consts.Add(k);
                        }
                    }
                }
            }

            /// <summary>
            /// 같은 이름을 두 모드가 더하면 두 모드 이름을 담은 오류 (schema-design 4.2).
            /// 타입을 선언한 모드의 이름과 겹치면 확장한 쪽만 오류다.
            /// </summary>
            private void Conflict(DefType t, string name, string mod, SourceSpan span, string existingMod,
                SourceSpan existingSpan)
            {
                if (existingMod == t.Mod)
                {
                    Error(mod, Codes.SchMemberConflict, "'" + t.Name + "' already has '" + name + "'", span,
                        related: new[] { existingSpan });
                    return;
                }
                if (existingMod == mod)
                {
                    Error(mod, Codes.SchMemberConflict, "'" + name + "' is added to '" + t.Name + "' twice", span,
                        related: new[] { existingSpan });
                    return;
                }
                string message = "'" + name + "' is added to '" + t.Name + "' by both mod '" + existingMod + "' and mod '" +
                                 mod + "'";
                _diagnostics.ConflictError(mod, Codes.SchMemberConflict, message, span, related: new[] { existingSpan });
                _diagnostics.ConflictError(existingMod, Codes.SchMemberConflict, message, existingSpan,
                    related: new[] { span });
            }

            private void CheckMembers()
            {
                foreach (var c in _set.Components)
                {
                    // 엔티티 키의 대상은 엔티티가 되는 타입이어야 한다. extend로 컴포넌트가 붙을 수 있으므로 확장 적용 뒤에 본다.
                    if (c.KeyKind == KeyKind.Entity && c.Key != null && !HasVisibleComponents(c.Key, c.Mod))
                    {
                        Error(c.Mod, Codes.SchBadDeclaration,
                            NoEntitiesMessage(c.Key, "to key by"), c.Decl.Key!.Value.Span,
                            hint: "write key=\"" + c.Key.Name + "\" for a row per Def");
                    }
                    foreach (var member in c.Members)
                    {
                        CheckTypeNames(member.Spec.Type, member.Mod);
                        CheckComponentMember(c, member);
                    }
                }
                foreach (var t in _set.Types)
                {
                    foreach (var k in t.Consts) CheckTypeNames(k.Spec.Type, k.Mod);
                }
            }

            private void CheckTypeNames(TypeRef type, string mod)
            {
                switch (type)
                {
                    case EntityRefTypeRef r:
                        // 엔티티를 가리키려면 대상이 엔티티가 되는 타입이어야 한다 (extend 적용 뒤라 컴포넌트가 확정됨)
                        var target = ResolveType(r.Target, mod);
                        if (target != null && !HasVisibleComponents(target, mod))
                            Error(mod, Codes.SchBadDeclaration, NoEntitiesMessage(target, "to refer to"), r.Target.Span,
                                hint: "to refer to a Def, write DefId[\"" + target.Name + "\"] (spec-gaps G32)");
                        break;
                    case DefIdTypeRef d:
                        ResolveType(d.Target, mod);
                        break;
                    case FunctionTypeRef f:
                        ResolveType(f.Target, mod);
                        break;
                    case ListTypeRef l:
                        CheckTypeNames(l.Element, mod);
                        break;
                    case DictTypeRef d:
                        CheckTypeNames(d.Key, mod);
                        CheckTypeNames(d.Value, mod);
                        break;
                }
            }

            private void CheckComponentMember(ComponentType c, MemberDecl member)
            {
                var spec = member.Spec;
                // default_from은 같은 컴포넌트의 const 또는 derived (schema-design 2.6). 둘 다 default_from을
                // 가질 수 없으므로 순환이 생기지 않는다.
                if (spec.DefaultFrom is NameRef from)
                {
                    if (from.Name == member.Name || !c.TryGetMember(from.Name, out var source))
                    {
                        Error(member.Mod, Codes.SchUnknownName,
                            "default_from names '" + from.Name + "', which is not another value of component '" + c.Name + "'",
                            from.Span, hint: Suggest.Hint(from.Name, MemberNames(c, member.Name)));
                    }
                    else if (source.Kind != MemberKind.Const && source.Kind != MemberKind.Derived)
                    {
                        Error(member.Mod, Codes.SchBadDeclaration,
                            "default_from must name a const or derived value, not a " +
                            (source.Kind == MemberKind.Ref ? "ref" : "field") + " value",
                            from.Span, related: new[] { source.Span });
                    }
                }
                if (spec.OrderBy is NameRef orderBy)
                {
                    if (!c.TryGetMember(orderBy.Name, out var key) || key == member)
                    {
                        Error(member.Mod, Codes.SchUnknownName,
                            "order_by names '" + orderBy.Name + "', which is not a field of component '" + c.Name + "'",
                            orderBy.Span, hint: Suggest.Hint(orderBy.Name, MemberNames(c, member.Name)));
                    }
                    else if (key.Kind != MemberKind.Field)
                    {
                        Error(member.Mod, Codes.SchBadDeclaration, "order_by must name a field, not a " +
                                                                    key.Kind.ToString().ToLowerInvariant() + " value",
                            orderBy.Span, related: new[] { key.Span });
                    }
                }
            }

            /// <summary>
            /// <paramref name="from"/> 모드가 보기에 이 타입이 엔티티가 되는가: 보이는 모드가 붙인 컴포넌트가 하나라도 있는가.
            /// 보이지 않는 모드의 <c>extend</c>로 판정이 바뀌면 결과가 의존하지 않는 모드의 설치 여부에 달린다 (schema-design 4.3).
            /// </summary>
            private bool HasVisibleComponents(DefType t, string from)
            {
                foreach (var slot in t.Components)
                    if (_visibility.CanSee(from, slot.Mod)) return true;
                return false;
            }

            private static string NoEntitiesMessage(DefType t, string purpose) =>
                t.IsDataOnly
                    ? "'" + t.Name + "' has no components, so it has no entities " + purpose
                    : "'" + t.Name + "' has components only from mods this mod does not depend on, so it has no entities " +
                      purpose;

            private static List<string> MemberNames(ComponentType c, string except)
            {
                var names = new List<string>();
                foreach (var m in c.Members)
                    if (m.Name != except) names.Add(m.Name);
                return names;
            }

            // 3. inverse는 대상 타입에 이름을 하나 더한다 (spec S3). 대상이 데이터 전용이면 CheckTypeNames가 이미 보고했다.
            private void RegisterInverses()
            {
                foreach (var c in _set.Components)
                {
                    foreach (var member in c.Members)
                    {
                        if (!(member.Spec.Inverse is NameRef inv) || !(member.Spec.Type is EntityRefTypeRef r)) continue;
                        if (!_set.TryGetType(r.Target.Name, out var target) || !_visibility.CanSee(member.Mod, target.Mod))
                            continue; // 이미 보고됨

                        if (TryFindMember(target, inv.Name, out var mod, out var first))
                        {
                            bool otherInverse = false;
                            foreach (var existing in target.Inverses)
                                if (existing.Name == inv.Name) otherInverse = true;
                            if (otherInverse && mod != member.Mod)
                            {
                                Conflict(target, inv.Name, member.Mod, inv.Span, mod, first);
                            }
                            else
                            {
                                Error(member.Mod, Codes.SchMemberConflict,
                                    "inverse '" + inv.Name + "' collides with '" + target.Name + "." + inv.Name + "'",
                                    inv.Span, related: new[] { first });
                            }
                            continue;
                        }
                        target.Inverses.Add(new InverseRelation(inv.Name, c, member, inv));
                    }
                }
            }

            /// <summary>Def 타입에서 이 이름을 이미 쓰는 것 (const, 컴포넌트, inverse)과 그것을 더한 모드.</summary>
            private static bool TryFindMember(DefType t, string name, out string mod, out SourceSpan span)
            {
                foreach (var k in t.Consts)
                {
                    if (k.Name != name) continue;
                    mod = k.Mod;
                    span = k.Span;
                    return true;
                }
                foreach (var slot in t.Components)
                {
                    if (slot.Component.Name != name) continue;
                    mod = slot.Mod;
                    span = slot.Span;
                    return true;
                }
                foreach (var inv in t.Inverses)
                {
                    if (inv.Name != name) continue;
                    mod = inv.Mod;
                    span = inv.Span;
                    return true;
                }
                mod = "";
                span = SourceSpan.None;
                return false;
            }

            private DefType? ResolveType(NameRef r, string from)
            {
                if (_set.TryGetType(r.Name, out var t))
                {
                    if (_visibility.CanSee(from, t.Mod)) return t;
                    NotVisible(r, from, t.Mod, "Def type");
                    return null;
                }
                if (_set.TryGetComponent(r.Name, out _))
                {
                    Error(from, Codes.SchUnknownName, "'" + r.Name + "' is a component, not a Def type", r.Span);
                    return null;
                }
                if (_ambiguous.Contains(r.Name)) return null;

                var visible = new List<string>();
                foreach (var x in _set.Types)
                    if (_visibility.CanSee(from, x.Mod)) visible.Add(x.Name);
                Error(from, Codes.SchUnknownName, "unknown Def type '" + r.Name + "'", r.Span,
                    hint: Suggest.Hint(r.Name, visible));
                return null;
            }

            private ComponentType? ResolveComponent(NameRef r, string from)
            {
                if (_set.TryGetComponent(r.Name, out var c))
                {
                    if (_visibility.CanSee(from, c.Mod)) return c;
                    NotVisible(r, from, c.Mod, "component");
                    return null;
                }
                if (_set.TryGetType(r.Name, out _))
                {
                    Error(from, Codes.SchUnknownName, "'" + r.Name + "' is a Def type, not a component", r.Span);
                    return null;
                }
                if (_ambiguous.Contains(r.Name)) return null;

                var visible = new List<string>();
                foreach (var x in _set.Components)
                    if (_visibility.CanSee(from, x.Mod)) visible.Add(x.Name);
                Error(from, Codes.SchUnknownName, "unknown component '" + r.Name + "'", r.Span,
                    hint: Suggest.Hint(r.Name, visible));
                return null;
            }

            private void NotVisible(NameRef r, string from, string owner, string what)
            {
                Error(from, Codes.SchNotVisible,
                    what + " '" + r.Name + "' belongs to mod '" + owner + "', which mod '" + from + "' does not depend on",
                    r.Span, hint: "add \"" + owner + "\" to dependencies in mod.py");
            }
        }
    }
}
