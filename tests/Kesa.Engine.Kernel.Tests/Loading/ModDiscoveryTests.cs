using System.IO;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    public class ModDiscoveryTests
    {
        private string _root = "";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text, bool bom = false)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(bom));
        }

        [Test]
        public void DiscoversModsInOrdinalFolderOrder()
        {
            Write("zeta/mod.py", "Mod(id=\"zeta\")\n");
            Write("alpha/mod.py", "Mod(id=\"alpha\")\n");
            var bag = new DiagnosticBag();
            var mods = ModDiscovery.Discover(_root, bag).Mods;
            Assert.That(bag.Count, Is.Zero);
            Assert.That(mods.Select(m => m.Id), Is.EqualTo(new[] { "alpha", "zeta" }));
        }

        [Test]
        public void CrlfAndBomManifestsReadLikeLf()
        {
            Write("a/mod.py", "Mod(\r\n    id=\"a\",\r\n    dependencies=[\"b\"],\r\n)\r\n", bom: true);
            var bag = new DiagnosticBag();
            var mods = ModDiscovery.Discover(_root, bag).Mods;
            Assert.That(bag.Count, Is.Zero);
            Assert.That(mods.Single().Dependencies.Single().Span.Line, Is.EqualTo(3));
            Assert.That(mods.Single().Dependencies.Single().Span.Col, Is.EqualTo(19));
        }

        [Test]
        public void FolderWithoutManifestWarns()
        {
            Write("notes/readme.txt", "hello");
            var bag = new DiagnosticBag();
            var mods = ModDiscovery.Discover(_root, bag).Mods;
            Assert.That(mods, Is.Empty);
            var d = bag.ToSortedList().Single();
            Assert.That(d.Severity, Is.EqualTo(Severity.Warning));
            Assert.That(d.File, Is.EqualTo("notes/mod.py"));
        }

        [Test]
        public void WrongCaseManifestIsAnError()
        {
            Write("a/Mod.py", "Mod(id=\"a\")\n");
            var bag = new DiagnosticBag();
            ModDiscovery.Discover(_root, bag);
            var d = bag.ToSortedList().Single();
            Assert.That(d.Severity, Is.EqualTo(Severity.Error));
            Assert.That(d.Code, Is.EqualTo(Codes.ModNoManifest));
        }

        [Test]
        public void DependentOfBrokenManifestReportsFailureNotMissing()
        {
            Write("core/mod.py", "Mod(id=\"core\", dependancies=[])\n");
            Write("a/mod.py", "Mod(id=\"a\", dependencies=[\"core\"])\n");
            var bag = new DiagnosticBag();
            var discovery = ModDiscovery.Discover(_root, bag);
            Assert.That(discovery.FailedIds, Is.EqualTo(new[] { "core" }));

            var order = ModSorter.Sort(discovery, bag);
            var codes = bag.ToSortedList().Select(d => d.Code).ToList();
            Assert.That(order.Mods, Is.Empty);
            Assert.That(order.Disabled, Is.EqualTo(new[] { "a", "core" }));
            Assert.That(codes, Does.Contain(Codes.ModUnknownField));
            Assert.That(codes, Does.Contain(Codes.ModDependencyFailed));
            Assert.That(codes, Does.Not.Contain(Codes.ModMissingDependency));
        }

        [Test]
        public void MissingUseTargetIsAnError()
        {
            Write("a/mod.py", "Mod(id=\"a\")\nuse(\"units.py\")\nuse_all(\"defs/\")\n");
            Write("a/defs/x.py", "");
            var bag = new DiagnosticBag();
            ModDiscovery.Discover(_root, bag);
            var d = bag.ToSortedList().Single();
            Assert.That(d.Code, Is.EqualTo(Codes.ModBadManifest));
            Assert.That((d.File, d.Line), Is.EqualTo(("a/mod.py", 2)));
            Assert.That(d.Mod, Is.EqualTo("a"));
        }
    }
}
