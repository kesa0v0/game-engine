using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <c>@when</c> 반응과 정착 (spec S5, spec-gaps G169~G171). 정착 지점(제출·답·응답이 끝난 뒤, 틱의 마지막 단계)에서 모든 조건을
    /// 평가하고 거짓→참이 된 것의 본문을 각자 Flow·트랜잭션으로 돌린다. 발동한 것이 있으면 다시 평가하고, 새로 발동한 것이 없으면 끝난다.
    /// 지금은 매 정착마다 모든 조건을 다시 평가한다(조건이 순수하므로 "읽은 칸이 바뀔 때만"과 결과가 같다).
    /// </summary>
    public sealed partial class World
    {
        public const int DefaultMaxSettleRounds = 64;

        private readonly int _maxSettleRounds = DefaultMaxSettleRounds;

        /// <summary>반응, 단계 순서 → 로드 순서 → 정의 순서 (핸들러와 같은 규칙, S5).</summary>
        private IReadOnlyList<WhenDecl> _whens = Array.Empty<WhenDecl>();

        /// <summary>전역 조건의 거짓→참 기록이 쓰는 엔티티 자리. 엔티티 id는 1부터라 겹치지 않는다.</summary>
        private const long GlobalTarget = 0;

        /// <summary>
        /// 거짓→참 기록 (G170): 반응 id → 지난 정착 때 조건이 참이었던 엔티티(전역 조건은 <see cref="GlobalTarget"/>). 기록이 없으면 거짓으로 본다.
        /// </summary>
        private readonly SortedDictionary<string, SortedSet<long>> _whenTrue = new SortedDictionary<string, SortedSet<long>>(StringComparer.Ordinal);

        /// <summary>쿨다운 (G174): 반응 id → 대상 → 다시 볼 수 있게 되는 틱. 그 틱 전까지는 조건을 거짓으로 본다. 저장한다.</summary>
        private readonly SortedDictionary<string, SortedDictionary<long, long>> _whenCooldown =
            new SortedDictionary<string, SortedDictionary<long, long>>(StringComparer.Ordinal);

        /// <summary>
        /// 한 번 쓴 <c>once</c> 반응 (G175): 반응 id → 대상. <c>once="game"</c>이면 대상 자리에 <see cref="GlobalTarget"/> 하나다. 저장한다.
        /// </summary>
        private readonly SortedDictionary<string, SortedSet<long>> _whenDone = new SortedDictionary<string, SortedSet<long>>(StringComparer.Ordinal);

        private static long OnceTarget(WhenDecl w, long target) => w.Once == WhenOnce.Game ? GlobalTarget : target;

        private bool Done(WhenDecl w, long target) =>
            w.Once != WhenOnce.None && _whenDone.TryGetValue(w.Id, out var done) && done.Contains(OnceTarget(w, target));

        /// <summary>쿨다운 중인가. 끝난 쿨다운은 지운다.</summary>
        private bool Cooling(WhenDecl w, long target)
        {
            if (w.Cooldown == null || !_whenCooldown.TryGetValue(w.Id, out var until) || !until.TryGetValue(target, out long tick)) return false;
            if (_tick < tick) return true;
            until.Remove(target);
            return false;
        }

        /// <summary><c>when</c> 반응의 본문이 처음 도는 중인가: 이 안에서는 <c>advance()</c>를 부를 수 없다.</summary>
        private bool _settling;

        private void SetWhens(IReadOnlyList<WhenDecl> whens)
        {
            var keyed = new List<KeyValuePair<long, WhenDecl>>(whens.Count);
            for (int i = 0; i < whens.Count; i++)
            {
                var w = whens[i];
                long stage = w.Stage == null ? int.MaxValue : _schema.StageIndex(w.Stage);
                keyed.Add(new KeyValuePair<long, WhenDecl>(stage * whens.Count + i, w));
            }
            keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
            var ordered = new List<WhenDecl>(keyed.Count);
            foreach (var pair in keyed) ordered.Add(pair.Value);
            _whens = ordered;
        }

        /// <summary>발동을 기다리는 (반응, 대상): 회차(세대)와, 이것을 참으로 만든 앞 발동 (고리를 보이는 데 쓴다).</summary>
        private sealed class Pending
        {
            public Pending(WhenDecl when, int order, long target, int round, Pending? cause)
            {
                When = when;
                Order = order;
                Target = target;
                Round = round;
                Cause = cause;
            }

            public WhenDecl When { get; }

            /// <summary><see cref="_whens"/>에서의 자리 (단계 순서 → 로드 순서 → 정의 순서).</summary>
            public int Order { get; }

            public long Target { get; }
            public int Round { get; }
            public Pending? Cause { get; }
        }

        /// <summary>
        /// 정착 (S5, G171·G172). 커맨드의 트랜잭션 밖에서만 부른다. 본문 하나가 끝날 때마다 모든 조건을 다시 보고(G172), 거짓→참인 것 중
        /// 회차가 가장 이른 것을 단계 순서 → 엔티티 순으로 하나 돌린다. 처음부터 참인 것은 1회차, 어떤 본문 뒤에 새로 참이 된 것은 그 본문의
        /// 회차 + 1이다. 회차가 상한을 넘은 것은 돌리지 않는다. 다시 볼 때 거짓이 된 것은 돌리지 않고 기록을 지운다.
        /// </summary>
        /// <param name="tickEnd">틱의 마지막 단계인가. <c>repeat=True</c> 반응은 이때만 본다 (G173).</param>
        private void Settle(List<Reaction> reactions, bool tickEnd)
        {
            if (_whens.Count == 0) return;
            var failed = new SortedSet<string>(StringComparer.Ordinal); // 이 정착에서 조건이 오류 난 (반응, 대상): 다시 볼 때마다 되풀이하지 않는다
            var repeated = new SortedSet<string>(StringComparer.Ordinal); // 이 정착에서 이미 돈 repeat 반응 (반응, 대상): 틱마다 한 번
            var pending = new SortedDictionary<string, Pending>(StringComparer.Ordinal);
            Pending? fired = null;
            while (true)
            {
                Look(pending, fired, failed, repeated, tickEnd, reactions);
                Pending? next = null;
                foreach (var p in pending.Values)
                    if (next == null || Earlier(p, next)) next = p;
                if (next == null) return;
                if (next.Round > _maxSettleRounds)
                {
                    // 남은 것은 모두 상한을 넘었다. 참으로 기록하지 않는다: 다음 정착 지점에서 다시 시도되어 고리가 드러난다 (G171)
                    var left = new List<Pending>(pending.Values);
                    left.Sort((a, b) => Earlier(a, b) ? -1 : Earlier(b, a) ? 1 : 0);
                    foreach (var p in left)
                        reactions.Add(new Reaction(Reaction.WhenSignal, WhenValues(p.When, p.Target), p.When.Id, 0, ReactionOutcome.Dropped)
                        {
                            Message = "the reaction " + p.When.Id + " did not run: settling went past " +
                                      _maxSettleRounds.ToString(CultureInfo.InvariantCulture) + " rounds; the chain was: " + Chain(p),
                            Hint = "reactions that keep turning each other's conditions true never settle; make one of them stop, " +
                                   "or a host with longer chains can raise maxSettleRounds (spec-gaps G171)",
                        });
                    return;
                }
                pending.Remove(Key(next.When, next.Target));
                if (next.When.Repeat) repeated.Add(Key(next.When, next.Target));
                else _whenTrue[next.When.Id].Add(next.Target);
                if (next.When.Cooldown != null)
                {
                    // 본문이 실패해도 발동한 것이다 (G172·G174)
                    if (!_whenCooldown.TryGetValue(next.When.Id, out var until))
                        _whenCooldown.Add(next.When.Id, until = new SortedDictionary<long, long>());
                    until[next.Target] = _tick + next.When.Cooldown.Value;
                }
                if (next.When.Once != WhenOnce.None)
                {
                    if (!_whenDone.TryGetValue(next.When.Id, out var done)) _whenDone.Add(next.When.Id, done = new SortedSet<long>());
                    done.Add(OnceTarget(next.When, next.Target));
                }
                reactions.Add(RunWhen(next.When, next.Target));
                reactions.AddRange(RunReactions());
                fired = next;
            }
        }

        private static string Key(WhenDecl w, long target) => w.Id + "#" + target.ToString(CultureInfo.InvariantCulture);

        private static bool Earlier(Pending a, Pending b) =>
            a.Round != b.Round ? a.Round < b.Round : a.Order != b.Order ? a.Order < b.Order : a.Target < b.Target;

        /// <summary>
        /// 모든 조건을 지금 상태로 본다. 참이고 기록이 없으면 기다리는 목록에 넣고(이미 있으면 회차를 그대로 둔다), 거짓이면 기록과 목록에서 뺀다.
        /// <c>repeat=True</c> 반응은 틱 끝에만 보고, 기록 대신 이 정착에서 이미 돌았는지를 본다 (G173).
        /// </summary>
        private void Look(SortedDictionary<string, Pending> pending, Pending? fired, SortedSet<string> failed, SortedSet<string> repeated,
            bool tickEnd, List<Reaction> reactions)
        {
            int round = fired == null ? 1 : fired.Round + 1;
            for (int order = 0; order < _whens.Count; order++)
            {
                var w = _whens[order];
                if (w.Repeat && !tickEnd) continue;
                SortedSet<long> record;
                if (w.Repeat) record = new SortedSet<long>(); // 쓰지 않는다
                else if (!_whenTrue.TryGetValue(w.Id, out record)) _whenTrue.Add(w.Id, record = new SortedSet<long>());
                var alive = new SortedSet<long>(Targets(w, record));
                // 그 사이 사라진 엔티티는 기다리지 않는다
                var gone = new List<string>();
                foreach (var pair in pending)
                    if (pair.Value.When == w && !alive.Contains(pair.Value.Target)) gone.Add(pair.Key);
                foreach (var key in gone) pending.Remove(key);
                foreach (long target in alive)
                {
                    string key = Key(w, target);
                    if (failed.Contains(key)) continue;
                    bool now;
                    try
                    {
                        // 쿨다운 중에는 거짓으로 본다: 끝난 뒤에도 참이면 그때 거짓→참이다 (G174)
                        now = !Done(w, target) && !Cooling(w, target) && Condition(w, target);
                    }
                    catch (WorldException e)
                    {
                        failed.Add(key);
                        pending.Remove(key);
                        reactions.Add(new Reaction(Reaction.WhenSignal, WhenValues(w, target), w.Id, 0, ReactionOutcome.ScriptError) { Error = e });
                        continue;
                    }
                    if (!now)
                    {
                        record.Remove(target);
                        pending.Remove(key);
                        continue;
                    }
                    if (record.Contains(target) || repeated.Contains(key) || pending.ContainsKey(key)) continue;
                    pending.Add(key, new Pending(w, order, target, round, fired));
                }
            }
        }

        /// <summary>고리를 보이는 경로: 이것을 참으로 만든 발동들을 거슬러 올라가 최근 8개까지, 오래된 것부터.</summary>
        private static string Chain(Pending p)
        {
            const int shown = 8;
            var parts = new List<string>();
            var c = p;
            for (; c != null && parts.Count < shown; c = c.Cause) parts.Add(c.When.Id);
            parts.Reverse();
            return (c != null ? "… → " : "") + string.Join(" → ", parts);
        }

        /// <summary>
        /// 평가할 대상, id 순: 전역이면 <see cref="GlobalTarget"/> 하나, 아니면 그 타입의 살아 있는 엔티티. 사라진 엔티티의 기록은 지운다 (G169).
        /// </summary>
        private List<long> Targets(WhenDecl w, SortedSet<long> record)
        {
            var targets = new List<long>();
            if (w.Subject == null)
            {
                targets.Add(GlobalTarget);
                return targets;
            }
            var dead = new List<long>();
            foreach (long id in record)
                if (!_entities.ContainsKey(id)) dead.Add(id);
            foreach (long id in dead) record.Remove(id);
            if (_schema.TryGetType(w.Subject, out var type) && _byType.TryGetValue(type, out var set)) targets.AddRange(set);
            return targets;
        }

        private static SortedDictionary<string, Value> WhenValues(WhenDecl w, long target)
        {
            var values = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            if (w.Parameter != null) values[w.Parameter] = new EntityValue(new EntityId(target));
            return values;
        }

        private bool Condition(WhenDecl w, long target)
        {
            string label = "the condition of " + w.Id + (w.Subject == null ? "" : " for " + new EntityId(target));
            var args = new List<Value>();
            if (w.Subject != null) args.Add(ToScript(new EntityValue(new EntityId(target))));
            var value = RunGateFunction(w.Condition, args, label);
            if (value is BoolValue b) return b.Value;
            throw new WorldException(Codes.RunType, "computing " + label + ": it must return True or False, not " + value.KindName,
                "a @when condition says whether the reaction should run (spec-gaps G169)");
        }

        /// <summary>거짓→참 기록 (G170): <c>{"reaction": id, "entity": n}</c>, 전역 조건은 <c>"entity": null</c>. 반응 id 순 → 엔티티 순.</summary>
        private JsonArray SaveWhens()
        {
            var list = new JsonArray();
            foreach (var pair in _whenTrue)
            foreach (long target in pair.Value)
            {
                if (target != GlobalTarget && !_entities.ContainsKey(target)) continue;
                list.Add(new JsonObject()
                    .Add("reaction", new JsonString(pair.Key))
                    .Add("entity", target == GlobalTarget ? (JsonNode)JsonNull.Instance : new JsonNumber(target)));
            }
            return list;
        }

        /// <summary>쿨다운 (G174): <c>{"reaction": id, "entity": n, "until": 틱}</c>, 전역 조건은 <c>"entity": null</c>. 끝난 것은 적지 않는다.</summary>
        private JsonArray SaveCooldowns()
        {
            var list = new JsonArray();
            foreach (var pair in _whenCooldown)
            foreach (var entry in pair.Value)
            {
                if (entry.Value <= _tick || (entry.Key != GlobalTarget && !_entities.ContainsKey(entry.Key))) continue;
                list.Add(new JsonObject()
                    .Add("reaction", new JsonString(pair.Key))
                    .Add("entity", entry.Key == GlobalTarget ? (JsonNode)JsonNull.Instance : new JsonNumber(entry.Key))
                    .Add("until", new JsonNumber(entry.Value)));
            }
            return list;
        }

        /// <summary>한 번 쓴 <c>once</c> 반응 (G175): <c>{"reaction": id, "entity": n}</c>, <c>once="game"</c>과 전역 조건은 <c>"entity": null</c>.</summary>
        private JsonArray SaveOnce()
        {
            var list = new JsonArray();
            foreach (var pair in _whenDone)
            foreach (long target in pair.Value)
            {
                if (target != GlobalTarget && !_entities.ContainsKey(target)) continue;
                list.Add(new JsonObject()
                    .Add("reaction", new JsonString(pair.Key))
                    .Add("entity", target == GlobalTarget ? (JsonNode)JsonNull.Instance : new JsonNumber(target)));
            }
            return list;
        }

        /// <summary>
        /// 한 번 쓴 기록을 되살린다. 사라진 반응, <c>once</c>가 없어지거나 단위가 바뀐 반응, 사라진 엔티티의 것은 버린다(그 반응은 다시 돌 수 있다).
        /// </summary>
        private void RestoreOnce(JsonArray saved)
        {
            var byId = new SortedDictionary<string, WhenDecl>(StringComparer.Ordinal);
            foreach (var w in _whens) byId[w.Id] = w;
            foreach (var node in saved.Items)
            {
                var obj = AsObject(node, "a when once record");
                string id = Text(Field(obj, "reaction"), "a when once reaction");
                var entity = Field(obj, "entity");
                long target = entity is JsonNull ? GlobalTarget : Long(entity, "a when once entity");
                if (target != GlobalTarget && target < 1) throw new JsonFormatException("a when once record has a bad entity id");
                if (!byId.TryGetValue(id, out var w) || w.Once == WhenOnce.None) continue;
                bool whole = w.Once == WhenOnce.Game || w.Subject == null;
                if (whole != (target == GlobalTarget)) continue;
                if (target != GlobalTarget && !_entities.ContainsKey(target)) continue;
                if (!_whenDone.TryGetValue(id, out var done)) _whenDone.Add(id, done = new SortedSet<long>());
                done.Add(target);
            }
        }

        /// <summary>쿨다운을 되살린다. 사라진 반응·엔티티, 쿨다운이 없어진 반응, 전역과 엔티티별이 바뀐 반응의 것은 버린다.</summary>
        private void RestoreCooldowns(JsonArray saved)
        {
            var byId = new SortedDictionary<string, WhenDecl>(StringComparer.Ordinal);
            foreach (var w in _whens) byId[w.Id] = w;
            foreach (var node in saved.Items)
            {
                var obj = AsObject(node, "a when cooldown");
                string id = Text(Field(obj, "reaction"), "a when cooldown reaction");
                var entity = Field(obj, "entity");
                long target = entity is JsonNull ? GlobalTarget : Long(entity, "a when cooldown entity");
                if (target != GlobalTarget && target < 1) throw new JsonFormatException("a when cooldown has a bad entity id");
                long until = Long(Field(obj, "until"), "a when cooldown tick");
                if (until <= _tick) throw new JsonFormatException("a when cooldown should end after the saved tick");
                if (!byId.TryGetValue(id, out var w) || w.Cooldown == null) continue;
                if ((w.Subject == null) != (target == GlobalTarget)) continue;
                if (target != GlobalTarget && !_entities.ContainsKey(target)) continue;
                if (!_whenCooldown.TryGetValue(id, out var map)) _whenCooldown.Add(id, map = new SortedDictionary<long, long>());
                map[target] = until;
            }
        }

        /// <summary>
        /// 거짓→참 기록을 되살린다. 모드에서 사라진 반응, 전역과 엔티티별이 바뀐 반응, 없어진 엔티티의 기록은 버린다(그 조건은 처음처럼 거짓으로 본다, G170).
        /// </summary>
        private void RestoreWhens(JsonArray saved)
        {
            var byId = new SortedDictionary<string, WhenDecl>(StringComparer.Ordinal);
            foreach (var w in _whens) byId[w.Id] = w;
            foreach (var node in saved.Items)
            {
                var obj = AsObject(node, "a when record");
                string id = Text(Field(obj, "reaction"), "a when reaction");
                var entity = Field(obj, "entity");
                long target = entity is JsonNull ? GlobalTarget : Long(entity, "a when entity");
                if (target != GlobalTarget && target < 1) throw new JsonFormatException("a when record has a bad entity id");
                if (!byId.TryGetValue(id, out var w) || w.Repeat) continue;
                if ((w.Subject == null) != (target == GlobalTarget)) continue;
                if (target != GlobalTarget && !_entities.ContainsKey(target)) continue;
                if (!_whenTrue.TryGetValue(id, out var record)) _whenTrue.Add(id, record = new SortedSet<long>());
                record.Add(target);
            }
        }

        /// <summary>반응의 본문을 새 Flow, 자기 트랜잭션으로 돌린다 (S5). 실패하면 그 본문의 효과만 되돌린다.</summary>
        private Reaction RunWhen(WhenDecl w, long target)
        {
            var values = WhenValues(w, target);
            _reactingHandler = w.Id; // 본문이 낸 신호는 1세대이고 이 반응이 냈다
            _settling = true;
            try
            {
                var opened = Atomically(() =>
                {
                    var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                    foreach (var pair in values) args[pair.Key] = ToScript(pair.Value);
                    bool outermost = _meter == null;
                    if (outermost) _meter = new StepMeter(_stepBudget);
                    try
                    {
                        Evaluator.RunCommand(w.Function, args, out var suspended, w.Span, _meter);
                        if (suspended == null) return ((long?, Request?))(null, null);
                        long flow = _nextFlow;
                        return (flow, OpenFlow(w.Id, suspended, SubjectsOf(args.Values), handler: true));
                    }
                    catch (ScriptRuntimeException ex)
                    {
                        throw ToWorldException(ex, "the reaction " + w.Id + (w.Subject == null ? "" : " for " + new EntityId(target)));
                    }
                    finally
                    {
                        if (outermost) _meter = null;
                    }
                });
                if (opened.Item1 == null) return new Reaction(Reaction.WhenSignal, values, w.Id, 0, ReactionOutcome.Finished);
                return new Reaction(Reaction.WhenSignal, values, w.Id, 0, ReactionOutcome.Suspended)
                {
                    Flow = opened.Item1,
                    OpenedRequest = opened.Item2,
                    SleepsUntil = SleepsUntil(opened.Item1.Value),
                };
            }
            catch (WorldException ex)
            {
                return new Reaction(Reaction.WhenSignal, values, w.Id, 0, ReactionOutcome.ScriptError) { Error = ex };
            }
            finally
            {
                _settling = false;
                _reactingHandler = null;
            }
        }
    }
}
