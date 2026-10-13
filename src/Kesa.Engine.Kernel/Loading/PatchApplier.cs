using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ④: Def 하나의 패치를 적용 순서(G50)대로 <see cref="ResolvedDef"/>에 적용한다. ③이 못 본 값에 의존하는 검사
    /// (spec-gaps G42·G44·G51·G53·G54)를 여기서 한다: 컴포넌트가 이미 붙어 있는지/없는지, <c>add</c>의 기준값,
    /// <c>remove</c>할 것이 있는지, 맵 <c>append</c>의 키 충돌. 실패한 패치는 호출 줄에 오류를 내고 건너뛰며
    /// 같은 Def의 나머지 패치는 계속 적용한다 (G60). 오류는 패치를 호출한 모드의 것이다.
    /// </summary>
    internal sealed class PatchApplier
    {
        private readonly StageDiagnostics _stage;
        private readonly HashSet<string> _conflicted;

        public PatchApplier(StageDiagnostics stage, HashSet<string> conflicted)
        {
            _stage = stage;
            _conflicted = conflicted;
        }

        private sealed class Failure : Exception
        {
            public Failure(string code, string message, SourceSpan span, string? hint = null)
                : base(message)
            {
                Code = code;
                Span = span;
                Hint = hint;
            }

            public string Code { get; }
            public SourceSpan Span { get; }
            public string? Hint { get; }
        }

        public void Apply(ResolvedDef def, IEnumerable<PatchLogEntry> entries)
        {
            foreach (var entry in entries)
            {
                try
                {
                    ApplyOne(def, entry);
                    entry.Status = PatchStatus.Applied;
                }
                catch (Failure f)
                {
                    // 이 Def에서 이미 충돌 오류가 난 모드의 실패는 따로 알리지 않는다: 충돌부터 풀어야 하고, 적용 순서가 우연이라 탓이 불분명하다 (G60)
                    if (!_conflicted.Contains(PatchConflicts.ConflictKey(entry.Mod, def.Id)))
                        _stage.Error(entry.Mod, f.Code, f.Message, f.Span, f.Hint);
                    entry.Status = PatchStatus.Failed;
                }
            }
        }

        private void ApplyOne(ResolvedDef def, PatchLogEntry entry)
        {
            var target = entry.Patch.Target;
            switch (target.Kind)
            {
                case PatchTargetKind.Component:
                    AttachOrDetachComponent(def, entry);
                    return;
                case PatchTargetKind.Row:
                    AttachOrDetachRow(def, entry);
                    return;
            }
            ApplyToValue(def, entry);
        }

        // ---- 컴포넌트와 줄 붙이기·떼기 (G44·G51) ----

        private static ResolvedValue Patched(PatchLogEntry entry, ResolvedDef def, Value value, SourceSpan span) =>
            new ResolvedValue(value, ValueOrigin.Patched, def.Record, span, entry);

        private static void SetMembers(OrderedMap<ResolvedValue> members, ComponentValue source, PatchLogEntry entry, ResolvedDef def)
        {
            foreach (var f in source.Fields) members.Set(f.Name, Patched(entry, def, f.Value, f.Span));
        }

        private static void AttachOrDetachComponent(ResolvedDef def, PatchLogEntry entry)
        {
            var call = entry.Call;
            var component = entry.Patch.Target.Component!;
            bool present = def.Components.Contains(component.Name);
            if (call.Op == PatchOp.Remove)
            {
                if (!present)
                {
                    throw new Failure(Codes.PatNoComponent,
                        "Def '" + def.Id + "' has no component '" + component.Name + "' to remove", call.PathSpan,
                        hint: "it was not attached, or another patch already removed it");
                }
                def.Components.Remove(component.Name);
                return;
            }

            if (present)
            {
                throw new Failure(Codes.PatAlreadyThere,
                    "Def '" + def.Id + "' already has component '" + component.Name + "'", call.PathSpan,
                    hint: "patch its values one by one, for example " + component.Name + ".<value>");
            }
            var attached = new ResolvedComponent(component) { AttachedBy = entry };
            SetMembers(attached.Members, (ComponentValue)call.Value!, entry, def);
            def.Components.Set(component.Name, attached);
        }

        private static void AttachOrDetachRow(ResolvedDef def, PatchLogEntry entry)
        {
            var call = entry.Call;
            var target = entry.Patch.Target;
            var component = target.Component!;
            string key = target.RowKey!;
            if (!def.Components.TryGet(component.Name, out var container))
            {
                container = new ResolvedComponent(component);
                if (call.Op != PatchOp.Remove) def.Components.Set(component.Name, container);
            }
            bool present = container.Rows.Contains(key);
            if (call.Op == PatchOp.Remove)
            {
                if (!present)
                {
                    throw new Failure(Codes.PatNoComponent,
                        "Def '" + def.Id + "' has no row '" + key + "' of '" + component.Name + "' to remove", call.PathSpan);
                }
                container.Rows.Remove(key);
                return;
            }

            if (present)
            {
                throw new Failure(Codes.PatAlreadyThere,
                    "Def '" + def.Id + "' already has row '" + key + "' of '" + component.Name + "'", call.PathSpan,
                    hint: "patch its values one by one, for example " + component.Name + "." + key + ".<value>");
            }
            var row = new ResolvedRow { AttachedBy = entry };
            SetMembers(row.Members, (ComponentValue)call.Value!, entry, def);
            container.Rows.Set(key, row);
        }

        // ---- 값 패치 ----

        private void ApplyToValue(ResolvedDef def, PatchLogEntry entry)
        {
            var call = entry.Call;
            var target = entry.Patch.Target;
            var member = target.Member!;

            OrderedMap<ResolvedValue> map;
            ComponentType? component = null;
            int keyStart;
            switch (target.Kind)
            {
                case PatchTargetKind.Const:
                    map = def.Consts;
                    keyStart = 1;
                    break;
                case PatchTargetKind.Member:
                    component = target.Component!;
                    map = EnsureComponent(def, target, call).Members;
                    keyStart = 2;
                    break;
                default: // RowMember
                    component = target.Component!;
                    map = EnsureRow(def, target, call).Members;
                    keyStart = 3;
                    break;
            }

            map.TryGet(member.Name, out var stored);
            Value? effective = stored != null ? stored.Value : MemberDefaults.Of(component, member, map);

            var keys = new List<PathSegment>();
            for (int i = keyStart; i < call.Path.Segments.Count; i++) keys.Add(call.Path.Segments[i]);

            var type = member.Spec.Type;
            var leafType = target.ValueType!;
            Value updated = Update(effective, type, keys, 0, current => Leaf(call, leafType, current, keys.Count > 0, def), call);
            map.Set(member.Name, Patched(entry, def, updated, call.Span));
        }

        private static ResolvedComponent EnsureComponent(ResolvedDef def, PatchTarget target, PatchCall call)
        {
            var component = target.Component!;
            if (def.Components.TryGet(component.Name, out var found)) return found;
            if (!target.IsTypeSlot)
            {
                throw new Failure(Codes.PatNoComponent,
                    "Def '" + def.Id + "' has no component '" + component.Name + "'", call.PathSpan,
                    hint: "attach it first: patch.set(\"" + def.Id + "\", \"" + component.Name + "\", " + component.Name + "(...))");
            }
            // 타입에 붙은 컴포넌트는 모든 Def가 가진다: 값을 처음 쓸 때 만든다
            var created = new ResolvedComponent(component);
            def.Components.Set(component.Name, created);
            return created;
        }

        private static ResolvedRow EnsureRow(ResolvedDef def, PatchTarget target, PatchCall call)
        {
            var component = target.Component!;
            string key = target.RowKey!;
            if (!def.Components.TryGet(component.Name, out var container))
            {
                container = new ResolvedComponent(component);
                def.Components.Set(component.Name, container);
            }
            if (container.Rows.TryGet(key, out var row)) return row;

            bool implicitRow = target.IsTypeSlot && component.Keys == KeysMode.All;
            if (!implicitRow)
            {
                throw new Failure(Codes.PatNoComponent,
                    "Def '" + def.Id + "' has no row '" + key + "' of '" + component.Name + "'", call.PathSpan,
                    hint: "attach it first: patch.set(\"" + def.Id + "\", \"" + component.Name + "." + key + "\", " +
                          component.Name + "(...))");
            }
            row = new ResolvedRow();
            container.Rows.Set(key, row);
            return row;
        }

        /// <summary>맵 키 조각을 따라 내려가 끝에서 <paramref name="leaf"/>를 적용하고, 바뀐 값을 위로 다시 짠다 (G54: 없는 키는 만든다).</summary>
        private static Value Update(Value? current, TypeRef type, IReadOnlyList<PathSegment> keys, int i,
            Func<Value?, Value> leaf, PatchCall call)
        {
            if (i == keys.Count) return leaf(current);

            var dictType = (DictTypeRef)type;
            var items = current is DictValue dict ? new List<DictItem>(dict.Items) : new List<DictItem>();
            var key = KeyValue(keys[i]);
            int at = IndexOfKey(items, key);
            var updated = Update(at >= 0 ? items[at].Value : null, dictType.Value, keys, i + 1, leaf, call);
            if (at >= 0) items[at] = new DictItem(items[at].Key, updated, items[at].Span);
            else items.Add(new DictItem(key, updated, call.PathSpan));
            return new DictValue(items);
        }

        private static Value KeyValue(PathSegment segment) =>
            segment.IsNumber ? (Value)new IntValue(segment.Number) : new StrValue(segment.Text!);

        private static int IndexOfKey(List<DictItem> items, Value key)
        {
            for (int i = 0; i < items.Count; i++)
                if (Equal(items[i].Key, key)) return i;
            return -1;
        }

        private static Value Leaf(PatchCall call, TypeRef type, Value? current, bool isEntry, ResolvedDef def)
        {
            string where = "'" + call.Path.Text + "'";
            switch (call.Op)
            {
                case PatchOp.Set:
                    return call.Value!;

                case PatchOp.Add:
                    if (current == null)
                    {
                        throw new Failure(Codes.PatNoBase,
                            "patch.add has no value to add to: " + where + " has no value on '" + def.Id + "'" +
                            (isEntry ? " (no such map entry)" : " and no default"),
                            call.PathSpan, hint: "use patch.set to give it a value");
                    }
                    return AddNumbers(call, type, current);

                case PatchOp.Append:
                    return Append(call, type, current);

                default: // Remove
                    return Remove(call, type, current, where, def);
            }
        }

        private static Value AddNumbers(PatchCall call, TypeRef type, Value current)
        {
            var delta = call.Value!;
            if (ReferenceEquals(type, PrimitiveTypeRef.Int) && current is IntValue a && delta is IntValue b)
            {
                if (!FixedMath.TryAdd(a.Value, b.Value, out long sum)) throw OutOfRange(call);
                return new IntValue(sum);
            }
            if (!TryFixed(current, out var x) || !TryFixed(delta, out var y)) throw OutOfRange(call);
            if (!FixedMath.TryAdd(x.Raw, y.Raw, out long raw)) throw OutOfRange(call);
            return new FixedValue(Fixed.FromRaw(raw));
        }

        private static bool TryFixed(Value value, out Fixed result)
        {
            if (value is FixedValue f)
            {
                result = f.Value;
                return true;
            }
            if (value is IntValue i) return FixedMath.TryFromInt(i.Value, out result);
            result = default;
            return false;
        }

        private static Failure OutOfRange(PatchCall call) =>
            new Failure(Codes.PatOutOfRange, "the result of patch.add is out of range", call.ValueSpan);

        private static Value Append(PatchCall call, TypeRef type, Value? current)
        {
            if (type is ListTypeRef)
            {
                var items = new List<Value>();
                if (current is ListValue existing) items.AddRange(existing.Items);
                items.AddRange(((ListValue)call.Value!).Items);
                return new ListValue(items);
            }

            var entries = new List<DictItem>();
            if (current is DictValue map) entries.AddRange(map.Items);
            foreach (var added in ((DictValue)call.Value!).Items)
            {
                int at = IndexOfKey(entries, added.Key);
                if (at >= 0)
                {
                    throw new Failure(Codes.PatKeyExists, "the map already has the key " + Describe(added.Key), added.Span,
                        hint: "use patch.set(..., \"" + call.Path.Text + "." + Describe(added.Key).Trim('"') + "\", value) to change it");
                }
                entries.Add(added);
            }
            return new DictValue(entries);
        }

        private static Value Remove(PatchCall call, TypeRef type, Value? current, string where, ResolvedDef def)
        {
            var wanted = call.Value!;
            if (type is ListTypeRef)
            {
                var items = current is ListValue list ? new List<Value>(list.Items) : new List<Value>();
                for (int i = 0; i < items.Count; i++)
                {
                    if (!Equal(items[i], wanted)) continue;
                    items.RemoveAt(i);
                    return new ListValue(items);
                }
                throw new Failure(Codes.PatNoTarget,
                    "the list " + where + " of '" + def.Id + "' has no element " + Describe(wanted) + " to remove", call.ValueSpan);
            }

            var entries = current is DictValue map ? new List<DictItem>(map.Items) : new List<DictItem>();
            int at = IndexOfKey(entries, wanted);
            if (at < 0)
            {
                throw new Failure(Codes.PatNoTarget,
                    "the map " + where + " of '" + def.Id + "' has no key " + Describe(wanted) + " to remove", call.ValueSpan);
            }
            entries.RemoveAt(at);
            return new DictValue(entries);
        }

        private static string Describe(Value value)
        {
            switch (value)
            {
                case StrValue s: return "\"" + s.Value + "\"";
                case IntValue i: return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case FixedValue f: return f.Value.ToString();
                case BoolValue b: return b.Value ? "True" : "False";
                case NoneValue _: return "None";
            }
            return value.KindName;
        }

        /// <summary>데이터 값의 동등성. <c>int</c>와 <c>fixed</c>는 수로 비교한다.</summary>
        private static bool Equal(Value a, Value b)
        {
            switch (a)
            {
                case StrValue sa: return b is StrValue sb && sa.Value == sb.Value;
                case BoolValue ba: return b is BoolValue bb && ba.Value == bb.Value;
                case NoneValue _: return b is NoneValue;
                case IntValue ia when b is IntValue ib: return ia.Value == ib.Value;
                case IntValue _:
                case FixedValue _:
                    return TryFixed(a, out var x) && TryFixed(b, out var y) && x.Raw == y.Raw;
                case ListValue la:
                    if (!(b is ListValue lb) || la.Items.Count != lb.Items.Count) return false;
                    for (int i = 0; i < la.Items.Count; i++)
                        if (!Equal(la.Items[i], lb.Items[i])) return false;
                    return true;
                case DictValue da:
                    if (!(b is DictValue db) || da.Items.Count != db.Items.Count) return false;
                    foreach (var item in da.Items)
                    {
                        int at = IndexOfKey(new List<DictItem>(db.Items), item.Key);
                        if (at < 0 || !Equal(item.Value, db.Items[at].Value)) return false;
                    }
                    return true;
            }
            return ReferenceEquals(a, b);
        }
    }
}
