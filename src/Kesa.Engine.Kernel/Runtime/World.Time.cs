using System;
using System.Collections.Generic;
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
        /// 다 진행했으면 비어 있다.
        /// </summary>
        public IReadOnlyList<Request> BlockedBy { get; internal set; } = Array.Empty<Request>();

        public bool Blocked => BlockedBy.Count > 0;
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
            if (_undo != null || _meter != null)
                throw new InvalidOperationException("time cannot advance while a command or a function is running");
            var reactions = new List<Reaction>();
            var tick = _schema.TryGetSignal(EngineSignals.Tick, out var t) ? t : null;
            for (int i = 0; i < ticks; i++)
            {
                // 매 틱 전에 다시 본다: 앞 틱에서 열린 질문도 막는다 (X2)
                var blocking = BlockingRequests();
                if (blocking.Count > 0) return new AdvanceResult(i, _tick, reactions) { BlockedBy = blocking };
                _tick++;
                WakeSleepers(reactions);
                // 엔진이 낸 신호는 1세대다. 핸들러마다 각자 트랜잭션이라 핸들러가 실패해도 틱은 진행된다 (S5)
                if (tick != null)
                {
                    _emitted.Add(new Emitted(new SignalValue(this, tick, new List<KeyValuePair<SignalField, Value>>()), 1, null, null));
                    reactions.AddRange(RunReactions());
                }
            }
            return new AdvanceResult(ticks, _tick, reactions);
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
                try
                {
                    var opened = asked == null
                        ? Atomically(() => Continue(flow, NoneValue.Instance, "after waking"))
                        : Atomically(() => Continue(flow, TimeoutAnswer(asked), "after its deadline"));
                    bool asleep = _flows.ContainsKey(flow.Id);
                    reactions.Add(new Reaction(signal, values, flow.Command, 0,
                        asleep ? ReactionOutcome.Suspended : ReactionOutcome.Finished)
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
                reactions.AddRange(RunReactions());
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
            return value;
        }
    }
}
