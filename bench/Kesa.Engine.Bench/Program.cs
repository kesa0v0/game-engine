using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Bench
{
    /// <summary>
    /// 규모 벤치 (spec 9절, spec-gaps G223). 엔티티 N개에서 장면마다 예열 뒤 여러 번 재 중앙값과 한 번의 할당량을 낸다.
    /// 합격 기준은 없다: 병목이 저장·인터프리터·추적 중 어디인지 보려는 자릿수다.
    /// <c>dotnet run -c Release --project bench/Kesa.Engine.Bench [-- 1000 10000]</c>
    /// </summary>
    public static class Program
    {
        private const string Schema =
            "def health_max(h):\n" +
            "    flat = 0\n" +
            "    for r in contribs(h, \"max\"):\n" +
            "        flat = flat + r.value\n" +
            "    return floor(h.base_max + flat)\n" +
            "component(\"Health\", base_max=const(int, default=10), current=field(int, default=5), max=derived(int, health_max))\n" +
            "component(\"Food\", amount=field(int, default=0))\n" +
            // 실행기 몫을 재는 모양들 (--shapes): 같은 일을 C#으로 한 것과 비교한다
            "def formula(s) -> int:\n" +
            "    return floor(s.Health.base_max * 3 / 2 + s.Food.amount * 2 - 1)\n" +
            "def mood(s) -> int:\n" +
            "    hp = s.Health.current\n" +
            "    if hp < s.Health.base_max // 2 and s.Food.amount < 3:\n" +
            "        return 0\n" +
            "    elif hp < s.Health.base_max:\n" +
            "        return 1\n" +
            "    return 2\n" +
            "def crunch(s) -> int:\n" +
            "    t = 0\n" +
            "    for i in range(50):\n" +
            "        t = t + i * i % 7\n" +
            "    return t\n" +
            "def one(s) -> int:\n    return 1\n" +
            "component(\"Shape\", one=derived(int, one), formula=derived(int, formula), mood=derived(int, mood), crunch=derived(int, crunch))\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Food\", \"Shape\"])\n";

        private const string Defs =
            "Unit(\"Soldier\")\n" +
            "@on(Tick)\n" +
            "def grow():\n" +
            "    for u in query(Unit):\n" +
            "        u.Food.amount += 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def FeedAll(fail: bool):\n" +
            "    for u in query(Unit):\n" +
            "        u.Food.amount += 1\n" +
            "    if fail:\n" +
            "        empty = []\n" +
            "        u = empty[0]\n" +
            "@command(free=True, acts_for=None)\n" +
            "def FeedHost():\n" +
            "    core.feed.all()\n";

        private static readonly Dictionary<string, Value> NoArgs = new Dictionary<string, Value>();

        public static int Main(string[] args)
        {
            var sizes = new List<int>();
            bool profileDerived = false, profileTick = false, shapes = false;
            foreach (var a in args)
            {
                if (a == "--shapes") shapes = true;
                else if (a == "--profile-derived") profileDerived = true;
                else if (a == "--profile-tick") profileTick = true;
                else sizes.Add(int.Parse(a, CultureInfo.InvariantCulture));
            }
            if (sizes.Count == 0) sizes.AddRange(new[] { 1000, 10000, 100000 });

            string root = Path.Combine(Path.GetTempPath(), "kesa-engine-bench");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Write(root, "core/schema/types.py", Schema);
            Write(root, "core/defs/defs.py", Defs);
            Write(root, "core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var content = Load(root);
            if (profileDerived || profileTick)
            {
                if (profileDerived) ProfileDerived(content);
                else ProfileTick(content);
                Directory.Delete(root, true);
                return 0;
            }

            if (shapes)
            {
                Shapes(content);
                Directory.Delete(root, true);
                return 0;
            }

            Console.WriteLine("| scene | N | median ms | ns per entity | KB allocated |");
            Console.WriteLine("|---|---:|---:|---:|---:|");
            // 처음 한 판은 JIT 예열이라 버린다
            Run(content, 200, print: false);
            foreach (int n in sizes) Run(content, n, print: true);
            Directory.Delete(root, true);
            return 0;
        }

        private static void Write(string root, string relative, string text)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static ContentLoadResult Load(string root)
        {
            var host = new HostFunctions();
            host.For("core").Writes("def feed.all() -> None", "모든 유닛의 식량을 1 늘린다.", (HostAccess access) =>
            {
                var amount = access.Member("Food", "amount");
                foreach (var id in access.All("Unit"))
                    access.Set(id, amount, new IntValue(((IntValue)access.Get(id, amount)).Value + 1));
            });
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(root, bag), bag);
            var content = ContentLoader.Load(root, order, bag, host);
            foreach (var d in bag.ToSortedList())
                if (d.Severity == Severity.Error) throw new InvalidOperationException(d.ToString());
            return content;
        }

        private static void Run(ContentLoadResult content, int n, bool print)
        {
            int reps = n >= 100000 ? 3 : 7;
            // 기본 예산 그대로 잰다: 스크립트가 N개를 도는 틱·커맨드는 행동 예산(1천만 단계, G224) 안이다
            var world = new World(content);
            var amount = world.Member("Food", "amount");
            var max = world.Member("Health", "max");

            Report(print, "create", n, Once(() =>
            {
                for (int i = 0; i < n; i++) world.Create("Soldier");
            }));
            var units = world.All("Unit");
            if (units.Count != n) throw new InvalidOperationException("expected " + n + " units");

            Report(print, "C# Get field", n, Measure(reps, () =>
            {
                foreach (var id in units) world.Get(id, amount);
            }));
            Report(print, "C# Set field", n, Measure(reps, () =>
            {
                foreach (var id in units) world.Set(id, amount, new IntValue(1));
            }));
            Report(print, "C# All(\"Unit\")", n, Measure(reps, () => world.All("Unit")));
            Report(print, "C# Query(\"Unit\")", n, Measure(reps, () => world.Query("Unit")));
            Report(print, "C# Query(\"Food\") (component only)", n, Measure(reps, () => world.Query("Food")));
            Report(print, "C# Get derived, no contributions", n, Measure(reps, () =>
            {
                foreach (var id in units) world.Get(id, max);
            }));
            Report(print, "add 2 contributions each", n, Once(() =>
            {
                foreach (var id in units)
                {
                    world.AddContribution(id, max, "add", new IntValue(1));
                    world.AddContribution(id, max, "add", new IntValue(2));
                }
            }));
            Report(print, "C# Get derived, 2 contributions", n, Measure(reps, () =>
            {
                foreach (var id in units) world.Get(id, max);
            }));
            if (((IntValue)world.Get(units[0], max)).Value != 13) throw new InvalidOperationException("derived is wrong");

            long before = ((IntValue)world.Get(units[0], amount)).Value;
            Report(print, "script tick: @on(Tick) updates all", n, Measure(reps, () => world.Advance(1)));
            if (((IntValue)world.Get(units[0], amount)).Value == before) throw new InvalidOperationException("the tick did nothing");

            var ok = new Dictionary<string, Value> { ["fail"] = BoolValue.False };
            var fail = new Dictionary<string, Value> { ["fail"] = BoolValue.True };
            Report(print, "script command: update all", n, Measure(reps, () => Submit(world, "core.FeedAll", ok, true)));
            Report(print, "script command: update all, then fail (rollback)", n,
                Measure(reps, () => Submit(world, "core.FeedAll", fail, false)));
            Report(print, "host function command: update all", n,
                Measure(reps, () => Submit(world, "core.FeedHost", NoArgs, true)));
        }

        /// <summary>
        /// 프로파일러용(<c>--profile-derived</c>): 기여 행 2개씩인 유닛 1천 개의 <c>derived</c>를 10초 동안 읽기만 한다.
        /// <c>dotnet-trace collect -- dotnet bench.dll --profile-derived</c>로 잡는다.
        /// </summary>
        private static void ProfileDerived(ContentLoadResult content)
        {
            var world = new World(content);
            var max = world.Member("Health", "max");
            for (int i = 0; i < 1000; i++)
            {
                var id = world.Create("Soldier");
                world.AddContribution(id, max, "add", new IntValue(1));
                world.AddContribution(id, max, "add", new IntValue(2));
            }
            var units = world.All("Unit");
            long reads = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 10000)
            {
                foreach (var id in units) world.Get(id, max);
                reads += units.Count;
            }
            Console.WriteLine("derived reads: " + reads.ToString("N0", CultureInfo.InvariantCulture) + ", " +
                              (sw.Elapsed.TotalMilliseconds * 1e6 / reads).ToString("0", CultureInfo.InvariantCulture) + " ns each");
        }

        /// <summary>프로파일러용(<c>--profile-tick</c>): 유닛 1만 개를 갱신하는 스크립트 틱을 10초 동안 돌린다.</summary>
        private static void ProfileTick(ContentLoadResult content)
        {
            var world = new World(content);
            for (int i = 0; i < 10000; i++) world.Create("Soldier");
            long ticks = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 10000)
            {
                world.Advance(1);
                ticks++;
            }
            Console.WriteLine("ticks: " + ticks.ToString("N0", CultureInfo.InvariantCulture) + ", " +
                              (sw.Elapsed.TotalMilliseconds / ticks).ToString("0.00", CultureInfo.InvariantCulture) + " ms each");
        }

        /// <summary>
        /// 실행기 몫 (<c>--shapes</c>): 모드 스크립트에 흔한 모양마다 스크립트와, 같은 일을 C# API로 한 것을 유닛 1만 개에서 잰다.
        /// C# 쪽은 거의 월드 접근 비용이라 차이가 실행기(실행 준비 포함) 몫이다.
        /// </summary>
        private static void Shapes(ContentLoadResult content)
        {
            const int n = 10000;
            const int reps = 9;
            var world = new World(content);
            for (int i = 0; i < n; i++) world.Create("Soldier");
            var units = world.All("Unit");
            var baseMax = world.Member("Health", "base_max");
            var current = world.Member("Health", "current");
            var amount = world.Member("Food", "amount");
            var formula = world.Member("Shape", "formula");
            var mood = world.Member("Shape", "mood");
            var crunch = world.Member("Shape", "crunch");
            long Int(EntityId id, MemberHandle m) => ((IntValue)world.Get(id, m)).Value;

            // C# 쪽이 같은 답을 내는지 먼저 본다(fixed 나눗셈은 정수로 흉내: 10 * 3 / 2는 나누어떨어진다)
            long CsFormula(EntityId id) => Int(id, baseMax) * 3 / 2 + Int(id, amount) * 2 - 1;
            long CsMood(EntityId id)
            {
                long hp = Int(id, current);
                if (hp < Int(id, baseMax) / 2 && Int(id, amount) < 3) return 0;
                if (hp < Int(id, baseMax)) return 1;
                return 2;
            }
            long CsCrunch()
            {
                long t = 0;
                for (long i = 0; i < 50; i++) t = t + i * i % 7;
                return t;
            }
            foreach (var id in units)
            {
                if (Int(id, formula) != CsFormula(id) || Int(id, mood) != CsMood(id) || Int(id, crunch) != CsCrunch())
                    throw new InvalidOperationException("a shape gives a different answer in C#");
            }

            Console.WriteLine("| shape | script ns | C# ns | interpreter share |");
            Console.WriteLine("|---|---:|---:|---:|");
            long sink = 0;
            var one = world.Member("Shape", "one");
            Compare("call only: return 1", n,
                Measure(reps, () => { foreach (var id in units) world.Get(id, one); }),
                Measure(reps, () => { foreach (var id in units) sink += 1; }));
            Compare("formula: 2 reads + arithmetic", n,
                Measure(reps, () => { foreach (var id in units) world.Get(id, formula); }),
                Measure(reps, () => { foreach (var id in units) sink += CsFormula(id); }));
            Compare("conditions: 3 reads + if/elif", n,
                Measure(reps, () => { foreach (var id in units) world.Get(id, mood); }),
                Measure(reps, () => { foreach (var id in units) sink += CsMood(id); }));
            Compare("compute loop: 50 rounds, no reads", n,
                Measure(reps, () => { foreach (var id in units) world.Get(id, crunch); }),
                Measure(reps, () => { foreach (var id in units) sink += CsCrunch(); }));
            Compare("tick: read + write one field", n,
                Measure(reps, () => world.Advance(1)),
                Measure(reps, () => { foreach (var id in units) world.Set(id, amount, new IntValue(Int(id, amount) + 1)); }));
            if (sink == 42) Console.WriteLine(); // C# 쪽 계산이 지워지지 않게
        }

        private static void Compare(string shape, int n, Result script, Result csharp)
        {
            double s = script.Ms * 1e6 / n, c = csharp.Ms * 1e6 / n;
            Console.WriteLine("| " + shape + " | " + s.ToString("0", CultureInfo.InvariantCulture) + " | " +
                              c.ToString("0", CultureInfo.InvariantCulture) + " | " +
                              ((s - c) / s * 100).ToString("0", CultureInfo.InvariantCulture) + "% |");
        }

        private static void Submit(World world, string command, Dictionary<string, Value> args, bool succeeds)
        {
            var r = world.Submit("host", command, args);
            if (r.Succeeded != succeeds)
                throw new InvalidOperationException(command + ": expected " + (succeeds ? "success" : "failure") + ", got " + r.Error);
        }

        private readonly struct Result
        {
            public Result(double ms, long bytes)
            {
                Ms = ms;
                Bytes = bytes;
            }

            public double Ms { get; }
            public long Bytes { get; }
        }

        private static Result Once(Action body)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            body();
            sw.Stop();
            return new Result(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
        }

        /// <summary>한 번 예열하고 <paramref name="reps"/>번 재서 중앙값. 할당량은 마지막 한 번.</summary>
        private static Result Measure(int reps, Action body)
        {
            body();
            var times = new List<double>();
            long bytes = 0;
            for (int i = 0; i < reps; i++)
            {
                var r = Once(body);
                times.Add(r.Ms);
                bytes = r.Bytes;
            }
            times.Sort();
            return new Result(times[times.Count / 2], bytes);
        }

        private static void Report(bool print, string scene, int n, Result r)
        {
            if (!print) return;
            Console.WriteLine("| " + scene + " | " + n.ToString("N0", CultureInfo.InvariantCulture) + " | " +
                              r.Ms.ToString("0.00", CultureInfo.InvariantCulture) + " | " +
                              (r.Ms * 1e6 / n).ToString("0", CultureInfo.InvariantCulture) + " | " +
                              (r.Bytes / 1024).ToString("N0", CultureInfo.InvariantCulture) + " |");
        }
    }
}
