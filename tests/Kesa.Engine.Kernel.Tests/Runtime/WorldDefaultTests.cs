using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>생성 순간에 계산하는 기본값: 함수 기본값과 derived 원천의 default_from (spec-gaps G89·G106).</summary>
    public class WorldDefaultTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10), max=derived(int, lambda h: h.base_max * 2),\n" +
            "    current=field(int, default_from=\"max\", min=0),\n" +
            "    half=field(int, default=lambda h: h.current // 2),\n" +
            "    early=field(int, default=lambda h: h.late),\n" +
            "    late=field(int, default=3))\n" +
            "component(\"Bad\", n=field(int, default=lambda b: 1 / 2))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", base=const(int, default=1),\n" +
            "    level=field(int, default=lambda s: s.base + 4))\n" +
            "component(\"Mark\", n=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Skill\"])\n" +
            "deftype(\"Broken\", components=[\"Mark\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nSkillDef(\"Medicine\")\n" +
            "Unit(\"Scout\", Health=Health(base_max=12), Skill={\"Cooking\": Skill()})\n" +
            "Broken(\"Rock\")\n";

        private World _world = null!;

        private void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
        }

        private MemberHandle M(string owner, string member) => _world.Member(owner, member);
        private static long Int(Value v) => ((IntValue)v).Value;

        [Test]
        public void ADerivedSourceAndFunctionDefaultsAreStoredOnce()
        {
            NewWorld();
            var scout = _world.Create("Scout");
            Assert.That(Int(_world.Get(scout, M("Health", "current"))), Is.EqualTo(24)); // max = 12 * 2
            Assert.That(Int(_world.Get(scout, M("Health", "half"))), Is.EqualTo(12));    // 앞 칸 current를 읽는다
            _world.Set(scout, M("Health", "current"), new IntValue(1));
            Assert.That(Int(_world.Get(scout, M("Health", "half"))), Is.EqualTo(12));    // 저장된 값, 따라가지 않는다
        }

        [Test]
        public void KeyedRowFunctionDefaultsAreComputedWhenTheRowIsCreated()
        {
            NewWorld();
            var scout = _world.Create("Scout");
            Assert.That(Int(_world.GetRow(scout, M("Skill", "level"), "Cooking")), Is.EqualTo(5));
            _world.AddRow(scout, "Skill", "Medicine");
            Assert.That(Int(_world.GetRow(scout, M("Skill", "level"), "Medicine")), Is.EqualTo(5));
        }

        [Test]
        public void ARuntimeAttachedComponentComputesItsDefaultsToo()
        {
            NewWorld();
            var rock = _world.Create("Rock");
            _world.Add(rock, "Health", new Init(M("Health", "current"), new IntValue(8)));
            Assert.That(Int(_world.Get(rock, M("Health", "half"))), Is.EqualTo(4));
        }

        [Test]
        public void AFailingDefaultOnCreateLeavesNoTraceAndGivesTheIdBack()
        {
            Mod("banking", schema:
                "component(\"Z\", n=field(int, default=lambda z: 1 / 2))\n" +
                "deftype(\"Zed\", components=[\"Z\"])\n", defs: "Zed(\"zed\")\n");
            _world = new World(LoadClean());
            var first = _world.Create("zed", new Init(M("Z", "n"), new IntValue(1)));
            var failure = Assert.Throws<WorldException>(() => _world.Create("zed"))!;
            Assert.That(failure.Code, Is.EqualTo(Codes.RunType));
            Assert.That(failure.Message, Does.Contain("default of Z.n"));
            Assert.That(_world.All("Zed").Count, Is.EqualTo(1));
            Assert.That(_world.Create("zed", new Init(M("Z", "n"), new IntValue(2))).Value, Is.EqualTo(first.Value + 1));
        }

        [Test]
        public void AFailingDefaultOnAddRemovesTheComponentAgain()
        {
            Mod("banking", schema:
                "component(\"Bad\", n=field(int, default=lambda b: 1 / 2))\n" +
                "component(\"Mark\", n=field(int, default=0))\n" +
                "deftype(\"Rock\", components=[\"Mark\"])\n", defs: "Rock(\"rock\")\n");
            _world = new World(LoadClean());
            var rock = _world.Create("rock");
            var e = Assert.Throws<WorldException>(() => _world.Add(rock, "Bad"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunType));
            Assert.That(_world.Has(rock, "Bad"), Is.False);
        }

        [Test]
        public void ReadingAMemberDeclaredLaterIsAClearError()
        {
            Mod("banking", schema:
                "component(\"P\", a=field(int, default=lambda p: p.b), b=field(int, default=lambda p: 1))\n" +
                "deftype(\"Pt\", components=[\"P\"])\n", defs: "Pt(\"pt\")\n");
            _world = new World(LoadClean());
            var e = Assert.Throws<WorldException>(() => _world.Create("pt"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunMissingValue));
            Assert.That(e.Message, Does.Contain("P.b").And.Contain("no value yet"));
            Assert.That(_world.All("Pt"), Is.Empty);
        }
    }
}
