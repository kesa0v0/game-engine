using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>제출이 어디서 끝났나 (spec S6, spec-gaps G131). 앞의 단계에서 멈추면 뒤 단계는 돌지 않는다.</summary>
    public enum SubmitOutcome
    {
        /// <summary>
        /// 모든 검사를 통과하고 본문을 실행했다 (G132). 본문이 <c>ask</c>에서 멈췄으면 <see cref="SubmitResult.OpenedRequest"/>가 있다.
        /// </summary>
        Succeeded,

        /// <summary>자유 커맨드(<c>free=True</c>)가 아니라 Request 없이 낼 수 없다 (S6 "기본 거부").</summary>
        NeedsRequest,

        /// <summary>인자가 매개변수 타입에 맞지 않는다 (G115). <see cref="SubmitResult.ArgumentRejections"/>.</summary>
        ArgumentsRejected,

        /// <summary>정책 함수가 이 issuer가 그 대상으로 행동하는 것을 막았다 (G122).</summary>
        GateDenied,

        /// <summary><c>require</c>가 하나 이상 실패했다 (G110). <see cref="SubmitResult.Preconditions"/>.</summary>
        PreconditionsFailed,

        /// <summary>스크립트 오류 (모드 버그). 효과는 남지 않는다 (C8). <see cref="SubmitResult.Error"/>.</summary>
        ScriptError,
    }

    /// <summary>
    /// 제출 하나의 결과 (spec S6, spec-gaps G131). 제출자의 입력과 모드 버그로 생기는 결과는 모두 여기에 담기고 예외가 아니다.
    /// 거절·오류는 상태와 난수를 건드리지 않는다 (C7·C8·C10).
    /// </summary>
    public sealed class SubmitResult
    {
        internal SubmitResult(string command, string issuer, SubmitOutcome outcome)
        {
            Command = command;
            Issuer = issuer;
            Outcome = outcome;
        }

        /// <summary>커맨드 id (<c>모드id.함수이름</c>, G109).</summary>
        public string Command { get; }

        /// <summary>호스트가 붙인 제출자 꼬리표 (G119). 엔진은 검증하지 않고 받은 그대로 쓴다.</summary>
        public string Issuer { get; }

        public SubmitOutcome Outcome { get; }

        public bool Succeeded => Outcome == SubmitOutcome.Succeeded;

        /// <summary>
        /// 기본값까지 채우고 타입에 맞춘 전체 인자 (G116). 인자 검사를 통과했을 때만 있다. 커맨드 로그에 남길 인자가 이것이다.
        /// </summary>
        public IReadOnlyDictionary<string, Value>? Arguments { get; internal set; }

        /// <summary>인자 거절 (G115). <see cref="SubmitOutcome.ArgumentsRejected"/>일 때만 비어 있지 않다.</summary>
        public IReadOnlyList<ArgumentRejection> ArgumentRejections { get; internal set; } = Array.Empty<ArgumentRejection>();

        /// <summary>
        /// <c>acts_for</c>가 고른 대상: 엔티티(<see cref="EntityValue"/>)나 <see cref="NoneValue"/>(<c>acts_for=None</c>).
        /// 입구까지 왔을 때만 있다.
        /// </summary>
        public Value? Target { get; internal set; }

        /// <summary>사전 검사 결과. 입구를 통과했을 때만 있다.</summary>
        public PreconditionReport? Preconditions { get; internal set; }

        /// <summary>스크립트 오류 (<see cref="SubmitOutcome.ScriptError"/>일 때만). 위치와 호출 흔적이 메시지에 있다.</summary>
        public WorldException? Error { get; internal set; }

        /// <summary>
        /// 본문이 <c>ask</c>에서 멈춰 연 질문 (spec X1). 커맨드는 거기까지 실행되어 효과가 남았고, 나머지는 답이 오면 이어서 실행된다.
        /// </summary>
        public Request? OpenedRequest { get; internal set; }

        /// <summary>
        /// 본문이 <c>wait</c>로 잠들었으면 깨울 틱 (spec X2, spec-gaps G158). 효과는 거기까지 남았고 결과는 <see cref="SubmitOutcome.Succeeded"/>다.
        /// </summary>
        public long? SleepsUntil { get; internal set; }

        /// <summary>
        /// 본문이 낸 신호로 돈 핸들러들, 돈 순서대로 (spec S5, spec-gaps G148). 핸들러가 실패해도 <see cref="Outcome"/>은 그대로다.
        /// </summary>
        public IReadOnlyList<Reaction> Reactions { get; internal set; } = Array.Empty<Reaction>();
    }

    /// <summary>커맨드 제출 (spec S6 권한 3층, spec-gaps G122~G124·G131).</summary>
    public sealed partial class World
    {
        private readonly SubmitPolicyDecl? _submitPolicy;

        /// <summary>
        /// 커맨드를 제출한다: 자유 커맨드 확인 → 인자 검사(G115) → 입구 <c>may_act(issuer, acts_for(인자))</c>(G122) →
        /// 전제 조건(G110) → 본문(G132) 순서다. <paramref name="issuer"/>는 호스트가 연결에 붙인 불투명한 꼬리표다 (G119).
        /// 없는 커맨드 id만 호출자 오류(<see cref="WorldException"/>)이고 나머지는 모두 결과로 돌려준다 (G131).
        /// </summary>
        public SubmitResult Submit(string issuer, string command, IReadOnlyDictionary<string, Value> args)
        {
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
                var report = Atomically(() =>
                {
                    var r = EvaluateRequires(decl, checkedArgs, runBody: true, out var suspended);
                    // ask·wait에서 멈췄으면 여기까지가 한 트랜잭션이고 Flow가 질문을 들거나 잠들어 남는다 (S5·X1·X2)
                    if (suspended != null)
                    {
                        flow = _nextFlow;
                        opened = OpenFlow(command, suspended);
                    }
                    return r;
                });
                if (!report.Passed) return Copy(result, SubmitOutcome.PreconditionsFailed, target, report);
                var done = Copy(result, SubmitOutcome.Succeeded, target, report);
                done.OpenedRequest = opened;
                done.SleepsUntil = flow == null ? null : SleepsUntil(flow.Value);
                // 커맨드의 트랜잭션이 끝난 뒤 핸들러를 각자의 트랜잭션으로 돌린다 (S5, G148)
                done.Reactions = RunReactions();
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
