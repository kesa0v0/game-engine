using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 스크립트의 순회 (spec-gaps G88·G194): 엔진이 준 묶음(역방향 컬렉션, <c>query</c>, 키 줄)은 시작 시점의 목록을 돌며 그사이 죽은
    /// 엔티티와 뗀 줄을 건너뛰고, <c>len</c>·<c>in</c>은 지금 살아 있는 것만 본다(파이썬 <c>weakref.WeakSet</c>처럼). 스크립트가 만든
    /// 리스트는 파이썬 그대로다.
    /// </summary>
    public class WorldIterationTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Home\", city=ref(\"City\", optional=True, inverse=\"units\"), n=field(int, default=0))\n" +
            "component(\"Tally\", n=field(int, default=0), s=field(str, default=\"\"))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", level=field(int, default=0))\n" +
            "component(\"Fond\", key=Ref[\"Unit\"], score=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"City\", components=[\"Tally\"])\n" +
            "deftype(\"Unit\", components=[\"Home\", \"Skill\", \"Fond\"])\n";

        private const string Defs =
            "SkillDef(\"Ride\")\nSkillDef(\"Swim\")\nSkillDef(\"Sail\")\nCity(\"Town\")\nUnit(\"Scout\")\n" +
            "def second(group):\n    return [u for u in group][1]\n";

        private World _w = null!;
        private EntityId _city;
        private readonly List<EntityId> _units = new List<EntityId>();

        private void Make(string defs)
        {
            Mod("core", schema: Schema, defs: Defs + defs);
            _w = new World(LoadClean());
            _city = _w.Create("core:Town");
            _w.Seat(_city, "host");
            _units.Clear();
            for (int i = 0; i < 3; i++)
            {
                var u = _w.Create("core:Scout");
                _w.Seat(u, "host");
                _w.Set(u, _w.Member("Home", "city"), new EntityValue(_city));
                _units.Add(u);
            }
        }

        private SubmitResult Run(string command, params (string, Value)[] args) =>
            _w.Submit("host", "core." + command, args.ToDictionary(a => a.Item1, a => a.Item2));

        private SubmitResult RunOnCity(string command) => Run(command, ("c", new EntityValue(_city)));

        private long Tally() => ((IntValue)_w.Get(_city, _w.Member("Tally", "n"))).Value;
        private string Log() => ((StrValue)_w.Get(_city, _w.Member("Tally", "s"))).Value;

        private static void Ok(SubmitResult r) => Assert.That(r.Succeeded, Is.True, r.Error?.Message);

        // ---- 반복 중 죽은 것은 건너뛴다 ----

        [Test]
        public void ALoopOverAnInverseCollectionSkipsWhatDiesOnTheWay()
        {
            Make("@command(free=True, acts_for=None)\ndef Purge(c: Ref[City]):\n" +
                 "    first = True\n" +
                 "    for u in c.units:\n" +
                 "        if first:\n            destroy(second(c.units))\n            first = False\n" +
                 "        u.Home.n = 5\n" +
                 "        c.Tally.n += 1\n");
            Ok(RunOnCity("Purge"));
            Assert.That(Tally(), Is.EqualTo(2), "the second unit died before its turn and was skipped");
            Assert.That(_w.IsAlive(_units[1]), Is.False);
            Assert.That(((IntValue)_w.Get(_units[2], _w.Member("Home", "n"))).Value, Is.EqualTo(5));
        }

        [Test]
        public void ALoopOverAQuerySkipsWhatDiesOnTheWay()
        {
            Make("@command(free=True, acts_for=None)\ndef Cull(c: Ref[City]):\n" +
                 "    for u in query(Unit):\n" +
                 "        c.Tally.n += 1\n" +
                 "        for v in query(Unit):\n" +
                 "            if v != u:\n                destroy(v)\n");
            Ok(RunOnCity("Cull"));
            Assert.That(Tally(), Is.EqualTo(1), "the first unit destroys the rest, so the outer loop runs once");
        }

        [Test]
        public void ALoopOverKeyedRowsSkipsRowsRemovedOnTheWay()
        {
            Make("@command(free=True, acts_for=None)\ndef Forget(u: Ref[Unit], c: Ref[City]):\n" +
                 "    add_row(u.Skill, \"Ride\", Skill(level=1))\n" +
                 "    add_row(u.Skill, \"Swim\", Skill(level=20))\n" +
                 "    add_row(u.Skill, \"Sail\", Skill(level=300))\n" +
                 "    first = True\n" +
                 "    for k in u.Skill:\n" +
                 "        if first:\n            remove_row(u.Skill, \"Swim\")\n            first = False\n" +
                 "        c.Tally.n += u.Skill[k].level\n");
            Ok(Run("Forget", ("u", new EntityValue(_units[0])), ("c", new EntityValue(_city))));
            Assert.That(Tally(), Is.EqualTo(301));
        }

        [Test]
        public void ALoopOverEntityKeyedRowsSkipsKeysThatDie()
        {
            Make("@command(free=True, acts_for=None)\ndef Grieve(u: Ref[Unit], c: Ref[City]):\n" +
                 "    for v in c.units:\n" +
                 "        if v != u:\n            add_row(u.Fond, v, Fond(score=10))\n" +
                 "    first = True\n" +
                 "    for v in u.Fond:\n" +
                 "        if first:\n            destroy([w for w in c.units][2])\n            first = False\n" +
                 "        c.Tally.n += u.Fond[v].score\n");
            Ok(Run("Grieve", ("u", new EntityValue(_units[0])), ("c", new EntityValue(_city))));
            Assert.That(Tally(), Is.EqualTo(10), "the row keyed by the destroyed unit went with it");
        }

        // ---- len과 in은 지금 살아 있는 것만 ----

        [Test]
        public void LenAndInSeeOnlyTheLiving()
        {
            Make("@command(free=True, acts_for=None)\ndef Count(c: Ref[City]):\n" +
                 "    us = c.units\n" +
                 "    v = second(us)\n" +
                 "    before = len(us)\n" +
                 "    destroy(v)\n" +
                 "    c.Tally.n = before * 100 + len(us) * 10 + (1 if v in us else 0)\n" +
                 "    c.Tally.s = \"empty\" if not query(Unit, Skill) else \"some\"\n");
            Ok(RunOnCity("Count"));
            Assert.That(Tally(), Is.EqualTo(320));
            Assert.That(Log(), Is.EqualTo("some"));
        }

        // ---- 스크립트가 만든 리스트는 파이썬 그대로 ----

        [Test]
        public void AListMadeFromAGroupIsAPlainPythonList()
        {
            Make("@command(free=True, acts_for=None)\ndef Copy(c: Ref[City]):\n" +
                 "    xs = [u for u in c.units]\n" +
                 "    destroy(xs[1])\n" +
                 "    c.Tally.n = len(xs)\n" +
                 "    for x in xs:\n" +
                 "        x.Home.n = 1\n");
            var r = RunOnCity("Copy");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError), "a plain list keeps the dead entity; reading it fails");
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunDeadEntity));
        }

        [TestCase("c.units[0]", "an entity group (units) cannot be indexed")]
        [TestCase("query(Unit)[0]", "an entity group (query(...)) cannot be indexed")]
        public void AGroupCannotBeIndexed(string expr, string message)
        {
            Make("@command(free=True, acts_for=None)\ndef Pick(c: Ref[City]):\n    v = " + expr + "\n");
            var r = RunOnCity("Pick");
            Assert.That(r.Error!.Message, Does.Contain(message));
            Assert.That(r.Error.Hint, Does.Contain("[u for u in ...]"));
        }

        [TestCase("x = c.units + []")]
        [TestCase("x = min(c.units)")]
        [TestCase("x = random.choice(c.units)")]
        [TestCase("r = ask(c, \"Pick\", choices=c.units)")]
        public void ListOnlyPlacesPointToACopy(string stmt)
        {
            Make("@command(free=True, acts_for=None)\ndef Use(c: Ref[City]):\n    " + stmt + "\n");
            var r = RunOnCity("Use");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Hint, Does.Contain("make a list first: [x for x in ...]"));
        }

        [Test]
        public void GroupsCompareByTheirLivingMembers()
        {
            Make("@command(free=True, acts_for=None)\ndef Same(c: Ref[City]):\n" +
                 "    a = c.units\n" +
                 "    b = query(Unit)\n" +
                 "    c.Tally.n = (10 if a == b else 0) + (1 if a == [u for u in a] else 0)\n");
            Ok(RunOnCity("Same"));
            Assert.That(Tally(), Is.EqualTo(10), "two groups with the same members are equal; a group is not a list");
        }

        [Test]
        public void AHeldGroupKeepsMembersThatMoveAway()
        {
            // 묶음은 받을 때의 목록이다: 죽은 것은 빠지지만 다른 도시로 옮긴 것은 남는다(WeakSet의 사본처럼). 새로 읽으면 바뀐 목록
            Make("@command(free=True, acts_for=None)\ndef Move(c: Ref[City]):\n" +
                 "    us = c.units\n" +
                 "    second(us).Home.city = None\n" +
                 "    c.Tally.n = len(us) * 10 + len(c.units)\n");
            Ok(RunOnCity("Move"));
            Assert.That(Tally(), Is.EqualTo(32));
        }

        // ---- 저장 ----

        [Test]
        public void ALoopPausedOnAnAskStillSkipsTheDeadAfterLoading()
        {
            Make("@command(free=True, acts_for=None)\ndef Patrol(c: Ref[City]):\n" +
                 "    for u in c.units:\n" +
                 "        r = ask(u, \"Report?\", choices=[\"yes\"])\n" +
                 "        c.Tally.n += 1\n");
            Ok(RunOnCity("Patrol"));
            var open = _w.OpenRequests.Single();
            Assert.That(open.Who.Single(), Is.EqualTo(_units[0]));
            _w.Destroy(_units[1]); // 첫 유닛에게 묻는 동안 호스트가 다음 차례의 유닛을 지운다
            var bytes = _w.Save();
            var loaded = World.Load(LoadClean(), bytes);
            Assert.That(loaded.Succeeded, Is.True, string.Join("\n", loaded.Diagnostics));
            _w = loaded.World!;
            Assert.That(_w.Save(), Is.EqualTo(bytes), "C4");
            open = _w.OpenRequests.Single();
            Assert.That(_w.Answer("host", open.Id, open.Who[0], 0).Error, Is.Null);
            open = _w.OpenRequests.Single();
            Assert.That(open.Who.Single(), Is.EqualTo(_units[2]), "the dead unit is skipped after loading too");
            Assert.That(_w.Answer("host", open.Id, open.Who[0], 0).Error, Is.Null);
            Assert.That(Tally(), Is.EqualTo(2));
        }
    }
}
