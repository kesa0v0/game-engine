using System.IO;
using System.Text;
using NUnit.Framework;

namespace Kesa.Engine.Cli.Tests
{
    /// <summary><c>engine check</c>의 화면 파일 검사 (G481).</summary>
    public class UiCheckTests
    {
        private const string Defs =
            "Resource(\"Wood\")\n" +
            "def label():\n    return \"x\"\n" +
            "def echo(v):\n    return v\n" +
            "@command(free=True, acts_for=None)\ndef Go():\n    pass\n";

        private string _root = "";
        private StringWriter _out = new StringWriter();
        private StringWriter _err = new StringWriter();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-cli-ui-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(_root);
            _out = new StringWriter();
            _err = new StringWriter();
            Write("core/schema/types.py", "deftype(\"Resource\")\n");
            Write("core/defs/defs.py", Defs);
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
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

        private static string Uxml(string body) => "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n  " + body + "\n</ui:UXML>\n";

        private void Screen(string body) => Write("core/ui/screen.uxml", Uxml(body));

        private string Report(int expectedExit)
        {
            int exit = Commands.Run(new[] { "check", _root }, _out, _err);
            Assert.That(exit, Is.EqualTo(expectedExit), _out + "\n" + _err);
            return _out.ToString();
        }

        [Test]
        public void AGoodScreenPasses()
        {
            Write("core/ui/style.uss", ".a {\n  color: red;\n}\n");
            Write("core/ui/card.uxml", Uxml("<ui:Label params=\"v\" bind-text=\"core.echo(v)\" />"));
            Screen("<ui:VisualElement>\n<ui:Style src=\"ui/style.uss\" />\n<ui:Button bind-text=\"core.label()\" on-click=\"core.Go()\" />\n" +
                   "<ui:Include src=\"ui/card.uxml\" v=\"core.label()\" />\n</ui:VisualElement>");
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void BadXmlIsAnErrorAtItsFile()
        {
            Write("core/ui/screen.uxml", "<ui:UXML>\n<a>\n");
            Assert.That(Report(1), Does.Contain("/core/ui/screen.uxml:").And.Contain("error UIX001: the UXML is not valid XML"));
        }

        [Test]
        public void AnUndeclaredVariableIsAnError()
        {
            Screen("<ui:Label bind-text=\"nobody\" />");
            Assert.That(Report(1), Does.Contain("core/ui/screen.uxml:2:1: error UIX001: bind-text=\"nobody\": the variable 'nobody' is not declared"));
        }

        [Test]
        public void AnUnknownFunctionGetsAHint()
        {
            Screen("<ui:Label bind-text=\"core.labl()\" />");
            string text = Report(1);
            Assert.That(text, Does.Contain("error UIX001: bind-text: mod 'core' has no function 'labl'"));
            Assert.That(text, Does.Contain("label"));
        }

        [Test]
        public void AWrongArgumentCountIsAnError()
        {
            Screen("<ui:Label bind-text=\"core.label(1)\" />");
            Assert.That(Report(1), Does.Contain("error UIX001: bind-text: 'core.label' takes 0 arguments, not 1"));
        }

        [Test]
        public void ACommandUsedAsAValueIsRefused()
        {
            Screen("<ui:Label bind-text=\"core.Go()\" />");
            Assert.That(Report(1), Does.Contain("error UIX001: bind-text: 'core.Go' cannot be called by the host"));
        }

        [Test]
        public void AnUnknownCommandIsAnError()
        {
            Screen("<ui:Button on-click=\"core.Og()\" />");
            Assert.That(Report(1), Does.Contain("error UIX001: on-click: 'core.Og' is neither a command nor a UI action"));
        }

        [Test]
        public void TooManyCommandArgumentsIsAnError()
        {
            Screen("<ui:Button on-click=\"core.Go(1)\" />");
            Assert.That(Report(1), Does.Contain("error UIX001: on-click: 'core.Go' takes 0 argument(s), got 1"));
        }

        [Test]
        public void AMissingIncludeIsAnError()
        {
            Screen("<ui:Include src=\"ui/none.uxml\" />");
            Assert.That(Report(1), Does.Contain("error UIX001: Include 'ui/none.uxml' was not found"));
        }

        [Test]
        public void AnIncludeWithoutItsValueIsAnError()
        {
            Write("core/ui/card.uxml", Uxml("<ui:Label params=\"v\" bind-text=\"v\" />"));
            Screen("<ui:Include src=\"ui/card.uxml\" />");
            Assert.That(Report(1), Does.Contain("core/ui/screen.uxml:2:1: error UIX001: Include 'ui/card.uxml' needs the value 'v'"));
        }

        [Test]
        public void AProblemInAnIncludedFileNamesThatFile()
        {
            Write("core/ui/card.uxml", Uxml("<ui:Label bind-text=\"core.labl()\" />"));
            Screen("<ui:Include src=\"ui/card.uxml\" />");
            Assert.That(Report(1), Does.Contain("core/ui/card.uxml:2:1: error UIX001: bind-text: mod 'core' has no function 'labl'"));
        }

        [Test]
        public void IncludeCyclesAreFoundEvenWithNoOtherFile()
        {
            Write("core/ui/a.uxml", Uxml("<ui:Include src=\"ui/b.uxml\" />"));
            Write("core/ui/b.uxml", Uxml("<ui:Include src=\"ui/a.uxml\" />"));
            Assert.That(Report(1), Does.Contain("error UIX001: Include 'ui/a.uxml' includes itself"));
        }

        [Test]
        public void AnIncludedFileIsSeenInsideTheWindowThatIncludesIt()
        {
            Write("core/ui/title.uxml", Uxml("<ui:Label window-drag=\"true\" />"));
            Screen("<ui:VisualElement window=\"normal\"><ui:Include src=\"ui/title.uxml\" /></ui:VisualElement>");
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void AWindowInsideAWindowAcrossAnIncludeIsAnError()
        {
            Write("core/ui/inner.uxml", Uxml("<ui:VisualElement window=\"normal\" />"));
            Screen("<ui:VisualElement window=\"normal\"><ui:Include src=\"ui/inner.uxml\" /></ui:VisualElement>");
            Assert.That(Report(1), Does.Contain("error UIX001: a window cannot be inside another window"));
        }

        [Test]
        public void TheSameProblemReachedTwiceIsReportedOnce()
        {
            Write("core/ui/card.uxml", Uxml("<ui:Label bind-text=\"core.labl()\" />"));
            Write("core/ui/a.uxml", Uxml("<ui:Include src=\"ui/card.uxml\" />"));
            Write("core/ui/b.uxml", Uxml("<ui:Include src=\"ui/card.uxml\" />"));
            Assert.That(Report(1), Does.Contain("errors: 1, warnings: 0"));
        }

        [Test]
        public void AMissingStyleIsAnError()
        {
            Screen("<ui:Style src=\"ui/none.uss\" />\n<ui:Label />");
            Assert.That(Report(1), Does.Contain("error UIX001: Style 'ui/none.uss' was not found"));
        }

        [Test]
        public void AWrongUssPropertyIsAnErrorEvenIfNoScreenUsesTheFile()
        {
            Write("core/ui/style.uss", ".a {\n  colr: red;\n}\n");
            string text = Report(1);
            Assert.That(text, Does.Contain("core/ui/style.uss:2:1: error UIX001: 'colr' is not a USS property"));
            Assert.That(text, Does.Contain("color"));
        }

        [Test]
        public void ALaterModsFileIsTheOneRead()
        {
            Write("core/ui/screen.uxml", "<broken");
            Write("zed/mod.py", "Mod(id=\"zed\", dependencies=[\"core\"])\n");
            Write("zed/ui/screen.uxml", Uxml("<ui:Label />"));
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void WithADisabledModNamesAreNotCheckedButReadingStillIs()
        {
            Write("broken/mod.py", "Mod(id=\"broken\")\nuse_all(\"defs/\")\n");
            Write("broken/defs/bad.py", "nothing_here()\n");
            Screen("<ui:VisualElement>\n<ui:Label bind-text=\"broken.name()\" />\n<ui:Button on-click=\"broken.Go()\" />\n<ui:Label bind-text=\"nobody\" />\n</ui:VisualElement>");
            string text = Report(1);
            Assert.That(text, Does.Contain("error UIX001: bind-text=\"nobody\""));
            Assert.That(text, Does.Not.Contain("has no function"));
            Assert.That(text, Does.Not.Contain("neither a command"));
        }

        [Test]
        public void TheSamplesScreensPass()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples", "skirmish"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            Assert.That(Directory.GetFiles(Path.Combine(dir!.FullName, "samples", "skirmish"), "*.uxml", SearchOption.AllDirectories), Is.Not.Empty);
            int exit = Commands.Run(new[] { "check", Path.Combine(dir.FullName, "samples", "skirmish") }, _out, _err);
            Assert.That(exit, Is.EqualTo(0), _out.ToString());
            Assert.That(_out.ToString(), Does.Contain("errors: 0, warnings: 0"));
        }
    }
}
