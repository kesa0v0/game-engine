using System.Collections.Generic;
using System.Text;
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

        /// <summary><c>query(...)</c>로 엔티티 목록을 읽었다 (G193). <see cref="ExplainNode.Member"/>가 출처 이름들, <see cref="ExplainNode.Value"/>가 그 목록이다.</summary>
        Query,

        /// <summary>
        /// 거른 읽기에서만: 이 단계에 그 연결이 못 보는 근거가 있었다 (spec-gaps G205). 단계마다 많아야 하나이고 그 단계의 끝에 온다.
        /// 몇 개였는지, 어디였는지는 보이지 않는다.
        /// </summary>
        Hidden,
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

        internal static ExplainNode Query(IReadOnlyList<string> names, ListValue result)
        {
            // 스크립트에 쓰는 대로 맨 이름이다 (G306)
            return new ExplainNode(ExplainKind.Query, default, "", null, string.Join(", ", names), result, new List<ExplainNode>())
            {
                Names = names,
            };
        }

        internal static ExplainNode Hidden() =>
            new ExplainNode(ExplainKind.Hidden, default, "", null, "", HiddenValue.Instance, new List<ExplainNode>());

        /// <summary>같은 근거에 값과 아래 근거만 바꾼 것 (거른 읽기).</summary>
        internal ExplainNode With(Value value, IReadOnlyList<ExplainNode> reads) =>
            new ExplainNode(Kind, Entity, Component, Key, Member, value, reads, Op, Source)
            {
                Names = Names,
                FromComponent = FromComponent,
                FromKey = FromKey,
            };

        /// <summary><see cref="ExplainKind.Query"/>의 출처 이름들 (Def 타입·컴포넌트).</summary>
        internal IReadOnlyList<string> Names { get; private set; } = new string[0];

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

        /// <summary>선언에서 모인 기여면 선언이 있던 컴포넌트 (G298). 저장된 기여 행이면 null.</summary>
        public string? FromComponent { get; internal set; }

        /// <summary>선언이 키 줄의 키 Def에 있었으면 그 Def id (G298).</summary>
        public string? FromKey { get; internal set; }

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
            if (Kind == ExplainKind.Hidden)
            {
                text.Append("(hidden)");
            }
            else if (Kind == ExplainKind.Time)
            {
                text.Append("now() = ").Append(World.Show(Value));
            }
            else if (Kind == ExplainKind.Query)
            {
                var ids = new List<string>();
                foreach (var item in ((ListValue)Value).Items) ids.Add(((IEntityReference)item).EntityId.ToString());
                text.Append("query(").Append(Member).Append(") = [").Append(string.Join(", ", ids)).Append(']');
            }
            else if (Kind == ExplainKind.Contribution)
            {
                text.Append("contribution ").Append(Op).Append(' ').Append(World.Show(Value));
                if (Source != null) text.Append(" from ").Append(Source.Value);
                if (FromComponent != null)
                {
                    text.Append(" (").Append(FromComponent);
                    if (FromKey != null) text.Append('[').Append(FromKey).Append(']');
                    text.Append(')');
                }
            }
            else
            {
                text.Append(Component).Append('.').Append(Member);
                if (Key != null) text.Append('[').Append(Key).Append(']');
                // defdata(id)로 읽은 Def 상수는 엔티티가 없다 (Key가 Def id, G303)
                if (Entity.Value != 0) text.Append(" of ").Append(Entity);
                text.Append(" = ").Append(World.Show(Value));
            }
            text.Append('\n');
            foreach (var read in Reads) read.Append(text, depth + 1);
        }
    }
}
