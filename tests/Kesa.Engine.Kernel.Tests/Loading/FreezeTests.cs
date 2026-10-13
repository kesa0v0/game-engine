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
            "Unit(\"Scout\", parent=\"Base\", tags=[\"a\"], Health=Health(base_max=12, current=9))\n";

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
            Assert.That(set.Defs.Select(d => d.Id), Is.EqualTo(new[] { "Wood", "Scout" }));
            Assert.That(set.TryGet("Base", out _), Is.False);
            Assert.That(set.TryGet("Scout", out var scout), Is.True);
            Assert.That(scout.ParentId, Is.EqualTo("Base"));
            Assert.That(set.OfType("Unit").Select(d => d.Id), Is.EqualTo(new[] { "Scout" }));
            Assert.That(set.OfType("Resource").Select(d => d.Id), Is.EqualTo(new[] { "Wood" }));
        }

        [Test]
        public void ConstsAreFilledWithDefaultsAndMarkedAsSuch()
        {
            var set = Frozen();
            set.TryGet("Scout", out var scout);

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
            Frozen().TryGet("Scout", out var scout);
            var costs = Const(scout, "costs");
            Assert.That(((DictValue)costs.Value).Items, Is.Empty);
            Assert.That(costs.Origin, Is.EqualTo(ValueOrigin.Default));
        }

        [Test]
        public void ComponentsOfTheTypeAreAlwaysThereWithTheirConstsFilled()
        {
            Frozen().TryGet("Scout", out var scout);
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
            Frozen().TryGet("Scout", out var scout);
            scout.Components.TryGet("Health", out var health);
            Assert.That(health.Members.Contains("current"), Is.True);

            var other = Frozen(Defs + "Unit(\"Archer\", parent=\"Base\")\n");
            other.TryGet("Archer", out var archer);
            archer.Components.TryGet("Health", out var archerHealth);
            Assert.That(archerHealth.Members.Contains("current"), Is.False);
            Assert.That(archerHealth.Members.Contains("base_max"), Is.True);
        }

        [Test]
        public void KeyedRowsAreFilledButNotInvented()
        {
            Mod("core", schema: Schema, defs: Defs + "Unit(\"Archer\", parent=\"Base\", Skill={\"Wood\": Skill()})\n");
            var set = LoadClean().Frozen;
            set.TryGet("Archer", out var archer);
            archer.Components.TryGet("Skill", out var skill);
            Assert.That(skill.Rows.Keys, Is.EqualTo(new[] { "Wood" }));
            skill.Rows.TryGet("Wood", out var row);
            row.Members.TryGet("level", out var level);
            Assert.That((((IntValue)level.Value).Value, level.Origin), Is.EqualTo((1L, ValueOrigin.Default)));

            set.TryGet("Scout", out var scout);
            scout.Components.TryGet("Skill", out var none);
            Assert.That(none.Rows.Keys, Is.Empty);
        }

        [Test]
        public void AComponentAttachedByAPatchIsFrozenWithItsDefaults()
        {
            Mod("core", schema: Schema, defs: Defs);
            Mod("addon", defs: "patch.set(\"Scout\", \"Morale\", Morale())\n", extra: ", dependencies=[\"core\"]");
            var set = LoadClean().Frozen;
            set.TryGet("Scout", out var scout);
            Assert.That(scout.Components.TryGet("Morale", out var morale), Is.True);
            morale.Members.TryGet("value", out var value);
            Assert.That(((IntValue)value.Value).Value, Is.EqualTo(3));
            Assert.That(morale.AttachedBy!.Mod, Is.EqualTo("addon"));
        }

        [Test]
        public void PatchedValuesKeepTheirOrigin()
        {
            Mod("core", schema: Schema, defs: Defs);
            Mod("addon", defs: "patch.add(\"Scout\", \"move\", 5)\n", extra: ", dependencies=[\"core\"]");
            var set = LoadClean().Frozen;
            set.TryGet("Scout", out var scout);
            var move = Const(scout, "move");
            Assert.That(((IntValue)move.Value).Value, Is.EqualTo(7));
            Assert.That((move.Origin, move.Patch!.Mod), Is.EqualTo((ValueOrigin.Patched, "addon")));
        }

        [Test]
        public void TheFrozenSetKeepsTheExpandedSourceForTheDump()
        {
            var result = LoadWithResult();
            result.Frozen.TryGet("Scout", out var scout);
            Assert.That(scout.Source, Is.SameAs(result.Expanded.Defs.Single(d => d.Id == "Scout")));
            Assert.That(result.Expanded.TryGet("Base", out var template), Is.True);
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
