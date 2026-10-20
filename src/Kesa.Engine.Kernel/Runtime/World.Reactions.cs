using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 신호 핸들러 실행 (spec S5, spec-gaps G146~G150). 제출·답·응답이 끝나거나 <c>ask</c>로 멈춘 뒤, 같은 호출 안에서 대기열을 앞에서부터
    /// 비운다. 신호마다 그 신호의 핸들러를 로드 순서 → 정의 순서로 각자 새 Flow, 각자 트랜잭션으로 돌리고, 핸들러가 낸 신호는 대기열 뒤에
    /// 붙는다. 세대 상한보다 깊은 신호는 돌리지 않는다.
    /// </summary>
    public sealed partial class World
    {
        public const int DefaultMaxSignalDepth = 64;

        private readonly int _maxSignalDepth = DefaultMaxSignalDepth;

        /// <summary>핸들러, 로드 순서 → 정의 순서 (S5 동률 규칙).</summary>
        private IReadOnlyList<HandlerDecl> _handlers = Array.Empty<HandlerDecl>();

        private readonly SortedDictionary<string, HandlerDecl> _handlerById = new SortedDictionary<string, HandlerDecl>(StringComparer.Ordinal);

        /// <summary>지금 돌고 있는 핸들러가 받은 신호. 그 핸들러가 낸 신호의 세대와 경로를 정한다. 커맨드·답이면 null.</summary>
        private Emitted? _reacting;

        private string? _reactingHandler;

        /// <summary>
        /// 핸들러를 실행 순서로 둔다 (spec S5, G152): 단계의 위상 정렬 순서, 같은 단계 안에서는 로드 순서 → 정의 순서(받은 순서),
        /// <c>stage=</c>가 없는 핸들러는 맨 뒤에 받은 순서로. 신호마다 거르기만 하면 그 신호의 순서가 된다.
        /// </summary>
        private void SetHandlers(IReadOnlyList<HandlerDecl> handlers)
        {
            var keyed = new List<KeyValuePair<long, HandlerDecl>>(handlers.Count);
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                long stage = h.Stage == null ? int.MaxValue : _schema.StageIndex(h.Stage);
                keyed.Add(new KeyValuePair<long, HandlerDecl>(stage * handlers.Count + i, h));
            }
            keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
            var ordered = new List<HandlerDecl>(keyed.Count);
            foreach (var pair in keyed) ordered.Add(pair.Value);
            _handlers = ordered;
            foreach (var h in handlers) _handlerById[HandlerId(h)] = h;
        }

        private static string HandlerId(HandlerDecl h) => h.Mod + "." + h.Function.Name;

        /// <summary>대기열의 신호 하나: 값, 세대, 그 신호를 낸 핸들러와 그 핸들러가 받은 신호(경로).</summary>
        private sealed class Emitted
        {
            public Emitted(SignalValue signal, int generation, string? emitter, Emitted? parent)
            {
                Signal = signal;
                Generation = generation;
                Emitter = emitter;
                Parent = parent;
            }

            public SignalValue Signal { get; }
            public int Generation { get; }

            /// <summary>이 신호를 낸 핸들러 id. 커맨드·답이 냈으면 null.</summary>
            public string? Emitter { get; }

            public Emitted? Parent { get; }
        }

        /// <summary>대기열을 비운다. 커맨드의 트랜잭션이 끝난 뒤(바깥)에서만 부른다.</summary>
        private List<Reaction> RunReactions()
        {
            var reactions = new List<Reaction>();
            if (_emitted.Count > 0)
            {
                // 앞에서 지우지 않고 읽는 자리만 옮긴다: 핸들러 트랜잭션의 되감기가 대기열 끝의 위치로 지운다.
                // 엔진 결함으로 중간에 빠져나가도 다음 제출이 옛 신호를 다시 돌리지 않게 반드시 비운다
                try
                {
                    Drain(reactions);
                }
                finally
                {
                    _emitted.Clear();
                }
            }
            // 실행과 그 신호 처리가 끝났다: 사라진 대상을 가진 멈춘 Flow를 버린다 (G164)
            SweepStale(reactions);
            return reactions;
        }

        private void Drain(List<Reaction> reactions)
        {
            for (int next = 0; next < _emitted.Count; next++)
            {
                var e = _emitted[next];
                foreach (var h in _handlers)
                {
                    if (h.Signal.Name != e.Signal.Signal.Name) continue;
                    string id = HandlerId(h);
                    if (e.Generation > _maxSignalDepth)
                    {
                        reactions.Add(new Reaction(e.Signal.Signal.Name, e.Signal.Plain(), id, e.Generation, ReactionOutcome.Dropped)
                        {
                            Message = DropMessage(e),
                            Hint = "a chain that should go on over time can wait() or run from a tick handler, which starts again at generation 1; " +
                                   "a long chain that ends can get a higher limit from the host (maxSignalDepth) (spec-gaps G147, G150)",
                        });
                        continue;
                    }
                    reactions.Add(RunHandler(h, id, e));
                }
            }
        }

        private Reaction RunHandler(HandlerDecl h, string id, Emitted e)
        {
            var reaction = new Reaction(e.Signal.Signal.Name, e.Signal.Plain(), id, e.Generation, ReactionOutcome.Finished);
            // 받는 매개변수 중 사라진 엔티티가 있으면 돌리지 않는다. 받지 않는 값은 보지 않는다 (S5 on_stale 기본 취소, G151)
            foreach (var p in h.Function.Definition!.Parameters)
            {
                if (e.Signal.Value(p.Name) is EntityValue entity && !IsAlive(entity.Id))
                {
                    return new Reaction(reaction.Signal, reaction.Values, id, e.Generation, ReactionOutcome.Canceled)
                    {
                        Message = "'" + p.Name + "' (" + entity.Id + ") was destroyed before the handler ran, so it did not run",
                    };
                }
            }
            _reacting = e;
            _reactingHandler = id;
            try
            {
                var opened = WithWarnings(() => Atomically(() =>
                {
                    // 인자도 핸들러 안에서 만든다: 실패하면 그 핸들러만의 오류다
                    var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var p in h.Function.Definition!.Parameters) args[p.Name] = ToScript(e.Signal.Value(p.Name));
                    bool outermost = _meter == null;
                    if (outermost) _meter = new StepMeter(_actionStepBudget);
                    try
                    {
                        Evaluator.RunCommand(h.Function, args, out var suspended, h.Span, _meter, () => StartStream(id));
                        if (suspended == null) return ((long?, Request?))(null, null);
                        long flow = _nextFlow;
                        return (flow, OpenFlow(id, suspended, SubjectsOf(args.Values), handler: true)); // 질문, 또는 wait면 null
                    }
                    catch (ScriptRuntimeException ex)
                    {
                        throw ToWorldException(ex, "the handler " + id + " for " + e.Signal.Signal.Name);
                    }
                    finally
                    {
                        if (outermost && CloseActionMeter() is var heavy && heavy > 0)
                            WarnHeavy(heavy, "the handler " + id + " for " + e.Signal.Signal.Name, h.Span);
                    }
                }), out var warnings);
                if (opened.Item1 == null)
                {
                    reaction.Warnings = warnings;
                    return reaction;
                }
                return new Reaction(reaction.Signal, reaction.Values, id, e.Generation, ReactionOutcome.Suspended)
                {
                    OpenedRequest = opened.Item2,
                    SleepsUntil = SleepsUntil(opened.Item1.Value),
                    Warnings = warnings,
                };
            }
            catch (WorldException ex)
            {
                return new Reaction(reaction.Signal, reaction.Values, id, e.Generation, ReactionOutcome.ScriptError) { Error = ex };
            }
            finally
            {
                _reacting = null;
                _reactingHandler = null;
            }
        }

        /// <summary>
        /// 상한을 넘은 신호의 설명. 경로에서 (신호, 낸 핸들러)가 처음 되풀이되는 곳까지를 고리로 보인다:
        /// <c>Damaged → combat.retaliate → Damaged</c>. 되풀이가 없으면 경로의 끝부분을 보인다.
        /// </summary>
        private string DropMessage(Emitted e)
        {
            var chain = new List<Emitted>(); // 0이 가장 새 것
            for (var c = e; c != null; c = c.Parent) chain.Add(c);
            int k = -1;
            for (int i = 1; i < chain.Count && k < 0; i++)
                if (chain[i].Signal.Signal.Name == e.Signal.Signal.Name && chain[i].Emitter == e.Emitter) k = i;

            const int shown = 8;
            int last = k >= 0 ? k : Math.Min(chain.Count - 1, shown);
            var parts = new List<string>();
            for (int i = last; i >= 0; i--)
            {
                if (i != last) parts.Add(chain[i].Emitter ?? "?");
                parts.Add(chain[i].Signal.Signal.Name);
            }
            string path = string.Join(" → ", parts);
            string limit = _maxSignalDepth.ToString(CultureInfo.InvariantCulture);
            return "signal " + e.Signal.Signal.Name + " was not handled: it is generation " +
                   e.Generation.ToString(CultureInfo.InvariantCulture) + ", deeper than the limit " + limit + "; " +
                   (k >= 0 ? "the chain repeats: " : "the chain ends with: ") + (k < 0 && last < chain.Count - 1 ? "… → " : "") + path;
        }
    }
}
