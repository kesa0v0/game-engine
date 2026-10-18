using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 멈춘 Flow와 질문 (spec S2 평가 모델·X1, spec-gaps G136·G138). 커맨드 본문이 문장 수준의 <c>ask</c>에서 멈추면 거기까지가 한
    /// 트랜잭션이고(S5), 멈춘 실행(프레임 스택)이 질문을 들고 남는다. 답이 오면 그 실행의 사본을 이어서 실행하고, 오류가 나면
    /// 그 구간의 효과를 되감아 Flow와 질문이 답하기 전으로 돌아간다 (C8·C9).
    /// </summary>
    public sealed partial class World
    {
        private sealed class FlowRecord
        {
            public FlowRecord(long id, string command, PureEvaluator.Suspended run, Request? request, bool handler, long? wakeAt = null)
            {
                Id = id;
                Command = command;
                Run = run;
                Request = request;
                Handler = handler;
                WakeAt = wakeAt;
            }

            /// <summary><c>wait</c>로 자는 Flow면 깨울 틱. 그때 <see cref="Request"/>는 없다 (X2).</summary>
            public long? WakeAt { get; }

            public long Id { get; }

            /// <summary>Flow를 만든 커맨드 id, 또는 핸들러면 핸들러 id (<c>모드id.함수이름</c>, G148).</summary>
            public string Command { get; }

            /// <summary>신호 핸들러가 만든 Flow인가 (S5).</summary>
            public bool Handler { get; }
            public PureEvaluator.Suspended Run { get; }

            /// <summary>질문을 기다리면 그 질문. 자는 Flow면 null.</summary>
            public Request? Request { get; }

            /// <summary>
            /// Flow의 대상: 만들 때 받은 커맨드 인자·핸들러 매개변수 중 엔티티 (S5, spec-gaps G163). 본문이 변수를 바꿔도 그대로다.
            /// </summary>
            public IReadOnlyList<EntityId> Subjects { get; set; } = Array.Empty<EntityId>();

            /// <summary>
            /// 이번 멈춤의 <c>about=</c>(G165). 있으면 <see cref="Subjects"/> 대신 이것을 본다. 다음 멈춤에서는 그 멈춤이 정한다.
            /// </summary>
            public IReadOnlyList<EntityId>? About { get; set; }

            /// <summary><c>advance(n)</c>에서 멈췄으면 n (G167). 같은 호출 안에서 진행하고 이어 가므로 저장되지 않는다.</summary>
            public long? AdvanceBy { get; set; }

            /// <summary>이 Flow의 난수열 (G182). 이어 갈 때 사본으로 잇는다: 그 구간이 실패하면 멈춘 때의 n으로 남는다.</summary>
            public RandStream? Rand { get; set; }
        }

        /// <summary>멈춘 Flow, 번호 순 (실행 순서가 결정론적이다, X1).</summary>
        private readonly SortedDictionary<long, FlowRecord> _flows = new SortedDictionary<long, FlowRecord>();

        private long _nextFlow = 1;
        private long _nextRequest = 1;

        /// <summary>열린 질문 전체, Flow 번호 순 (X1: 모든 Flow의 대기의 합집합).</summary>
        public IReadOnlyList<Request> OpenRequests
        {
            get
            {
                var list = new List<Request>(_flows.Count);
                foreach (var flow in _flows.Values)
                    if (flow.Request != null) list.Add(flow.Request);
                return list;
            }
        }

        /// <summary>새 Flow를 만들어 멈춘 실행과 질문을 담는다. 되돌리기 기록에 남는다.</summary>
        /// <returns>연 질문. <c>wait</c>로 잠들었으면 null.</returns>
        private Request? OpenFlow(string command, PureEvaluator.Suspended run, IReadOnlyList<EntityId> subjects, bool handler = false)
        {
            long id = _nextFlow;
            _nextFlow++;
            Journal(() => _nextFlow = id);
            return Park(id, command, run, handler, subjects);
        }

        /// <summary>
        /// Flow <paramref name="id"/>가 <paramref name="run"/>에서 새 질문을 들고 기다리거나(<c>ask</c>) 깨울 틱까지 잔다(<c>wait</c>).
        /// </summary>
        private Request? Park(long id, string command, PureEvaluator.Suspended run, bool handler, IReadOnlyList<EntityId> subjects)
        {
            if (run.Request is AdvancePayload advance)
            {
                // 이 호출이 끝나기 전에 시간을 흘리고 이어 간다 (G167). 저장되는 일은 없다
                _flows.Add(id, new FlowRecord(id, command, run, null, handler) { Rand = _rand?.Copy(), Subjects = subjects, AdvanceBy = advance.Ticks });
                _advancers.Add(id);
                Journal(() =>
                {
                    _flows.Remove(id);
                    _advancers.Remove(id);
                });
                return null;
            }
            if (run.Request is WaitPayload sleep)
            {
                _flows.Add(id, new FlowRecord(id, command, run, null, handler, _tick + sleep.Ticks) { Rand = _rand?.Copy(), Subjects = subjects, About = sleep.About });
                Journal(() => _flows.Remove(id));
                return null;
            }
            var ask = (AskPayload)run.Request;
            long requestId = _nextRequest;
            _nextRequest++;
            Journal(() => _nextRequest = requestId);
            var request = new Request(requestId, id, command, ask.Who, ask.Title, ask.Choices, ask.Responses, ask.ResponseNames,
                ask.Hint, ask.Data)
            {
                BlocksTime = ask.BlocksTime,
                Deadline = ask.DeadlineIn == null ? (long?)null : _tick + ask.DeadlineIn.Value,
                TimeoutChoice = ask.TimeoutChoice,
                TimeoutResponse = ask.TimeoutResponse,
            };
            _flows.Add(id, new FlowRecord(id, command, run, request, handler) { Rand = _rand?.Copy(), Subjects = subjects, About = ask.About });
            Journal(() => _flows.Remove(id));
            return request;
        }

        /// <summary>지난 확인 뒤에 엔티티가 파괴되었나 (G164).</summary>
        private bool _destroyedSinceSweep;

        /// <summary>
        /// 엔티티가 파괴되었으면 멈춘 Flow 전부의 대상을 보고, 사라졌으면 Flow를 버린다 (S5 on_stale 기본 취소, G163·G164).
        /// 실행 하나(와 그 신호 처리)가 끝난 뒤, 트랜잭션 밖에서 부른다. 오류가 아니라 <see cref="ReactionOutcome.Canceled"/>로 남긴다.
        /// </summary>
        private void SweepStale(List<Reaction> reactions)
        {
            if (!_destroyedSinceSweep) return;
            _destroyedSinceSweep = false;
            var stale = new List<KeyValuePair<FlowRecord, string>>();
            foreach (var flow in _flows.Values)
            {
                var reason = StaleReason(flow);
                if (reason != null) stale.Add(new KeyValuePair<FlowRecord, string>(flow, reason));
            }
            foreach (var pair in stale)
            {
                var flow = pair.Key;
                DropFlow(flow);
                reactions.Add(new Reaction(Reaction.StaleSignal, new SortedDictionary<string, Value>(StringComparer.Ordinal),
                    flow.Command, 0, ReactionOutcome.Canceled)
                {
                    Flow = flow.Id,
                    Message = "flow " + flow.Id.ToString(CultureInfo.InvariantCulture) + " was canceled: " + pair.Value,
                });
            }
        }

        /// <summary>
        /// 호출이 끝났을 때 Flow <paramref name="id"/>가 들고 있는 질문. 끝났거나 잠들었거나 대상 소멸로 취소되었으면(G164) null.
        /// <c>advance</c> 뒤 같은 호출 안에서 연 질문도 여기 나온다 (G167).
        /// </summary>
        private Request? HeldRequest(long id) => _flows.TryGetValue(id, out var f) ? f.Request : null;

        /// <summary>입구의 인자 중 엔티티, 처음 나온 순서로 한 번씩 (G163).</summary>
        private static List<EntityId> SubjectsOf(IEnumerable<Value> args)
        {
            var subjects = new List<EntityId>();
            foreach (var v in args)
                if (v is IEntityReference e && !subjects.Contains(e.EntityId)) subjects.Add(e.EntityId);
            return subjects;
        }

        /// <summary>
        /// Flow의 대상이 사라졌으면 그 이유, 아니면 null (S5, G163): 대상 중 하나가 사라졌거나, 질문을 든 Flow의 답할 엔티티가 모두 사라졌다.
        /// </summary>
        private string? StaleReason(FlowRecord flow)
        {
            foreach (var id in flow.About ?? flow.Subjects)
                if (!IsAlive(id)) return "its subject " + id + " was destroyed";
            if (flow.Request != null)
            {
                foreach (var id in flow.Request.Who)
                    if (IsAlive(id)) return null;
                return "everyone who could answer request " + flow.Request.Id.ToString(CultureInfo.InvariantCulture) + " was destroyed";
            }
            return null;
        }

        /// <summary>Flow <paramref name="id"/>가 <c>wait</c>로 자고 있으면 깨울 틱 (G158). 질문을 들었거나 끝났으면 null.</summary>
        private long? SleepsUntil(long id) => _flows.TryGetValue(id, out var f) ? f.WakeAt : null;

        private void DropFlow(FlowRecord flow)
        {
            _flows.Remove(flow.Id);
            Journal(() => _flows[flow.Id] = flow);
        }

        /// <summary>
        /// 열린 질문 <paramref name="request"/>에 엔티티 <paramref name="by"/>로서 선택지 <paramref name="choice"/>를 골라 답한다
        /// (spec X1, spec-gaps G126). 질문 확인 → 목록 → 입구 <c>may_act(issuer, by)</c> → 선택지 범위 순서이고, 통과하면 기다리던 Flow를
        /// 이어서 실행한다. 거절과 오류는 상태를 건드리지 않는다 (C8·C9·C10).
        /// </summary>
        public AnswerResult Answer(string issuer, long request, EntityId by, long choice)
        {
            NoHostWrites("Answer");
            var flow = FindFlow(request);
            if (flow == null) return Unknown(issuer, request);

            var open = flow.Request!;
            var refused = Gate(issuer, open, by);
            if (refused != null) return refused;

            if (choice < 0 || choice >= open.Choices.Count)
            {
                return new AnswerResult(request, issuer, AnswerOutcome.BadChoice,
                    "request " + request.ToString(CultureInfo.InvariantCulture) + " has " + open.Choices.Count +
                    " choices, so " + choice.ToString(CultureInfo.InvariantCulture) + " is out of range");
            }

            try
            {
                IReadOnlyList<Diagnostic> warnings;
                var outerWarnings = OpenWarnings();
                try
                {
                    Atomically(() => Continue(flow, new AnswerValue(this, null, choice, open.Choices[(int)choice], by)));
                }
                finally
                {
                    warnings = CloseWarnings(outerWarnings);
                }
                return new AnswerResult(request, issuer, AnswerOutcome.Succeeded)
                {
                    Reactions = AfterRun(), OpenedRequest = HeldRequest(flow.Id), SleepsUntil = SleepsUntil(flow.Id),
                    Warnings = warnings,
                };
            }
            catch (WorldException e)
            {
                return new AnswerResult(request, issuer, AnswerOutcome.ScriptError) { Error = e };
            }
        }

        /// <summary>
        /// 열린 질문 <paramref name="request"/>에 응답 커맨드 <paramref name="command"/>로 답한다 (spec X1·S6, spec-gaps G139).
        /// 질문 확인 → 그 커맨드를 받나 → 인자(G115) → <c>acts_for</c> → 목록 → <c>may_act</c> → 사전 조건 → 본문 순서이고, 본문은
        /// 기다리던 Flow를 이어서 실행하는 것과 같은 트랜잭션이다. 끝나면 <c>ask</c>가 그 커맨드 이름과 답한 엔티티를 돌려준다.
        /// 없는 커맨드 id만 호출자 오류(<see cref="WorldException"/>)다.
        /// </summary>
        public AnswerResult Respond(string issuer, long request, string command, IReadOnlyDictionary<string, Value> args)
        {
            NoHostWrites("Respond");
            if (!_commands.TryGetValue(command, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + command + "'",
                    Suggest.Hint(command, _commands.Keys));
            }
            var flow = FindFlow(request);
            if (flow == null) return Unknown(issuer, request);
            var open = flow.Request!;
            int accepted = -1;
            for (int i = 0; i < open.Responses.Count; i++)
                if (open.Responses[i] == command) accepted = i;
            if (accepted < 0)
            {
                return new AnswerResult(request, issuer, AnswerOutcome.NotAccepted,
                    "request " + request.ToString(CultureInfo.InvariantCulture) + " does not accept " + command +
                    ", it accepts: [" + string.Join(", ", open.Responses) + "]" + (open.Choices.Count > 0 ? " and choices" : ""));
            }

            var rejections = new List<ArgumentRejection>();
            var checkedArgs = CheckArguments(decl, args, rejections);
            if (rejections.Count > 0)
                return new AnswerResult(request, issuer, AnswerOutcome.ArgumentsRejected) { ArgumentRejections = rejections };

            EntityId by;
            try
            {
                if (!(ActsFor(decl, checkedArgs) is EntityValue chosen))
                {
                    throw new WorldException(Codes.RunType,
                        "computing acts_for of " + decl.Id + ": a response must come from an entity, not None",
                        "spec-gaps G139");
                }
                by = chosen.Id;
            }
            catch (WorldException e)
            {
                return new AnswerResult(request, issuer, AnswerOutcome.ScriptError) { Arguments = checkedArgs, Error = e };
            }
            var refused = Gate(issuer, open, by);
            if (refused != null) return refused;

            try
            {
                PreconditionReport report;
                IReadOnlyList<Diagnostic> warnings;
                var outerWarnings = OpenWarnings();
                try
                {
                    report = Atomically(() =>
                    {
                        var r = EvaluateRequires(decl, checkedArgs, runBody: true, out var inner);
                        if (!r.Passed) return r; // 본문이 돌지 않았다: 쓴 것이 없다
                        if (inner != null)
                        {
                            string at = inner.Request is WaitPayload ? "wait()" : inner.Request is AdvancePayload ? "advance()" : "ask()";
                            throw new WorldException(Codes.RunScript,
                                "the response command " + command + " stopped at " + at + ", which is not supported yet",
                                "ask from the command that is waiting instead (spec-gaps G139)");
                        }
                        Continue(flow, new AnswerValue(this, open.ResponseNames[accepted], null, null, by));
                        return r;
                    });
                }
                finally
                {
                    warnings = CloseWarnings(outerWarnings);
                }
                var outcome = report.Passed ? AnswerOutcome.Succeeded : AnswerOutcome.PreconditionsFailed;
                return new AnswerResult(request, issuer, outcome)
                {
                    Arguments = checkedArgs, Preconditions = report, Reactions = AfterRun(),
                    OpenedRequest = HeldRequest(flow.Id), SleepsUntil = SleepsUntil(flow.Id),
                    Warnings = warnings,
                };
            }
            catch (WorldException e)
            {
                return new AnswerResult(request, issuer, AnswerOutcome.ScriptError) { Arguments = checkedArgs, Error = e };
            }
        }

        private FlowRecord? FindFlow(long request)
        {
            foreach (var f in _flows.Values)
                if (f.Request != null && f.Request.Id == request) return f;
            return null;
        }

        private static AnswerResult Unknown(string issuer, long request) =>
            new AnswerResult(request, issuer, AnswerOutcome.UnknownRequest,
                "there is no open request " + request.ToString(CultureInfo.InvariantCulture) + "; it may have been answered already");

        /// <summary>답하는 엔티티가 목록에 있고 정책이 허용하는가 (G125·G126). 통과하면 null.</summary>
        private AnswerResult? Gate(string issuer, Request open, EntityId by)
        {
            bool listed = false;
            foreach (var id in open.Who) listed |= id == by;
            string number = open.Id.ToString(CultureInfo.InvariantCulture);
            if (!listed)
            {
                return new AnswerResult(open.Id, issuer, AnswerOutcome.NotListed,
                    "entity " + by + " cannot answer request " + number + ", allowed: [" + string.Join(", ", open.Who) + "]");
            }
            try
            {
                if (!MayAct(issuer, new EntityValue(by), "request " + number))
                    return new AnswerResult(open.Id, issuer, AnswerOutcome.GateDenied,
                        "the submit policy does not let '" + issuer + "' act as " + by);
            }
            catch (WorldException e)
            {
                return new AnswerResult(open.Id, issuer, AnswerOutcome.ScriptError) { Error = e };
            }
            return null;
        }

        /// <summary>Flow를 답으로 이어서 실행한다 (한 트랜잭션, S5). 다시 멈추면 같은 Flow가 새 질문을 든다.</summary>
        private Request? Continue(FlowRecord flow, Value answer, string after = "after an answer")
        {
            DropFlow(flow);
            _rand = flow.Rand?.Copy(); // 같은 난수열을 잇는다 (spec S2, G182)
            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_actionStepBudget);
            try
            {
                var next = Evaluator.Resume(flow.Run, answer, _meter);
                return next == null ? null : Park(flow.Id, flow.Command, next, flow.Handler, flow.Subjects);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, (flow.Handler ? "the handler " : "the command ") + flow.Command + " (" + after + ")");
            }
            finally
            {
                if (outermost && CloseActionMeter() is var heavy && heavy > 0)
                    WarnHeavy(heavy, (flow.Handler ? "the handler " : "the command ") + flow.Command + " (" + after + ")", SourceSpan.None);
            }
        }
    }
}
