using System;
using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>커맨드 사전 검사의 결과 (spec S6·C7, spec-gaps G110·G115).</summary>
    public sealed class PreconditionReport
    {
        public PreconditionReport(string command, IReadOnlyList<RequireOutcome> requires)
            : this(command, Array.Empty<ArgumentRejection>(), requires) { }

        public PreconditionReport(string command, IReadOnlyList<ArgumentRejection> arguments,
            IReadOnlyList<RequireOutcome> requires)
        {
            Command = command;
            Arguments = arguments;
            Requires = requires;
            var failures = new List<RequireOutcome>();
            foreach (var r in requires)
                if (r.Status == RequireStatus.Failed) failures.Add(r);
            Failures = failures;
        }

        /// <summary>커맨드 id (<c>모드id.함수이름</c>, G109).</summary>
        public string Command { get; }

        /// <summary><c>require</c>·<c>cost</c>마다의 결과, 적힌 순서대로 ("왜?" 도구 3).</summary>
        public IReadOnlyList<RequireOutcome> Requires { get; }

        /// <summary>실패한 <c>require</c>·<c>cost</c>만 (사유 목록). 평가 안 됨은 들어 있지 않다.</summary>
        public IReadOnlyList<RequireOutcome> Failures { get; }

        /// <summary>인자 거절 (G115). 하나라도 있으면 사전 조건은 평가하지 않았으므로 <see cref="Requires"/>가 비어 있다.</summary>
        public IReadOnlyList<ArgumentRejection> Arguments { get; }

        public bool Passed => Arguments.Count == 0 && Failures.Count == 0;
    }
}
