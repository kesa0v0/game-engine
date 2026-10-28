using System;
using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>봇 한 번 생각하기가 어디서 끝났나 (spec-gaps G369·G375).</summary>
    public enum ThinkOutcome
    {
        /// <summary>본문까지 돌았다. 기억과 난수 카운터가 들어갔고 <see cref="ThinkResult.Submissions"/>가 낼 수다(없으면 할 일이 없다).</summary>
        Thought,

        /// <summary>맡은 엔티티가 없거나 안 보이거나, 설정이 매개변수 타입에 맞지 않는다. <see cref="ThinkResult.ArgumentRejections"/>.</summary>
        ArgumentsRejected,

        /// <summary>스크립트 오류나 예산 초과. 기억·난수 카운터는 그대로이고 낼 수도 없다 (G375). <see cref="ThinkResult.Error"/>.</summary>
        ScriptError,
    }

    /// <summary><c>@bot</c> 한 번 생각하기의 결과. 게임은 바뀌지 않는다: 낼 수는 호스트가 <see cref="World.Deliver"/>로 낸다.</summary>
    public sealed class ThinkResult
    {
        internal ThinkResult(string bot, ThinkOutcome outcome)
        {
            Bot = bot;
            Outcome = outcome;
        }

        public string Bot { get; }

        public ThinkOutcome Outcome { get; }

        public IReadOnlyList<ArgumentRejection> ArgumentRejections { get; internal set; } = Array.Empty<ArgumentRejection>();

        public WorldException? Error { get; internal set; }

        /// <summary>본문이 적어 둔 <c>submit</c>, 적힌 순서대로.</summary>
        public IReadOnlyList<UiSubmission> Submissions { get; internal set; } = Array.Empty<UiSubmission>();
    }
}
