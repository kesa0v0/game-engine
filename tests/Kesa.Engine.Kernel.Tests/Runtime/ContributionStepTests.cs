using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 다른 엔티티에서 오는 선언 보너스 <c>contrib(..., to=)</c> (spec-gaps G300): 선언을 든 엔티티에서 한 걸음,
    /// Ref(<c>"Held.holder"</c>)를 따라가거나 역방향 컬렉션(<c>"units"</c>)으로 간다. 관계가 바뀌면 저절로 빠진다.
    /// </summary>
    public class ContributionStepTests : LoaderTestBase
    {
        private const string Schema =
            "def melee(m):\n" +
            "    return m.base + floor(sum([c.value for c in contribs(m, \"dmg\")]))\n" +
            "component(\"Melee\", base=const(int, default=1), dmg=derived(int, melee))\n" +
            "component(\"Morale\", value=derived(int, lambda m: floor(sum([c.value for c in contribs(m, \"value\")]))))\n" +
            "component(\"Durability\", max=derived(int, lambda d: 10 + floor(sum([c.value for c in contribs(d, \"max\")]))))\n" +
            "component(\"Held\", holder=ref(\"Pawn\", optional=True))\n" +
            "component(\"Home\", faction=ref(\"Faction\", optional=True, inverse=\"units\"))\n" +
            "component(\"Blade\", grants=const(list[Contrib], default=[]))\n" +
            "component(\"Tech\", grants=const(list[Contrib], default=[]))\n" +
            "component(\"Mark\", hit=field(int, default=0))\n" +
            "component(\"Bond\", key=\"Pawn\", with_=ref(\"Pawn\", optional=True))\n" +
            "deftype(\"Pawn\", components=[\"Melee\", \"Morale\", \"Home\", \"Mark\"])\n" +
            "deftype(\"Sword\", components=[\"Held\", \"Blade\", \"Durability\"])\n" +
            "deftype(\"Faction\", components=[\"Tech\"])\n";

        private const string Defs =
            "Sword(\"Longsword\", Blade=Blade(grants=[contrib(\"Melee.dmg\", \"add\", 5, to=\"Held.holder\"),\n" +
            "    contrib(\"Durability.max\", \"add\", 20)]))\n" +
            "Faction(\"Rome\", Tech=Tech(grants=[contrib(\"Morale.value\", \"add\", 1, to=\"units\")]))\n" +
            "Pawn(\"Legionary\")\n" +
            "@when(lambda p: p.Melee.dmg >= 6)\ndef armed(p: Ref[Pawn]):\n    p.Mark.hit = 1\n";

        private World _world = null!;
        private EntityId _pawn, _sword, _rome;

        private void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _pawn = _world.Create("banking:Legionary");
            _sword = _world.Create("banking:Longsword");
            _rome = _world.Create("banking:Rome");
        }

        private long Read(EntityId id, string component, string member) =>
            ((IntValue)_world.Get(id, _world.Member(component, member))).Value;

        private void Hold(EntityId sword, EntityId? pawn) =>
            _world.Set(sword, _world.Member("Held", "holder"), pawn == null ? (Value)NoneValue.Instance : new EntityValue(pawn.Value));

        [Test]
        public void ARefStepGivesTheBonusToWhatTheRefPointsAt()
        {
            NewWorld();
            Assert.That(Read(_pawn, "Melee", "dmg"), Is.EqualTo(1));
            Assert.That(Read(_sword, "Durability", "max"), Is.EqualTo(30), "without to= the sword keeps its own bonus");

            Hold(_sword, _pawn);
            Assert.That(Read(_pawn, "Melee", "dmg"), Is.EqualTo(6));
            Assert.That(Read(_sword, "Durability", "max"), Is.EqualTo(30));
            Assert.That(_world.Explain(_pawn, _world.Member("Melee", "dmg")).Render(), Does.EndWith(
                "  contribution add 5.0 from " + _sword + " (Blade)"));

            var second = _world.Create("banking:Longsword");
            Hold(second, _pawn);
            Assert.That(Read(_pawn, "Melee", "dmg"), Is.EqualTo(11));

            Hold(_sword, null);
            Assert.That(Read(_pawn, "Melee", "dmg"), Is.EqualTo(6), "putting the sword down takes its bonus away");
            _world.Destroy(second);
            Assert.That(Read(_pawn, "Melee", "dmg"), Is.EqualTo(1));
        }

        [Test]
        public void AnInverseStepGivesTheBonusToEveryMember()
        {
            NewWorld();
            var other = _world.Create("banking:Legionary");
            var home = _world.Member("Home", "faction");
            _world.Set(_pawn, home, new EntityValue(_rome));
            _world.Set(other, home, new EntityValue(_rome));
            Assert.That(Read(_pawn, "Morale", "value"), Is.EqualTo(1));
            Assert.That(Read(other, "Morale", "value"), Is.EqualTo(1));
            Assert.That(_world.Explain(_pawn, _world.Member("Morale", "value")).Render(), Does.EndWith(
                "  contribution add 1.0 from " + _rome + " (Tech)"));

            _world.Set(other, home, NoneValue.Instance);
            Assert.That(Read(other, "Morale", "value"), Is.EqualTo(0));
            _world.Destroy(_rome);
            Assert.That(Read(_pawn, "Morale", "value"), Is.EqualTo(0), "the faction is gone");
        }

        [Test]
        public void PickingUpASwordWakesAWhen()
        {
            NewWorld();
            _world.Advance(1);
            Assert.That(Read(_pawn, "Mark", "hit"), Is.EqualTo(0));
            Hold(_sword, _pawn); // 폰의 칸은 건드리지 않았다
            _world.Advance(1);
            Assert.That(Read(_pawn, "Mark", "hit"), Is.EqualTo(1));
        }

        private Diagnostic Broken(string defs)
        {
            Mod("banking", schema: Schema, defs: defs + "\n");
            Load(out var diags);
            return diags.Single();
        }

        [TestCase("Sword(\"X\", Blade=Blade(grants=[contrib(\"Melee.dmg\", \"add\", 1, to=\"Held.holdr\")]))",
            Codes.LnkNotFound, "no ref column 'holdr'", "holder")]
        [TestCase("Sword(\"X\", Blade=Blade(grants=[contrib(\"Melee.dmg\", \"add\", 1, to=\"Hold.holder\")]))",
            Codes.LnkNotFound, "no component 'Hold'", "Held")]
        [TestCase("Sword(\"X\", Blade=Blade(grants=[contrib(\"Melee.dmg\", \"add\", 1, to=\"Bond.with_\")]))",
            Codes.LnkWrongType, "keyed component", "G300")]
        [TestCase("Faction(\"X\", Tech=Tech(grants=[contrib(\"Morale.value\", \"add\", 1, to=\"unit\")]))",
            Codes.LnkNotFound, "no inverse collection 'unit'", "units")]
        [TestCase("Sword(\"X\", Blade=Blade(grants=[contrib(\"Morale.value\", \"add\", 1, to=\"units\")]))",
            Codes.LnkNotFound, "declared on a Sword", "G300")]
        [TestCase("Sword(\"X\", Blade=Blade(grants=[contrib(\"Melee.dmg\", \"add\", 1, to=\"Held.holder.x\")]))",
            Codes.EvlArguments, "to= is", "G300")]
        public void AStepIsCheckedAtLoad(string defs, string code, string message, string hint)
        {
            var d = Broken(defs);
            Assert.That(d.Code, Is.EqualTo(code), d.ToString());
            Assert.That(d.Message, Does.Contain(message));
            Assert.That(d.Hint, Does.Contain(hint));
        }
    }
}
