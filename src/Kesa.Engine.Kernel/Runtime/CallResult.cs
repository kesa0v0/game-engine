using System.Collections.Generic;
using System.Linq;
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
        private readonly string?[] _readNames;
        private readonly long[] _readVersions;
        private readonly string[] _sources;
        private readonly long[] _sourceVersions;
        private readonly long? _tick;
        private readonly long? _seats;
        private readonly long? _requests;
        private readonly IReadOnlyCollection<string>? _questions;
        private UiState? _ui;
        private long _uiVersion;

        private UiState? _pointerUi;
        private KeyValuePair<string, long>[]? _pointerReads;

        /// <summary>함수가 <c>cursor()</c>·<c>drag()</c>로 읽은 키와 그때의 바뀐 때를 붙인다 (G487·G488).</summary>
        internal CallResult WithPointer(UiState ui, IReadOnlyDictionary<string, long> reads)
        {
            _pointerUi = ui;
            _pointerReads = reads.ToArray();
            return this;
        }

        /// <summary>함수가 <c>ui.…</c>를 읽었으면 그 기록과 그때의 바뀐 횟수를 붙인다 (G279).</summary>
        internal CallResult WithUi(UiState ui, long version)
        {
            _ui = ui;
            _uiVersion = version;
            return this;
        }

        internal CallResult(string function, Value value, World world, long[] reads, string?[] readNames, long[] readVersions,
            string[] sources, long[] sourceVersions, long? tick, long? seats, long? requests = null,
            IReadOnlyCollection<string>? questions = null)
        {
            _questions = questions;
            _seats = seats;
            _requests = requests;
            Function = function;
            Value = value;
            _world = world;
            _reads = reads;
            _readNames = readNames;
            _readVersions = readVersions;
            _sources = sources;
            _sourceVersions = sourceVersions;
            _tick = tick;
        }

        /// <summary>함수가 <c>questions()</c>로 이 이름의 질문을 읽었나(이름 없이 모두 읽었어도 참) (G386).</summary>
        public bool ReadsQuestion(string name) => _questions != null && (_questions.Contains("") || _questions.Contains(name));

        /// <summary>부른 함수 <c>모드.함수</c>.</summary>
        public string Function { get; }

        public Value Value { get; }

        /// <summary>
        /// 다시 불러야 하나: 함수가 읽은 (엔티티, 컴포넌트)·역방향 컬렉션(죽음 포함), <c>query</c>로 읽은 목록, 읽은 틱 가운데 하나라도
        /// 그 뒤 바뀌었다. 같은 엔티티의 읽지 않은 컴포넌트가 바뀌어도 거짓이다 (G290). Def와 그 <c>const</c>, Ref가 가리키는 대상은
        /// 죽었는지만 본다. 보는 쪽으로 불렀으면 보기 정책이 읽은 것도 들고, 배정 표가 바뀌어도 참이다 (G248). 다른 월드(불러온 월드 포함)면
        /// 늘 참이다. <c>questions()</c>를 읽었으면 열린 질문이 바뀌어도 참이다(G383). UI 기록과 함께 불렀고 <c>ui.…</c>를 읽었으면 그 기록이 바뀌어도 참이다(기록 단위, G279).
        /// <c>cursor()</c>·<c>drag()</c>로 읽은 키의 값이 바뀌어도 참이다(키 단위, G487·G488).
        /// </summary>
        public bool Changed(World world)
        {
            if (!ReferenceEquals(world, _world)) return true;
            if (_ui != null && _ui.Version != _uiVersion) return true;
            if (_pointerReads != null)
                foreach (var read in _pointerReads)
                    if (_pointerUi!.Pointer(read.Key).Value != read.Value) return true;
            if (_tick != null && world.Tick != _tick.Value) return true;
            if (_seats != null && world.SeatsVersion != _seats.Value) return true;
            if (_requests != null && world.RequestsVersion != _requests.Value) return true;
            for (int i = 0; i < _reads.Length; i++)
                if (world.ReadVersion(_reads[i], _readNames[i]) != _readVersions[i]) return true;
            for (int i = 0; i < _sources.Length; i++)
                if (world.SourceVersion(_sources[i]) != _sourceVersions[i]) return true;
            return false;
        }
    }
}
