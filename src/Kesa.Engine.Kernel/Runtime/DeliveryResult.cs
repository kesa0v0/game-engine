namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <see cref="World.Deliver"/>의 결과 (spec-gaps G283): 자유 커맨드면 <see cref="Submitted"/>, 응답 커맨드면 짝지은 질문에 낸
    /// <see cref="Answered"/>, 짝지을 질문이 없거나 여럿이면 <see cref="Refusal"/>만 있다.
    /// </summary>
    public sealed class DeliveryResult
    {
        internal DeliveryResult(SubmitResult? submitted, AnswerResult? answered, string? refusal)
        {
            Submitted = submitted;
            Answered = answered;
            Refusal = refusal;
        }

        public SubmitResult? Submitted { get; }
        public AnswerResult? Answered { get; }

        /// <summary>낼 곳을 찾지 못한 이유. 상태는 바뀌지 않았다.</summary>
        public string? Refusal { get; }

        public bool Succeeded => Submitted?.Succeeded ?? Answered?.Succeeded ?? false;
    }
}
