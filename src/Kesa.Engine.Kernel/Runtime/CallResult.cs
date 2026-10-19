using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 호스트가 부른 모드 순수 함수의 결과 (spec-gaps G244·G247). 엔티티는 <see cref="EntityValue"/>이고 목록·사전 안도 그렇다.
    /// 함수가 읽은 곳(영수증)은 닫혀 있고 <see cref="Changed"/>로만 묻는다 (G246).
    /// </summary>
    public sealed class CallResult
    {
        private readonly World _world;
        private readonly long[] _reads;
        private readonly long[] _readVersions;
        private readonly string[] _sources;
        private readonly long[] _sourceVersions;
        private readonly long? _tick;

        internal CallResult(string function, Value value, World world, long[] reads, long[] readVersions, string[] sources,
            long[] sourceVersions, long? tick)
        {
            Function = function;
            Value = value;
            _world = world;
            _reads = reads;
            _readVersions = readVersions;
            _sources = sources;
            _sourceVersions = sourceVersions;
            _tick = tick;
        }

        /// <summary>부른 함수 <c>모드.함수</c>.</summary>
        public string Function { get; }

        public Value Value { get; }

        /// <summary>
        /// 다시 불러야 하나: 함수가 읽은 엔티티(죽음 포함), <c>query</c>로 읽은 목록, 읽은 틱 가운데 하나라도 그 뒤 바뀌었다. 다른 월드(불러온
        /// 월드 포함)면 늘 참이다. 엔티티 단위라 읽지 않은 컴포넌트가 바뀌어도 참일 수 있다 (G246).
        /// </summary>
        public bool Changed(World world)
        {
            if (!ReferenceEquals(world, _world)) return true;
            if (_tick != null && world.Tick != _tick.Value) return true;
            for (int i = 0; i < _reads.Length; i++)
                if (world.Version(new EntityId(_reads[i])) != _readVersions[i]) return true;
            for (int i = 0; i < _sources.Length; i++)
                if (world.SourceVersion(_sources[i]) != _sourceVersions[i]) return true;
            return false;
        }
    }
}
