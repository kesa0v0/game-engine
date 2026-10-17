using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 엔티티와 그 칼럼을 담는 런타임 월드 (spec S4, spec-gaps G86). 동결된 Def 집합과 스키마로 만든다. 정적 기본 월드는 없다(P3).
    /// <list type="bullet">
    /// <item>읽기는 엔티티 값 → Def 값 → 스키마 기본값(Flyweight). 쓰면 값이 같아도 엔티티 값이 된다.</item>
    /// <item>구조 변경(생성·삭제·부착·줄)은 즉시 보이고, 순회는 시작 시점의 목록이다 (G88).</item>
    /// <item>id는 재사용하지 않는다 (G87). 실패한 호출은 월드를 바꾸지 않는다.</item>
    /// <item><c>derived</c>는 아직 계산하지 않는다 (G90).</item>
    /// </list>
    /// 내부 저장 방식은 약속이 아니다(S4): 지금은 엔티티마다 바뀐 값만 맵으로 둔다.
    /// </summary>
    public sealed partial class World
    {
        private readonly DefSet _defs;
        private readonly SchemaSet _schema;
        private long _nextId = 1;
        private readonly Dictionary<long, EntityRecord> _entities = new Dictionary<long, EntityRecord>();
        private readonly Dictionary<DefType, SortedSet<long>> _byType = new Dictionary<DefType, SortedSet<long>>();

        /// <summary>Ref 칼럼 → 대상 엔티티 → 그 대상을 가리키는 엔티티들 (역방향 컬렉션, spec S3).</summary>
        private readonly Dictionary<MemberDecl, Dictionary<long, SortedSet<long>>> _inverse =
            new Dictionary<MemberDecl, Dictionary<long, SortedSet<long>>>();

        /// <summary>키 엔티티 → 그 엔티티를 키로 한 줄을 가진 엔티티들. 키 엔티티가 죽으면 그 줄을 지운다 (spec S4).</summary>
        private readonly Dictionary<long, SortedSet<long>> _keyHolders = new Dictionary<long, SortedSet<long>>();

        private static readonly Dictionary<MemberDecl, Value> NoValues = new Dictionary<MemberDecl, Value>();

        public const int DefaultStepBudget = 100000;

        /// <param name="requireSubmitPolicy">
        /// 멀티 게임처럼 제출 정책 함수가 꼭 있어야 하는 호스트가 켠다. 켰는데 정책이 없으면 만들지 않는다 (spec-gaps G124).
        /// </param>
        /// <param name="maxSignalDepth">
        /// 신호 연쇄의 세대 상한 (spec-gaps G147·G150). 이보다 깊은 신호는 핸들러를 돌리지 않고 결과에 알린다.
        /// </param>
        /// <param name="maxAdvancePerCall">
        /// 제출·답·응답 한 번 안에서 스크립트 <c>advance()</c>로 진행할 수 있는 틱의 합 (spec-gaps G168). 넘기려는 Flow는 오류로 버린다.
        /// </param>
        /// <param name="maxSettleRounds">
        /// 정착 한 번의 회차 상한 (spec-gaps G171). 넘으면 남은 <c>when</c> 반응을 돌리지 않고 결과에 알린다.
        /// </param>
        /// <param name="seed">
        /// 월드 시드 (spec-gaps G180). 같은 시드에 같은 입력이면 같은 난수가 나온다. 새 게임마다 다르게 할지는 호스트가 정한다.
        /// 저장에 남고 불러오면 그대로 잇는다.
        /// </param>
        /// <param name="requireViewPolicy">
        /// 서버 권위 멀티 게임처럼 숨김이 꼭 지켜져야 하는 호스트가 켠다. 켰는데 보기 정책이나 입구 정책이 없으면 만들지 않는다
        /// (spec-gaps G203: "답할 수 있는 질문"을 입구 정책으로 정하기 때문에 둘 다 필요하다).
        /// </param>
        public World(ContentLoadResult loaded, int stepBudget = DefaultStepBudget, bool requireSubmitPolicy = false,
            int maxSignalDepth = DefaultMaxSignalDepth, int maxAdvancePerCall = DefaultMaxAdvancePerCall,
            int maxSettleRounds = DefaultMaxSettleRounds, long seed = 0, bool requireViewPolicy = false)
            : this(loaded.Frozen, loaded.Schema, loaded.Scopes, stepBudget, loaded.Commands, loaded.SubmitPolicy)
        {
            _viewPolicy = loaded.ViewPolicy;
            _mods = loaded.Mods;
            _contents = loaded.Contents;
            _hostFunctions = loaded.HostFunctions;
            if (maxSignalDepth < 1)
                throw new ArgumentOutOfRangeException(nameof(maxSignalDepth), maxSignalDepth, "the signal depth limit must be at least 1");
            _maxSignalDepth = maxSignalDepth;
            if (maxAdvancePerCall < 1)
                throw new ArgumentOutOfRangeException(nameof(maxAdvancePerCall), maxAdvancePerCall, "the advance limit must be at least 1");
            _maxAdvancePerCall = maxAdvancePerCall;
            if (maxSettleRounds < 1)
                throw new ArgumentOutOfRangeException(nameof(maxSettleRounds), maxSettleRounds, "the settle round limit must be at least 1");
            _maxSettleRounds = maxSettleRounds;
            _seed = seed;
            SetHandlers(loaded.Handlers);
            SetWhens(loaded.Whens);
            if (requireSubmitPolicy && loaded.SubmitPolicy == null)
                throw new WorldException(Codes.RunNoSubmitPolicy,
                    "the host requires a submit policy, but no active mod declares one",
                    "a mod must declare @submit_policy def may_act(issuer, target) (spec-gaps G124)");
            if (requireViewPolicy && (loaded.ViewPolicy == null || loaded.SubmitPolicy == null))
                throw new WorldException(Codes.RunNoViewPolicy,
                    loaded.ViewPolicy == null
                        ? "the host requires a view policy, but no active mod declares one"
                        : "the host requires a view policy, which also needs a submit policy, but no active mod declares one",
                    loaded.ViewPolicy == null
                        ? "a mod must declare @view_policy def may_see(issuer, entity, component) (spec-gaps G203)"
                        : "questions are shown to the connections that may answer them, which the submit policy decides; " +
                          "declare @submit_policy def may_act(issuer, target) (spec-gaps G203)");
        }

        public World(DefSet defs, SchemaSet schema, IReadOnlyDictionary<string, ModScope>? scopes = null,
            int stepBudget = DefaultStepBudget, IReadOnlyDictionary<string, CommandDecl>? commands = null,
            PolicyDecl? submitPolicy = null)
        {
            _defs = defs;
            _schema = schema;
            _scopes = scopes ?? new SortedDictionary<string, ModScope>(StringComparer.Ordinal);
            _stepBudget = stepBudget;
            _commands = commands ?? new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);
            _submitPolicy = submitPolicy;
        }

        // ---- 이름 풀기 ----

        /// <summary>
        /// <c>Member("Health", "current")</c>는 컴포넌트의 멤버, <c>Member("Unit", "cost")</c>는 Def 타입 바로 아래의 <c>const</c>다.
        /// 없는 이름은 "혹시 …?"와 함께 실패한다.
        /// </summary>
        public MemberHandle Member(string owner, string member)
        {
            if (_schema.TryGetComponent(owner, out var component))
            {
                if (component.TryGetMember(member, out var decl)) return new MemberHandle(component, null, decl);
                var names = new List<string>();
                foreach (var m in component.Members) names.Add(m.Name);
                throw Unknown("component '" + owner + "' has no member '" + member + "'", member, names);
            }
            if (_schema.TryGetType(owner, out var type))
            {
                foreach (var decl in type.Consts)
                    if (decl.Name == member) return new MemberHandle(null, type, decl);
                var names = new List<string>();
                foreach (var k in type.Consts) names.Add(k.Name);
                throw Unknown("Def type '" + owner + "' has no const '" + member + "'", member, names);
            }
            throw Unknown("there is no component or Def type '" + owner + "'", owner, OwnerNames());
        }

        // ---- 생성·삭제 ----

        /// <summary>
        /// Def 하나로 엔티티를 만든다. <paramref name="inits"/>는 Def가 가진(키 없는) 컴포넌트의 <c>field</c>·Ref 처음 값이다.
        /// 필수 Ref와 기본값 없는 <c>field</c>는 여기서 줘야 하고(G91·G93), <c>default_from</c>은 이때 계산해 저장한다 (G89).
        /// </summary>
        public EntityId Create(string defId, params Init[] inits)
        {
            NoHostWrites("Create");
            if (!_defs.TryGet(defId, out var def))
            {
                var ids = new List<string>();
                foreach (var d in _defs.Defs) ids.Add(d.Id);
                throw Unknown("there is no Def '" + defId + "' (abstract Defs cannot be created)", defId, ids);
            }
            if (def.Type.IsDataOnly)
                throw new WorldException(Codes.RunNotEntity,
                    "'" + defId + "' is a " + def.Type.Name + ", a data-only Def type with no components, so it does not become entities");

            var given = Given(inits, h => h.Component != null && !h.Component.IsKeyed && def.Components.Contains(h.Component.Name),
                "'" + defId + "' has no such component, or it is keyed (add rows with AddRow)");
            // 모든 검사를 마친 뒤에 월드를 바꾼다 (실패한 호출은 흔적이 없다)
            var planned = new Planned();
            var plannedRows = new EntityRecord(0, def);
            foreach (var name in def.Components.Keys)
            {
                def.Components.TryGet(name, out var rc);
                var component = rc.Component;
                if (!component.IsKeyed)
                {
                    PlanMembers(planned, component, rc.Members, given, "creating '" + defId + "'");
                    continue;
                }
                // Def에 적힌 listed 줄도 생성 순간이 이때다 (G89·G93)
                if (component.KeyKind != KeyKind.Def || component.Keys != KeysMode.Listed) continue;
                foreach (var key in rc.Rows.Keys)
                {
                    rc.Rows.TryGet(key, out var row);
                    var values = new Planned();
                    PlanMembers(values, component, row.Members, NoValues, "creating '" + defId + "', row '" + key + "'", RowKey.Def(key));
                    if (values.Count == 0 && values.Pending.Count == 0) continue;
                    var record = new RowRecord(attached: false);
                    values.CopyTo(record.Values);
                    plannedRows.RowsOf(component).ByDef.Add(key, record);
                    planned.Pending.AddRange(values.Pending);
                }
            }

            var id = _nextId++;
            var entity = new EntityRecord(id, def);
            planned.CopyTo(entity.Values);
            foreach (var pair in plannedRows.Rows) entity.Rows.Add(pair.Key, pair.Value);
            _entities.Add(id, entity);
            if (!_byType.TryGetValue(def.Type, out var set)) _byType.Add(def.Type, set = new SortedSet<long>());
            set.Add(id);
            Touch(id);
            TouchSources(entity); // 그 타입과 컴포넌트의 query 목록이 바뀐다 (G193)
            // 되감으면 엔티티도 id도 쓰지 않은 것이 된다 (G133). 색인은 아래에서 따로 기록된다
            Journal(() =>
            {
                _entities.Remove(id);
                set.Remove(id);
                _nextId = id;
            });
            foreach (var name in def.Components.Keys)
            {
                def.Components.TryGet(name, out var rc);
                IndexRefs(entity, rc.Component);
            }
            if (planned.Pending.Count == 0) return new EntityId(id);

            // 함수 기본값과 derived 원천의 default_from은 엔티티가 있어야 계산된다. 실패하면 만든 흔적을 모두 지운다.
            try
            {
                FinishPending(entity, planned.Pending);
            }
            catch
            {
                foreach (var component in ComponentsOf(entity)) UnindexRefs(entity, component);
                _entities.Remove(id);
                _byType[def.Type].Remove(id);
                _nextId = id;
                throw;
            }
            return new EntityId(id);
        }

        /// <summary>
        /// 엔티티를 지운다. 남의 Ref 칸은 그대로 두고 읽을 때 처리한다(G91). 이 엔티티를 키로 한 줄은 지운다(S4).
        /// </summary>
        public void Destroy(EntityId id)
        {
            NoHostWrites("Destroy");
            var entity = Record(id);
            foreach (var component in ComponentsOf(entity)) UnindexRefs(entity, component);
            DropContributionsOf(id.Value);
            _destroyedSinceSweep = true; // 멈춘 Flow의 대상을 다음 확인 때 본다 (G164). 되감겨도 한 번 더 볼 뿐이다
            Touch(id.Value); // 이 엔티티를 읽거나 Ref로 가리킨 조건을 다시 본다
            TouchSources(entity);
            _entities.Remove(id.Value);
            var set = _byType[entity.Def.Type];
            set.Remove(id.Value);
            Journal(() =>
            {
                _entities.Add(id.Value, entity);
                set.Add(id.Value);
            });

            if (_keyHolders.TryGetValue(id.Value, out var holders))
            {
                foreach (var holder in holders)
                {
                    if (!_entities.TryGetValue(holder, out var h)) continue;
                    foreach (var rows in h.Rows.Values)
                    {
                        if (!rows.ByEntity.TryGetValue(id.Value, out var row)) continue;
                        Touch(holder);
                        rows.ByEntity.Remove(id.Value);
                        Journal(() => rows.ByEntity.Add(id.Value, row));
                    }
                }
                _keyHolders.Remove(id.Value);
                Journal(() => _keyHolders.Add(id.Value, holders));
            }
        }

        public bool IsAlive(EntityId id) => _entities.ContainsKey(id.Value);

        /// <summary>엔티티가 가리키는 Def의 id.</summary>
        public string DefOf(EntityId id) => Record(id).Def.Id;

        // ---- 읽기·쓰기 (키 없는 컴포넌트와 Def 타입의 const) ----

        public Value Get(EntityId id, MemberHandle member)
        {
            var entity = Record(id);
            if (member.Component == null)
            {
                if (entity.Def.Type != member.DefType)
                    throw new WorldException(Codes.RunNoComponent,
                        id + " is a " + entity.Def.Type.Name + ", not a " + member.DefType!.Name + " (reading " + member.Display + ")");
                entity.Def.Consts.TryGet(member.Member.Name, out var value);
                return Normalize(member.Member.Spec.Type, value.Value);
            }
            var component = NotKeyed(member);
            Require(entity, component);
            if (member.Kind == MemberKind.Derived) return ReadDerivedTop(entity, component, null, member.Member);
            return Read(member.Display, component, member.Member, DefMembers(entity, component), entity.Values);
        }

        /// <summary><c>field</c>·Ref에 쓴다. 타입과 범위가 맞아야 하고(G92), 쓴 값은 Def 값과 같아도 엔티티 값이 된다.</summary>
        public void Set(EntityId id, MemberHandle member, Value value)
        {
            NoHostWrites("Set");
            var entity = Record(id);
            if (member.Component == null)
                throw new WorldException(Codes.RunNotWritable, "'" + member.Display + "' is a const of the Def type and cannot be written");
            var component = NotKeyed(member);
            Require(entity, component);
            var stored = CheckWrite(member.Display, member.Member, value);
            if (member.Kind == MemberKind.Ref) Reindex(member.Member, id.Value, entity.Values, stored);
            Assign(id.Value, entity.Values, member.Member, stored);
        }

        // ---- 런타임 컴포넌트 ----

        /// <summary>
        /// Def에 없던 컴포넌트를 붙인다. 값은 스키마 기본값에서 오고, <c>const</c>는 기본값이 있어야 한다 (G93).
        /// 키 컴포넌트를 붙이면 줄은 없이 시작한다(<c>keys="all"</c>은 모든 키의 기본 줄이 보인다).
        /// </summary>
        public void Add(EntityId id, string componentName, params Init[] inits)
        {
            NoHostWrites("Add");
            var entity = Record(id);
            var component = ComponentNamed(componentName);
            if (Has(entity, component))
                throw new WorldException(Codes.RunNoComponent, id + " already has a '" + componentName + "'");
            var given = Given(inits, h => h.Component == component && !component.IsKeyed,
                "the value does not belong to '" + componentName + "' (rows are added with AddRow)");
            var values = new Planned();
            // 키 컴포넌트는 줄 없이 시작한다. 줄의 값은 AddRow가 생성 순간에 검사한다 (keys="all"은 G77·G93이 기본값을 보장)
            if (!component.IsKeyed) PlanMembers(values, component, null, given, "adding '" + componentName + "' to " + id);

            Touch(id.Value);
            TouchSource(component.Name);
            entity.Runtime.Add(component.Name, component);
            values.CopyTo(entity.Values);
            Journal(() =>
            {
                foreach (var member in component.Members) entity.Values.Remove(member);
                entity.Rows.Remove(component.Name);
                entity.Runtime.Remove(component.Name);
            });
            IndexRefs(entity, component);
            if (values.Pending.Count == 0) return;
            try
            {
                FinishPending(entity, values.Pending);
            }
            catch
            {
                UnindexRefs(entity, component);
                foreach (var member in component.Members) entity.Values.Remove(member);
                entity.Runtime.Remove(component.Name);
                throw;
            }
        }

        /// <summary>런타임에 붙인 컴포넌트를 뗀다. Def가 가진 컴포넌트는 뗄 수 없다 (spec S4).</summary>
        public void Remove(EntityId id, string componentName)
        {
            NoHostWrites("Remove");
            var entity = Record(id);
            var component = ComponentNamed(componentName);
            if (entity.Def.Components.Contains(componentName))
                throw new WorldException(Codes.RunNoComponent,
                    "'" + componentName + "' comes from " + id + "'s Def '" + entity.Def.Id + "' and cannot be removed");
            if (!entity.Runtime.ContainsKey(componentName))
                throw new WorldException(Codes.RunNoComponent, id + " has no '" + componentName + "'");
            UnindexRefs(entity, component);
            var oldValues = new List<KeyValuePair<MemberDecl, Value>>();
            foreach (var member in component.Members)
                if (entity.Values.TryGetValue(member, out var v)) oldValues.Add(new KeyValuePair<MemberDecl, Value>(member, v));
            entity.Rows.TryGetValue(componentName, out var oldRows);
            Touch(id.Value);
            TouchSource(componentName);
            foreach (var member in component.Members) entity.Values.Remove(member);
            entity.Rows.Remove(componentName);
            entity.Runtime.Remove(componentName);
            Journal(() =>
            {
                entity.Runtime.Add(componentName, component);
                if (oldRows != null) entity.Rows.Add(componentName, oldRows);
                foreach (var pair in oldValues) entity.Values[pair.Key] = pair.Value;
            });
            DropContributionsTo(id.Value, component);
        }

        public bool Has(EntityId id, string componentName) => Has(Record(id), ComponentNamed(componentName));

        // ---- 키 컴포넌트의 줄 ----

        public Value GetRow(EntityId id, MemberHandle member, string key) => GetRow(id, member, RowKey.Def(key));
        public Value GetRow(EntityId id, MemberHandle member, EntityId key) => GetRow(id, member, RowKey.Entity(key));
        public void SetRow(EntityId id, MemberHandle member, string key, Value value) => SetRow(id, member, RowKey.Def(key), value);
        public void SetRow(EntityId id, MemberHandle member, EntityId key, Value value) => SetRow(id, member, RowKey.Entity(key), value);
        public bool HasRow(EntityId id, string componentName, string key) => HasRow(id, componentName, RowKey.Def(key));
        public bool HasRow(EntityId id, string componentName, EntityId key) => HasRow(id, componentName, RowKey.Entity(key));

        // key=int 컴포넌트의 줄 (G129)
        public Value GetRow(EntityId id, MemberHandle member, long key) => GetRow(id, member, RowKey.Int(key));
        public void SetRow(EntityId id, MemberHandle member, long key, Value value) => SetRow(id, member, RowKey.Int(key), value);
        public bool HasRow(EntityId id, string componentName, long key) => HasRow(id, componentName, RowKey.Int(key));
        public void AddRow(EntityId id, string componentName, long key, params Init[] inits) =>
            AddRow(id, componentName, RowKey.Int(key), inits);
        public void RemoveRow(EntityId id, string componentName, long key) => RemoveRow(id, componentName, RowKey.Int(key));

        /// <summary><c>key=int</c> 컴포넌트의 줄 키들 (키 순, 시작 시점의 목록, G129).</summary>
        public IReadOnlyList<long> RowInts(EntityId id, string componentName)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), KeyKind.Int);
            Require(entity, component);
            var keys = new List<long>();
            if (entity.Rows.TryGetValue(componentName, out var rows)) keys.AddRange(rows.ByInt.Keys);
            return keys;
        }

        /// <summary><c>keys="listed"</c>나 엔티티 키 컴포넌트에 줄을 붙인다. 줄의 생성 순간이라 G89·G93이 적용된다.</summary>
        public void AddRow(EntityId id, string componentName, string key, params Init[] inits) =>
            AddRow(id, componentName, RowKey.Def(key), inits);

        public void AddRow(EntityId id, string componentName, EntityId key, params Init[] inits) =>
            AddRow(id, componentName, RowKey.Entity(key), inits);

        /// <summary>런타임에 붙인 줄을 뗀다. Def에서 온 줄은 뗄 수 없다 (spec S4).</summary>
        public void RemoveRow(EntityId id, string componentName, string key) => RemoveRow(id, componentName, RowKey.Def(key));
        public void RemoveRow(EntityId id, string componentName, EntityId key) => RemoveRow(id, componentName, RowKey.Entity(key));

        /// <summary>Def 키 컴포넌트의 줄 키들(키 id 순, 시작 시점의 목록). <c>keys="all"</c>이면 키 타입의 모든 구체 Def.</summary>
        public IReadOnlyList<string> RowKeys(EntityId id, string componentName)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), KeyKind.Def);
            Require(entity, component);
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (component.Keys == KeysMode.All)
            {
                foreach (var d in _defs.OfType(component.Key!.Name)) keys.Add(d.Id);
            }
            else
            {
                if (entity.Def.Components.TryGet(componentName, out var rc)) foreach (var k in rc.Rows.Keys) keys.Add(k);
                if (entity.Rows.TryGetValue(componentName, out var rows))
                    foreach (var pair in rows.ByDef)
                        if (pair.Value.Attached) keys.Add(pair.Key);
            }
            return new List<string>(keys);
        }

        /// <summary>엔티티 키 컴포넌트의 줄 키들 (id 순, 시작 시점의 목록).</summary>
        public IReadOnlyList<EntityId> RowEntities(EntityId id, string componentName)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), KeyKind.Entity);
            Require(entity, component);
            var keys = new List<EntityId>();
            if (entity.Rows.TryGetValue(componentName, out var rows))
                foreach (var key in rows.ByEntity.Keys) keys.Add(new EntityId(key));
            return keys;
        }

        // ---- 순회 ----

        /// <summary>그 Def 타입의 살아 있는 엔티티 (id 순 = 생성 순, 시작 시점의 목록, G87·G88).</summary>
        public IReadOnlyList<EntityId> All(string typeName)
        {
            if (!_schema.TryGetType(typeName, out var type))
            {
                var names = new List<string>();
                foreach (var t in _schema.Types) names.Add(t.Name);
                throw Unknown("there is no Def type '" + typeName + "'", typeName, names);
            }
            var result = new List<EntityId>();
            if (_byType.TryGetValue(type, out var set))
                foreach (var id in set) result.Add(new EntityId(id));
            return result;
        }

        /// <summary>
        /// <paramref name="target"/>을 이 Ref 칼럼으로 가리키는 엔티티들 (역방향 컬렉션, spec S3). 순서는 id 순이고,
        /// 칼럼이 <c>order_by=</c>를 선언했으면 원소의 그 칼럼 순(동률은 id 순). 시작 시점의 목록이다.
        /// </summary>
        public IReadOnlyList<EntityId> Inverse(EntityId target, MemberHandle reference)
        {
            if (reference.Kind != MemberKind.Ref || reference.Component == null)
                throw new WorldException(Codes.RunType, "'" + reference.Display + "' is not a ref column");
            if (reference.Component.IsKeyed)
                throw new WorldException(Codes.RunType,
                    "inverse collections of refs inside keyed rows are not supported yet ('" + reference.Display + "')");
            var entity = Record(target);
            string expected = ((EntityRefTypeRef)reference.Member.Spec.Type).Target.Name;
            if (entity.Def.Type.Name != expected)
                throw new WorldException(Codes.RunType,
                    target + " is a " + entity.Def.Type.Name + ", but '" + reference.Display + "' refers to " + expected);

            var result = new List<EntityId>();
            if (_inverse.TryGetValue(reference.Member, out var byTarget) && byTarget.TryGetValue(target.Value, out var sources))
                foreach (var s in sources) result.Add(new EntityId(s));

            if (reference.Member.Spec.OrderBy is NameRef orderBy)
            {
                var order = Member(reference.Component.Name, orderBy.Name);
                var keyed = new List<KeyValuePair<Value, EntityId>>();
                foreach (var e in result) keyed.Add(new KeyValuePair<Value, EntityId>(Get(e, order), e));
                keyed.Sort((a, b) =>
                {
                    int c = CompareValues(a.Key, b.Key);
                    return c != 0 ? c : a.Value.CompareTo(b.Value);
                });
                result.Clear();
                foreach (var pair in keyed) result.Add(pair.Value);
            }
            return result;
        }

        // ---- 줄 구현 ----

        private Value GetRow(EntityId id, MemberHandle member, RowKey key)
        {
            var entity = Record(id);
            var component = KeyedMember(member);
            var row = FindRow(entity, component, key, out var defRow, out var record);
            if (!row) throw NoRow(id, component, key);
            if (member.Kind == MemberKind.Derived) return ReadDerivedTop(entity, component, key, member.Member);
            return Read(member.Display + "[" + key + "]", component, member.Member, defRow?.Members,
                record?.Values ?? NoValues);
        }

        private void SetRow(EntityId id, MemberHandle member, RowKey key, Value value)
        {
            NoHostWrites("SetRow");
            var entity = Record(id);
            var component = KeyedMember(member);
            if (!FindRow(entity, component, key, out _, out var record)) throw NoRow(id, component, key);
            var stored = CheckWrite(member.Display, member.Member, value);
            if (record == null)
            {
                // keys="all"의 안 적힌 줄: 처음 쓸 때 기록을 만든다. 되감으면 기록이 사라져 다시 기본값 줄이 된다
                record = new RowRecord(attached: false);
                var rows = entity.RowsOf(component);
                rows.Put(key, record);
                Journal(() => rows.Remove(key));
            }
            Assign(id.Value, record.Values, member.Member, stored);
        }

        private bool HasRow(EntityId id, string componentName, RowKey key)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), key.Kind);
            return Has(entity, component) && FindRow(entity, component, key, out _, out _);
        }

        private void AddRow(EntityId id, string componentName, RowKey key, Init[] inits)
        {
            NoHostWrites("AddRow");
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), key.Kind);
            if (component.KeyKind == KeyKind.Def && component.Keys == KeysMode.All)
                throw new WorldException(Codes.RunNoRow,
                    "'" + componentName + "' is keys=\"all\": every key already has a row, so rows are not added");
            Require(entity, component);
            if (FindRow(entity, component, key, out _, out _))
                throw new WorldException(Codes.RunNoRow, id + " already has a '" + componentName + "' row for " + key);

            var given = Given(inits, h => h.Component == component, "the value does not belong to '" + componentName + "'");
            var values = new Planned();
            PlanMembers(values, component, null, given, "adding the '" + componentName + "' row " + key + " to " + id, key);

            var record = new RowRecord(attached: true);
            values.CopyTo(record.Values);
            var keyedRows = entity.RowsOf(component);
            Touch(id.Value);
            keyedRows.Put(key, record);
            Journal(() => keyedRows.Remove(key));
            if (key.IsEntity)
            {
                if (!_keyHolders.TryGetValue(key.EntityKey, out var holders))
                    _keyHolders.Add(key.EntityKey, holders = new SortedSet<long>());
                if (holders.Add(id.Value)) Journal(() => holders.Remove(id.Value));
            }
            if (values.Pending.Count == 0) return;
            try
            {
                FinishPending(entity, values.Pending);
            }
            catch
            {
                entity.Rows[componentName].Remove(key);
                if (key.IsEntity && _keyHolders.TryGetValue(key.EntityKey, out var held))
                {
                    held.Remove(id.Value);
                    if (held.Count == 0) _keyHolders.Remove(key.EntityKey);
                }
                throw;
            }
        }

        private void RemoveRow(EntityId id, string componentName, RowKey key)
        {
            NoHostWrites("RemoveRow");
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), key.Kind);
            if (component.KeyKind == KeyKind.Def && component.Keys == KeysMode.All)
                throw new WorldException(Codes.RunNoRow,
                    "'" + componentName + "' is keys=\"all\": every key always has a row, so rows are not removed");
            Require(entity, component);
            if (!FindRow(entity, component, key, out _, out var record)) throw NoRow(id, component, key);
            if (record == null || !record.Attached)
                throw new WorldException(Codes.RunNoRow,
                    "the '" + componentName + "' row " + key + " of " + id + " comes from its Def and cannot be removed");
            var keyedRows = entity.Rows[componentName];
            Touch(id.Value);
            keyedRows.Remove(key);
            Journal(() => keyedRows.Put(key, record));
            DropContributionsTo(id.Value, component, key);
        }

        /// <summary>
        /// 줄이 있는가. 있으면 Def 쪽 줄(<c>keys="all"</c>의 기본 줄 포함)과 엔티티 쪽 기록을 돌려준다.
        /// 키가 맞지 않는 종류·타입이면 오류다.
        /// </summary>
        private bool FindRow(EntityRecord entity, ComponentType component, RowKey key, out ResolvedRow? defRow,
            out RowRecord? record)
        {
            defRow = null;
            record = null;
            Require(entity, component);
            CheckKey(component, key);
            if (entity.Rows.TryGetValue(component.Name, out var rows)) record = rows.Find(key);
            if (key.Kind != KeyKind.Def) return record != null && record.Attached; // 엔티티·int 키 줄은 런타임에만 있다

            if (entity.Def.Components.Contains(component.Name) && entity.Def.TryGetRow(component.Name, key.DefKey!, out var r))
            {
                defRow = r;
                return true;
            }
            if (component.Keys == KeysMode.All) return true; // 런타임에 붙인 keys="all": 기본 줄
            return record != null && record.Attached;
        }

        private void CheckKey(ComponentType component, RowKey key)
        {
            if (component.KeyKind != key.Kind) throw WrongKey(component, key.Kind);
            if (key.IsInt) return;
            if (key.IsEntity)
            {
                if (!_entities.TryGetValue(key.EntityKey, out var keyEntity))
                    throw new WorldException(Codes.RunDeadEntity, "the key " + key + " does not exist");
                if (keyEntity.Def.Type != component.Key)
                    throw new WorldException(Codes.RunType,
                        "the key " + key + " is a " + keyEntity.Def.Type.Name + ", but '" + component.Name + "' is keyed by " + component.Key!.Name);
                return;
            }
            if (!_defs.TryGet(key.DefKey!, out var keyDef) || keyDef.Type != component.Key)
            {
                var ids = new List<string>();
                foreach (var d in _defs.OfType(component.Key!.Name)) ids.Add(d.Id);
                throw Unknown("there is no " + component.Key!.Name + " '" + key.DefKey + "' to key '" + component.Name + "' by",
                    key.DefKey!, ids);
            }
        }

        private static WorldException NoRow(EntityId id, ComponentType component, RowKey key) =>
            new WorldException(Codes.RunNoRow, id + " has no '" + component.Name + "' row for " + key,
                "add it with AddRow (keys=\"listed\" rows exist only when added)");

        // ---- 값 계획과 검사 ----

        /// <summary>
        /// 생성 순간(G89·G93)에 한 컴포넌트(또는 줄)의 값을 정한다. 엔티티에 저장할 값만 <paramref name="target"/>에 넣는다:
        /// 준 처음 값과 <c>default_from</c>으로 계산한 값. 어느 길로도 값이 정해지지 않는 칸이 있으면 실패한다.
        /// </summary>
        private void PlanMembers(Planned target, ComponentType component,
            OrderedMap<ResolvedValue>? defMembers, Dictionary<MemberDecl, Value> given, string where, RowKey? row = null)
        {
            foreach (var member in component.Members)
            {
                var spec = member.Spec;
                switch (member.Kind)
                {
                    case MemberKind.Derived:
                        continue;
                    case MemberKind.Const:
                        if (defMembers == null || !defMembers.Contains(member.Name)) RequireConstDefault(component, member, where);
                        continue;
                }

                if (given.TryGetValue(member, out var value))
                {
                    target.Add(member, value);
                    continue;
                }
                if (defMembers != null && defMembers.Contains(member.Name)) continue;
                if (spec.Default is FunctionValue fn)
                {
                    // 엔티티가 만들어진 뒤에 계산한다 (FinishPending)
                    target.Pending.Add(new PendingDefault(component, row, member, fn, null, where));
                    continue;
                }
                if (spec.Default != null) continue;
                if (spec.DefaultFrom is NameRef from)
                {
                    component.TryGetMember(from.Name, out var sourceMember);
                    if (sourceMember.Kind == MemberKind.Derived)
                    {
                        target.Pending.Add(new PendingDefault(component, row, member, null, sourceMember, where));
                        continue;
                    }
                    var source = SourceValue(component, from.Name, defMembers, given, where);
                    target.Add(member, CheckWrite(component.Name + "." + member.Name, member, source));
                    continue;
                }
                if (member.Kind == MemberKind.Ref && spec.Optional) continue;
                throw new WorldException(Codes.RunMissingValue,
                    "'" + component.Name + "." + member.Name + "' has no value (" + where + ")",
                    member.Kind == MemberKind.Ref
                        ? "a required ref must be given when the entity is created (spec-gaps G91)"
                        : "give it as an initial value, or declare a default (spec-gaps G93)");
            }
        }

        /// <summary><c>default_from</c>이 가리키는 같은 컴포넌트의 값 (생성 순간 기준).</summary>
        private Value SourceValue(ComponentType component, string name, OrderedMap<ResolvedValue>? defMembers,
            Dictionary<MemberDecl, Value> given, string where)
        {
            component.TryGetMember(name, out var source);
            if (given.TryGetValue(source, out var v)) return v;
            if (defMembers != null && defMembers.TryGet(name, out var d)) return d.Value;
            if (source.Spec.Default != null && !(source.Spec.Default is FunctionValue)) return source.Spec.Default;
            throw new WorldException(Codes.RunMissingValue,
                "'" + component.Name + "." + name + "' has no value to start '" + component.Name + "' from (" + where + ")");
        }

        private static void RequireConstDefault(ComponentType component, MemberDecl member, string where)
        {
            if (member.Kind != MemberKind.Const || member.Spec.Default != null) return;
            if (member.Spec.Type is ListTypeRef || member.Spec.Type is DictTypeRef) return; // 빈 컬렉션 (G67)
            throw new WorldException(Codes.RunMissingValue,
                "'" + component.Name + "." + member.Name + "' is a const without a default, so it has no value outside a Def (" + where + ")",
                "declare a default for it (spec-gaps G93)");
        }

        /// <summary>처음 값들을 검사해 멤버별로 모은다. 다른 컴포넌트의 값이나 같은 칸을 두 번 주면 오류.</summary>
        private Dictionary<MemberDecl, Value> Given(Init[] inits, Func<MemberHandle, bool> belongs, string notBelonging)
        {
            var given = new Dictionary<MemberDecl, Value>();
            foreach (var init in inits)
            {
                if (!belongs(init.Member))
                    throw new WorldException(Codes.RunNoComponent, "cannot give '" + init.Member.Display + "' here: " + notBelonging);
                if (given.ContainsKey(init.Member.Member))
                    throw new WorldException(Codes.RunType, "'" + init.Member.Display + "' is given twice");
                given.Add(init.Member.Member, CheckWrite(init.Member.Display, init.Member.Member, init.Value));
            }
            return given;
        }

        /// <summary>쓸 수 있는 칸인지, 값의 타입과 범위가 맞는지 (G92). 저장할 값(정규화된 값)을 돌려준다.</summary>
        private Value CheckWrite(string display, MemberDecl member, Value value)
        {
            var spec = member.Spec;
            switch (member.Kind)
            {
                case MemberKind.Const:
                    throw new WorldException(Codes.RunNotWritable, "'" + display + "' is a const value and cannot be written");
                case MemberKind.Derived:
                    throw new WorldException(Codes.RunNotWritable, "'" + display + "' is a derived value and cannot be written");
                case MemberKind.Ref:
                    string targetType = ((EntityRefTypeRef)spec.Type).Target.Name;
                    if (value is NoneValue)
                    {
                        if (spec.Optional) return value;
                        throw new WorldException(Codes.RunType, "'" + display + "' is a required ref and cannot be None");
                    }
                    if (!(value is EntityValue ev))
                        throw new WorldException(Codes.RunType, "'" + display + "' takes an entity, not " + value.KindName);
                    if (!_entities.TryGetValue(ev.Id.Value, out var target))
                        throw new WorldException(Codes.RunDeadRef, "'" + display + "' cannot refer to " + ev.Id + ", which does not exist");
                    if (target.Def.Type.Name != targetType)
                        throw new WorldException(Codes.RunType,
                            "'" + display + "' refers to " + targetType + ", but " + ev.Id + " is a " + target.Def.Type.Name);
                    return value;
            }

            string? problem = PatchOperationCheck.Mismatch(spec.Type, value);
            if (problem != null) throw new WorldException(Codes.RunType, "'" + display + "': " + problem);
            if (spec.Type is DefIdTypeRef defIdType)
            {
                string id = ((StrValue)value).Value;
                if (!_defs.TryGet(id, out var def) || def.Type.Name != defIdType.Target.Name)
                {
                    var ids = new List<string>();
                    foreach (var d in _defs.OfType(defIdType.Target.Name)) ids.Add(d.Id);
                    throw Unknown("'" + display + "': there is no " + defIdType.Target.Name + " '" + id + "'", id, ids);
                }
            }
            var normalized = Normalize(spec.Type, value);
            if (spec.Min != null && CompareValues(normalized, spec.Min) < 0)
                throw new WorldException(Codes.RunRange, "'" + display + "': " + Show(normalized) + " is below min " + Show(spec.Min),
                    "clamp the value explicitly (spec-gaps G92)");
            if (spec.Max != null && CompareValues(normalized, spec.Max) > 0)
                throw new WorldException(Codes.RunRange, "'" + display + "': " + Show(normalized) + " is above max " + Show(spec.Max),
                    "clamp the value explicitly (spec-gaps G92)");
            return normalized;
        }

        /// <summary>엔티티 값 → Def 값 → 스키마 기본값 (Flyweight, spec S4).</summary>
        private Value Read(string display, ComponentType component, MemberDecl member, OrderedMap<ResolvedValue>? defMembers,
            Dictionary<MemberDecl, Value> values)
        {
            var spec = member.Spec;
            if (member.Kind == MemberKind.Derived)
                throw new WorldException(Codes.RunNotComputed, "'" + display + "' is derived and cannot be computed yet (spec-gaps G90)");

            Value? value = null;
            if (member.Kind != MemberKind.Const && values.TryGetValue(member, out var own)) value = own;
            else if (defMembers != null && defMembers.TryGet(member.Name, out var fromDef)) value = fromDef.Value;
            else if (spec.Default != null && !(spec.Default is FunctionValue)) value = spec.Default;
            else if (member.Kind == MemberKind.Const && (spec.Type is ListTypeRef || spec.Type is DictTypeRef))
                value = spec.Type is ListTypeRef ? (Value)new ListValue(new List<Value>()) : new DictValue(new List<DictItem>());
            else if (member.Kind == MemberKind.Ref && spec.Optional) value = NoneValue.Instance;

            // 생성 순간의 검사(G93)가 구멍을 막으므로 여기 오면 월드의 버그다
            // (함수 기본값이 아직 계산되지 않은 칸을 읽은 경우도 여기다: 선언 순서상 뒤의 칸이다, G106)
            if (value == null)
                throw new WorldException(Codes.RunMissingValue, "'" + display + "' has no value yet",
                    "a default computed at creation can only read members declared before it (spec-gaps G106)");

            // Ref 칸은 대상이 살아 있는지에 따라 읽는 값이 달라진다(G91): when 조건이면 대상도 읽은 것이다
            if (member.Kind == MemberKind.Ref && value is EntityValue target) _whenReads?.Add(target.Id.Value);
            if (member.Kind == MemberKind.Ref && value is EntityValue ev && !_entities.ContainsKey(ev.Id.Value))
            {
                if (spec.Optional) return NoneValue.Instance;
                throw new WorldException(Codes.RunDeadRef, "'" + display + "' refers to " + ev.Id + ", which was destroyed",
                    "declare the ref optional=True if it may outlive its target (spec-gaps G91)");
            }
            return member.Kind == MemberKind.Ref ? value : Normalize(spec.Type, value);
        }

        /// <summary><c>fixed</c> 칸의 정수는 <c>fixed</c>로 바꾼다 (Def 리터럴은 정수로 적을 수 있다).</summary>
        private static Value Normalize(TypeRef type, Value value)
        {
            if (!ReferenceEquals(type, PrimitiveTypeRef.Fixed) || !(value is IntValue i)) return value;
            try
            {
                return new FixedValue(Fixed.FromRaw(checked(i.Value * Fixed.Scale)));
            }
            catch (OverflowException)
            {
                throw new WorldException(Codes.RunType, Show(value) + " is too large for a fixed value");
            }
        }

        private static BigInteger Scaled(Value value) =>
            value is IntValue i ? new BigInteger(i.Value) * Fixed.Scale : new BigInteger(((FixedValue)value).Value.Raw);

        /// <summary>숫자는 값으로, 문자열은 서수로, bool은 False 먼저 (order_by와 범위 검사).</summary>
        private static int CompareValues(Value a, Value b)
        {
            if ((a is IntValue || a is FixedValue) && (b is IntValue || b is FixedValue)) return Scaled(a).CompareTo(Scaled(b));
            if (a is StrValue sa && b is StrValue sb) return string.CompareOrdinal(sa.Value, sb.Value);
            if (a is BoolValue ba && b is BoolValue bb) return ba.Value.CompareTo(bb.Value);
            return 0;
        }

        internal static string Show(Value value) =>
            value is IntValue i ? i.Value.ToString(CultureInfo.InvariantCulture)
            : value is FixedValue f ? f.Value.ToString()
            : value.KindName;

        // ---- 역방향 색인 ----

        private void IndexRefs(EntityRecord entity, ComponentType component)
        {
            if (component.IsKeyed) return;
            foreach (var member in component.Members)
            {
                if (member.Kind != MemberKind.Ref) continue;
                if (entity.Values.TryGetValue(member, out var v) && v is EntityValue ev) AddInverse(member, ev.Id.Value, entity.Id);
            }
        }

        private void UnindexRefs(EntityRecord entity, ComponentType component)
        {
            if (component.IsKeyed) return;
            foreach (var member in component.Members)
            {
                if (member.Kind != MemberKind.Ref) continue;
                if (entity.Values.TryGetValue(member, out var v) && v is EntityValue ev) RemoveInverse(member, ev.Id.Value, entity.Id);
            }
        }

        private void Reindex(MemberDecl member, long source, Dictionary<MemberDecl, Value> values, Value next)
        {
            if (values.TryGetValue(member, out var old) && old is EntityValue o) RemoveInverse(member, o.Id.Value, source);
            if (next is EntityValue n) AddInverse(member, n.Id.Value, source);
        }

        private void AddInverse(MemberDecl member, long target, long source)
        {
            if (!_inverse.TryGetValue(member, out var byTarget))
                _inverse.Add(member, byTarget = new Dictionary<long, SortedSet<long>>());
            if (!byTarget.TryGetValue(target, out var sources)) byTarget.Add(target, sources = new SortedSet<long>());
            Touch(target); // 대상의 역방향 컬렉션이 바뀐다
            if (sources.Add(source)) Journal(() => sources.Remove(source));
        }

        private void RemoveInverse(MemberDecl member, long target, long source)
        {
            Touch(target);
            if (_inverse.TryGetValue(member, out var byTarget) && byTarget.TryGetValue(target, out var sources) &&
                sources.Remove(source))
                Journal(() => sources.Add(source));
        }

        // ---- 작은 도우미 ----

        private EntityRecord Record(EntityId id)
        {
            _whenReads?.Add(id.Value); // when 조건을 평가하는 중이면 이 엔티티를 읽었다 (S5)
            if (_entities.TryGetValue(id.Value, out var entity)) return entity;
            throw new WorldException(Codes.RunDeadEntity,
                id.Value < 1 || id.Value >= _nextId ? id + " was never created" : id + " was destroyed");
        }

        private ComponentType ComponentNamed(string name)
        {
            if (_schema.TryGetComponent(name, out var component)) return component;
            var names = new List<string>();
            foreach (var c in _schema.Components) names.Add(c.Name);
            throw Unknown("there is no component '" + name + "'", name, names);
        }

        private static bool Has(EntityRecord entity, ComponentType component) =>
            entity.Def.Components.Contains(component.Name) || entity.Runtime.ContainsKey(component.Name);

        private static void Require(EntityRecord entity, ComponentType component)
        {
            if (!Has(entity, component))
                throw new WorldException(Codes.RunNoComponent,
                    new EntityId(entity.Id) + " (" + entity.Def.Id + ") has no '" + component.Name + "'",
                    "check with Has, or add it with Add");
        }

        private static OrderedMap<ResolvedValue>? DefMembers(EntityRecord entity, ComponentType component) =>
            entity.Def.Components.TryGet(component.Name, out var rc) ? rc.Members : null;

        /// <summary>엔티티가 가진 컴포넌트: Def의 것(순서대로) 뒤에 런타임에 붙인 것(이름 순).</summary>
        private static List<ComponentType> ComponentsOf(EntityRecord entity)
        {
            var list = new List<ComponentType>();
            foreach (var name in entity.Def.Components.Keys)
            {
                entity.Def.Components.TryGet(name, out var rc);
                list.Add(rc.Component);
            }
            foreach (var c in entity.Runtime.Values) list.Add(c);
            return list;
        }

        private static ComponentType NotKeyed(MemberHandle member)
        {
            if (member.Component!.IsKeyed)
                throw new WorldException(Codes.RunNoRow,
                    "'" + member.Component.Name + "' is a keyed component; read and write its rows with GetRow/SetRow");
            return member.Component;
        }

        private static ComponentType KeyedMember(MemberHandle member)
        {
            if (member.Component == null || !member.Component.IsKeyed)
                throw new WorldException(Codes.RunNoRow, "'" + member.Display + "' is not in a keyed component; use Get/Set");
            return member.Component;
        }

        private static ComponentType Keyed(ComponentType component, KeyKind kind)
        {
            if (!component.IsKeyed)
                throw new WorldException(Codes.RunNoRow, "'" + component.Name + "' is not a keyed component");
            if (component.KeyKind != kind) throw WrongKey(component, kind);
            return component;
        }

        private static WorldException WrongKey(ComponentType component, KeyKind given)
        {
            static string Kind(ComponentType c, KeyKind k) =>
                k == KeyKind.Def ? (c.KeyKind == KeyKind.Def ? c.KeyDisplay + " Def ids" : "Def ids")
                : k == KeyKind.Entity ? (c.KeyKind == KeyKind.Entity ? c.KeyDisplay + " entities" : "entities")
                : "int";
            return new WorldException(Codes.RunType,
                "'" + component.Name + "' is keyed by " + Kind(component, component.KeyKind) + ", not " + Kind(component, given),
                component.KeyKind == KeyKind.Int ? "rows of an int-keyed component are read as " + component.Name + "[i]" : null);
        }

        private IEnumerable<string> OwnerNames()
        {
            foreach (var c in _schema.Components) yield return c.Name;
            foreach (var t in _schema.Types) yield return t.Name;
        }

        private static WorldException Unknown(string message, string input, IEnumerable<string> candidates) =>
            new WorldException(Codes.RunUnknownName, message, Suggest.Hint(input, candidates));

        // ---- 저장 구조 ----

        /// <summary>함수 기본값이나 <c>derived</c>를 원천으로 하는 <c>default_from</c>: 생성 순간에 엔티티를 만든 뒤 계산한다 (G89·G106).</summary>
        private sealed class PendingDefault
        {
            public PendingDefault(ComponentType component, RowKey? row, MemberDecl member, FunctionValue? function,
                MemberDecl? source, string where)
            {
                Component = component;
                Row = row;
                Member = member;
                Function = function;
                Source = source;
                Where = where;
            }

            public ComponentType Component { get; }
            public RowKey? Row { get; }
            public MemberDecl Member { get; }
            public FunctionValue? Function { get; }
            public MemberDecl? Source { get; }
            public string Where { get; }
        }

        /// <summary>생성 순간에 정한 값들 (멤버 선언 순서). 검사가 다 끝난 뒤에 월드로 옮긴다.</summary>
        private sealed class Planned
        {
            private readonly List<KeyValuePair<MemberDecl, Value>> _items = new List<KeyValuePair<MemberDecl, Value>>();

            public int Count => _items.Count;

            /// <summary>엔티티가 있어야 계산되는 기본값 (선언 순서).</summary>
            public List<PendingDefault> Pending { get; } = new List<PendingDefault>();

            public void Add(MemberDecl member, Value value) => _items.Add(new KeyValuePair<MemberDecl, Value>(member, value));

            public void CopyTo(Dictionary<MemberDecl, Value> target)
            {
                foreach (var pair in _items) target[pair.Key] = pair.Value;
            }
        }

        private sealed class EntityRecord
        {
            public EntityRecord(long id, FrozenDef def)
            {
                Id = id;
                Def = def;
            }

            public long Id { get; }
            public FrozenDef Def { get; }

            /// <summary>키 없는 컴포넌트의 엔티티 값 (쓴 값, 생성 순간에 계산한 값).</summary>
            public Dictionary<MemberDecl, Value> Values { get; } = new Dictionary<MemberDecl, Value>();

            /// <summary>런타임에 붙인 컴포넌트 (이름 순).</summary>
            public SortedDictionary<string, ComponentType> Runtime { get; } =
                new SortedDictionary<string, ComponentType>(StringComparer.Ordinal);

            /// <summary>키 컴포넌트별 줄 기록 (이름 순).</summary>
            public SortedDictionary<string, KeyedRows> Rows { get; } = new SortedDictionary<string, KeyedRows>(StringComparer.Ordinal);

            public KeyedRows RowsOf(ComponentType component)
            {
                if (!Rows.TryGetValue(component.Name, out var rows)) Rows.Add(component.Name, rows = new KeyedRows());
                return rows;
            }
        }

        private sealed class KeyedRows
        {
            public SortedDictionary<string, RowRecord> ByDef { get; } = new SortedDictionary<string, RowRecord>(StringComparer.Ordinal);
            public SortedDictionary<long, RowRecord> ByEntity { get; } = new SortedDictionary<long, RowRecord>();

            /// <summary><c>key=int</c> 컴포넌트의 줄 (키 순, G129).</summary>
            public SortedDictionary<long, RowRecord> ByInt { get; } = new SortedDictionary<long, RowRecord>();

            public RowRecord? Find(RowKey key)
            {
                RowRecord? record;
                if (key.IsEntity) ByEntity.TryGetValue(key.Number, out record);
                else if (key.IsInt) ByInt.TryGetValue(key.Number, out record);
                else ByDef.TryGetValue(key.DefKey!, out record);
                return record;
            }

            public void Put(RowKey key, RowRecord record)
            {
                if (key.IsEntity) ByEntity[key.Number] = record;
                else if (key.IsInt) ByInt[key.Number] = record;
                else ByDef[key.DefKey!] = record;
            }

            public void Remove(RowKey key)
            {
                if (key.IsEntity) ByEntity.Remove(key.Number);
                else if (key.IsInt) ByInt.Remove(key.Number);
                else ByDef.Remove(key.DefKey!);
            }
        }

        /// <summary>줄 하나의 엔티티 쪽 기록. <see cref="Attached"/>면 런타임에 붙인 줄(뗄 수 있음).</summary>
        private sealed class RowRecord
        {
            public RowRecord(bool attached) => Attached = attached;
            public bool Attached { get; }
            public Dictionary<MemberDecl, Value> Values { get; } = new Dictionary<MemberDecl, Value>();
        }

        private readonly struct RowKey
        {
            private RowKey(KeyKind kind, string? defKey, long number)
            {
                Kind = kind;
                DefKey = defKey;
                Number = number;
            }

            public static RowKey Def(string key) => new RowKey(KeyKind.Def, key, 0);
            public static RowKey Entity(EntityId key) => new RowKey(KeyKind.Entity, null, key.Value);
            public static RowKey Int(long key) => new RowKey(KeyKind.Int, null, key);

            public KeyKind Kind { get; }
            public string? DefKey { get; }

            /// <summary>엔티티 키의 엔티티 id, 또는 int 키.</summary>
            public long Number { get; }

            public long EntityKey => Number;
            public bool IsEntity => Kind == KeyKind.Entity;
            public bool IsInt => Kind == KeyKind.Int;

            public bool Same(RowKey other) => Kind == other.Kind && Number == other.Number && DefKey == other.DefKey;

            public override string ToString() =>
                Kind == KeyKind.Def ? "'" + DefKey + "'"
                : Kind == KeyKind.Entity ? new EntityId(Number).ToString()
                : Number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
