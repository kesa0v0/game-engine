using System;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>경계를 넘을 수 없는 값 (spec-gaps G326): 나가는 함수 값, 인자로 들어온 숨김·글·선택지.</summary>
    public sealed class BridgeValueException : Exception
    {
        public BridgeValueException(string message) : base(message) { }
    }
}
