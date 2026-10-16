using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    public enum ExplainKind
    {
        /// <summary>저장된 값(<c>field</c>·<c>const</c>·Ref)을 읽었다.</summary>
        Read,

        /// <summary>다른 <c>derived</c>를 읽었다. <see cref="ExplainNode.Reads"/>가 그 계산의 근거다.</summary>
        Derived,

        /// <summary>기여 행 하나를 읽었다.</summary>
        Contribution,

        /// <summary><c>now()</c>로 지금 틱을 읽었다 (G155). <see cref="ExplainNode.Value"/>가 그 틱이다.</summary>
        Time,
    }

    /// <summary>
    /// <c>derived</c> 값이 어떻게 나왔는지의 근거 (spec S4 trace, spec-gaps G105): 계산 중 실제로 읽은 값들의 트리.
    /// 읽은 순서대로 나열하고, 같은 값을 두 번 읽었으면 두 번 나온다.
    /// </summary>
    public sealed class ExplainNode
    {
        internal ExplainNode(ExplainKind kind, EntityId entity, string component, string? key, string member, Value value,
            IReadOnlyList<ExplainNode> reads, string? op = null, EntityId? source = null)
        {
            Kind = kind;
            Entity = entity;
            Component = component;
            Key = key;
            Member = member;
            Value = value;
            Reads = reads;
            Op = op;
            Source = source;
        }

        internal static ExplainNode Time(Value tick) =>
            new ExplainNode(ExplainKind.Time, default, "", null, "", tick, new List<ExplainNode>());

        public ExplainKind Kind { get; }
        public EntityId Entity { get; }
        public string Component { get; }

        /// <summary>키 컴포넌트의 줄이면 그 키 (Def id 또는 엔티티 id).</summary>
        public string? Key { get; }

        public string Member { get; }
        public Value Value { get; }

        /// <summary><see cref="ExplainKind.Derived"/>의 근거. 그 밖에는 비어 있다.</summary>
        public IReadOnlyList<ExplainNode> Reads { get; }

        /// <summary><see cref="ExplainKind.Contribution"/>의 연산 이름.</summary>
        public string? Op { get; }

        public EntityId? Source { get; }

        /// <summary>사람이 읽는 들여쓴 글 (예: <c>Health.max of #1 = 14</c>).</summary>
        public string Render()
        {
            var text = new StringBuilder();
            Append(text, 0);
            return text.ToString().TrimEnd('\n');
        }

        private void Append(StringBuilder text, int depth)
        {
            text.Append(' ', depth * 2);
            if (Kind == ExplainKind.Time)
            {
                text.Append("now() = ").Append(World.Show(Value));
            }
            else if (Kind == ExplainKind.Contribution)
            {
                text.Append("contribution ").Append(Op).Append(' ').Append(World.Show(Value));
                if (Source != null) text.Append(" from ").Append(Source.Value);
            }
            else
            {
                text.Append(Component).Append('.').Append(Member);
                if (Key != null) text.Append('[').Append(Key).Append(']');
                text.Append(" of ").Append(Entity).Append(" = ").Append(World.Show(Value));
            }
            text.Append('\n');
            foreach (var read in Reads) read.Append(text, depth + 1);
        }
    }

    public sealed partial class World
    {
        /// <summary>읽은 값을 모으는 중이면 지금 계산의 읽기 목록. 평소에는 null이라 읽기에 비용이 없다.</summary>
        private List<ExplainNode>? _trace;

        /// <summary><c>derived</c> 값을 계산하고 그 근거(읽은 값들의 트리)를 함께 돌려준다. 값은 <see cref="ExplainNode.Value"/>다.</summary>
        public ExplainNode Explain(EntityId id, MemberHandle member) => ExplainCore(id, member, null);

        public ExplainNode Explain(EntityId id, MemberHandle member, string key) => ExplainCore(id, member, RowKey.Def(key));

        public ExplainNode Explain(EntityId id, MemberHandle member, EntityId key) => ExplainCore(id, member, RowKey.Entity(key));

        private ExplainNode ExplainCore(EntityId id, MemberHandle member, RowKey? key)
        {
            var entity = Record(id);
            if (member.Component == null)
                throw new WorldException(Diagnostics.Codes.RunNotComputed, "'" + member.Display + "' is a const of the Def type, not a derived value");
            var component = member.Component;
            if (component.IsKeyed != (key != null))
                throw new WorldException(Diagnostics.Codes.RunNoRow, component.IsKeyed
                    ? "'" + component.Name + "' is a keyed component; give the row key"
                    : "'" + component.Name + "' is not a keyed component");
            if (key == null) Require(entity, component);
            else if (!FindRow(entity, component, key.Value, out _, out _)) throw NoRow(id, component, key.Value);

            var saved = _trace;
            var reads = new List<ExplainNode>();
            _trace = reads;
            try
            {
                var value = member.Kind == MemberKind.Derived
                    ? ReadDerivedTop(entity, component, key, member.Member)
                    : key == null ? Get(id, member) : GetRow(id, member, key.Value);
                return new ExplainNode(member.Kind == MemberKind.Derived ? ExplainKind.Derived : ExplainKind.Read, id,
                    component.Name, key?.ToString().Trim('\''), member.Member.Name, value, reads);
            }
            finally
            {
                _trace = saved;
            }
        }

        /// <summary>함수 본문이 읽은 멤버 하나를 기록하며 읽는다. 기록 중이 아니면 그냥 읽는다.</summary>
        private Value ReadMemberTraced(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            var parent = _trace;
            if (parent == null) return ReadMember(entity, component, row, member);

            var id = new EntityId(entity.Id);
            string? key = row?.ToString().Trim('\'');
            if (member.Kind != MemberKind.Derived)
            {
                var plain = ReadMember(entity, component, row, member);
                parent.Add(new ExplainNode(ExplainKind.Read, id, component.Name, key, member.Name, plain, new List<ExplainNode>()));
                return plain;
            }

            var inner = new List<ExplainNode>();
            _trace = inner;
            try
            {
                var value = ReadMember(entity, component, row, member);
                parent.Add(new ExplainNode(ExplainKind.Derived, id, component.Name, key, member.Name, value, inner));
                return value;
            }
            finally
            {
                _trace = parent;
            }
        }
    }
}
