using System.Collections.Generic;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
    /// <summary>
    /// 엔티티를 쓰는 호스트 함수(<c>Writes</c>)가 첫 인자로 받는 창구 (spec-gaps G217). 읽기에 더해 쓰기와 그 Flow의 난수 흐름을
    /// 준다. 쓰기는 커맨드 안이면 롤백 기록에 들어간다. <c>Submit</c>·<c>Answer</c>·<c>Respond</c>·<c>Advance</c>·<c>Save</c>는 없다.
    /// </summary>
    public sealed class HostAccess : HostReader
    {
        internal HostAccess(World world, string function) : base(world, function)
        {
        }

        public void Set(EntityId id, MemberHandle member, Value value) => Use(() => World.Set(id, member, value));
        public void SetRow(EntityId id, MemberHandle member, string key, Value value) => Use(() => World.SetRow(id, member, key, value));
        public void SetRow(EntityId id, MemberHandle member, EntityId key, Value value) => Use(() => World.SetRow(id, member, key, value));
        public void SetRow(EntityId id, MemberHandle member, long key, Value value) => Use(() => World.SetRow(id, member, key, value));

        public EntityId Create(string defId, params Init[] inits) => Use(() => World.Create(defId, inits));
        public void Destroy(EntityId id) => Use(() => World.Destroy(id));

        /// <summary>컴포넌트를 붙인다 (스크립트의 <c>attach</c>).</summary>
        public void Add(EntityId id, string component, params Init[] inits) => Use(() => World.Add(id, component, inits));

        /// <summary>컴포넌트를 뗀다 (스크립트의 <c>detach</c>).</summary>
        public void Remove(EntityId id, string component) => Use(() => World.Remove(id, component));

        public void AddRow(EntityId id, string component, string key, params Init[] inits) => Use(() => World.AddRow(id, component, key, inits));
        public void AddRow(EntityId id, string component, EntityId key, params Init[] inits) => Use(() => World.AddRow(id, component, key, inits));
        public void AddRow(EntityId id, string component, long key, params Init[] inits) => Use(() => World.AddRow(id, component, key, inits));
        public void RemoveRow(EntityId id, string component, string key) => Use(() => World.RemoveRow(id, component, key));
        public void RemoveRow(EntityId id, string component, EntityId key) => Use(() => World.RemoveRow(id, component, key));
        public void RemoveRow(EntityId id, string component, long key) => Use(() => World.RemoveRow(id, component, key));

        public long AddContribution(EntityId target, MemberHandle member, string op, Value value, EntityId? source = null) =>
            Use(() => World.AddContribution(target, member, op, value, source));
        public long AddContribution(EntityId target, MemberHandle member, string key, string op, Value value, EntityId? source = null) =>
            Use(() => World.AddContribution(target, member, key, op, value, source));
        public long AddContribution(EntityId target, MemberHandle member, EntityId key, string op, Value value, EntityId? source = null) =>
            Use(() => World.AddContribution(target, member, key, op, value, source));
        public long AddContribution(EntityId target, MemberHandle member, long key, string op, Value value, EntityId? source = null) =>
            Use(() => World.AddContribution(target, member, key, op, value, source));
        public void RemoveContribution(long id) => Use(() => World.RemoveContribution(id));

        // ---- 난수: 스크립트 random과 같은 흐름·같은 뜻 (G178·G179) ----

        /// <summary><c>random.randint(a, b)</c>: a 이상 b 이하.</summary>
        public long RandInt(long a, long b) => Use(() => ((IntValue)World.HostRandom("randint", new IntValue(a), new IntValue(b))).Value);

        /// <summary><c>random.randrange(stop)</c>: 0 이상 stop 미만.</summary>
        public long RandRange(long stop) => Use(() => ((IntValue)World.HostRandom("randrange", new IntValue(stop))).Value);

        /// <summary><c>random.randrange(start, stop, step)</c>.</summary>
        public long RandRange(long start, long stop, long step = 1) =>
            Use(() => ((IntValue)World.HostRandom("randrange", new IntValue(start), new IntValue(stop), new IntValue(step))).Value);

        /// <summary><c>random.random()</c>: 0 이상 1 미만의 <see cref="Fixed"/>.</summary>
        public Fixed Random() => Use(() => ((FixedValue)World.HostRandom("random")).Value);

        /// <summary><c>random.choice(items)</c>.</summary>
        public T Choice<T>(IReadOnlyList<T> items) =>
            items[(int)((IntValue)Use(() => World.HostRandom("choice", Indices(items.Count)))).Value];

        /// <summary><c>random.sample(items, k)</c>: 중복 없이 k개를 뽑은 순서대로.</summary>
        public IReadOnlyList<T> Sample<T>(IReadOnlyList<T> items, long k)
        {
            var picked = (ListValue)Use(() => World.HostRandom("sample", Indices(items.Count), new IntValue(k)));
            var result = new List<T>(picked.Items.Count);
            foreach (var index in picked.Items) result.Add(items[(int)((IntValue)index).Value]);
            return result;
        }

        private static ListValue Indices(int count)
        {
            var indices = new List<Value>(count);
            for (int i = 0; i < count; i++) indices.Add(new IntValue(i));
            return new ListValue(indices);
        }
    }
}
