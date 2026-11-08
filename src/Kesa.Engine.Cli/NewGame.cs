using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Kesa.Engine.Loading;

namespace Kesa.Engine.Cli
{
    /// <summary>
    /// <c>engine new &lt;이름&gt;</c>: 엔진 저장소의 <c>hosts/unity/template</c>을 복사해 새 Unity 게임을 만든다.
    /// copy(기본)는 엔진 패키지·안내서·CLI를 게임 안에 넣고, link는 엔진까지의 상대 경로를 적는다.
    /// </summary>
    public static class NewGame
    {
        public const string Usage = "usage: engine new <name> [--dir <path>] [--id <mod-id>] [--engine copy|link] [--no-git]";

        private const string TemplatePath = "hosts/unity/template";
        private const string PackagePath = "hosts/unity/uk.kesa.engine.unity";
        private const string PackageName = "uk.kesa.engine.unity";
        private const string TemplateModId = "game";
        private const string TemplateLink = "file:../../" + PackageName;

        // 템플릿 밖으로 가져가지 않는 것: Unity 생성물과 템플릿 설명
        private static readonly string[] SkippedTop = { "Library", "Logs", "Temp", "UserSettings", "obj", "README.md" };

        /// <summary>CLI와 Unity 패키지가 함께 쓰는 DLL. copy는 둘이 같은 빌드인지 본다.</summary>
        private static readonly string[] SharedAssemblies = { "Kesa.Engine.Kernel", "Kesa.Engine.Ui", "Kesa.Engine.Text", "Kesa.Engine.Bridge" };

