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
        public LegalMoveList LegalMoves(string command, IReadOnlyDictionary<string, Value> args, int max)
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
                    if (problems.Count == 0 && EvaluateRequires(decl, full).Passed) moves.Add(full);
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
                foreach (var item in items)
                {
                    var plain = ToPlain("a candidate of '" + choose.Parameter + "'", item);
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
