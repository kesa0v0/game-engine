using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
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

        /// <summary>
        /// 읽기 실행 하나(<c>derived</c>, 입구·정책 함수, <c>choose</c> 후보)의 기본 단계 예산 (G101·G224). 값 하나를 읽는 일이라 작다.
        /// </summary>
        public const int DefaultStepBudget = 100000;

        /// <summary>
        /// 행동 실행 하나(커맨드의 사전 조건과 본문, <c>@on</c>·<c>@when</c> 핸들러, 멈춘 Flow를 이어서 실행하는 구간)의 기본 단계 예산 (G224).
        /// 사전 조건만 검사할 때도 제출과 같은 예산이다.
        /// 행동은 세계 전체를 돌 수 있어 크게 둔다: 한 단계 약 45ns(규모 벤치)로 약 0.5초, 유닛 하나에 7단계면 약 140만 개.
        /// </summary>
        public const int DefaultActionStepBudget = 10_000_000;

        /// <param name="stepBudget">읽기 실행 하나의 단계 예산 (<see cref="DefaultStepBudget"/>, G101).</param>
        /// <param name="actionStepBudget">행동 실행 하나의 단계 예산 (<see cref="DefaultActionStepBudget"/>, G224).</param>
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
        /// <param name="requireViewRules">
        /// 서버 권위 멀티 게임처럼 숨김이 꼭 지켜져야 하는 호스트가 켠다. 켰는데 보기 규칙이 하나도 없으면 만들지 않는다 (spec-gaps G203·G315).
        /// </param>
        public World(ContentLoadResult loaded, int stepBudget = DefaultStepBudget,
            int maxSignalDepth = DefaultMaxSignalDepth, int maxAdvancePerCall = DefaultMaxAdvancePerCall,
            int maxSettleRounds = DefaultMaxSettleRounds, long seed = 0, bool requireViewRules = false,
            int actionStepBudget = DefaultActionStepBudget)
            : this(loaded.Frozen, loaded.Schema, loaded.Scopes, stepBudget, loaded.Commands, actionStepBudget)
        {
            SetViewRules(loaded.ViewRules);
            _uiActions = loaded.UiActions;
            _mods = loaded.Mods;
            _contents = loaded.Contents;
            _hostFunctions = loaded.HostFunctions;
            foreach (var signature in _hostFunctions.Values)
                if (signature.Function.IsDeclarationOnly)
                    throw new ArgumentException("host function " + signature.Function.Name + " has no implementation: it was " +
                                                "read from a declarations file, which is for engine check and stubs only; " +
                                                "register the C# function to run a world (spec-gaps G221)", nameof(loaded));
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
            SetWraps(loaded.Wraps);
            if (requireViewRules && loaded.ViewRules.Count == 0)
                throw new WorldException(Codes.RunNoViewPolicy,
                    "the host requires view rules, but no active mod declares one",
                    "a mod must declare @view_rule(Hand) def f(viewer, entity) for what it hides (spec-gaps G203·G315)");
        }

        public World(DefSet defs, SchemaSet schema, IReadOnlyDictionary<string, ModScope>? scopes = null,
            int stepBudget = DefaultStepBudget, IReadOnlyDictionary<string, CommandDecl>? commands = null,
            int actionStepBudget = DefaultActionStepBudget)
        {
            _defs = defs;
            _schema = schema;
            _scopes = scopes ?? new SortedDictionary<string, ModScope>(StringComparer.Ordinal);
            _stepBudget = stepBudget;
            _actionStepBudget = actionStepBudget;
            _commands = commands ?? new SortedDictionary<string, CommandDecl>(StringComparer.Ordinal);
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
            defId = ScriptDefId(defId);
            if (!_defs.TryGet(defId, out var def))
            {
                var ids = new List<string>();
                foreach (var d in _defs.Defs) ids.Add(d.Id);
                throw UnknownDef("there is no Def '" + defId + "' (abstract Defs cannot be created)", defId, ids);
            }
            if (def.Type.IsDataOnly)
                throw new WorldException(Codes.RunNotEntity,
                    "'" + defId + "' is a " + def.Type.Name + ", a data-only Def type with no components, so it does not become entities");

            var given = Given(inits, h => h.Component != null && !h.Component.IsKeyed && def.Components.Contains(h.Component.Name),
                "'" + defId + "' has no such component, or it is keyed (add rows with AddRow)");
            // 모든 검사를 마친 뒤에 월드를 바꾼다 (실패한 호출은 흔적이 없다)
            var planned = new Planned();
            var plannedRows = new EntityRecord(0, def, LayoutOf(def));
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
            var entity = new EntityRecord(id, def, LayoutOf(def));
            planned.CopyTo(entity.Values);
            foreach (var pair in plannedRows.Rows) entity.Rows.Add(pair.Key, pair.Value);
            _entities.Add(id, entity);
            if (!_byType.TryGetValue(def.Type, out var set)) _byType.Add(def.Type, set = new SortedSet<long>());
            set.Add(id);
            Touch(id, null);
            TouchSources(entity); // 그 타입과 컴포넌트의 query 목록이 바뀐다 (G193)
            StampCreated(id);
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
                _stamps.Remove(id);
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
            Touch(id.Value, null); // 이 엔티티를 읽거나 Ref로 가리킨 조건을 다시 본다
            TouchSources(entity);
            StampDestroyed(id.Value);
            DropSeat(id.Value);
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
                    foreach (var pair in h.Rows)
                    {
                        var rows = pair.Value;
                        if (!rows.ByEntity.TryGetValue(id.Value, out var row)) continue;
                        Touch(holder, pair.Key);
                        Stamp(holder, pair.Key);
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
        public string DefOf(EntityId id) => Record(id, AliveRead).Def.Id;

        // ---- 읽기·쓰기 (키 없는 컴포넌트와 Def 타입의 const) ----

        public Value Get(EntityId id, MemberHandle member)
        {
            var entity = Record(id, member.Component?.Name ?? AliveRead);
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
            return Read(null, component, member.Member, DefMembers(entity, component), entity.Values);
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
            Assign(id.Value, component, entity.Values, member.Member, stored);
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

            Touch(id.Value, component.Name);
            TouchSource(component.Name);
            Stamp(id.Value, component.Name);
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
            Touch(id.Value, componentName);
            TouchSource(componentName);
            Stamp(id.Value, componentName);
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

        public bool Has(EntityId id, string componentName) => Has(Record(id, componentName), ComponentNamed(componentName));


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
            var entity = Record(target, reference.Member.Spec.Inverse?.Name);
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


        // ---- 작은 도우미 ----

        /// <summary>엔티티 전체를 읽는다: when 조건과 영수증이 엔티티의 어느 바뀜에든 다시 본다 (S5·G246).</summary>
        private EntityRecord Record(EntityId id) => Record(id, null);

        /// <summary>
        /// 엔티티의 칸 <paramref name="name"/>(컴포넌트·역방향 컬렉션 이름, <see cref="AliveRead"/>, null이면 전체)을 읽는다. 영수증은
        /// 그 칸이 바뀔 때만 다시 부르고, when 조건도 그 칸이 바뀔 때만 다시 본다 (G290).
        /// </summary>
        private EntityRecord Record(EntityId id, string? name)
        {
            Read(id.Value, name);
            if (_entities.TryGetValue(id.Value, out var entity))
            {
                // 보는 쪽으로 부르는 중이면 못 보는 엔티티는 죽은 id와 같다 (G198·G248)
                if (_viewer != null && HiddenFromViewer(id.Value)) throw GoneForView(id);
                return entity;
            }
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
            entity.Layout.Find(component) != null || entity.Runtime.Count > 0 && entity.Runtime.ContainsKey(component.Name);

        private static void Require(EntityRecord entity, ComponentType component)
        {
            if (!Has(entity, component))
                throw new WorldException(Codes.RunNoComponent,
                    new EntityId(entity.Id) + " (" + entity.Def.Id + ") has no '" + component.Name + "'",
                    "check with Has, or add it with Add");
        }

        private static OrderedMap<ResolvedValue>? DefMembers(EntityRecord entity, ComponentType component) =>
            entity.Layout.Find(component)?.Members;

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

        /// <summary>없는 Def id. 짧은 이름이면 같은 경로의 전체 이름을 먼저 권한다 (G264).</summary>
        private static WorldException UnknownDef(string message, string input, IEnumerable<string> candidates) =>
            new WorldException(Codes.RunUnknownName, message, DefIds.Hint(input, candidates));

        /// <summary>
        /// 스크립트 함수가 내장을 부르거나 칸에 쓰는 중이면 그 모드 기준으로 짧은 Def id를 푼다 (G261). 호스트가 부르면 그대로라서
        /// 전체 이름이어야 한다 (G264).
        /// </summary>
        private string ScriptDefId(string id)
        {
            string mod = _evaluator?.ScriptMod ?? "";
            return mod.Length == 0 || id.Length == 0 ? id : DefIds.Resolve(id, mod);
        }

        /// <summary>
        /// <c>defid("Sloop")</c> (spec-gaps G266): 부른 함수의 모드 기준 전체 이름. 그런 (구체) Def가 없으면 오류다. 리터럴 인자는 ⑤ 링크가
        /// 먼저 본다.
        /// </summary>
        private Value DefIdBuiltin(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count != 1 || keywords.Count > 0 || !(positional[0] is StrValue name))
                throw new ScriptRuntimeException(Codes.RunType,
                    "defid() takes one Def id string, got " +
                    (positional.Count == 1 && keywords.Count == 0 ? positional[0].KindName : positional.Count + keywords.Count + " arguments"),
                    "write defid(\"Sloop\")");
            string id = mod.Length == 0 ? name.Value : DefIds.Resolve(name.Value, mod);
            if (!_defs.TryGet(id, out _))
            {
                var ids = new List<string>();
                foreach (var d in _defs.Defs) ids.Add(d.Id);
                throw new ScriptRuntimeException(Codes.RunUnknownName, "there is no Def '" + id + "'", DefIds.Hint(id, ids));
            }
            return new StrValue(id);
        }
    }
}
