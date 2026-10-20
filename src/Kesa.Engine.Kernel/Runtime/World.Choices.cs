using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <c>choose</c>의 후보와 합법 수를 호스트에 준다 (spec S2·S6, spec-gaps G183·G184·G188). 상태를 바꾸지 않는다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// 매개변수 하나의 후보 (UI가 앞에서 고른 값에 따라 다음 후보를 강조할 때). <paramref name="args"/>는 이미 정한 인자들이고
        /// <paramref name="parameter"/>의 값은 있어도 보지 않는다. 그 <c>choose</c>의 식이 읽는 매개변수가 빠졌으면 목록 대신
        /// <see cref="CandidateList.Needs"/>를 준다. <c>require</c>·<c>cost</c>는 보지 않는다(고른 뒤 막히는 이유는 사전 검사 보고서).
        /// 없는 커맨드, <c>choose</c>가 없는 매개변수, 스크립트 오류는 <see cref="WorldException"/>이다.
        /// </summary>
        public CandidateList Candidates(string command, IReadOnlyDictionary<string, Value> args, string parameter)
        {
            var decl = FindCommand(command);
            var chooses = ChoosesOf(decl);
            var choose = chooses.Find(c => c.Parameter == parameter);
            if (choose == null)
            {
                var names = new List<string>();
                foreach (var c in chooses) names.Add(c.Parameter);
                throw new WorldException(Codes.RunUnknownName, command + " has no choose(" + parameter + ", ...)",
                    Suggest.Hint(parameter, names) ?? "a parameter without choose() gets its value from the host (spec-gaps G183)");
            }

            var given = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in args)
                if (pair.Key != parameter) given[pair.Key] = pair.Value;
            var rejections = new List<ArgumentRejection>();
            var fitted = CheckArguments(decl, given, rejections, partial: true);
            if (rejections.Count > 0)
                return new CandidateList(command, parameter, Array.Empty<Value>(), Array.Empty<string>(), rejections);

            var needs = new List<string>();
            foreach (var p in decl.Parameters)
                if (choose.Needs.Contains(p.Name) && !fitted.ContainsKey(p.Name)) needs.Add(p.Name);
            if (needs.Count > 0)
                return new CandidateList(command, parameter, Array.Empty<Value>(), needs, Array.Empty<ArgumentRejection>());

            return new CandidateList(command, parameter, CandidatesOf(decl, fitted, choose), Array.Empty<string>(),
                Array.Empty<ArgumentRejection>());
        }

        /// <summary>
        /// 낼 수 있는 인자 조합 (AI, 시나리오 ③). <c>choose</c>가 있는 매개변수를 블록 순서대로 후보로 펼치고 조합마다 사전 검사
        /// (<c>require</c>·<c>cost</c>·<c>choose</c>)를 통과한 것만 준다. <paramref name="args"/>에 값이 있는 매개변수는 펼치지 않는다
        /// (기본값은 고정하지 않는다: <c>choose</c>가 있으면 펼친다).
        /// <paramref name="max"/>개를 넘으면 멈추고 <see cref="LegalMoveList.Truncated"/>를 켠다 (큰 후보, S6). 조합마다 단계 예산이 따로다.
        /// <c>choose</c>가 없고 기본값도 없는 매개변수는 호스트가 줘야 하며 빠지면 <see cref="WorldException"/>이다.
        /// </summary>
        public LegalMoveList LegalMoves(string command, IReadOnlyDictionary<string, Value> args, int max) =>
            LegalMoves(command, args, max, null);

        /// <param name="keep">있으면 사전 검사를 통과한 수 중 이것이 참인 것만 센다 (거른 읽기, G202: 상한은 거른 뒤에).</param>
        internal LegalMoveList LegalMoves(string command, IReadOnlyDictionary<string, Value> args, int max,
            Func<IReadOnlyDictionary<string, Value>, bool>? keep)
        {
            if (max < 1) throw new WorldException(Codes.RunRange, "max must be at least 1, not " + max);
            var decl = FindCommand(command);
            var chooses = ChoosesOf(decl);
            var rejections = new List<ArgumentRejection>();
            var fitted = CheckArguments(decl, args, rejections, partial: true);
            if (rejections.Count > 0)
                return new LegalMoveList(command, Array.Empty<IReadOnlyDictionary<string, Value>>(), false, rejections);
            // choose가 있는 매개변수는 기본값이 있어도 펼친다: 호스트가 값을 준 것만 고정이다
            foreach (var c in chooses)
                if (!args.ContainsKey(c.Parameter)) fitted.Remove(c.Parameter);
            foreach (var p in decl.Parameters)
            {
                if (fitted.ContainsKey(p.Name) || chooses.Exists(c => c.Parameter == p.Name)) continue;
                throw new WorldException(Codes.RunMissingValue,
                    "LegalMoves of " + command + " needs the argument '" + p.Name + "' (" + p.Type.Display + ")",
                    "'" + p.Name + "' has no choose(), so its candidates are unknown and the host must give it (spec-gaps G184)");
            }

            var moves = new List<IReadOnlyDictionary<string, Value>>();
            void Expand(int index, SortedDictionary<string, Value> state)
            {
                if (index == chooses.Count)
                {
                    var problems = new List<ArgumentRejection>();
                    var full = CheckArguments(decl, state, problems);
                    // from_=가 매개변수 타입에 맞지 않는 값을 주면 그 조합은 낼 수 없다
                    if (problems.Count == 0 && EvaluateRequires(decl, full).Passed && (keep == null || keep(full))) moves.Add(full);
                    return;
                }
                var choose = chooses[index];
                if (state.ContainsKey(choose.Parameter))
                {
                    Expand(index + 1, state);
                    return;
                }
                foreach (var candidate in CandidatesOf(decl, state, choose))
                {
                    var next = new SortedDictionary<string, Value>(state, StringComparer.Ordinal) { [choose.Parameter] = candidate };
                    Expand(index + 1, next);
                    if (moves.Count > max) return;
                }
            }
            Expand(0, fitted);

            bool truncated = moves.Count > max;
            if (truncated) moves.RemoveAt(moves.Count - 1);
            return new LegalMoveList(command, moves, truncated, Array.Empty<ArgumentRejection>());
        }

        /// <summary>
        /// 열린 질문 <paramref name="request"/>에 응답 커맨드로 낼 수 있는 수 (spec-gaps G232). 질문이 받는 응답 커맨드마다
        /// <see cref="LegalMoves"/>처럼 펼치되, 답하는 엔티티는 엔진이 정한다: <c>acts_for</c>가 매개변수를 그대로 돌려주고
        /// (<c>lambda party: party</c>) 그 매개변수에 <c>choose</c>가 없으면 질문 대상으로 채우고, 아니면 펼친 뒤 <c>acts_for</c>가
        /// 질문 대상인 수만 남긴다. <paramref name="by"/>가 있으면 그 대상의 수만 (대상이 아니면 빈 목록).
        /// 입구 정책은 보지 않는다(어느 연결이 낼 수 있는지는 <see cref="ViewAs"/>). 선택지 답도 엔진 커맨드 <see cref="PickCommand"/>의
        /// 수로 먼저 든다(G272): 호스트는 어느 수든 <see cref="Respond"/>로 낸다. 없는 질문 번호는 <see cref="WorldException"/>이다.
        /// </summary>
        public ResponseMoveList LegalResponses(long request, EntityId? by, int max) => LegalResponses(request, by, max, null, null);

        /// <summary>
        /// 선택지로 답하는 엔진 커맨드 (spec-gaps G272·G273): 인자는 <see cref="PickBy"/>(답하는 엔티티)와 <see cref="PickChoice"/>(선택지 번호).
        /// 값이 아니라 번호인 것은 못 보는 엔티티가 든 선택지도 가려진 채 고를 수 있어야 해서다. <see cref="Respond"/>가 <see cref="Answer"/>로 보낸다.
        /// </summary>
        public const string PickCommand = Loading.ManifestReader.ReservedId + ".pick";

        public const string PickBy = "by";
        public const string PickChoice = "choice";

        /// <param name="answers">있으면 이 대상만 (보기 쪽: 그 연결이 답할 수 있는 대상, G200).</param>
        /// <param name="keep">있으면 이것이 참인 수만 센다 (거른 읽기, G202).</param>
        internal ResponseMoveList LegalResponses(long request, EntityId? by, int max, Func<EntityId, bool>? answers,
            Func<IReadOnlyDictionary<string, Value>, bool>? keep)
        {
            if (max < 1) throw new WorldException(Codes.RunRange, "max must be at least 1, not " + max);
            var flow = FindFlow(request);
            if (flow == null) throw NoRequest(request);
            var open = flow.Request!;
            var who = new List<EntityId>();
            foreach (var id in open.Who)
                if ((by == null || id == by) && (answers == null || answers(id))) who.Add(id);

            var moves = new List<ResponseMove>();
            var needs = new List<ResponseNeeds>();
            bool truncated = false;
            // 선택지 답도 같은 목록의 수다 (G272): 대상마다 선택지 순서로 engine.pick(by=, choice=)
            foreach (var id in who)
            {
                for (int i = 0; i < open.Choices.Count && !truncated; i++)
                {
                    if (moves.Count == max)
                    {
                        truncated = true;
                        break;
                    }
                    moves.Add(new ResponseMove(PickCommand, new SortedDictionary<string, Value>(StringComparer.Ordinal)
                    {
                        [PickBy] = new EntityValue(id), [PickChoice] = new IntValue(i),
                    }, id));
                }
            }
            if (truncated) return new ResponseMoveList(request, moves, truncated, needs);
            foreach (var command in open.Responses)
            {
                var decl = FindCommand(command);
                var chooses = ChoosesOf(decl);
                string? actor = ActorParameter(decl);
                if (actor != null && chooses.Exists(c => c.Parameter == actor)) actor = null;
                var missing = new List<string>();
                foreach (var p in decl.Parameters)
                    if (p.Name != actor && p.Default == null && !chooses.Exists(c => c.Parameter == p.Name)) missing.Add(p.Name);
                if (missing.Count > 0)
                {
                    needs.Add(new ResponseNeeds(command, missing));
                    continue;
                }

                // 펼치기 한 번: 정한 인자와 거르기. 상한은 남은 몫이고, 남은 몫이 없으면 하나라도 더 있는지만 본다
                bool Expand(IReadOnlyDictionary<string, Value> args, Func<IReadOnlyDictionary<string, Value>, EntityId?> answerer)
                {
                    int room = max - moves.Count;
                    var list = LegalMoves(command, args, Math.Max(room, 1),
                        move => answerer(move) != null && (keep == null || keep(move)));
                    if (room == 0)
                    {
                        if (list.Moves.Count > 0) truncated = true;
                        return !truncated;
                    }
                    foreach (var move in list.Moves) moves.Add(new ResponseMove(command, move, answerer(move)!.Value));
                    if (list.Truncated) truncated = true;
                    return !truncated;
                }

                if (actor != null)
                {
                    foreach (var id in who)
                    {
                        var fixedBy = id;
                        if (!Expand(new SortedDictionary<string, Value>(StringComparer.Ordinal) { [actor] = new EntityValue(id) }, _ => fixedBy)) break;
                    }
                }
                else
                {
                    Expand(new SortedDictionary<string, Value>(StringComparer.Ordinal), move =>
                        ActsFor(decl, Sorted(move))
                            is EntityValue e && who.Contains(e.Id) ? e.Id : (EntityId?)null);
                }
                if (truncated) break;
            }
            return new ResponseMoveList(request, moves, truncated, needs);
        }

        internal static WorldException NoRequest(long request) =>
            new WorldException(Codes.RunUnknownName,
                "there is no open request " + request.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "it may have been answered already; OpenRequests lists the open ones");

        /// <summary>합법 수(<see cref="CheckArguments"/>가 만든 정렬 맵)를 <see cref="ActsFor"/>에 넘길 모양으로.</summary>
        private static SortedDictionary<string, Value> Sorted(IReadOnlyDictionary<string, Value> move)
        {
            if (move is SortedDictionary<string, Value> sorted) return sorted;
            var copy = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in move) copy[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary><c>acts_for</c>가 커맨드 매개변수 하나를 그대로 돌려주는 람다면 그 이름 (<c>lambda party: party</c>).</summary>
        private static string? ActorParameter(CommandDecl decl)
        {
            var lambda = decl.ActsFor?.Lambda;
            if (lambda == null || !(lambda.Body is NameExpr name)) return null;
            bool own = false, declared = false;
            foreach (var p in lambda.Parameters) own |= p.Name == name.Name;
            foreach (var p in decl.Parameters) declared |= p.Name == name.Name;
            return own && declared ? name.Name : null;
        }

        private CommandDecl FindCommand(string command)
        {
            if (_commands.TryGetValue(command, out var decl)) return decl;
            throw new WorldException(Codes.RunUnknownName, "there is no command '" + command + "'",
                Suggest.Hint(command, _commands.Keys));
        }

        private static List<ChooseInfo> ChoosesOf(CommandDecl decl)
        {
            var def = decl.Function.Definition!;
            return def.Body.Count > 0 && def.Body[0] is WithStmt with
                ? ChooseDependencies.Of(with, def.Parameters)
                : new List<ChooseInfo>();
        }

        /// <summary>
        /// <c>choose</c> 하나의 후보 (G183). <c>where=</c>는 매개변수 타입의 살아 있는 엔티티(id 순)를 함수로 거르고, <c>from_=</c>는 목록을
        /// 그대로(같은 값은 처음 것만) 준다. 커맨드 하나의 나열에 단계 예산 하나다 (G101).
        /// </summary>
        private IReadOnlyList<Value> CandidatesOf(CommandDecl decl, IReadOnlyDictionary<string, Value> state, ChooseInfo choose)
        {
            var scriptArgs = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var p in decl.Parameters)
                scriptArgs[p.Name] = state.TryGetValue(p.Name, out var v) ? ToScript(v) : NoneValue.Instance;

            bool outermost = _meter == null;
            if (outermost) _meter = new StepMeter(_stepBudget);
            try
            {
                var option = Evaluator.RunChooseOption(decl.Function, scriptArgs, choose, decl.Span, _meter);
                var result = new List<Value>();
                if (choose.Where)
                {
                    EntityRefTypeRef? type = null;
                    foreach (var p in decl.Parameters)
                        if (p.Name == choose.Parameter) type = (EntityRefTypeRef)p.Type;
                    foreach (var id in All(type!.Target.Name))
                    {
                        var keep = Evaluator.Run(option, new Value[] { new EntityView(this, id) }, choose.Call.Span, _meter);
                        if (PureOps.Truthy(keep)) result.Add(new EntityValue(id));
                    }
                    return result;
                }
                IReadOnlyList<Value> items = option is ListValue list ? list.Items
                    : option is IRuntimeCollection collection ? collection.Iterate()
                    : throw new ScriptRuntimeException(Codes.RunType, "from_= of choose() must be a list, not " + option.KindName,
                        "write choose(" + choose.Parameter + ", from_=[...]) (spec-gaps G183)");
                var parameterType = CommandParameterType(decl.Function, choose.Parameter);
                foreach (var item in items)
                {
                    // DefId 매개변수의 짧은 이름은 커맨드의 모드 기준 (G261)
                    var full = parameterType == null ? item : Loading.DefIds.Normalize(item, parameterType, decl.Mod);
                    var plain = ToPlain("a candidate of '" + choose.Parameter + "'", full);
                    if (!result.Exists(x => PureOps.ValuesEqual(ToScript(x), ToScript(plain)))) result.Add(plain);
                }
                return result;
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, "the candidates of '" + choose.Parameter + "' in " + decl.Id);
            }
            finally
            {
                if (outermost) _meter = null;
            }
        }
    }
}
