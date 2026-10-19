using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Reporting;
using Kesa.Engine.Runtime;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// <c>kesa-play &lt;mods-dir&gt; --seat &lt;conn&gt;... [--seed N]</c>: 모드 폴더를 불러 터미널에서 한 판을 돌린다.
    /// 종료 코드는 CLI와 같다(G78): 0 끝까지 돎, 1 모드 콘텐츠 오류, 2 사용법.
    /// </summary>
    public static class Play
    {
        public const string Usage = "usage: kesa-play <mods-dir> --seat <connection> [--seat <connection>...] [--seed <n>]";

        public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            string? root = null;
            var seats = new List<string>();
            int seed = 1;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--seat" && i + 1 < args.Length) seats.Add(args[++i]);
                else if (args[i] == "--seed" && i + 1 < args.Length &&
                         int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) i++;
                else if (root == null && !args[i].StartsWith("--", StringComparison.Ordinal)) root = args[i];
                else
                {
                    stderr.WriteLine(Usage);
                    return 2;
                }
            }
            if (root == null || seats.Count == 0 || !Directory.Exists(root))
            {
                stderr.WriteLine(root != null && !Directory.Exists(root) ? "no mods folder at " + root : Usage);
                return 2;
            }

            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(root, bag), bag);
            var content = ContentLoader.Load(root, order, bag);
            var diagnostics = bag.ToSortedList();
            if (System.Linq.Enumerable.Any(diagnostics, d => d.Severity == Severity.Error))
            {
                stderr.Write(CheckReport.ToText(diagnostics, content, root.TrimEnd('/', '\\') + "/"));
                return 1;
            }

            var world = new World(content, seed: seed, requireSubmitPolicy: true);
            new ConsoleGame(content, world, seats, stdin, stdout).Run();
            return 0;
        }
    }
}
