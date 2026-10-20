using System;
using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>UI 동작이 어디서 끝났나 (spec-gaps G280). 앞의 단계에서 멈추면 뒤 단계는 돌지 않는다.</summary>
    public enum ActOutcome
    {
        /// <summary>본문까지 돌았다. UI 기록의 쓰기가 들어갔고 <see cref="ActResult.Submissions"/>가 호스트가 낼 커맨드다.</summary>
        Succeeded,

        /// <summary>인자가 매개변수 타입에 맞지 않는다 (G115와 같다). <see cref="ActResult.ArgumentRejections"/>.</summary>
        ArgumentsRejected,

        /// <summary><c>require</c>가 하나 이상 실패했다. <see cref="ActResult.Preconditions"/>.</summary>
        PreconditionsFailed,

        /// <summary>스크립트 오류 (모드 버그). UI 기록은 그대로이고 <c>submit</c>도 없다 (G278). <see cref="ActResult.Error"/>.</summary>
        ScriptError,
    }

    /// <summary>
    /// <c>@ui_action</c> 하나의 결과 (spec-gaps G278·G280). 게임 상태는 어느 경우에도 바뀌지 않는다. 성공했을 때만 UI 기록이 바뀌고
    /// <see cref="Submissions"/>가 있다.
    /// </summary>
    public sealed class ActResult
    {
        internal ActResult(string action, ActOutcome outcome)
        {
            Action = action;
            Outcome = outcome;
        }

        /// <summary>UI 동작 id (<c>모드id.함수이름</c>).</summary>
        public string Action { get; }

        public ActOutcome Outcome { get; }

        public bool Succeeded => Outcome == ActOutcome.Succeeded;

        /// <summary>기본값까지 채우고 타입에 맞춘 전체 인자. 인자 검사를 통과했을 때만 있다.</summary>
        public IReadOnlyDictionary<string, Value>? Arguments { get; internal set; }

        public IReadOnlyList<ArgumentRejection> ArgumentRejections { get; internal set; } = Array.Empty<ArgumentRejection>();

        /// <summary>사전 조건 결과. 인자 검사를 통과했을 때만 있다.</summary>
        public PreconditionReport? Preconditions { get; internal set; }

        public WorldException? Error { get; internal set; }

        /// <summary>
        /// 본문이 적어 둔 <c>submit</c>, 적힌 순서대로 (G278). 엔진은 내지 않는다: 호스트가 <see cref="World.Deliver"/>로 하나씩 낸다.
        /// </summary>
        public IReadOnlyList<UiSubmission> Submissions { get; internal set; } = Array.Empty<UiSubmission>();
    }
}
