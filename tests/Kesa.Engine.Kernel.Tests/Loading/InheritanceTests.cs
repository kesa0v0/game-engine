using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>④ 상속 펼치기 (spec-gaps G52·G55·G56·G57·G58).</summary>
    public class InheritanceTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10), regen=const(int, default=0), current=field(int))\n" +
            "component(\"Stock\", key=\"Resource\", amount=field(fixed, default=0))\n" +
            "deftype(\"Resource\")\n" +
            "deftype(\"Building\")\n" +
            "deftype(\"Unit\",\n" +
            "    components=[\"Health\", \"Stock\"],\n" +
            "    move=const(int, default=1),\n" +
            "    ratio=const(fixed),\n" +
            "    label=const(str),\n" +
            "    tags=const(list[str], merge=\"append\"),\n" +
            "    names=const(list[str]),\n" +
            "    cost=const(dict[DefId[\"Resource\"], int]),\n" +
            ")\n";

        private const string Resources = "Resource(\"Wood\")\nResource(\"Iron\")\n";

        private ResolvedDefs Expand(string defs, out IReadOnlyList<Diagnostic> diags)
        {
            Mod("core", schema: Schema, defs: Resources + defs);
            return Load(out diags).Expanded;
        }

        private ResolvedDefs ExpandClean(string defs)
        {
            var expanded = Expand(defs, out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            return expanded;
        }

        private static long Int(ResolvedValue v) => ((IntValue)v.Value).Value;

        private static ResolvedValue Const(ResolvedDefs defs, string id, string name)
        {
            Assert.That(defs.TryGet(id, out var def), Is.True, id);
            Assert.That(def.Consts.TryGet(name, out var value), Is.True, id + "." + name);
            return value;
        }

        // ---- 병합 ----

        [Test]
        public void EveryDefIsExpandedInRegistrationOrder()
        {
            var defs = ExpandClean("Unit(\"Base\", abstract=True)\nUnit(\"Scout\", parent=\"Base\")\n");
            Assert.That(defs.Defs.Select(d => d.Id), Is.EqualTo(new[] { "Wood", "Iron", "Base", "Scout" }));
            Assert.That(defs.TryGet("Base", out var b), Is.True);
            Assert.That(b.IsAbstract, Is.True);
            Assert.That(defs.TryGet("Scout", out var s), Is.True);
            Assert.That(s.Parent, Is.SameAs(b));
        }

        [Test]
        public void AChildInheritsWhatItDoesNotWrite()
        {
            var defs = ExpandClean("Unit(\"Base\", move=3, label=\"b\")\nUnit(\"Scout\", parent=\"Base\", label=\"s\")\n");
            var move = Const(defs, "Scout", "move");
            Assert.That(Int(move), Is.EqualTo(3));
            Assert.That(move.Origin, Is.EqualTo(ValueOrigin.Inherited));
            Assert.That(move.Source.Id, Is.EqualTo("Base"));
            var label = Const(defs, "Scout", "label");
            Assert.That(((StrValue)label.Value).Value, Is.EqualTo("s"));
            Assert.That(label.Origin, Is.EqualTo(ValueOrigin.Own));
            Assert.That(Const(defs, "Base", "move").Origin, Is.EqualTo(ValueOrigin.Own));
        }

        [Test]
        public void ReplaceListsAreReplacedAndAppendListsAreJoined()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", tags=[\"a\"], names=[\"x\"])\n" +
                "Unit(\"Scout\", parent=\"Base\", tags=[\"b\"], names=[\"y\"])\n" +
                "Unit(\"Archer\", parent=\"Scout\", tags=[\"c\"])\n");
            string Items(ResolvedValue v) => string.Join(",", ((ListValue)v.Value).Items.Select(i => ((StrValue)i).Value));
            Assert.That(Items(Const(defs, "Scout", "tags")), Is.EqualTo("a,b"));
            Assert.That(Items(Const(defs, "Scout", "names")), Is.EqualTo("y"));
            Assert.That(Items(Const(defs, "Archer", "tags")), Is.EqualTo("a,b,c"));
            Assert.That(Items(Const(defs, "Archer", "names")), Is.EqualTo("y"));
        }

        [Test]
        public void AnInheritExpressionOnAnAppendFieldIsTheWholeValue()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", tags=[\"a\"])\n" +
                "Unit(\"Scout\", parent=\"Base\", tags=inherit.tags + [\"fast\"])\n" +
                "Unit(\"Archer\", parent=\"Base\", tags=[\"z\"] + inherit.tags)\n");
            string Items(string id) =>
                string.Join(",", ((ListValue)Const(defs, id, "tags").Value).Items.Select(i => ((StrValue)i).Value));
            Assert.That(Items("Scout"), Is.EqualTo("a,fast"));
            Assert.That(Items("Archer"), Is.EqualTo("z,a"));
        }

        [Test]
        public void ComponentsAreMergedValueByValue()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", Health=Health(base_max=10, regen=2))\n" +
                "Unit(\"Scout\", parent=\"Base\", Health=Health(base_max=12))\n");
            Assert.That(defs.TryGet("Scout", out var scout), Is.True);
            Assert.That(scout.Components.TryGet("Health", out var health), Is.True);
            Assert.That(health.Members.Keys, Is.EquivalentTo(new[] { "base_max", "regen" }));
            health.Members.TryGet("base_max", out var baseMax);
            health.Members.TryGet("regen", out var regen);
            Assert.That((Int(baseMax), baseMax.Origin), Is.EqualTo((12L, ValueOrigin.Own)));
            Assert.That((Int(regen), regen.Origin), Is.EqualTo((2L, ValueOrigin.Inherited)));
        }

        [Test]
        public void AComponentTheChildDoesNotWriteIsInherited()
        {
            var defs = ExpandClean("Unit(\"Base\", Health=Health())\nUnit(\"Scout\", parent=\"Base\")\n");
            Assert.That(defs.TryGet("Scout", out var scout), Is.True);
            Assert.That(scout.Components.Contains("Health"), Is.True);
            Assert.That(scout.Components.Contains("Stock"), Is.False);
        }

        [Test]
        public void KeyedComponentsAreMergedByRowThenByValue()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", Stock={\"Wood\": Stock(amount=1.5), \"Iron\": Stock(amount=2.0)})\n" +
                "Unit(\"Scout\", parent=\"Base\", Stock={\"Wood\": Stock(amount=3.0), \"Wood2\": Stock()})\n");
            Assert.That(defs.TryGet("Scout", out var scout), Is.True);
            scout.Components.TryGet("Stock", out var stock);
            Assert.That(stock.Rows.Keys, Is.EqualTo(new[] { "Wood", "Iron", "Wood2" }));
            stock.Rows.TryGet("Wood", out var wood);
            wood.Members.TryGet("amount", out var amount);
            Assert.That(((FixedValue)amount.Value).Value.Raw, Is.EqualTo(3_000_000L));
            Assert.That(amount.Origin, Is.EqualTo(ValueOrigin.Own));
            stock.Rows.TryGet("Iron", out var iron);
            iron.Members.TryGet("amount", out var ironAmount);
            Assert.That(ironAmount.Origin, Is.EqualTo(ValueOrigin.Inherited));
        }

        // ---- inherit 식 ----

        [Test]
        public void InheritReadsTheParentsFinalValue()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", move=2)\n" +
                "Unit(\"Scout\", parent=\"Base\", move=inherit.move + 1)\n" +
                "Unit(\"Archer\", parent=\"Scout\", move=inherit.move * 2)\n");
            Assert.That(Int(Const(defs, "Scout", "move")), Is.EqualTo(3));
            Assert.That(Int(Const(defs, "Archer", "move")), Is.EqualTo(6));
            Assert.That(Const(defs, "Archer", "move").Origin, Is.EqualTo(ValueOrigin.Own));
        }

        [Test]
        public void InheritFallsBackToTheSchemaDefault()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", Health=Health())\n" +
                "Unit(\"Scout\", parent=\"Base\", move=inherit.move + 4,Health=Health(base_max=inherit.Health.base_max + 2))\n" +
                "Unit(\"Plain\", Health=Health())\n" +
                "Unit(\"Tough\", parent=\"Plain\", Health=Health(base_max=inherit.Health.base_max * 3))\n");
            Assert.That(Int(Const(defs, "Scout", "move")), Is.EqualTo(5));
            defs.TryGet("Tough", out var tough);
            tough.Components.TryGet("Health", out var health);
            health.Members.TryGet("base_max", out var baseMax);
            Assert.That(Int(baseMax), Is.EqualTo(30));
        }

        [Test]
        public void InheritedListsCanBeExtended()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", names=[\"a\"])\n" +
                "Unit(\"Scout\", parent=\"Base\", names=inherit.names + [\"b\", \"c\"])\n");
            var items = ((ListValue)Const(defs, "Scout", "names").Value).Items.Select(i => ((StrValue)i).Value);
            Assert.That(items, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void InheritExpressionsCanUseNamesFromTheSameFile()
        {
            var defs = ExpandClean("bonus = 5\nUnit(\"Base\", move=1)\nUnit(\"Scout\", parent=\"Base\", move=inherit.move + bonus)\n");
            Assert.That(Int(Const(defs, "Scout", "move")), Is.EqualTo(6));
        }

        [Test]
        public void MapLiteralsAreBuiltButIndexingIsNotSupportedYet()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", cost={\"Wood\": 2})\n" +
                "Unit(\"Scout\", parent=\"Base\", cost={\"Wood\": inherit.move + 2, \"Iron\": inherit.move * 6})\n");
            var cost = (DictValue)Const(defs, "Scout", "cost").Value;
            Assert.That(cost.Items.Select(i => ((IntValue)i.Value).Value), Is.EqualTo(new[] { 3L, 6L }));
            Expand("Unit(\"Base\", cost={\"Wood\": 2})\nUnit(\"Scout\", parent=\"Base\", cost={\"Wood\": inherit.cost[\"Wood\"]})\n",
                out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.EvlUnsupported));
        }

        [Test]
        public void FixedArithmeticFollowsTheDeterministicRules()
        {
            var defs = ExpandClean(
                "Unit(\"Base\", ratio=1.5)\n" +
                "Unit(\"A\", parent=\"Base\", ratio=inherit.ratio * 1.5)\n" +
                "Unit(\"B\", parent=\"Base\", ratio=inherit.ratio * 3)\n" +
                "Unit(\"C\", parent=\"Base\", ratio=inherit.ratio + 1)\n" +
                "Unit(\"D\", parent=\"Base\", ratio=inherit.ratio - 2)\n" +
                "Unit(\"E\", parent=\"Base\", ratio=-inherit.ratio)\n");
            long Raw(string id) => ((FixedValue)Const(defs, id, "ratio").Value).Value.Raw;
            Assert.That(Raw("A"), Is.EqualTo(2_250_000L));
            Assert.That(Raw("B"), Is.EqualTo(4_500_000L));
            Assert.That(Raw("C"), Is.EqualTo(2_500_000L));
            Assert.That(Raw("D"), Is.EqualTo(-500_000L));
            Assert.That(Raw("E"), Is.EqualTo(-1_500_000L));
        }

        [TestCase(1, 500_000, 1)]
        [TestCase(-1, 500_000, -1)]
        [TestCase(1, 499_999, 0)]
        [TestCase(3, 500_000, 2)]
        [TestCase(-3, 500_000, -2)]
        public void FixedMultiplicationRoundsAwayFromZero(long a, long b, long expected)
        {
            Assert.That(FixedMath.TryMultiply(Fixed.FromRaw(a), Fixed.FromRaw(b), out var result), Is.True);
            Assert.That(result.Raw, Is.EqualTo(expected));
        }

        [Test]
        public void FixedMultiplicationOverflowIsReported()
        {
            Assert.That(FixedMath.TryMultiply(Fixed.FromRaw(long.MaxValue), Fixed.FromRaw(long.MaxValue), out _), Is.False);
            Assert.That(FixedMath.TryMultiply(long.MaxValue, 2, out _), Is.False);
            Assert.That(FixedMath.TryNegate(long.MinValue, out _), Is.False);
        }

        // ---- inherit 오류 ----

        [TestCase("Unit(\"Scout\", move=inherit.move)\n", Codes.InhNoParent)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", names=inherit.names + [\"x\"])\n", Codes.InhNoValue)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", Health=Health(current=inherit.Health.current))\n",
            Codes.InhNoValue)]
        [TestCase("Unit(\"Base\", Health=Health())\nUnit(\"Scout\", parent=\"Base\", move=inherit.mvoe + 1)\n", Codes.DefUnknownField)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.Health + 1)\n", Codes.EvlUnsupported)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.move / 2)\n", Codes.EvlUnsupported)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.move ** 2)\n", Codes.EvlUnsupported)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.Health.base_max.x)\n", Codes.EvlUnsupported)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.move.x)\n", Codes.DefUnknownField)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.move + \"a\")\n", Codes.EvlType)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.move + nope)\n", Codes.EvlUnknownName)]
        [TestCase("Unit(\"Base\", move=9223372036854775807)\nUnit(\"Scout\", parent=\"Base\", move=inherit.move + 1)\n", Codes.EvlType)]
        [TestCase("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=Unit(\"X\") + inherit.move)\n", Codes.EvlUnsupported)]
        public void BadInheritExpressions(string defs, string code)
        {
            Expand(defs, out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code), string.Join("\n", diags));
        }

        [TestCase("names=[inherit.names[0]]")]
        [TestCase("names=[Unit] + inherit.names")]
        [TestCase("names=inherit.names + [Health]")]
        [TestCase("label=inherit.label + str")]
        public void AnInheritResultMustBeData(string field)
        {
            Expand("Unit(\"Base\", names=[\"a\"], label=\"x\")\nUnit(\"Scout\", parent=\"Base\", " + field + ")\n", out var diags);
            Assert.That(diags, Is.Not.Empty);
            Assert.That(diags.Select(d => d.Code), Has.None.EqualTo(Codes.InhNoValue));
        }

        [Test]
        public void ABuiltInNameInsideAnInheritResultIsRejected()
        {
            Expand("Unit(\"Base\", names=[\"a\"])\nUnit(\"Scout\", parent=\"Base\", names=inherit.names + [Unit])\n", out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.DefBadValue, 4)));
        }

        [Test]
        public void ABadInheritExpressionDisablesTheModAndPointsAtTheExpression()
        {
            Mod("core", schema: Schema, defs: Resources + "Unit(\"Base\")\nUnit(\"Scout\", parent=\"Base\", move=inherit.move / 2)\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Line, d.Mod), Is.EqualTo((Codes.EvlUnsupported, 4, "core")));
            Assert.That(d.Hint, Does.Contain("+ - *"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        // ---- parent 검증 (G57) ----

        [Test]
        public void AMissingParentIsAnErrorWithASuggestion()
        {
            Expand("Unit(\"Base\")\nUnit(\"Scout\", parent=\"Bse\")\n", out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.InhParentNotFound, 4)));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'Base'?"));
        }

        [Test]
        public void AParentOfAnotherTypeIsAnError()
        {
            Expand("Building(\"Hut\")\nUnit(\"Scout\", parent=\"Hut\")\n", out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.InhParentType));
            Assert.That(d.Related, Has.Count.EqualTo(1));
        }

        [Test]
        public void AParentFromADependencyModIsFine()
        {
            Mod("core", schema: Schema, defs: Resources + "Unit(\"Base\")\n");
            Mod("addon", defs: "Unit(\"Scout\", parent=\"Base\")\n", extra: ", dependencies=[\"core\"]");
            Assert.That(LoadClean().Expanded.TryGet("Scout", out _), Is.True);
        }

        [Test]
        public void AModThatDoesNotDependOnTheParentsModCannotUseIt()
        {
            Mod("core", schema: Schema, defs: Resources + "Unit(\"Base\")\n");
            Mod("lone", defs: "Unit(\"Other\", parent=\"Base\")\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.SchNotVisible), string.Join("\n", diags));
        }

        [TestCase("Unit(\"A\", parent=\"A\")\n", 1)]
        [TestCase("Unit(\"A\", parent=\"B\")\nUnit(\"B\", parent=\"A\")\n", 2)]
        [TestCase("Unit(\"A\", parent=\"B\")\nUnit(\"B\", parent=\"C\")\nUnit(\"C\", parent=\"A\")\n", 3)]
        public void ACycleIsReportedOncePerDef(string defs, int count)
        {
            Expand(defs, out var diags);
            Assert.That(diags.Count(d => d.Code == Codes.InhCycle), Is.EqualTo(count), string.Join("\n", diags));
            Assert.That(diags, Has.Count.EqualTo(count));
        }

        [Test]
        public void ChildrenOfAFailedDefAreSkippedWithoutFollowUpErrors()
        {
            Expand(
                "Unit(\"Base\", parent=\"Nope\")\n" +
                "Unit(\"Scout\", parent=\"Base\", move=inherit.move + 1)\n" +
                "Unit(\"Archer\", parent=\"Scout\")\n", out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.InhParentNotFound }));
        }

        [Test]
        public void ADefWhoseExpansionFailsIsMissingFromTheResult()
        {
            Mod("core", schema: Schema, defs: Resources + "Unit(\"Base\")\n");
            Mod("addon", defs: "Unit(\"Scout\", parent=\"Nope\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Single().Mod, Is.EqualTo("addon"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon" }));
            Assert.That(result.Expanded.Defs.Select(d => d.Id), Is.EqualTo(new[] { "Wood", "Iron", "Base" }));
        }

        [Test]
        public void AnOverridingDefDoesNotInheritFromTheDefItReplaces()
        {
            Mod("core", schema: Schema, defs: Resources + "Unit(\"Base\", move=5, label=\"core\")\n");
            Mod("addon", defs: "Unit(\"Base\", override=True, move=7)\n", extra: ", dependencies=[\"core\"]");
            var result = LoadClean();
            Assert.That(Int(Const(result.Expanded, "Base", "move")), Is.EqualTo(7));
            Assert.That(result.Expanded.TryGet("Base", out var def), Is.True);
            Assert.That(def.Consts.Contains("label"), Is.False);
            Assert.That(def.Mod, Is.EqualTo("addon"));
        }
    }
}
