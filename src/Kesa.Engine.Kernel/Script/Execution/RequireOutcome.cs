using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary><c>require</c> 하나의 결과 (spec-gaps G110).</summary>
    public enum RequireStatus
    {
        Passed,
        Failed,

        /// <summary>앞의 <c>require</c>가 이미 실패했고 이 <c>require</c>의 평가가 실행 오류를 냈다 (앞 조건에 기대는 조건).</summary>
        NotEvaluated,
    }

    /// <summary>
    /// <c>require(조건, "메시지 키", 이름=값, ...)</c> 하나의 결과 (spec S6·C7, spec-gaps G110). 평가 안 됨이면 인자가 없고,
    /// 메시지 키는 문자열 리터럴로 적혀 있을 때만 있다.
    /// </summary>
    public sealed class RequireOutcome
    {
        private static readonly IReadOnlyDictionary<string, Value> NoArgs =
            new SortedDictionary<string, Value>(StringComparer.Ordinal);

        public RequireOutcome(RequireStatus status, string? messageKey, IReadOnlyDictionary<string, Value>? args,
            SourceSpan span)
        {
            Status = status;
            MessageKey = messageKey;
            Args = args ?? NoArgs;
            Span = span;
        }

        public RequireStatus Status { get; }
        public string? MessageKey { get; }

        /// <summary>메시지 인자 (이름 서수 순).</summary>
        public IReadOnlyDictionary<string, Value> Args { get; }

        /// <summary><c>require</c> 호출의 위치.</summary>
        public SourceSpan Span { get; }
    }
}
