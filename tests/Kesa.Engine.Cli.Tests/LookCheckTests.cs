using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Kesa.Engine.Cli.Tests
{
    /// <summary><c>engine check</c>의 그림·공간·보는 법 파일 검사 (G480).</summary>
    public class LookCheckTests
    {
        private string _root = "";
        private StringWriter _out = new StringWriter();
        private StringWriter _err = new StringWriter();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-cli-look-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(_root);
            _out = new StringWriter();
            _err = new StringWriter();
            Write("core/schema/types.py", "deftype(\"Resource\")\n");
            Write("core/defs/defs.py", "Resource(\"Wood\")\n");
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

        private void Look(string body) => Write("core/looks/a.look.xml", "<Looks>\n  <Look name=\"a\">\n    " + body + "\n  </Look>\n</Looks>\n");

        private int Check(params string[] options)
        {
            var args = new string[options.Length + 2];
            args[0] = "check";
            args[1] = _root;
            options.CopyTo(args, 2);
            return Commands.Run(args, _out, _err);
        }

        private string Report(int expectedExit, params string[] options)
        {
            Assert.That(Check(options), Is.EqualTo(expectedExit), _out + "\n" + _err);
            return _out.ToString();
        }

        [Test]
        public void GoodFilesPass()
        {
            Write("core/art/white.png", "");
            Look("<Sprite src=\"art/white.png\" tint=\"#4080ff80\" />");
            Write("core/looks/a.space.xml", "<Spaces>\n  <Space name=\"square\" kind=\"grid\" cell=\"1\" />\n</Spaces>\n");
            Write("core/looks/a.view.xml", "<Views>\n  <View name=\"main\" rig=\"top\" space=\"square\" look-at=\"2,1\" size=\"4\" />\n</Views>\n");
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void AWrongAttributeIsAnErrorAtItsLine()
        {
            Write("core/art/white.png", "");
            Look("<Sprite src=\"art/white.png\" tnit=\"#fff\" />");
            string text = Report(1);
            Assert.That(text, Does.Contain("/core/looks/a.look.xml:3:1: error LOK001: tnit: <Sprite> has no attribute 'tnit'"));
            Assert.That(text, Does.Contain("tint"), "the did-you-mean hint");
        }

        [Test]
        public void BadXmlIsAnError()
        {
            Write("core/looks/a.space.xml", "<Spaces>\n  <Space\n");
            Assert.That(Report(1), Does.Contain("error LOK001: not valid XML"));
        }

        [Test]
        public void AMissingFileIsAnError()
        {
            Look("<Sprite src=\"art/none.png\" />");
            Assert.That(Report(1), Does.Contain("error LOK001: src=\"art/none.png\": no such file in the mods"));
        }

        [Test]
        public void AFileInAnotherLoadedModIsFound()
        {
            Write("other/art/white.png", "");
            Write("other/mod.py", "Mod(id=\"other\")\n");
            Look("<Sprite src=\"art/white.png\" />");
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void AnUnknownSpaceKindWarnsAndItsViewStillFindsTheSpace()
        {
            Write("core/looks/a.space.xml", "<Spaces>\n  <Space name=\"field\" kind=\"continuous\" radius=\"3\" />\n</Spaces>\n");
            Write("core/looks/a.view.xml", "<Views>\n  <View name=\"main\" rig=\"top\" space=\"field\" look-at=\"0,0\" size=\"4\" />\n</Views>\n");
            string text = Report(0);
            Assert.That(text, Does.Contain("warning LOK002: kind=\"continuous\": not a standard space kind"));
            Assert.That(text, Does.Contain("errors: 0, warnings: 1"));
        }

        [Test]
        public void AViewOfASpaceNobodyDeclaredIsStillAnError()
        {
            Write("core/looks/a.view.xml", "<Views>\n  <View name=\"main\" rig=\"top\" space=\"field\" look-at=\"0,0\" size=\"4\" />\n</Views>\n");
            Assert.That(Report(1), Does.Contain("error LOK001: space=\"field\": no space is named 'field'"));
        }

        [Test]
        public void ATypoInAKindGetsAHint()
        {
            Write("core/looks/a.space.xml", "<Spaces>\n  <Space name=\"field\" kind=\"gird\" />\n</Spaces>\n");
            string text = Report(0);
            Assert.That(text, Does.Contain("warning LOK002: kind=\"gird\""));
            Assert.That(text, Does.Contain("grid"));
        }

        [Test]
        public void WarningsAsErrorsFailsOnAnUnknownKind()
        {
            Write("core/looks/a.space.xml", "<Spaces>\n  <Space name=\"field\" kind=\"continuous\" />\n</Spaces>\n");
            Assert.That(Report(1, Commands.WarningsAsErrors), Does.Contain("error LOK002"));
        }

        [Test]
        public void AnUnknownRigWarns()
        {
            Write("core/looks/a.space.xml", "<Spaces>\n  <Space name=\"square\" kind=\"grid\" cell=\"1\" />\n</Spaces>\n");
            Write("core/looks/a.view.xml", "<Views>\n  <View name=\"main\" rig=\"orbit\" space=\"square\" look-at=\"0,0\" />\n</Views>\n");
            Assert.That(Report(0), Does.Contain("warning LOK002: rig=\"orbit\": not a standard camera rig"));
        }

        [Test]
        public void AnUnknownElementWarnsAndDoesNotBlameWhatIsInsideIt()
        {
            Write("core/art/white.png", "");
            Look("<Banner width=\"3\"><State name=\"idle\" clip=\"idle\" /><Sprite src=\"art/white.png\" /></Banner>");
            string text = Report(0);
            Assert.That(text, Does.Contain("warning LOK002: <Banner>: not a standard look element"));
            Assert.That(text, Does.Contain("errors: 0, warnings: 1"));
        }

        [Test]
        public void AWrongStandardElementInsideAnUnknownOneIsStillAnError()
        {
            Look("<Banner><Sprite src=\"art/none.png\" /></Banner>");
            Assert.That(Report(1), Does.Contain("error LOK001: src=\"art/none.png\""));
        }

        [Test]
        public void AnUnknownElementInsideAModelIsNotBlamed()
        {
            Write("core/models/s.glb", "");
            Look("<Model src=\"models/s.glb\"><Halo /></Model>");
            string text = Report(0);
            Assert.That(text, Does.Contain("warning LOK002: <Halo>"));
            Assert.That(text, Does.Contain("errors: 0, warnings: 1"));
        }

        [Test]
        public void AModelFormatOutsideTheStandardLoaderWarns()
        {
            Write("core/models/s.fbx", "");
            Look("<Model src=\"models/s.fbx\" />");
            Assert.That(Report(0), Does.Contain("warning LOK002: src=\"models/s.fbx\": no model loader takes '.fbx'"));
        }

        [Test]
        public void TheStandardModelFormatIsQuiet()
        {
            Write("core/models/s.glb", "");
            Look("<Model src=\"models/s.glb\" />");
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void PathsNameTheFolderWhenItDiffersFromTheModId()
        {
            Write("tanks/mod.py", "Mod(id=\"armor\")\n");
            Write("tanks/looks/a.space.xml", "<Spaces>\n  <Space name=\"x\" />\n</Spaces>\n");
            Assert.That(Check("--json"), Is.EqualTo(1), _out.ToString());
            string json = _out.ToString();
            Assert.That(json, Does.Contain("\"code\": \"LOK001\""));
            Assert.That(json, Does.Contain("\"mod\": \"armor\""));
            Assert.That(json, Does.Contain("tanks/looks/a.space.xml"));
        }

        [Test]
        public void InputAndScreenFilesAreNotRead()
        {
            Write("core/keys.input.xml", "<not closed");
            Write("core/ui/screen.uxml", "<not closed");
            Assert.That(Report(0), Does.Contain("errors: 0, warnings: 0"));
        }

        [Test]
        public void TheSamplesPass()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "samples", "skirmish"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            int exit = Commands.Run(new[] { "check", Path.Combine(dir!.FullName, "samples", "skirmish") }, _out, _err);
            Assert.That(exit, Is.EqualTo(0), _out.ToString());
            Assert.That(_out.ToString(), Does.Contain("errors: 0, warnings: 0"));
        }
    }
}
