using System.IO;
using System.Text;
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
            Assert.That(_out.ToString(), Does.Contain("\"Wood\""));
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
