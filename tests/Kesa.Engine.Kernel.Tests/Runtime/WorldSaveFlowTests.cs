using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>멈춘 Flow의 저장 (spec C1·C4·C5·S8, spec-gaps G141·G142·G143·G144).</summary>
    public class WorldSaveFlowTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0), log=field(str, default=\"\"))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"all\", level=field(int, default=3))\n" +
            "component(\"Wounded\", turns=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Party\", tier=const(int, default=2), components=[\"Score\", \"Skill\"])\n" +
            "signal(\"Hit\", who=Ref[\"Party\"], n=int)\n";

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
            "    wc = Wounded\n" + // 컴포넌트 생성자 값 (G230: builtin이 아니라 schema로 적힌다)
            "    rg = range(2)\n" +
            "    mixed = {\"a\": [p, hv], \"b\": rg}\n" +
            "    bonus = 0\n" +
            "    lam = lambda x: x + bonus\n" +
            "    same = lam\n" +
            "    mk = make(lam)\n" +
            "    pg = pair()\n" +
            "    cv = Score(v=lam)\n" +
            "    tv = text(\"k\", f=p)\n" +
            "    sg = Hit(who=p, n=2)\n" +
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
            "    hv.log = hv.log + (\"W\" if w == wc(turns=2) else \"-\")\n" +
            "    hv.v = hv.v + lam(1) + mk() + pg()\n" +
            "    hv.log = hv.log + (\"L\" if lam == same else \"-\") + (\"N\" if lam != mk else \"-\")\n" +
            "    hv.log = hv.log + (\"C\" if cv == Score(v=lam) else \"-\") + (\"T\" if tv == text(\"k\", f=p) else \"-\")\n" +
            "    hv.log = hv.log + (\"E\" if each == same else \"-\")\n" +
            "    hv.log = hv.log + (\"G\" if sg == Hit(n=2, who=p) and sg.who == p and sg.n == 2 else \"-\")\n";

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
        /// <paramref name="reloadDefs"/>가 있으면 그 내용으로 불러온다(모드 업데이트).
        /// </summary>
        private (List<string> Log, byte[] Final, string? Saved) Run(int saveBefore, string? reloadDefs = null)
        {
            var world = new World(Content());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var blue = world.Create("banking:Blue");
            world.Seat(blue, "host");
            var log = new List<string>();
            string? saved = null;

            for (int step = 0; step <= Answers.Length; step++)
            {
                if (step == saveBefore)
                {
                    var bytes = world.Save();
                    saved = Encoding.UTF8.GetString(bytes);
                    if (reloadDefs != null) Mod("banking", schema: Schema, defs: reloadDefs);
                    var result = World.Load(reloadDefs == null ? Content() : LoadClean(), bytes);
                    Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
                    world = result.World!;
                    // 내용이 바뀌었으면 저장은 새 내용 지문을 적는다 (G212). 상태는 같아야 한다
                    static string State(string save) =>
                        System.Text.RegularExpressions.Regex.Replace(save, "\"hash\": \"[0-9a-f]{32}\"", "\"hash\": …");
                    if (reloadDefs == null) Assert.That(Encoding.UTF8.GetString(world.Save()), Is.EqualTo(saved), "C4 at step " + step);
                    else Assert.That(State(Encoding.UTF8.GetString(world.Save())), Is.EqualTo(State(saved)), "C4 at step " + step);
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
                         "function", "builtin", "schema", "branch", "items", "closure", "lambda", "frame", "env", "signal" })
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
            Assert.That(((StrValue)w.Get(red, w.Member("Score", "log"))).Value, Is.EqualTo("SHMWLNCTEG"));
            Assert.That(((StrValue)w.Get(new EntityId(2), w.Member("Score", "log"))).Value, Is.EqualTo("s"));
            Assert.That(w.Has(red, "Wounded"), Is.True);
        }

        // ---- 함수 본문 해시 (C5, G144) ----

        /// <summary>시나리오를 <paramref name="answers"/>개 답한 뒤 저장한다.</summary>
        private byte[] SaveAfter(int answers)
        {
            var world = new World(Content());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var blue = world.Create("banking:Blue");
            world.Seat(blue, "host");
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(blue) });
            for (int i = 0; i < answers; i++)
            {
                var open = world.OpenRequests.Single();
                world.Answer("host", open.Id, open.Who[0], Answers[i]);
            }
            return world.Save();
        }

        private WorldLoadResult LoadWith(byte[] save, string defs)
        {
            Mod("banking", schema: Schema, defs: defs);
            return World.Load(LoadClean(), save);
        }

        [Test]
        public void CommentsLayoutAndFunctionOrderDoNotInvalidateTheSave()
        {
            // 주석, 빈 줄, 공백, 괄호, 파일 안의 함수 순서만 다르다
            string loose = Defs
                .Replace("def bump(p, n):\n", "# 도우미\ndef bump(p, n):\n\n    # 점수를 더한다\n")
                .Replace("    picked = []\n", "    picked = []   # 빈 목록\n\n")
                .Replace("    for o in others:\n", "    for o in others :\n")
                .Replace("    p.Score.v = p.Score.v + key(p)\n", "    p.Score.v = (p.Score.v) + key( p )\n")
                .Replace("    bonus = 5\n", "    # 재개 뒤\n    bonus = 5\n")
                .Replace("{\"a\": [p, hv], \"b\": rg}", "{ \"a\" : [ p , hv ] , \"b\": rg }")
                .Replace("def double(x):\n    return x * 2\n", "") + "\n\ndef double(x):\n    return x  *  2\n";
            Assert.That(loose, Is.Not.EqualTo(Defs));

            // 오류 위치는 새 파일의 줄을 가리킨다 (그 줄이 실제로 실행된 코드다)
            static List<string> Places(List<string> log) =>
                log.Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"defs\.py:\d+:\d+", "defs.py:…")).ToList();
            // 저장의 내용 지문은 글자를 그대로 보므로 다르다 (G212). 상태만 비교한다
            static string State(byte[] save) =>
                System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(save), "\"hash\": \"[0-9a-f]{32}\"", "\"hash\": …");
            var reference = Run(-1);
            for (int k = 0; k <= Answers.Length; k++)
            {
                var run = Run(k, loose);
                Assert.That(Places(run.Log), Is.EqualTo(Places(reference.Log)), "saved before step " + k);
                Assert.That(State(run.Final), Is.EqualTo(State(reference.Final)), "saved before step " + k);
            }
        }

        [Test]
        public void AChangedConstantInAPausedFunctionIsRejected()
        {
            var save = SaveAfter(1); // survey 안에서 멈춤 (Campaign → survey)
            var result = LoadWith(save, Defs.Replace("bump(p, 10)", "bump(p, 11)").Replace("bump(o, 1)", "bump(o, 2)"));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV004 flow 1 (banking.Campaign): the function 'banking.Campaign' changed since the save",
                "SAV004 flow 1 (banking.Campaign): the function 'banking.survey' changed since the save",
            }));
        }

        [Test]
        public void ASavedLocalThatTheFunctionDoesNotHaveIsACorruptSave()
        {
            // 함수 해시가 같으면 지역 변수 이름은 함수의 칸 배치 안에 있다(G226). 밖의 이름은 고친 저장이다
            string save = Encoding.UTF8.GetString(SaveAfter(1));
            Assert.That(save, Does.Contain("\"bonus\": "));
            var result = LoadWith(Encoding.UTF8.GetBytes(save.Replace("\"bonus\": ", "\"bonusx\": ")), Defs);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message), Has.Some.Contains("SAV").And.Contains("has no local 'bonusx'"));
        }

        [Test]
        public void AChangedFunctionThatHoldsASavedLambdaIsRejected()
        {
            // pair는 이미 끝났지만 Flow가 pair 안의 람다를 들고 있다
            var result = LoadWith(SaveAfter(0), Defs.Replace("    g = lambda: 1\n", "    g = lambda: 2\n"));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV004 flow 1 (banking.Campaign): the function 'banking.pair' changed since the save",
            }));
        }

        [Test]
        public void AChangedFunctionThatIsNotPausedRunsItsNewBody()
        {
            // bump과 double은 멈춘 프레임도 아니고 람다를 들지도 않는다: 이어 가면 새 본문이 실행된다
            var save = SaveAfter(Answers.Length - 1); // 마지막 confirm(q)에서 멈춤
            var result = LoadWith(save, Defs.Replace("    return x * 2\n", "    return x * 3\n"));
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            var world = result.World!;
            var open = world.OpenRequests.Single();
            world.Answer("host", open.Id, open.Who[0], Answers[Answers.Length - 1]);
            Assert.That(((IntValue)world.Get(new EntityId(1), world.Member("Score", "v"))).Value,
                Is.EqualTo(12 + 3 + 2 + 2 + 3 + 100 + 6 + 6 + 1), "double(len(picked)) is now 3");
        }

        [Test]
        public void TheBodyHashIsFixed()
        {
            // 해시는 프로세스·기계와 무관하다 (C6). 이 값이 바뀌면 이전 저장의 멈춘 Flow가 모두 SAV004로 거부된다
            Mod("banking", schema: Schema, defs: "Party(\"Red\")\n" +
                                                 "@command(free=True, acts_for=None)\n" +
                                                 "def Hold(p: Ref[Party]):\n" +
                                                 "    r = ask(p, \"Go\", choices=[\"a\", 1, None, True, 1.5])\n" +
                                                 "    p.Score.v = -r.index if r.index > 0 else len([lambda x: x[0]])\n");
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            world.Submit("host", "banking.Hold", new Dictionary<string, Value> { ["p"] = new EntityValue(red) });
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Contain("\"hash\": \"15198fccf5c8e6bb4be771e5743b6936\""));
        }

        [Test]
        public void AFlowWhoseFunctionIsGoneIsRejected()
        {
            var world = new World(Content());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var blue = world.Create("banking:Blue");
            world.Seat(blue, "host");
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(blue) });
            world.Answer("host", world.OpenRequests.Single().Id, red, 0); // survey 안에서 멈춤
            var save = world.Save();

            Mod("banking", schema: Schema, defs: Defs.Replace("def survey(p, others):", "def survey2(p, others):")
                .Replace("picked = survey(p, [p, q])", "picked = survey2(p, [p, q])"));
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV004 flow 1 (banking.Campaign): the function 'banking.Campaign' changed since the save",
                "SAV002 flow 1 (banking.Campaign): the paused function 'banking.survey' no longer exists",
                "SAV002 flow 1 (banking.Campaign): the lambda 0 of 'banking.survey' that the flow holds no longer exists",
            }));
        }

        // ---- 호스트가 허락하면 이어 갈 수 없는 Flow를 취소하고 불러온다 (G214) ----

        private const string Nap =
            "@command(free=True, acts_for=None)\ndef Nap(p: Ref[Party]):\n    p.Score.log = p.Score.log + \"a\"\n" +
            "    wait(ticks=3)\n    p.Score.log = p.Score.log + \"z\"\n";

        [Test]
        public void TheHostCanCancelAFlowItsContentNoLongerSupports()
        {
            Mod("banking", schema: Schema, defs: Defs + Nap);
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var blue = world.Create("banking:Blue");
            world.Seat(blue, "host");
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(blue) });
            var request = world.OpenRequests.Single().Id;
            world.Answer("host", request, red, Answers[0]); // survey 안에서 멈춤, 새 질문
            request = world.OpenRequests.Single().Id;
            world.Submit("host", "banking.Nap", new Dictionary<string, Value> { ["p"] = new EntityValue(blue) });
            var save = world.Save();

            string changed = Defs.Replace("bump(p, 10)", "bump(p, 11)").Replace("bump(o, 1)", "bump(o, 2)") + Nap;
            Mod("banking", schema: Schema, defs: changed);
            var rejected = World.Load(LoadClean(), save);
            Assert.That(rejected.Succeeded, Is.False, "by default nothing changes");
            Assert.That(rejected.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code).Distinct(), Is.EqualTo(new[] { "SAV004" }));

            var result = World.Load(LoadClean(), save, cancelBrokenFlows: true);
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Severity + " " + d.Message),
                Is.EqualTo(new[]
                {
                    "SAV006 Warning flow 1 (banking.Campaign) was canceled with its request " + request +
                    ": the function 'banking.Campaign' changed since the save; the function 'banking.survey' changed since the save",
                }));

            // 질문은 함께 사라지고, 다른 Flow는 이어 가고, 멈추기 전에 한 일은 남는다
            var w = result.World!;
            Assert.That(w.OpenRequests, Is.Empty);
            Assert.That(((StrValue)w.Get(blue, w.Member("Score", "log"))).Value, Is.EqualTo("a"));
            var woke = w.Advance(3).Reactions.Single();
            Assert.That(woke.Handler + " " + woke.Outcome, Is.EqualTo("banking.Nap Finished"));
            Assert.That(((StrValue)w.Get(blue, w.Member("Score", "log"))).Value, Is.EqualTo("az"));

            // 취소한 뒤의 저장은 지금 내용과 맞는다
            var again = World.Load(LoadClean(), w.Save());
            Assert.That(again.Diagnostics, Is.Empty, string.Join("\n", again.Diagnostics));
        }

        [Test]
        public void CancelingCoversAGoneFunctionButNotProblemsOutsideFlows()
        {
            var save = SaveAfter(1); // survey 안에서 멈춤
            Mod("banking", schema: Schema, defs: Defs.Replace("def survey(p, others):", "def survey2(p, others):")
                .Replace("picked = survey(p, [p, q])", "picked = survey2(p, [p, q])"));
            var result = World.Load(LoadClean(), save, cancelBrokenFlows: true);
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            var canceled = result.Diagnostics.Single(d => d.Code == Codes.SaveFlowCanceled).Message;
            Assert.That(canceled, Does.StartWith("flow 1 (banking.Campaign) was canceled with its request "));
            Assert.That(canceled, Does.EndWith(": the function 'banking.Campaign' changed since the save; " +
                "the paused function 'banking.survey' no longer exists; " +
                "the lambda 0 of 'banking.survey' that the flow holds no longer exists"));

            // Flow 밖의 어긋남(엔티티에 쓴 값의 멤버가 지워짐)은 이 옵션으로 넘어가지 않는다
            var world = new World(Content());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var blue = world.Create("banking:Blue");
            world.Seat(blue, "host");
            world.Set(red, world.Member("Score", "log"), new StrValue("x"));
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(blue) });
            world.Answer("host", world.OpenRequests.Single().Id, red, Answers[0]);
            save = world.Save();
            // 로직이 지운 멤버를 쓰는 곳은 타입을 모르게 감싸 로드가 되게 한다 (아니면 G228의 로드 오류)
            Mod("banking", schema: Schema.Replace(", log=field(str, default=\"\")", ""),
                defs: Defs.Replace("def survey(p, others):", "def survey2(p, others):")
                    .Replace("picked = survey(p, [p, q])", "picked = survey2(p, [p, q])")
                    .Replace("hv.log", "[hv][0].log").Replace("h.log", "[h][0].log"));
            result = World.Load(LoadClean(), save, cancelBrokenFlows: true);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Code).Distinct(),
                Is.EqualTo(new[] { "SAV002" }));
            Assert.That(result.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message),
                Has.All.Not.StartWith("flow "));
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
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var blue = world.Create("banking:Blue");
            world.Seat(blue, "host");
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
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            world.Submit("host", "banking.Round",
                new Dictionary<string, Value> { ["a"] = new EntityValue(red), ["b"] = new EntityValue(red) });
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"response_names\""));

            Mod("banking", schema: Schema, defs: RoundDefs.Replace("def Bid(", "def Raise("));
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV002 flow 1 (banking.Round): the response command banking.Bid no longer exists or is now free",
            }));
        }

        [Test]
        public void ALambdaWhoseFunctionIsGoneIsRejected()
        {
            var world = new World(Content());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            world.Submit("host", "banking.Campaign",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red), ["q"] = new EntityValue(red) });
            var save = world.Save();

            Mod("banking", schema: Schema, defs: Defs.Replace("def pair():", "def pair2():").Replace("pg = pair()", "pg = pair2()"));
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV004 flow 1 (banking.Campaign): the function 'banking.Campaign' changed since the save",
                "SAV002 flow 1 (banking.Campaign): the lambda 0 of 'banking.pair' that the flow holds no longer exists",
            }));
        }
    }
}
