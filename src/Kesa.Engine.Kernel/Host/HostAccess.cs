using System;
using System.Collections.Generic;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
    /// <summary>
    /// 호스트 함수가 월드를 읽는 창구 (spec S7, spec-gaps G216·G217). 순수 함수는 첫 매개변수를 <c>HostReader</c>로 두면 받는다.
    /// 호출마다 새로 만들고 호출이 끝나면 무효가 된다. 월드의 공개 읽기를 그대로 부르므로 읽은 칸은 스크립트처럼 기록된다
    /// (<c>derived</c> 재계산, <c>when</c> 재평가, <c>Explain</c>).
    /// </summary>
    public class HostReader
    {
        private readonly string _function;
        private bool _closed;

        internal HostReader(World world, string function)
        {
            World = world;
            _function = function;
        }

        internal World World { get; }

        internal void Close() => _closed = true;

        /// <summary>월드를 부른다. 호출이 끝난 창구는 쓸 수 없고, 부르는 동안은 엔진이 하는 일이라 공개 쓰기 막기를 푼다.</summary>
        private protected T Use<T>(Func<T> body)
        {
            if (_closed)
                throw new InvalidOperationException("the accessor of host function " + _function +
                                                    " was used after the call ended; do not keep it (spec-gaps G217)");
            return World.AsEngine(body);
        }

        private protected void Use(Action body) => Use(() =>
        {
            body();
            return true;
        });

        public MemberHandle Member(string owner, string member) => Use(() => World.Member(owner, member));

        public bool IsAlive(EntityId id) => Use(() => World.IsAlive(id));
        public string DefOf(EntityId id) => Use(() => World.DefOf(id));
        public bool Has(EntityId id, string component) => Use(() => World.Has(id, component));

        /// <summary>멤버 값. Def 타입의 const도 이것으로 읽는다.</summary>
        public Value Get(EntityId id, MemberHandle member) => Use(() => World.Get(id, member));

        public Value GetRow(EntityId id, MemberHandle member, string key) => Use(() => World.GetRow(id, member, key));
        public Value GetRow(EntityId id, MemberHandle member, EntityId key) => Use(() => World.GetRow(id, member, key));
        public Value GetRow(EntityId id, MemberHandle member, long key) => Use(() => World.GetRow(id, member, key));
        public bool HasRow(EntityId id, string component, string key) => Use(() => World.HasRow(id, component, key));
        public bool HasRow(EntityId id, string component, EntityId key) => Use(() => World.HasRow(id, component, key));
        public bool HasRow(EntityId id, string component, long key) => Use(() => World.HasRow(id, component, key));
        public IReadOnlyList<string> RowKeys(EntityId id, string component) => Use(() => World.RowKeys(id, component));
        public IReadOnlyList<long> RowInts(EntityId id, string component) => Use(() => World.RowInts(id, component));
        public IReadOnlyList<EntityId> RowEntities(EntityId id, string component) => Use(() => World.RowEntities(id, component));

        public IReadOnlyList<EntityId> All(string typeName) => Use(() => World.All(typeName));
        public IReadOnlyList<EntityId> Query(params string[] names) => Use(() => World.Query(names));
        public IReadOnlyList<EntityId> Inverse(EntityId target, MemberHandle reference) => Use(() => World.Inverse(target, reference));

        /// <summary>지금 틱 (스크립트의 <c>now()</c>).</summary>
        public long Now => Use(() => World.Tick);
    }

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
