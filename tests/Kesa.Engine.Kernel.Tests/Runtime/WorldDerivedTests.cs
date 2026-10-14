using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>월드의 derived 읽기 (spec S4, spec-gaps G100·G103).</summary>
    public class WorldDerivedTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10),\n" +
            "    max=derived(int, lambda h: h.base_max + h.Armor.bonus),\n" +
            "    current=field(int, default=5))\n" +
            "component(\"Armor\", bonus=field(int, default=2))\n" +
            "component(\"Aura\", power=field(int, default=1))\n" +
            "component(\"Mood\", base=field(int, default=3),\n" +
            "    shown=derived(int, lambda m: m.base + (m.Aura.power if m.Aura else 100)))\n" +
            "component(\"Ally\", friend=ref(\"Banner\", optional=True),\n" +
            "    friend_hp=derived(int, lambda a: a.friend.Health.current if a.friend else -1))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=0),\n" +
            "    eff=derived(int, lambda s: s.level + s.entity.Health.base_max),\n" +
            "    label=derived(str, lambda s: s.key))\n" +
            "def total_levels(t):\n" +
            "    total = 0\n" +
            "    for k in t.Skill:\n" +
            "        total = total + t.Skill[k].level\n" +
            "    return total\n" +
            "component(\"Total\", value=derived(int, total_levels))\n" +
            "component(\"Loop\", a=derived(int, lambda l: l.b), b=derived(int, lambda l: l.a))\n" +
            "component(\"Bad\", ratio=derived(int, lambda b: 1 / 2), oops=derived(int, lambda b: b.nope),\n" +
            "    text=derived(int, lambda b: \"x\"), whole=derived(fixed, lambda b: 3))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Banner\", components=[\"Health\"])\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Armor\", \"Mood\", \"Ally\", \"Skill\", \"Total\", \"Loop\", \"Bad\"])\n";

        private const string Defs =
            "SkillDef(\"Medicine\")\nSkillDef(\"Cooking\")\n" +
            "Banner(\"Flag\", Health=Health(base_max=40))\n" +
            "Unit(\"Scout\", Health=Health(base_max=12), Skill={\"Cooking\": Skill(level=4), \"Medicine\": Skill(level=1)})\n";

        private World _world = null!;

        private EntityId NewScout()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            return _world.Create("Scout");
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
        public void ReadsOwnMembersAndSiblingComponents()
        {
            var scout = NewScout();
            Assert.That(Int(_world.Get(scout, M("Health", "max"))), Is.EqualTo(14)); // 12 + Armor.bonus 2
            _world.Set(scout, M("Armor", "bonus"), new IntValue(5));
            Assert.That(Int(_world.Get(scout, M("Health", "max"))), Is.EqualTo(17)); // 매번 새로 계산한다
        }

        [Test]
        public void ARuntimeAttachedSiblingThatIsMissingReadsAsNone()
        {
            var scout = NewScout();
            Assert.That(Int(_world.Get(scout, M("Mood", "shown"))), Is.EqualTo(103)); // Aura 없음 → 100
            _world.Add(scout, "Aura");
            Assert.That(Int(_world.Get(scout, M("Mood", "shown"))), Is.EqualTo(4));
            _world.Remove(scout, "Aura");
            Assert.That(Int(_world.Get(scout, M("Mood", "shown"))), Is.EqualTo(103));
        }

        [Test]
        public void FollowsARefToAnotherEntity()
        {
            var scout = NewScout();
            Assert.That(Int(_world.Get(scout, M("Ally", "friend_hp"))), Is.EqualTo(-1)); // 빈 Ref는 None
            var flag = _world.Create("Flag");
            _world.Set(scout, M("Ally", "friend"), new EntityValue(flag));
            Assert.That(Int(_world.Get(scout, M("Ally", "friend_hp"))), Is.EqualTo(5));
        }

        [Test]
        public void KeyedRowsGetTheirOwnRowAndCanReachTheEntityAndKey()
        {
            var scout = NewScout();
            Assert.That(Int(_world.GetRow(scout, M("Skill", "eff"), "Cooking")), Is.EqualTo(16)); // 4 + 12
            Assert.That(Int(_world.GetRow(scout, M("Skill", "eff"), "Medicine")), Is.EqualTo(13));
            Assert.That(((StrValue)_world.GetRow(scout, M("Skill", "label"), "Cooking")).Value, Is.EqualTo("Cooking"));
        }

        [Test]
        public void AFunctionCanIterateTheRowsOfAKeyedComponent()
        {
            var scout = NewScout();
            Assert.That(Int(_world.Get(scout, M("Total", "value"))), Is.EqualTo(5));
        }

        [Test]
        public void ADerivedThatReadsItselfIsACycle()
        {
            var scout = NewScout();
            var e = Fails(() => _world.Get(scout, M("Loop", "a")), Codes.RunCycle);
            Assert.That(e.Message, Does.Contain("Loop.a").And.Contain("Loop.b").And.Contain("->"));
        }

        [Test]
        public void ScriptErrorsBecomeWorldExceptionsWithTheirCode()
        {
            var scout = NewScout();
            var e = Fails(() => _world.Get(scout, M("Bad", "oops")), Codes.RunScript);
            Assert.That(e.Message, Does.Contain("Bad.oops").And.Contain("nope"));
        }

        [Test]
        public void TheReturnValueMustMatchTheDeclaredType()
        {
            var scout = NewScout();
            var e = Fails(() => _world.Get(scout, M("Bad", "ratio")), Codes.RunType); // 1 / 2 는 fixed
            Assert.That(e.Hint, Does.Contain("floor"));
            Fails(() => _world.Get(scout, M("Bad", "text")), Codes.RunType);
            // int는 fixed로 올려 준다
            Assert.That(((FixedValue)_world.Get(scout, M("Bad", "whole"))).Value, Is.EqualTo(Fixed.FromRaw(3_000_000)));
        }

        [Test]
        public void DeadEntitiesStillFailWithTheWorldApiCodes()
        {
            var scout = NewScout();
            _world.Destroy(scout);
            Fails(() => _world.Get(scout, M("Health", "max")), Codes.RunDeadEntity);
        }
    }
}
