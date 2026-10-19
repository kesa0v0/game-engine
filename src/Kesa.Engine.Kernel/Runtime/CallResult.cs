using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 호스트가 부른 모드 순수 함수의 결과 (spec-gaps G244·G247). 엔티티는 <see cref="EntityValue"/>이고 목록·사전 안도 그렇다.
    /// </summary>
    public sealed class CallResult
    {
        internal CallResult(string function, Value value)
        {
            Function = function;
            Value = value;
        }

        /// <summary>부른 함수 <c>모드.함수</c>.</summary>
        public string Function { get; }

        public Value Value { get; }
    }
}