        /// <param name="engineRoot">테스트용. 없으면 실행 중인 CLI 위에서 <c>Kesa.Engine.slnx</c>를 찾는다.</param>
        /// <param name="cliFolder">테스트용. 복사할 CLI DLL이 있는 폴더. 없으면 실행 중인 CLI의 폴더.</param>
        public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string? engineRoot = null, string? cliFolder = null)
        {
            string? name = null, dir = null, id = null, mode = "copy";
            bool git = true;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--dir" || a == "--id" || a == "--engine")
                {
                    if (i + 1 >= args.Length) return UsageError(stderr, a + " needs a value");
                    string v = args[++i];
                    if (a == "--dir") dir = v;
                    else if (a == "--id") id = v;
                    else mode = v;
                }
                else if (a == "--no-git") git = false;
                else if (a.StartsWith("-", StringComparison.Ordinal)) return UsageError(stderr, "unknown option '" + a + "'");
                else if (name != null) return UsageError(stderr, "more than one name given");
                else name = a;
            }
            if (name == null) return UsageError(stderr, "no name given");
            if (mode != "copy" && mode != "link") return UsageError(stderr, "--engine is 'copy' or 'link', not '" + mode + "'");
            if (name.Trim().Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/'))
                return UsageError(stderr, "'" + name + "' cannot be a folder name");
            id ??= name.ToLowerInvariant();
            if (!ManifestReader.IsValidId(id) || id == ManifestReader.ReservedId)
                return UsageError(stderr, "'" + id + "' is not a mod id (lowercase letters, digits and '_', starting with a letter, not 'engine'); give one with --id");

            engineRoot ??= FindEngineRoot();
            if (engineRoot == null || !Directory.Exists(Path.Combine(engineRoot, TemplatePath)))
            {
                stderr.WriteLine("engine new: run this from the engine repository (dotnet run --project <engine>/src/Kesa.Engine.Cli -- new ...); the template " + TemplatePath + " was not found");
                return Commands.ExitUsage;
            }
            engineRoot = Path.GetFullPath(engineRoot);
            string game = Path.GetFullPath(dir ?? name);
            if (File.Exists(game) || (Directory.Exists(game) && Directory.EnumerateFileSystemEntries(game).Any()))
            {
                stderr.WriteLine("engine new: '" + game + "' already exists and is not empty");
                return Commands.ExitUsage;
            }
            cliFolder ??= Path.GetDirectoryName(typeof(NewGame).Assembly.Location)!;

            string package = Path.GetFullPath(Path.Combine(engineRoot, PackagePath));
            var warnings = new List<string>();
            Dictionary<string, string> tokens;
            if (mode == "copy")
            {
                string? stale = StalePlugins(Path.Combine(package, "Plugins"), cliFolder);
                if (stale != null)
                {
                    stderr.WriteLine("engine new: " + stale + "; run 'dotnet build' at the engine root first");
                    return Commands.ExitUsage;
                }
                tokens = new Dictionary<string, string>
                {
                    ["{{ENGINE}}"] = "Packages/" + PackageName + "/Documentation~",
                    ["{{PACKAGE}}"] = "Packages/" + PackageName,
                    ["{{CLI}}"] = "dotnet Packages/" + PackageName + "/Tools~/cli/engine.dll",
                };
            }
            else
            {
                string engine = Relative(game, engineRoot);
                if (Path.IsPathRooted(engine))
                {
                    stderr.WriteLine("engine new: the engine and '" + game + "' are on different drives; use --engine copy");
                    return Commands.ExitUsage;
                }
                if (!string.Equals(Path.GetDirectoryName(game), Path.GetDirectoryName(engineRoot), StringComparison.OrdinalIgnoreCase))
                    warnings.Add("the game is not next to the engine (" + engine + "); other computers need the same layout");
                tokens = new Dictionary<string, string>
                {
                    ["{{ENGINE}}"] = engine,
                    ["{{PACKAGE}}"] = engine + "/" + PackagePath,
                    ["{{CLI}}"] = "dotnet run --project " + engine + "/src/Kesa.Engine.Cli --",
                };
            }
            tokens["{{GAME}}"] = name;
            tokens["{{MOD_ID}}"] = id;

            try
            {
                CopyTree(Path.Combine(engineRoot, TemplatePath), game, top: true, skip: _ => false);
                Customize(game, name, id, tokens);
                string manifest = Path.Combine(game, "Packages", "manifest.json");
                if (mode == "link")
                {
                    Replace(manifest, TemplateLink, "file:" + Relative(Path.Combine(game, "Packages"), package));
                }
                else
                {
                    // Packages/ 안의 폴더는 Unity가 내장 패키지로 읽으므로 경로 줄을 뺀다
                    WriteText(manifest, string.Join("\n", File.ReadAllLines(manifest).Where(l => !l.Contains(TemplateLink))) + "\n");
                    CopyEngine(engineRoot, package, Path.Combine(game, "Packages", PackageName), cliFolder);
                }
            }
            catch (IOException e)
            {
                stderr.WriteLine("engine new: " + e.Message);
                return Commands.ExitUsage;
            }
            catch (UnauthorizedAccessException e)
            {
                stderr.WriteLine("engine new: " + e.Message);
                return Commands.ExitUsage;
            }

            stdout.WriteLine("created " + game + " (mod '" + id + "', engine " + mode + ")");
            foreach (var w in warnings) stdout.WriteLine("warning: " + w);
            if (git && !GitInit(game)) stdout.WriteLine("warning: 'git init' failed; run it yourself");

            string mods = Path.Combine(game, "Assets", "StreamingAssets", "mods");
            string host = Path.Combine(game, "host", "game.host.json");
            return Commands.Run(new[] { "check", mods, "--host", host }, stdout, stderr);
        }

        private static int UsageError(TextWriter stderr, string message)
        {
            stderr.WriteLine("engine new: " + message);
            stderr.WriteLine(Usage);
            return Commands.ExitUsage;
        }

