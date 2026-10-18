using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>제출이 어디서 끝났나 (spec S6, spec-gaps G131). 앞의 단계에서 멈추면 뒤 단계는 돌지 않는다.</summary>
    public enum SubmitOutcome
    {
        /// <summary>
        /// 모든 검사를 통과하고 본문을 실행했다 (G132). 본문이 <c>ask</c>에서 멈췄으면 <see cref="SubmitResult.OpenedRequest"/>가 있다.
        /// </summary>
        Succeeded,

        /// <summary>자유 커맨드(<c>free=True</c>)가 아니라 Request 없이 낼 수 없다 (S6 "기본 거부").</summary>
        NeedsRequest,

        /// <summary>인자가 매개변수 타입에 맞지 않는다 (G115). <see cref="SubmitResult.ArgumentRejections"/>.</summary>
        ArgumentsRejected,

        /// <summary>정책 함수가 이 issuer가 그 대상으로 행동하는 것을 막았다 (G122).</summary>
        GateDenied,

        /// <summary><c>require</c>가 하나 이상 실패했다 (G110). <see cref="SubmitResult.Preconditions"/>.</summary>
        PreconditionsFailed,

        /// <summary>스크립트 오류 (모드 버그). 효과는 남지 않는다 (C8). <see cref="SubmitResult.Error"/>.</summary>
        ScriptError,
    }

    /// <summary>
    /// 제출 하나의 결과 (spec S6, spec-gaps G131). 제출자의 입력과 모드 버그로 생기는 결과는 모두 여기에 담기고 예외가 아니다.
    /// 거절·오류는 상태와 난수를 건드리지 않는다 (C7·C8·C10).
    /// </summary>
    public sealed class SubmitResult
    {
        internal SubmitResult(string command, string issuer, SubmitOutcome outcome)
        {
            Command = command;
            Issuer = issuer;
            Outcome = outcome;
        }

        /// <summary>커맨드 id (<c>모드id.함수이름</c>, G109).</summary>
        public string Command { get; }

        /// <summary>호스트가 붙인 제출자 꼬리표 (G119). 엔진은 검증하지 않고 받은 그대로 쓴다.</summary>
        public string Issuer { get; }

        public SubmitOutcome Outcome { get; }

        public bool Succeeded => Outcome == SubmitOutcome.Succeeded;

        /// <summary>
        /// 기본값까지 채우고 타입에 맞춘 전체 인자 (G116). 인자 검사를 통과했을 때만 있다. 커맨드 로그에 남길 인자가 이것이다.
        /// </summary>
        public IReadOnlyDictionary<string, Value>? Arguments { get; internal set; }

        /// <summary>인자 거절 (G115). <see cref="SubmitOutcome.ArgumentsRejected"/>일 때만 비어 있지 않다.</summary>
        public IReadOnlyList<ArgumentRejection> ArgumentRejections { get; internal set; } = Array.Empty<ArgumentRejection>();

        /// <summary>
        /// <c>acts_for</c>가 고른 대상: 엔티티(<see cref="EntityValue"/>)나 <see cref="NoneValue"/>(<c>acts_for=None</c>).
        /// 입구까지 왔을 때만 있다.
        /// </summary>
        public Value? Target { get; internal set; }

        /// <summary>사전 검사 결과. 입구를 통과했을 때만 있다.</summary>
        public PreconditionReport? Preconditions { get; internal set; }

        /// <summary>스크립트 오류 (<see cref="SubmitOutcome.ScriptError"/>일 때만). 위치와 호출 흔적이 메시지에 있다.</summary>
        public WorldException? Error { get; internal set; }

        /// <summary>
        /// 본문이 <c>ask</c>에서 멈춰 연 질문 (spec X1). 커맨드는 거기까지 실행되어 효과가 남았고, 나머지는 답이 오면 이어서 실행된다.
        /// </summary>
        public Request? OpenedRequest { get; internal set; }

        /// <summary>
        /// 본문이 <c>wait</c>로 잠들었으면 깨울 틱 (spec X2, spec-gaps G158). 효과는 거기까지 남았고 결과는 <see cref="SubmitOutcome.Succeeded"/>다.
        /// </summary>
        public long? SleepsUntil { get; internal set; }

        /// <summary>
        /// 본문이 낸 신호로 돈 핸들러들, 돈 순서대로 (spec S5, spec-gaps G148). 핸들러가 실패해도 <see cref="Outcome"/>은 그대로다.
        /// </summary>
        public IReadOnlyList<Reaction> Reactions { get; internal set; } = Array.Empty<Reaction>();

        /// <summary>
        /// 실패하지 않은 경고 (spec-gaps G225): 사전 조건과 본문이 읽기 예산보다 많은 단계를 썼으면 <c>RUN021</c>. 핸들러의 경고는 각
        /// <see cref="Reaction"/>에 있다.
        /// </summary>
        public IReadOnlyList<Diagnostic> Warnings { get; internal set; } = Array.Empty<Diagnostic>();
    }
}
