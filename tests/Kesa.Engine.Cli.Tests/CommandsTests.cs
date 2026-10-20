using System.IO;
using System.Text;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Cli.Tests
{
    /// <summary>CLI 명령의 종료 코드(spec-gaps G78), <c>check</c> 출력 형식(G79), <c>dump</c>(G80).</summary>
    public class CommandsTests
    {
        private const string Schema = "deftype(\"Resource\")\n";

        private string _root = "";
        private StringWriter _out = new StringWriter();
        private StringWriter _err = new StringWriter();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-cli-tests", TestContext.CurrentContext.Test.ID);
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

        private void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private void Mod(string defs)
        {
            Write("core/schema/types.py", Schema);
            Write("core/defs/defs.py", defs);
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
        }

        private int Run(params string[] args) => Commands.Run(Prepend("check", args), _out, _err);

        private int Dump(params string[] args) => Commands.Run(Prepend("dump", args), _out, _err);

        private int Stubs(params string[] args) => Commands.Run(Prepend("stubs", args), _out, _err);

        private string Read(string relative) =>
            File.ReadAllText(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)), Encoding.UTF8);

        private static string[] Prepend(string command, string[] args)
        {
            var all = new string[args.Length + 1];
            all[0] = command;
            args.CopyTo(all, 1);
            return all;
        }

        [Test]
        public void ACleanModExitsZeroWithTheSummary()
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Run(_root), Is.EqualTo(0), _err.ToString());
            Assert.That(_out.ToString(), Is.EqualTo("errors: 0, warnings: 0, info: 0; mods loaded: 1, disabled: 0\n"));
            Assert.That(_err.ToString(), Is.Empty);
        }

        // check는 번역 파일도 읽고 검사한다. 번역 오류는 모드를 끄지 않지만 오류라 종료 코드는 1이다 (G257)
        [Test]
        public void CheckAlsoChecksTranslations()
        {
            Mod("Resource(\"Wood\")\ndef greet():\n    return text(\"core-hello\")\n");
            Write("core/locale/en/core.ftl", "core-hi = { JOSA($x, \"에게\") }\n");
            Assert.That(Run(_root), Is.EqualTo(1), _err.ToString());
            string text = _out.ToString();
            Assert.That(text, Does.Contain("core/mod.py:1:1: error LOC005: mod 'core' has locale/ but no defaultLanguage"));
            Assert.That(text, Does.Contain("core/locale/en/core.ftl:1:1: error LOC006:"));
            Assert.That(text, Does.Contain("core/defs/defs.py:3:17: warning LOC008: text(\"core-hello\") has no translation in any language"));
            Assert.That(text, Does.EndWith("errors: 2, warnings: 1, info: 0; mods loaded: 1, disabled: 0\n"));
        }

        [Test]
        public void ContentErrorsExitOneAndPrintPathsUnderTheGivenFolder()
        {
            Mod("Resource(\"Wood\")\nResource(\"Wood\")\n");
            Assert.That(Run(_root + Path.DirectorySeparatorChar), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.StartWith(_root + "/core/defs/defs.py:2:"));
            Assert.That(_err.ToString(), Is.Empty);
        }

        [Test]
        public void JsonGoesToStdoutAloneAndMatchesTheExitCode()
        {
            Mod("Resource(\"Wood\")\nResource(\"Wood\")\n");
            Assert.That(Run("--json", _root), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.StartWith("{\n  \"version\": 1,\n  \"ok\": false,"));
            Assert.That(_err.ToString(), Is.Empty);
        }

        [Test]
        public void AMissingFolderIsAUsageError()
        {
            Assert.That(Run(Path.Combine(_root, "nope")), Is.EqualTo(2));
            Assert.That(_out.ToString(), Is.Empty);
            Assert.That(_err.ToString(), Does.Contain("does not exist"));
        }

        [TestCase]
        [TestCase("--yaml")]
        public void BadArgumentsAreUsageErrors(params string[] extra)
        {
            Assert.That(Run(extra), Is.EqualTo(2));
            Assert.That(_out.ToString(), Is.Empty);
            Assert.That(_err.ToString(), Does.Contain(Commands.Usage));
        }

        [TestCase]
        [TestCase("frobnicate")]
        public void AnUnknownCommandIsAUsageError(params string[] args)
        {
            Assert.That(Commands.Run(args, _out, _err), Is.EqualTo(2));
            Assert.That(_err.ToString(), Does.Contain(Commands.Usage));
        }

        [Test]
        public void DumpWritesTheDefDumpToStdoutAndTheSummaryToStderr()
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Dump(_root), Is.EqualTo(0), _err.ToString());
            Assert.That(_out.ToString(), Does.StartWith("{\n  \"version\": 1,"));
            Assert.That(_out.ToString(), Does.Contain("\"core:Wood\""));
            Assert.That(_err.ToString(), Is.EqualTo("errors: 0, warnings: 0, info: 0; mods loaded: 1, disabled: 0\n"));
        }

        [Test]
        public void DumpStillDumpsWhenThereAreErrorsAndExitsOne()
        {
            Mod("Resource(\"Wood\")\nResource(\"Wood\")\n");
            Assert.That(Dump(_root), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.StartWith("{\n  \"version\": 1,"));
            Assert.That(_err.ToString(), Does.StartWith(_root + "/core/defs/defs.py:2:"));
        }

        [Test]
        public void DumpTakesNoJsonOption()
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Dump("--json", _root), Is.EqualTo(2));
            Assert.That(_out.ToString(), Is.Empty);
        }

        [Test]
        public void TwoFoldersAreAUsageError()
        {
            Assert.That(Run(_root, _root), Is.EqualTo(2));
            Assert.That(_err.ToString(), Does.Contain("more than one"));
        }

        // --warnings-as-errors (G84)

        [Test]
        public void AWarningAloneExitsZero()
        {
            Mod("Resource(\"Wood\")\n");
            Directory.CreateDirectory(Path.Combine(_root, "notes"));
            Assert.That(Run(_root), Is.EqualTo(0), _out.ToString());
            Assert.That(_out.ToString(), Does.Contain(": warning MOD"));
        }

        [Test]
        public void WarningsAsErrorsPromotesTheWarningEverywhere()
        {
            Mod("Resource(\"Wood\")\n");
            Directory.CreateDirectory(Path.Combine(_root, "notes"));
            Assert.That(Run("--warnings-as-errors", "--json", _root), Is.EqualTo(1));
            string json = _out.ToString();
            Assert.That(json, Does.Contain("\"ok\": false"));
            Assert.That(json, Does.Contain("\"counts\": {\"error\": 1, \"warning\": 0, \"info\": 0}"));
            Assert.That(json, Does.Contain("\"severity\": \"error\""));
            Assert.That(json, Does.Not.Contain("\"severity\": \"warning\""));
        }

        [Test]
        public void WarningsAsErrorsLeavesInfoAndErrorsAlone()
        {
            var span = new SourceSpan("core/defs/defs.py", 1, 1);
            var input = new[]
            {
                new Diagnostic("A001", Severity.Error, "e", "core", span),
                new Diagnostic("A002", Severity.Warning, "w", "core", span, "h",
                    new[] { span }),
                new Diagnostic("A003", Severity.Info, "i", "core", span),
            };
            var output = Commands.PromoteWarnings(input);
            Assert.That(output[0], Is.SameAs(input[0]));
            Assert.That(output[1].Severity, Is.EqualTo(Severity.Error));
            Assert.That(output[1].Code, Is.EqualTo("A002"));
            Assert.That(output[1].Hint, Is.EqualTo("h"));
            Assert.That(output[1].Related, Is.EqualTo(new[] { span }));
            Assert.That(output[2], Is.SameAs(input[2]));
        }

        [TestCase("dump")]
        [TestCase("stubs")]
        public void OnlyCheckTakesWarningsAsErrors(string command)
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Commands.Run(new[] { command, "--warnings-as-errors", _root }, _out, _err), Is.EqualTo(2));
        }

        // stubs (G81)

        [Test]
        public void StubsWritesABuiltinsStubPerModAndTheConfig()
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Stubs(_root), Is.EqualTo(0), _err.ToString());
            Assert.That(_out.ToString(), Is.EqualTo(
                "wrote " + _root + "/core/__builtins__.pyi\n" +
                "wrote " + _root + "/core/schema/__builtins__.pyi\n" +
                "wrote " + _root + "/pyrightconfig.json\n"));
            Assert.That(Read("core/__builtins__.pyi"), Does.StartWith("# generated by the engine for mod 'core'."));
            Assert.That(Read("core/__builtins__.pyi"), Does.Contain("class Resource:"));
            // 스키마 스텁은 모드와 무관한 부분에 그 모드가 보는 클래스를 더한 것 (G209)
            Assert.That(Read("core/schema/__builtins__.pyi"), Does.StartWith(Kesa.Engine.Stubs.StubGenerator.GenerateSchema()));
            Assert.That(Read("core/schema/__builtins__.pyi"), Does.Contain("\nclass Resource:"));
            Assert.That(Read("pyrightconfig.json"), Is.EqualTo(Commands.PyrightConfig));
            Assert.That(_err.ToString(), Is.EqualTo("errors: 0, warnings: 0, info: 0; mods loaded: 1, disabled: 0\n"));
        }

        [Test]
        public void StubsOverwritesTheStubButKeepsAnExistingConfig()
        {
            Mod("Resource(\"Wood\")\n");
            Write("core/__builtins__.pyi", "old");
            Write("pyrightconfig.json", "{\"mine\": true}\n");
            Assert.That(Stubs(_root), Is.EqualTo(0), _err.ToString());
            Assert.That(_out.ToString(), Is.EqualTo(
                "wrote " + _root + "/core/__builtins__.pyi\n" +
                "wrote " + _root + "/core/schema/__builtins__.pyi\n"));
            Assert.That(Read("core/__builtins__.pyi"), Does.Contain("class Resource:"));
            Assert.That(Read("pyrightconfig.json"), Is.EqualTo("{\"mine\": true}\n"));
        }

        [Test]
        public void StubsKeepsTheOldStubOfAModThatFailedInItsDefFilesAndExitsOne()
        {
            Mod("Resource(\"Wood\")\nResource(\"Wood\")\n");
            Write("core/__builtins__.pyi", "old");
            Assert.That(Stubs(_root), Is.EqualTo(1));
            Assert.That(Read("core/__builtins__.pyi"), Is.EqualTo("old"));
            Assert.That(_out.ToString(), Does.Contain("kept " + _root + "/core/__builtins__.pyi (the mod failed to load)\n"));
            Assert.That(Read("core/schema/__builtins__.pyi"), Is.EqualTo(Kesa.Engine.Stubs.StubGenerator.GenerateSchema()));
            Assert.That(_err.ToString(), Does.Contain("disabled: 1 (core)"));
        }

        [Test]
        public void StubsWritesManifestNamesForAFailedModWithoutAStub()
        {
            Mod("Resource(\"Wood\")\nResource(\"Wood\")\n");
            Assert.That(Stubs(_root), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.Contain(
                "wrote " + _root + "/core/__builtins__.pyi (mod.py names only; the mod failed to load)\n"));
            Assert.That(Read("core/__builtins__.pyi"), Is.EqualTo(Kesa.Engine.Stubs.StubGenerator.GenerateManifestOnly("core")));
        }

        [Test]
        public void StubsWritesAFirstRoundStubForAModDisabledByALinkError()
        {
            Write("core/schema/types.py", "deftype(\"Resource\")\ndeftype(\"Unit\", cost=const(dict[DefId[\"Resource\"], int]))\n");
            Write("core/defs/defs.py", "Resource(\"Wood\")\nUnit(\"Scout\", cost={\"Wod\": 1})\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            Write("core/__builtins__.pyi", "old");
            Assert.That(Stubs(_root), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.StartWith("wrote " + _root + "/core/__builtins__.pyi\n"));
            Assert.That(Read("core/__builtins__.pyi"), Does.Contain("class Unit:"));
            Assert.That(Read("core/__builtins__.pyi"), Does.Contain("failed to load"));
        }

        [Test]
        public void AModWithoutASchemaFolderGetsNoSchemaStub()
        {
            Mod("Resource(\"Wood\")\n");
            Write("extra/defs/defs.py", "Resource(\"Stone\")\n");
            Write("extra/mod.py", "Mod(id=\"extra\", dependencies=[\"core\"])\nuse_all(\"defs/\")\n");
            Assert.That(Stubs(_root), Is.EqualTo(0), _err.ToString());
            Assert.That(_out.ToString(), Does.Contain("/extra/__builtins__.pyi\n"));
            Assert.That(_out.ToString(), Does.Not.Contain("/extra/schema/"));
            Assert.That(Directory.Exists(Path.Combine(_root, "extra", "schema")), Is.False);
        }

        [Test]
        public void TheGeneratedStubDoesNotChangeTheNextCheck()
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Stubs(_root), Is.EqualTo(0), _err.ToString());
            _out = new StringWriter();
            Assert.That(Run(_root), Is.EqualTo(0), _out.ToString());
            Assert.That(_out.ToString(), Is.EqualTo("errors: 0, warnings: 0, info: 0; mods loaded: 1, disabled: 0\n"));
        }

        // --host (G221)

        private const string UsesHost = "Resource(\"Wood\")\ndef count(r):\n    return core.stock.count(r)\n";

        /// <summary>호스트가 내보낸 선언 파일을 모드 폴더에 쓰고 경로를 준다.</summary>
        private string HostFile(string type = "DefId[Resource]")
        {
            var host = new Kesa.Engine.Host.HostFunctions();
            if (type.StartsWith("Ref"))
                host.For("core").Pure("def stock.count(resource: " + type + ") -> int", "창고의 개수.",
                    (Kesa.Engine.Runtime.EntityId resource) => 3L);
            else
                host.For("core").Pure("def stock.count(resource: " + type + ") -> int", "창고의 개수.",
                    (string resource) => 3L);
            Write("host.json", host.ExportDeclarations());
            return Path.Combine(_root, "host.json");
        }

        [Test]
        public void CheckKnowsTheHostFunctionsOnlyWithTheHostFile()
        {
            Mod(UsesHost);
            string file = HostFile();
            Assert.That(Run(_root), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.Contain("core"));
            _out = new StringWriter();
            Assert.That(Run(_root, "--host", file), Is.EqualTo(0), _out + _err.ToString());
            Assert.That(_out.ToString(), Is.EqualTo("errors: 0, warnings: 0, info: 0; mods loaded: 1, disabled: 0\n"));
        }

        [Test]
        public void StubsWithTheHostFileDeclareTheFunctions()
        {
            Mod(UsesHost);
            Assert.That(Stubs("--host", HostFile(), _root), Is.EqualTo(0), _err.ToString());
            string expected =
                "class core:\n" +
                "    class stock:\n" +
                "        @staticmethod\n" +
                "        def count(resource: Literal[\"Wood\", \"core:Wood\"]) -> int:\n" +
                "            \"\"\"창고의 개수.\"\"\"\n" +
                "            ...\n";
            Assert.That(Read("core/__builtins__.pyi"), Does.Contain(expected));
            Assert.That(Read("core/schema/__builtins__.pyi"), Does.Contain(expected));
        }

        [Test]
        public void DumpTakesTheHostFileToo()
        {
            Mod(UsesHost);
            Assert.That(Dump(_root, "--host", HostFile()), Is.EqualTo(0), _err.ToString());
        }

        [TestCase("{", "host.json': the host declarations file is not valid JSON (line 1)")]
        [TestCase("{}", "host.json': this is not a host declarations file")]
        public void ABrokenHostFileIsAUsageError(string text, string expected)
        {
            Mod("Resource(\"Wood\")\n");
            Write("host.json", text);
            Assert.That(Run(_root, "--host", Path.Combine(_root, "host.json")), Is.EqualTo(2));
            Assert.That(_out.ToString(), Is.Empty);
            Assert.That(_err.ToString(), Does.StartWith("engine check: '").And.Contain(expected));
        }

        [Test]
        public void AMissingHostFileIsAUsageError()
        {
            Mod("Resource(\"Wood\")\n");
            Assert.That(Run(_root, "--host", Path.Combine(_root, "nope.json")), Is.EqualTo(2));
            Assert.That(_err.ToString(), Does.StartWith("engine check: cannot read host declarations file '"));
        }

        [Test]
        public void HostNeedsExactlyOneFile()
        {
            Mod("Resource(\"Wood\")\n");
            string file = HostFile();
            Assert.That(Run(_root, "--host"), Is.EqualTo(2));
            Assert.That(_err.ToString(), Does.StartWith("engine check: --host needs a file"));
            _err = new StringWriter();
            Assert.That(Run(_root, "--host", file, "--host", file), Is.EqualTo(2));
            Assert.That(_err.ToString(), Does.StartWith("engine check: more than one --host file given"));
        }

        [Test]
        public void AHostSignatureErrorIsNotPlacedUnderTheModsFolder()
        {
            // <host ...>는 모드 폴더의 파일이 아니다
            Mod(UsesHost);
            Assert.That(Run(_root, "--host", HostFile("Ref[Resource]")), Is.EqualTo(1));
            Assert.That(_out.ToString(), Does.StartWith("<host core.stock.count>:1:30: error HST001: "));
        }

        [TestCase("mods", "mods/")]
        [TestCase("mods/", "mods/")]
        [TestCase("mods\\", "mods/")]
        [TestCase("/", "/")]
        public void ThePrefixEndsWithASlash(string root, string expected)
        {
            Assert.That(Commands.PathPrefix(root), Is.EqualTo(expected));
        }
    }
}
