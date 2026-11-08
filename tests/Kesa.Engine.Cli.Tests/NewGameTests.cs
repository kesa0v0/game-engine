using System;
using System.IO;
using System.Linq;
using Kesa.Engine.Host;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Kesa.Engine.Cli.Tests
{
    /// <summary><c>engine new</c>와 템플릿 <c>hosts/unity/template</c>.</summary>
    public class NewGameTests
    {
        private static readonly string Engine = FindEngine();
        private string _root = "";
        private StringWriter _out = new StringWriter();
        private StringWriter _err = new StringWriter();

        private static string FindEngine()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Kesa.Engine.slnx"))) return d.FullName;
            throw new InvalidOperationException("no engine root above " + AppContext.BaseDirectory);
        }

        private static string Template => Path.Combine(Engine, "hosts", "unity", "template");

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-new-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(_root);
            _out = new StringWriter();
            _err = new StringWriter();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        /// <summary>
        /// 실제 엔진의 템플릿·안내서·예시와, 테스트 폴더의 DLL을 Plugins로 둔 가짜 엔진. 진짜 Plugins는 이 테스트 빌드와 다를 수 있다.
        /// </summary>
        private string FakeEngine()
        {
            string engine = Path.Combine(_root, "engine");
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(engine).FullName, "Kesa.Engine.slnx"), "");
            Copy(Template, Path.Combine(engine, "hosts", "unity", "template"));
            Copy(Path.Combine(Engine, "docs", "modding"), Path.Combine(engine, "docs", "modding"));
            Copy(Path.Combine(Engine, "samples"), Path.Combine(engine, "samples"));
            const string demo = "hosts/unity/demo/Assets/Scripts/KesaDemo.cs";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(engine, demo))!);
            File.Copy(Path.Combine(Engine, demo), Path.Combine(engine, demo));
            string package = Path.Combine(engine, "hosts", "unity", "uk.kesa.engine.unity");
            foreach (var f in new[] { "package.json", "README.md" }) Copy(Path.Combine(Engine, "hosts", "unity", "uk.kesa.engine.unity", f), Path.Combine(package, f));
            File.WriteAllText(Path.Combine(package, ".gitignore"), "Plugins/\n");
            string plugins = Directory.CreateDirectory(Path.Combine(package, "Plugins")).FullName;
            foreach (var dll in new[] { "Kesa.Engine.Kernel", "Kesa.Engine.Ui", "Kesa.Engine.Text", "Kesa.Engine.Bridge" })
            {
                File.Copy(Path.Combine(AppContext.BaseDirectory, dll + ".dll"), Path.Combine(plugins, dll + ".dll"));
                File.WriteAllText(Path.Combine(plugins, dll + ".pdb"), "");
            }
            return engine;
        }

        private static void Copy(string from, string to)
        {
            if (File.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
                return;
            }
            Directory.CreateDirectory(to);
            foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.EnumerateDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
        }

        private int New(string engine, params string[] args) =>
            NewGame.Run(args.Concat(new[] { "--no-git" }).ToArray(), _out, _err, engine, AppContext.BaseDirectory);

        private static string Read(string game, string relative) => File.ReadAllText(Path.Combine(game, relative.Replace('/', Path.DirectorySeparatorChar)));

        [Test]
        public void TheTemplateModsPassCheckWithoutWarnings()
        {
            string mods = Path.Combine(Template, "Assets", "StreamingAssets", "mods");
            string host = Path.Combine(Template, "host", "game.host.json");
            int code = Commands.Run(new[] { "check", mods, "--host", host, Commands.WarningsAsErrors }, _out, _err);
            Assert.That(code, Is.EqualTo(Commands.ExitOk), _out + "\n" + _err);
            StringAssert.Contains("errors: 0, warnings: 0", _out.ToString());
        }

        [Test]
        public void TheTemplateHostDeclarationsMatchAnEmptyRegistry()
        {
            string Norm(string t) => t.Replace("\r\n", "\n").Trim();
            Assert.That(Norm(File.ReadAllText(Path.Combine(Template, "host", "game.host.json"))), Is.EqualTo(Norm(new HostFunctions().ExportDeclarations())));
        }

        [Test]
        public void CopyPutsTheEngineInsideTheGame()
        {
            string engine = FakeEngine();
            string game = Path.Combine(_root, "games", "Weimar");
            Assert.That(New(engine, "Weimar", "--dir", game), Is.EqualTo(Commands.ExitOk), _out + "\n" + _err);

            StringAssert.DoesNotContain("uk.kesa.engine.unity", Read(game, "Packages/manifest.json"));
            string package = Path.Combine(game, "Packages", "uk.kesa.engine.unity");
            Assert.That(File.Exists(Path.Combine(package, "Plugins", "Kesa.Engine.Kernel.dll")));
            Assert.That(Directory.EnumerateFiles(package, "*.pdb", SearchOption.AllDirectories), Is.Empty);
            Assert.That(File.Exists(Path.Combine(package, ".gitignore")), Is.False, "the engine's .gitignore leaves Plugins/ out of git");
            Assert.That(File.Exists(Path.Combine(package, "Tools~", "cli", "engine.dll")));
            Assert.That(File.Exists(Path.Combine(package, "Documentation~", "docs", "modding", "README.md")));
            Assert.That(File.Exists(Path.Combine(package, "Documentation~", "samples", "guide", "harbor", "mod.py")));
            Assert.That(File.Exists(Path.Combine(package, "Documentation~", "hosts", "unity", "demo", "Assets", "Scripts", "KesaDemo.cs")));

            string claude = Read(game, "CLAUDE.md");
            StringAssert.StartsWith("# Weimar\n", claude.Replace("\r\n", "\n"));
            StringAssert.Contains("`Packages/uk.kesa.engine.unity/Documentation~/docs/modding/README.md`", claude);
            StringAssert.Contains("dotnet Packages/uk.kesa.engine.unity/Tools~/cli/engine.dll check", claude);
            StringAssert.Contains("Mod(id=\"weimar\"", Read(game, "Assets/StreamingAssets/mods/weimar/mod.py"));
            Assert.That(File.Exists(Path.Combine(game, "Assets", "StreamingAssets", "mods", "weimar.meta")));
            StringAssert.Contains("ModId = \"weimar\"", Read(game, "Assets/Scripts/GameHost.cs"));
            string settings = Read(game, "ProjectSettings/ProjectSettings.asset");
            StringAssert.Contains("productName: Weimar\n", settings);
            StringAssert.DoesNotContain(Read(Template, "ProjectSettings/ProjectSettings.asset").Split('\n').First(l => l.Contains("productGUID")).Trim(), settings);
            Assert.That(Directory.EnumerateFiles(game, "*.md", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains("{{")), Is.Empty);
            Assert.That(File.Exists(Path.Combine(game, "README.md")), Is.False, "the template README stays in the engine");
            StringAssert.Contains("*.dll filter=lfs", Read(game, ".gitattributes"));
            StringAssert.Contains("errors: 0, warnings: 0", _out.ToString());
        }

        [Test]
        public void LinkWritesRelativePathsFromBothBases()
        {
            string engine = FakeEngine();
            string game = Path.Combine(_root, "Coup");
            Assert.That(New(engine, "Coup", "--dir", game, "--engine", "link"), Is.EqualTo(Commands.ExitOk), _out + "\n" + _err);

            StringAssert.Contains("\"uk.kesa.engine.unity\": \"file:../../engine/hosts/unity/uk.kesa.engine.unity\"", Read(game, "Packages/manifest.json"));
            Assert.That(Directory.Exists(Path.Combine(game, "Packages", "uk.kesa.engine.unity")), Is.False);
            string claude = Read(game, "CLAUDE.md");
            StringAssert.Contains("`../engine/docs/modding/README.md`", claude);
            StringAssert.Contains("dotnet run --project ../engine/src/Kesa.Engine.Cli -- check", claude);
            StringAssert.Contains("`../engine/hosts/unity/uk.kesa.engine.unity/README.md`", claude);
            StringAssert.DoesNotContain("warning:", _out.ToString());
        }

        [Test]
        public void LinkWarnsWhenTheGameIsNotNextToTheEngine()
        {
            string engine = FakeEngine();
            string game = Path.Combine(_root, "deeper", "Coup");
            Assert.That(New(engine, "Coup", "--dir", game, "--engine", "link"), Is.EqualTo(Commands.ExitOk), _err.ToString());
            StringAssert.Contains("file:../../../engine/hosts/unity/uk.kesa.engine.unity", Read(game, "Packages/manifest.json"));
            StringAssert.Contains("warning: the game is not next to the engine (../../engine)", _out.ToString());
        }

        [Test]
        public void CopyRefusesPluginsFromAnotherBuild()
        {
            string engine = FakeEngine();
            File.WriteAllText(Path.Combine(engine, "hosts", "unity", "uk.kesa.engine.unity", "Plugins", "Kesa.Engine.Kernel.dll"), "old");
            Assert.That(New(engine, "Weimar", "--dir", Path.Combine(_root, "Weimar")), Is.EqualTo(Commands.ExitUsage));
            StringAssert.Contains("Kesa.Engine.Kernel.dll is from another build", _err.ToString());
            Assert.That(Directory.Exists(Path.Combine(_root, "Weimar")), Is.False);
        }

        [TestCase("Weimar Republic", "is not a mod id")]
        [TestCase("Engine", "is not a mod id")]
        public void ANameThatIsNotAModIdNeedsAnId(string name, string message)
        {
            Assert.That(New(FakeEngine(), name, "--dir", Path.Combine(_root, "g")), Is.EqualTo(Commands.ExitUsage));
            StringAssert.Contains(message, _err.ToString());
        }

        [Test]
        public void AnIdCanBeGiven()
        {
            string game = Path.Combine(_root, "g");
            Assert.That(New(FakeEngine(), "Weimar Republic", "--id", "weimar", "--dir", game), Is.EqualTo(Commands.ExitOk), _err.ToString());
            Assert.That(File.Exists(Path.Combine(game, "Assets", "StreamingAssets", "mods", "weimar", "mod.py")));
            StringAssert.StartsWith("# Weimar Republic", Read(game, "CLAUDE.md"));
        }

        [Test]
        public void ANonEmptyFolderIsRefused()
        {
            string game = Path.Combine(_root, "g");
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "x.txt"), "");
            Assert.That(New(FakeEngine(), "Weimar", "--dir", game), Is.EqualTo(Commands.ExitUsage));
            StringAssert.Contains("already exists and is not empty", _err.ToString());
        }
    }
}
