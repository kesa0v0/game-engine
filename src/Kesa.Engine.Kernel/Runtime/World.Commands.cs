using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>커맨드 사전 검사의 결과 (spec S6·C7, spec-gaps G110).</summary>
    public sealed class PreconditionReport
    {
        public PreconditionReport(string command, IReadOnlyList<RequireOutcome> requires)
        {
            Command = command;
            Requires = requires;
            var failures = new List<RequireOutcome>();
            foreach (var r in requires)
                if (r.Status == RequireStatus.Failed) failures.Add(r);
            Failures = failures;
        }

        /// <summary>커맨드 id (<c>모드id.함수이름</c>, G109).</summary>
        public string Command { get; }

        /// <summary><c>require</c>마다의 결과, 적힌 순서대로 ("왜?" 도구 3).</summary>
        public IReadOnlyList<RequireOutcome> Requires { get; }

        /// <summary>실패한 <c>require</c>만 (사유 목록). 평가 안 됨은 들어 있지 않다.</summary>
        public IReadOnlyList<RequireOutcome> Failures { get; }

        public bool Passed => Failures.Count == 0;
    }

    /// <summary>커맨드 (spec S6). 지금은 사전 검사만 한다: 본문 실행·쓰기·롤백은 다음 단계다.</summary>
    public sealed partial class World
    {
        private readonly IReadOnlyDictionary<string, CommandDecl> _commands;

        /// <summary>
        /// 커맨드의 <c>with preconditions:</c>만 평가한다 (본문은 실행하지 않음, spec S2). 상태를 바꾸지 않는다.
        /// <paramref name="args"/>는 매개변수 이름 → 값이고 엔티티는 <see cref="EntityValue"/>로 넘긴다.
        /// 스크립트 오류(앞에 실패가 없는 조건의 실행 오류 등, G110)는 <see cref="WorldException"/>이다.
        /// </summary>
        public PreconditionReport CheckPreconditions(string command, IReadOnlyDictionary<string, Value> args)
        {
            if (!_commands.TryGetValue(command, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + command + "'",
                    Suggest.Hint(command, _commands.Keys));
            }

            var scriptArgs = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in args) scriptArgs[pair.Key] = ToScript(pair.Value);

            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_stepBudget);
            try
            {
                var raw = Evaluator.RunPreconditions(decl.Function, scriptArgs, decl.Span, _meter);
                var requires = new List<RequireOutcome>(raw.Count);
                foreach (var r in raw)
                {
                    var plain = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in r.Args)
                        plain[pair.Key] = ToPlain("argument '" + pair.Key + "' of require()", pair.Value);
                    requires.Add(new RequireOutcome(r.Status, r.MessageKey, plain, r.Span));
                }
                return new PreconditionReport(command, requires);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, "the preconditions of " + command);
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }
    }
}
