using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 난수 (spec S2 난수, spec-gaps G178~G182). 값은 <c>H(월드 시드, 난수열 키, n)</c>이고 H는 ChaCha20이다 (G181).
    /// 난수열은 Flow마다 하나이고 키는 (Flow를 만든 정의 id, 그 정의의 실행 횟수)다 (G182). 횟수 카운터는 월드 상태라 롤백되면 함께
    /// 돌아가고, 실패한 입력은 난수를 소비하지 않는다 (C10). 스크립트는 <c>random</c> 이름 공간으로 부른다 (G179).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>월드 시드 (G180). 호스트가 월드를 만들 때 주고 저장에 남는다.</summary>
        private long _seed;

        public long Seed => _seed;

        /// <summary>정의 id(커맨드·핸들러·<c>when</c> 반응) → 그 정의로 만든 Flow 수 (G182).</summary>
        private readonly SortedDictionary<string, long> _randCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);

        /// <summary>지금 실행 중인 Flow의 난수열. 본문이 시작할 때(새 Flow)나 이어 갈 때(멈춘 Flow) 정해진다.</summary>
        private RandStream? _rand;

        /// <summary>Flow 하나의 난수열: 키(정의 id, 실행 횟수)와 지금까지 부른 횟수 n (spec S2 "Flow의 RNG 상태 = (키, n)").</summary>
        private sealed class RandStream
        {
            public RandStream(string id, long count, long calls)
            {
                Id = id;
                Count = count;
                Calls = calls;
            }

            public string Id { get; }
            public long Count { get; }

            /// <summary>이 Flow에서 난수 함수를 부른 횟수. 다음 호출의 n이다.</summary>
            public long Calls { get; set; }

            /// <summary>ChaCha20 키 = SHA-256(시드, 실행 횟수, 정의 id). 처음 쓸 때 계산한다.</summary>
            public uint[]? Key { get; set; }

            public RandStream Copy() => new RandStream(Id, Count, Calls) { Key = Key };
        }

        /// <summary>새 Flow의 본문이 시작된다: 그 정의의 실행 횟수를 하나 올리고 난수열을 연다 (G182). 되돌리기 기록에 남는다.</summary>
        private void StartStream(string id)
        {
            _randCounts.TryGetValue(id, out long before);
            _randCounts[id] = before + 1;
            Journal(() =>
            {
                if (before == 0) _randCounts.Remove(id);
                else _randCounts[id] = before;
            });
            _rand = new RandStream(id, before + 1, 0);
        }

        private uint[] KeyOf(RandStream stream)
        {
            if (stream.Key != null) return stream.Key;
            var id = Encoding.UTF8.GetBytes(stream.Id);
            var input = new byte[16 + id.Length];
            WriteLittleEndian(input, 0, _seed);
            WriteLittleEndian(input, 8, stream.Count);
            Array.Copy(id, 0, input, 16, id.Length);
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
            var key = new uint[8];
            for (int i = 0; i < 8; i++)
                key[i] = digest[4 * i] | (uint)digest[4 * i + 1] << 8 | (uint)digest[4 * i + 2] << 16 | (uint)digest[4 * i + 3] << 24;
            return stream.Key = key;
        }

        private static void WriteLittleEndian(byte[] buffer, int at, long value)
        {
            for (int i = 0; i < 8; i++) buffer[at + i] = unchecked((byte)(value >> (8 * i)));
        }

        /// <summary>
        /// 난수 함수 호출 하나의 뽑기들: nonce = n, 블록 카운터 = 이 호출 안의 블록 번호 (G181). 호출 하나에서 여러 번 뽑아도
        /// n은 하나만 늘어난다.
        /// </summary>
        private sealed class Draws
        {
            private readonly uint[] _key;
            private readonly uint[] _nonce;
            private readonly uint[] _block = new uint[16];
            private int _blockNo = -1;
            private long _index;

            public Draws(uint[] key, long n)
            {
                _key = key;
                _nonce = new[] { unchecked((uint)n), unchecked((uint)(n >> 32)), 0u };
            }

            public ulong Next()
            {
                int block = checked((int)(_index / 8));
                if (block != _blockNo)
                {
                    ChaCha20.Block(_key, (uint)block, _nonce, _block);
                    _blockNo = block;
                }
                int w = (int)(_index % 8);
                _index++;
                return _block[2 * w] | (ulong)_block[2 * w + 1] << 32;
            }

            /// <summary>0 이상 <paramref name="span"/> 미만을 고르게 (0이면 64비트 전체). 쏠림은 버리고 다시 뽑기로 없앤다.</summary>
            public ulong Below(ulong span)
            {
                if (span == 0) return Next();
                ulong threshold = unchecked(0UL - span) % span; // 2^64 mod span: 이보다 작은 값을 버리면 나머지가 고르다
                while (true)
                {
                    ulong x = Next();
                    if (x >= threshold) return x % span;
                }
            }

            /// <summary><paramref name="lo"/> 이상 <paramref name="hi"/> 이하 (lo ≤ hi).</summary>
            public long Between(long lo, long hi) => unchecked(lo + (long)Below((ulong)(hi - lo) + 1));
        }

        private Draws BeginDraws()
        {
            var stream = _rand ?? throw new InvalidOperationException("a random function ran outside a flow body");
            long n = stream.Calls;
            stream.Calls = n + 1;
            return new Draws(KeyOf(stream), n);
        }

        // ---- 저장 (G182) ----

        /// <summary>정의마다 실행 횟수: <c>{"id": 정의 id, "count": n}</c>, id 순.</summary>
        private JsonArray SaveRandCounts()
        {
            var list = new JsonArray();
            foreach (var pair in _randCounts)
                list.Add(new JsonObject().Add("id", new JsonString(pair.Key)).Add("count", new JsonNumber(pair.Value)));
            return list;
        }

        /// <summary>멈춘 Flow의 난수열: <c>{"count": 실행 횟수, "calls": 부른 횟수}</c>. 정의 id는 Flow의 커맨드·핸들러 id다.</summary>
        private static JsonObject SaveStream(FlowRecord flow)
        {
            var stream = flow.Rand ?? throw new InvalidOperationException("engine: flow " + flow.Id + " has no random stream");
            return new JsonObject().Add("count", new JsonNumber(stream.Count)).Add("calls", new JsonNumber(stream.Calls));
        }

        private static RandStream LoadStream(JsonNode node, string id)
        {
            var obj = AsObject(node, "a flow random stream");
            long count = Long(Field(obj, "count"), "a flow random count");
            long calls = Long(Field(obj, "calls"), "a flow random calls");
            if (count < 1 || calls < 0) throw new JsonFormatException("a flow random stream has a bad count or calls");
            return new RandStream(id, count, calls);
        }

        /// <summary>실행 횟수를 되살린다. 모드에서 사라진 정의의 것은 버린다(다른 저장 구역과 같은 규칙).</summary>
        private void RestoreRandCounts(JsonArray saved)
        {
            string last = "";
            foreach (var node in saved.Items)
            {
                var obj = AsObject(node, "a random count");
                string id = Text(Field(obj, "id"), "a random count id");
                long count = Long(Field(obj, "count"), "a random count");
                if (count < 1) throw new JsonFormatException("a random count should be at least 1");
                if (string.CompareOrdinal(id, last) <= 0) throw new JsonFormatException("random counts must be in increasing id order");
                last = id;
                if (_commands.ContainsKey(id) || _handlerById.ContainsKey(id) || _whenOrder.ContainsKey(id)) _randCounts[id] = count;
            }
        }

        // ---- 스크립트의 random (G179) ----

        public const string RandomName = PureBuiltins.RandomName;

        private void AddRandomBuiltins(SortedDictionary<string, Value> table) => table.Add(RandomName, new RandomModule(this));

        /// <summary>
        /// <c>random</c> 이름 공간: 파이썬 <c>random</c> 모듈과 이름·뜻이 같은 것만 (G179). 함수는 Flow 본문에서만 부를 수 있다 (G178,
        /// 실행기의 안전망이 <see cref="PureBuiltin.Random"/>을 본다).
        /// </summary>
        private sealed class RandomModule : Value, IRuntimeAttributes
        {
            private readonly SortedDictionary<string, PureBuiltin> _functions = new SortedDictionary<string, PureBuiltin>(StringComparer.Ordinal);

            public RandomModule(World world)
            {
                void Add(string name, Func<IReadOnlyList<Value>, IReadOnlyDictionary<string, Value>, Value> body) =>
                    _functions.Add(name, new PureBuiltin(RandomName + "." + name, body) { Random = true });

                Add("randint", world.RandInt);
                Add("randrange", world.RandRange);
                Add("choice", world.Choice);
                Add("choices", world.Choices);
                Add("sample", world.Sample);
                Add("random", world.RandomFixed);
            }

            public override string KindName => "module";

            public Value GetAttribute(string name)
            {
                if (_functions.TryGetValue(name, out var f)) return f;
                string? hint = name == "shuffle"
                    ? "lists cannot change in place; random.sample(seq, len(seq)) gives a shuffled copy (spec-gaps G179)"
                    : name == "seed"
                        ? "the world seed is set by the host when it creates the world (spec-gaps G180)"
                        : Suggest.Hint(name, _functions.Keys);
                throw new ScriptRuntimeException(Codes.RunScript, "random has no '" + name + "'", hint);
            }
        }

        private static ScriptRuntimeException RandomError(string message, string? hint = null) =>
            new ScriptRuntimeException(Codes.RunScript, message, hint);

        private static void NoKeywords(string name, IReadOnlyDictionary<string, Value> keywords)
        {
            foreach (var pair in keywords) throw RandomError(name + "() does not take the keyword argument '" + pair.Key + "'");
        }

        private static long IntArg(string name, string what, Value v)
        {
            if (v is IntValue i) return i.Value;
            throw PureOps.TypeError(name + "() needs an int for " + what + ", not " + v.KindName);
        }

        /// <summary><c>randint(a, b)</c>: a 이상 b 이하의 정수.</summary>
        private Value RandInt(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = "random.randint";
            NoKeywords(name, keywords);
            if (args.Count != 2) throw PureOps.TypeError(name + "() takes 2 arguments, got " + args.Count);
            long a = IntArg(name, "a", args[0]), b = IntArg(name, "b", args[1]);
            if (a > b) throw RandomError(name + "(" + a + ", " + b + "): the range is empty", "the first bound must not be larger than the second");
            return new IntValue(BeginDraws().Between(a, b));
        }

        /// <summary><c>randrange(stop)</c>·<c>randrange(start, stop[, step])</c>: <c>range(...)</c>에서 하나 (끝 제외).</summary>
        private Value RandRange(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = "random.randrange";
            NoKeywords(name, keywords);
            if (args.Count < 1 || args.Count > 3) throw PureOps.TypeError(name + "() takes 1 to 3 arguments, got " + args.Count);
            long start = args.Count == 1 ? 0 : IntArg(name, "start", args[0]);
            long stop = IntArg(name, "stop", args[args.Count == 1 ? 0 : 1]);
            long step = args.Count == 3 ? IntArg(name, "step", args[2]) : 1;
            if (step == 0) throw RandomError(name + "() step must not be zero");
            // 원소 수: 오버플로가 없게 큰 정수로 센다
            var width = (System.Numerics.BigInteger)stop - start;
            var count = step > 0 ? (width + step - 1) / step : (width + step + 1) / step;
            if (count <= 0) throw RandomError(name + "(" + start + ", " + stop + ", " + step + "): the range is empty");
            var picked = (System.Numerics.BigInteger)BeginDraws().Below(count > ulong.MaxValue ? 0UL : (ulong)count);
            return new IntValue((long)(start + picked * step));
        }

        /// <summary>고를 수 있는 순서 있는 값: 리스트와 <c>range(...)</c> (파이썬의 시퀀스).</summary>
        private static long SequenceCount(string name, Value seq)
        {
            switch (seq)
            {
                case ListValue list: return list.Items.Count;
                case RangeValue range: return range.Count;
            }
            throw PureOps.TypeError(name + "() needs a list or a range, not " + seq.KindName);
        }

        private static Value SequenceAt(Value seq, long index) =>
            seq is ListValue list ? list.Items[(int)index] : new IntValue(((RangeValue)seq).At(index));

        /// <summary><c>choice(seq)</c>: 하나.</summary>
        private Value Choice(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = "random.choice";
            NoKeywords(name, keywords);
            if (args.Count != 1) throw PureOps.TypeError(name + "() takes 1 argument, got " + args.Count);
            long count = SequenceCount(name, args[0]);
            if (count == 0) throw RandomError(name + "() cannot choose from an empty sequence");
            return SequenceAt(args[0], (long)BeginDraws().Below((ulong)count));
        }

        /// <summary><c>choices(seq, weights=None, k=1)</c>: 중복을 허용해 k개를 리스트로. 가중치는 정수다 (G179).</summary>
        private Value Choices(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = "random.choices";
            if (args.Count < 1 || args.Count > 2) throw PureOps.TypeError(name + "() takes the sequence and optionally the weights, got " + args.Count + " positional arguments");
            Value? weightsArg = args.Count == 2 ? args[1] : null;
            long k = 1;
            foreach (var pair in keywords)
            {
                if (pair.Key == "weights" && weightsArg == null) weightsArg = pair.Value;
                else if (pair.Key == "k") k = IntArg(name, "k", pair.Value);
                else if (pair.Key == "weights") throw PureOps.TypeError(name + "() got the weights twice");
                else throw RandomError(name + "() does not take the keyword argument '" + pair.Key + "'",
                    pair.Key == "cum_weights" ? "give the weights themselves (spec-gaps G179)" : Suggest.Hint(pair.Key, new[] { "weights", "k" }));
            }
            if (k < 0) throw RandomError(name + "() k must not be negative");
            if (k > MaxRandomItems) throw RandomError(name + "() k is too large (at most " + MaxRandomItems + ")");
            long count = SequenceCount(name, args[0]);
            if (weightsArg is NoneValue) weightsArg = null;
            long[]? cumulative = null;
            if (weightsArg != null)
            {
                if (!(weightsArg is ListValue weights)) throw PureOps.TypeError(name + "() weights must be a list of int, not " + weightsArg.KindName);
                if (weights.Items.Count != count)
                    throw RandomError(name + "() got " + weights.Items.Count + " weights for " + count + " items", "give one weight for each item");
                cumulative = new long[count];
                long total = 0;
                for (int i = 0; i < count; i++)
                {
                    long w = IntArg(name, "a weight", weights.Items[i]);
                    if (w < 0) throw RandomError(name + "() weights must not be negative, got " + w);
                    try
                    {
                        total = checked(total + w);
                    }
                    catch (OverflowException)
                    {
                        throw RandomError(name + "() the weights add up to more than an int can hold");
                    }
                    cumulative[i] = total;
                }
                if (total == 0 && k > 0) throw RandomError(name + "() the weights add up to zero");
            }
            if (count == 0 && k > 0) throw RandomError(name + "() cannot choose from an empty sequence");
            var items = new List<Value>((int)k);
            var draws = BeginDraws();
            for (long i = 0; i < k; i++)
            {
                if (cumulative == null)
                {
                    items.Add(SequenceAt(args[0], (long)draws.Below((ulong)count)));
                    continue;
                }
                long r = (long)draws.Below((ulong)cumulative[count - 1]);
                // r보다 큰 첫 누적값의 자리 (이분 탐색)
                long lo = 0, hi = count - 1;
                while (lo < hi)
                {
                    long mid = lo + (hi - lo) / 2;
                    if (cumulative[mid] > r) hi = mid;
                    else lo = mid + 1;
                }
                items.Add(SequenceAt(args[0], lo));
            }
            return new ListValue(items);
        }

        /// <summary>한 번에 만드는 리스트의 크기 상한 (<c>range()</c>를 펼치는 상한과 같다).</summary>
        private const long MaxRandomItems = 100_000;

        /// <summary><c>sample(seq, k)</c>: 중복 없이 k개를 뽑은 순서대로. 섞기는 <c>sample(seq, len(seq))</c>다 (G179).</summary>
        private Value Sample(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = "random.sample";
            Value? kArg = args.Count == 2 ? args[1] : null;
            foreach (var pair in keywords)
            {
                if (pair.Key == "k" && kArg == null) kArg = pair.Value;
                else throw RandomError(name + "() does not take the keyword argument '" + pair.Key + "'");
            }
            if (args.Count < 1 || args.Count > 2 || kArg == null) throw PureOps.TypeError(name + "() takes the sequence and k");
            long k = IntArg(name, "k", kArg);
            long count = SequenceCount(name, args[0]);
            if (k < 0 || k > count) throw RandomError(name + "() k must be between 0 and the number of items (" + count + "), got " + k);
            if (k > MaxRandomItems) throw RandomError(name + "() k is too large (at most " + MaxRandomItems + ")");
            // 부분 피셔-예이츠: 바뀐 자리만 기억한다 (큰 range에서도 k에 비례)
            var swapped = new Dictionary<long, long>();
            long At(long i) => swapped.TryGetValue(i, out long v) ? v : i;
            var items = new List<Value>((int)k);
            var draws = BeginDraws();
            for (long i = 0; i < k; i++)
            {
                long j = i + (long)draws.Below((ulong)(count - i));
                long pick = At(j);
                swapped[j] = At(i);
                items.Add(SequenceAt(args[0], pick));
            }
            return new ListValue(items);
        }

        /// <summary><c>random()</c>: 0 이상 1 미만의 <c>fixed</c> (파이썬과 달리 float이 아니다, G179).</summary>
        private Value RandomFixed(IReadOnlyList<Value> args, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = "random.random";
            NoKeywords(name, keywords);
            if (args.Count != 0) throw PureOps.TypeError(name + "() takes no arguments, got " + args.Count);
            return new FixedValue(Fixed.FromRaw((long)BeginDraws().Below((ulong)Fixed.Scale)));
        }
    }
}
