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
            Touch(row.Target);
            Stamp(row.Target, row.Component.Name);
            _contributions.Remove(row.Id);
            Unindex(_contribByTarget, row.Target, row.Id);
            if (row.Source != null) Unindex(_contribBySource, row.Source.Value, row.Id);
        }

        private void PutContribution(Contribution row)
        {
            Touch(row.Target);
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

        /// <summary><c>contribs(h, "max")</c>: 그 자리를 겨냥한 행들 (넣은 순). 행은 <c>op</c>, <c>value</c>(fixed), <c>source</c>를 가진다.</summary>
        private Value ContribsBuiltin(IReadOnlyList<Value> args)
        {
            if (args.Count != 2)
                throw new ScriptRuntimeException(Codes.RunScript, "contribs() takes the component and a derived name, e.g. contribs(h, \"max\")");
            if (!(args[0] is ComponentView view))
                throw new ScriptRuntimeException(Codes.RunType,
                    "contribs() takes the component being computed (or one of its sibling components), not " + args[0].KindName,
                    args[0] is RowsView ? "pick one row first, e.g. h.Skill[key]" : null);
            if (!(args[1] is StrValue name))
                throw new ScriptRuntimeException(Codes.RunType, "the second argument of contribs() is a value name (str), not " + args[1].KindName);

            if (!view.Component.TryGetMember(name.Value, out var member) || member.Kind != MemberKind.Derived)
            {
                var derived = new List<string>();
                foreach (var m in view.Component.Members)
                    if (m.Kind == MemberKind.Derived) derived.Add(m.Name);
                throw new ScriptRuntimeException(Codes.RunScript,
                    "'" + view.Component.Name + "' has no derived value '" + name.Value + "' to take contributions",
                    Suggest.Hint(name.Value, derived));
            }

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
            return new ListValue(rows);
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
                    case "op": return new StrValue(_row.Op);
                    case "value": return new FixedValue(_row.Value);
                    case "source":
                        return _row.Source != null ? (Value)new EntityView(_world, new EntityId(_row.Source.Value)) : NoneValue.Instance;
                }
                throw new ScriptRuntimeException(Codes.RunScript, "a contribution has no '" + name + "'",
                    Suggest.Hint(name, new[] { "op", "value", "source" }));
            }
        }
    }
}
