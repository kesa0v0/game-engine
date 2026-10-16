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
                OrderStages();
                return _set;
            }

            // 단계 (spec S5, G152·G153): 중복 → 참조 풀이(필수는 의존 필요, 선택은 없으면 무시) → 위상 정렬(동률은 id 순) → 사이클 오류
            private void OrderStages()
            {
                var first = new SortedDictionary<string, StageDecl>(StringComparer.Ordinal);
                foreach (var m in _mods)
                {
                    foreach (var s in m.Stages)
                    {
                        if (first.TryGetValue(s.Id, out var earlier))
                        {
                            Error(s.Mod, Codes.SchDuplicateName, "stage '" + s.Id + "' is declared more than once", s.Span,
                                related: new[] { earlier.Span });
                            continue;
                        }
                        first[s.Id] = s;
                        _set.AddStage(s);
                    }
                }

                // 간선 a → b: a가 b보다 먼저. 위치는 그 간선을 적은 곳 (사이클 보고용)
                var next = new SortedDictionary<string, SortedDictionary<string, SourceSpan>>(StringComparer.Ordinal);
                var incoming = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var id in first.Keys)
                {
                    next[id] = new SortedDictionary<string, SourceSpan>(StringComparer.Ordinal);
                    incoming[id] = 0;
                }
                void Edge(string from, string to, SourceSpan span)
                {
                    if (next[from].ContainsKey(to)) return;
                    next[from][to] = span;
                    incoming[to]++;
                }
                foreach (var s in first.Values)
                {
                    foreach (var r in s.After) if (Resolve(s, r, optional: false)) Edge(r.Name, s.Id, r.Span);
                    foreach (var r in s.OptionalAfter) if (Resolve(s, r, optional: true)) Edge(r.Name, s.Id, r.Span);
                    foreach (var r in s.Before) if (Resolve(s, r, optional: false)) Edge(s.Id, r.Name, r.Span);
                    foreach (var r in s.OptionalBefore) if (Resolve(s, r, optional: true)) Edge(s.Id, r.Name, r.Span);
                }

                var ready = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var pair in incoming)
                    if (pair.Value == 0) ready.Add(pair.Key);
                while (ready.Count > 0)
                {
                    string id = ready.Min!;
                    ready.Remove(id);
                    _set.OrderStage(first[id]);
                    foreach (var to in next[id].Keys)
                        if (--incoming[to] == 0) ready.Add(to);
                }
                if (_set.StageOrder.Count == first.Count) return;

                // 남은 단계마다 남은 앞 단계가 하나 이상 있다. 그래서 앞 단계를 거슬러 가면 반드시 되돌아오고, 그 고리가 사이클이다
                // (사이클 뒤에 매달려 남은 단계에서 출발해도 사이클에 닿는다)
                var left = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var id in first.Keys)
                    if (_set.StageIndex(id) < 0) left.Add(id);
                var previous = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var from in left)
                    foreach (var to in next[from].Keys)
                        if (left.Contains(to) && !previous.ContainsKey(to)) previous[to] = from;
                var reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (var start in left)
                {
                    if (reported.Contains(start)) continue;
                    var path = new List<string>();
                    var at = new Dictionary<string, int>(StringComparer.Ordinal);
                    string cur = start;
                    while (!at.ContainsKey(cur))
                    {
                        at[cur] = path.Count;
                        path.Add(cur);
                        cur = previous[cur];
                    }
                    var cycle = path.GetRange(at[cur], path.Count - at[cur]);
                    cycle.Reverse(); // 거슬러 모았으니 앞 → 뒤 순서로
                    // 가장 작은 id에서 시작해 같은 사이클은 늘 같은 문구로
                    int min = 0;
                    for (int i = 1; i < cycle.Count; i++)
                        if (string.CompareOrdinal(cycle[i], cycle[min]) < 0) min = i;
                    var rotated = cycle.GetRange(min, cycle.Count - min);
                    rotated.AddRange(cycle.GetRange(0, min));
                    cycle = rotated;
                    if (cycle.Exists(reported.Contains)) continue;
                    foreach (var id in cycle) reported.Add(id);
                    var related = new List<SourceSpan>();
                    for (int i = 0; i < cycle.Count; i++) related.Add(next[cycle[i]][cycle[(i + 1) % cycle.Count]]);
                    var head = first[cycle[0]];
                    Error(head.Mod, Codes.SchBadDeclaration,
                        "stages are ordered in a cycle: " + string.Join(" → ", cycle) + " → " + cycle[0], head.Span,
                        hint: "each arrow is an after= or before=; drop one of them (spec S5)", related: related);
                }
            }

            /// <summary>단계 참조 하나. 필수는 있어야 하고 그 모드에 의존해야 한다. 선택은 없으면 조용히 빠진다.</summary>
            private bool Resolve(StageDecl from, NameRef r, bool optional)
            {
                if (r.Name == from.Id)
                {
                    Error(from.Mod, Codes.SchBadDeclaration, "stage '" + from.Id + "' cannot be ordered against itself", r.Span);
                    return false;
                }
                if (!_set.TryGetStage(r.Name, out var target))
                {
                    if (optional) return false;
                    var ids = new List<string>();
                    foreach (var m in _mods)
                        foreach (var s in m.Stages) ids.Add(s.Id);
                    Error(from.Mod, Codes.SchUnknownName, "unknown stage '" + r.Name + "'", r.Span,
                        hint: Suggest.Hint(r.Name, ids) ?? "use optional_after= or optional_before= for a stage that may not be there (spec S5)");
                    return false;
                }
                if (!optional && !_visibility.CanSee(from.Mod, target.Mod))
                {
                    Error(from.Mod, Codes.SchNotVisible,
                        "stage '" + r.Name + "' belongs to mod '" + target.Mod + "', which mod '" + from.Mod + "' does not depend on",
                        r.Span, hint: "add \"" + target.Mod + "\" to dependencies in mod.py, or use optional_after= / optional_before= (spec-gaps G153)");
                    return false;
                }
                return true;
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

                // 엔진 신호(Tick)는 먼저 자리를 잡는다. 모드가 같은 이름을 쓰면 그 모드의 오류다 (G154)
                foreach (var name in EngineSignals.All)
                    _set.Add(new SignalType(new SignalDecl(name, SourceSpan.None, EngineSignals.Owner, new List<SignalField>())));
                bool Reserved(string name, string mod, SourceSpan span)
                {
                    if (!EngineSignals.IsEngine(name)) return false;
                    Error(mod, Codes.SchDuplicateName, "'" + name + "' is a signal declared by the engine", span,
                        hint: "pick another name; handle the engine's signal with @on(" + name + ") (spec-gaps G154)");
                    return true;
                }

                foreach (var m in _mods)
                {
                    foreach (var t in m.Types) if (!Reserved(t.Name, t.Mod, t.Span)) Note(t.Name, t.Mod, t.Span);
                    foreach (var c in m.Components) if (!Reserved(c.Name, c.Mod, c.Span)) Note(c.Name, c.Mod, c.Span);
                    foreach (var s in m.Signals) if (!Reserved(s.Name, s.Mod, s.Span)) Note(s.Name, s.Mod, s.Span);
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
                    const string hint = "Def types, components and signals share one namespace";
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
                    // 엔진 이름과 겹친 선언은 위에서 오류를 냈고 등록하지 않는다
                    foreach (var t in m.Types)
                        if (!EngineSignals.IsEngine(t.Name) && Unique(t.Name, t.Mod, t.Span)) _set.Add(new DefType(t));
                    foreach (var c in m.Components)
                        if (!EngineSignals.IsEngine(c.Name) && Unique(c.Name, c.Mod, c.Span)) _set.Add(new ComponentType(c));
                    foreach (var s in m.Signals)
                        if (!EngineSignals.IsEngine(s.Name) && Unique(s.Name, s.Mod, s.Span)) _set.Add(new SignalType(s));
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
                            if (ext.Mod != t.Mod) RequireDefaults(t, c, ext.Mod, r.Span);
                        }

                        foreach (var k in ext.Consts)
                        {
                            if (TryFindMember(t, k.Name, out var mod, out var first))
                            {
                                Conflict(t, k.Name, ext.Mod, k.Span, mod, first);
                                continue;
                            }
                            t.Consts.Add(k);
                            if (ext.Mod != t.Mod && NeedsDefault(k.Spec)) NoDefault(k.Mod, k.Span, "'" + k.Name + "' added to '" + t.Name + "'");
                        }
                    }
                }
            }

            /// <summary>기본값이 없으면 필수가 되는 <c>const</c>인가: 리스트·맵은 비어 있는 것이 기본값이다 (spec-gaps G67).</summary>
            private static bool NeedsDefault(MemberSpec spec) =>
                spec.Kind == MemberKind.Const && spec.Default == null && !(spec.Type is ListTypeRef) && !(spec.Type is DictTypeRef);

            /// <summary><c>keys="all"</c> 줄의 런타임 칸이 생성 순간 없이 값을 가질 수 없는 이유. 문제없으면 null (G89·G93).</summary>
            private static string? RowWithoutCreation(MemberSpec spec)
            {
                switch (spec.Kind)
                {
                    case MemberKind.Field:
                        if (spec.DefaultFrom != null) return "uses default_from";
                        if (spec.Default is Script.FunctionValue) return "uses a function default";
                        if (spec.Default == null) return "has no default";
                        return null;
                    case MemberKind.Ref:
                        return spec.Optional ? null : "is a required ref";
                    default:
                        return null;
                }
            }

            private void NoDefault(string mod, SourceSpan span, string what)
            {
                Error(mod, Codes.SchBadDeclaration, what + " needs a default: other mods' Defs cannot see it, so nothing would fill it in",
                    span, hint: "add default=... to the declaration (spec-gaps G74)");
            }

            /// <summary>다른 모드가 붙인 컴포넌트의 기본값 없는 <c>const</c> 멤버는 확장한 모드의 오류다 (G74).</summary>
            private void RequireDefaults(DefType t, ComponentType c, string extMod, SourceSpan span)
            {
                foreach (var member in c.Members)
                {
                    if (NeedsDefault(member.Spec))
                        NoDefault(extMod, span, "'" + c.Name + "." + member.Name + "' attached to '" + t.Name + "'");
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
                        // 함수 본문에서 h.entity, h.key는 엔티티와 줄 키를, h.<이름>은 자기 멤버를 읽는다 (G103)
                        if (member.Name == "entity" || member.Name == "key")
                        {
                            Error(member.Mod, Codes.SchBadDeclaration,
                                "'" + member.Name + "' is reserved in functions: h." + member.Name + " reads the " +
                                (member.Name == "entity" ? "entity" : "row key") + ", so '" + c.Name + "' cannot have a member of that name",
                                member.Span, hint: "choose another name for this value (spec-gaps G103)");
                        }
                        else if (member.Name == c.Name)
                        {
                            Error(member.Mod, Codes.SchBadDeclaration,
                                "'" + c.Name + "' has a member named like itself, so h." + member.Name +
                                " in a function would be ambiguous between the member and the component",
                                member.Span, hint: "choose another name for this value (spec-gaps G103)");
                        }
                        // keys="all"은 키 타입 Def마다 줄이 있고 아무도 그 줄을 다 쓰지 않으니 기본값이 채워야 한다 (G77)
                        if (c.IsKeyed && c.KeyKind == KeyKind.Def && c.Keys == KeysMode.All && NeedsDefault(member.Spec))
                        {
                            Error(member.Mod, Codes.SchBadDeclaration,
                                "'" + c.Name + "." + member.Name + "' needs a default: every " + c.Key!.Name +
                                " has a '" + c.Name + "' row, and nobody writes all of them",
                                member.Span, hint: "add default=... or use keys=\"listed\" (spec-gaps G77)");
                        }
                        // 그 줄은 아무도 만들지 않으니 생성 순간이 없다: 런타임 칸도 고정 기본값이 있어야 한다 (G89·G93)
                        if (c.IsKeyed && c.KeyKind == KeyKind.Def && c.Keys == KeysMode.All)
                        {
                            string? problem = RowWithoutCreation(member.Spec);
                            if (problem != null)
                            {
                                Error(member.Mod, Codes.SchBadDeclaration,
                                    "'" + c.Name + "." + member.Name + "' " + problem + ": rows of a keys=\"all\" component " +
                                    "are never created, so the value must be a fixed default",
                                    member.Span, hint: "use keys=\"listed\" to create rows explicitly (spec-gaps G89, G93)");
                            }
                        }
                    }
                }
                foreach (var t in _set.Types)
                {
                    foreach (var k in t.Consts) CheckTypeNames(k.Spec.Type, k.Mod);
                }
                foreach (var s in _set.Signals)
                {
                    foreach (var f in s.Fields) CheckTypeNames(f.Type, s.Mod);
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
                if (_set.TryGetSignal(r.Name, out _))
                {
                    Error(from, Codes.SchUnknownName, "'" + r.Name + "' is a signal, not a Def type", r.Span);
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
                if (_set.TryGetSignal(r.Name, out _))
                {
                    Error(from, Codes.SchUnknownName, "'" + r.Name + "' is a signal, not a component", r.Span);
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
