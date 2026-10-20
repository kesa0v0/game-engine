using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>핸들러 하나를 돌린 결과 (spec S5, spec-gaps G148).</summary>
    public enum ReactionOutcome
    {
        /// <summary>끝까지 실행했다.</summary>
        Finished,

        /// <summary>
        /// <c>ask</c>나 <c>wait</c>에서 멈췄다. 효과는 남았고 Flow가 질문을 들고(<see cref="Reaction.OpenedRequest"/>) 기다리거나 잔다.
        /// </summary>
        Suspended,

        /// <summary>스크립트 오류. 그 핸들러의 효과만 되돌렸고 원인이 된 커맨드는 그대로다 (S5).</summary>
        ScriptError,

        /// <summary>신호가 세대 상한보다 깊어 돌리지 않았다 (G147). <see cref="Reaction.Message"/>에 고리 경로가 있다.</summary>
        Dropped,

        /// <summary>
        /// 핸들러가 받는 엔티티가 핸들러가 돌기 전에 사라져 돌리지 않았다 (S5 <c>on_stale</c> 기본 취소, spec-gaps G151). 오류가 아니다.
        /// </summary>
        Canceled,
    }

    /// <summary>
    /// 제출·답·응답·시간 진행이 일으킨 핸들러 실행 하나, 또는 <c>wait</c>에서 깨어난 Flow 하나 (spec S5, spec-gaps G148·G157).
    /// 돈 순서대로 결과에 담긴다.
    /// </summary>
    public sealed class Reaction
    {
        internal Reaction(string signal, IReadOnlyDictionary<string, Value> values, string handler, int generation,
            ReactionOutcome outcome)
        {
            Signal = signal;
            Values = values;
            Handler = handler;
            Generation = generation;
            Outcome = outcome;
        }

        /// <summary>신호 이름. <c>wait</c>에서 깨어난 Flow면 <see cref="WakeSignal"/>이다 (G157).</summary>
        public string Signal { get; }

        /// <summary>깨어난 Flow 항목의 신호 자리.</summary>
        public const string WakeSignal = "wait";

        /// <summary>마감이 지나 기본 답을 받은 Flow 항목의 신호 자리 (G161).</summary>
        public const string TimeoutSignal = "timeout";

        /// <summary>대상이 사라져 취소된 멈춘 Flow 항목의 신호 자리 (G164).</summary>
        public const string StaleSignal = "stale";

        /// <summary><c>advance(n)</c>으로 시간을 흘린 뒤 이어 간 Flow 항목의 신호 자리 (G167).</summary>
        public const string AdvanceSignal = "advance";

        /// <summary>응답 본문 Flow가 버려져 그 응답을 기다리던 바깥 Flow를 <c>r.interrupted</c>로 이어 간 항목의 신호 자리 (G243).</summary>
        public const string InterruptedSignal = "interrupted";

        /// <summary><c>@when</c> 반응이 발동한 항목의 신호 자리 (G169). 값은 반응의 매개변수(엔티티)이고, 전역 조건이면 비어 있다.</summary>
        public const string WhenSignal = "when";

        /// <summary>깨어난 Flow 항목이면 그 Flow 번호.</summary>
        public long? Flow { get; internal set; }

        /// <summary>신호의 값 (저장 가능한 모양, 엔티티는 <see cref="EntityValue"/>).</summary>
        public IReadOnlyDictionary<string, Value> Values { get; }

        /// <summary>핸들러 id (<c>모드id.함수이름</c>).</summary>
        public string Handler { get; }

        /// <summary>
        /// 신호의 세대: 제출·답·깨어난 Flow가 낸 신호가 1, 핸들러가 낸 신호는 그 핸들러가 받은 신호 + 1 (G147). 깨어난 Flow 항목 자체는 0.
        /// </summary>
        public int Generation { get; }

        public ReactionOutcome Outcome { get; }

        /// <summary><see cref="ReactionOutcome.Suspended"/>일 때 연 질문.</summary>
        public Request? OpenedRequest { get; internal set; }

        /// <summary><see cref="ReactionOutcome.Suspended"/>이고 <c>wait</c>로 잠들었으면 깨울 틱 (G158).</summary>
        public long? SleepsUntil { get; internal set; }

        /// <summary><see cref="ReactionOutcome.ScriptError"/>일 때의 오류.</summary>
        public WorldException? Error { get; internal set; }

        /// <summary><see cref="ReactionOutcome.Dropped"/>·<see cref="ReactionOutcome.Canceled"/>일 때의 이유 (개발자용, 영어).</summary>
        public string? Message { get; internal set; }

        /// <summary><see cref="ReactionOutcome.Dropped"/>일 때 고칠 방법.</summary>
        public string? Hint { get; internal set; }

        /// <summary>실패하지 않은 경고 (spec-gaps G225): 이 핸들러가 읽기 예산보다 많은 단계를 썼으면 <c>RUN021</c>.</summary>
        public IReadOnlyList<Diagnostic> Warnings { get; internal set; } = Array.Empty<Diagnostic>();
    }
}
