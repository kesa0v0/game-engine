using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary><see cref="World.Candidates"/>의 결과 (spec-gaps G184).</summary>
    public sealed class CandidateList
    {
        internal CandidateList(string command, string parameter, IReadOnlyList<Value> values, IReadOnlyList<string> needs,
            IReadOnlyList<ArgumentRejection> arguments)
        {
            Command = command;
            Parameter = parameter;
            Values = values;
            Needs = needs;
            Arguments = arguments;
        }

        public string Command { get; }
        public string Parameter { get; }

        /// <summary>후보, <c>where=</c>면 id 순, <c>from_=</c>면 목록 순(같은 값은 처음 것만). 엔티티는 <see cref="EntityValue"/>.</summary>
        public IReadOnlyList<Value> Values { get; }

        /// <summary>후보 식이 읽는데 아직 정하지 않은 매개변수 (매개변수 순). 있으면 <see cref="Values"/>는 비어 있다.</summary>
        public IReadOnlyList<string> Needs { get; }

        /// <summary>넘긴 인자의 거절 (G115). 있으면 <see cref="Values"/>는 비어 있다.</summary>
        public IReadOnlyList<ArgumentRejection> Arguments { get; }

        public bool Ready => Needs.Count == 0 && Arguments.Count == 0;
    }
}
