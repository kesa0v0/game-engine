using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>기여 행과 contribs (spec S4, spec-gaps G98).</summary>
    public class WorldContributionTests : LoaderTestBase
    {
        private const string Schema =
            "def health_max(h):\n" +
            "    flat = 0\n" +
            "    pct = 0\n" +
            "    for r in contribs(h, \"max\"):\n" +
            "        if r.op == \"add\":\n" +
            "            flat = flat + r.value\n" +
            "        elif r.op == \"pct\":\n" +
            "            pct = pct + r.value\n" +
            "    return floor((h.base_max + flat) * (1 + pct / 100))\n" +
            "def who(h):\n" +
            "    for r in contribs(h, \"max\"):\n" +
            "        return r.source\n" +
            "    return None\n" +
            "component(\"Health\", base_max=const(int, default=10), max=derived(int, health_max),\n" +
            "    src=derived(int, lambda h: 1 if who(h) else 0), plain=field(int, default=0))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=1),\n" +
            "    eff=derived(int, lambda s: floor(s.level + contribs(s, \"eff\")[0].value if contribs(s, \"eff\") else s.level)))\n" +
            "component(\"Badcall\", x=derived(int, lambda b: len(contribs(b, \"nope\"))))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Skill\", \"Badcall\"])\n" +
            "component(\"Mark\", n=field(int, default=0))\n" +
            "deftype(\"Wall\", components=[\"Mark\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\n" +
            "Unit(\"Scout\", Health=Health(base_max=12), Skill={\"Cooking\": Skill(level=4)})\n" +
            "Wall(\"Stone\")\n";

        private World _world = null!;

        private EntityId NewScout()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            return _world.Create("banking:Scout");
        }

        private MemberHandle M(string owner, string member) => _world.Member(owner, member);

        private static long Int(Value v) => ((IntValue)v).Value;

        private static WorldException Fails(TestDelegate action, string code)
        {
            var e = Assert.Throws<WorldException>(action)!;
            Assert.That(e.Code, Is.EqualTo(code), e.ToString());
            return e;
        }

        [Test]
        public void ContributionsAreSummedByTheFormula()
        {
            var scout = NewScout();
            var max = M("Health", "max");
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(12));
            _world.AddContribution(scout, max, "add", new IntValue(3));
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(15));
            long pct = _world.AddContribution(scout, max, "pct", new IntValue(10));
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(16)); // 15 * 1.1 = 16.5 → floor
            _world.RemoveContribution(pct);
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(15));
            Fails(() => _world.RemoveContribution(pct), Codes.RunNoRow);
        }

        [Test]
        public void FixedValuesKeepTheirFraction()
        {
            var scout = NewScout();
            var max = M("Health", "max");
            _world.AddContribution(scout, max, "add", new FixedValue(Fixed.FromRaw(2_500_000)));
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(14)); // 12 + 2.5 → floor
        }

        [Test]
        public void OnlyDerivedMembersCanBeTargeted()
        {
            var scout = NewScout();
            Fails(() => _world.AddContribution(scout, M("Health", "plain"), "add", new IntValue(1)), Codes.RunNotWritable);
            Fails(() => _world.AddContribution(scout, M("Health", "base_max"), "add", new IntValue(1)), Codes.RunNotWritable);
            Fails(() => _world.AddContribution(scout, M("Health", "max"), "add", new StrValue("x")), Codes.RunType);
            Fails(() => _world.AddContribution(scout, M("Skill", "eff"), "add", new IntValue(1)), Codes.RunNoRow); // 키 필요
        }

        [Test]
        public void ContributionsDieWithTheirSourceOrTarget()
        {
            var scout = NewScout();
            var max = M("Health", "max");
            var wall = _world.Create("banking:Stone");
            var other = _world.Create("banking:Scout");
            _world.AddContribution(scout, max, "add", new IntValue(5), wall);
            _world.AddContribution(other, max, "add", new IntValue(7), wall);
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(17));

            _world.Destroy(wall); // 출처가 죽으면 두 행 모두 지워진다
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(12));
            Assert.That(Int(_world.Get(other, max)), Is.EqualTo(12));

            var tower = _world.Create("banking:Stone");
            _world.AddContribution(scout, max, "add", new IntValue(1), tower);
            Assert.That(_world.RemoveBySource(tower), Is.EqualTo(1));
            Assert.That(_world.RemoveBySource(tower), Is.EqualTo(0));
            Assert.That(Int(_world.Get(scout, max)), Is.EqualTo(12));
        }

        [Test]
        public void ARowSeesItsSourceAsAnEntity()
        {
            var scout = NewScout();
            Assert.That(Int(_world.Get(scout, M("Health", "src"))), Is.EqualTo(0));
            var wall = _world.Create("banking:Stone");
            _world.AddContribution(scout, M("Health", "max"), "add", new IntValue(1), wall);
            Assert.That(Int(_world.Get(scout, M("Health", "src"))), Is.EqualTo(1));
            _world.AddContribution(scout, M("Health", "max"), "add", new IntValue(1)); // 출처 없음
            _world.RemoveBySource(wall);
            Assert.That(Int(_world.Get(scout, M("Health", "src"))), Is.EqualTo(0)); // 남은 행의 source는 None
        }

        [Test]
        public void KeyedRowContributionsTargetOneRowAndGoWhenTheRowGoes()
        {
            var scout = NewScout();
            var eff = M("Skill", "eff");
            Assert.That(Int(_world.GetRow(scout, eff, "banking:Cooking")), Is.EqualTo(4));
            _world.AddContribution(scout, eff, "banking:Cooking", "add", new IntValue(2));
            Assert.That(Int(_world.GetRow(scout, eff, "banking:Cooking")), Is.EqualTo(6));
        }

        [Test]
        public void ExplainListsTheValuesAndContributionsThatWereActuallyRead()
        {
            var scout = NewScout();
            var wall = _world.Create("banking:Stone");
            var max = M("Health", "max");
            _world.AddContribution(scout, max, "add", new IntValue(3), wall);
            _world.AddContribution(scout, max, "pct", new IntValue(10));

            var node = _world.Explain(scout, max);
            Assert.That(node.Kind, Is.EqualTo(ExplainKind.Derived));
            Assert.That(Int(node.Value), Is.EqualTo(16));
            Assert.That(node.Reads, Has.Count.EqualTo(3)); // 기여 둘, base_max
            Assert.That(node.Reads[0].Kind, Is.EqualTo(ExplainKind.Contribution));
            Assert.That(node.Reads[0].Op, Is.EqualTo("add"));
            Assert.That(node.Reads[0].Source, Is.EqualTo(wall));
            Assert.That(node.Reads[2].Member, Is.EqualTo("base_max"));
            Assert.That(Int(node.Reads[2].Value), Is.EqualTo(12));
            Assert.That(node.Render(), Does.Contain("Health.max of " + scout + " = 16"));
            Assert.That(node.Render(), Does.Contain("  Health.base_max of " + scout + " = 12"));
        }

        [Test]
        public void ExplainNestsDerivedReadsAndDoesNotLeakIntoPlainReads()
        {
            var scout = NewScout();
            var node = _world.Explain(scout, M("Health", "src"));
            Assert.That(node.Reads, Is.Empty); // 기여 행이 없으니 읽은 게 없다
            _world.AddContribution(scout, M("Health", "max"), "add", new IntValue(1));
            Assert.That(_world.Explain(scout, M("Health", "src")).Reads, Has.Count.EqualTo(1));
            // Explain 밖의 평범한 읽기는 기록하지 않는다
            Assert.That(Int(_world.Get(scout, M("Health", "src"))), Is.EqualTo(0));
            Assert.That(_world.Explain(scout, M("Health", "src")).Reads, Has.Count.EqualTo(1));
        }

        [Test]
        public void ExplainOfAKeyedRowNamesTheKey()
        {
            var scout = NewScout();
            var node = _world.Explain(scout, M("Skill", "eff"), "banking:Cooking");
            Assert.That(node.Key, Is.EqualTo("banking:Cooking"));
            Assert.That(Int(node.Value), Is.EqualTo(4));
            Assert.That(node.Render(), Does.Contain("Skill.eff[banking:Cooking]"));
        }

        [Test]
        public void ContribsRejectsAValueThatCannotTakeContributions()
        {
            var scout = NewScout();
            var e = Fails(() => _world.Get(scout, M("Badcall", "x")), Codes.RunScript);
            Assert.That(e.Message, Does.Contain("nope"));
        }
    }
}
