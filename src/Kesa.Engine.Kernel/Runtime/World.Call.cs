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
        public CallResult Call(string function, params Value[] arguments) => Call(function, arguments, withUi: false);

        /// <param name="withUi">UI 기록과 함께 부르나 (<see cref="WorldView.Call(UiState, string, Value[])"/>, G279): 그러면 <c>ui.…</c>를 읽는 함수도 부른다.</param>
        private CallResult Call(string function, Value[] arguments, bool withUi)
        {
            NoHostWrites("Call");
            var fn = Callable(function, arguments.Length, withUi);
            var args = new List<Value>(arguments.Length);
            foreach (var a in arguments) args.Add(ToScript(a));
            // 목록 출처와 틱은 when 조건과 같은 장치로, 읽은 엔티티는 (엔티티, 칸 이름) 단위로 모은다 (G246·G290)
            var savedReads = _whenReads;
            var savedSources = _whenReadsSources;
            bool savedTick = _whenReadsTick;
            var reads = new SortedSet<CallRead>();
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

        // ---- 영수증이 모으는 읽기 (G246·G290) ----

        /// <summary>
        /// 함수가 읽은 것 하나: 엔티티의 칸 하나(컴포넌트·역방향 컬렉션 이름), 살았는지만(<see cref="AliveRead"/>), 엔티티 전체(null).
        /// </summary>
        private readonly struct CallRead : IComparable<CallRead>
        {
            public CallRead(long id, string? name)
            {
                Id = id;
                Name = name;
            }

            public long Id { get; }
            public string? Name { get; }

            public int CompareTo(CallRead other) => Id != other.Id ? Id.CompareTo(other.Id) : string.CompareOrdinal(Name, other.Name);
        }

        /// <summary>
        /// 살았는지만 읽었다: Def와 그 <c>const</c>, Ref가 가리키는 대상, 엔티티 자신(<c>c.entity</c>). 죽을 때만 바뀐다. 칸 이름은 식별자라
        /// 빈 문자열과 겹치지 않는다.
        /// </summary>
        private const string AliveRead = "";

        /// <summary><c>controller(e)</c>를 읽었다: 배정이 바뀌면 도장이 찍히는 칸. 칸 이름은 식별자라 겹치지 않는다.</summary>
        private const string SeatRead = "@seat";

        /// <summary>when 조건을 평가하거나 호스트가 함수를 부르는 중이면 읽은 것을 모은다. 평소에는 null이라 읽기에 비용이 없다.</summary>
        private SortedSet<CallRead>? _whenReads;

        /// <summary>엔티티 <paramref name="id"/>의 <paramref name="name"/>을 읽었다.</summary>
        private void Read(long id, string? name) => _whenReads?.Add(new CallRead(id, name));

        /// <summary>읽은 것의 지금 도장. 죽었거나 없는 엔티티는 0이고, 칸은 아직 찍히지 않았으면 만들어진 때다.</summary>
        internal long ReadVersion(long id, string? name)
        {
            if (!_entities.ContainsKey(id) || !_stamps.TryGetValue(id, out var s)) return 0;
            if (name == null) return s.Any;
            if (name.Length == 0) return s.Created;
            return s.ByName != null && s.ByName.TryGetValue(name, out var v) ? v : s.Created;
        }

        private CallResult Receipt(string function, Value value, SortedSet<CallRead> reads, SortedSet<string> sources, bool tick)
        {
            var ids = new long[reads.Count];
            var readNames = new string?[reads.Count];
            var versions = new long[reads.Count];
            int at = 0;
            foreach (var read in reads)
            {
                ids[at] = read.Id;
                readNames[at] = read.Name;
                versions[at] = ReadVersion(read.Id, read.Name);
                at++;
            }
            var names = new string[sources.Count];
            sources.CopyTo(names);
            var sourceVersions = new long[names.Length];
            for (int i = 0; i < names.Length; i++) sourceVersions[i] = SourceVersion(names[i]);
            return new CallResult(function, value, this, ids, readNames, versions, names, sourceVersions, tick ? _tick : (long?)null,
                _viewer != null && _viewRules.Count > 0 ? SeatsVersion : (long?)null);
        }

        /// <summary>
        /// 부르기 전에 확인한다: 부를 수 있으면 null, 아니면 이유 문장(없는 이름이면 "혹시 …?"). UI 정의를 읽을 때 칸마다 쓴다 (G245·G247).
        /// </summary>
        /// <param name="withUi">UI 기록과 함께 부를 칸이면 참: <c>ui.…</c>를 읽는 함수도 된다 (G279).</param>
        public string? CheckCall(string function, int argumentCount, bool withUi = false)
        {
            try
            {
                Callable(function, argumentCount, withUi);
                return null;
            }
            catch (WorldException e)
            {
                return e.Hint == null ? e.Message : e.Message + " (" + e.Hint + ")";
            }
        }

        private FunctionValue Callable(string function, int argumentCount, bool withUi)
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
                    if (scope.TryGet(n, out var v, out _) && v is FunctionValue f && Refusal(f, withUi) == null) names.Add(n);
                throw Unknown("mod '" + mod + "' has no function '" + name + "'" +
                              (found ? " ('" + name + "' is a " + bound.KindName + ")" : ""), name, names);
            }
            string? refusal = Refusal(fn, withUi);
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
        private static string? Refusal(FunctionValue fn, bool withUi)
        {
            if (fn.IsUiAction) return "it is a @ui_action, which the host runs as an action, not as a value to show";
            if (fn.IsBot) return "it is a @bot, which the host runs in a bot's seat, not as a value to show";
            if (fn.IsEntry) return "it is a " + fn.EntryKind + ", which runs only when submitted or triggered";
            if (fn.Writes) return "it changes the world (at " + fn.WriteWitness!.Value + ")";
            if (fn.Random) return "it uses random numbers (at " + fn.RandomWitness!.Value + ")";
            if (fn.WritesUi) return "it changes UI state (at " + fn.UiWriteWitness!.Value + "); run it from a @ui_action";
            if (fn.TouchesBot) return "it uses bot state (at " + fn.BotTouchWitness!.Value + "); only a @bot can use it";
            // ui를 읽는 화면 함수는 연결의 UI 기록과 함께 부른다 (G279)
            if (fn.TouchesUi && !withUi)
                return "it reads UI state (at " + fn.UiTouchWitness!.Value + "); call it with ViewAs(issuer).Call(ui, ...)";
            return null;
        }

        private static string Count(int n) =>
            n == 1 ? "1 argument" : n.ToString(CultureInfo.InvariantCulture) + " arguments";
    }
}
