using System;
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
    /// 탐색 봇의 비용: 틱택토 전체 minimax를 로직 언어와 C#으로 같은 방식(수마다 새 판)으로 돌려 노드당 시간을 잰다.
    /// <c>dotnet run -c Release --project bench/Kesa.Engine.Bench -- --minimax</c>
    /// </summary>
    public static class MinimaxProbe
    {
        private const string Defs =
            "patch.set(\"engine:Rules\", \"step_budget\", 2000000000)\n" +
            "LINES = [[0, 1, 2], [3, 4, 5], [6, 7, 8], [0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]]\n" +
            "def winner(b):\n" +
            "    for l in LINES:\n" +
            "        if b[l[0]] != 0 and b[l[0]] == b[l[1]] and b[l[1]] == b[l[2]]:\n" +
            "            return b[l[0]]\n" +
            "    return 0\n" +
            "def score(b, p):\n" +
            "    w = winner(b)\n" +
            "    if w != 0:\n" +
            "        return w\n" +
            "    best = -2 * p\n" +
            "    moved = False\n" +
            "    for i in range(9):\n" +
            "        if b[i] == 0:\n" +
            "            moved = True\n" +
            "            v = score([p if j == i else b[j] for j in range(9)], -p)\n" +
            "            if (p == 1 and v > best) or (p == -1 and v < best):\n" +
            "                best = v\n" +
            "    if not moved:\n" +
            "        return 0\n" +
            "    return best\n";

        private static readonly int[][] Lines =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 }, new[] { 0, 3, 6 },
            new[] { 1, 4, 7 }, new[] { 2, 5, 8 }, new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
        };

        private static long _nodes;

        public static bool Handles(string[] args)
        {
            if (args.Length != 1 || args[0] != "--minimax") return false;
            var world = new World(Load());
            var empty = new ListValue(new Value[] { Zero, Zero, Zero, Zero, Zero, Zero, Zero, Zero, Zero });

            _nodes = 0;
            int csharp = Score(new int[9], 1);
            long nodes = _nodes;
            var watch = Stopwatch.StartNew();
            for (int r = 0; r < 5; r++) Score(new int[9], 1);
            double csMs = watch.Elapsed.TotalMilliseconds / 5;

            var script = world.Call("core.score", empty, new IntValue(1));
            watch.Restart();
            world.Call("core.score", empty, new IntValue(1));
            double scriptMs = watch.Elapsed.TotalMilliseconds;

            Console.WriteLine("tic-tac-toe full minimax: " + nodes.ToString("N0", CultureInfo.InvariantCulture) + " nodes, value C# " +
                              csharp + ", script " + ((IntValue)script.Value).Value);
            Console.WriteLine("| | total ms | ns per node |");
            Console.WriteLine("|---|---:|---:|");
            Row("C#", csMs, nodes);
            Row("script", scriptMs, nodes);
            Console.WriteLine("script / C# = " + (scriptMs / csMs).ToString("F0", CultureInfo.InvariantCulture) + "x");
            return true;
        }

        private static readonly IntValue Zero = new IntValue(0);

        private static void Row(string name, double ms, long nodes) =>
            Console.WriteLine("| " + name + " | " + ms.ToString("F1", CultureInfo.InvariantCulture) + " | " +
                              (ms * 1_000_000 / nodes).ToString("F0", CultureInfo.InvariantCulture) + " |");

        private static int Winner(int[] b)
        {
            foreach (var l in Lines)
                if (b[l[0]] != 0 && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]]) return b[l[0]];
            return 0;
        }

        private static int Score(int[] b, int p)
        {
            _nodes++;
            int w = Winner(b);
            if (w != 0) return w;
            int best = -2 * p;
            bool moved = false;
            for (int i = 0; i < 9; i++)
            {
                if (b[i] != 0) continue;
                moved = true;
                var next = (int[])b.Clone();
                next[i] = p;
                int v = Score(next, -p);
                if ((p == 1 && v > best) || (p == -1 && v < best)) best = v;
            }
            return moved ? best : 0;
        }

        private static ContentLoadResult Load()
        {
            string root = Path.Combine(Path.GetTempPath(), "kesa-engine-minimax");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(Path.Combine(root, "core", "defs"));
            File.WriteAllText(Path.Combine(root, "core", "mod.py"), "Mod(id=\"core\")\nuse_all(\"defs/\")\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "core", "defs", "defs.py"), Defs, new UTF8Encoding(false));
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(root, ModSorter.Sort(ModDiscovery.Discover(root, bag), bag), bag, new HostFunctions());
            Directory.Delete(root, true);
            foreach (var d in bag.ToSortedList())
                if (d.Severity == Severity.Error) throw new InvalidOperationException(d.ToString());
            return content;
        }
    }
}
