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

    /// <summary>사전 조건 블록의 문장 종류 (spec S2).</summary>
    public enum PreconditionKind
    {
        Require,

        /// <summary><c>cost(칸, 금액)</c> (spec-gaps G185·G186).</summary>
        Cost,

        /// <summary><c>choose(매개변수, ...)</c>: 실패면 인자가 후보가 아니다 (spec-gaps G183).</summary>
        Choose,
    }

    /// <summary>
    /// 사전 조건 문장 하나의 결과 (spec S6·C7, spec-gaps G110). <c>require</c>면 메시지 키와 인자, <c>cost</c>면 칸과 가진 양·필요한 양이 있다.
    /// 평가 안 됨이면 인자가 없고, 메시지 키는 문자열 리터럴로 적혀 있을 때만 있다.
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

        private RequireOutcome(RequireStatus status, string? slot, Value? have, Value? need, SourceSpan span)
            : this(status, null, null, span)
        {
            Kind = PreconditionKind.Cost;
            Slot = slot;
            Have = have;
            Need = need;
        }

        /// <summary><c>cost</c>의 결과. 평가 안 됨이면 칸과 양이 없다.</summary>
        public static RequireOutcome Cost(RequireStatus status, string? slot, Value? have, Value? need, SourceSpan span) =>
            new RequireOutcome(status, slot, have, need, span);

        private RequireOutcome(RequireStatus status, string parameter, SourceSpan span)
            : this(status, null, null, span)
        {
            Kind = PreconditionKind.Choose;
            Parameter = parameter;
        }

        /// <summary><c>choose</c>의 결과: 실패면 그 매개변수의 인자가 후보가 아니다.</summary>
        public static RequireOutcome Choose(RequireStatus status, string parameter, SourceSpan span) =>
            new RequireOutcome(status, parameter, span);

        public PreconditionKind Kind { get; } = PreconditionKind.Require;

        /// <summary><c>choose</c>의 매개변수 이름.</summary>
        public string? Parameter { get; }

        public RequireStatus Status { get; }
        public string? MessageKey { get; }

        /// <summary>메시지 인자 (이름 서수 순).</summary>
        public IReadOnlyDictionary<string, Value> Args { get; }

        /// <summary>문장의 위치.</summary>
        public SourceSpan Span { get; }

        /// <summary><c>cost</c>의 칸 (<c>#3.Treasury.gold</c>).</summary>
        public string? Slot { get; }

        /// <summary><c>cost</c>의 칸이 가진 양.</summary>
        public Value? Have { get; }

        /// <summary>
        /// <c>cost</c>가 필요로 하는 양: 블록에서 그 칸에 앞서 나온 <c>cost</c>의 금액을 더한 값 (같은 칸은 합해서 확인한다, G185).
        /// </summary>
        public Value? Need { get; }

        /// <summary>메시지 인자만 바꾼 사본 (월드가 함수 본문의 값을 호스트 값으로 바꿀 때).</summary>
        public RequireOutcome WithArgs(IReadOnlyDictionary<string, Value> args) =>
            Kind != PreconditionKind.Require ? this : new RequireOutcome(Status, MessageKey, args, Span);
    }
}
