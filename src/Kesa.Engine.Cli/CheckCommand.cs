using System;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Reporting;

namespace Kesa.Engine.Cli
{
    /// <summary>
    /// <c>engine check &lt;mods-dir&gt; [--json]</c> (spec S9). 종료 코드는 spec-gaps G78, 출력 형식은 G79.
    /// </summary>
    public static class CheckCommand
    {
        public const int ExitOk = 0;
        public const int ExitContentErrors = 1;
        public const int ExitUsage = 2;
        public const int ExitInternal = 3;

        public const string Usage = "usage: engine check <mods-dir> [--json]";

        /// <param name="args">명령 이름 <c>check</c>를 뺀 인자</param>
        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            string? root = null;
            bool json = false;
            foreach (var a in args)
            {
                if (a == "--json") json = true;
                else if (a.StartsWith("-", StringComparison.Ordinal))
                    return UsageError(stderr, "unknown option '" + a + "'");
                else if (root != null)
                    return UsageError(stderr, "more than one mods folder given");
                else root = a;
            }
            if (root == null) return UsageError(stderr, "no mods folder given");
            if (!Directory.Exists(root))
            {
                stderr.WriteLine("engine check: mods folder '" + root + "' does not exist");
                return ExitUsage;
            }

            try
            {
                var bag = new DiagnosticBag();
                var order = ModSorter.Sort(ModDiscovery.Discover(root, bag), bag);
                var result = ContentLoader.Load(root, order, bag);
                var diagnostics = bag.ToSortedList();

                stdout.Write(json ? CheckReport.ToJson(diagnostics, result) : CheckReport.ToText(diagnostics, result, PathPrefix(root)));
                foreach (var d in diagnostics)
                    if (d.Severity == Severity.Error) return ExitContentErrors;
                return ExitOk;
            }
            catch (Exception e)
            {
                stderr.WriteLine("engine check: internal error (this is an engine bug, please report it)");
                stderr.WriteLine(e.ToString());
                return ExitInternal;
            }
        }

        /// <summary>사용자가 준 폴더 경로를 <c>/</c>로 끝나게 한다. 텍스트의 파일 경로가 현재 폴더 기준이 된다 (G79).</summary>
        internal static string PathPrefix(string root)
        {
            string trimmed = root.TrimEnd('/', '\\');
            return trimmed.Length == 0 ? root : trimmed + "/";
        }

        private static int UsageError(TextWriter stderr, string message)
        {
            stderr.WriteLine("engine check: " + message);
            stderr.WriteLine(Usage);
            return ExitUsage;
        }
    }
}
