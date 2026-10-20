using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Reporting;
using Kesa.Engine.Stubs;
using Kesa.Engine.Text;

namespace Kesa.Engine.Cli
{
    /// <summary>
    /// CLI 명령 (spec S9). 종료 코드는 spec-gaps G78, <c>check</c> 출력 형식은 G79, <c>dump</c>는 G80, <c>stubs</c>는 G81, <c>--host</c>는 G221.
    /// </summary>
    public static class Commands
    {
        public const int ExitOk = 0;
        public const int ExitContentErrors = 1;
        public const int ExitUsage = 2;
        public const int ExitInternal = 3;

        /// <summary>경고를 오류로 올린다 (G84). <c>check</c>만 받는다.</summary>
        public const string WarningsAsErrors = "--warnings-as-errors";

        public const string Usage =
            "usage: engine check <mods-dir> [--host <file>] [--json] [--warnings-as-errors]\n" +
            "       engine dump <mods-dir> [--host <file>]\n" +
            "       engine stubs <mods-dir> [--host <file>]";

        /// <param name="args">명령 이름까지 포함한 전체 인자</param>
        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            if (args.Length >= 1 && args[0] == "check") return Check(args[1..], stdout, stderr);
            if (args.Length >= 1 && args[0] == "dump") return Dump(args[1..], stdout, stderr);
            if (args.Length >= 1 && args[0] == "stubs") return Stubs(args[1..], stdout, stderr);
            stderr.WriteLine(Usage);
            return ExitUsage;
        }

        /// <summary><c>engine check &lt;mods-dir&gt; [--json]</c>: 진단을 텍스트나 JSON으로 stdout에 낸다 (G79).</summary>
        private static int Check(string[] args, TextWriter stdout, TextWriter stderr)
        {
            int parsed = Parse("check", args, new[] { "--json", WarningsAsErrors }, stderr, out var root, out var options, out var host);
            if (parsed != ExitOk) return parsed;
            bool json = options.Contains("--json");
            return LoadAndReport("check", root, host, stderr, (diagnostics, result, _) =>
            {
                stdout.Write(json ? CheckReport.ToJson(diagnostics, result) : CheckReport.ToText(diagnostics, result, PathPrefix(root)));
                return ExitOk;
            }, warningsAsErrors: options.Contains(WarningsAsErrors), translations: true);
        }

        /// <summary><c>engine dump &lt;mods-dir&gt;</c>: 덤프 JSON은 stdout, 진단 텍스트는 stderr (G80).</summary>
        private static int Dump(string[] args, TextWriter stdout, TextWriter stderr)
        {
            int parsed = Parse("dump", args, Array.Empty<string>(), stderr, out var root, out _, out var host);
            if (parsed != ExitOk) return parsed;
            return LoadAndReport("dump", root, host, stderr, (diagnostics, result, _) =>
            {
                stdout.Write(DefDump.ToJson(result));
                stderr.Write(CheckReport.ToText(diagnostics, result, PathPrefix(root)));
                return ExitOk;
            });
        }

        public const string StubFileName = "__builtins__.pyi";

        /// <summary>스키마 파일 폴더 (<see cref="ContentLoader.SchemaFolder"/>에서 끝의 <c>/</c>를 뺀 것).</summary>
        private static readonly string SchemaDirectory = ContentLoader.SchemaFolder.TrimEnd('/');
        public const string PyrightConfigFileName = "pyrightconfig.json";
        /// <summary>
        /// "None일 수 있음" 계열은 경고다 (spec-gaps G208): 모드가 규칙으로 아는 값(<c>optional=True</c> Ref 등)에 빨간 오류를 내지 않고,
        /// 이 언어에는 <c>assert</c>가 없어 좁힐 방법이 <c>if</c>뿐이기 때문이다.
        /// </summary>
        public const string PyrightConfig =
            "{\n" +
            "  \"pythonVersion\": \"3.12\",\n" +
            "  \"reportOptionalMemberAccess\": \"warning\",\n" +
            "  \"reportOptionalSubscript\": \"warning\",\n" +
            "  \"reportOptionalIterable\": \"warning\",\n" +
            "  \"reportOptionalCall\": \"warning\",\n" +
            "  \"reportOptionalOperand\": \"warning\",\n" +
            "  \"reportOptionalContextManager\": \"warning\"\n" +
            "}\n";

