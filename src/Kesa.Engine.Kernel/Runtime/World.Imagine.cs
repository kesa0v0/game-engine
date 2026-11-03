using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
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

        /// <summary>복사하는 엔티티·값·줄 하나를 이만큼으로 센다 (G467, 잰 값: 저장 나무를 만들고 다시 읽는 비용).</summary>
        private const int CopyStepWeight = 300;

        /// <summary>이 판을 복사하는 단계 몫 (G467): 엔티티와 그 값·런타임 컴포넌트·줄의 수.</summary>
        private long CopySteps()
        {
            long items = 0;
            foreach (long id in LiveIds())
            {
                var entity = _entities[id];
                items += 1 + entity.Values.Count + entity.Runtime.Count;
                foreach (var rows in entity.Rows.Values)
                    items += rows.ByDef.Count + rows.ByEntity.Count + rows.ByInt.Count;
            }
            return items * CopyStepWeight;
        }

        private static ScriptRuntimeException ImagineError(string message, string hint) =>
            new ScriptRuntimeException(Codes.RunImagine, message, hint);

        /// <summary><c>with imagine():</c>에 들어간다: 지금 판(바깥 시험 판이 있으면 그것)을 복사해 맨 위에 올린다.</summary>
        private void EnterImagine(IReadOnlyDictionary<string, Value> keywords)
        {
            if (_botRun == null)
                throw ImagineError("imagine() can only be used while a @bot thinks",
                    "a bot tries moves on a copy of the board; game code changes the real board (spec-gaps G454)");
            long? given = null;
            foreach (var pair in keywords)
            {
                if (pair.Key != SeedArgument)
                    throw ImagineError("imagine() takes no argument '" + pair.Key + "'",
                        "write with imagine(): or with imagine(seed=1): (spec-gaps G454, G461)");
                if (!(pair.Value is IntValue k))
                    throw new ScriptRuntimeException(Codes.RunType, "imagine(seed=...) takes an int, not " + pair.Value.KindName,
                        "the same seed= gives the same luck on the imagined board (spec-gaps G461)");
                given = k.Value;
            }
            if (_loaded == null)
                throw new InvalidOperationException("this world was made without loaded content, so it cannot copy itself for imagine()");

            var source = Current;
            var tree = source.SaveTree();
            var copy = Load(_loaded, () => tree, false, false, false, this);
            if (copy.World == null)
            {
                var problems = new StringBuilder();
                foreach (var d in copy.Diagnostics) problems.Append('\n').Append(d);
                // 엔진 결함이지만 호스트를 죽이지 않고 그 생각만 실패하게 한다
                throw ImagineError("imagine() could not copy the board:" + problems, "this is an engine bug; please report it (spec-gaps G453)");
            }
            var trial = copy.World;
            // 봇이 본 판만 (G462·G464). 안쪽 블록은 이미 거른 바깥 시험 판을 복사하므로 가림 표시만 잇는다
            if (source == this && _viewRules.Count > 0)
            {
                trial.Veil(this);
                trial._maskedQuestions = QuestionsTheBotCannotSee();
            }
            else
            {
                if (source._veiled != null) trial._veiled = new SortedSet<string>(source._veiled, StringComparer.Ordinal);
                if (source._maskedQuestions != null) trial._maskedQuestions = new SortedSet<long>(source._maskedQuestions);
            }
            trial._meter = _meter; // 시험 판의 일도 봇 한 번 부르기의 예산으로 센다 (G375·G460)
            // 진짜 판의 다음 난수를 알면 안 된다: 시드는 봇 난수열에서, seed=면 봇 시드와 그 값에서 (G461)
            trial._seed = given is long k2 ? ImagineSeed(_botRun.Seed, k2) : unchecked((long)BeginDraws().Next());
            // 시험 판의 새 엔티티는 봇마다 이어 세는 따로 구간에서 받는다 (G463)
            trial._nextId = Math.Max(_botRun.TrialIds, source._nextId);
            _trials.Add(trial);
        }

        // ---- 봇이 본 판 (G462·G464) ----

        /// <summary>시험 판에서 봇이 못 보는 Def 컴포넌트: 값은 지우고 붙지 않은 것처럼 본다. <c>attach</c>가 채우면 빠진다.</summary>
        private SortedSet<string>? _veiled;

        private static string VeilKey(long id, string component) => id.ToString(CultureInfo.InvariantCulture) + "/" + component;

        private bool Veiled(long id, string component) => _veiled != null && _veiled.Contains(VeilKey(id, component));

        /// <summary>
        /// 진짜 판에서 막 복사한 시험 판을 봇의 눈(<paramref name="root"/>의 보는 쪽)으로 거른다: 못 보는 엔티티는 없애고, 못 보는 컴포넌트는
        /// 런타임에 붙은 것이면 떼고 Def의 것이면 값을 지워 가린다. 지운 값은 어디에도 남지 않는다.
        /// </summary>
        private void Veil(World root)
        {
            var gone = new List<EntityId>();
            foreach (long id in LiveIds())
            {
                if (root.HiddenFromViewer(id))
                {
                    gone.Add(new EntityId(id));
                    continue;
                }
                var entity = _entities[id];
                foreach (var component in new List<ComponentType>(ComponentsOf(entity)))
                {
                    if (!root.HiddenFromViewer(id, component.Name)) continue;
                    if (entity.Runtime.ContainsKey(component.Name))
                    {
                        Remove(new EntityId(id), component.Name);
                        continue;
                    }
                    UnindexRefs(entity, component);
                    foreach (var member in component.Members) entity.Values.Remove(member);
                    entity.Rows.Remove(component.Name);
                    IndexRefs(entity, component); // Def 값의 Ref는 남는다
                    (_veiled ??= new SortedSet<string>(StringComparer.Ordinal)).Add(VeilKey(id, component.Name));
                }
            }
            foreach (var id in gone) Destroy(id);
        }

        /// <summary>복사할 때 열려 있던 질문 가운데 이 봇이 블록 밖에서 못 보던 것: 시험 판에서 뼈대만 읽힌다 (G466).</summary>
        private SortedSet<long>? _maskedQuestions;

        private bool QuestionMasked(long request) => _maskedQuestions != null && _maskedQuestions.Contains(request);

        private SortedSet<long> QuestionsTheBotCannotSee()
        {
            var seen = new SortedSet<long>();
            foreach (var q in _viewer!.OpenRequests) seen.Add(q.Id);
            var masked = new SortedSet<long>();
            foreach (var q in OpenRequests)
                if (!seen.Contains(q.Id)) masked.Add(q.Id);
            return masked;
        }

        /// <summary>가린 Def 컴포넌트에 <c>attach</c>한 추측을 쓴다 (G464): 준 칸만 쓰고 나머지는 Def 값이다.</summary>
        private void Unveil(EntityId id, ComponentType component, Init[] inits)
        {
            Given(inits, h => h.Component == component && !component.IsKeyed, "the value does not belong to '" + component.Name + "'");
            _veiled!.Remove(VeilKey(id.Value, component.Name));
            foreach (var init in inits) Set(id, init.Member, init.Value);
        }

        /// <summary>블록을 나간다: 맨 위 시험 판을 버린다. 그 판에서 쓴 시험 id는 봇과 바깥 시험 판이 이어 받는다 (G463).</summary>
        private void ExitImagine()
        {
            var trial = _trials[_trials.Count - 1];
            _trials.RemoveAt(_trials.Count - 1);
            var run = _botRun!;
            run.TrialIds = Math.Max(run.TrialIds, trial._nextId);
            if (_trials.Count > 0) _trials[_trials.Count - 1]._nextId = Math.Max(_trials[_trials.Count - 1]._nextId, run.TrialIds);
        }

        private const string SeedArgument = "seed";

        /// <summary>시험 판에서 생기는 엔티티 id의 시작. 진짜 판은 1부터라 닿지 않는다 (G463).</summary>
        internal const long TrialIdBase = 1L << 62;

        /// <summary><c>imagine(seed=k)</c>의 시드: SHA-256(봇 시드, k, "imagine")의 앞 8바이트 (G461). 봇 난수열을 쓰지 않는다.</summary>
        private static long ImagineSeed(long botSeed, long k)
        {
            var tag = Encoding.UTF8.GetBytes("imagine");
            var input = new byte[16 + tag.Length];
            WriteLittleEndian(input, 0, botSeed);
            WriteLittleEndian(input, 8, k);
            Array.Copy(tag, 0, input, 16, tag.Length);
            byte[] digest;
            using (var sha = System.Security.Cryptography.SHA256.Create()) digest = sha.ComputeHash(input);
            long seed = 0;
            for (int i = 0; i < 8; i++) seed |= (long)digest[i] << (8 * i);
            return seed;
        }

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

        /// <summary>
        /// 블록 안의 <c>advance(n)</c> (G460): 맨 위 시험 판을 n틱 굴리고 실제로 간 틱 수를 돌려준다. 막는 질문이 열려 있으면 덜 간다.
        /// 한도는 커맨드의 <c>advance</c>와 같은 <c>max_advance_per_call</c>이고, 틱 핸들러의 일도 봇의 예산으로 센다.
        /// </summary>
        private Value AdvanceImagined(IReadOnlyList<Value> positional)
        {
            if (positional.Count != 1 || !(positional[0] is IntValue n) || n.Value < 1)
                throw new ScriptRuntimeException(Codes.RunType, "advance() takes the number of ticks (an int of at least 1)",
                    "write went = advance(10) (spec-gaps G460)");
            var trial = Current;
            if (n.Value > trial._maxAdvancePerCall)
                throw ImagineError("advance(" + n.Value + ") is past the limit of " + trial._maxAdvancePerCall + " ticks per call",
                    "advance in smaller steps, or the game can raise the limit: patch.set(\"engine:Rules\", \"max_advance_per_call\", …) (spec-gaps G451, G460)");
            int went = trial.AdvanceTicks(n.Value, new List<Reaction>(), out _);
            return new IntValue(went);
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
