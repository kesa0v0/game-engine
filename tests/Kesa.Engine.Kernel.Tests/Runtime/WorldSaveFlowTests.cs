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
    /// <summary>멈춘 Flow의 저장 (spec C1·C4·S8, spec-gaps G141·G142). 람다는 아직 저장하지 않는다.</summary>
    public class WorldSaveFlowTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0), log=field(str, default=\"\"))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"all\", level=field(int, default=3))\n" +
            "component(\"Wounded\", turns=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Party\", tier=const(int, default=2), components=[\"Score\", \"Skill\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nSkillDef(\"Mining\")\n" +
            "Party(\"Red\")\nParty(\"Blue\")\n" +
            "def bump(p, n):\n" +
            "    p.Score.v = p.Score.v + n\n" +
            // 한 줄 전체로 불려 결과를 버리는 도우미 안의 ask
            "def confirm(p):\n" +
            "    ask(p, \"Ok\", choices=[\"ok\"])\n" +
            // 끝난 함수의 묶음이 프레임의 묶음을 잡은 람다를 든다 / 자기를 잡은 묶음 (G143)
            "def make(h):\n" +
            "    k = lambda: h(1)\n" +
            "    return k\n" +
            "def pair():\n" +
            "    g = lambda: 1\n" +
            "    return g\n" +
            "def double(x):\n" +
            "    return x * 2\n" +
            // 반복 중간에서 멈추는 도우미, x = survey(...)로 불린다
            "def survey(p, others):\n" +
            "    picked = []\n" +
            "    key = lambda o: o.Score.v\n" +
            "    for o in others:\n" +
            "        h = o.Score\n" +
            "        r = ask(p, text(\"Pick\", who=o), choices=[o, \"skip\"], data={\"o\": o})\n" +
            "        if r.index == 0:\n" +
            "            picked = picked + [r.choice]\n" +
            "            bump(o, 1)\n" +
            "        elif r.choice == \"skip\":\n" +
            "            h.log = h.log + \"s\"\n" +
            "    p.Score.v = p.Score.v + key(p)\n" +
            "    return picked\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Campaign(p: Ref[Party], q: Ref[Party]):\n" +
            "    sk = p.Skill\n" +
            "    row = p.Skill.Cooking\n" +
            "    tier = p.Party\n" +
            "    hv = p.Score\n" +
            "    w = Wounded(turns=2)\n" +
            "    f = double\n" +
            "    g = len\n" +
            "    rg = range(2)\n" +
            "    mixed = {\"a\": [p, hv], \"b\": rg}\n" +
            "    bonus = 0\n" +
            "    lam = lambda x: x + bonus\n" +
            "    same = lam\n" +
            "    mk = make(lam)\n" +
            "    pg = pair()\n" +
            "    cv = Score(v=lam)\n" +
            "    tv = text(\"k\", f=lam)\n" +
            "    r0 = ask(p, \"Start\", choices=[\"go\", \"rush\"])\n" +
            "    bonus = 5\n" +
            "    picked = survey(p, [p, q])\n" +
            "    for i in rg:\n" +
            "        r2 = ask(q, \"Again\", choices=[\"x\", \"y\", \"z\"], hint=\"std.choice\")\n" +
            "        if r2.index == 0:\n" +
            "            bump(p, 10)\n" +
            "        elif r2.index == 1:\n" +
            "            r3 = ask(p, \"Sure\", choices=[\"yes\", \"no\"])\n" +
            "            if r3.index == 0 and p.Wounded is None:\n" +
            "                attach(p, w)\n" +
            "        else:\n" +
            "            hv.v = \"oops\"\n" +
            "    for each in [lam, same]:\n" +
            "        ask(p, \"Each\", choices=[\"ok\"])\n" +
            "    confirm(q)\n" +
            "    hv.v = hv.v + f(len(picked)) + g([1, 2]) + tier.tier + row.level + r0.index * 100\n" +
            "    hv.log = hv.log + (\"S\" if sk == p.Skill else \"-\") + (\"H\" if hv == p.Score else \"-\")\n" +
            "    hv.log = hv.log + (\"M\" if mixed[\"a\"][1] == p.Score and mixed[\"b\"] == range(2) else \"-\")\n" +
            "    hv.log = hv.log + (\"W\" if w == Wounded(turns=2) else \"-\")\n" +
            "    hv.v = hv.v + lam(1) + mk() + pg()\n" +
            "    hv.log = hv.log + (\"L\" if lam == same else \"-\") + (\"N\" if lam != mk else \"-\")\n" +
            "    hv.log = hv.log + (\"C\" if cv == Score(v=lam) else \"-\") + (\"T\" if tv == text(\"k\", f=lam) else \"-\")\n" +
            "    hv.log = hv.log + (\"E\" if each == same else \"-\")\n";

        /// <summary>시나리오의 걸음: 제출 하나, 그다음 열린 질문에 고른 번호로 답하기. 6번째 답(2)은 재개 뒤 오류를 낸다.</summary>
        private static readonly long[] Answers = { 1, 0, 1, 1, 0, 2, 0, 0, 0, 0 };

        private ContentLoadResult Content()
        {
            Mod("banking", schema: Schema, defs: Defs);
            return LoadClean();
        }

        /// <summary>
        /// 시나리오를 돌린다. <paramref name="saveBefore"/>번째 걸음 직전에 저장하고 새 내용으로 불러온 월드로 바꿔 이어 간다(-1이면 저장 없음).
        /// 걸음마다의 결과와 마지막 저장을 돌려준다. 저장한 순간에는 저장 → 불러오기 → 저장이 같은 바이트인지(C4)도 본다.
        /// </summary>
        private (List<string> Log, byte[] Final, string? Saved) Run(int saveBefore)
        {
            var world = new World(Content());
            var red = world.Create("Red");
            var blue = world.Create("Blue");
            var log = new List<string>();
            string? saved = null;

            for (int step = 0; step <= Answers.Length; step++)
            {
                if (step == saveBefore)
                {
                    var bytes = world.Save();
                    saved = Encoding.UTF8.GetString(bytes);
                    var result = World.Load(Content(), bytes);
                    Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
                    world = result.World!;
                    Assert.That(Encoding.UTF8.GetString(world.Save()), Is.EqualTo(saved), "C4 at step " + step);
                }

                if (step == 0)
                {
                    var r = world.Submit("host", "banking.Campaign",
                        new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(blue) });
                    log.Add(r.Outcome + " " + r.Error + " " + Show(r.OpenedRequest));
                    continue;
                }
                var open = world.OpenRequests.Single();
                var a = world.Answer("host", open.Id, open.Who[0], Answers[step - 1]);
                log.Add(a.Outcome + " " + a.Message + " " + a.Error + " " + Show(a.OpenedRequest));
            }
            log.Add(string.Join(" | ", world.OpenRequests.Select(Show)));
            return (log, world.Save(), saved);
        }

        private static string Show(Request? r) =>
            r == null ? "-" : "#" + r.Id + " flow " + r.Flow + " " + r.Command + " who " + string.Join(",", r.Who) +
                              " choices " + r.Choices.Count + " hint " + r.Hint;

        [Test]
        public void ContinuingAfterLoadingAtAnyWaitIsTheSameAsNeverSaving()
        {
            var reference = Run(-1);
            Assert.That(reference.Log.Count(l => l.StartsWith("ScriptError")), Is.EqualTo(1), string.Join("\n", reference.Log));
            Assert.That(reference.Log.Last(), Is.Empty, "the flow finished");

            var all = new StringBuilder();
            for (int k = 0; k <= Answers.Length; k++)
            {
                var run = Run(k);
                Assert.That(run.Log, Is.EqualTo(reference.Log), "saved before step " + k);
                Assert.That(Encoding.UTF8.GetString(run.Final), Is.EqualTo(Encoding.UTF8.GetString(reference.Final)),
                    "saved before step " + k);
                all.Append(run.Saved);
            }

            // 저장들이 실제로 모든 종류의 값을 거쳤다
            var text = all.ToString();
            foreach (var tag in new[] { "entity_view", "component", "rows", "def_view", "answer", "component_value", "range",
                         "function", "builtin", "branch", "items", "closure", "lambda", "frame", "env" })
                Assert.That(text, Does.Contain("\"" + tag + "\""), tag);
        }

        [Test]
        public void TheFinalStateIsWhatTheScriptSays()
        {
            var (log, final, _) = Run(-1);
            var w = World.Load(Content(), final).World!;
            var red = new EntityId(1);
            // bump(p,1)=1, key(p)로 2, bump(p,10)=12, + double(len([p]))=2 + len([1,2])=2 + tier 2 + level 3 + rush 100,
            // + lam(1)=1+5 (bonus는 재개 뒤에 5) + mk()=lam(1)=6 + pg()=1
            Assert.That(((IntValue)w.Get(red, w.Member("Score", "v"))).Value, Is.EqualTo(12 + 2 + 2 + 2 + 3 + 100 + 6 + 6 + 1));
            Assert.That(((StrValue)w.Get(red, w.Member("Score", "log"))).Value, Is.EqualTo("SHMWLNCTE"));
            Assert.That(((StrValue)w.Get(new EntityId(2), w.Member("Score", "log"))).Value, Is.EqualTo("s"));
            Assert.That(w.Has(red, "Wounded"), Is.True);
        }

        [Test]
        public void AFlowWhoseFunctionIsGoneIsRejected()
        {
            var world = new World(Content());
            var red = world.Create("Red");
            var blue = world.Create("Blue");
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(blue) });
            world.Answer("host", world.OpenRequests.Single().Id, red, 0); // survey 안에서 멈춤
            var save = world.Save();

            Mod("banking", schema: Schema, defs: Defs.Replace("def survey(p, others):", "def survey2(p, others):")
                .Replace("picked = survey(p, [p, q])", "picked = survey2(p, [p, q])"));
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV002 flow 1 (banking.Campaign): the paused function 'banking.survey' no longer exists",
            }));
        }

        // ---- 응답 커맨드를 기다리는 Flow (G139) ----

        private const string RoundDefs =
            "SkillDef(\"Cooking\")\nParty(\"Red\")\nParty(\"Blue\")\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Pass(party: Ref[Party]):\n" +
            "    pass\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Bid(party: Ref[Party], amount: int):\n" +
            "    party.Score.v = party.Score.v + amount\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Round(a: Ref[Party], b: Ref[Party]):\n" +
            "    last = None\n" +
            "    passes = 0\n" +
            "    for i in range(6):\n" +
            "        p = a if i % 2 == 0 else b\n" +
            "        r = ask(p, \"Bid\", responses=[\"Pass\", \"Bid\"])\n" +
            "        if last is not None and last.response == \"Bid\":\n" +
            "            a.Score.log = a.Score.log + (\"b\" if last.by == b else \"a\")\n" +
            "        last = r\n" +
            "        if r.response == \"Pass\":\n" +
            "            passes += 1\n" +
            "            if passes == 2:\n" +
            "                break\n" +
            "        else:\n" +
            "            passes = 0\n" +
            "    a.Score.v = a.Score.v + 1000\n";

        private (List<string> Log, string Final) RunRound(int saveBefore)
        {
            ContentLoadResult RoundContent()
            {
                Mod("banking", schema: Schema, defs: RoundDefs);
                return LoadClean();
            }

            var world = new World(RoundContent());
            var red = world.Create("Red");
            var blue = world.Create("Blue");
            var steps = new (string Command, long Amount)[] { ("Bid", 3), ("Bid", 0), ("Bid", 2), ("Pass", 0), ("Pass", 0) };
            var log = new List<string>();
            for (int step = 0; step <= steps.Length; step++)
            {
                if (step == saveBefore)
                {
                    var bytes = world.Save();
                    var result = World.Load(RoundContent(), bytes);
                    Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
                    world = result.World!;
                    Assert.That(world.Save(), Is.EqualTo(bytes), "C4 at step " + step);
                }
                if (step == 0)
                {
                    var r = world.Submit("host", "banking.Round",
                        new Dictionary<string, Value> { ["a"] = new EntityValue(red), ["b"] = new EntityValue(blue) });
                    log.Add(r.Outcome + " " + Show(r.OpenedRequest));
                    continue;
                }
                var open = world.OpenRequests.Single();
                var (command, amount) = steps[step - 1];
                var args = new Dictionary<string, Value> { ["party"] = new EntityValue(open.Who[0]) };
                if (command == "Bid") args["amount"] = new IntValue(amount);
                var a = world.Respond("host", open.Id, "banking." + command, args);
                log.Add(a.Outcome + " " + a.Message + " " + a.Error + " " + Show(a.OpenedRequest));
            }
            return (log, Encoding.UTF8.GetString(world.Save()));
        }

        [Test]
        public void AFlowWaitingForResponseCommandsContinuesTheSameAfterLoading()
        {
            var reference = RunRound(-1);
            // 직전 응답이 Bid였으면 누가 냈는지 적는다: a(3), b(0), a(2), Pass, Pass → "aba", 3 + 2 + 1000
            Assert.That(reference.Final, Does.Contain("\"log\": \"aba\""), reference.Final);
            Assert.That(reference.Final, Does.Contain("\"v\": 1005"), reference.Final);
            for (int k = 1; k <= 5; k++)
            {
                var run = RunRound(k);
                Assert.That(run.Log, Is.EqualTo(reference.Log), "saved before step " + k);
                Assert.That(run.Final, Is.EqualTo(reference.Final), "saved before step " + k);
            }
        }

        [Test]
        public void AFlowWaitingForAResponseCommandThatIsGoneIsRejected()
        {
            Mod("banking", schema: Schema, defs: RoundDefs);
            var world = new World(LoadClean());
            var red = world.Create("Red");
            world.Submit("host", "banking.Round",
                new Dictionary<string, Value> { ["a"] = new EntityValue(red), ["b"] = new EntityValue(red) });
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"response_names\""));

            Mod("banking", schema: Schema, defs: RoundDefs.Replace("def Bid(", "def Raise("));
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV002 flow 1 (banking.Round): the response command banking.Bid no longer exists or is now free",
            }));
        }

        [Test]
        public void ALambdaWhoseFunctionIsGoneIsRejected()
        {
            var world = new World(Content());
            var red = world.Create("Red");
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(red) });
            var save = world.Save();

            Mod("banking", schema: Schema, defs: Defs.Replace("def pair():", "def pair2():").Replace("pg = pair()", "pg = pair2()"));
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV002 flow 1 (banking.Campaign): the lambda 0 of 'banking.pair' that the flow holds no longer exists",
            }));
        }
    }
}
