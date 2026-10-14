using System;
using System.Collections.Generic;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Reporting;

namespace Kesa.Engine.Cli
{
    /// <summary>
    /// CLI 명령 (spec S9). 종료 코드는 spec-gaps G78, <c>check</c> 출력 형식은 G79, <c>dump</c>는 G80.
    /// </summary>
    public static class Commands
    {
        public const int ExitOk = 0;
        public const int ExitContentErrors = 1;
        public const int ExitUsage = 2;
        public const int ExitInternal = 3;

        public const string Usage =
            "usage: engine check <mods-dir> [--json]\n" +
            "       engine dump <mods-dir>";

        /// <param name="args">명령 이름까지 포함한 전체 인자</param>
        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length >= 1 && args[0] == "check") return Check(args[1..], stdout, stderr);
            if (args.Length >= 1 && args[0] == "dump") return Dump(args[1..], stdout, stderr);
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        /// <summary><c>engine check &lt;mods-dir&gt; [--json]</c>: 진단을 텍스트나 JSON으로 stdout에 낸다 (G79).</summary>
        private static int Check(string[] args, TextWriter stdout, TextWriter stderr)
        {
            int parsed = Parse("check", args, new[] { "--json" }, stderr, out var root, out var options);
            if (parsed != ExitOk) return parsed;
            bool json = options.Contains("--json");
            return LoadAndReport("check", root, stderr, (diagnostics, result) =>
                stdout.Write(json ? CheckReport.ToJson(diagnostics, result) : CheckReport.ToText(diagnostics, result, PathPrefix(root))));
        }

        /// <summary><c>engine dump &lt;mods-dir&gt;</c>: 덤프 JSON은 stdout, 진단 텍스트는 stderr (G80).</summary>
        private static int Dump(string[] args, TextWriter stdout, TextWriter stderr)
        {
            int parsed = Parse("dump", args, Array.Empty<string>(), stderr, out var root, out _);
            if (parsed != ExitOk) return parsed;
            return LoadAndReport("dump", root, stderr, (diagnostics, result) =>
            {
                stdout.Write(DefDump.ToJson(result));
                stderr.Write(CheckReport.ToText(diagnostics, result, PathPrefix(root)));
            });
        }

        /// <summary>모드 폴더 하나와 허용된 옵션만 받는다. 아니면 사용법 오류 (G78의 2).</summary>
        private static int Parse(string command, string[] args, string[] allowed, TextWriter stderr,
            out string root, out HashSet<string> options)
        {
            root = "";
            options = new HashSet<string>(StringComparer.Ordinal);
            string? folder = null;
            foreach (var a in args)
            {
                if (Array.IndexOf(allowed, a) >= 0) options.Add(a);
                else if (a.StartsWith("-", StringComparison.Ordinal))
                    return UsageError(command, stderr, "unknown option '" + a + "'");
                else if (folder != null)
                    return UsageError(command, stderr, "more than one mods folder given");
                else folder = a;
            }
            if (folder == null) return UsageError(command, stderr, "no mods folder given");
            if (!Directory.Exists(folder))
            {
                stderr.WriteLine("engine " + command + ": mods folder '" + folder + "' does not exist");
                return ExitUsage;
            }
            root = folder;
            return ExitOk;
        }

        /// <summary>①~⑥을 돌리고 결과를 넘긴다. 오류 진단이 있으면 1, 엔진 예외는 3 (G78).</summary>
        private static int LoadAndReport(string command, string root, TextWriter stderr,
            Action<IReadOnlyList<Diagnostic>, ContentLoadResult> report)
        {
            try
            {
                var bag = new DiagnosticBag();
                var order = ModSorter.Sort(ModDiscovery.Discover(root, bag), bag);
                var result = ContentLoader.Load(root, order, bag);
                var diagnostics = bag.ToSortedList();
                report(diagnostics, result);
                foreach (var d in diagnostics)
                    if (d.Severity == Severity.Error) return ExitContentErrors;
                return ExitOk;
            }
            catch (Exception e)
            {
                stderr.WriteLine("engine " + command + ": internal error (this is an engine bug, please report it)");
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

        private static int UsageError(string command, TextWriter stderr, string message)
        {
            stderr.WriteLine("engine " + command + ": " + message);
            stderr.WriteLine(Usage);
            return ExitUsage;
        }
    }
}
