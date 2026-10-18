using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
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

        /// <summary>
        /// 실패하지 않은 경고 (spec-gaps G225): 응답 커맨드나 이어서 실행한 구간이 읽기 예산보다 많은 단계를 썼으면 <c>RUN021</c>.
        /// </summary>
        public IReadOnlyList<Diagnostic> Warnings { get; internal set; } = Array.Empty<Diagnostic>();
    }
}
