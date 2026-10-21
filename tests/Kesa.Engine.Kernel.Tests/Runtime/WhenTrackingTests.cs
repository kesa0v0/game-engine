using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 정착이 바뀌었을 수 있는 조건만 다시 평가해도(S5 "읽은 칸이 바뀔 때만") 매번 모두 평가한 것과 결과가 같은가.
    /// 읽기 경로마다 조건을 하나씩 두고, 같은 단계를 두 월드에 주어 결과 항목과 저장을 단계마다 견준다.
    /// </summary>
    public class WhenTrackingTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Out\", s=field(str, default=\"\"), n=field(int, default=0))\n" +
            "component(\"Home\", city=ref(\"City\", optional=True, inverse=\"units\"))\n" +
            "component(\"Pop\", size=field(int, default=0), bonus=derived(int, lambda p: p.size + len(contribs(p, \"bonus\"))))\n" +
            "component(\"Tag\", x=field(int, default=0))\n" +
            "component(\"Opinion\", key=Ref[\"City\"], score=field(int, default=0))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", level=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"City\", components=[\"Pop\"])\n" +
            "deftype(\"Unit\", components=[\"Out\", \"Home\", \"Skill\", \"Opinion\"])\n";

        private const string Defs =
            "SkillDef(\"Ride\")\nCity(\"Town\")\nUnit(\"Scout\")\n" +
            // 읽기 경로마다 조건 하나
            "@when(lambda u: u.Home.city is not None and u.Home.city.Pop.size >= 3)\ndef big_home(u: Ref[Unit]):\n    pass\n" +
            "@when(lambda c: len(c.units) >= 2)\ndef crowded(c: Ref[City]):\n    pass\n" +
            "@when(lambda c: c.Pop.bonus >= 5)\ndef rich(c: Ref[City]):\n    pass\n" +
            "@when(lambda u: \"Ride\" in u.Skill and u.Skill.Ride.level >= 2)\ndef rider(u: Ref[Unit]):\n    pass\n" +
            "@when(lambda u: u.Tag is not None)\ndef tagged(u: Ref[Unit]):\n    pass\n" +
            // 줄 키인 도시가 사라지면 유닛의 줄이 지워진다: 도시를 읽지 않아도 유닛을 다시 봐야 한다
            "@when(lambda u: len(u.Opinion) >= 1)\ndef fond(u: Ref[Unit]):\n    pass\n" +
            "@when(lambda u: now() - u.Out.n >= 3)\ndef old(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"o\"\n" +
            "@when(lambda c: c.Pop.size > 0, repeat=True, cooldown=2)\ndef grow(c: Ref[City]):\n    pass\n" +
            "@when(lambda: now() % 4 == 0)\ndef season():\n    pass\n" +
            // query의 목록: 생성·삭제(City), 붙이기·떼기(Tag)로 바뀐다 (G193)
            "@when(lambda: len(query(City)) >= 2)\ndef towns():\n    pass\n" +
            "@when(lambda: any(u.Tag.x >= 1 for u in query(Unit, Tag)))\ndef marked():\n    pass\n" +
            "@when(lambda u: u.Out.n == 7, once=\"game\")\ndef lucky(u: Ref[Unit]):\n    pass\n" +
            // 본문이 다른 조건을 바꾸는 반응: 이사하면 그 유닛의 n을 올린다
            "@when(lambda u: u.Home.city is not None)\ndef settled(u: Ref[Unit]):\n    u.Out.n = u.Out.n + 1\n" +
            // 커맨드
            "@command(free=True, acts_for=None)\ndef MoveIn(u: Ref[Unit], c: Ref[City]):\n    u.Home.city = c\n" +
            "@command(free=True, acts_for=None)\ndef Leave(u: Ref[Unit]):\n    u.Home.city = None\n" +
            "@command(free=True, acts_for=None)\ndef Grow(c: Ref[City], n: int):\n    c.Pop.size = n\n" +
            "@command(free=True, acts_for=None)\ndef Learn(u: Ref[Unit], n: int):\n" +
            "    if \"Ride\" in u.Skill:\n        u.Skill.Ride.level = n\n    else:\n        add_row(u.Skill, \"Ride\", Skill(level=n))\n" +
            "@command(free=True, acts_for=None)\ndef Unlearn(u: Ref[Unit]):\n    remove_row(u.Skill, \"Ride\")\n" +
            "@command(free=True, acts_for=None)\ndef Mark(u: Ref[Unit]):\n    attach(u, Tag(x=1))\n" +
            "@command(free=True, acts_for=None)\ndef Unmark(u: Ref[Unit]):\n    detach(u.Tag)\n" +
            "@command(free=True, acts_for=None)\ndef Like(u: Ref[Unit], c: Ref[City]):\n" +
            "    if c not in u.Opinion:\n        add_row(u.Opinion, c, Opinion(score=1))\n" +
            "@command(free=True, acts_for=None)\ndef Raze(c: Ref[City]):\n    destroy(c)\n" +
            "@command(free=True, acts_for=None)\ndef Found():\n    spawn(\"Town\")\n" +
            "@command(free=True, acts_for=None)\ndef Recruit():\n    spawn(\"Scout\")\n" +
            "@command(free=True, acts_for=None)\ndef SetN(u: Ref[Unit], n: int):\n    u.Out.n = n\n";

        private static string Show(Reaction r) =>
            r.Signal + " " + r.Handler + " " + string.Join(",", r.Values.Select(p => p.Key + "=" + Plain(p.Value))) + " " + r.Outcome +
            (r.Error == null ? "" : " " + r.Error.Message) + (r.Message == null ? "" : " " + r.Message);

        private static string Plain(Value v) => v is EntityValue e ? e.Id.ToString() : v.KindName;

        /// <summary>고정 시드의 선형 합동 생성기 (테스트끼리 결과가 같게).</summary>
        private sealed class Steps
        {
            private ulong _state;
            public Steps(ulong seed) => _state = seed;

            public int Next(int bound)
            {
                _state = _state * 6364136223846793005UL + 1442695040888963407UL;
                return (int)((_state >> 33) % (ulong)bound);
            }
        }

        private static string Step(World w, Steps r, MemberHandle bonus, List<long> contributions)
        {
            var units = w.All("Unit").ToList();
            var cities = w.All("City").ToList();
            Dictionary<string, Value> Args(params (string, Value)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);
            Value U() => new EntityValue(units[r.Next(units.Count)]);
            Value C() => new EntityValue(cities[r.Next(cities.Count)]);
            string Run(string command, Dictionary<string, Value> args)
            {
                var result = w.Submit("host", "core." + command, args);
                return command + " " + result.Outcome + " | " + string.Join(" | ", result.Reactions.Select(Show));
            }

            int pick = r.Next(17);
            if (units.Count == 0 || cities.Count == 0) pick = units.Count == 0 ? 11 : 10;
            switch (pick)
            {
                case 0: case 1: return Run("MoveIn", Args(("u", U()), ("c", C())));
                case 2: return Run("Leave", Args(("u", U())));
                case 3: case 4: return Run("Grow", Args(("c", C()), ("n", new IntValue(r.Next(6)))));
                case 5: return Run("Learn", Args(("u", U()), ("n", new IntValue(r.Next(4)))));
                case 6: return Run("Unlearn", Args(("u", U())));
                case 7: return Run("Mark", Args(("u", U())));
                case 8: return Run("Unmark", Args(("u", U())));
                case 9: return cities.Count > 1 ? Run("Raze", Args(("c", C()))) : Run("Found", Args());
                case 10: return Run("Found", Args());
                case 11: return Run("Recruit", Args());
                case 12: return Run("SetN", Args(("u", U()), ("n", new IntValue(r.Next(9)))));
                case 13:
                {
                    // 호스트가 커맨드 밖에서 기여 행을 넣는다: 다음 정착에서 보여야 한다
                    var target = cities[r.Next(cities.Count)];
                    contributions.Add(w.AddContribution(target, bonus, "add", new IntValue(1)));
                    return "contribute " + target;
                }
                case 14:
                    if (contributions.Count > 0)
                    {
                        long id = contributions[r.Next(contributions.Count)];
                        contributions.Remove(id);
                        try
                        {
                            w.RemoveContribution(id);
                        }
                        catch (WorldException)
                        {
                            return "uncontribute gone";
                        }
                        return "uncontribute " + id;
                    }
                    goto default;
                case 15: return Run("Like", Args(("u", U()), ("c", C())));
                default:
                {
                    var advanced = w.Advance(1 + r.Next(2));
                    return "advance " + advanced.Advanced + " | " + string.Join(" | ", advanced.Reactions.Select(Show));
                }
            }
        }

        [TestCase(1UL)]
        [TestCase(7UL)]
        [TestCase(42UL)]
        [TestCase(2026UL)]
        public void TrackingGivesTheSameResultAsLookingAtEverything(ulong seed)
        {
            Mod("core", schema: Schema, defs: Defs);
            var content = LoadClean();
            var tracked = new World(content);
            var full = new World(content) { WhenFullScan = true };
            foreach (var w in new[] { tracked, full })
            {
                w.Create("core:Town");
                w.Create("core:Town");
                for (int i = 0; i < 4; i++) w.Create("core:Scout");
            }
            var bonus = tracked.Member("Pop", "bonus");
            var a = new Steps(seed);
            var b = new Steps(seed);
            var ca = new List<long>();
            var cb = new List<long>();
            int fired = 0;
            for (int i = 0; i < 400; i++)
            {
                string x = Step(tracked, a, bonus, ca);
                string y = Step(full, b, bonus, cb);
                Assert.That(x, Is.EqualTo(y), "step " + i);
                Assert.That(Encoding.UTF8.GetString(tracked.Save()), Is.EqualTo(Encoding.UTF8.GetString(full.Save())), "save after step " + i);
                fired += x.Split(new[] { "when core." }, System.StringSplitOptions.None).Length - 1;
                // 불러온 월드는 기억이 비어 있다: 처음 정착에서 모두 보고 이어서 같은 결과를 내야 한다
                if (i % 50 == 49) tracked = World.Load(content, tracked.Save()).World!;
            }
            Assert.That(fired, Is.GreaterThan(100), "the steps should make reactions fire");
        }

        [Test]
        public void EveryReadPathIsCovered()
        {
            // 위 견주기가 의미 있으려면 각 반응이 실제로 발동해야 한다
            Mod("core", schema: Schema, defs: Defs);
            var content = LoadClean();
            var world = new World(content);
            world.Create("core:Town");
            world.Create("core:Town");
            for (int i = 0; i < 4; i++) world.Create("core:Scout");
            var bonus = world.Member("Pop", "bonus");
            var r = new Steps(7);
            var seen = new SortedSet<string>();
            var log = new List<long>();
            for (int i = 0; i < 400; i++)
                foreach (var part in Step(world, r, bonus, log).Split('|'))
                    if (part.Trim().StartsWith("when core."))
                        seen.Add(part.Trim().Split(' ')[1]);
            Assert.That(seen, Is.EquivalentTo(new[]
            {
                "core.big_home", "core.crowded", "core.rich", "core.rider", "core.tagged", "core.fond", "core.old", "core.grow", "core.season",
                "core.lucky", "core.settled", "core.towns", "core.marked",
            }));
        }
    }
}
