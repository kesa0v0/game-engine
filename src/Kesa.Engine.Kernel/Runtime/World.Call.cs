using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 호스트가 모드의 순수 함수를 이름으로 부른다 (spec-gaps G244·G245·G247). UI가 보여 줄 값은 이 함수들이 만들고, 호스트 연결부는
    /// UI 정의에서 읽은 이름으로 부를 뿐 게임 단어를 모른다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// <c>모드.함수</c>를 위치 순서의 인자로 부른다. 쓰기 함수·난수 함수·커맨드·핸들러·반응은 부르지 않는다(G245). 없는 이름, 인자 수,
        /// 스크립트 오류, 걸음 예산(G224) 초과는 <see cref="WorldException"/>이다(G247). 아무것도 바꾸지 않는다.
        /// </summary>
        public CallResult Call(string function, params Value[] arguments)
        {
            NoHostWrites("Call");
            var fn = Callable(function, arguments.Length);
            var args = new List<Value>(arguments.Length);
            foreach (var a in arguments) args.Add(ToScript(a));
            // 읽은 곳은 when 조건과 같은 장치로 모은다 (G246)
            var savedReads = _whenReads;
            var savedSources = _whenReadsSources;
            bool savedTick = _whenReadsTick;
            var reads = new SortedSet<long>();
            var sources = new SortedSet<string>(StringComparer.Ordinal);
            _whenReads = reads;
            _whenReadsSources = sources;
            _whenReadsTick = false;
            Value value;
            bool tick;
            try
            {
                value = RunGateFunction(fn, args, function);
            }
            finally
            {
                tick = _whenReadsTick;
                _whenReads = savedReads;
                _whenReadsSources = savedSources;
                _whenReadsTick = savedTick;
            }
            return Receipt(function, value, reads, sources, tick);
        }

        // ---- 보는 쪽으로 부르기 (G248) ----

        /// <summary>
        /// 보는 쪽으로 부르는 중이면 그 연결. 함수 안의 읽기가 <see cref="WorldView"/>처럼 걸러진다. 보기 정책과 <c>derived</c> 계산(G199)
        /// 안에서는 잠시 풀어 실제 상태를 읽는다.
        /// </summary>
        private WorldView? _viewer;

        /// <summary>배정 표가 바뀐 횟수: 보는 쪽 영수증은 이것도 본다(앉은 곳이 바뀌면 보이는 것이 바뀐다).</summary>
        internal long SeatsVersion { get; private set; }

        internal CallResult CallAs(WorldView viewer, string function, Value[] arguments)
        {
            var saved = _viewer;
            _viewer = viewer;
            try
            {
                return Call(function, arguments);
            }
            finally
            {
                _viewer = saved;
            }
        }

        private T Unviewed<T>(Func<T> read)
        {
            var saved = _viewer;
            _viewer = null;
            try
            {
                return read();
            }
            finally
            {
                _viewer = saved;
            }
        }

        /// <summary>보는 쪽으로 부르는 중이고 이 살아 있는 엔티티가 안 보인다.</summary>
        private bool HiddenFromViewer(long id) => _viewer != null && _entities.ContainsKey(id) && !_viewer.Sees(new EntityId(id));

        /// <summary>보는 쪽으로 부르는 중이고 이 엔티티의 이 컴포넌트가 안 보인다.</summary>
        private bool HiddenFromViewer(long id, string component) =>
            _viewer != null && _entities.ContainsKey(id) && !_viewer.Sees(new EntityId(id), component);

        /// <summary>읽은 값의 Ref: 못 보는 엔티티는 <see cref="HiddenValue"/>, 목록에서는 빠진다 (G198).</summary>
        private Value ViewFiltered(Value value) => _viewer == null ? value : _viewer.Filter(value);

        /// <summary><c>query</c>의 목록을 보이는 것만으로: 엔티티가 보이고 이름으로 고른 컴포넌트가 모두 보여야 한다 (<see cref="WorldView.Query"/>).</summary>
        private IReadOnlyList<EntityId> ViewQueried(IReadOnlyList<EntityId> ids, string[] names)
        {
            var kept = new List<EntityId>(ids.Count);
            foreach (var id in ids)
            {
                bool seen = _viewer!.Sees(id);
                foreach (var name in names)
                {
                    if (!seen) break;
                    if (IsComponentName(name)) seen = _viewer.Sees(id, name);
                }
                if (seen) kept.Add(id);
            }
            return kept;
        }

        /// <summary>엔티티 목록에서 못 보는 것을 뺀다. <paramref name="component"/>가 있으면 그 컴포넌트도 보여야 한다.</summary>
        private IReadOnlyList<EntityId> ViewFiltered(IReadOnlyList<EntityId> ids, string? component)
        {
            if (_viewer == null) return ids;
            var kept = new List<EntityId>(ids.Count);
            foreach (var id in ids)
                if (component == null ? _viewer.Sees(id) : _viewer.Sees(id, component)) kept.Add(id);
            return kept;
        }

        private CallResult Receipt(string function, Value value, SortedSet<long> reads, SortedSet<string> sources, bool tick)
        {
            var ids = new long[reads.Count];
            reads.CopyTo(ids);
            var versions = new long[ids.Length];
            for (int i = 0; i < ids.Length; i++) versions[i] = Version(new EntityId(ids[i]));
            var names = new string[sources.Count];
            sources.CopyTo(names);
            var sourceVersions = new long[names.Length];
            for (int i = 0; i < names.Length; i++) sourceVersions[i] = SourceVersion(names[i]);
            return new CallResult(function, value, this, ids, versions, names, sourceVersions, tick ? _tick : (long?)null,
                _viewer != null && _viewPolicy != null ? SeatsVersion : (long?)null);
        }

        /// <summary>
        /// 부르기 전에 확인한다: 부를 수 있으면 null, 아니면 이유 문장(없는 이름이면 "혹시 …?"). UI 정의를 읽을 때 칸마다 쓴다 (G245·G247).
        /// </summary>
        public string? CheckCall(string function, int argumentCount)
        {
            try
            {
                Callable(function, argumentCount);
                return null;
            }
            catch (WorldException e)
            {
                return e.Hint == null ? e.Message : e.Message + " (" + e.Hint + ")";
            }
        }

        private FunctionValue Callable(string function, int argumentCount)
        {
            int dot = function.LastIndexOf('.');
            if (dot <= 0 || dot == function.Length - 1)
                throw new WorldException(Codes.RunUnknownName,
                    "'" + function + "' is not a function name; write it as mod.function", "for example skirmish.unit_row");
            string mod = function.Substring(0, dot);
            string name = function.Substring(dot + 1);
            if (!_scopes.TryGetValue(mod, out var scope))
                throw Unknown("there is no mod '" + mod + "'", mod, _scopes.Keys);
            bool found = scope.TryGet(name, out var bound, out _);
            if (!(bound is FunctionValue fn))
            {
                var names = new List<string>();
                foreach (var n in scope.Names)
                    if (scope.TryGet(n, out var v, out _) && v is FunctionValue f && Refusal(f) == null) names.Add(n);
                throw Unknown("mod '" + mod + "' has no function '" + name + "'" +
                              (found ? " ('" + name + "' is a " + bound.KindName + ")" : ""), name, names);
            }
            string? refusal = Refusal(fn);
            if (refusal != null)
                throw new WorldException(Codes.RunNotCallable, "'" + function + "' cannot be called by the host: " + refusal,
                    "the host calls pure functions only; move the reading part into its own function (spec-gaps G245)");
            int count = fn.Parameters.Count;
            if (argumentCount != count)
                throw new WorldException(Codes.RunNotCallable,
                    "'" + function + "' takes " + Count(count) + ", not " + argumentCount.ToString(CultureInfo.InvariantCulture),
                    "pass the arguments in the order the function lists them (spec-gaps G247)");
            return fn;
        }

        /// <summary>호스트가 부르지 않는 함수면 그 이유, 아니면 null (G245).</summary>
        private static string? Refusal(FunctionValue fn)
        {
            if (fn.IsUiAction) return "it is a @ui_action, which the host runs as an action, not as a value to show";
            if (fn.IsEntry) return "it is a " + fn.EntryKind + ", which runs only when submitted or triggered";
            if (fn.Writes) return "it changes the world (at " + fn.WriteWitness!.Value + ")";
            if (fn.Random) return "it uses random numbers (at " + fn.RandomWitness!.Value + ")";
            if (fn.WritesUi) return "it changes UI state (at " + fn.UiWriteWitness!.Value + "); run it from a @ui_action";
            // 화면 함수가 ui를 읽는 길은 아직 없다 (G279의 영수증과 함께 붙인다)
            if (fn.TouchesUi) return "it reads UI state (at " + fn.UiTouchWitness!.Value + ")";
            return null;
        }

        private static string Count(int n) =>
            n == 1 ? "1 argument" : n.ToString(CultureInfo.InvariantCulture) + " arguments";
    }
}
