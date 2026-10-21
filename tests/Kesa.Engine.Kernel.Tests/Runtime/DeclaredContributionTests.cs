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
    /// 선언 보너스 (spec-gaps G295~G298): <c>const(list[Contrib])</c> 칸은 붙은 컴포넌트와 키 줄의 키 Def에서 모여
    /// <c>contribs()</c>에 저장된 줄 뒤로 들어온다. 값이 Def에 있으므로 패치가 반영되고 로드 때 대상을 검사한다.
    /// </summary>
    public class DeclaredContributionTests : LoaderTestBase
    {
        private const string Schema =
            "def health_max(h):\n" +
            "    return h.base + floor(sum([c.value for c in contribs(h, \"max\")]))\n" +
            "def from_tough(h):\n" +
            "    return len([c for c in contribs(h, \"max\") if c.component == \"Traits\" and c.key == \"banking:Tough\" and c.id is None])\n" +
            "def stored(h):\n" +
            "    return len([c for c in contribs(h, \"max\") if c.component is None and c.key is None and c.id is not None])\n" +
            "component(\"Health\", base=const(int, default=10), max=derived(int, health_max),\n" +
            "    tough=derived(int, from_tough), stored=derived(int, stored), hit=field(int, default=0))\n" +
            "component(\"Armor\", bonus=const(list[Contrib], default=[contrib(\"Health.max\", \"add\", 1)]))\n" +
            "component(\"Fortified\", bonus=const(list[Contrib], default=[contrib(\"Health.max\", \"add\", 5)]))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=1),\n" +
            "    eff=derived(int, lambda s: s.level + floor(sum([c.value for c in contribs(s, \"eff\")]))))\n" +
            "component(\"Traits\", key=\"Trait\", keys=\"listed\", n=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Trait\", grants=const(list[Contrib], default=[]))\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Armor\", \"Skill\", \"Traits\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nSkillDef(\"Medicine\")\n" +
            "Trait(\"Tough\", grants=[contrib(\"Health.max\", \"add\", 3), contrib(\"Skill.eff\", \"add\", 2, key=\"Medicine\")])\n" +
            "Trait(\"Lazy\")\n" +
            "Unit(\"Scout\", Armor=Armor(bonus=[contrib(\"Health.max\", \"add\", 2)]), Traits={\"Tough\": Traits()})\n" +
            "Unit(\"Plain\")\n" +
            "@when(lambda u: u.Health.max >= 16)\ndef strong(u: Ref[Unit]):\n    u.Health.hit = 1\n" +
            "@command(free=True, acts_for=None)\ndef Hold(u: Ref[Unit]):\n" +
            "    c = contribs(u.Health, \"max\")[1]\n    wait(ticks=2)\n" +
            "    u.Health.hit = floor(c.value) + (10 if c.key == \"banking:Tough\" and c.id is None else 0)\n";

        private World _world = null!;
        private EntityId _scout, _plain;

        private void NewWorld(string defs = Defs)
        {
            Mod("banking", schema: Schema, defs: defs);
            _world = new World(LoadClean());
            _scout = _world.Create("banking:Scout");
            _plain = _world.Create("banking:Plain");
        }

        private long Read(EntityId id, string component, string member) =>
            ((IntValue)_world.Get(id, _world.Member(component, member))).Value;

        private long Eff(EntityId id, string skill) =>
            ((IntValue)_world.GetRow(id, _world.Member("Skill", "eff"), skill)).Value;

        [Test]
        public void ComponentsAndKeyDefsDeclareBonuses()
        {
            NewWorld();
            Assert.That(Read(_scout, "Health", "max"), Is.EqualTo(15), "10 + Armor 2 (the Def's value) + Tough 3");
            Assert.That(Read(_plain, "Health", "max"), Is.EqualTo(11), "10 + Armor 1 (the schema default)");
            Assert.That(Eff(_scout, "banking:Medicine"), Is.EqualTo(3), "Tough targets the Medicine row only");
            Assert.That(Eff(_scout, "banking:Cooking"), Is.EqualTo(1));
            Assert.That(Read(_scout, "Health", "tough"), Is.EqualTo(1), "c.component, c.key and c.id of a declared line");
        }

        [Test]
        public void StoredRowsComeFirstAndKeepNoComponentOrKey()
        {
            NewWorld();
            _world.AddContribution(_scout, _world.Member("Health", "max"), "add", new IntValue(4));
            Assert.That(Read(_scout, "Health", "max"), Is.EqualTo(19));
            Assert.That(Read(_scout, "Health", "stored"), Is.EqualTo(1));
            Assert.That(_world.Explain(_scout, _world.Member("Health", "max")).Render(), Does.Contain(
                "  contribution add 4.0\n" +
                "  contribution add 2.0 from " + _scout + " (Armor)\n" +
                "  contribution add 3.0 from " + _scout + " (Traits[banking:Tough])"));
        }

        [Test]
        public void APatchChangesTheBonusEvenForEntitiesThatAlreadyExist()
        {
            Mod("scouts", defs: "patch.append(\"banking:Tough\", \"grants\", [contrib(\"Health.max\", \"add\", 4)])\n",
                extra: ", dependencies=[\"banking\"]");
            NewWorld();
            Assert.That(Read(_scout, "Health", "max"), Is.EqualTo(19));
        }

        [Test]
        public void AttachingAComponentOrARowBringsItsBonusAndDetachingTakesItAway()
        {
            NewWorld();
            _world.Add(_scout, "Fortified");
            Assert.That(Read(_scout, "Health", "max"), Is.EqualTo(20));
            _world.Remove(_scout, "Fortified");
            Assert.That(Read(_scout, "Health", "max"), Is.EqualTo(15));

            _world.AddRow(_plain, "Traits", "banking:Lazy");
            Assert.That(Read(_plain, "Health", "max"), Is.EqualTo(11), "Lazy declares nothing");
            _world.AddRow(_plain, "Traits", "banking:Tough");
            Assert.That(Read(_plain, "Health", "max"), Is.EqualTo(14));
            _world.RemoveRow(_plain, "Traits", "banking:Tough");
            Assert.That(Read(_plain, "Health", "max"), Is.EqualTo(11));
        }

        [Test]
        public void AttachingADeclaringComponentWakesAWhen()
        {
            NewWorld();
            _world.Advance(1);
            Assert.That(Read(_scout, "Health", "hit"), Is.EqualTo(0));
            _world.Add(_scout, "Fortified"); // Health는 건드리지 않았다
            _world.Advance(1);
            Assert.That(Read(_scout, "Health", "hit"), Is.EqualTo(1));
            Assert.That(Read(_plain, "Health", "hit"), Is.EqualTo(0));
        }

        [Test]
        public void AViewerWhoCannotSeeTheDeclaringComponentDoesNotSeeItsLine()
        {
            NewWorld(Defs + "@view_policy\ndef may_see(viewer, entity, component):\n    return component != \"Armor\"\n");
            _world.Seat(_plain, "A");
            var a = _world.ViewAs("A");
            var max = _world.Member("Health", "max");
            Assert.That(((IntValue)a.Get(_scout, max)).Value, Is.EqualTo(15), "the value itself is computed from the real state (G199)");
            Assert.That(a.Explain(_scout, max).Render(), Is.EqualTo(
                "Health.max of " + _scout + " = 15\n" +
                "  Health.base of " + _scout + " = 10\n" +
                "  contribution add 3.0 from " + _scout + " (Traits[banking:Tough])\n" +
                "  (hidden)"));
            Assert.That(a.Diagnostics, Is.Empty);
        }

        [Test]
        public void APausedFlowHoldingADeclaredLineSavesAndLoads()
        {
            Mod("banking", schema: Schema, defs: Defs);
            var content = LoadClean();
            _world = new World(content);
            _scout = _world.Create("banking:Scout");
            Assert.That(_world.Submit("host", "banking.Hold",
                new Dictionary<string, Value> { ["u"] = new EntityValue(_scout) }).Succeeded, Is.True);
            var loaded = World.Load(content, _world.Save());
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            _world.Advance(2);
            Assert.That(Read(_scout, "Health", "hit"), Is.EqualTo(13), "the Tough line keeps its value and key across the save");
        }

        private IReadOnlyList<Diagnostic> Broken(string defs, string schema = Schema)
        {
            Mod("banking", schema: schema, defs: defs);
            Load(out var diags);
            return diags;
        }

        [TestCase("Unit(\"X\", Armor=Armor(bonus=[contrib(\"Helth.max\", \"add\", 1)]))", Codes.LnkNotFound, "no component 'Helth'")]
        [TestCase("Unit(\"X\", Armor=Armor(bonus=[contrib(\"Health.base\", \"add\", 1)]))", Codes.LnkWrongType, "not a derived value")]
        [TestCase("Unit(\"X\", Armor=Armor(bonus=[contrib(\"Health.mx\", \"add\", 1)]))", Codes.LnkNotFound, "no derived value 'mx'")]
        [TestCase("Trait(\"X\", grants=[contrib(\"Skill.eff\", \"add\", 1)])", Codes.LnkWrongType, "key=")]
        [TestCase("Trait(\"X\", grants=[contrib(\"Health.max\", \"add\", 1, key=\"Cooking\")])", Codes.LnkWrongType, "not a keyed component")]
        [TestCase("Trait(\"X\", grants=[contrib(\"Skill.eff\", \"add\", 1, key=\"Baking\")])", null, "Baking")]
        public void ATargetIsCheckedAtLoad(string defs, string? code, string message)
        {
            var d = Broken("SkillDef(\"Cooking\")\n" + defs + "\n").Single();
            if (code != null) Assert.That(d.Code, Is.EqualTo(code), d.ToString());
            Assert.That(d.Message, Does.Contain(message));
        }

        [TestCase("component(\"Bad\", b=field(list[Contrib]))\n", "only be const")]
        [TestCase("component(\"Bad\", b=const(Contrib))\n", "only from const(list[Contrib])")]
        [TestCase("component(\"Bad\", key=\"SkillDef\", b=const(list[Contrib], default=[]))\n", "keyed component")]
        public void ContribSlotsHaveOneShape(string extra, string message)
        {
            var d = Broken("", Schema + extra).Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchBadDeclaration), d.ToString());
            Assert.That(d.Message, Does.Contain(message));
        }

        [Test]
        public void AKeyedTargetsKeyMustBeADefType()
        {
            var d = Broken("", Schema +
                "component(\"Bond\", key=int, w=derived(int, lambda b: 0))\n" +
                "component(\"Bad\", b=const(list[Contrib], default=[contrib(\"Bond.w\", \"add\", 1)]))\n").Single();
            Assert.That(d.Code, Is.EqualTo(Codes.LnkWrongType), d.ToString());
            Assert.That(d.Message, Does.Contain("Bond"));
        }
    }
}
