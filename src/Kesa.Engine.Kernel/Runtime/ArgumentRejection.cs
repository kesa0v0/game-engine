using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>제출된 인자가 매개변수 타입에 맞지 않는 이유 (spec-gaps G115).</summary>
    public enum ArgumentProblem
    {
        /// <summary>매개변수에 해당하는 인자가 없다.</summary>
        Missing,

        /// <summary>그런 이름의 매개변수가 없다.</summary>
        Unexpected,

        /// <summary>값의 종류가 다르다 (int 자리에 str 등).</summary>
        WrongType,

        /// <summary>엔티티가 이미 죽었다 (클릭과 제출 사이에 죽는 정상 상황).</summary>
        DeadEntity,

        /// <summary>살아 있지만 다른 Def 타입의 엔티티다.</summary>
        WrongEntityType,

        /// <summary>그런 id의 Def가 없다.</summary>
        UnknownDef,

        /// <summary>Def는 있지만 다른 Def 타입이다.</summary>
        WrongDefType,
    }

    /// <summary>인자 하나의 거절 (G115). <see cref="Path"/>는 컬렉션 안의 위치까지 적은 것이다 (<c>path[1]</c>, <c>goods["Iron"]</c>).</summary>
    public sealed class ArgumentRejection
    {
        public ArgumentRejection(string parameter, string path, ArgumentProblem problem, string? expected, string message)
        {
            Parameter = parameter;
            Path = path;
            Problem = problem;
            Expected = expected;
            Message = message;
        }

        public string Parameter { get; }
        public string Path { get; }
        public ArgumentProblem Problem { get; }

        /// <summary>그 자리의 타입 표기. 남는 인자면 없다.</summary>
        public string? Expected { get; }

        /// <summary>개발자용 설명 (영어). 플레이어에게 보일 문구는 <see cref="Problem"/>으로 고른다.</summary>
        public string Message { get; }

        public override string ToString() => Message;
    }
}
