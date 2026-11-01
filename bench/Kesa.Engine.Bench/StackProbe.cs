using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Bench
{
    /// <summary>
    /// 스크립트 호출 한 단이 C# 스택을 몇 바이트 쓰나 (G452). 넘침은 잡을 수 없으므로 넘기지 않고, 재귀 맨 밑의 호스트 함수에서
    /// 지역 변수 주소를 재어 깊이 1과 깊이 N의 차이로 낸다.
    /// <c>dotnet run -c Release --project bench/Kesa.Engine.Bench -- --stack</c>
    /// </summary>
    public static unsafe class StackProbe
    {
        // 식 안의 재귀(C# 재귀로 들어감), 대입 오른쪽의 재귀, 커맨드 본문에서 시작한 식 재귀
        private const string Defs =
            "def expr(n):\n    if n == 0:\n        return core.mark()\n    return expr(n - 1)\n" +
            "def stmt(n):\n    if n == 0:\n        return core.mark()\n    r = stmt(n - 1)\n    return r\n" +
            "@command(free=True, acts_for=None)\ndef Deep(n: int):\n    r = expr(n)\n";

        private static long _deepest;

        public static bool Handles(string[] args)
        {
            if (args.Length != 1 || args[0] != "--stack") return false;
            var content = Load();
            Console.WriteLine("| path | bytes per call |");
            Console.WriteLine("|---|---:|");
            foreach (var kind in new[] { "expr", "stmt", "command", "wrap" })
            {
                long one = Measure(content, kind, 1), many = Measure(content, kind, 101);
                Console.WriteLine("| " + kind + " | " + ((many - one) / 100).ToString(CultureInfo.InvariantCulture) + " |");
            }
            return true;
        }

        private static ContentLoadResult Load()
        {
            string root = Path.Combine(Path.GetTempPath(), "kesa-engine-stack");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(Path.Combine(root, "core", "defs"));
            File.WriteAllText(Path.Combine(root, "core", "mod.py"), "Mod(id=\"core\")\nuse_all(\"defs/\")\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "core", "defs", "defs.py"),
                "patch.set(\"engine:Rules\", \"max_call_depth\", 1000)\n" + Defs + "def base(n):\n    return 0\n", new UTF8Encoding(false));
            // 감싼 함수 안에서 대상을 이름으로 부르면 다시 감싼 것으로 들어간다: 한 단이 프레임 둘이다
            Directory.CreateDirectory(Path.Combine(root, "outer", "defs"));
            File.WriteAllText(Path.Combine(root, "outer", "mod.py"), "Mod(id=\"outer\", dependencies=[\"core\"])\nuse_all(\"defs/\")\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "outer", "defs", "defs.py"),
                "@wrap(core.base)\ndef more(original, n):\n    if n == 0:\n        return core.mark()\n    return core.base(n - 1) + 1\n", new UTF8Encoding(false));
            var host = new HostFunctions();
            host.For("core").Pure("def mark() -> int", "재귀 맨 밑의 스택 주소를 적는다.", () =>
            {
                int here;
                _deepest = (long)&here;
                return 0L;
            });
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(root, ModSorter.Sort(ModDiscovery.Discover(root, bag), bag), bag, host);
            Directory.Delete(root, true);
            foreach (var d in bag.ToSortedList())
                if (d.Severity == Severity.Error) throw new InvalidOperationException(d.ToString());
            return content;
        }

        /// <summary>새 스레드의 바닥에서 재귀 맨 밑까지 쓴 스택 바이트.</summary>
        private static long Measure(ContentLoadResult content, string kind, int depth)
        {
            long used = 0;
            var thread = new Thread(() =>
            {
                int bottom;
                long start = (long)&bottom;
                var world = new World(content);
                if (kind == "command")
                {
                    var r = world.Submit("host", "core.Deep", new Dictionary<string, Value> { ["n"] = new IntValue(depth) });
                    if (!r.Succeeded) throw new InvalidOperationException(r.Error?.ToString());
                }
                else world.Call("core." + (kind == "wrap" ? "base" : kind), new IntValue(depth));
                used = start - _deepest;
            }, 16 << 20);
            thread.Start();
            thread.Join();
            return used;
        }
    }
}
