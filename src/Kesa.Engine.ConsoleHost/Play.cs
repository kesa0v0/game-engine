using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Reporting;
using Kesa.Engine.Runtime;
using Kesa.Engine.Text;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// <c>kesa-play &lt;mods-dir&gt; --seat &lt;conn&gt;[=&lt;Def&gt;]... [--seed N]</c>: 모드 폴더를 불러 터미널에서 한 판을 돌린다.
    /// <c>--seat red</c>는 앉을 후보(World.SeatChoices, G239)가 생기면 연결 <c>red</c>가 어디에 앉을지 화면에서 묻는다.
    /// <c>--seat red=Red</c>는 묻지 않고 Def <c>Red</c>인 엔티티가 생기면 앉힌다(G236, 테스트·자동 실행용).
    /// 종료 코드는 CLI와 같다(G78): 0 끝까지 돎, 1 모드 콘텐츠 오류, 2 사용법.
    /// </summary>
    public static class Play
    {
        public const string Usage = "usage: kesa-play <mods-dir> --seat <connection>[=<Def>] [--seat <connection>[=<Def>]...] [--seed <n>] [--lang <tag>[,<tag>...]]";

        public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
        {
            string? root = null;
            var seats = new List<(string Connection, string? Def)>();
            int seed = 1;
            string[]? languages = null;
            for (int i = 0; i < args.Length; i++)
            {
                string? value = args[i] == "--seat" && i + 1 < args.Length ? args[i + 1] : null;
                int eq = value == null ? -1 : value.IndexOf('=');
                if (value != null && eq < 0 && value.Length > 0 && !value.StartsWith("--", StringComparison.Ordinal))
                {
                    seats.Add((value, null));
                    i++;
                }
                else if (value != null && eq > 0 && eq < value.Length - 1)
                {
                    seats.Add((value.Substring(0, eq), value.Substring(eq + 1)));
                    i++;
                }
                else if (args[i] == "--seed" && i + 1 < args.Length &&
                         int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) i++;
                else if (args[i] == "--lang" && i + 1 < args.Length && languages == null &&
                         Array.TrueForAll(args[i + 1].Split(','), LanguageTag.IsValid))
                    languages = args[++i].Split(',');
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

            var world = new World(content, seed: seed);
            var panels = ConsoleUi.Load(root, content);
            var wrong = ConsoleUi.Check(world, panels);
            if (wrong.Count > 0)
            {
                foreach (var line in wrong) stderr.WriteLine(line);
                return 1;
            }
            new ConsoleGame(content, world, seats, panels, Translator(content, languages ?? DefaultLanguages(), stderr), stdin, stdout).Run();
            return 0;
        }

        /// <summary>콘솔 자신의 번역 폴더: 실행 파일 옆 <c>locale/</c> (G274).</summary>
        public static string HostLocale => Path.Combine(AppContext.BaseDirectory, Translations.LocaleFolder);

        /// <summary><c>--lang</c>이 없으면 OS 표시 언어 뒤에 <c>en</c> (G274).</summary>
        public static string[] DefaultLanguages()
        {
            string os = CultureInfo.CurrentUICulture.Name;
            return os.Length == 0 || os == "en" ? new[] { "en" } : new[] { os, "en" };
        }

        /// <summary>
        /// 번역을 읽는다. 번역 오류는 판을 막지 않는다(G257): 읽기 진단은 정보를 빼고 stderr에, 화면을 그리다 난 번역 오류는 같은 것을 한 번만.
        /// </summary>
        private static Translator Translator(ContentLoadResult content, string[] languages, TextWriter stderr)
        {
            var bag = new DiagnosticBag();
            var texts = Translations.Load(content, bag, HostLocale);
            foreach (var d in bag.ToSortedList())
                if (d.Severity != Severity.Info) stderr.WriteLine(d);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return texts.For(languages, message =>
            {
                if (seen.Add(message)) stderr.WriteLine("translation: " + message);
            });
        }
    }
}
