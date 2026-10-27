using System;
using System.Collections.Generic;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>
    /// 커널의 결과(질문, 합법 수, 제출·답·화면 동작의 결과)를 경계의 데이터로 바꾼다 (spec-gaps G325). 넘어오는 결과는 이미 그 연결에게
    /// 보낼 모양(<see cref="WorldView"/>의 <c>Show</c>, G201)이다.
    /// </summary>
    internal static class KernelResults
    {
        public static IReadOnlyList<BridgeCommand> Commands(IReadOnlyDictionary<string, CommandDecl> declared)
        {
            var names = new List<string>(declared.Keys);
            names.Sort(StringComparer.Ordinal);
            var commands = new List<BridgeCommand>(names.Count);
            foreach (var name in names)
            {
                var decl = declared[name];
                var parameters = new List<BridgeParameter>(decl.Parameters.Count);
                foreach (var p in decl.Parameters) parameters.Add(new BridgeParameter(p.Name, p.Type.Display));
                commands.Add(new BridgeCommand(name, decl.Free, parameters));
            }
            return commands;
        }

        public static BridgeRequest Request(Request q, Func<EntityId, string?> defOf)
        {
            var who = new List<BridgeEntity>(q.Who.Count);
            foreach (var e in q.Who) who.Add(new BridgeEntity(e.Value, defOf(e)));
            var choices = new List<BridgeOption>(q.Choices.Count);
            foreach (var c in q.Choices) choices.Add((BridgeOption)KernelValues.ToBridge(c, defOf));
            return new BridgeRequest(q.Id, q.Responses, who, KernelValues.ToBridge(q.Title, defOf), choices, q.Hint,
                KernelValues.ToBridge(q.Data, defOf), q.BlocksTime, q.Deadline);
        }

        public static BridgeMoves Moves(LegalMoveList list, Func<EntityId, string?> defOf)
        {
            var moves = new List<BridgeMove>(list.Moves.Count);
            foreach (var move in list.Moves) moves.Add(new BridgeMove(list.Command, Arguments(move, defOf)));
            var problems = new List<BridgeReason>();
            AddArguments(problems, list.Arguments);
            return new BridgeMoves(moves, list.Truncated, null, problems);
        }

        public static BridgeMoves Moves(ResponseMoveList list, Func<EntityId, string?> defOf)
        {
            var moves = new List<BridgeMove>(list.Moves.Count);
            foreach (var move in list.Moves)
                moves.Add(new BridgeMove(move.Command, Arguments(move.Arguments, defOf), new BridgeEntity(move.By.Value, defOf(move.By))));
            var needs = new List<BridgeNeeds>(list.Needs.Count);
            foreach (var n in list.Needs) needs.Add(new BridgeNeeds(n.Command, n.Parameters));
            return new BridgeMoves(moves, list.Truncated, needs);
        }

        /// <summary>나열하지 못함 (모르는 커맨드·질문, 나열 중 실행 오류).</summary>
        public static BridgeMoves NoMoves(WorldException error) =>
            new BridgeMoves(Array.Empty<BridgeMove>(), false, null, new[] { Error(error) });

        /// <summary>사전 조건만 본 결과(G342). 통과하면 <c>Passed</c>.</summary>
        public static BridgeResult Check(PreconditionReport report, Func<EntityId, string?> defOf)
        {
            string outcome = report.Arguments.Count > 0 ? SubmitOutcome.ArgumentsRejected.ToString()
                : report.Failures.Count > 0 ? SubmitOutcome.PreconditionsFailed.ToString()
                : "Passed";
            return new BridgeResult(report.Passed, outcome, Reasons(null, Array.Empty<ArgumentRejection>(), report, defOf));
        }

        /// <summary>이 연결이 낼 수 없다(입구 정책, 낼 질문 없음 등)는 검사 결과 (G233·G283).</summary>
        public static BridgeResult Denied(string why) =>
            new BridgeResult(false, SubmitOutcome.GateDenied.ToString(), new[] { new BridgeReason(BridgeReasonKind.Error, message: why) });

        /// <summary>검사하다 난 오류(없는 이름, 스크립트 오류).</summary>
        public static BridgeResult Failure(WorldException error) =>
            new BridgeResult(false, SubmitOutcome.ScriptError.ToString(), new[] { Error(error) });

        public static IReadOnlyDictionary<string, BridgeValue> Arguments(IReadOnlyDictionary<string, Value> args, Func<EntityId, string?> defOf)
        {
            var result = new SortedDictionary<string, BridgeValue>(StringComparer.Ordinal);
            foreach (var pair in args) result[pair.Key] = KernelValues.ToBridge(pair.Value, defOf);
            return result;
        }

        public static BridgeResult Result(SubmitResult r, Func<EntityId, string?> defOf) =>
            new BridgeResult(r.Succeeded, r.Outcome.ToString(), Reasons(r.Error, r.ArgumentRejections, r.Preconditions, defOf));

        public static BridgeResult Result(AnswerResult r, Func<EntityId, string?> defOf)
        {
            var reasons = new List<BridgeReason>();
            if (r.Message != null) reasons.Add(new BridgeReason(BridgeReasonKind.Error, message: r.Message));
            reasons.AddRange(Reasons(r.Error, r.ArgumentRejections, r.Preconditions, defOf));
            return new BridgeResult(r.Succeeded, r.Outcome.ToString(), reasons);
        }

        public static BridgeResult Result(ActResult r, Func<EntityId, string?> defOf) =>
            new BridgeResult(r.Succeeded, r.Outcome.ToString(), Reasons(r.Error, r.ArgumentRejections, r.Preconditions, defOf));

        /// <summary>화면 동작의 <c>submit</c> 하나를 낸 결과: 제출이나 답, 낼 곳이 없으면 거절 (G283).</summary>
        public static BridgeResult Result(DeliveryResult r, Func<EntityId, string?> defOf)
        {
            if (r.Submitted != null) return Result(r.Submitted, defOf);
            if (r.Answered != null) return Result(r.Answered, defOf);
            return new BridgeResult(false, "Refused", new[] { new BridgeReason(BridgeReasonKind.Refusal, message: r.Refusal) });
        }

        private static IReadOnlyList<BridgeReason> Reasons(WorldException? error, IReadOnlyList<ArgumentRejection> rejections,
            PreconditionReport? preconditions, Func<EntityId, string?> defOf)
        {
            var reasons = new List<BridgeReason>();
            if (error != null) reasons.Add(Error(error));
            AddArguments(reasons, rejections);
            if (preconditions != null)
            {
                AddArguments(reasons, preconditions.Arguments);
                foreach (var f in preconditions.Failures) reasons.Add(Reason(f, defOf));
            }
            return reasons;
        }

        private static BridgeReason Reason(RequireOutcome failure, Func<EntityId, string?> defOf)
        {
            switch (failure.Kind)
            {
                case PreconditionKind.Cost:
                    return new BridgeReason(BridgeReasonKind.Cost, slot: failure.Slot);
                case PreconditionKind.Choose:
                    return new BridgeReason(BridgeReasonKind.Choose, parameter: failure.Parameter);
            }
            if (failure.MessageKey == null) return new BridgeReason(BridgeReasonKind.Require);
            var args = new List<KeyValuePair<string, BridgeValue>>(failure.Args.Count);
            foreach (var pair in failure.Args) args.Add(new KeyValuePair<string, BridgeValue>(pair.Key, KernelValues.ToBridge(pair.Value, defOf)));
            return new BridgeReason(BridgeReasonKind.Require, new BridgeText(failure.MessageKey, args));
        }

        private static void AddArguments(List<BridgeReason> reasons, IReadOnlyList<ArgumentRejection> rejections)
        {
            foreach (var a in rejections)
                reasons.Add(new BridgeReason(BridgeReasonKind.Argument, message: a.Message, parameter: a.Parameter, code: a.Problem.ToString()));
        }

        private static BridgeReason Error(WorldException e) => new BridgeReason(BridgeReasonKind.Error, message: e.Message, code: e.Code);
    }
}
