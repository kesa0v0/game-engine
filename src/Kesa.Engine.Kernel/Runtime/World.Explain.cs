using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
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
