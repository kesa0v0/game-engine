using System;
using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 봇의 시험 두기 (spec-gaps G453~G463): <c>with imagine():</c>이 지금 판을 통째 복사한 시험 판을 열고, 블록 안의 읽기는 모두 그 판을 보며
    /// <c>play</c>가 거기에 수를 둔다. 봇 본문은 진짜 판의 실행기에서 계속 돌고, 스크립트 값과 내장은 읽을 때마다 <see cref="Current"/>로 간다.
    /// 시험 판은 호스트와 이어지지 않으므로 효과·그림·버전이 새지 않는다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>시험 판을 만들 때 쓰는 내용. 내용 없이 만든 월드(테스트용 생성자)는 시험 판을 열 수 없다.</summary>
        private readonly ContentLoadResult? _loaded;

        /// <summary>시험 판이면 그 판을 연 진짜 판.</summary>
        private World? _root;

        /// <summary>진짜 판에서 열려 있는 시험 판들, 바깥부터.</summary>
        private readonly List<World> _trials = new List<World>();

        /// <summary>시험 판에서 수를 내는 연결 꼬리표. 시험 판에서는 입구 검사를 통과한다 (G457).</summary>
        private const string TrialIssuer = "engine.imagine";

        /// <summary>스크립트 값이 지금 읽는 판: 열린 시험 판이 있으면 맨 안쪽 것, 없으면 진짜 판 (G454).</summary>
        private World Current
        {
            get
            {
                var root = _root ?? this;
                return root._trials.Count == 0 ? root : root._trials[root._trials.Count - 1];
            }
        }

        private bool IsTrial => _root != null;

        private static ScriptRuntimeException ImagineError(string message, string hint) =>
            new ScriptRuntimeException(Codes.RunImagine, message, hint);

        /// <summary><c>with imagine():</c>에 들어간다: 지금 판(바깥 시험 판이 있으면 그것)을 복사해 맨 위에 올린다.</summary>
        private void EnterImagine(IReadOnlyDictionary<string, Value> keywords)
        {
            if (_botRun == null)
                throw ImagineError("imagine() can only be used while a @bot thinks",
                    "a bot tries moves on a copy of the board; game code changes the real board (spec-gaps G454)");
            foreach (var pair in keywords)
                throw ImagineError("imagine() takes no argument '" + pair.Key + "'", "write with imagine(): (spec-gaps G454)");
            if (_viewRules.Count > 0)
                throw ImagineError("imagine() cannot be used yet in a game with @view_rule",
                    "the copy would hold what this bot cannot see (spec-gaps G462)");
            if (_loaded == null)
                throw new InvalidOperationException("this world was made without loaded content, so it cannot copy itself for imagine()");

            var source = Current;
            var copy = Load(_loaded, source.SaveBytes(), false, false, false, this);
            if (copy.World == null)
            {
                var problems = new StringBuilder();
                foreach (var d in copy.Diagnostics) problems.Append('\n').Append(d);
                throw new InvalidOperationException("imagine() could not copy the board:" + problems);
            }
            var trial = copy.World;
            trial._meter = _meter; // 시험 판의 일도 봇 한 번 부르기의 예산으로 센다 (G375·G460)
            _trials.Add(trial);
        }

        /// <summary>블록을 나간다: 맨 위 시험 판을 버린다.</summary>
        private void ExitImagine() => _trials.RemoveAt(_trials.Count - 1);

        /// <summary>
        /// <c>play("커맨드", 이름=…)</c>·<c>play(q, "커맨드", 이름=…)</c> (G458): 맨 위 시험 판에 <c>submit</c>·<c>respond</c>처럼 수를 둔다.
        /// 결과는 <see cref="PlayValue"/>다.
        /// </summary>
        private Value PlayBuiltin(string mod, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            var trial = Current;
            if (trial == this)
                throw ImagineError("play() can only be used inside 'with imagine():'",
                    "write with imagine(): and play the move inside the block; submit() plays it for real (spec-gaps G454)");
            var submission = positional.Count > 0 && positional[0] is QuestionValue
                ? ReadRespond("play()", mod, positional, keywords)
                : ReadSubmit("play()", mod, positional, keywords);
            DeliveryResult delivered;
            try
            {
                delivered = trial.Deliver(TrialIssuer, submission);
            }
            catch (WorldException e)
            {
                throw new ScriptRuntimeException(e.Code, e.Message, e.Hint);
            }
            return new PlayValue(this, delivered);
        }

        /// <summary>봇 쪽에 남는 내장: 봇의 기억·내기·자리·시험 두기 자체. 나머지 판을 읽고 쓰는 내장은 맨 위 시험 판으로 간다.</summary>
        private static readonly string[] StaysWithBot =
        {
            PureBuiltins.UiName, PureBuiltins.BotName, PureBuiltins.SubmitName, PureBuiltins.RespondName, PureBuiltins.SeatsName,
            PureBuiltins.PlayName,
        };

        /// <summary>판을 읽고 쓰는 내장을 부를 때마다 <see cref="Current"/>의 같은 내장으로 넘긴다 (G454). 값만 다루는 내장은 그대로 둔다.</summary>
        private void ForwardToTrials(SortedDictionary<string, Value> table)
        {
            var plain = PureBuiltins.Create();
            foreach (var name in new List<string>(table.Keys))
            {
                if (!(table[name] is PureBuiltin b) || Array.IndexOf(StaysWithBot, name) >= 0) continue;
                if (plain.ContainsKey(name) && name != PureBuiltins.TextName) continue;
                table[name] = Forwarding(b);
            }
        }

        private PureBuiltin Forwarding(PureBuiltin b)
        {
            PureBuiltin Target()
            {
                var w = Current;
                return w == this ? b : (PureBuiltin)w.Evaluator.Builtins[b.Name];
            }

            var forwarding = b.KeywordBody != null
                ? new PureBuiltin(b.Name, (p, k) => Target().KeywordBody!(p, k))
                : new PureBuiltin(b.Name, p => Target().Body(p));
            if (b.CallerBody != null) forwarding.CallerBody = (m, p, k) => Target().CallerBody!(m, p, k);
            forwarding.Writes = b.Writes;
            forwarding.Random = b.Random;
            forwarding.WritesUi = b.WritesUi;
            forwarding.Suspends = b.Suspends;
            forwarding.RefusedInImmediate = b.RefusedInImmediate;
            forwarding.Immediate = b.Immediate;
            forwarding.ImmediateArguments = b.ImmediateArguments;
            forwarding.Kind = b.Kind;
            return forwarding;
        }

        /// <summary>시험 판에서는 쓰기 등급 호스트 함수가 실패한다: C# 쪽에서 일어난 일은 복사로 막을 수 없다 (G453).</summary>
        private void NoHostWritesInTrial(string name)
        {
            if (IsTrial)
                throw ImagineError("the host function " + name + "() writes, so it cannot run on an imagined board",
                    "what a host function does outside the board cannot be thrown away with the copy (spec-gaps G453)");
        }

        /// <summary>블록 안의 <c>submit</c>·<c>respond</c>는 오류다 (G456).</summary>
        private void NoSubmitWhileImagining(string what)
        {
            if (_trials.Count > 0)
                throw ImagineError(what + " cannot be used inside 'with imagine():'",
                    "keep what you learned in a local variable and submit after the block (spec-gaps G456)");
        }

        private static string Snake(string name)
        {
            var text = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && text.Length > 0) text.Append('_');
                text.Append(char.ToLowerInvariant(c));
            }
            return text.ToString();
        }

        /// <summary>
        /// <c>play</c>의 결과 (G458·G459): <c>ok</c>(본문까지 돌았나), <c>outcome</c>(<see cref="SubmitOutcome"/>·<see cref="AnswerOutcome"/>의
        /// 이름, 낼 곳을 못 찾았으면 <c>"refused"</c>), <c>failed</c>(실패한 require의 문구 키), <c>error</c>, <c>question</c>(이 수가 연 질문).
        /// </summary>
        private sealed class PlayValue : Value, IRuntimeAttributes
        {
            private static readonly string[] Attributes = { "ok", "outcome", "failed", "error", "question" };

            private readonly World _world;
            private readonly DeliveryResult _result;

            public PlayValue(World world, DeliveryResult result)
            {
                _world = world;
                _result = result;
            }

            public override string KindName => "play result";

            public Value GetAttribute(string name)
            {
                var submitted = _result.Submitted;
                var answered = _result.Answered;
                switch (name)
                {
                    case "ok": return _result.Succeeded ? BoolValue.True : BoolValue.False;
                    case "outcome":
                        return new StrValue(submitted != null ? Snake(submitted.Outcome.ToString())
                            : answered != null ? Snake(answered.Outcome.ToString())
                            : "refused");
                    case "failed":
                    {
                        var report = submitted?.Preconditions ?? answered?.Preconditions;
                        var keys = new List<Value>();
                        if (report != null)
                            foreach (var failure in report.Failures)
                                if (failure.MessageKey != null) keys.Add(new StrValue(failure.MessageKey));
                        return new ListValue(keys);
                    }
                    case "error":
                    {
                        if (_result.Succeeded) return NoneValue.Instance;
                        string? message = submitted?.Error?.Message ?? answered?.Error?.Message ?? answered?.Message ?? _result.Refusal;
                        return message == null ? (Value)NoneValue.Instance : new StrValue(message);
                    }
                    case "question":
                    {
                        var opened = submitted?.OpenedRequest ?? answered?.OpenedRequest;
                        return opened == null ? (Value)NoneValue.Instance : new QuestionValue(_world, opened);
                    }
                }
                throw new ScriptRuntimeException(Codes.RunScript, "a play result has no attribute '" + name + "'",
                    Suggest.Hint(name, Attributes) ?? "it has ok, outcome, failed, error and question");
            }
        }
    }
}
