using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>⑤ 링크: Def를 가리키는 id 문자열 검사 (spec-gaps G63~G66).</summary>
    public class LinkTests : LoaderTestBase
    {
        private const string CoreDep = ", dependencies=[\"core\"]";

        private const string Schema =
            "component(\"Stock\", key=\"Resource\", amount=field(fixed, default=0))\n" +
            "deftype(\"Resource\")\n" +
            "deftype(\"Building\")\n" +
            "deftype(\"Unit\",\n" +
            "    components=[\"Stock\"],\n" +
            "    cost=const(dict[DefId[\"Resource\"], int]),\n" +
            "    builds=const(list[DefId[\"Building\"]]),\n" +
            "    home=const(DefId[\"Building\"], default=\"Hut\"),\n" +
            "    groups=const(dict[str, list[DefId[\"Resource\"]]]),\n" +
            ")\n";

        private const string Things = "Resource(\"Wood\")\nResource(\"Iron\")\nBuilding(\"Hut\")\nBuilding(\"Tower\", abstract=True)\n";

        private ContentLoadResult Run(string defs, out IReadOnlyList<Diagnostic> diags)
        {
            Mod("core", schema: Schema, defs: Things + defs);
            return Load(out diags);
        }

        private Diagnostic Single(string defs, string code)
        {
            Run(defs, out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(code), d.ToString());
            return d;
        }

        [Test]
        public void ValidReferencesPassEverywhereATypeNamesADef()
        {
            var result = Run(
                "Unit(\"Scout\", cost={\"Wood\": 1}, builds=[\"Hut\"], home=\"Hut\", groups={\"a\": [\"Wood\", \"Iron\"]},\n" +
                "     Stock={\"Iron\": Stock(amount=2.0)})\n", out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Disabled, Is.Empty);
        }

        [Test]
        public void ValuesAreNotChangedByLinking()
        {
            var result = Run("Unit(\"Scout\", home=\"Hut\")\n", out _);
            result.Expanded.TryGet("Scout", out var scout);
            scout.Consts.TryGet("home", out var home);
            Assert.That(((Kesa.Engine.Script.StrValue)home.Value).Value, Is.EqualTo("Hut"));
        }

        // ---- 존재, 타입 ----

        [Test]
        public void AMissingDefIsAnErrorWithASuggestion()
        {
            var d = Single("Unit(\"Scout\", cost={\"Wod\": 1})\n", Codes.LnkNotFound);
            Assert.That(d.Hint, Is.EqualTo("did you mean 'Wood'?"));
            Assert.That((d.Mod, d.Line), Is.EqualTo(("core", 5)));
        }

        [TestCase("Unit(\"Scout\", home=\"Nope\")\n")]
        [TestCase("Unit(\"Scout\", builds=[\"Hut\", \"Nope\"])\n")]
        [TestCase("Unit(\"Scout\", groups={\"a\": [\"Nope\"]})\n")]
        [TestCase("Unit(\"Scout\", Stock={\"Nope\": Stock()})\n")]
        public void EveryPlaceAReferenceCanBeIsChecked(string defs)
        {
            var d = Single(defs, Codes.LnkNotFound);
            Assert.That(d.Message, Does.Contain("Nope"));
        }

        [Test]
        public void ADefOfAnotherTypeIsAnError()
        {
            var d = Single("Unit(\"Scout\", cost={\"Hut\": 1})\n", Codes.LnkWrongType);
            Assert.That(d.Message, Does.Contain("Building").And.Contain("Resource"));
            Assert.That(d.Related, Has.Count.EqualTo(1));
        }

        [Test]
        public void ARowKeyMustBeADefOfTheKeyType()
        {
            Single("Unit(\"Scout\", Stock={\"Hut\": Stock()})\n", Codes.LnkWrongType);
        }

        [Test]
        public void AMapKeyIsCheckedLikeAValue()
        {
            Single("Unit(\"Scout\", cost={\"Nope\": 1})\n", Codes.LnkNotFound);
        }

        // ---- abstract (G66) ----

        [Test]
        public void AnAbstractDefCannotBeReferenced()
        {
            var d = Single("Unit(\"Scout\", home=\"Tower\")\n", Codes.LnkAbstract);
            Assert.That(d.Hint, Does.Contain("concrete"));
        }

        [Test]
        public void AnAbstractParentIsStillFine()
        {
            var result = Run("Unit(\"Base\", abstract=True, home=\"Hut\")\nUnit(\"Scout\", parent=\"Base\")\n", out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Disabled, Is.Empty);
        }

        // ---- 보이는 범위 (G64) ----

        [Test]
        public void ADefOfAModTheWriterDoesNotDependOnIsAnError()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\n");
            Mod("huts", defs: "Building(\"Hut\")\n", extra: CoreDep);
            Mod("lone", defs: "Unit(\"Scout\", home=\"Hut\")\n", extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Mod)), Does.Contain((Codes.LnkNotVisible, "lone")));
            Assert.That(result.Disabled, Does.Contain("lone"));
        }

        [Test]
        public void ADefOfADependencyIsVisible()
        {
            Mod("core", schema: Schema, defs: Things);
            Mod("addon", defs: "Unit(\"Scout\", home=\"Hut\")\n", extra: CoreDep);
            Assert.That(LoadClean().Disabled, Is.Empty);
        }

        [Test]
        public void ABadInheritedValueIsBlamedOnTheModThatWroteIt()
        {
            Mod("core", schema: Schema, defs: Things + "Unit(\"Base\", home=\"Nope\")\n");
            Mod("addon", defs: "Unit(\"Scout\", parent=\"Base\")\nUnit(\"Archer\", parent=\"Base\")\n", extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Where(d => d.Code == Codes.LnkNotFound).Select(d => d.Mod), Is.EqualTo(new[] { "core" }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon", "core" }));
        }

        // ---- 패치 값과 가드 (G65) ----

        [Test]
        public void ABadPatchValueIsBlamedOnThePatchingMod()
        {
            Mod("core", schema: Schema, defs: Things + "Unit(\"Scout\")\n");
            Mod("addon", defs: "patch.set(\"Scout\", \"home\", \"Nope\")\n", extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.LnkNotFound, "addon", 1)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon" }));
        }

        private void OptionalSetup(string addonDefs)
        {
            Mod("core", schema: Schema, defs: Things + "Unit(\"Scout\")\n");
            Mod("farm", defs: "Resource(\"FarmCoin\")\n", extra: CoreDep);
            Mod("addon", defs: addonDefs, extra: CoreDep + ", optional=[\"farm\"]");
        }

        [Test]
        public void AGuardOpensAnOptionalModsDefsInPatchValues()
        {
            OptionalSetup("if mod_loaded(\"farm\"):\n    patch.append(\"Scout\", \"cost\", {\"FarmCoin\": 1})\n");
            var result = Load(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Disabled, Is.Empty);
        }

        [Test]
        public void WithoutTheGuardTheOptionalModsDefIsNotVisible()
        {
            OptionalSetup("patch.append(\"Scout\", \"cost\", {\"FarmCoin\": 1})\n");
            Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.LnkNotVisible);
            Assert.That(d.Mod, Is.EqualTo("addon"));
            Assert.That(d.Hint, Does.Contain("mod_loaded(\"farm\")"));
        }

        [Test]
        public void AGuardForAnotherModDoesNotOpenIt()
        {
            Mod("core", schema: Schema, defs: Things + "Unit(\"Scout\")\n");
            Mod("farm", defs: "Resource(\"FarmCoin\")\n", extra: CoreDep);
            Mod("mine", defs: "Resource(\"Ore\")\n", extra: CoreDep);
            Mod("addon", defs: "if mod_loaded(\"mine\"):\n    patch.append(\"Scout\", \"cost\", {\"FarmCoin\": 1})\n",
                extra: CoreDep + ", optional=[\"farm\", \"mine\"]");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.LnkNotVisible));
        }

        [Test]
        public void ASkippedGuardIsNotLinked()
        {
            Mod("core", schema: Schema, defs: Things + "Unit(\"Scout\")\n");
            Mod("addon", defs: "if mod_loaded(\"farm\"):\n    patch.append(\"Scout\", \"cost\", {\"FarmCoin\": 1})\n",
                extra: CoreDep + ", optional=[\"farm\"]");
            Mod("farm", defs: "Resource(\"FarmCoin\")\n", extra: CoreDep);
            System.IO.Directory.Delete(System.IO.Path.Combine(_root, "farm"), true);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Severity), Has.All.EqualTo(Severity.Info));
            Assert.That(result.Disabled, Is.Empty);
        }

        [Test]
        public void ALiteralDefValueCannotUseAnOptionalModsDef()
        {
            Mod("core", schema: Schema, defs: Things);
            Mod("farm", defs: "Resource(\"FarmCoin\")\n", extra: CoreDep);
            Mod("addon", defs: "Unit(\"Scout\", cost={\"FarmCoin\": 1})\n", extra: CoreDep + ", optional=[\"farm\"]");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.LnkNotVisible));
        }

        // ---- 오류 모음 ----

        [Test]
        public void SeveralBadReferencesAreAllReportedAndOnlyOnceEach()
        {
            Run("Unit(\"Scout\", home=\"Nope\", cost={\"Hut\": 1})\nUnit(\"Archer\", parent=\"Scout\")\n", out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EquivalentTo(new[] { Codes.LnkNotFound, Codes.LnkWrongType }));
        }
    }
}
