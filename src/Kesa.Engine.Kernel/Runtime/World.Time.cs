using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>시간을 흘린 결과 (spec X2, spec-gaps G154). 핸들러 결과는 제출처럼 담긴다 (G148).</summary>
    public sealed class AdvanceResult
    {
        internal AdvanceResult(int advanced, long tick, IReadOnlyList<Reaction> reactions)
        {
            Advanced = advanced;
            Tick = tick;
            Reactions = reactions;
        }

        /// <summary>실제로 진행한 틱 수.</summary>
        public int Advanced { get; }

        /// <summary>진행한 뒤의 틱.</summary>
        public long Tick { get; }

        /// <summary>진행하는 동안 돈 핸들러들, 돈 순서대로.</summary>
        public IReadOnlyList<Reaction> Reactions { get; }

        /// <summary>
        /// 시간을 막는 질문이 열려 있어 멈췄으면 그 질문들, 질문 번호 순 (spec X2, spec-gaps G159). 이때 <see cref="Advanced"/>는 바란 틱보다 작다.
        /// 다 진행했으면 비어 있다. 거른 결과(<see cref="WorldView.Show(AdvanceResult)"/>)에서는 그 연결이 답할 수 있는 것만 있다 (G206).
        /// </summary>
        public IReadOnlyList<Request> BlockedBy
        {
            get => _blockedBy;
            internal set
            {
                _blockedBy = value;
                Blocked = value.Count > 0;
            }
        }

        private IReadOnlyList<Request> _blockedBy = Array.Empty<Request>();

        /// <summary>시간을 막는 질문 때문에 멈췄다. 거른 결과에서도 그대로다(멈춘 것은 모두가 안다, G206).</summary>
        public bool Blocked { get; internal set; }
    }

    /// <summary>
    /// 시간 (spec X2, spec-gaps G154·G155). 틱 하나가 단위이고 틱은 저장 상태다. <c>advance(1)</c> 한 번은 틱 +1 →
    /// (만료 스윕: 표준 모드 몫) → Flow 깨우기(<c>wait</c>와 마감의 기본 답, G157·G161) → <see cref="EngineSignals.Tick"/> 핸들러 →
    /// (정착: <c>when</c>, 반응 ②) 순서다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>지금 틱. 0에서 시작한다.</summary>
        private long _tick;

        public long Tick => _tick;

        /// <summary>
        /// 시간을 <paramref name="ticks"/>만큼 흘린다. 게임의 C# 루프가 부른다(누가 부를지는 게임이 정한다, X2).
        /// 커맨드·함수 실행 중에는 부를 수 없다.
        /// </summary>
        public AdvanceResult Advance(int ticks = 1)
        {
            if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "advance needs at least one tick");
            if (_undo != null || _meter != null || _advancing)
                throw new InvalidOperationException("time cannot advance while a command or a function is running");
            var reactions = new List<Reaction>();
            int advanced = AdvanceTicks(ticks, reactions, out var blocking);
            return new AdvanceResult(advanced, _tick, reactions) { BlockedBy = blocking };
        }

        /// <summary>시간이 흐르는 중인가: 이 안에서 깨어난 Flow·틱 핸들러는 <c>advance()</c>를 부를 수 없다 (G167).</summary>
        private bool _advancing;

        /// <summary>틱을 흘린다. 실제로 간 틱 수를 돌려주고, 막는 질문에서 멈췄으면 그 질문들을 준다.</summary>
        private int AdvanceTicks(long ticks, List<Reaction> reactions, out IReadOnlyList<Request> blockedBy)
        {
            blockedBy = Array.Empty<Request>();
            var tick = _schema.TryGetSignal(EngineSignals.Tick, out var t) ? t : null;
            _advancing = true;
            try
            {
                for (int i = 0; i < ticks; i++)
                {
                    // 호스트가 실행 밖에서 지운 엔티티도 있다: 죽은 질문이 시간을 막지 않게 먼저 버린다 (G164)
                    SweepStale(reactions);
                    // 매 틱 전에 다시 본다: 앞 틱에서 열린 질문도 막는다 (X2)
                    var blocking = BlockingRequests();
                    if (blocking.Count > 0)
                    {
                        blockedBy = blocking;
                        return i;
                    }
                    _tick++;
                    WakeSleepers(reactions);
                    // 엔진이 낸 신호는 1세대다. 핸들러마다 각자 트랜잭션이라 핸들러가 실패해도 틱은 진행된다 (S5)
                    if (tick != null)
                    {
                        _emitted.Add(new Emitted(new SignalValue(this, tick, new List<KeyValuePair<SignalField, Value>>()), 1, null, null));
                        reactions.AddRange(RunReactions());
                    }
                    Settle(reactions, tickEnd: true); // 틱의 마지막 단계 (X2 5단계, S5)
                }
                return (int)ticks;
            }
            finally
            {
                _advancing = false;
            }
        }

        public const int DefaultMaxAdvancePerCall = 10000;

        private readonly int _maxAdvancePerCall = DefaultMaxAdvancePerCall;

        /// <summary><c>advance(n)</c>에서 멈춘 Flow, 멈춘 순서대로 (G167).</summary>
        private readonly List<long> _advancers = new List<long>();

        /// <summary>
        /// 제출·답·응답 하나가 끝난 뒤: 신호를 처리하고, <c>advance(n)</c>에서 멈춘 Flow가 있으면 멈춘 순서대로 n틱을 진행한 뒤 그 Flow를
        /// 이어 간다 (G167). 이어 간 Flow가 다시 <c>advance</c>하면 또 진행한다.
        /// </summary>
        private List<Reaction> AfterRun()
        {
            var reactions = RunReactions();
            long used = 0;
            while (_advancers.Count > 0)
            {
                long id = _advancers[0];
                _advancers.RemoveAt(0);
                if (!_flows.TryGetValue(id, out var flow)) continue; // 그 사이 대상이 사라져 취소되었다 (결과에 이미 있다)
                long want = flow.AdvanceBy!.Value;
                if (used + want > _maxAdvancePerCall)
                {
                    // 호출 하나가 호스트를 끝없이 붙잡지 않게 자른다. 그때까지 간 틱과 효과는 남는다 (G168)
                    DropFlow(flow);
                    reactions.Add(new Reaction(Reaction.AdvanceSignal, new SortedDictionary<string, Value>(StringComparer.Ordinal),
                        flow.Command, 0, ReactionOutcome.ScriptError)
                    {
                        Flow = flow.Id,
                        Error = new WorldException(Codes.RunScript,
                            "the " + (flow.Handler ? "handler " : "command ") + flow.Command + " called advance(" +
                            want.ToString(CultureInfo.InvariantCulture) + ") after " + used.ToString(CultureInfo.InvariantCulture) +
                            " ticks asked in this call, past the limit of " + _maxAdvancePerCall.ToString(CultureInfo.InvariantCulture) +
                            " ticks per call, so the flow was dropped",
                            "a loop of advance() should pause on a question each turn; a host that needs more can raise maxAdvancePerCall (spec-gaps G168)"),
                    });
                    continue;
                }
                int advanced = AdvanceTicks(want, reactions, out _);
                used += want; // 막혀서 덜 갔어도 요청한 만큼 센다: 막힌 채 도는 루프도 끝나게
                if (!_flows.ContainsKey(id)) continue; // 진행 중에 취소되었다
                Resume(flow, Reaction.AdvanceSignal, new IntValue(advanced), "after advancing", reactions);
                reactions.AddRange(RunReactions());
            }
            Settle(reactions, tickEnd: false); // 커맨드 하나가 끝난 뒤의 정착 지점 (S5)
            return reactions;
        }

        /// <summary>
        /// 깨울 틱이 된 Flow를 Flow id 순으로 깨워 각각 막힐 때까지 실행한다 (X2 3단계). 각자 트랜잭션이고, 그 Flow가 낸 신호는
        /// 그 Flow가 막히거나 끝난 뒤 돈다(S5). 실패하면 그 구간을 되감고 Flow를 버린다 (G157).
        /// </summary>
        private void WakeSleepers(List<Reaction> reactions)
        {
            // 잠이 끝난 Flow와 기본 답이 있는 마감이 지난 질문을 Flow id 순으로 (X2 3단계)
            var due = new List<FlowRecord>();
            foreach (var f in _flows.Values)
                if ((f.WakeAt != null && f.WakeAt.Value <= _tick) || (f.Request != null && f.Request.TimesOut && f.Request.Deadline!.Value <= _tick))
                    due.Add(f);
            foreach (var flow in due)
            {
                var values = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                var asked = flow.Request;
                string signal = asked == null ? Reaction.WakeSignal : Reaction.TimeoutSignal;
                // 대상이 사라졌으면 깨우지 않고 취소한다. 오류가 아니다 (S5 on_stale 기본 취소, G163)
                var stale = StaleReason(flow);
                if (stale != null)
                {
                    DropFlow(flow);
                    reactions.Add(new Reaction(signal, values, flow.Command, 0, ReactionOutcome.Canceled)
                    {
                        Flow = flow.Id,
                        Message = "flow " + flow.Id.ToString(CultureInfo.InvariantCulture) + " was canceled: " + stale,
                    });
                    continue;
                }
                if (asked == null) Resume(flow, signal, NoneValue.Instance, "after waking", reactions);
                else Resume(flow, signal, TimeoutAnswer(asked), "after its deadline", reactions);
                reactions.AddRange(RunReactions());
            }
        }

        /// <summary>
        /// 엔진이 Flow를 이어 간다(깨우기·마감·<c>advance</c> 뒤). 각자 트랜잭션이고 결과 목록에 남긴다. 실패하면 그 구간을 되감고 Flow를
        /// 버린다 (G157): 다시 두면 매번 같은 오류가 난다.
        /// </summary>
        private void Resume(FlowRecord flow, string signal, Value answer, string after, List<Reaction> reactions)
        {
            var values = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            try
            {
                var opened = Atomically(() => Continue(flow, answer, after));
                bool paused = _flows.ContainsKey(flow.Id);
                reactions.Add(new Reaction(signal, values, flow.Command, 0, paused ? ReactionOutcome.Suspended : ReactionOutcome.Finished)
                {
                    Flow = flow.Id,
                    OpenedRequest = opened,
                    SleepsUntil = SleepsUntil(flow.Id),
                });
            }
            catch (WorldException e)
            {
                DropFlow(flow); // 되감기가 되돌려 놓은 Flow를 버린다. 트랜잭션 밖이라 기록하지 않는다
                reactions.Add(new Reaction(signal, values, flow.Command, 0, ReactionOutcome.ScriptError)
                {
                    Flow = flow.Id,
                    Error = e,
                });
            }
        }

        /// <summary>마감이 지난 질문에 넣는 기본 답 (G126·G161): 답한 엔티티는 없다.</summary>
        private Value TimeoutAnswer(Request request)
        {
            if (request.TimeoutChoice != null)
            {
                long i = request.TimeoutChoice.Value;
                return new AnswerValue(this, null, i, request.Choices[(int)i], null);
            }
            return new AnswerValue(this, request.ResponseNames[(int)request.TimeoutResponse!.Value], null, null, null);
        }

        private List<Request> BlockingRequests()
        {
            var blocking = new List<Request>();
            foreach (var r in OpenRequests)
                if (r.BlocksTime) blocking.Add(r);
            blocking.Sort((a, b) => a.Id.CompareTo(b.Id));
            return blocking;
        }

        /// <summary><c>now()</c>: 지금 틱. 읽으면 "틱" 칸을 읽은 것으로 기록한다 (G155).</summary>
        private Value NowBuiltin(IReadOnlyList<Value> positional)
        {
            if (positional.Count != 0)
                throw new ScriptRuntimeException(Codes.RunScript, "now() takes no arguments, got " + positional.Count);
            var value = new IntValue(_tick);
            _trace?.Add(ExplainNode.Time(value));
            if (_whenReads != null) _whenReadsTick = true; // when 조건이 틱을 읽었다: 틱마다 다시 본다
            return value;
        }
    }
}
