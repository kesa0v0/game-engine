using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>⑥ 값 검증: 필수 값, 타입, 범위 (spec-gaps G67·G68).</summary>
    public class ValidationTests : LoaderTestBase
    {
        private const string CoreDep = ", dependencies=[\"core\"]";

        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10, min=1, max=100), regen=const(int), current=field(int, min=0))\n" +
            "component(\"Morale\", value=const(int))\n" +
            "deftype(\"Resource\")\n" +
            "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=const(int, min=0, max=20))\n" +
            "deftype(\"Unit\",\n" +
            "    components=[\"Health\", \"Skill\"],\n" +
            "    move=const(int, min=0),\n" +
            "    ratio=const(fixed, default=1.5, min=0.5, max=3),\n" +
            "    label=const(str, default=\"\"),\n" +
            "    tags=const(list[str]),\n" +
            "    costs=const(dict[DefId[\"Resource\"], int], min=0),\n" +
            "    marks=const(list[int], max=9),\n" +
            ")\n";

        private const string Wood = "Resource(\"Wood\")\n";
        private const string ScoutOk = "Unit(\"Scout\", move=3, Health=Health(regen=1))\n";

        private ContentLoadResult Run(string defs, out IReadOnlyList<Diagnostic> diags)
        {
            Mod("core", schema: Schema, defs: Wood + defs);
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
        public void ACompleteDefPasses()
        {
            var result = Run(ScoutOk, out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Disabled, Is.Empty);
        }

        // ---- 필수 값 (G67) ----

        [Test]
        public void AScalarWithoutDefaultMustHaveAValue()
        {
            var d = Single("Unit(\"Scout\", Health=Health(regen=1))\n", Codes.ValMissing);
            Assert.That(d.Message, Does.Contain("'move'").And.Contain("core:Scout"));
            Assert.That(d.Hint, Does.Contain("default="));
            Assert.That((d.Mod, d.Line), Is.EqualTo(("core", 2)));
        }

        [Test]
        public void ATypeSlotComponentsConstMustHaveAValueEvenIfTheDefNeverWroteTheComponent()
        {
            var d = Single("Unit(\"Scout\", move=3)\n", Codes.ValMissing);
            Assert.That(d.Message, Does.Contain("'regen'"));
        }

        [Test]
        public void ListsAndMapsDefaultToEmpty()
        {
            Assert.That(Run(ScoutOk, out var diags).Disabled, Is.Empty);
            Assert.That(diags, Is.Empty);
        }

        [Test]
        public void AnAbstractDefIsExemptButItsChildrenAreNot()
        {
            Run("Unit(\"Base\", abstract=True)\nUnit(\"Scout\", parent=\"core:Base\", move=1, Health=Health(regen=0))\n", out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));

            Run("Unit(\"Base\", abstract=True, move=2)\nUnit(\"Scout\", parent=\"core:Base\")\n", out var missing);
            Assert.That(missing.Single().Message, Does.Contain("'regen'"));
        }

        [Test]
        public void AValueInheritedFromTheParentCounts()
        {
            Run("Unit(\"Base\", abstract=True, move=2, Health=Health(regen=1))\nUnit(\"Scout\", parent=\"core:Base\")\n", out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }

        [Test]
        public void AComponentAttachedByAPatchNeedsItsRequiredValuesAndThePatchModIsBlamed()
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("addon", defs: "patch.set(\"core:Scout\", \"Morale\", Morale())\n", extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.ValMissing, "addon", 1)));
            Assert.That(d.Message, Does.Contain("'value'"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon" }));
        }

        [Test]
        public void AnExtensionTheDefsModCannotSeeIsNotRequiredOfItBecauseItHasADefault()
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("ext", schema: "extend(\"Unit\", weight=const(int, default=4))\n", extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Disabled, Is.Empty);
            // 동결본에는 core의 Scout에도 weight가 기본값으로 채워진다 (G69·G74)
            result.Frozen.TryGet("core:Scout", out var scout);
            scout.Consts.TryGet("weight", out var weight);
            Assert.That(((Kesa.Engine.Script.IntValue)weight.Value).Value, Is.EqualTo(4));
        }

        // ---- extend가 더한 값은 기본값이 있어야 한다 (G74) ----

        [Test]
        public void AnExtendedConstWithoutADefaultIsAnErrorOfTheExtendingMod()
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("ext", schema: "extend(\"Unit\", weight=const(int))\n", extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.SchBadDeclaration, "ext", 1)));
            Assert.That(d.Message, Does.Contain("weight").And.Contain("default"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "ext" }));
        }

        [TestCase("extend(\"Unit\", tags2=const(list[str]), costs2=const(dict[str, int]))\n")]
        [TestCase("extend(\"Unit\", flag=const(bool, default=False), name=const(str, default=\"\"))\n")]
        public void CollectionsAndDefaultedConstsMayBeExtended(string extend)
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("ext", schema: extend, extra: CoreDep);
            Assert.That(LoadClean().Disabled, Is.Empty);
        }

        [Test]
        public void AComponentAttachedByAnotherModNeedsDefaultsForItsConsts()
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("ext", schema: "component(\"Armor\", plates=const(int))\nextend(\"Unit\", components=[\"Armor\"])\n", extra: CoreDep);
            Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod), Is.EqualTo((Codes.SchBadDeclaration, "ext")));
            Assert.That(d.Message, Does.Contain("Armor.plates"));
        }

        [Test]
        public void AModMayExtendItsOwnTypeWithoutDefaults()
        {
            // 같은 모드의 Def는 모두 그 확장을 보므로 구멍이 생기지 않는다
            Mod("core", schema: Schema + "extend(\"Unit\", weight=const(int))\n",
                defs: Wood + "Unit(\"Scout\", move=3, weight=2, Health=Health(regen=1))\n");
            Assert.That(LoadClean().Disabled, Is.Empty);
        }

        // ---- 타입 (G68) ----

        [TestCase("Unit(\"Scout\", move=\"fast\", Health=Health(regen=1))\n")]
        [TestCase("Unit(\"Scout\", move=3, Health=Health(regen=1), label=5)\n")]
        [TestCase("Unit(\"Scout\", move=3, Health=Health(regen=1), tags=[\"a\", 1])\n")]
        [TestCase("Unit(\"Scout\", move=3, Health=Health(regen=1), marks=[1, \"x\"])\n")]
        [TestCase("Unit(\"Scout\", move=3, Health=Health(regen=1), costs={\"Wood\": \"many\"})\n")]
        [TestCase("Unit(\"Scout\", move=3, Health=Health(regen=1), ratio=\"big\")\n")]
        [TestCase("Unit(\"Scout\", move=True, Health=Health(regen=1))\n")]
        public void AWrongTypeIsAnError(string defs)
        {
            var d = Single(defs, Codes.ValType);
            Assert.That(d.Message, Does.Contain("expected"));
        }

        [Test]
        public void AFixedWhereAnIntIsExpectedExplainsWhy()
        {
            var d = Single("Unit(\"Scout\", move=2.5, Health=Health(regen=1))\n", Codes.ValType);
            Assert.That(d.Hint, Does.Contain("whole numbers"));
        }

        [Test]
        public void AnIntWhereAFixedIsExpectedIsFine()
        {
            Run("Unit(\"Scout\", move=3, Health=Health(regen=1), ratio=2)\n", out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }

        // ---- 범위 (G68) ----

        [TestCase("move=-1", "below min")]
        [TestCase("ratio=3.5", "above max")]
        [TestCase("ratio=0", "below min")]
        public void AScalarOutsideItsRangeIsAnError(string field, string text)
        {
            var extra = field.StartsWith("move") ? "" : "move=1, ";
            var d = Single("Unit(\"Scout\", " + extra + field + ", Health=Health(regen=1))\n",
                Codes.ValRange);
            Assert.That(d.Message, Does.Contain(text));
        }

        [TestCase("ratio=0.5")]
        [TestCase("ratio=3")]
        [TestCase("move=0")]
        public void TheBoundsThemselvesAreAllowed(string field)
        {
            var extra = field.StartsWith("move") ? "" : "move=1, ";
            Run("Unit(\"Scout\", " + extra + field + ", Health=Health(regen=1))\n", out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }

        [Test]
        public void RangesApplyToEveryElementAndMapValueButNotToMapKeys()
        {
            Single("Unit(\"Scout\", move=1, Health=Health(regen=1), marks=[1, 9, 10])\n", Codes.ValRange);
            var d = Single("Unit(\"Scout\", move=1, Health=Health(regen=1), costs={\"Wood\": -2})\n", Codes.ValRange);
            Assert.That(d.Message, Does.Contain("-2"));
            Run("Unit(\"Scout\", move=1, Health=Health(regen=1), costs={\"Wood\": 0}, marks=[9, 0])\n", out var ok);
            Assert.That(ok, Is.Empty, string.Join("\n", ok));
        }

        [Test]
        public void ComponentValuesAreRangeCheckedToo()
        {
            var d = Single("Unit(\"Scout\", move=1, Health=Health(base_max=500, regen=1))\n", Codes.ValRange);
            Assert.That(d.Message, Does.Contain("base_max").And.Contain("above max 100"));
            Single("Unit(\"Scout\", move=1, Health=Health(regen=1, current=-3))\n", Codes.ValRange);
        }

        [Test]
        public void AKeyedRowsValuesAreCheckedToo()
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("addon", defs: "patch.set(\"core:Scout\", \"Skill['core:Wood']\", Skill(level=30))\n", extra: CoreDep);
            Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod), Is.EqualTo((Codes.ValRange, "addon")));
        }

        // ---- 책임 모드 ----

        [Test]
        public void ARangeBrokenByAPatchIsBlamedOnThePatchingMod()
        {
            Mod("core", schema: Schema, defs: Wood + ScoutOk);
            Mod("addon", defs: "patch.add(\"core:Scout\", \"move\", -10)\n", extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.ValRange, "addon", 1)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon" }));
        }

        [Test]
        public void AnInheritedBadValueIsBlamedOnTheParentsMod()
        {
            Mod("core", schema: Schema, defs: Wood + "Unit(\"Base\", abstract=True, move=-5, Health=Health(regen=1))\n");
            Mod("addon", defs: "Unit(\"Scout\", parent=\"core:Base\")\nUnit(\"Archer\", parent=\"core:Base\")\n", extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Where(d => d.Code == Codes.ValRange).Select(d => d.Mod), Is.EqualTo(new[] { "core" }));
            Assert.That(result.Disabled, Does.Contain("core"));
        }

        // ---- 스키마가 선언한 기본값 (G73) ----

        [TestCase("n=const(int, default=1.5)", Codes.ValType)]
        [TestCase("n=const(int, default=500, max=100)", Codes.ValRange)]
        [TestCase("n=const(int, default=-1, min=0)", Codes.ValRange)]
        [TestCase("n=const(list[int], default=[1, 99], max=9)", Codes.ValRange)]
        [TestCase("home=const(DefId[\"Building\"], default=\"core:Nope\")", Codes.LnkNotFound)]
        [TestCase("home=const(DefId[\"Building\"], default=\"core:Wall\")", Codes.LnkAbstract)]
        [TestCase("home=const(DefId[\"Building\"], default=\"core:Wood\")", Codes.LnkWrongType)]
        [TestCase("homes=const(list[DefId[\"Building\"]], default=[\"core:Hut\", \"core:Nope\"])", Codes.LnkNotFound)]
        public void ASchemaDefaultIsCheckedLikeAValueAndBlamesTheDeclaringMod(string member, string code)
        {
            Mod("core", schema: "deftype(\"Resource\")\ndeftype(\"Building\")\n", defs: "Resource(\"Wood\")\nBuilding(\"Hut\")\nBuilding(\"Wall\", abstract=True)\n");
            Mod("rules", schema: "extend(\"Resource\", " + member + ")\n", extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((code, "rules", 1)));
            Assert.That(result.Disabled, Does.Contain("rules"));
        }

        [Test]
        public void AValidSchemaDefaultPassesAndAComputedFieldDefaultIsLeftAlone()
        {
            Mod("core",
                schema:
                "deftype(\"Building\")\n" +
                "component(\"Health\", base_max=const(int, default=10, min=1, max=100), current=field(int, default=lambda h: h.base_max // 2))\n" +
                "deftype(\"Unit\", components=[\"Health\"], home=const(DefId[\"Building\"], default=\"Hut\"), marks=const(list[int], default=[1, 2], max=9))\n",
                defs: "Building(\"Hut\")\nUnit(\"Scout\")\n");
            Assert.That(LoadClean().Disabled, Is.Empty);
        }

        // ---- 스키마 선언 ----

        [TestCase("component(\"C\", hp=const(str, min=1))\n", true)]
        [TestCase("component(\"C\", hp=const(list[str], max=3))\n", true)]
        [TestCase("component(\"C\", hp=const(list[int], max=3))\n", false)]
        [TestCase("component(\"C\", hp=const(dict[str, fixed], min=0))\n", false)]
        [TestCase("component(\"C\", hp=const(dict[int, str], min=0))\n", true)]
        public void MinMaxNeedNumbersInside(string schema, bool rejected)
        {
            Mod("core", schema: schema);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code).Contains(Codes.SchBadDeclaration), Is.EqualTo(rejected), string.Join("\n", diags));
        }
    }
}
