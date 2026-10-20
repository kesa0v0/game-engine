using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary><c>submit("Attack", target=u)</c> 하나: 커맨드 id와 이름 인자 (spec-gaps G278). 엔티티는 <see cref="EntityValue"/>다.</summary>
    public sealed class UiSubmission
    {
        public UiSubmission(string command, IReadOnlyDictionary<string, Value> arguments)
        {
            Command = command;
            Arguments = arguments;
        }

        /// <summary>커맨드 id (<c>모드id.함수이름</c>). 점 없이 적은 이름은 <c>submit</c>을 부른 함수의 모드로 푼다.</summary>
        public string Command { get; }

        public IReadOnlyDictionary<string, Value> Arguments { get; }
    }
}
