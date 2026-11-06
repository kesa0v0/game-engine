using System;
using System.Collections.Generic;
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
        private protected EngineScope Engine()
        {
            if (_closed)
                throw new InvalidOperationException("the accessor of host function " + _function +
                                                    " was used after the call ended; do not keep it (spec-gaps G217)");
            return new EngineScope(World);
        }

        public MemberHandle Member(string owner, string member) { using (Engine()) return World.Member(owner, member); }

        public bool IsAlive(EntityId id) { using (Engine()) return World.IsAlive(id); }
        public string DefOf(EntityId id) { using (Engine()) return World.DefOf(id); }
        public bool Has(EntityId id, string component) { using (Engine()) return World.Has(id, component); }

        /// <summary>멤버 값. Def 타입의 const도 이것으로 읽는다.</summary>
        public Value Get(EntityId id, MemberHandle member) { using (Engine()) return World.Get(id, member); }

        /// <summary>칼럼을 읽기 전용 조각으로 읽는다 (G478).</summary>
        public World.ColumnParts Parts(string typeName, params MemberHandle[] members) { using (Engine()) return World.Parts(typeName, members); }

        public Value GetRow(EntityId id, MemberHandle member, string key) { using (Engine()) return World.GetRow(id, member, key); }
        public Value GetRow(EntityId id, MemberHandle member, EntityId key) { using (Engine()) return World.GetRow(id, member, key); }
        public Value GetRow(EntityId id, MemberHandle member, long key) { using (Engine()) return World.GetRow(id, member, key); }
        public bool HasRow(EntityId id, string component, string key) { using (Engine()) return World.HasRow(id, component, key); }
        public bool HasRow(EntityId id, string component, EntityId key) { using (Engine()) return World.HasRow(id, component, key); }
        public bool HasRow(EntityId id, string component, long key) { using (Engine()) return World.HasRow(id, component, key); }
        public IReadOnlyList<string> RowKeys(EntityId id, string component) { using (Engine()) return World.RowKeys(id, component); }
        public IReadOnlyList<long> RowInts(EntityId id, string component) { using (Engine()) return World.RowInts(id, component); }
        public IReadOnlyList<EntityId> RowEntities(EntityId id, string component) { using (Engine()) return World.RowEntities(id, component); }

        public IReadOnlyList<EntityId> All(string typeName) { using (Engine()) return World.All(typeName); }
        public IReadOnlyList<EntityId> Query(params string[] names) { using (Engine()) return World.Query(names); }
        public IReadOnlyList<EntityId> Inverse(EntityId target, MemberHandle reference) { using (Engine()) return World.Inverse(target, reference); }

        /// <summary>지금 틱 (스크립트의 <c>now()</c>).</summary>
        public long Now { get { using (Engine()) return World.Tick; } }
    }
}
