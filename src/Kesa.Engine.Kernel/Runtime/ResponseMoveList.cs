using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>응답 커맨드로 낼 수 있는 수 하나: 그대로 <see cref="World.Respond"/>에 넘기면 된다 (spec-gaps G232).</summary>
    public sealed class ResponseMove
    {
        internal ResponseMove(string command, IReadOnlyDictionary<string, Value> arguments, EntityId by)
        {
            Command = command;
            Arguments = arguments;
            By = by;
        }

        /// <summary>응답 커맨드 id (<c>모드id.함수이름</c>).</summary>
        public string Command { get; }

        /// <summary>사전 검사를 통과한 전체 인자들 (기본값까지 채움).</summary>
        public IReadOnlyDictionary<string, Value> Arguments { get; }

        /// <summary>이 수가 답하는 엔티티 (<c>acts_for</c>가 고른 것, 질문 대상 목록 안).</summary>
        public EntityId By { get; }
    }

    /// <summary>나열할 수 없는 응답 커맨드: <c>choose</c>도 기본값도 없고 답하는 엔티티도 아닌 매개변수가 있다.</summary>
    public sealed class ResponseNeeds
    {
        internal ResponseNeeds(string command, IReadOnlyList<string> parameters)
        {
            Command = command;
            Parameters = parameters;
        }

        public string Command { get; }

        /// <summary>호스트가 정해야 하는 매개변수, 선언 순서.</summary>
        public IReadOnlyList<string> Parameters { get; }
    }

    /// <summary><see cref="World.LegalResponses"/>의 결과 (spec-gaps G232).</summary>
    public sealed class ResponseMoveList
    {
        internal ResponseMoveList(long request, IReadOnlyList<ResponseMove> moves, bool truncated, IReadOnlyList<ResponseNeeds> needs)
        {
            Request = request;
            Moves = moves;
            Truncated = truncated;
            Needs = needs;
        }

        /// <summary>질문 번호.</summary>
        public long Request { get; }

        /// <summary>질문이 받는 응답 커맨드 순서 → 질문 대상 순서 → <c>choose</c> 순서 → 후보 순서.</summary>
        public IReadOnlyList<ResponseMove> Moves { get; }

        /// <summary>최대 개수에 닿아 더 있는 수를 버렸다.</summary>
        public bool Truncated { get; }

        /// <summary>펼칠 수 없어 건너뛴 응답 커맨드. 그 수는 호스트가 <see cref="World.Candidates"/>와 입력으로 만든다.</summary>
        public IReadOnlyList<ResponseNeeds> Needs { get; }
    }
}