        /// <summary>
        /// <c>engine stubs &lt;mods-dir&gt;</c>: 매니페스트를 읽은 모드마다 <c>__builtins__.pyi</c>를 덮어쓰고(G81, 비활성 모드는 G83),
        /// <c>schema/</c> 폴더가 있으면 그 안에 스키마 스텁도 쓴다(G82). <c>pyrightconfig.json</c>은 없을 때만 만든다. 쓴 파일은 stdout, 진단 텍스트는 stderr. 파일 쓰기 실패는 환경 문제(2)다.
        /// </summary>
        private static int Stubs(string[] args, TextWriter stdout, TextWriter stderr)
        {
            int parsed = Parse("stubs", args, Array.Empty<string>(), stderr, out var root, out _, out var host);
            if (parsed != ExitOk) return parsed;
            string prefix = PathPrefix(root);
            return LoadAndReport("stubs", root, host, stderr, (diagnostics, result, discovered) =>
            {
                stderr.Write(CheckReport.ToText(diagnostics, result, prefix));
                string current = "";
                try
                {
                    // 매니페스트를 읽은 모든 모드에 쓴다: 오류가 있는 모드야말로 편집기 도움이 필요하다 (G83)
                    foreach (var mod in discovered)
                    {
                        current = prefix + mod.Directory + "/" + StubFileName;
                        string stubPath = Path.Combine(root, mod.Directory, StubFileName);
                        if (StubGenerator.CanGenerate(mod.Id, result))
                        {
                            WriteText(stubPath, StubGenerator.Generate(mod.Id, result));
                            stdout.Write("wrote " + current + "\n");
                        }
                        else if (File.Exists(stubPath))
                        {
                            // ②에서 실패해 선언을 알 수 없다. 전에 만든 스텁이 지금 것보다 낫다
                            stdout.Write("kept " + current + " (the mod failed to load)\n");
                        }
                        else
                        {
                            WriteText(stubPath, StubGenerator.GenerateManifestOnly(mod.Id));
                            stdout.Write("wrote " + current + " (mod.py names only; the mod failed to load)\n");
                        }

                        string schemaDir = Path.Combine(root, mod.Directory, SchemaDirectory);
                        if (!Directory.Exists(schemaDir)) continue;
                        current = prefix + mod.Directory + "/" + SchemaDirectory + "/" + StubFileName;
                        WriteText(Path.Combine(schemaDir, StubFileName), StubGenerator.GenerateSchema(mod.Id, result));
                        stdout.Write("wrote " + current + "\n");
                    }
                    string config = Path.Combine(root, PyrightConfigFileName);
                    current = prefix + PyrightConfigFileName;
                    if (!File.Exists(config))
                    {
                        WriteText(config, PyrightConfig);
                        stdout.Write("wrote " + current + "\n");
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    stderr.WriteLine("engine stubs: cannot write '" + current + "': " + e.Message);
                    return ExitUsage;
                }
                return ExitOk;
            });
        }

        private static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

        /// <summary>호스트 함수 선언 파일을 받는 옵션 (spec-gaps G221). 모든 명령이 받는다.</summary>
        public const string HostOption = "--host";

        /// <summary>
        /// 모드 폴더 하나와 허용된 옵션만 받는다. 아니면 사용법 오류 (G78의 2). <c>--host &lt;file&gt;</c>는 선언 파일을 읽어
        /// <paramref name="host"/>로 준다 (G221). 파일을 못 읽거나 틀렸으면 환경 문제(2)다.
        /// </summary>
        private static int Parse(string command, string[] args, string[] allowed, TextWriter stderr,
            out string root, out HashSet<string> options, out HostFunctions? host)
        {
            root = "";
            options = new HashSet<string>(StringComparer.Ordinal);
            host = null;
            string? folder = null;
            string? hostFile = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == HostOption)
                {
                    if (hostFile != null) return UsageError(command, stderr, "more than one " + HostOption + " file given");
                    if (i + 1 >= args.Length) return UsageError(command, stderr, HostOption + " needs a file");
                    hostFile = args[++i];
                }
                else if (Array.IndexOf(allowed, a) >= 0) options.Add(a);
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
            if (hostFile == null) return ExitOk;

            string text;
            try
            {
                text = File.ReadAllText(hostFile, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                                      e is NotSupportedException)
            {
                stderr.WriteLine("engine " + command + ": cannot read host declarations file '" + hostFile + "': " + e.Message);
                return ExitUsage;
            }
            try
            {
                host = HostFunctions.ImportDeclarations(text);
            }
            catch (FormatException e)
            {
                stderr.WriteLine("engine " + command + ": '" + hostFile + "': " + e.Message);
                return ExitUsage;
            }
            return ExitOk;
        }

        /// <summary>
        /// ①~⑥을 돌리고 결과를 넘긴다. <paramref name="report"/>가 0이 아닌 코드를 돌려주면 그 코드로 끝나고,
        /// 아니면 오류 진단이 있을 때 1. 엔진 예외는 3 (G78). <paramref name="warningsAsErrors"/>면 경고를 오류로 올린 진단을
        /// 넘기고 그것으로 종료 코드를 정한다 (G84).
        /// </summary>
        internal static int LoadAndReport(string command, string root, HostFunctions? host, TextWriter stderr,
            Func<IReadOnlyList<Diagnostic>, ContentLoadResult, IReadOnlyList<ModManifest>, int> report,
            bool warningsAsErrors = false, bool translations = false)
        {
            try
            {
                var bag = new DiagnosticBag();
                var discovery = ModDiscovery.Discover(root, bag);
                var order = ModSorter.Sort(discovery, bag);
                var result = ContentLoader.Load(root, order, bag, host);
                // 번역 파일 읽기와 검사 (G257). 번역 오류는 모드를 끄지 않는다
                if (translations) TranslationCheck.Run(Translations.Load(result, bag), result, bag);
                var diagnostics = bag.ToSortedList();
                if (warningsAsErrors) diagnostics = PromoteWarnings(diagnostics);
                int reported = report(diagnostics, result, discovery.Mods);
                if (reported != ExitOk) return reported;
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

        /// <summary>
        /// 경고만 오류로 바꾼다. 정보는 알림이라 그대로 둔다 (G84). 정렬 기준에 심각도가 없어 순서는 그대로다.
        /// </summary>
        internal static IReadOnlyList<Diagnostic> PromoteWarnings(IReadOnlyList<Diagnostic> diagnostics)
        {
            var promoted = new List<Diagnostic>(diagnostics.Count);
            foreach (var d in diagnostics)
            {
                promoted.Add(d.Severity != Severity.Warning
                    ? d
                    : new Diagnostic(d.Code, Severity.Error, d.Message, d.Mod, d.Span, d.Hint, d.Related));
            }
            return promoted;
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
