using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <c>@when</c> 반응과 정착 (spec S5, spec-gaps G169~G175). 정착 지점(제출·답·응답이 끝난 뒤, 틱의 마지막 단계)에서 조건을
    /// 보고 거짓→참이 된 것의 본문을 각자 Flow·트랜잭션으로 돌린다. 본문 하나가 끝날 때마다 다시 보고, 새로 발동한 것이 없으면 끝난다.
    /// 다시 평가하는 것은 읽은 엔티티에 쓰기가 있었거나 틱이 바뀌어 값이 바뀌었을 수 있는 조건뿐이다(S5 "읽은 칸이 바뀔 때만", 단위는 엔티티).
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
            for (int order = 0; order < ordered.Count; order++)
            {
                var w = ordered[order];
                _whenOrder[w.Id] = order;
                if (w.Subject == null) continue;
                if (!_whenBySubject.TryGetValue(w.Subject, out var orders)) _whenBySubject.Add(w.Subject, orders = new List<int>());
                orders.Add(order);
            }
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
                    foreach (var p in left) _whenRetry.Add(new WhenKey(p.Order, p.Target));
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
                else if (next.When.Once == WhenOnce.None) _whenTrue[next.When.Id].Add(next.Target); // 한 번 쓴 것은 늘 거짓으로 본다 (G175)
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
        /// 조건을 지금 상태로 본다 (G172). 다시 평가하는 것은 바뀌었을 수 있는 것만이다(S5 "읽은 칸이 바뀔 때만"): 지난번 평가 뒤 쓰기가 있었던
        /// 엔티티를 읽은 조건, 틱이 바뀌었으면 <c>now()</c>를 읽은 조건, 아직 평가하지 않은 (반응, 엔티티), 오류가 났던 것. 나머지는 지난 값을 쓴다.
        /// 그다음 값이 바뀌었을 수 있는 것(다시 평가한 것, 쿨다운 중인 것, 기다리는 것, 틱 끝이면 참인 repeat 반응)만 판정한다:
        /// 참이고 기록이 없으면 기다리는 목록에 넣고(이미 있으면 회차를 그대로 둔다), 거짓이면 기록과 목록에서 뺀다.
        /// <c>repeat=True</c> 반응은 틱 끝에만 판정하고, 기록 대신 이 정착에서 이미 돌았는지를 본다 (G173).
        /// </summary>
        private void Look(SortedDictionary<string, Pending> pending, Pending? fired, SortedSet<string> failed, SortedSet<string> repeated,
            bool tickEnd, List<Reaction> reactions)
        {
            int round = fired == null ? 1 : fired.Round + 1;
            var eval = new SortedSet<WhenKey>();
            foreach (long id in _whenTouched)
            {
                if (_whenReaders.TryGetValue(id, out var readers)) eval.UnionWith(readers);
                if (_entities.TryGetValue(id, out var entity))
                {
                    // 새 엔티티(또는 아직 본 적 없는 것)는 그 타입을 보는 반응마다 처음 평가한다
                    if (_whenBySubject.TryGetValue(entity.Def.Type.Name, out var orders))
                        foreach (int order in orders)
                            if (!_whenCache.ContainsKey(new WhenKey(order, id))) eval.Add(new WhenKey(order, id));
                }
                else Forget(id, pending);
            }
            _whenTouched.Clear();
            if (_whenAll || WhenFullScan)
            {
                _whenAll = false;
                for (int order = 0; order < _whens.Count; order++)
                {
                    var w = _whens[order];
                    if (w.Subject == null) eval.Add(new WhenKey(order, GlobalTarget));
                    else if (_schema.TryGetType(w.Subject, out var type) && _byType.TryGetValue(type, out var set))
                        foreach (long id in set) eval.Add(new WhenKey(order, id));
                }
            }
            if (_tick != _whenSeenTick) eval.UnionWith(_whenTickReaders);
            _whenSeenTick = _tick;
            eval.UnionWith(_whenUnknown);

            foreach (var key in eval)
            {
                var w = _whens[key.Order];
                if (w.Subject != null && !_entities.ContainsKey(key.Target)) continue;
                if (failed.Contains(Key(w, key.Target)) || Done(w, key.Target)) continue;
                try
                {
                    Evaluate(key, w);
                }
                catch (WorldException e)
                {
                    failed.Add(Key(w, key.Target));
                    reactions.Add(new Reaction(Reaction.WhenSignal, WhenValues(w, key.Target), w.Id, 0, ReactionOutcome.ScriptError) { Error = e });
                }
            }

            // 판정: 값이 바뀌었을 수 있는 것만
            var decide = eval;
            foreach (var pair in _whenCooldown)
                if (_whenOrder.TryGetValue(pair.Key, out int order))
                    foreach (long target in pair.Value.Keys) decide.Add(new WhenKey(order, target));
            // 기다리는 것은 다시 평가했거나(위) 방금 once로 막혔을 때만 바뀐다
            if (fired != null && fired.When.Once != WhenOnce.None)
                foreach (var p in pending.Values)
                    if (p.Order == fired.Order) decide.Add(new WhenKey(p.Order, p.Target));
            if (tickEnd) decide.UnionWith(_whenRepeatTrue);
            decide.UnionWith(_whenRetry);
            _whenRetry.Clear();
            foreach (var key in decide)
            {
                var w = _whens[key.Order];
                long target = key.Target;
                string skey = Key(w, target);
                if (w.Repeat && !tickEnd) continue;
                if (w.Subject != null && !_entities.ContainsKey(target))
                {
                    pending.Remove(skey);
                    continue;
                }
                if (failed.Contains(skey))
                {
                    pending.Remove(skey);
                    continue;
                }
                // 쿨다운 중이거나 한 번 쓴 것은 거짓으로 본다: 쿨다운이 끝난 뒤에도 참이면 그때 거짓→참이다 (G174·G175)
                bool now = _whenCache.TryGetValue(key, out var cached) && cached.Ok && cached.Value &&
                           !Done(w, target) && !Cooling(w, target);
                SortedSet<long>? record = null;
                if (!w.Repeat && !_whenTrue.TryGetValue(w.Id, out record)) _whenTrue.Add(w.Id, record = new SortedSet<long>());
                if (!now)
                {
                    record?.Remove(target);
                    pending.Remove(skey);
                    continue;
                }
                if ((record != null && record.Contains(target)) || repeated.Contains(skey) || pending.ContainsKey(skey)) continue;
                pending.Add(skey, new Pending(w, key.Order, target, round, fired));
            }
        }

        // ---- 다시 평가할 조건 고르기 (S5 "읽은 칸이 바뀔 때만 재평가") ----

        /// <summary>(반응 자리, 대상). 반응 자리는 <see cref="_whens"/>의 순서다.</summary>
        private readonly struct WhenKey : IComparable<WhenKey>, IEquatable<WhenKey>
        {
            public WhenKey(int order, long target)
            {
                Order = order;
                Target = target;
            }

            public int Order { get; }
            public long Target { get; }

            public int CompareTo(WhenKey other) => Order != other.Order ? Order.CompareTo(other.Order) : Target.CompareTo(other.Target);
            public bool Equals(WhenKey other) => Order == other.Order && Target == other.Target;
            public override bool Equals(object? obj) => obj is WhenKey other && Equals(other);
            public override int GetHashCode() => Order * 397 ^ Target.GetHashCode();
        }

        /// <summary>조건의 지난 평가: 값, 그때 읽은 엔티티, <c>now()</c>를 읽었는지. 오류였으면 <see cref="Ok"/>가 거짓이다.</summary>
        private sealed class WhenCache
        {
            public bool Ok;
            public bool Value;
            public long[] Reads = Array.Empty<long>();
            public bool Tick;
        }

        private readonly Dictionary<WhenKey, WhenCache> _whenCache = new Dictionary<WhenKey, WhenCache>();

        /// <summary>엔티티 → 그 엔티티를 읽은 조건.</summary>
        private readonly Dictionary<long, SortedSet<WhenKey>> _whenReaders = new Dictionary<long, SortedSet<WhenKey>>();

        /// <summary><c>now()</c>를 읽은 조건: 틱이 바뀌면 다시 본다.</summary>
        private readonly SortedSet<WhenKey> _whenTickReaders = new SortedSet<WhenKey>();

        /// <summary>지난 평가가 오류였던 조건: 정착마다 다시 본다.</summary>
        private readonly SortedSet<WhenKey> _whenUnknown = new SortedSet<WhenKey>();

        /// <summary>지난 값이 참인 <c>repeat=True</c> 반응: 틱 끝마다 판정한다.</summary>
        private readonly SortedSet<WhenKey> _whenRepeatTrue = new SortedSet<WhenKey>();

        /// <summary>상한에 걸려 돌지 못한 것: 참으로 기록하지 않았으니 다음 정착에서 다시 판정한다 (G171).</summary>
        private readonly SortedSet<WhenKey> _whenRetry = new SortedSet<WhenKey>();

        /// <summary>지난 정착 뒤 쓰기가 있었던 엔티티 (값·줄·컴포넌트·생성·삭제·역방향 컬렉션·기여 행).</summary>
        private readonly SortedSet<long> _whenTouched = new SortedSet<long>();

        /// <summary>아직 아무것도 평가하지 않았다(새 월드, 불러온 월드): 다음 정착에서 모두 평가한다.</summary>
        private bool _whenAll = true;

        private long _whenSeenTick;

        /// <summary>Def 타입 이름 → 그 타입의 엔티티마다 보는 반응 자리.</summary>
        private readonly Dictionary<string, List<int>> _whenBySubject = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        /// <summary>반응 id → 반응 자리.</summary>
        private readonly Dictionary<string, int> _whenOrder = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>조건을 평가하는 중이면 읽은 엔티티를 모은다. 평소에는 null이라 읽기에 비용이 없다.</summary>
        private SortedSet<long>? _whenReads;

        private bool _whenReadsTick;

        /// <summary>테스트용: 정착 때마다 모든 조건을 다시 평가한다. 고르기가 빠뜨린 것이 없는지 결과를 견주는 데 쓴다.</summary>
        internal bool WhenFullScan { get; set; }

        /// <summary>엔티티에 쓰기가 있었다: 그 엔티티를 읽은 조건을 다음 정착에서 다시 본다.</summary>
        private void Touch(long id)
        {
            if (_whens.Count > 0) _whenTouched.Add(id);
        }

        /// <summary>조건 하나를 평가해 값과 읽은 것을 기억한다. 오류면 기억을 "모름"으로 두고 예외를 그대로 던진다.</summary>
        private void Evaluate(WhenKey key, WhenDecl w)
        {
            if (!_whenCache.TryGetValue(key, out var cache)) _whenCache.Add(key, cache = new WhenCache());
            Unlink(key, cache);
            var reads = new SortedSet<long>();
            _whenReads = reads;
            _whenReadsTick = false;
            try
            {
                cache.Value = Condition(w, key.Target);
                cache.Ok = true;
                _whenUnknown.Remove(key);
            }
            catch
            {
                cache.Ok = false;
                cache.Value = false;
                _whenUnknown.Add(key);
                throw;
            }
            finally
            {
                _whenReads = null;
                cache.Reads = new long[reads.Count];
                reads.CopyTo(cache.Reads);
                cache.Tick = _whenReadsTick;
                foreach (long id in cache.Reads)
                {
                    if (!_whenReaders.TryGetValue(id, out var readers)) _whenReaders.Add(id, readers = new SortedSet<WhenKey>());
                    readers.Add(key);
                }
                if (cache.Tick) _whenTickReaders.Add(key);
                if (w.Repeat && cache.Ok && cache.Value) _whenRepeatTrue.Add(key);
                else _whenRepeatTrue.Remove(key);
            }
        }

        /// <summary>조건의 지난 읽기를 색인에서 뺀다.</summary>
        private void Unlink(WhenKey key, WhenCache cache)
        {
            foreach (long id in cache.Reads)
                if (_whenReaders.TryGetValue(id, out var readers))
                {
                    readers.Remove(key);
                    if (readers.Count == 0) _whenReaders.Remove(id);
                }
            _whenTickReaders.Remove(key);
        }

        /// <summary>사라진 엔티티를 대상으로 한 조건의 기억과 기록을 지운다 (G169).</summary>
        private void Forget(long id, SortedDictionary<string, Pending> pending)
        {
            for (int order = 0; order < _whens.Count; order++)
            {
                var w = _whens[order];
                if (w.Subject == null) continue;
                var key = new WhenKey(order, id);
                if (_whenCache.TryGetValue(key, out var cache))
                {
                    Unlink(key, cache);
                    _whenCache.Remove(key);
                }
                _whenUnknown.Remove(key);
                _whenRepeatTrue.Remove(key);
                if (_whenTrue.TryGetValue(w.Id, out var record)) record.Remove(id);
                if (_whenCooldown.TryGetValue(w.Id, out var until)) until.Remove(id);
                pending.Remove(Key(w, id));
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
                        Evaluator.RunCommand(w.Function, args, out var suspended, w.Span, _meter, () => StartStream(w.Id));
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
