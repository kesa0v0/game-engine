using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 저장과 불러오기 (spec S8, spec-gaps G140). 엔진이 정한 정규형 JSON이고 같은 상태는 같은 바이트다 (C4).
    /// 지금은 월드 상태(엔티티·값·줄·기여 행·카운터)만 저장한다. 멈춘 Flow가 있으면 아직 저장하지 못한다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>저장 형식 버전. 형식이 바뀌면 올리고 이전 버전을 읽는 단계를 더한다 (S8).</summary>
        public const int SaveFormatVersion = 1;

        /// <summary>이 월드를 만든 모드 목록 (로드 순서). <see cref="DefSet"/>으로 직접 만든 월드는 null.</summary>
        private IReadOnlyList<ModManifest>? _mods;

        /// <summary>
        /// 월드를 저장한다. 커맨드나 함수가 실행 중이면 저장하지 않는다(상태가 반쯤 바뀐 순간이다).
        /// </summary>
        public byte[] Save()
        {
            if (_undo != null || _meter != null)
                throw new InvalidOperationException("the world cannot be saved while a command or a function is running");
            if (_emitted.Count > 0) // 제출·답·응답이 끝나기 전에 비우므로 여기 오면 엔진 결함이다 (G148)
                throw new InvalidOperationException("engine: emitted signals are still waiting for their handlers");

            var mods = new JsonArray();
            if (_mods != null)
                foreach (var m in _mods)
                    mods.Add(new JsonObject()
                        .Add("id", new JsonString(m.Id))
                        .Add("version", m.Version == null ? (JsonNode)JsonNull.Instance : new JsonString(m.Version)));

            var counters = new JsonObject()
                .Add("entity", new JsonNumber(_nextId))
                .Add("contribution", new JsonNumber(_nextContribution))
                .Add("flow", new JsonNumber(_nextFlow))
                .Add("request", new JsonNumber(_nextRequest));

            var entities = new JsonArray();
            foreach (var id in LiveIds()) entities.Add(SaveEntity(_entities[id]));

            var contributions = new JsonArray();
            foreach (var c in _contributions.Values) contributions.Add(SaveContribution(c));

            var root = new JsonObject()
                .Add("format", new JsonNumber(SaveFormatVersion))
                .Add("mods", mods)
                .Add("tick", new JsonNumber(_tick))
                .Add("seed", new JsonNumber(_seed))
                .Add("counters", counters)
                .Add("entities", entities)
                .Add("contributions", contributions)
                .Add("flows", SaveFlows())
                .Add("when", SaveWhens())
                .Add("when_cooldown", SaveCooldowns())
                .Add("when_once", SaveOnce())
                .Add("rand", SaveRandCounts());
            return CanonicalJson.Write(root);
        }

        /// <summary>살아 있는 엔티티 id (id 순).</summary>
        private SortedSet<long> LiveIds()
        {
            var ids = new SortedSet<long>();
            foreach (var type in _schema.Types)
                if (_byType.TryGetValue(type, out var set))
                    foreach (var id in set) ids.Add(id);
            return ids;
        }

        private JsonObject SaveEntity(EntityRecord entity)
        {
            var runtime = new JsonArray();
            foreach (var name in entity.Runtime.Keys) runtime.Add(new JsonString(name));

            // 키 없는 컴포넌트의 엔티티 값: 컴포넌트 이름 순, 멤버 이름 순 (쓴 값은 기본값과 같아도 적는다)
            var byName = new SortedDictionary<string, ComponentType>(StringComparer.Ordinal);
            foreach (var c in ComponentsOf(entity)) byName[c.Name] = c;
            var values = new JsonObject();
            foreach (var component in byName.Values)
            {
                if (component.IsKeyed) continue;
                var members = SaveValues(component, entity.Values);
                if (members.Members.Count > 0) values.Add(component.Name, members);
            }

            var rows = new JsonObject();
            foreach (var pair in entity.Rows)
            {
                _schema.TryGetComponent(pair.Key, out var component);
                var list = new JsonArray();
                foreach (var r in pair.Value.ByDef) list.Add(SaveRow(new JsonString(r.Key), r.Value, component));
                foreach (var r in pair.Value.ByEntity) list.Add(SaveRow(EntityNode(r.Key), r.Value, component));
                foreach (var r in pair.Value.ByInt) list.Add(SaveRow(new JsonNumber(r.Key), r.Value, component));
                rows.Add(pair.Key, list);
            }

            return new JsonObject()
                .Add("id", new JsonNumber(entity.Id))
                .Add("def", new JsonString(entity.Def.Id))
                .Add("runtime", runtime)
                .Add("values", values)
                .Add("rows", rows);
        }

        private static JsonObject SaveRow(JsonNode key, RowRecord row, ComponentType component) =>
            new JsonObject()
                .Add("key", key)
                .Add("attached", new JsonBool(row.Attached))
                .Add("values", SaveValues(component, row.Values));

        private static JsonObject SaveValues(ComponentType component, Dictionary<MemberDecl, Value> values)
        {
            var byName = new SortedDictionary<string, MemberDecl>(StringComparer.Ordinal);
            foreach (var m in component.Members) byName[m.Name] = m;
            var result = new JsonObject();
            foreach (var member in byName.Values)
                if (values.TryGetValue(member, out var v)) result.Add(member.Name, SaveValue(v));
            return result;
        }

        private static JsonNode SaveKey(RowKey key) =>
            key.Kind == KeyKind.Def ? new JsonString(key.DefKey!)
            : key.Kind == KeyKind.Entity ? EntityNode(key.Number)
            : (JsonNode)new JsonNumber(key.Number);

        private static JsonObject EntityNode(long id) => new JsonObject().Add("entity", new JsonNumber(id));

        /// <summary>
        /// 값 하나. 태그는 엔진이 정한 닫힌 집합이다: <c>null</c>, bool, 수(int), 문자열, 리스트(배열),
        /// <c>{"fixed": raw}</c>, <c>{"entity": id}</c>, <c>{"dict": [[키, 값], ...]}</c>(넣은 순), <c>{"text": 키, "args": {...}}</c>.
        /// </summary>
        private static JsonNode SaveValue(Value value)
        {
            switch (value)
            {
                case NoneValue _: return JsonNull.Instance;
                case BoolValue b: return new JsonBool(b.Value);
                case IntValue i: return new JsonNumber(i.Value);
                case FixedValue f: return new JsonObject().Add("fixed", new JsonNumber(f.Value.Raw));
                case StrValue s: return new JsonString(s.Value);
                case IEntityReference e: return EntityNode(e.EntityId.Value);
                case ListValue l:
                    var items = new JsonArray();
                    foreach (var item in l.Items) items.Add(SaveValue(item));
                    return items;
                case DictValue d:
                    var pairs = new JsonArray();
                    foreach (var item in d.Items) pairs.Add(new JsonArray().Add(SaveValue(item.Key)).Add(SaveValue(item.Value)));
                    return new JsonObject().Add("dict", pairs);
                case TextValue t:
                    var names = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var pair in t.Args) names.Add(pair.Key);
                    var args = new JsonObject();
                    foreach (var name in names) args.Add(name, SaveValue(t.Args[name]));
                    return new JsonObject().Add("text", new JsonString(t.Key)).Add("args", args);
            }
            throw new InvalidOperationException("a " + value.KindName + " value cannot be saved yet");
        }

        // ---- 불러오기 ----

        /// <summary>
        /// 저장을 새 월드로 불러온다 (S8 "로드는 원자적": 지금 월드는 건드리지 않는다). 실패해도 예외가 아니라
        /// 진단 목록으로 알린다. 저장이 지금 내용(Def·컴포넌트·멤버·타입)과 맞지 않으면 무엇이 어긋나는지 모두 적고 거부한다.
        /// 모드 목록이 달라진 것은 경고로만 알린다(Def 값 변경은 항상 허용, S8).
        /// <paramref name="dropRemoved"/>를 켜면 지워진 것(컴포넌트, 멤버, Def 목록에서 빠진 컴포넌트, 없어진 Def를 키로 한 줄)의
        /// 저장된 값은 버리고 버린 곳마다 경고(<c>SAV005</c>)를 남긴 채 불러온다(G210). 타입이 바뀐 값 같은 나머지 어긋남은 그래도 거부한다.
        /// </summary>
        public static WorldLoadResult Load(ContentLoadResult loaded, byte[] save, int stepBudget = DefaultStepBudget,
            bool requireSubmitPolicy = false, int maxSignalDepth = DefaultMaxSignalDepth, int maxAdvancePerCall = DefaultMaxAdvancePerCall,
            int maxSettleRounds = DefaultMaxSettleRounds, bool requireViewPolicy = false, bool dropRemoved = false)
        {
            var diagnostics = new List<Diagnostic>();
            World world;
            try
            {
                world = new World(loaded, stepBudget, requireSubmitPolicy, maxSignalDepth, maxAdvancePerCall, maxSettleRounds,
                    requireViewPolicy: requireViewPolicy);
            }
            catch (WorldException e)
            {
                diagnostics.Add(new Diagnostic(e.Code, Severity.Error, e.Message, null, SourceSpan.None, e.Hint));
                return new WorldLoadResult(null, diagnostics);
            }
            try
            {
                var root = AsObject(CanonicalJson.Read(save), "the save");
                world.Restore(root, diagnostics, dropRemoved);
            }
            catch (JsonFormatException e)
            {
                diagnostics.Add(new Diagnostic(Codes.SaveCorrupt, Severity.Error, e.Message, null, SourceSpan.None));
            }
            foreach (var d in diagnostics)
                if (d.Severity == Severity.Error) return new WorldLoadResult(null, diagnostics);
            return new WorldLoadResult(world, diagnostics);
        }

        private void Restore(JsonObject root, List<Diagnostic> diagnostics, bool dropRemoved)
        {
            long format = Long(Field(root, "format"), "format");
            if (format != SaveFormatVersion)
                throw new JsonFormatException("the save format is " + format.ToString(CultureInfo.InvariantCulture) +
                                              ", but this engine reads format " + SaveFormatVersion.ToString(CultureInfo.InvariantCulture));
            CompareMods(AsArray(Field(root, "mods"), "mods"), diagnostics);

            _tick = Long(Field(root, "tick"), "tick");
            if (_tick < 0) throw new JsonFormatException("tick should not be negative");
            _seed = Long(Field(root, "seed"), "seed");
            var counters = AsObject(Field(root, "counters"), "counters");
            _nextId = Positive(Field(counters, "entity"), "counters.entity");
            _nextContribution = Positive(Field(counters, "contribution"), "counters.contribution");
            _nextFlow = Positive(Field(counters, "flow"), "counters.flow");
            _nextRequest = Positive(Field(counters, "request"), "counters.request");

            var problems = new LoadProblems(diagnostics, dropRemoved);
            var entities = AsArray(Field(root, "entities"), "entities");

            // 1차: 엔티티를 모두 만든다 (값과 줄이 다른 엔티티를 가리킬 수 있다)
            var records = new List<KeyValuePair<EntityRecord, JsonObject>>();
            long last = 0;
            foreach (var node in entities.Items)
            {
                var obj = AsObject(node, "an entity");
                long id = Long(Field(obj, "id"), "an entity id");
                if (id <= last || id >= _nextId)
                    throw new JsonFormatException("entity ids must be increasing and below counters.entity (found #" +
                                                  id.ToString(CultureInfo.InvariantCulture) + ")");
                last = id;
                string defId = Text(Field(obj, "def"), "an entity Def");
                var where = new EntityId(id) + " (" + defId + ")";
                if (!_defs.TryGet(defId, out var def))
                {
                    // 버리면 불러올 때 파괴된 것처럼 다룬다: 남의 Ref 칸은 죽은 id로 남고 키 줄·기여 행·대상 Flow는 함께 버린다 (G211)
                    problems.Removed(where + ": the Def '" + defId + "' no longer exists");
                    problems.Skipped.Add(id);
                    continue;
                }
                if (def.Type.IsDataOnly)
                {
                    problems.Add(where + ": '" + defId + "' is now a " + def.Type.Name + ", a data-only Def type");
                    problems.Skipped.Add(id);
                    continue;
                }
                var entity = new EntityRecord(id, def);
                _entities.Add(id, entity);
                if (!_byType.TryGetValue(def.Type, out var set)) _byType.Add(def.Type, set = new SortedSet<long>());
                set.Add(id);
                records.Add(new KeyValuePair<EntityRecord, JsonObject>(entity, obj));
            }

            // 2차: 컴포넌트·값·줄
            foreach (var pair in records) RestoreEntity(pair.Key, pair.Value, problems);
            if (problems.Dropping)
                foreach (var pair in records) CheckRequiredRefs(pair.Key, problems);

            foreach (var node in AsArray(Field(root, "contributions"), "contributions").Items)
                RestoreContribution(AsObject(node, "a contribution"), problems);

            RestoreFlows(AsArray(Field(root, "flows"), "flows"), problems);
            RestoreWhens(AsArray(Field(root, "when"), "when"));
            RestoreCooldowns(AsArray(Field(root, "when_cooldown"), "when_cooldown"));
            RestoreOnce(AsArray(Field(root, "when_once"), "when_once"));
            RestoreRandCounts(AsArray(Field(root, "rand"), "rand"));

            // 색인은 저장하지 않고 다시 만든다 (기록 없이: 커맨드 밖이다)
            foreach (var pair in records)
            {
                var entity = pair.Key;
                foreach (var component in ComponentsOf(entity)) IndexRefs(entity, component);
                foreach (var rows in entity.Rows.Values)
                    foreach (var key in rows.ByEntity.Keys)
                    {
                        if (!_keyHolders.TryGetValue(key, out var holders)) _keyHolders.Add(key, holders = new SortedSet<long>());
                        holders.Add(entity.Id);
                    }
            }
        }

        private void CompareMods(JsonArray saved, List<Diagnostic> diagnostics)
        {
            var before = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            foreach (var node in saved.Items)
            {
                var obj = AsObject(node, "a mod");
                var version = Field(obj, "version");
                before[Text(Field(obj, "id"), "a mod id")] = version is JsonNull ? null : Text(version, "a mod version");
            }
            var now = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            if (_mods != null) foreach (var m in _mods) now[m.Id] = m.Version;

            var changes = new List<string>();
            foreach (var pair in before)
            {
                if (!now.TryGetValue(pair.Key, out var version)) changes.Add("removed '" + pair.Key + "'");
                else if (version != pair.Value)
                    changes.Add("'" + pair.Key + "' " + (pair.Value ?? "(no version)") + " -> " + (version ?? "(no version)"));
            }
            foreach (var pair in now)
                if (!before.ContainsKey(pair.Key)) changes.Add("added '" + pair.Key + "'");
            if (changes.Count > 0)
                diagnostics.Add(new Diagnostic(Codes.SaveModsChanged, Severity.Warning,
                    "the mods differ from the save: " + string.Join(", ", changes), null, SourceSpan.None));
        }

        private void RestoreEntity(EntityRecord entity, JsonObject obj, LoadProblems problems)
        {
            string where = new EntityId(entity.Id) + " (" + entity.Def.Id + ")";
            foreach (var node in AsArray(Field(obj, "runtime"), "runtime components").Items)
            {
                string name = Text(node, "a runtime component");
                if (!_schema.TryGetComponent(name, out var component))
                    problems.Removed(where + ": the component '" + name + "' no longer exists");
                else if (entity.Def.Components.Contains(name))
                    problems.Add(where + ": '" + name + "' was attached at runtime, but its Def now has it");
                else entity.Runtime.Add(name, component);
            }

            foreach (var pair in AsObject(Field(obj, "values"), "entity values").Members)
            {
                var component = ComponentOf(entity, pair.Key, where, problems);
                if (component == null) continue;
                if (component.IsKeyed)
                {
                    problems.Add(where + ": '" + component.Name + "' is now a keyed component");
                    continue;
                }
                RestoreValues(component, AsObject(pair.Value, "values"), entity.Values, where + " " + component.Name, problems);
            }

            foreach (var pair in AsObject(Field(obj, "rows"), "entity rows").Members)
            {
                var component = ComponentOf(entity, pair.Key, where, problems);
                if (component == null) continue;
                if (!component.IsKeyed)
                {
                    problems.Add(where + ": '" + component.Name + "' is no longer a keyed component");
                    continue;
                }
                var rows = entity.RowsOf(component);
                foreach (var node in AsArray(pair.Value, "rows").Items)
                {
                    var row = AsObject(node, "a row");
                    var key = RestoreKey(Field(row, "key"), component, where + " " + component.Name, problems);
                    if (key == null) continue;
                    var record = new RowRecord(Bool(Field(row, "attached"), "attached"));
                    RestoreValues(component, AsObject(Field(row, "values"), "row values"), record.Values,
                        where + " " + component.Name + "[" + key.Value + "]", problems);
                    rows.Put(key.Value, record);
                }
            }
            CheckFilled(entity, where, problems);
        }

        /// <summary>
        /// 저장 뒤에 생긴 멤버(또는 Def에 새로 붙은 컴포넌트)는 정적인 값(Def 값, 기본값)으로만 채운다 (S8 "칼럼 추가는 기본값으로 채움").
        /// 정적인 값이 없으면 읽을 때 터지므로 여기서 거부한다. 함수 기본값과 <c>default_from</c>은 아직 불러올 때 계산하지 않는다.
        /// </summary>
        private void CheckFilled(EntityRecord entity, string where, LoadProblems problems)
        {
            foreach (var component in ComponentsOf(entity))
            {
                string at = where + " " + component.Name;
                var defComponent = entity.Def.Components.TryGet(component.Name, out var rc) ? rc : null;
                if (!component.IsKeyed)
                {
                    CheckFilled(component, defComponent?.Members, entity.Values, at, problems);
                    continue;
                }
                entity.Rows.TryGetValue(component.Name, out var rows);
                if (component.KeyKind == KeyKind.Def && component.Keys != KeysMode.All && defComponent != null)
                    foreach (var key in defComponent.Rows.Keys)
                    {
                        defComponent.Rows.TryGet(key, out var defRow);
                        RowRecord? record = null;
                        rows?.ByDef.TryGetValue(key, out record);
                        CheckFilled(component, defRow.Members, record?.Values ?? NoValues, at + "['" + key + "']", problems);
                    }
                if (rows == null) continue;
                foreach (var pair in rows.ByDef)
                    if (pair.Value.Attached) CheckFilled(component, null, pair.Value.Values, at + "['" + pair.Key + "']", problems);
                foreach (var pair in rows.ByEntity)
                    CheckFilled(component, null, pair.Value.Values, at + "[" + new EntityId(pair.Key) + "]", problems);
                foreach (var pair in rows.ByInt)
                    CheckFilled(component, null, pair.Value.Values, at + "[" + pair.Key.ToString(CultureInfo.InvariantCulture) + "]", problems);
            }
        }

        private static void CheckFilled(ComponentType component, OrderedMap<ResolvedValue>? defMembers,
            Dictionary<MemberDecl, Value> values, string where, LoadProblems problems)
        {
            foreach (var member in component.Members)
            {
                var spec = member.Spec;
                if (member.Kind == MemberKind.Derived) continue;
                if (member.Kind != MemberKind.Const && values.ContainsKey(member)) continue;
                if (defMembers != null && defMembers.Contains(member.Name)) continue;
                if (spec.Default != null && !(spec.Default is FunctionValue)) continue;
                if (member.Kind == MemberKind.Const && (spec.Type is ListTypeRef || spec.Type is DictTypeRef)) continue;
                if (member.Kind == MemberKind.Ref && spec.Optional) continue;
                problems.Add(where + "." + member.Name + ": the member is new and has no default to fill the saved entity with");
            }
        }

        /// <summary>
        /// 버린 엔티티를 가리키는 필수 Ref는 거부한다 (S8 "필수 Ref는 로드 오류", G211). 게임이 파괴해 생긴 죽은 Ref(G91)와 달리
        /// 모드는 이 엔티티가 사라질 줄 몰랐고, 그대로 두면 그 칸을 읽는 순간 오류가 난다. 선택적 Ref는 읽으면 None이다.
        /// </summary>
        private void CheckRequiredRefs(EntityRecord entity, LoadProblems problems)
        {
            string where = new EntityId(entity.Id) + " (" + entity.Def.Id + ")";
            foreach (var component in ComponentsOf(entity))
            {
                if (!component.IsKeyed)
                {
                    CheckRequiredRefs(component, entity.Values, where + " " + component.Name, problems);
                    continue;
                }
                if (!entity.Rows.TryGetValue(component.Name, out var rows)) continue;
                foreach (var pair in rows.ByDef)
                    CheckRequiredRefs(component, pair.Value.Values, where + " " + component.Name + "['" + pair.Key + "']", problems);
                foreach (var pair in rows.ByEntity)
                    CheckRequiredRefs(component, pair.Value.Values, where + " " + component.Name + "[" + new EntityId(pair.Key) + "]", problems);
                foreach (var pair in rows.ByInt)
                    CheckRequiredRefs(component, pair.Value.Values,
                        where + " " + component.Name + "[" + pair.Key.ToString(CultureInfo.InvariantCulture) + "]", problems);
            }
        }

        private static void CheckRequiredRefs(ComponentType component, Dictionary<MemberDecl, Value> values, string where,
            LoadProblems problems)
        {
            foreach (var member in component.Members)
            {
                if (member.Kind != MemberKind.Ref || member.Spec.Optional) continue;
                if (values.TryGetValue(member, out var value) && value is EntityValue ev && problems.Skipped.Contains(ev.Id.Value))
                    problems.Add(where + "." + member.Name + ": the required ref points at " + ev.Id +
                                 ", whose Def no longer exists");
            }
        }

        private ComponentType? ComponentOf(EntityRecord entity, string name, string where, LoadProblems problems)
        {
            if (!_schema.TryGetComponent(name, out var component))
            {
                problems.Removed(where + ": the component '" + name + "' no longer exists");
                return null;
            }
            if (!Has(entity, component))
            {
                // 런타임 목록은 값보다 먼저 되살리므로, 여기 오는 것은 Def 목록에서 빠진 컴포넌트다
                problems.Removed(where + ": the entity no longer has '" + name + "'");
                return null;
            }
            return component;
        }

        private void RestoreValues(ComponentType component, JsonObject saved, Dictionary<MemberDecl, Value> target, string where,
            LoadProblems problems)
        {
            foreach (var pair in saved.Members)
            {
                string display = where + "." + pair.Key;
                if (!component.TryGetMember(pair.Key, out var member))
                {
                    problems.Removed(display + ": the member no longer exists");
                    continue;
                }
                if (member.Kind == MemberKind.Const || member.Kind == MemberKind.Derived)
                {
                    problems.Add(display + ": the member is now " + (member.Kind == MemberKind.Const ? "a const" : "derived") +
                                 ", so it holds no saved value");
                    continue;
                }
                var value = LoadValue(pair.Value);
                string? problem = StoredValueProblem(member, value);
                if (problem != null)
                {
                    problems.Add(display + ": " + problem);
                    continue;
                }
                target[member] = member.Kind == MemberKind.Ref ? value : Normalize(member.Spec.Type, value);
            }
        }

        /// <summary>
        /// 저장된 값이 지금 멤버 선언에 맞는가. 범위(min·max)도 다시 본다: 범위는 누가 쓰든 지키는 불변식이고(G92),
        /// 모드가 범위를 좁히면 저장된 값을 조용히 받거나 자르지 않는다.
        /// </summary>
        private string? StoredValueProblem(MemberDecl member, Value value)
        {
            var spec = member.Spec;
            if (member.Kind == MemberKind.Ref)
            {
                if (value is NoneValue) return spec.Optional ? null : "the ref is now required, but the save has None";
                if (!(value is EntityValue ev)) return "a ref takes an entity, but the save has " + value.KindName;
                // 죽은 엔티티를 가리키는 칸은 그대로 둔다 (읽을 때 처리, G91)
                string expected = ((EntityRefTypeRef)spec.Type).Target.Name;
                if (_entities.TryGetValue(ev.Id.Value, out var target) && target.Def.Type.Name != expected)
                    return "the ref now refers to " + expected + ", but " + ev.Id + " is a " + target.Def.Type.Name;
                return null;
            }
            string? mismatch = PatchOperationCheck.Mismatch(spec.Type, value);
            if (mismatch != null) return mismatch;
            if (spec.Type is DefIdTypeRef defIdType)
            {
                string id = ((StrValue)value).Value;
                if (!_defs.TryGet(id, out var def) || def.Type.Name != defIdType.Target.Name)
                    return "there is no " + defIdType.Target.Name + " '" + id + "' any more";
            }
            var normalized = Normalize(spec.Type, value);
            if (spec.Min != null && CompareValues(normalized, spec.Min) < 0)
                return Show(normalized) + " is below min " + Show(spec.Min);
            if (spec.Max != null && CompareValues(normalized, spec.Max) > 0)
                return Show(normalized) + " is above max " + Show(spec.Max);
            return null;
        }

        private RowKey? RestoreKey(JsonNode node, ComponentType component, string where, LoadProblems problems)
        {
            switch (component.KeyKind)
            {
                case KeyKind.Def:
                    string id = Text(node, "a row key");
                    if (!_defs.TryGet(id, out var def))
                    {
                        problems.Removed(where + ": there is no " + component.Key!.Name + " '" + id + "' to key a row by any more");
                        return null;
                    }
                    if (def.Type != component.Key)
                    {
                        problems.Add(where + ": there is no " + component.Key!.Name + " '" + id + "' to key a row by any more");
                        return null;
                    }
                    return RowKey.Def(id);
                case KeyKind.Entity:
                    long entity = EntityIdOf(node, "a row key");
                    if (problems.Skipped.Contains(entity))
                    {
                        // 거부할 때는 그 엔티티가 왜 없는지 이미 적었다. 버릴 때는 함께 버린 줄로 적는다
                        problems.DroppedWith(where + ": the row keyed by " + new EntityId(entity) + " went with its entity");
                        return null;
                    }
                    if (!_entities.TryGetValue(entity, out var keyEntity))
                        throw new JsonFormatException("a row is keyed by #" + entity.ToString(CultureInfo.InvariantCulture) +
                                                      ", which is not in the save");
                    if (keyEntity.Def.Type != component.Key)
                    {
                        problems.Add(where + ": the key " + new EntityId(entity) + " is a " + keyEntity.Def.Type.Name +
                                     ", but rows are now keyed by " + component.Key!.Name);
                        return null;
                    }
                    return RowKey.Entity(new EntityId(entity));
                default:
                    if (!(node is JsonNumber n)) throw new JsonFormatException("an int row key should be a number");
                    return RowKey.Int(n.Value);
            }
        }

        private void RestoreContribution(JsonObject obj, LoadProblems problems)
        {
            long id = Long(Field(obj, "id"), "a contribution id");
            if (id < 1 || id >= _nextContribution || _contributions.ContainsKey(id))
                throw new JsonFormatException("contribution ids must be unique and below counters.contribution");
            long target = Long(Field(obj, "target"), "a contribution target");
            var sourceNode = Field(obj, "source");
            long? source = sourceNode is JsonNull ? (long?)null : Long(sourceNode, "a contribution source");
            if (problems.Skipped.Contains(target) || (source != null && problems.Skipped.Contains(source.Value)))
            {
                problems.DroppedWith("contribution " + id.ToString(CultureInfo.InvariantCulture) + " to " + new EntityId(target) +
                                     (source == null ? "" : " from " + new EntityId(source.Value)) + " went with its entity");
                return;
            }
            // 죽은 엔티티의 행은 함께 지워지므로 저장에 없다
            if (!_entities.TryGetValue(target, out var entity) || (source != null && !_entities.ContainsKey(source.Value)))
                throw new JsonFormatException("contribution " + id.ToString(CultureInfo.InvariantCulture) +
                                              " belongs to an entity that is not in the save");
            string where = "contribution " + id.ToString(CultureInfo.InvariantCulture) + " to " + new EntityId(target);
            var component = ComponentOf(entity, Text(Field(obj, "component"), "a contribution component"), where, problems);
            if (component == null) return;
            string memberName = Text(Field(obj, "member"), "a contribution member");
            if (!component.TryGetMember(memberName, out var member))
            {
                problems.Removed(where + ": '" + component.Name + "." + memberName + "' no longer exists");
                return;
            }
            if (member.Kind != MemberKind.Derived)
            {
                problems.Add(where + ": '" + component.Name + "." + memberName + "' is no longer a derived value");
                return;
            }
            var rowNode = Field(obj, "row");
            RowKey? row = null;
            if (!(rowNode is JsonNull))
            {
                if (!component.IsKeyed)
                {
                    problems.Add(where + ": '" + component.Name + "' is no longer a keyed component");
                    return;
                }
                row = RestoreKey(rowNode, component, where, problems);
                if (row == null) return;
            }
            else if (component.IsKeyed)
            {
                problems.Add(where + ": '" + component.Name + "' is now a keyed component");
                return;
            }
            var value = Fixed.FromRaw(Long(Field(obj, "value"), "a contribution value"));
            PutContribution(new Contribution(id, target, component, row, member, Text(Field(obj, "op"), "a contribution op"),
                value, source));
        }

        // ---- 값과 JSON 도우미 ----

        private static Value LoadValue(JsonNode node)
        {
            switch (node)
            {
                case JsonNull _: return NoneValue.Instance;
                case JsonBool b: return b.Value ? BoolValue.True : BoolValue.False;
                case JsonNumber n: return new IntValue(n.Value);
                case JsonString s: return new StrValue(s.Value);
                case JsonArray a:
                    var items = new List<Value>();
                    foreach (var item in a.Items) items.Add(LoadValue(item));
                    return new ListValue(items);
                case JsonObject o:
                    if (o.Members.Count == 1)
                    {
                        var tag = o.Members[0];
                        switch (tag.Key)
                        {
                            case "fixed": return new FixedValue(Fixed.FromRaw(Long(tag.Value, "a fixed value")));
                            case "entity": return new EntityValue(new EntityId(EntityIdOf(o, "an entity")));
                            case "dict":
                                var pairs = new List<DictItem>();
                                foreach (var p in AsArray(tag.Value, "a dict").Items)
                                {
                                    var kv = AsArray(p, "a dict item");
                                    if (kv.Items.Count != 2) throw new JsonFormatException("a dict item should be [key, value]");
                                    pairs.Add(new DictItem(LoadValue(kv.Items[0]), LoadValue(kv.Items[1]), SourceSpan.None));
                                }
                                return new DictValue(pairs);
                        }
                    }
                    else if (o.Members.Count == 2 && o.Members[0].Key == "text" && o.Members[1].Key == "args")
                    {
                        var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                        foreach (var arg in AsObject(o.Members[1].Value, "text arguments").Members) args[arg.Key] = LoadValue(arg.Value);
                        return new TextValue(Text(o.Members[0].Value, "a text key"), args);
                    }
                    throw new JsonFormatException("unknown value tag {" + string.Join(", ", KeysOf(o)) + "}");
            }
            throw new JsonFormatException("unknown value");
        }

        private static IEnumerable<string> KeysOf(JsonObject o)
        {
            foreach (var m in o.Members) yield return "\"" + m.Key + "\"";
        }

        /// <summary><c>{"entity": id}</c>의 id.</summary>
        private static long EntityIdOf(JsonNode node, string what)
        {
            var obj = AsObject(node, what);
            if (obj.Members.Count != 1 || obj.Members[0].Key != "entity")
                throw new JsonFormatException(what + " should be {\"entity\": id}");
            long id = Long(obj.Members[0].Value, what);
            if (id < 1) throw new JsonFormatException(what + " has a bad id");
            return id;
        }

        private static JsonNode Field(JsonObject obj, string name) =>
            obj.Get(name) ?? throw new JsonFormatException("the save is missing \"" + name + "\"");

        private static JsonObject AsObject(JsonNode node, string what) =>
            node as JsonObject ?? throw new JsonFormatException(what + " should be an object, not " + node.Kind);

        private static JsonArray AsArray(JsonNode node, string what) =>
            node as JsonArray ?? throw new JsonFormatException(what + " should be an array, not " + node.Kind);

        private static long Long(JsonNode node, string what) =>
            node is JsonNumber n ? n.Value : throw new JsonFormatException(what + " should be a number, not " + node.Kind);

        private static long Positive(JsonNode node, string what)
        {
            long n = Long(node, what);
            if (n < 1) throw new JsonFormatException(what + " should be at least 1");
            return n;
        }

        private static string Text(JsonNode node, string what) =>
            node is JsonString s ? s.Value : throw new JsonFormatException(what + " should be a string, not " + node.Kind);

        private static bool Bool(JsonNode node, string what) =>
            node is JsonBool b ? b.Value : throw new JsonFormatException(what + " should be true or false, not " + node.Kind);

        /// <summary>저장과 지금 내용이 어긋나는 곳들. 하나씩 진단이 된다(같은 곳은 한 번: 없어진 런타임 컴포넌트는 그 값에서도 걸린다).</summary>
        private sealed class LoadProblems
        {
            private readonly List<Diagnostic> _diagnostics;
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
            private readonly bool _dropRemoved;

            public LoadProblems(List<Diagnostic> diagnostics, bool dropRemoved)
            {
                _diagnostics = diagnostics;
                _dropRemoved = dropRemoved;
            }

            /// <summary>Def가 없어져 만들지 못한 엔티티. 그것을 가리키는 줄·기여 행은 따로 보고하지 않는다(로드는 어차피 실패한다).</summary>
            public HashSet<long> Skipped { get; } = new HashSet<long>();

            /// <summary>지금 불러오는 Flow의 람다 목록 (값의 <c>{"closure": n}</c>이 가리킨다, G143).</summary>
            public IReadOnlyList<ClosureValue>? Closures { get; set; }

            public void Add(string message)
            {
                if (!_seen.Add(message)) return;
                _diagnostics.Add(new Diagnostic(Codes.SaveMismatch, Severity.Error, message, null, SourceSpan.None,
                    "the content changed in a way the save cannot follow (spec S8)"));
            }

            /// <summary>
            /// 저장된 값의 자리가 지워짐 (G210). 기본은 <see cref="Add"/>처럼 거부하고, 호스트가 <c>dropRemoved</c>를 켰으면
            /// 그 값을 버리고 경고만 남긴다(호출한 쪽은 어느 경우든 그 값을 되살리지 않는다).
            /// </summary>
            public void Removed(string message)
            {
                if (!_dropRemoved)
                {
                    Add(message);
                    return;
                }
                if (!_seen.Add(message)) return;
                _diagnostics.Add(new Diagnostic(Codes.SaveDropped, Severity.Warning, message + "; its saved value was dropped", null,
                    SourceSpan.None, "the host allowed dropping what the content removed (spec S8)"));
            }

            /// <summary>Def가 없어진 엔티티를 버리는 중인가 (G211). 거부할 때는 끌려 사라지는 것을 따로 적지 않는다.</summary>
            public bool Dropping => _dropRemoved && Skipped.Count > 0;

            /// <summary>버린 엔티티에 딸려 함께 버린 것 (G211). 거부할 때는 적지 않는다(로드는 어차피 실패하고 원인은 이미 적었다).</summary>
            public void DroppedWith(string message)
            {
                if (!_dropRemoved || !_seen.Add(message)) return;
                _diagnostics.Add(new Diagnostic(Codes.SaveDropped, Severity.Warning, message, null, SourceSpan.None,
                    "the host allowed dropping what the content removed (spec S8)"));
            }

            /// <summary>멈춘 Flow의 함수 본문이 저장 뒤에 바뀜 (C5, G144). 위치를 옮겨 보지 않고 거부한다.</summary>
            public void FunctionChanged(string message)
            {
                if (!_seen.Add(message)) return;
                _diagnostics.Add(new Diagnostic(Codes.SaveFunctionChanged, Severity.Error, message, null, SourceSpan.None,
                    "a paused flow resumes only in the exact body it paused in; finish the flow before updating the mod (spec S8)"));
            }
        }
    }

    /// <summary><see cref="World.Load"/>의 결과. 오류가 하나라도 있으면 <see cref="World"/>는 null이다.</summary>
    public sealed class WorldLoadResult
    {
        internal WorldLoadResult(World? world, IReadOnlyList<Diagnostic> diagnostics)
        {
            World = world;
            Diagnostics = diagnostics;
        }

        public World? World { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
        public bool Succeeded => World != null;
    }
}