        private static string? FindEngineRoot()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Kesa.Engine.slnx"))) return d.FullName;
            return null;
        }

        private static string Relative(string from, string to) => Path.GetRelativePath(from, to).Replace('\\', '/');

        /// <summary>패키지의 DLL이 없거나 CLI와 다른 빌드면 그 이유. <c>dotnet run</c>은 CLI만 다시 빌드해서 패키지 쪽이 낡을 수 있다.</summary>
        private static string? StalePlugins(string plugins, string cliFolder)
        {
            foreach (var assembly in SharedAssemblies)
            {
                string mine = Path.Combine(plugins, assembly + ".dll");
                if (!File.Exists(mine)) return "the Unity package has no " + assembly + ".dll";
                string cli = Path.Combine(cliFolder, assembly + ".dll");
                if (File.Exists(cli) && !File.ReadAllBytes(cli).AsSpan().SequenceEqual(File.ReadAllBytes(mine)))
                    return "the Unity package's " + assembly + ".dll is from another build than this CLI";
            }
            return null;
        }

        private static void Customize(string game, string name, string id, Dictionary<string, string> tokens)
        {
            string modsDir = Path.Combine(game, "Assets", "StreamingAssets", "mods");
            if (id != TemplateModId)
            {
                Directory.Move(Path.Combine(modsDir, TemplateModId), Path.Combine(modsDir, id));
                File.Move(Path.Combine(modsDir, TemplateModId + ".meta"), Path.Combine(modsDir, id + ".meta"));
            }
            Replace(Path.Combine(modsDir, id, "mod.py"), "Mod(id=\"" + TemplateModId + "\"", "Mod(id=\"" + id + "\"");
            Replace(Path.Combine(game, "Assets", "Scripts", "GameHost.cs"), "ModId = \"" + TemplateModId + "\"", "ModId = \"" + id + "\"");

            string settings = Path.Combine(game, "ProjectSettings", "ProjectSettings.asset");
            var lines = File.ReadAllLines(settings).Select(l =>
                l.StartsWith("  productName: ", StringComparison.Ordinal) ? "  productName: " + name
                : l.StartsWith("  productGUID: ", StringComparison.Ordinal) ? "  productGUID: " + Guid.NewGuid().ToString("N")
                : l);
            WriteText(settings, string.Join("\n", lines) + "\n");

            foreach (var md in Directory.EnumerateFiles(game, "*.md", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(md);
                foreach (var t in tokens) text = text.Replace(t.Key, t.Value);
                WriteText(md, text);
            }
        }

        private static void Replace(string path, string from, string to)
        {
            string text = File.ReadAllText(path);
            if (!text.Contains(from)) throw new IOException("the template file " + path + " has no '" + from + "'");
            WriteText(path, text.Replace(from, to));
        }

        /// <summary>패키지(.pdb와 패키지의 .gitignore는 빼고), 안내서·예시·데모 호스트(엔진과 같은 상대 위치로), CLI를 넣는다.</summary>
        private static void CopyEngine(string engineRoot, string package, string target, string cliFolder)
        {
            CopyTree(package, target, top: false, skip: f =>
                f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pdb.meta", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(f) == ".gitignore"); // 엔진에서는 Plugins/를 git에서 빼지만 게임은 커밋한다

            string docs = Path.Combine(target, "Documentation~");
            CopyTree(Path.Combine(engineRoot, "docs", "modding"), Path.Combine(docs, "docs", "modding"), top: false, skip: _ => false);
            CopyTree(Path.Combine(engineRoot, "samples"), Path.Combine(docs, "samples"), top: false, skip: f =>
                Path.GetFileName(f) == Commands.StubFileName || Path.GetFileName(f) == Commands.PyrightConfigFileName);
            const string demoHost = "hosts/unity/demo/Assets/Scripts/KesaDemo.cs";
            CopyFile(Path.Combine(engineRoot, demoHost), Path.Combine(docs, demoHost));

            string cli = Path.Combine(target, "Tools~", "cli");
            CopyFile(Path.Combine(cliFolder, "engine.dll"), Path.Combine(cli, "engine.dll"));
            // 공유 DLL은 패키지의 Plugins/에서 찾는다(Program.cs). deps.json이 없어야 그 폴더를 본다
            WriteText(Path.Combine(cli, "engine.runtimeconfig.json"),
                "{\n  \"runtimeOptions\": {\n    \"tfm\": \"net9.0\",\n    \"framework\": {\n      \"name\": \"Microsoft.NETCore.App\",\n      \"version\": \"9.0.0\"\n    },\n    \"rollForward\": \"Major\"\n  }\n}\n");
        }

        private static void CopyTree(string from, string to, bool top, Func<string, bool> skip)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                if (skip(file) || (top && SkippedTop.Contains(Path.GetFileName(file)))) continue;
                CopyFile(file, Path.Combine(to, Path.GetFileName(file)));
            }
            foreach (var sub in Directory.EnumerateDirectories(from))
            {
                string n = Path.GetFileName(sub);
                if (skip(sub) || (top && SkippedTop.Contains(n)) || n == "bin" || n == "obj") continue;
                CopyTree(sub, Path.Combine(to, n), top: false, skip);
            }
        }

        private static void CopyFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to);
        }

        private static void WriteText(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

        private static bool GitInit(string dir)
        {
            try
            {
                var start = new ProcessStartInfo("git", "init -q") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(start);
                if (p == null) return false;
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
