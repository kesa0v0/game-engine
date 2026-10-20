using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    public class ManifestReaderTests
    {
        private static (ModManifest? Manifest, IReadOnlyList<Diagnostic> Diagnostics) Read(string text)
        {
            var bag = new DiagnosticBag();
            var m = ManifestReader.Read(text, "my_mod", bag);
            return (m, bag.ToSortedList());
        }

        [Test]
        public void FullManifest()
        {
            var (m, diags) = Read(
                "Mod(\n" +
                "    id=\"my_mod\",\n" +
                "    version=\"1.2.0\",\n" +
                "    defaultLanguage=\"ko\",\n" +
                "    dependencies=[\"core\"],\n" +
                "    loadAfter=[\"balance\"],\n" +
                "    optional=[\"extra_units\"],\n" +
                ")\n" +
                "use(\"units.py\")\n" +
                "use_all(\"defs/\")\n");

            Assert.That(diags, Is.Empty);
            Assert.That(m, Is.Not.Null);
            Assert.That(m!.Id, Is.EqualTo("my_mod"));
            Assert.That(m.Version, Is.EqualTo("1.2.0"));
            Assert.That(m.DefaultLanguage, Is.EqualTo("ko"));
            Assert.That(m.Dependencies.Select(r => r.Id), Is.EqualTo(new[] { "core" }));
            Assert.That(m.LoadAfter.Select(r => r.Id), Is.EqualTo(new[] { "balance" }));
            Assert.That(m.Optional.Select(r => r.Id), Is.EqualTo(new[] { "extra_units" }));
            Assert.That(m.Uses.Select(u => (u.Kind, u.Path)),
                Is.EqualTo(new[] { (UseKind.File, "units.py"), (UseKind.Directory, "defs") }));
            Assert.That(m.Span.File, Is.EqualTo("my_mod/mod.py"));
            Assert.That(m.Span.Line, Is.EqualTo(1));
        }

        [Test]
        public void UnknownFieldSuggestsClosest()
        {
            var (m, diags) = Read("Mod(id=\"a\", dependancies=[\"b\"])\n");
            Assert.That(m, Is.Null);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.ModUnknownField));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'dependencies'?"));
        }

        [TestCase("Mod(id=name)\n")]
        [TestCase("Mod(id=\"a\", dependencies=deps)\n")]
        [TestCase("Mod(id=\"a\", dependencies=[\"b\" + \"c\"])\n")]
        [TestCase("Mod(id=\"a\")\nuse(path)\n")]
        public void NonLiteralArgumentsAreRejected(string text)
        {
            var (m, diags) = Read(text);
            Assert.That(m, Is.Null);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.ModNotLiteral));
        }

        [TestCase("Mod(id=\"My_Mod\")\n")]
        [TestCase("Mod(id=\"core.units\")\n")]
        [TestCase("Mod(id=\"1st\")\n")]
        [TestCase("Mod(id=\"\")\n")]
        [TestCase("Mod(id=\"a\", dependencies=[\"Bad-Id\"])\n")]
        public void InvalidIdsAreRejected(string text)
        {
            var (m, diags) = Read(text);
            Assert.That(m, Is.Null);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.ModBadId));
        }

        [Test]
        public void TheEngineIdIsReserved()
        {
            var (m, diags) = Read("Mod(id=\"engine\")\n");
            Assert.That(m, Is.Null);
            Assert.That(diags.Select(d => d.Code + " " + d.Message + " | " + d.Hint), Is.EqualTo(new[]
            {
                "MOD005 mod id 'engine' is reserved for the engine | the engine names its own commands and text keys with it; pick another id",
            }));
            Assert.That(Read("Mod(id=\"engine_parts\")\n").Diagnostics, Is.Empty);
        }

        [TestCase("x = 1\nMod(id=\"a\")\n")]
        [TestCase("Mod(id=\"a\")\nUnit(\"Scout\")\n")]
        [TestCase("Mod(id=\"a\")\nMod(id=\"b\")\n")]
        [TestCase("use(\"a.py\")\n")]
        [TestCase("Mod(\"a\")\n")]
        [TestCase("Mod(version=\"1\")\n")]
        [TestCase("Mod(id=\"a\")\nuse(\"../other/a.py\")\n")]
        [TestCase("Mod(id=\"a\")\nuse(\"/abs/a.py\")\n")]
        [TestCase("Mod(id=\"a\")\nuse(\"dir\\\\a.py\")\n")]
        [TestCase("Mod(id=\"a\")\nuse(\"a.txt\")\n")]
        public void MalformedManifests(string text)
        {
            var (m, diags) = Read(text);
            Assert.That(m, Is.Null);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.ModBadManifest));
        }

        [Test]
        public void SelfReferenceIsAnError()
        {
            var (m, diags) = Read("Mod(id=\"a\", loadAfter=[\"a\"])\n");
            Assert.That(m, Is.Null);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.ModSelfReference));
        }

        [Test]
        public void SyntaxErrorsAreReportedWithLocation()
        {
            var (m, diags) = Read("Mod(id=\"a\",\n    dependencies=[\"b\" \"c\" 1])\n");
            Assert.That(m, Is.Null);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.ParSyntax));
            Assert.That((d.File, d.Line), Is.EqualTo(("my_mod/mod.py", 2)));
        }
    }
}
