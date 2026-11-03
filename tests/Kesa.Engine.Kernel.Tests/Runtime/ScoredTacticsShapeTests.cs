using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 공식 도우미 없이 모드가 짜는 "점수 매겨 고르는 봇" (G469, <c>docs/std-ideas.md</c>): 후보는 Def, 가중치는 그 <c>const</c>, 점수 계산은
    /// 함수. 확장 모드는 후보를 더하고, 가중치를 패치하고, 점수 함수를 <c>@wrap</c>으로 감싼다.
    /// </summary>
    public class ScoredTacticsShapeTests : LoaderTestBase
    {
        [Test]
        public void AModBuildsScoredTactics()
        {
            Mod("brain",
                schema: "component(\"Hp\", v=field(int, default=3))\ndeftype(\"Party\", components=[\"Hp\"])\ndeftype(\"Unit\", components=[\"Hp\"])\n" +
                        "deftype(\"Tactic\", weight=const(int, default=1), kind=const(str))\n" +
                        "bot_state(\"Mind\", picked=field(str, default=\"\"))\n",
                defs: "Party(\"Red\")\nUnit(\"Soldier\")\n" +
                      "Tactic(\"Hit\", weight=2, kind=\"hit\")\nTactic(\"Rest\", weight=5, kind=\"rest\")\n" +
                      "@command(free=True, acts_for=None)\ndef Hit(target: Ref[Unit]):\n    target.Hp.v -= 1\n" +
                      "def score(kind, me):\n    if kind == \"hit\":\n        return len(query(Unit))\n    if kind == \"rest\":\n        return 1\n    return 0\n" +
                      "@bot\ndef planner(me: Ref[Party]):\n" +
                      "    best = \"\"\n    top = -1\n" +
                      "    for t in defs(Tactic):\n" +
                      "        d = defdata(t).Tactic\n        s = d.weight * score(d.kind, me)\n" +
                      "        if s > top:\n            top = s\n            best = t\n" +
                      "    bot.Mind.picked = best\n" +
                      "    pick = random.choices([t for t in defs(Tactic)], weights=[defdata(t).Tactic.weight for t in defs(Tactic)], k=1)\n");
            Mod("raider",
                defs: "Tactic(\"Raid\", weight=3, kind=\"raid\")\n" +
                      "patch.set(\"brain:Rest\", \"weight\", 1)\n" +
                      "@wrap(brain.score)\ndef raid_score(original, kind, me):\n    if kind == \"raid\":\n        return 10\n    return original(kind, me)\n",
                extra: ", dependencies=[\"brain\"]");
            var w = new World(LoadClean());
            for (int i = 0; i < 4; i++) w.Create("brain:Soldier");
            var red = w.Create("brain:Red");
            w.Seat(red, "bot", SeatKind.Ai);
            var bot = w.NewBot("brain.planner", red);
            var r = w.ViewAs("bot").Think(bot);
            Assert.That(r.Error, Is.Null, r.Error?.ToString());
            Assert.That(((StrValue)bot.Stored("Mind", "picked")).Value, Is.EqualTo("raider:Raid"));
        }
    }
}
