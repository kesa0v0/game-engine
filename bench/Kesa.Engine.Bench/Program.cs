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
            "deftype(\"Unit\", components=[\"Health\", \"Food\"])\n";

        private const string Defs =
            "Unit(\"Soldier\")\n" +
            "@on(Tick)\n" +
            "def grow():\n" +
            "    for u in query(\"Unit\"):\n" +
            "        u.Food.amount += 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def FeedAll(fail: bool):\n" +
            "    for u in query(\"Unit\"):\n" +
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
            bool profileDerived = false;
            foreach (var a in args)
            {
                if (a == "--profile-derived") profileDerived = true;
                else sizes.Add(int.Parse(a, CultureInfo.InvariantCulture));
            }
            if (sizes.Count == 0) sizes.AddRange(new[] { 1000, 10000, 100000 });

            string root = Path.Combine(Path.GetTempPath(), "kesa-engine-bench");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Write(root, "core/schema/types.py", Schema);
            Write(root, "core/defs/defs.py", Defs);
            Write(root, "core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var content = Load(root);
            if (profileDerived)
            {
                ProfileDerived(content);
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
            // 스크립트가 N개를 도는 장면이 기본 예산(100,000걸음)을 넘으므로 예산을 풀고 잰다
            var world = new World(content, stepBudget: int.MaxValue);
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
