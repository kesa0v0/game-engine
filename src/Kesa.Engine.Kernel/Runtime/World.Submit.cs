using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>커맨드 제출 (spec S6 권한 3층, spec-gaps G122~G124·G131).</summary>
    public sealed partial class World
    {
        private readonly PolicyDecl? _submitPolicy;

        /// <summary>
        /// 커맨드를 제출한다: 자유 커맨드 확인 → 인자 검사(G115) → 입구 <c>may_act(issuer, acts_for(인자))</c>(G122) →
        /// 전제 조건(G110) → 본문(G132) 순서다. <paramref name="issuer"/>는 호스트가 연결에 붙인 불투명한 꼬리표다 (G119).
        /// 없는 커맨드 id만 호출자 오류(<see cref="WorldException"/>)이고 나머지는 모두 결과로 돌려준다 (G131).
        /// </summary>
        public SubmitResult Submit(string issuer, string command, IReadOnlyDictionary<string, Value> args)
        {
            NoHostWrites("Submit");
            if (!_commands.TryGetValue(command, out var decl))
            {
                throw new WorldException(Codes.RunUnknownName, "there is no command '" + command + "'",
                    Suggest.Hint(command, _commands.Keys));
            }
            // 응답 커맨드(Request id와 응답 엔티티)는 아직 없다: Request 없이 낸 제출은 자유 커맨드만 받는다 (S6)
            if (!decl.Free) return new SubmitResult(command, issuer, SubmitOutcome.NeedsRequest);

            var rejections = new List<ArgumentRejection>();
            var checkedArgs = CheckArguments(decl, args, rejections);
            if (rejections.Count > 0)
                return new SubmitResult(command, issuer, SubmitOutcome.ArgumentsRejected) { ArgumentRejections = rejections };

            var result = new SubmitResult(command, issuer, SubmitOutcome.Succeeded) { Arguments = checkedArgs };
            try
            {
                var target = ActsFor(decl, checkedArgs);
                if (!MayAct(issuer, target, command)) return Copy(result, SubmitOutcome.GateDenied, target);
                // 본문이 중간에 실패하면 그때까지의 쓰기를 되감는다 (C8, G133)
                Request? opened = null;
                long? flow = null;
                PreconditionReport report;
                IReadOnlyList<Diagnostic> warnings;
                var outerWarnings = OpenWarnings();
                try
                {
                    report = Atomically(() =>
                    {
                        var r = EvaluateRequires(decl, checkedArgs, runBody: true, out var suspended);
                        // ask·wait에서 멈췄으면 여기까지가 한 트랜잭션이고 Flow가 질문을 들거나 잠들어 남는다 (S5·X1·X2)
                        if (suspended != null)
                        {
                            flow = _nextFlow;
                            opened = OpenFlow(command, suspended, SubjectsOf(checkedArgs.Values));
                        }
                        return r;
                    });
                }
                finally
                {
                    warnings = CloseWarnings(outerWarnings);
                }
                if (!report.Passed)
                {
                    var failed = Copy(result, SubmitOutcome.PreconditionsFailed, target, report);
                    failed.Warnings = warnings;
                    return failed;
                }
                var done = Copy(result, SubmitOutcome.Succeeded, target, report);
                done.Warnings = warnings;
                // 커맨드의 트랜잭션이 끝난 뒤 핸들러를 각자의 트랜잭션으로 돌린다 (S5, G148)
                done.Reactions = AfterRun();
                // 신호 처리와 대상 소멸 확인(G164)이 끝난 뒤의 상태: 그 사이 취소된 Flow의 질문·잠은 보이지 않는다
                done.OpenedRequest = flow == null ? null : HeldRequest(flow.Value);
                done.SleepsUntil = flow == null ? null : SleepsUntil(flow.Value);
                return done;
            }
            catch (WorldException e)
            {
                return Copy(result, SubmitOutcome.ScriptError, null, null, e);
            }
        }

        private static SubmitResult Copy(SubmitResult from, SubmitOutcome outcome, Value? target,
            PreconditionReport? report = null, WorldException? error = null) =>
            new SubmitResult(from.Command, from.Issuer, outcome)
            {
                Arguments = from.Arguments,
                Target = target,
                Preconditions = report,
                Error = error,
            };

        /// <summary>
        /// <c>acts_for</c> 람다를 커맨드 인자로 부른다 (G130: 람다의 매개변수 이름으로 골라 넘김). 엔티티나 None이어야 한다.
        /// </summary>
        private Value ActsFor(CommandDecl decl, SortedDictionary<string, Value> checkedArgs)
        {
            if (decl.ActsFor == null) return NoneValue.Instance;
            string label = "acts_for of " + decl.Id;
            var picked = new List<Value>();
            foreach (var p in decl.ActsFor.Parameters) picked.Add(ToScript(checkedArgs[p.Name]));
            Value value = RunGateFunction(decl.ActsFor, picked, label);
            if (!(value is NoneValue) && !(value is EntityValue))
            {
                throw new WorldException(Codes.RunType,
                    "computing " + label + ": it must give an entity or None, not " + value.KindName,
                    "acts_for picks whom the command acts as (spec-gaps G122)");
            }
            return value;
        }

        /// <summary>정책 함수 <c>may_act(issuer, target)</c>. 없으면 모두 허용한다 (G124). bool이어야 한다.</summary>
        private bool MayAct(string issuer, Value target, string command)
        {
            if (_submitPolicy == null) return true;
            var function = _submitPolicy.Function;
            string label = "the submit policy '" + function.Name + "' for " + command;
            var value = RunGateFunction(function, new[] { new StrValue(issuer), ToScript(target) }, label);
            if (value is BoolValue b) return b.Value;
            throw new WorldException(Codes.RunType, "computing " + label + ": it must return True or False, not " + value.KindName,
                "spec-gaps G122");
        }

        /// <summary>입구의 순수 함수 실행: 예산 하나, 스크립트 오류는 위치가 붙은 <see cref="WorldException"/>, 엔티티는 저장 값으로.</summary>
        private Value RunGateFunction(FunctionValue function, IReadOnlyList<Value> args, string label)
        {
            var span = function.Definition?.Span ?? function.Lambda!.Span;
            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_stepBudget);
            try
            {
                return ToPlain(label, Evaluator.Run(function, args, span, _meter));
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, label);
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }
    }
}
