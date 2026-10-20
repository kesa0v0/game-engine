using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary><see cref="World.LegalMoves"/>의 결과 (spec-gaps G184).</summary>
    public sealed class LegalMoveList
    {
        internal LegalMoveList(string command, IReadOnlyList<IReadOnlyDictionary<string, Value>> moves, bool truncated,
            IReadOnlyList<ArgumentRejection> arguments)
        {
            Command = command;
            Moves = moves;
            Truncated = truncated;
            Arguments = arguments;
        }

        public string Command { get; }

        /// <summary>
        /// 사전 검사를 통과한 전체 인자들 (기본값까지 채움), <c>choose</c> 순서 → 후보 순서. 그대로 제출할 수 있다.
        /// </summary>
        public IReadOnlyList<IReadOnlyDictionary<string, Value>> Moves { get; }

        /// <summary>최대 개수에 닿아 더 있는 수를 버렸다.</summary>
        public bool Truncated { get; }

        /// <summary>넘긴 인자의 거절 (G115). 있으면 <see cref="Moves"/>는 비어 있다.</summary>
        public IReadOnlyList<ArgumentRejection> Arguments { get; }
    }
}
