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
            // Loop의 b는 리스트로 감싸 엔티티 자신인 줄 모르게 한다: 실행 중 재진입 감지를 본다 (아니면 G229의 로드 오류)
            "component(\"Loop\", a=derived(int, lambda l: l.b), b=derived(int, lambda l: [l][0].a))\n" +
            // oops는 리스트로 감싸 타입을 모르게 한다: 실행 중 안전망을 본다 (아니면 G228의 로드 오류)
            "component(\"Bad\", ratio=derived(int, lambda b: 1 / 2), oops=derived(int, lambda b: [b][0].nope),\n" +
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
            // 깊이 상한(MaxDerivedDepth)에 걸린 것이 아니라 바로 순환으로 잡혀 그 고리만 보인다
            Assert.That(e.Message, Does.Contain("Loop.a of " + scout + " reads itself: Loop.a of " + scout + " -> Loop.b of " +
                                                scout + " -> Loop.a of " + scout));
        }

        // 최상위 읽기 하나에 예산 하나: 안에서 읽은 derived는 남은 단계를 이어서 쓴다 (G101)
        private const string WorkSchema =
            "def spin(w) -> int:\n    t = 0\n    for i in range(20):\n        t += 1\n    return t\n" +
            "def gather(w) -> int:\n    s = 0\n    for k in range(10):\n        s += w.inner\n    return s\n" +
            "component(\"Work\", inner=derived(int, spin), outer=derived(int, gather))\n" +
            "deftype(\"Job\", components=[\"Work\"])\n";

        [Test]
        public void NestedDerivedReadsShareTheBudgetOfTheOutermostRead()
        {
            Mod("banking", schema: WorkSchema, defs: "Job(\"Dig\")\n");
            var loaded = LoadClean();

            var roomy = new World(loaded);
            Assert.That(Int(roomy.Get(roomy.Create("Dig"), roomy.Member("Work", "outer"))), Is.EqualTo(200));

            // 안쪽 계산 하나는 300단계 안에 들지만 그것을 열 번 읽는 바깥 읽기는 넘는다
            _world = new World(loaded, stepBudget: 300);
            var dig = _world.Create("Dig");
            Assert.That(Int(_world.Get(dig, M("Work", "inner"))), Is.EqualTo(20));
            var e = Fails(() => _world.Get(dig, M("Work", "outer")), Codes.RunBudget);
            Assert.That(e.Message, Does.Contain("Work.outer"));

            // 실패한 읽기의 계량기는 다음 읽기로 넘어가지 않는다
            Assert.That(Int(_world.Get(dig, M("Work", "inner"))), Is.EqualTo(20));
        }

        [Test]
        public void EveryOutermostReadStartsWithTheWholeBudget()
        {
            Mod("banking", schema: WorkSchema, defs: "Job(\"Dig\")\n");
            // 계량기는 읽기마다 비워서 다시 쓴다: 앞 읽기들의 단계가 쌓이면 몇 번째 읽기에서 넘는다
            _world = new World(LoadClean(), stepBudget: 300);
            var dig = _world.Create("Dig");
            for (int i = 0; i < 30; i++) Assert.That(Int(_world.Get(dig, M("Work", "inner"))), Is.EqualTo(20));
        }

        // 다른 derived를 읽다가 난 오류는 안쪽 함수 → 읽은 값 → 바깥 함수(읽은 위치)로 흔적이 이어진다
        private const string ChainSchema =
            "def boom(w) -> int:\n    return 1 // w.zero\n" +
            "def gather(w) -> int:\n    s = 0\n    s += w.inner\n    return s\n" +
            "component(\"Chain\", zero=field(int, default=0), inner=derived(int, boom), outer=derived(int, gather),\n" +
            "    half=derived(int, lambda c: 1 / 2), plus=derived(int, lambda c: c.half + 1))\n" +
            "deftype(\"Job\", components=[\"Chain\"])\n";

        [Test]
        public void AnErrorInANestedDerivedKeepsTheWholeChain()
        {
            Mod("banking", schema: ChainSchema, defs: "Job(\"Dig\")\n");
            _world = new World(LoadClean());
            var dig = _world.Create("Dig");
            var e = Fails(() => _world.Get(dig, M("Chain", "outer")), Codes.RunArithmetic);
            var lines = e.Message.Split('\n');
            Assert.That(lines[0], Does.StartWith("computing Chain.outer of " + dig + ": division by zero (at "));
            Assert.That(lines[0], Does.Contain(":2")); // boom의 줄
            Assert.That(lines[1], Does.StartWith("  in boom"));
            Assert.That(lines[2], Is.EqualTo("  reading Chain.inner of " + dig));
            Assert.That(lines[3], Does.StartWith("  in gather at ").And.Contain(":5")); // gather가 w.inner를 읽은 줄
            Assert.That(lines, Has.Length.EqualTo(4), e.Message);
        }

        [Test]
        public void AFailedReadLeavesNothingForTheNextRead()
        {
            Mod("banking", schema: ChainSchema, defs: "Job(\"Dig\")\n");
            _world = new World(LoadClean());
            var dig = _world.Create("Dig");
            // 실행 세션은 다시 쓴다: 실패한 읽기의 프레임이 남으면 다음 읽기가 같은 함수를 "재귀"로 보거나 흔적이 섞인다
            for (int i = 0; i < 3; i++)
            {
                var e = Fails(() => _world.Get(dig, M("Chain", "outer")), Codes.RunArithmetic);
                Assert.That(e.Message.Split('\n'), Has.Length.EqualTo(4), e.Message);
            }
            _world.Set(dig, M("Chain", "zero"), new IntValue(1));
            Assert.That(Int(_world.Get(dig, M("Chain", "outer"))), Is.EqualTo(1));
        }

        [Test]
        public void ATypeErrorOfANestedDerivedPointsAtTheReadInTheOuterFunction()
        {
            Mod("banking", schema: ChainSchema, defs: "Job(\"Dig\")\n");
            _world = new World(LoadClean());
            var dig = _world.Create("Dig");
            var e = Fails(() => _world.Get(dig, M("Chain", "plus")), Codes.RunType);
            var lines = e.Message.Split('\n');
            Assert.That(lines[0], Does.Contain("'Chain.half' is declared int").And.Contain(":8")); // plus의 람다가 c.half를 읽은 줄
            Assert.That(lines[1], Is.EqualTo("  reading Chain.half of " + dig));
            Assert.That(lines[2], Does.StartWith("  in <lambda>"));
        }

        // 키 컴포넌트의 줄 묶음은 파이썬 dict처럼: len은 줄 수, in은 키의 줄이 있는가, 비면 거짓 (G104)
        private const string RowsSchema =
            "component(\"Need\", key=\"NeedDef\", keys=\"listed\", met=field(bool, default=False))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=0))\n" +
            "component(\"Opinion\", key=Ref[\"Unit\"], score=field(int, default=0))\n" +
            "component(\"Q\",\n" +
            "    needs=derived(int, lambda q: len(q.Need)),\n" +
            "    any_need=derived(int, lambda q: 1 if q.Need else 0),\n" +
            "    has_food=derived(int, lambda q: 1 if \"Food\" in q.Need else 0),\n" +
            "    skills=derived(int, lambda q: len(q.Skill)),\n" +
            "    has_cook=derived(int, lambda q: 1 if \"Cooking\" in q.Skill and \"Mining\" in q.Skill else 0),\n" +
            "    opinions=derived(int, lambda q: len(q.Opinion)),\n" +
            "    knows_self=derived(int, lambda q: 1 if q.entity in q.Opinion else 0),\n" +
            "    typo=derived(int, lambda q: 1 if \"Fod\" in q.Need else 0),\n" +
            "    wrong_kind=derived(int, lambda q: 1 if 3 in q.Need else 0))\n" +
            "deftype(\"NeedDef\")\ndeftype(\"SkillDef\")\n" +
            "deftype(\"Unit\", components=[\"Need\", \"Skill\", \"Opinion\", \"Q\"])\n";

        [Test]
        public void KeyedRowsActLikeADictOfRows()
        {
            Mod("banking", schema: RowsSchema,
                defs: "NeedDef(\"Food\")\nNeedDef(\"Rest\")\nSkillDef(\"Cooking\")\nSkillDef(\"Mining\")\nUnit(\"Scout\")\n");
            _world = new World(LoadClean());
            var scout = _world.Create("Scout");
            long Q(string member) => Int(_world.Get(scout, M("Q", member)));

            // keys="listed": 줄이 없으면 0개이고 거짓
            Assert.That(new[] { Q("needs"), Q("any_need"), Q("has_food") }, Is.EqualTo(new long[] { 0, 0, 0 }));
            _world.AddRow(scout, "Need", "Food");
            Assert.That(new[] { Q("needs"), Q("any_need"), Q("has_food") }, Is.EqualTo(new long[] { 1, 1, 1 }));

            // keys="all": 키 타입의 모든 Def가 줄이다
            Assert.That(new[] { Q("skills"), Q("has_cook") }, Is.EqualTo(new long[] { 2, 1 }));

            // 엔티티 키
            Assert.That(new[] { Q("opinions"), Q("knows_self") }, Is.EqualTo(new long[] { 0, 0 }));
            _world.AddRow(scout, "Opinion", scout);
            Assert.That(new[] { Q("opinions"), Q("knows_self") }, Is.EqualTo(new long[] { 1, 1 }));

            // 없는 Def id는 조용한 False가 아니라 h.Need["Fod"]와 같은 "혹시 …?" 오류, 키 종류가 틀리면 타입 오류
            Assert.That(Fails(() => Q("typo"), Codes.RunUnknownName).Hint, Is.EqualTo("did you mean 'Food'?"));
            Fails(() => Q("wrong_kind"), Codes.RunType);
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
