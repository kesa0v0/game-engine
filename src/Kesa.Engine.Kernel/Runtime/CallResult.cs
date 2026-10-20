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
        private readonly long? _seats;
        private UiState? _ui;
        private long _uiVersion;

        /// <summary>함수가 <c>ui.…</c>를 읽었으면 그 기록과 그때의 바뀐 횟수를 붙인다 (G279).</summary>
        internal CallResult WithUi(UiState ui, long version)
        {
            _ui = ui;
            _uiVersion = version;
            return this;
        }

        internal CallResult(string function, Value value, World world, long[] reads, long[] readVersions, string[] sources,
            long[] sourceVersions, long? tick, long? seats)
        {
            _seats = seats;
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
        /// 다시 불러야 하나: 함수가 읽은 엔티티(죽음 포함), <c>query</c>로 읽은 목록, 읽은 틱 가운데 하나라도 그 뒤 바뀌었다. 보는 쪽으로
        /// 불렀으면 보기 정책이 읽은 엔티티도 들고, 배정 표가 바뀌어도 참이다 (G248). 다른 월드(불러온 월드 포함)면 늘 참이다.
        /// 엔티티 단위라 읽지 않은 컴포넌트가 바뀌어도 참일 수 있다 (G246). UI 기록과 함께 불렀고 <c>ui.…</c>를 읽었으면 그 기록이 바뀌어도
        /// 참이다(기록 단위, G279).
        /// </summary>
        public bool Changed(World world)
        {
            if (!ReferenceEquals(world, _world)) return true;
            if (_ui != null && _ui.Version != _uiVersion) return true;
            if (_tick != null && world.Tick != _tick.Value) return true;
            if (_seats != null && world.SeatsVersion != _seats.Value) return true;
            for (int i = 0; i < _reads.Length; i++)
                if (world.Version(new EntityId(_reads[i])) != _readVersions[i]) return true;
            for (int i = 0; i < _sources.Length; i++)
                if (world.SourceVersion(_sources[i]) != _sourceVersions[i]) return true;
            return false;
        }
    }
}
