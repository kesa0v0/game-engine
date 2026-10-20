using System.Linq;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>⑥ 동결: 기본값을 채운 읽기 전용 Def 집합 (spec-gaps G69·G70).</summary>
    public class FreezeTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10), regen=const(int, default=0), current=field(int, default=5))\n" +
            "component(\"Morale\", value=const(int, default=3))\n" +
            "deftype(\"Resource\")\n" +
            "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=const(int, default=1))\n" +
            "deftype(\"Unit\",\n" +
            "    components=[\"Health\", \"Skill\"],\n" +
            "    move=const(int),\n" +
            "    speed=const(fixed, default=1.5),\n" +
            "    tags=const(list[str]),\n" +
            "    costs=const(dict[DefId[\"Resource\"], int]),\n" +
            ")\n";

        private const string Defs =
            "Resource(\"Wood\")\n" +
            "Unit(\"Base\", abstract=True, move=2)\n" +
            "Unit(\"Scout\", parent=\"core:Base\", tags=[\"a\"], Health=Health(base_max=12, current=9))\n";

        private DefSet Frozen(string defs = Defs)
        {
            Mod("core", schema: Schema, defs: defs);
            return LoadClean().Frozen;
        }

        private static ResolvedValue Const(FrozenDef def, string name)
        {
            Assert.That(def.Consts.TryGet(name, out var v), Is.True, name);
            return v;
        }

        [Test]
        public void OnlyConcreteDefsAreFrozenInRegistrationOrder()
        {
            var set = Frozen();
            Assert.That(set.Defs.Select(d => d.Id), Is.EqualTo(new[] { "core:Wood", "core:Scout" }));
            Assert.That(set.TryGet("core:Base", out _), Is.False);
            Assert.That(set.TryGet("core:Scout", out var scout), Is.True);
            Assert.That(scout.ParentId, Is.EqualTo("core:Base"));
            Assert.That(set.OfType("Unit").Select(d => d.Id), Is.EqualTo(new[] { "core:Scout" }));
            Assert.That(set.OfType("Resource").Select(d => d.Id), Is.EqualTo(new[] { "core:Wood" }));
        }

        [Test]
        public void ConstsAreFilledWithDefaultsAndMarkedAsSuch()
        {
            var set = Frozen();
            set.TryGet("core:Scout", out var scout);

            var move = Const(scout, "move");
            Assert.That(((IntValue)move.Value).Value, Is.EqualTo(2));
            Assert.That(move.Origin, Is.EqualTo(ValueOrigin.Inherited));

            var speed = Const(scout, "speed");
            Assert.That(((FixedValue)speed.Value).Value.Raw, Is.EqualTo(1_500_000L));
            Assert.That(speed.Origin, Is.EqualTo(ValueOrigin.Default));

            Assert.That(((ListValue)Const(scout, "tags").Value).Items, Has.Count.EqualTo(1));
            Assert.That(Const(scout, "tags").Origin, Is.EqualTo(ValueOrigin.Own));
        }

        [Test]
        public void EmptyCollectionsAreFilledIn()
        {
            Frozen().TryGet("core:Scout", out var scout);
            var costs = Const(scout, "costs");
            Assert.That(((DictValue)costs.Value).Items, Is.Empty);
            Assert.That(costs.Origin, Is.EqualTo(ValueOrigin.Default));
        }

        [Test]
        public void ComponentsOfTheTypeAreAlwaysThereWithTheirConstsFilled()
        {
            Frozen().TryGet("core:Scout", out var scout);
            Assert.That(scout.Components.Keys, Is.EquivalentTo(new[] { "Health", "Skill" }));

            scout.Components.TryGet("Health", out var health);
            health.Members.TryGet("base_max", out var baseMax);
            health.Members.TryGet("regen", out var regen);
            Assert.That(((IntValue)baseMax.Value).Value, Is.EqualTo(12));
            Assert.That(baseMax.Origin, Is.EqualTo(ValueOrigin.Own));
            Assert.That(((IntValue)regen.Value).Value, Is.EqualTo(0));
            Assert.That(regen.Origin, Is.EqualTo(ValueOrigin.Default));
        }

        [Test]
        public void FieldsAppearOnlyWhenTheyWereWritten()
        {
            Frozen().TryGet("core:Scout", out var scout);
            scout.Components.TryGet("Health", out var health);
            Assert.That(health.Members.Contains("current"), Is.True);

            var other = Frozen(Defs + "Unit(\"Archer\", parent=\"core:Base\")\n");
            other.TryGet("core:Archer", out var archer);
            archer.Components.TryGet("Health", out var archerHealth);
            Assert.That(archerHealth.Members.Contains("current"), Is.False);
            Assert.That(archerHealth.Members.Contains("base_max"), Is.True);
        }

        [Test]
        public void KeyedRowsAreFilledButNotInvented()
        {
            Mod("core", schema: Schema, defs: Defs + "Unit(\"Archer\", parent=\"core:Base\", Skill={\"Wood\": Skill()})\n");
            var set = LoadClean().Frozen;
            set.TryGet("core:Archer", out var archer);
            archer.Components.TryGet("Skill", out var skill);
            Assert.That(skill.Rows.Keys, Is.EqualTo(new[] { "core:Wood" }));
            skill.Rows.TryGet("core:Wood", out var row);
            row.Members.TryGet("level", out var level);
            Assert.That((((IntValue)level.Value).Value, level.Origin), Is.EqualTo((1L, ValueOrigin.Default)));

            set.TryGet("core:Scout", out var scout);
            scout.Components.TryGet("Skill", out var none);
            Assert.That(none.Rows.Keys, Is.Empty);
        }

        // ---- keys="all" 줄 읽기 (G77) ----

        private const string AllSchema =
            "deftype(\"Resource\")\n" +
            "deftype(\"Tool\")\n" +
            "component(\"Stock\", key=\"Resource\", amount=const(int, default=7), note=field(str, default=\"\"))\n" +
            "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=const(int, default=1))\n" +
            "deftype(\"Unit\", components=[\"Stock\", \"Skill\"])\n";

        private const string AllDefs =
            "Resource(\"Wood\")\nResource(\"Iron\")\nResource(\"Gem\", abstract=True)\nTool(\"Axe\")\n" +
            "Unit(\"Scout\", Stock={\"Iron\": Stock(amount=2)}, Skill={\"Wood\": Skill()})\n";

        [Test]
        public void AKeysAllRowThatWasNotWrittenIsReadAsAFilledDefaultRow()
        {
            Mod("core", schema: AllSchema, defs: AllDefs);
            var set = LoadClean().Frozen;
            set.TryGet("core:Scout", out var scout);

            Assert.That(scout.TryGetRow("Stock", "core:Iron", out var written), Is.True);
            written.Members.TryGet("amount", out var two);
            Assert.That((((IntValue)two.Value).Value, two.Origin), Is.EqualTo((2L, ValueOrigin.Own)));

            Assert.That(scout.TryGetRow("Stock", "core:Wood", out var implicitRow), Is.True);
            implicitRow.Members.TryGet("amount", out var seven);
            Assert.That((((IntValue)seven.Value).Value, seven.Origin), Is.EqualTo((7L, ValueOrigin.Default)));
            Assert.That(implicitRow.Members.Contains("note"), Is.False);

            // 저장은 적힌 줄뿐이다
            scout.Components.TryGet("Stock", out var stock);
            Assert.That(stock.Rows.Keys, Is.EqualTo(new[] { "core:Iron" }));
        }

        [TestCase("core:Gem")]    // abstract는 가리킬 수 없다
        [TestCase("core:Axe")]    // 다른 타입
        [TestCase("Nope")]   // 없는 Def
        public void OnlyAConcreteDefOfTheKeyTypeHasAnImplicitRow(string key)
        {
            Mod("core", schema: AllSchema, defs: AllDefs);
            LoadClean().Frozen.TryGet("core:Scout", out var scout);
            Assert.That(scout.TryGetRow("Stock", key, out _), Is.False);
        }

        [Test]
        public void AKeysListedRowThatWasNotWrittenIsAbsent()
        {
            Mod("core", schema: AllSchema, defs: AllDefs);
            LoadClean().Frozen.TryGet("core:Scout", out var scout);
            Assert.That(scout.TryGetRow("Skill", "core:Wood", out _), Is.True);
            Assert.That(scout.TryGetRow("Skill", "core:Iron", out _), Is.False);
            Assert.That(scout.TryGetRow("Nope", "core:Wood", out _), Is.False);
        }

        [Test]
        public void AKeysAllConstWithoutADefaultIsASchemaError()
        {
            Mod("core", schema: "deftype(\"Resource\")\ncomponent(\"Stock\", key=\"Resource\", amount=const(int))\ndeftype(\"Unit\", components=[\"Stock\"])\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Kesa.Engine.Diagnostics.Codes.SchBadDeclaration, "core", 2)));
            Assert.That(d.Message, Does.Contain("Stock.amount"));
            Assert.That(d.Hint, Does.Contain("listed"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [TestCase("component(\"Stock\", key=\"Resource\", keys=\"listed\", amount=const(int))\n")]
        [TestCase("component(\"Stock\", key=\"Resource\", amount=const(int, default=0), tags=const(list[str]))\n")]
        public void KeysListedAndDefaultedOrCollectionConstsAreFine(string component)
        {
            Mod("core", schema: "deftype(\"Resource\")\n" + component + "deftype(\"Unit\", components=[\"Stock\"])\n",
                defs: "Resource(\"Wood\")\nUnit(\"Scout\", Stock={\"Wood\": Stock(" +
                      (component.Contains("listed") ? "amount=1" : "") + ")})\n");
            Assert.That(LoadClean().Disabled, Is.Empty);
        }

        [Test]
        public void AComponentAttachedByAPatchIsFrozenWithItsDefaults()
        {
            Mod("core", schema: Schema, defs: Defs);
            Mod("addon", defs: "patch.set(\"core:Scout\", \"Morale\", Morale())\n", extra: ", dependencies=[\"core\"]");
            var set = LoadClean().Frozen;
            set.TryGet("core:Scout", out var scout);
            Assert.That(scout.Components.TryGet("Morale", out var morale), Is.True);
            morale.Members.TryGet("value", out var value);
            Assert.That(((IntValue)value.Value).Value, Is.EqualTo(3));
            Assert.That(morale.AttachedBy!.Mod, Is.EqualTo("addon"));
        }

        [Test]
        public void PatchedValuesKeepTheirOrigin()
        {
            Mod("core", schema: Schema, defs: Defs);
            Mod("addon", defs: "patch.add(\"core:Scout\", \"move\", 5)\n", extra: ", dependencies=[\"core\"]");
            var set = LoadClean().Frozen;
            set.TryGet("core:Scout", out var scout);
            var move = Const(scout, "move");
            Assert.That(((IntValue)move.Value).Value, Is.EqualTo(7));
            Assert.That((move.Origin, move.Patch!.Mod), Is.EqualTo((ValueOrigin.Patched, "addon")));
        }

        [Test]
        public void TheFrozenSetKeepsTheExpandedSourceForTheDump()
        {
            var result = LoadWithResult();
            result.Frozen.TryGet("core:Scout", out var scout);
            Assert.That(scout.Source, Is.SameAs(result.Expanded.Defs.Single(d => d.Id == "core:Scout")));
            Assert.That(result.Expanded.TryGet("core:Base", out var template), Is.True);
            Assert.That(template.IsAbstract, Is.True);
        }

        private ContentLoadResult LoadWithResult()
        {
            Mod("core", schema: Schema, defs: Defs);
            return LoadClean();
        }

        [Test]
        public void AFailedLoadStillGivesAnEmptyFrozenSetForTheDisabledMod()
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\")\n");
            var result = Load(out var diags);
            Assert.That(diags, Is.Not.Empty);
            Assert.That(result.Frozen.Defs, Is.Empty);
        }
    }
}
