using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 열린 질문 (spec X1, spec-gaps G125·G126·G137·G138). 멈춘 Flow 하나가 질문 하나를 들고 있다. 값은 모두 저장 가능한 모양이다
    /// (엔티티는 <see cref="EntityValue"/>).
    /// </summary>
    public sealed class Request
    {
        internal Request(long id, long flow, string command, IReadOnlyList<EntityId> who, Value title,
            IReadOnlyList<Value> choices, IReadOnlyList<string> responses, IReadOnlyList<string> responseNames,
            string? hint, Value data)
        {
            Id = id;
            Flow = flow;
            Command = command;
            Who = who;
            Title = title;
            Choices = choices;
            Responses = responses;
            ResponseNames = responseNames;
            Hint = hint;
            Data = data;
        }

        /// <summary>받는 응답 커맨드의 id (<c>모드id.함수이름</c>, G138·G139). 응답은 <see cref="World.Respond"/>로 낸다.</summary>
        public IReadOnlyList<string> Responses { get; }

        /// <summary><see cref="Responses"/>를 <c>ask</c>에 적은 그대로 (<c>r.response</c>로 돌려준다).</summary>
        internal IReadOnlyList<string> ResponseNames { get; }

        /// <summary>질문 번호. 시뮬레이션 상태 안의 카운터다 (X1). 답은 이 번호로 낸다.</summary>
        public long Id { get; }

        /// <summary>질문을 들고 멈춘 Flow의 번호.</summary>
        public long Flow { get; }

        /// <summary>Flow를 만든 커맨드 id, 또는 신호 핸들러가 만든 Flow면 핸들러 id (둘 다 <c>모드id.함수이름</c>, S5).</summary>
        public string Command { get; }

        /// <summary>답할 수 있는 엔티티 (G125). 질문이 열릴 때 굳는다.</summary>
        public IReadOnlyList<EntityId> Who { get; }

        /// <summary>제목: 텍스트 키(<see cref="StrValue"/>)나 <see cref="TextValue"/>.</summary>
        public Value Title { get; }

        /// <summary>고를 값들 (G138). 어떻게 보일지는 UI가 정한다 (G137).</summary>
        public IReadOnlyList<Value> Choices { get; }

        /// <summary>선택 방법의 제안. 커널은 해석하지 않는다 (G137).</summary>
        public string? Hint { get; }

        /// <summary>UI가 읽을 자유 데이터 (<see cref="DictValue"/>, 없으면 <see cref="NoneValue"/>, G137).</summary>
        public Value Data { get; }

        /// <summary>열려 있는 동안 <see cref="World.Advance"/>가 시간을 흘리지 않는다 (spec X2, spec-gaps G159).</summary>
        public bool BlocksTime { get; internal set; } = true;

        /// <summary>마감 틱 (spec X2, spec-gaps G160). 질문이 열린 틱 + <c>deadline=</c>. 마감이 없으면 null.</summary>
        public long? Deadline { get; internal set; }

        /// <summary>마감이 지나면 엔진이 고를 선택지 번호 (<c>on_timeout=</c>, spec-gaps G161). 답한 엔티티는 없다.</summary>
        public long? TimeoutChoice { get; internal set; }

        /// <summary>마감이 지나면 엔진이 낼 응답의 <see cref="Responses"/> 번호 (G161). 응답 커맨드의 본문은 돌지 않는다.</summary>
        public long? TimeoutResponse { get; internal set; }

        /// <summary>마감이 지나면 기본 답이 들어간다.</summary>
        public bool TimesOut => Deadline != null && (TimeoutChoice != null || TimeoutResponse != null);
    }

    /// <summary>답이 어디서 끝났나 (spec X1·C9). 앞의 단계에서 멈추면 상태는 그대로다.</summary>
    public enum AnswerOutcome
    {
        /// <summary>답을 받아 Flow를 다음 막힘이나 끝까지 실행했다. 다시 멈췄으면 <see cref="AnswerResult.OpenedRequest"/>가 있다.</summary>
        Succeeded,

        /// <summary>그런 번호의 열린 질문이 없다: 이미 답했거나 없는 번호다 (C9).</summary>
        UnknownRequest,

        /// <summary>이 질문은 그 응답 커맨드를 받지 않는다 (<c>responses=</c>에 없다, G139).</summary>
        NotAccepted,

        /// <summary>응답 커맨드의 인자가 매개변수 타입에 맞지 않는다 (G115). <see cref="AnswerResult.ArgumentRejections"/>.</summary>
        ArgumentsRejected,

        /// <summary>그 엔티티는 이 질문에 답할 수 있는 목록에 없다 (G125). 응답 커맨드면 <c>acts_for</c>가 고른 엔티티다.</summary>
        NotListed,

        /// <summary>정책 함수가 이 issuer가 그 엔티티로 답하는 것을 막았다 (G126).</summary>
        GateDenied,

        /// <summary>고른 번호가 선택지 범위 밖이다.</summary>
        BadChoice,

        /// <summary>응답 커맨드의 <c>require</c>가 하나 이상 실패했다 (G110). <see cref="AnswerResult.Preconditions"/>.</summary>
        PreconditionsFailed,

        /// <summary>이어서 실행하다 스크립트 오류가 났다. 효과는 남지 않고 질문은 그대로 열려 있다 (C8·C9).</summary>
        ScriptError,
    }

    /// <summary>답 하나의 결과. 제출(<see cref="SubmitResult"/>)처럼 입력과 모드 버그로 생기는 결과는 예외가 아니다.</summary>
    public sealed class AnswerResult
    {
        internal AnswerResult(long request, string issuer, AnswerOutcome outcome, string? message = null)
        {
            Request = request;
            Issuer = issuer;
            Outcome = outcome;
            Message = message;
        }

        public long Request { get; }
        public string Issuer { get; }
        public AnswerOutcome Outcome { get; }
        public bool Succeeded => Outcome == AnswerOutcome.Succeeded;

        /// <summary>거절 이유 (개발자용, 영어). 성공이면 없다.</summary>
        public string? Message { get; }

        /// <summary>스크립트 오류 (<see cref="AnswerOutcome.ScriptError"/>일 때만).</summary>
        public WorldException? Error { get; internal set; }

        /// <summary>이어서 실행하다 다시 멈춰 연 질문.</summary>
        public Request? OpenedRequest { get; internal set; }

        /// <summary>이어서 실행하다 <c>wait</c>로 잠들었으면 깨울 틱 (G158).</summary>
        public long? SleepsUntil { get; internal set; }

        /// <summary>응답 커맨드의 기본값까지 채운 전체 인자 (G116). 인자 검사를 통과했을 때만 있다.</summary>
        public IReadOnlyDictionary<string, Value>? Arguments { get; internal set; }

        /// <summary>이어서 실행한 구간(과 응답 커맨드 본문)이 낸 신호로 돈 핸들러들, 돈 순서대로 (spec S5, spec-gaps G148).</summary>
        public IReadOnlyList<Reaction> Reactions { get; internal set; } = Array.Empty<Reaction>();

        /// <summary>응답 커맨드의 인자 거절 (G115).</summary>
        public IReadOnlyList<ArgumentRejection> ArgumentRejections { get; internal set; } = Array.Empty<ArgumentRejection>();

        /// <summary>응답 커맨드의 사전 검사 결과. 입구를 통과했을 때만 있다.</summary>
        public PreconditionReport? Preconditions { get; internal set; }
    }

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
        private Request? OpenFlow(string command, PureEvaluator.Suspended run, bool handler = false)
        {
            long id = _nextFlow;
            _nextFlow++;
            Journal(() => _nextFlow = id);
            return Park(id, command, run, handler);
        }

        /// <summary>
        /// Flow <paramref name="id"/>가 <paramref name="run"/>에서 새 질문을 들고 기다리거나(<c>ask</c>) 깨울 틱까지 잔다(<c>wait</c>).
        /// </summary>
        private Request? Park(long id, string command, PureEvaluator.Suspended run, bool handler)
        {
            if (run.Request is WaitPayload sleep)
            {
                _flows.Add(id, new FlowRecord(id, command, run, null, handler, _tick + sleep.Ticks));
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
            _flows.Add(id, new FlowRecord(id, command, run, request, handler));
            Journal(() => _flows.Remove(id));
            return request;
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
                var opened = Atomically(() => Continue(flow, new AnswerValue(this, null, choice, open.Choices[(int)choice], by)));
                return new AnswerResult(request, issuer, AnswerOutcome.Succeeded)
                {
                    OpenedRequest = opened, SleepsUntil = SleepsUntil(flow.Id), Reactions = RunReactions(),
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
                Request? opened = null;
                var report = Atomically(() =>
                {
                    var r = EvaluateRequires(decl, checkedArgs, runBody: true, out var inner);
                    if (!r.Passed) return r; // 본문이 돌지 않았다: 쓴 것이 없다
                    if (inner != null)
                    {
                        string at = inner.Request is WaitPayload ? "wait()" : "ask()";
                        throw new WorldException(Codes.RunScript,
                            "the response command " + command + " stopped at " + at + ", which is not supported yet",
                            "ask from the command that is waiting instead (spec-gaps G139)");
                    }
                    opened = Continue(flow, new AnswerValue(this, open.ResponseNames[accepted], null, null, by));
                    return r;
                });
                var outcome = report.Passed ? AnswerOutcome.Succeeded : AnswerOutcome.PreconditionsFailed;
                return new AnswerResult(request, issuer, outcome)
                {
                    Arguments = checkedArgs, Preconditions = report, OpenedRequest = opened, SleepsUntil = SleepsUntil(flow.Id),
                    Reactions = RunReactions(),
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
            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_stepBudget);
            try
            {
                var next = Evaluator.Resume(flow.Run, answer, _meter);
                return next == null ? null : Park(flow.Id, flow.Command, next, flow.Handler);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, (flow.Handler ? "the handler " : "the command ") + flow.Command + " (" + after + ")");
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }

        // ---- ask (G138) ----

        private void AddFlowBuiltins(SortedDictionary<string, Value> table)
        {
            table.Add(PureBuiltins.AskName,
                new PureBuiltin(PureBuiltins.AskName, (p, k) => Guard(() => Ask("", p, k)))
                {
                    Writes = true,
                    Suspends = true,
                    CallerBody = (mod, p, k) => Guard(() => Ask(mod, p, k)),
                });
            table.Add(PureBuiltins.WaitName,
                new PureBuiltin(PureBuiltins.WaitName, (p, k) => Wait(p, k)) { Writes = true, Suspends = true });
        }

        /// <summary><c>wait(ticks=n)</c> (spec X2·S5): n은 1 이상. 멈추는 것은 실행기가 하고, 깨어나면 None을 돌려준다.</summary>
        private static Value Wait(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "wait(ticks=3)";
            if (positional.Count > 0 || keywords.Count != 1 || !keywords.TryGetValue("ticks", out var ticks))
                throw new ScriptRuntimeException(Codes.RunScript, "wait() takes only ticks=, like " + usage, "write " + usage);
            if (!(ticks is IntValue n))
                throw new ScriptRuntimeException(Codes.RunType, "ticks= of wait() must be an int, not " + ticks.KindName, "write " + usage);
            if (n.Value < 1)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ticks= of wait() must be at least 1, got " + n.Value.ToString(CultureInfo.InvariantCulture), "spec X2");
            return new WaitPayload(n.Value);
        }

        /// <summary><c>wait</c>가 실행기에 넘기는 것: 잘 틱 수.</summary>
        private sealed class WaitPayload : Value
        {
            public WaitPayload(long ticks) => Ticks = ticks;
            public long Ticks { get; }
            public override string KindName => "wait";
        }

        /// <summary>
        /// <c>ask(who, title, choices=[...], responses=[...], hint=, data=)</c>의 인자를 검사해 질문의 내용을 만든다 (G138).
        /// 멈추는 것은 실행기가 한다. 응답 커맨드 이름은 부른 함수의 모드에서 찾고, 다른 모드의 것은 <c>"모드id.이름"</c>으로 적는다.
        /// </summary>
        private Value Ask(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "ask(who, \"Title_Key\", choices=[...], responses=[...])";
            if (positional.Count != 2)
            {
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() takes who and the title as positional arguments, got " + positional.Count, "write " + usage);
            }

            var who = new List<EntityId>();
            void Answerer(Value v)
            {
                if (!(v is IEntityReference e))
                    throw new ScriptRuntimeException(Codes.RunType,
                        "ask() asks entities, not " + v.KindName, "pass an entity or a list of entities (spec-gaps G125)");
                if (!IsAlive(e.EntityId))
                    throw new ScriptRuntimeException(Codes.RunScript, "ask() cannot ask the destroyed entity " + e.EntityId);
                if (!who.Contains(e.EntityId)) who.Add(e.EntityId);
            }
            if (positional[0] is ListValue list)
            {
                foreach (var item in list.Items) Answerer(item);
                if (who.Count == 0) throw new ScriptRuntimeException(Codes.RunScript, "ask() needs at least one entity to ask");
            }
            else Answerer(positional[0]);

            Value title = positional[1] switch
            {
                StrValue s => s,
                TextValue t => Savable("the title of ask()", t),
                _ => throw new ScriptRuntimeException(Codes.RunType,
                    "the title of ask() must be a text key (str) or text(...), not " + positional[1].KindName, "write " + usage),
            };

            var choices = new List<Value>();
            var responses = new List<string>();
            var responseNames = new List<string>();
            string? hint = null;
            Value data = NoneValue.Instance;
            bool hasChoices = false;
            bool? blocksTime = null; // 적지 않으면 마감 유무로 정한다 (G159)
            long? deadline = null;
            Value? onTimeout = null;
            foreach (var pair in keywords)
            {
                switch (pair.Key)
                {
                    case "choices":
                        if (!(pair.Value is ListValue items) || items.Items.Count == 0)
                            throw new ScriptRuntimeException(Codes.RunType, "choices= of ask() must be a non-empty list",
                                "write " + usage);
                        for (int i = 0; i < items.Items.Count; i++)
                            choices.Add(Savable("choice " + i.ToString(CultureInfo.InvariantCulture) + " of ask()", items.Items[i]));
                        hasChoices = true;
                        break;
                    case "responses":
                        if (!(pair.Value is ListValue names) || names.Items.Count == 0)
                            throw new ScriptRuntimeException(Codes.RunType,
                                "responses= of ask() must be a non-empty list of command names", "write " + usage);
                        foreach (var item in names.Items)
                        {
                            if (!(item is StrValue name))
                                throw new ScriptRuntimeException(Codes.RunType,
                                    "responses= of ask() takes command names (str), not " + item.KindName,
                                    "write responses=[\"Pass\", \"React\"]");
                            string id = ResponseCommand(mod, name.Value);
                            if (responses.Contains(id)) continue;
                            responses.Add(id);
                            responseNames.Add(name.Value);
                        }
                        break;
                    case "hint":
                        if (pair.Value is StrValue h) hint = h.Value;
                        else if (!(pair.Value is NoneValue))
                            throw new ScriptRuntimeException(Codes.RunType, "hint= of ask() must be a str, not " + pair.Value.KindName,
                                "the hint names how the UI should ask, e.g. \"map_click\" (spec-gaps G137)");
                        break;
                    case "data":
                        if (!(pair.Value is DictValue) && !(pair.Value is NoneValue))
                            throw new ScriptRuntimeException(Codes.RunType, "data= of ask() must be a dict, not " + pair.Value.KindName,
                                "write data={\"amount\": 2} (spec-gaps G137)");
                        data = Savable("data= of ask()", pair.Value);
                        break;
                    case "blocks_time":
                        if (!(pair.Value is BoolValue b))
                            throw new ScriptRuntimeException(Codes.RunType,
                                "blocks_time= of ask() must be True or False, not " + pair.Value.KindName,
                                "True stops time until it is answered (spec-gaps G159)");
                        blocksTime = b.Value;
                        break;
                    case "deadline":
                        if (!(pair.Value is IntValue n) || n.Value < 1)
                            throw new ScriptRuntimeException(Codes.RunType,
                                "deadline= of ask() must be a number of ticks of at least 1, not " + Show(pair.Value),
                                "deadline=3 closes it 3 ticks after it opens; for a fixed tick write deadline=tick - now() (spec-gaps G160)");
                        deadline = n.Value;
                        break;
                    case "on_timeout":
                        onTimeout = pair.Value;
                        break;
                    default:
                        throw new ScriptRuntimeException(Codes.RunScript, "ask() has no argument '" + pair.Key + "'",
                            Suggest.Hint(pair.Key, new[] { "choices", "responses", "hint", "data", "blocks_time", "deadline", "on_timeout" }));
                }
            }
            long? timeoutChoice = null, timeoutResponse = null;
            if (onTimeout != null) TimeoutAnswer(mod, onTimeout, deadline, choices, responses, out timeoutChoice, out timeoutResponse);
            else if (deadline != null)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() has a deadline but no on_timeout=, so nothing says what happens when the deadline passes",
                    "add on_timeout= with the choice or response to give then, e.g. a choice that does nothing (spec-gaps G162)");
            if (!hasChoices && responses.Count == 0)
                throw new ScriptRuntimeException(Codes.RunScript, "ask() needs choices=[...] or responses=[...]", "write " + usage);
            if (blocksTime == true && deadline != null)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() has blocks_time=True and a deadline, but time stops while it is open, so the deadline is never reached",
                    "drop blocks_time=True to let time flow until the deadline (spec X2, spec-gaps G159)");
            return new AskPayload(who, title, choices, responses, responseNames, hint, data)
            {
                BlocksTime = blocksTime ?? deadline == null,
                DeadlineIn = deadline,
                TimeoutChoice = timeoutChoice,
                TimeoutResponse = timeoutResponse,
            };
        }

        /// <summary>
        /// <c>on_timeout=</c>이 가리키는 선택지 번호나 응답 번호 (G161). 선택지 값(같은 값이 여럿이면 앞의 것)이나 응답 이름이어야 하고,
        /// 둘 다에 맞으면 어느 쪽인지 모호하다. 마감이 없으면 닿지 않는다.
        /// </summary>
        private void TimeoutAnswer(string mod, Value onTimeout, long? deadline, List<Value> choices, List<string> responses,
            out long? choice, out long? response)
        {
            choice = null;
            response = null;
            if (deadline == null)
                throw new ScriptRuntimeException(Codes.RunScript, "ask() has on_timeout= but no deadline, so it never times out",
                    "add deadline=n, the number of ticks before the default answer is given (spec-gaps G160, G161)");
            var value = Savable("on_timeout= of ask()", onTimeout);
            for (int i = 0; i < choices.Count && choice == null; i++)
                if (PureOps.ValuesEqual(choices[i], value)) choice = i;
            if (value is StrValue name)
            {
                string id = name.Value.IndexOf('.') >= 0 ? name.Value : mod + "." + name.Value;
                int at = responses.IndexOf(id);
                if (at >= 0) response = at;
            }
            if (choice != null && response != null)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "on_timeout=\"" + ((StrValue)value).Value + "\" of ask() is both a choice and a response, so the default answer is unclear",
                    "rename the choice or the response command (spec-gaps G161)");
            if (choice == null && response == null)
            {
                var names = new List<string>();
                foreach (var c in choices) if (c is StrValue s) names.Add(s.Value);
                foreach (var r in responses) names.Add(r.Substring(r.IndexOf('.') + 1));
                throw new ScriptRuntimeException(Codes.RunScript,
                    "on_timeout= of ask() must be one of its choices or responses, not " + Show(value),
                    value is StrValue missing ? Suggest.Hint(missing.Value, names) : "the default answer is given when the deadline passes (spec-gaps G161)");
            }
        }

        /// <summary>응답 커맨드 이름을 커맨드 id로: 점이 없으면 부른 함수의 모드에서 찾는다. 자유 커맨드는 답이 될 수 없다.</summary>
        private string ResponseCommand(string mod, string name)
        {
            string id = name.IndexOf('.') >= 0 ? name : mod + "." + name;
            if (!_commands.TryGetValue(id, out var decl))
            {
                throw new ScriptRuntimeException(Codes.RunUnknownName, "ask() got responses=['" + name + "'], but there is no command '" + id + "'",
                    Suggest.Hint(id, _commands.Keys));
            }
            if (decl.Free)
            {
                throw new ScriptRuntimeException(Codes.RunScript,
                    "ask() got responses=['" + name + "'], but " + id + " is a free command, so it cannot answer a question",
                    "drop free=True from it, or submit it on its own (spec S6)");
            }
            return id;
        }

        /// <summary>질문에 담아 저장할 수 있는 값으로 (spec S2 표: Request 값은 저장되므로 정규형). 엔티티는 id로 바꾼다.</summary>
        private Value Savable(string display, Value value)
        {
            switch (value)
            {
                case NoneValue _:
                case IntValue _:
                case FixedValue _:
                case BoolValue _:
                case StrValue _:
                    return value;
                case IEntityReference e:
                    return new EntityValue(e.EntityId);
                case TextValue t:
                    var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in t.Args) args[pair.Key] = Savable(display, pair.Value);
                    return new TextValue(t.Key, args);
                case ListValue list:
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items) items.Add(Savable(display, item));
                    return new ListValue(items);
                case DictValue dict:
                    var entries = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items) entries.Add(new DictItem(item.Key, Savable(display, item.Value), item.Span));
                    return new DictValue(entries);
            }
            throw new ScriptRuntimeException(Codes.RunType, display + " cannot hold " + value.KindName,
                "a question is saved, so it holds numbers, str, text(...), entities and lists or dicts of them");
        }

        /// <summary><c>ask</c>가 실행기에 넘기는 질문의 내용. 실행기는 이것을 들고 멈추기만 한다.</summary>
        private sealed class AskPayload : Value
        {
            public AskPayload(IReadOnlyList<EntityId> who, Value title, IReadOnlyList<Value> choices,
                IReadOnlyList<string> responses, IReadOnlyList<string> responseNames, string? hint, Value data)
            {
                Who = who;
                Title = title;
                Choices = choices;
                Responses = responses;
                ResponseNames = responseNames;
                Hint = hint;
                Data = data;
            }

            public IReadOnlyList<EntityId> Who { get; }
            public Value Title { get; }
            public IReadOnlyList<Value> Choices { get; }
            public IReadOnlyList<string> Responses { get; }
            public IReadOnlyList<string> ResponseNames { get; }
            public string? Hint { get; }
            public Value Data { get; }
            public bool BlocksTime { get; set; } = true;

            /// <summary>열린 뒤 몇 틱 뒤가 마감인가 (G160). 질문이 열릴 때 틱으로 굳는다.</summary>
            public long? DeadlineIn { get; set; }

            public long? TimeoutChoice { get; set; }
            public long? TimeoutResponse { get; set; }

            public override string KindName => "question";
        }

        /// <summary><c>ask</c>가 돌려주는 값 (G138): <c>r.response</c>·<c>r.index</c>·<c>r.choice</c>·<c>r.by</c>.</summary>
        private sealed class AnswerValue : Value, IRuntimeAttributes, IValueEquality
        {
            /// <summary>같은 응답·번호·선택지·답한 엔티티면 같다 (G142).</summary>
            public bool ValueEquals(Value other) =>
                other is AnswerValue a && a._response == _response && a._index == _index && a._by == _by &&
                (a._choice == null ? _choice == null : _choice != null && PureOps.ValuesEqual(a._choice, _choice));

            private readonly World _world;
            private readonly string? _response;
            private readonly long? _index;
            private readonly Value? _choice;
            private readonly EntityId? _by;

            public AnswerValue(World world, string? response, long? index, Value? choice, EntityId? by)
            {
                _world = world;
                _response = response;
                _index = index;
                _choice = choice;
                _by = by;
            }

            public override string KindName => "answer";

            public string? Response => _response;
            public long? Index => _index;
            public Value? Choice => _choice;
            public EntityId? By => _by;

            public Value GetAttribute(string name)
            {
                switch (name)
                {
                    case "response": return _response == null ? NoneValue.Instance : new StrValue(_response);
                    case "index": return _index == null ? NoneValue.Instance : new IntValue(_index.Value);
                    case "choice": return _choice == null ? NoneValue.Instance : _world.ToScript(_choice);
                    case "by": return _by == null ? NoneValue.Instance : _world.ToScript(new EntityValue(_by.Value));
                }
                throw new ScriptRuntimeException(Codes.RunScript, "an answer has no attribute '" + name + "'",
                    Suggest.Hint(name, new[] { "response", "index", "choice", "by" }) ?? "it has response, index, choice and by");
            }
        }
    }
}
