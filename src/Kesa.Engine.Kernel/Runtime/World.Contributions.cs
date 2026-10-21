using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>기여 행 (spec S4, spec-gaps G98). 커널 전용 저장이고, 대상은 <c>derived</c> 값뿐이다.</summary>
    public sealed partial class World
    {
        private long _nextContribution = 1;

        /// <summary>id 순 = 넣은 순. <c>contribs</c>가 이 순서로 행을 준다.</summary>
        private readonly SortedDictionary<long, Contribution> _contributions = new SortedDictionary<long, Contribution>();

        /// <summary>대상 엔티티 → 그 엔티티를 겨냥한 행 id들.</summary>
        private readonly Dictionary<long, SortedSet<long>> _contribByTarget = new Dictionary<long, SortedSet<long>>();

        /// <summary>출처 엔티티 → 그 엔티티가 낸 행 id들.</summary>
        private readonly Dictionary<long, SortedSet<long>> _contribBySource = new Dictionary<long, SortedSet<long>>();

        private sealed class Contribution
        {
            public Contribution(long id, long target, ComponentType component, RowKey? row, MemberDecl member,
                string op, Fixed value, long? source)
            {
                Id = id;
                Target = target;
                Component = component;
                Row = row;
                Member = member;
                Op = op;
                Value = value;
                Source = source;
            }

            public long Id { get; }
            public long Target { get; }
            public ComponentType Component { get; }
            public RowKey? Row { get; }
            public MemberDecl Member { get; }
            public string Op { get; }
            public Fixed Value { get; }
            public long? Source { get; }
        }

        /// <summary>
        /// 키 없는 컴포넌트의 <c>derived</c> 값에 기여 행을 넣는다. <paramref name="value"/>는 <c>int</c>나 <c>fixed</c>이고
        /// <c>fixed</c>로 저장한다 (G95·G98). <paramref name="source"/>가 죽으면 행도 지워진다. 돌려주는 id로 한 행만 뗄 수 있다.
        /// </summary>
        public long AddContribution(EntityId target, MemberHandle member, string op, Value value, EntityId? source = null) =>
            AddContribution(target, member, (RowKey?)null, op, value, source);

        public long AddContribution(EntityId target, MemberHandle member, string key, string op, Value value, EntityId? source = null) =>
            AddContribution(target, member, RowKey.Def(key), op, value, source);

        public long AddContribution(EntityId target, MemberHandle member, EntityId key, string op, Value value, EntityId? source = null) =>
            AddContribution(target, member, RowKey.Entity(key), op, value, source);

        public long AddContribution(EntityId target, MemberHandle member, long key, string op, Value value, EntityId? source = null) =>
            AddContribution(target, member, RowKey.Int(key), op, value, source);

        private long AddContribution(EntityId target, MemberHandle member, RowKey? key, string op, Value value, EntityId? source)
        {
            NoHostWrites("AddContribution");
            var entity = Record(target);
            if (member.Component == null || member.Kind != MemberKind.Derived)
                throw new WorldException(Codes.RunNotWritable,
                    "'" + member.Display + "' is not a derived value, so it takes no contributions",
                    "contributions only target derived members (spec-gaps G98)");
            var component = member.Component;
            if (component.IsKeyed != (key != null))
                throw new WorldException(Codes.RunNoRow, component.IsKeyed
                    ? "'" + component.Name + "' is a keyed component; give the row key"
                    : "'" + component.Name + "' is not a keyed component");
            if (key == null) Require(entity, component);
            else if (!FindRow(entity, component, key.Value, out _, out _)) throw NoRow(target, component, key.Value);

            Fixed amount;
            switch (value)
            {
                case FixedValue f: amount = f.Value; break;
                case IntValue i:
                    if (!FixedMath.TryFromInt(i.Value, out amount))
                        throw new WorldException(Codes.RunType, i.Value + " is too large for a fixed value");
                    break;
                default:
                    throw new WorldException(Codes.RunType, "a contribution value must be int or fixed, not " + value.KindName);
            }
            if (source != null) Record(source.Value);

            long id = _nextContribution++;
            var row = new Contribution(id, target.Value, component, key, member.Member, op, amount, source?.Value);
            PutContribution(row);
            Journal(() =>
            {
                DropContributionUnjournaled(row);
                _nextContribution = id;
            });
            return id;
        }

        /// <summary>행 하나를 뗀다. 이미 없으면 오류다.</summary>
        public void RemoveContribution(long id)
        {
            NoHostWrites("RemoveContribution");
            if (!_contributions.TryGetValue(id, out var row))
                throw new WorldException(Codes.RunNoRow, "there is no contribution " + id,
                    "it may already have been removed, or its source or target entity died");
            DropContribution(row);
        }

        /// <summary><paramref name="source"/>가 낸 행을 모두 뗀다. 뗀 행 수를 돌려준다.</summary>
        public int RemoveBySource(EntityId source)
        {
            Record(source);
            return RemoveContributionsIn(_contribBySource, source.Value);
        }

        /// <summary>엔티티가 죽을 때: 그 엔티티를 겨냥하거나 그 엔티티가 낸 행을 지운다 (G98).</summary>
        private void DropContributionsOf(long entity)
        {
            RemoveContributionsIn(_contribByTarget, entity);
            RemoveContributionsIn(_contribBySource, entity);
        }

        /// <summary>컴포넌트(또는 그 줄 하나)가 떼어질 때 그 자리를 겨냥한 행을 지운다. 값이 없어진 자리에 행이 남으면 안 된다.</summary>
        private void DropContributionsTo(long entity, ComponentType component, RowKey? row = null)
        {
            if (!_contribByTarget.TryGetValue(entity, out var ids)) return;
            foreach (var id in new List<long>(ids))
            {
                var c = _contributions[id];
                if (c.Component != component) continue;
                if (row != null && !SameRow(c.Row, row)) continue;
                DropContribution(c);
            }
        }

        private int RemoveContributionsIn(Dictionary<long, SortedSet<long>> index, long entity)
        {
            if (!index.TryGetValue(entity, out var ids)) return 0;
            int count = 0;
            foreach (var id in new List<long>(ids))
            {
                if (!_contributions.TryGetValue(id, out var row)) continue;
                DropContribution(row);
                count++;
            }
            return count;
        }

        /// <summary>행 하나를 지운다. 커맨드 실행 중이면 되살리는 동작을 기록한다 (G133).</summary>
        private void DropContribution(Contribution row)
        {
            DropContributionUnjournaled(row);
            Journal(() => PutContribution(row));
        }

        private void DropContributionUnjournaled(Contribution row)
        {
            Touch(row.Target, row.Component.Name);
            Stamp(row.Target, row.Component.Name);
            _contributions.Remove(row.Id);
            Unindex(_contribByTarget, row.Target, row.Id);
            if (row.Source != null) Unindex(_contribBySource, row.Source.Value, row.Id);
        }

        private void PutContribution(Contribution row)
        {
            Touch(row.Target, row.Component.Name);
            Stamp(row.Target, row.Component.Name);
            _contributions.Add(row.Id, row);
            Index(_contribByTarget, row.Target, row.Id);
            if (row.Source != null) Index(_contribBySource, row.Source.Value, row.Id);
        }

        private static void Index(Dictionary<long, SortedSet<long>> index, long entity, long id)
        {
            if (!index.TryGetValue(entity, out var set)) index.Add(entity, set = new SortedSet<long>());
            set.Add(id);
        }

        private static void Unindex(Dictionary<long, SortedSet<long>> index, long entity, long id)
        {
            if (!index.TryGetValue(entity, out var set)) return;
            set.Remove(id);
            if (set.Count == 0) index.Remove(entity);
        }

        // ---- 함수 본문의 contribs ----

        /// <summary><c>contribs(h, "max")</c>: 그 자리를 겨냥한 행들 (넣은 순). 행은 <c>id</c>, <c>op</c>, <c>value</c>(fixed), <c>source</c>를 가진다.</summary>
        private Value ContribsBuiltin(IReadOnlyList<Value> args)
        {
            if (args.Count != 2)
                throw new ScriptRuntimeException(Codes.RunScript, "contribs() takes the component and a derived name, e.g. contribs(h, \"max\")");
            if (!(args[0] is ComponentView view))
                throw new ScriptRuntimeException(Codes.RunType,
                    "contribs() takes the component being computed (or one of its sibling components), not " + args[0].KindName,
                    args[0] is RowsView ? "pick one row first, e.g. h.Skill[key]" : null);
            var member = DerivedMemberArg(PureBuiltins.ContribsName, view, args[1]);

            var rows = new List<Value>();
            Read(view.Id.Value, view.Component.Name); // 기여 행은 그 컴포넌트의 도장을 찍는다
            if (_contribByTarget.TryGetValue(view.Id.Value, out var ids))
            {
                foreach (var id in ids)
                {
                    var c = _contributions[id];
                    if (c.Member != member || !SameRow(c.Row, view.Row)) continue;
                    if (c.Source != null && HiddenFromViewer(c.Source.Value)) continue; // 못 보는 출처의 행은 빠진다 (G205·G248)
                    rows.Add(new ContributionView(this, c));
                    _trace?.Add(new ExplainNode(ExplainKind.Contribution, view.Id, view.Component.Name,
                        c.Row?.ToString().Trim('\''), member.Name, new FixedValue(c.Value), new List<ExplainNode>(), c.Op,
                        c.Source == null ? (EntityId?)null : new EntityId(c.Source.Value)));
                }
            }
            if (DeclarationSites().Count > 0) CollectDeclared(view, member, rows);
            return new ListValue(rows);
        }

        // ---- 선언 보너스 (G295~G298) ----

        /// <summary>
        /// 선언 칸이 있는 자리: 키 없는 컴포넌트와 그 <c>const(list[Contrib])</c> 칸들, 또는 Def 키 컴포넌트와 그 키 Def 타입의 칸들.
        /// 스키마에서 한 번 뽑아 두어 읽을 때마다 훑지 않는다 (G295).
        /// </summary>
        private sealed class DeclarationSite
        {
            public DeclarationSite(ComponentType component, List<MemberDecl> slots)
            {
                Component = component;
                Slots = slots;
            }

            public ComponentType Component { get; }
            public List<MemberDecl> Slots { get; }
        }

        private List<DeclarationSite>? _declarationSites;

        /// <summary>내용에 실제로 적힌 <c>to=</c> 걸음들 (G300): Ref 칼럼과 역방향 컬렉션. 없는 걸음은 읽을 때 찾지 않는다.</summary>
        private sealed class StepSet
        {
            public List<(MemberDecl Member, string To)> Refs { get; } = new List<(MemberDecl, string)>();
            public List<InverseRelation> Inverses { get; } = new List<InverseRelation>();
        }

        private StepSet? _contributionSteps;

        private StepSet ContributionSteps()
        {
            if (_contributionSteps != null) return _contributionSteps;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var def in _defs.Defs)
            {
                foreach (var name in def.Consts.Keys)
                {
                    def.Consts.TryGet(name, out var value);
                    AddSteps(value.Value, names);
                }
                foreach (var name in def.Components.Keys)
                {
                    def.Components.TryGet(name, out var rc);
                    foreach (var m in rc.Members.Keys)
                    {
                        rc.Members.TryGet(m, out var value);
                        AddSteps(value.Value, names);
                    }
                }
            }
            // 런타임에 붙인 컴포넌트는 스키마 기본값을 쓴다
            foreach (var component in _schema.Components)
                foreach (var m in component.Members)
                    if (m.Spec.Default != null) AddSteps(m.Spec.Default, names);
            foreach (var type in _schema.Types)
                foreach (var m in type.Consts)
                    if (m.Spec.Default != null) AddSteps(m.Spec.Default, names);

            var steps = new StepSet();
            foreach (var name in names)
            {
                int dot = name.IndexOf('.');
                if (dot >= 0)
                {
                    if (_schema.TryGetComponent(name.Substring(0, dot), out var component) && !component.IsKeyed &&
                        component.TryGetMember(name.Substring(dot + 1), out var reference) && reference.Kind == MemberKind.Ref)
                        steps.Refs.Add((reference, name));
                    continue;
                }
                foreach (var type in _schema.Types)
                    foreach (var inverse in type.Inverses)
                        if (inverse.Name == name) steps.Inverses.Add(inverse);
            }
            return _contributionSteps = steps;
        }

        private static void AddSteps(Value value, SortedSet<string> names)
        {
            if (!(value is ListValue list)) return;
            foreach (var item in list.Items)
                if (item is ContribValue c && c.To != null) names.Add(c.To);
        }

        private List<DeclarationSite> DeclarationSites()
        {
            if (_declarationSites != null) return _declarationSites;
            var sites = new List<DeclarationSite>();
            foreach (var component in _schema.Components)
            {
                IEnumerable<MemberDecl> members = component.Members;
                if (component.IsKeyed)
                {
                    if (component.KeyKind != KeyKind.Def || component.Key == null) continue;
                    members = component.Key.Consts;
                }
                var slots = new List<MemberDecl>();
                foreach (var m in members)
                    if (m.Kind == MemberKind.Const && SchemaBuiltins.IsContribList(m.Spec.Type)) slots.Add(m);
                if (slots.Count > 0) sites.Add(new DeclarationSite(component, slots));
            }
            return _declarationSites = sites;
        }

        /// <summary>
        /// 선언 보너스를 저장된 줄 뒤에 붙인다: 먼저 자기 엔티티의 것, 그 뒤 <c>to=</c>로 이 엔티티에 닿는 엔티티들의 것 (G300).
        /// Ref 걸음(<c>to="Held.holder"</c>)은 역방향 색인으로 나를 가리키는 엔티티를(id 순), 역방향 컬렉션 걸음(<c>to="units"</c>)은
        /// 내 Ref가 가리키는 엔티티를 찾는다. 관계가 바뀌면 그 쓰기가 이 엔티티를 건드리므로 <c>@when</c>이 깨어난다.
        /// </summary>
        private void CollectDeclared(ComponentView view, MemberDecl member, List<Value> rows)
        {
            string? wantKey = null;
            if (view.Row != null)
            {
                if (view.Row.Value.Kind != KeyKind.Def) return; // 선언의 key=는 Def id뿐이다 (G297)
                wantKey = view.Row.Value.DefKey;
            }
            var target = Record(view.Id);
            CollectFrom(target, null, view, member, wantKey, rows);

            var steps = ContributionSteps();
            foreach (var step in steps.Refs)
            {
                var inverseName = step.Member.Spec.Inverse?.Name;
                if (inverseName != null) Read(target.Id, inverseName); // 이름 없는 Ref의 변경은 엔티티 전체를 건드린다
                if (!_inverse.TryGetValue(step.Member, out var byTarget) || !byTarget.TryGetValue(target.Id, out var sources)) continue;
                foreach (var source in sources)
                    if (!HiddenFromViewer(source)) CollectFrom(_entities[source], step.To, view, member, wantKey, rows);
            }
            foreach (var inverse in steps.Inverses)
            {
                Read(target.Id, inverse.Source.Name);
                if (!Has(target, inverse.Source) || HiddenFromViewer(target.Id, inverse.Source.Name)) continue;
                if (!target.Values.TryGetValue(inverse.Member, out var value) || !(value is EntityValue holder)) continue;
                Read(holder.Id.Value, AliveRead);
                if (!_entities.TryGetValue(holder.Id.Value, out var record) || HiddenFromViewer(holder.Id.Value)) continue;
                CollectFrom(record, inverse.Name, view, member, wantKey, rows);
            }
        }

        /// <summary>
        /// <paramref name="entity"/>에 붙은 컴포넌트와 키 줄의 키 Def가 선언한 보너스 중, 걸음이 <paramref name="to"/>이고 이 자리를 겨냥한 것
        /// (컴포넌트 순, 키 id 순). 선언이 있을 수 있는 컴포넌트는 모두 읽은 것으로 적어, 나중에 붙거나 떼어져도 <c>@when</c>이 깨어난다.
        /// </summary>
        private void CollectFrom(EntityRecord entity, string? to, ComponentView view, MemberDecl member, string? wantKey, List<Value> rows)
        {
            foreach (var site in DeclarationSites())
            {
                var component = site.Component;
                Read(entity.Id, component.Name);
                if (!Has(entity, component) || HiddenFromViewer(entity.Id, component.Name)) continue;
                if (!component.IsKeyed)
                {
                    foreach (var slot in site.Slots)
                        Collect(Read(null, component, slot, DefMembers(entity, component), entity.Values), null);
                    continue;
                }
                foreach (var key in DefRowKeys(entity, component))
                {
                    if (!_defs.TryGet(key, out var keyDef)) continue;
                    foreach (var slot in site.Slots)
                    {
                        Value list = keyDef.Consts.TryGet(slot.Name, out var raw) ? raw.Value
                            : slot.Spec.Default ?? new ListValue(new List<Value>());
                        Collect(list, key);
                    }
                }

                void Collect(Value list, string? from)
                {
                    foreach (var item in ((ListValue)list).Items)
                    {
                        var c = (ContribValue)item;
                        if (c.To != to || c.Component != view.Component.Name || c.Member != member.Name || c.Key != wantKey) continue;
                        rows.Add(new DeclaredContributionView(this, entity.Id, component.Name, from, c));
                        _trace?.Add(new ExplainNode(ExplainKind.Contribution, view.Id, view.Component.Name, wantKey, member.Name,
                            new FixedValue(c.Amount), new List<ExplainNode>(), c.Op, new EntityId(entity.Id))
                        {
                            FromComponent = component.Name,
                            FromKey = from,
                        });
                    }
                }
            }
        }

        /// <summary>둘째 인자: 그 컴포넌트의 <c>derived</c> 이름. 없거나 <c>derived</c>가 아니면 "혹시 …?"를 붙인 오류다.</summary>
        private static MemberDecl DerivedMemberArg(string function, ComponentView view, Value name)
        {
            if (!(name is StrValue s))
                throw new ScriptRuntimeException(Codes.RunType,
                    "the second argument of " + function + "() is a value name (str), not " + name.KindName);
            if (view.Component.TryGetMember(s.Value, out var member) && member.Kind == MemberKind.Derived) return member;
            var derived = new List<string>();
            foreach (var m in view.Component.Members)
                if (m.Kind == MemberKind.Derived) derived.Add(m.Name);
            throw new ScriptRuntimeException(Codes.RunScript,
                "'" + view.Component.Name + "' has no derived value '" + s.Value + "' to take contributions",
                Suggest.Hint(s.Value, derived));
        }

        // ---- 함수 본문의 add_contrib·remove_contrib·remove_contribs (G299) ----

        private void AddContributionBuiltins(SortedDictionary<string, Value> table)
        {
            void Writer(string name, Func<IReadOnlyList<Value>, IReadOnlyDictionary<string, Value>, Value> body) =>
                table.Add(name, new PureBuiltin(name, (p, k) => Guard(() => body(p, k))) { Writes = true });

            Writer(PureBuiltins.AddContribName, AddContribBuiltin);
            Writer(PureBuiltins.RemoveContribName, (p, k) =>
            {
                Expect(PureBuiltins.RemoveContribName, p, k, 1, "remove_contrib(id)");
                switch (p[0])
                {
                    case IntValue id:
                        RemoveContribution(id.Value);
                        return NoneValue.Instance;
                    case NoneValue _:
                        throw new ScriptRuntimeException(Codes.RunType,
                            "remove_contrib() got None: that contribution has no id",
                            "a contribution declared by a component or a key row (c.id is None) cannot be removed; " +
                            "detach the component or remove the row that declares it (spec-gaps G299)");
                }
                throw new ScriptRuntimeException(Codes.RunType,
                    "remove_contrib() takes the id that add_contrib() returned (int), not " + p[0].KindName);
            });
            Writer(PureBuiltins.RemoveContribsName, (p, k) =>
            {
                Expect(PureBuiltins.RemoveContribsName, p, k, 1, "remove_contribs(source)");
                return new IntValue(RemoveBySource(EntityArg(PureBuiltins.RemoveContribsName, p[0])));
            });
        }

        /// <summary>
        /// <c>add_contrib(u.Health, "max", "add", 3, source=wall)</c>: 기여 행을 넣고 그 id(int)를 돌려준다. 키 줄은 줄 하나
        /// (<c>u.Skill.Medicine</c>)를 넘긴다. 검사는 호스트 API와 같다.
        /// </summary>
        private Value AddContribBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = PureBuiltins.AddContribName;
            const string usage = "add_contrib(entity.Component, \"derived name\", \"op\", value, source=entity)";
            if (positional.Count != 4)
                throw new ScriptRuntimeException(Codes.RunScript,
                    name + "() takes 4 positional arguments, got " + positional.Count, "write " + usage);
            EntityId? source = null;
            foreach (var pair in keywords)
            {
                if (pair.Key != "source")
                    throw new ScriptRuntimeException(Codes.RunScript, name + "() has no keyword '" + pair.Key + "'",
                        Suggest.Hint(pair.Key, new[] { "source" }));
                if (!(pair.Value is NoneValue)) source = EntityArg(name, pair.Value);
            }

            ComponentView view;
            switch (positional[0])
            {
                case ComponentView v: view = v; break;
                case RowsView _:
                    throw new ScriptRuntimeException(Codes.RunType, name + "() got all the rows of a keyed component",
                        "pick one row, like entity.Skill.Medicine or entity.Skill[key]");
                case NoneValue _:
                    throw new ScriptRuntimeException(Codes.RunNoComponent, name + "() got None: the component is not attached",
                        "check it with 'is not None' first");
                default:
                    throw new ScriptRuntimeException(Codes.RunType,
                        name + "() takes a component of an entity (entity.Component), not " + positional[0].KindName, "write " + usage);
            }
            var member = DerivedMemberArg(name, view, positional[1]);
            if (!(positional[2] is StrValue op))
                throw new ScriptRuntimeException(Codes.RunType,
                    "the op given to " + name + "() is a name (str), not " + positional[2].KindName, "write " + usage);

            long id = AddContribution(view.Id, new MemberHandle(view.Component, null, member), view.Row, op.Value, positional[3], source);
            return new IntValue(id);
        }

        private static bool SameRow(RowKey? a, RowKey? b)
        {
            if (a == null || b == null) return a == null && b == null;
            return a.Value.Same(b.Value);
        }

        private sealed class ContributionView : Value, IRuntimeAttributes, IValueEquality
        {
            /// <summary>같은 기여 행이면 같다 (G142).</summary>
            public bool ValueEquals(Value other) => other is ContributionView v && v._row.Id == _row.Id;

            private readonly World _world;
            private readonly Contribution _row;

            public ContributionView(World world, Contribution row)
            {
                _world = world;
                _row = row;
            }

            public override string KindName => "contribution";

            public Contribution Row => _row;

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "id": return new IntValue(_row.Id);
                    case "op": return new StrValue(_row.Op);
                    case "value": return new FixedValue(_row.Value);
                    case "source":
                        return _row.Source != null ? (Value)new EntityView(_world, new EntityId(_row.Source.Value)) : NoneValue.Instance;
                    case "component":
                    case "key":
                        return NoneValue.Instance; // 저장된 줄은 선언에서 오지 않았다 (G298)
                }
                throw NoAttribute(name);
            }
        }

        private static readonly string[] ContributionAttributes = { "id", "op", "value", "source", "component", "key" };

        private static ScriptRuntimeException NoAttribute(string name) =>
            new ScriptRuntimeException(Codes.RunScript, "a contribution has no '" + name + "'",
                Suggest.Hint(name, ContributionAttributes));

        /// <summary>
        /// 선언에서 모인 줄 (G298): <c>id</c>는 None(뗄 수 없다, G299), <c>source</c>는 선언을 든 엔티티,
        /// <c>component</c>는 선언이 있던 컴포넌트, <c>key</c>는 키 줄에서 왔으면 그 키 Def id.
        /// </summary>
        private sealed class DeclaredContributionView : Value, IRuntimeAttributes, IValueEquality
        {
            private readonly World _world;
            private readonly long _source;
            private readonly string _component;
            private readonly string? _key;
            private readonly ContribValue _contrib;

            public DeclaredContributionView(World world, long source, string component, string? key, ContribValue contrib)
            {
                _world = world;
                _source = source;
                _component = component;
                _key = key;
                _contrib = contrib;
            }

            public override string KindName => "contribution";

            /// <summary>같은 엔티티의 같은 자리에서 온 같은 선언이면 같다 (G142).</summary>
            public bool ValueEquals(Value other) =>
                other is DeclaredContributionView v && v._source == _source && v._component == _component && v._key == _key &&
                ReferenceEquals(v._contrib, _contrib);

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "id": return NoneValue.Instance;
                    case "op": return new StrValue(_contrib.Op);
                    case "value": return new FixedValue(_contrib.Amount);
                    case "source": return new EntityView(_world, new EntityId(_source));
                    case "component": return new StrValue(_component);
                    case "key": return _key == null ? (Value)NoneValue.Instance : new StrValue(_key);
                }
                throw NoAttribute(name);
            }
        }
    }
}
