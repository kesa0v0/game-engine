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
    /// 봇의 시험 두기 (spec-gaps G453~G463): <c>with imagine():</c>이 복사한 시험 판에서 <c>play</c>로 수를 두고 읽는다. 진짜 판과 호스트는 그대로다.
    /// </summary>
    public class ImagineTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\n" +
            "deftype(\"Party\", components=[\"Hp\"])\ndeftype(\"Unit\", components=[\"Hp\"])\n" +
            "bot_state(\"Mind\", seen=field(list[int], default=[]), kill=ref(\"Unit\", optional=True), note=field(str, default=\"\"), " +
            "made=field(list[Ref[\"Unit\"]], default=[]))\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\nUnit(\"Soldier\")\nUnit(\"Tank\", Hp=Hp(v=5))\n" +
            "@command(free=True, acts_for=None)\ndef Hit(target: Ref[Unit], power: int = 3):\n" +
            "    target.Hp.v -= power\n    if target.Hp.v <= 0:\n        destroy(target)\n" +
            // 진영만 낼 수 있는 커맨드: 시험 판에서는 상대 진영으로도 둘 수 있다 (G457)
            "@command(free=True, acts_for=lambda party: party)\ndef Heal(party: Ref[Party]):\n    party.Hp.v += 10\n" +
            // 유닛마다 쳐 보고 죽는 것을 찾는다. 블록 전에 잡은 u는 블록 안에서 시험 판을 읽는다
            "@bot\ndef hunter(me: Ref[Party]):\n" +
            "    for u in query(Unit):\n" +
            "        with imagine():\n" +
            "            r = play(\"Hit\", target=u)\n" +
            "            bot.Mind.seen = bot.Mind.seen + [u.Hp.v if r.ok and u in query(Unit) else -1]\n" +
            // 블록 안에서는 u가 시험 판에서 죽었으므로 기억에는 블록 밖에서 적는다
            "            dies = u not in query(Unit)\n" +
            "        if dies:\n" +
            "            bot.Mind.kill = u\n" +
            "    submit(\"Hit\", target=bot.Mind.kill)\n" +
            // 안쪽 블록의 수는 바깥 시험 판에 남지 않는다
            "@bot\ndef nested(me: Ref[Party]):\n" +
            "    with imagine():\n" +
            "        t = [u for u in query(Unit)][1]\n" +
            "        play(\"Hit\", target=t, power=1)\n" +
            "        with imagine():\n" +
            "            play(\"Hit\", target=t, power=1)\n" +
            "            inner = t.Hp.v\n" +
            "        outer = t.Hp.v\n" +
            "    bot.Mind.seen = [inner, outer, t.Hp.v]\n" +
            // 블록 안에서 얻은 값은 블록 밖에서 진짜 판을 읽는다
            "@bot\ndef carry(me: Ref[Party]):\n" +
            "    with imagine():\n" +
            "        t = [u for u in query(Unit)][0]\n" +
            "        play(\"Hit\", target=t, power=1)\n" +
            "        before = t.Hp.v\n" +
            "    bot.Mind.seen = [before, t.Hp.v]\n" +
            "@bot\ndef rival(me: Ref[Party]):\n" +
            "    other = [p for p in query(Party) if p != me][0]\n" +
            "    with imagine():\n" +
            "        r = play(\"Heal\", party=other)\n" +
            "        bot.Mind.seen = [1 if r.ok else 0, other.Hp.v]\n" +
            "@bot\ndef failing(me: Ref[Party]):\n" +
            "    with imagine():\n" +
            "        r = play(\"Hit\", target=None)\n" +
            "        bot.Mind.note = r.outcome\n" +
            // 시험 판에서 연 질문은 열린 채 남고, 봇이 답마다 가지를 나눈다 (G459)
            "@command(free=True, acts_for=None)\ndef Duel(p: Ref[Party]):\n    r = ask(p, \"duel\", choices=[10, 20], name=\"duel\")\n    p.Hp.v += r.choice\n" +
            "@bot\ndef duelist(me: Ref[Party]):\n" +
            "    other = [p for p in query(Party) if p != me][0]\n" +
            "    with imagine():\n" +
            "        r = play(\"Duel\", p=other)\n" +
            "        open = [q.name for q in questions()]\n" +
            "        for c in r.question.choices:\n" +
            "            with imagine():\n" +
            "                a = play(r.question, \"engine.pick\", choice=c.index)\n" +
            "                bot.Mind.seen = bot.Mind.seen + [other.Hp.v if a.ok else -1]\n" +
            "        bad = play(r.question, \"engine.pick\", choice=5)\n" +
            "        bot.Mind.note = bad.outcome + \" \" + open[0]\n" +
            "    bot.Mind.seen = bot.Mind.seen + [len(questions())]\n" +
            // 가정 쓰기 (G455): 블록 안에서는 커맨드 본문처럼 판에 쓴다. 쓰는 함수도 블록 안에서 부를 수 있다
            "def weaken(u):\n    u.Hp.v = 1\n" +
            "@bot\ndef supposer(me: Ref[Party]):\n" +
            "    t = [u for u in query(Unit)][1]\n" +
            "    with imagine():\n" +
            "        weaken(t)\n" +
            "        play(\"Hit\", target=t, power=1)\n" +
            "        gone = 0 if t in query(Unit) else 1\n" +
            "        n = spawn(\"core:Soldier\")\n" +
            "        made = len([u for u in query(Unit)])\n" +
            "    bot.Mind.seen = [gone, made, len([u for u in query(Unit)]), t.Hp.v]\n" +
            // 시간 굴리기 (G460): 틱마다 진영의 체력이 는다. 질문이 시간을 막으면 덜 간다
            "@on(Tick)\ndef regrow():\n    for p in query(Party):\n        p.Hp.v += 1\n" +
            "@bot\ndef waiter(me: Ref[Party]):\n" +
            "    with imagine():\n" +
            "        went = advance(4)\n" +
            "        grown = me.Hp.v\n" +
            "        play(\"Duel\", p=me)\n" +
            "        stuck = advance(2)\n" +
            "    bot.Mind.seen = [went, grown, stuck, me.Hp.v]\n" +
            // 난수 (G461): 시험 판의 주사위는 봇 난수열에서 시드를 받고, seed=가 같으면 같은 운이다
            "@command(free=True, acts_for=None)\ndef Roll(p: Ref[Party]):\n    p.Hp.v = random.randint(1, 1000000)\n" +
            "@bot\ndef roller(me: Ref[Party]):\n" +
            "    with imagine():\n        play(\"Roll\", p=me)\n        a = me.Hp.v\n" +
            "    with imagine():\n        play(\"Roll\", p=me)\n        b = me.Hp.v\n" +
            "    with imagine(seed=4):\n        play(\"Roll\", p=me)\n        c = me.Hp.v\n" +
            "    with imagine(seed=4):\n        play(\"Roll\", p=me)\n        d = me.Hp.v\n" +
            "    bot.Mind.seen = [a, b, c, d]\n" +
            // 시험 id (G463): 안쪽 블록과 바깥 블록, 다음 생각까지 이어 센다
            "@bot\ndef maker(me: Ref[Party]):\n" +
            "    with imagine():\n" +
            "        with imagine():\n            a = spawn(\"core:Soldier\")\n            bot.Mind.kill = a\n" +
            "        b = spawn(\"core:Soldier\")\n        bot.Mind.made = [b]\n" +
            "@bot\ndef asker(me: Ref[Party]):\n    with imagine():\n        ask(me, \"x\", choices=[1], name=\"x\")\n" +
            "@bot\ndef leaky(me: Ref[Party]):\n    with imagine():\n        submit(\"Hit\", target=[u for u in query(Unit)][0])\n" +
            "@bot\ndef loose(me: Ref[Party]):\n    play(\"Hit\", target=[u for u in query(Unit)][0])\n" +
            "@bot\ndef broken(me: Ref[Party]):\n    with imagine():\n        x = [][0]\n";

        private World _world = null!;
        private WorldView _view = null!;
        private EntityId _red, _blue, _soldier, _tank;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _soldier = _world.Create("core:Soldier");
            _tank = _world.Create("core:Tank");
            _red = _world.Create("core:Red");
            _blue = _world.Create("core:Blue");
            _world.Seat(_red, "bot", SeatKind.Ai);
            _world.Seat(_blue, "other", SeatKind.Human);
            _view = _world.ViewAs("bot");
        }

        private ThinkResult Think(string bot, out BotState state)
        {
            state = _world.NewBot("core." + bot, _red);
            return _view.Think(state);
        }

        private static List<long> Seen(BotState bot) => ((ListValue)bot.Stored("Mind", "seen")).Items.Select(v => ((IntValue)v).Value).ToList();

        [Test]
        public void ABotTriesEachHitOnACopyAndSubmitsTheOneThatKills()
        {
            byte[] before = _world.Save();
            long version = _world.Version();

            var result = Think("hunter", out var bot);

            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            Assert.That(Seen(bot), Is.EqualTo(new[] { -1L, 2L })); // 병사는 죽고 전차는 2가 남는다
            Assert.That(((EntityValue)bot.Stored("Mind", "kill")).Id, Is.EqualTo(_soldier));
            // 진짜 판도 시계도 그대로다
            Assert.That(_world.Save(), Is.EqualTo(before));
            Assert.That(_world.Version(), Is.EqualTo(version));
            Assert.That(_world.Deliver("bot", result.Submissions.Single()).Succeeded, Is.True);
            Assert.That(_world.IsAlive(_soldier), Is.False);
        }

        [Test]
        public void AnInnerBlockStartsFromTheOuterBoardAndLeavesItAlone()
        {
            var result = Think("nested", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            Assert.That(Seen(bot), Is.EqualTo(new[] { 3L, 4L, 5L }));
        }

        [Test]
        public void AValueFoundInsideTheBlockReadsTheRealBoardAfterIt()
        {
            var result = Think("carry", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            Assert.That(Seen(bot), Is.EqualTo(new[] { 2L, 3L }));
        }

        [Test]
        public void OnTheCopyTheBotMayPlayForAnotherSide()
        {
            var result = Think("rival", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            Assert.That(Seen(bot), Is.EqualTo(new[] { 1L, 13L }));
            Assert.That(_world.Submit("bot", "core.Heal", new Dictionary<string, Value> { ["party"] = new EntityValue(_blue) }).Outcome,
                Is.EqualTo(SubmitOutcome.GateDenied));
        }

        [Test]
        public void AFailedPlayTellsWhy()
        {
            var result = Think("failing", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            Assert.That(((StrValue)bot.Stored("Mind", "note")).Value, Is.EqualTo("arguments_rejected"));
        }

        [Test]
        public void AQuestionOpenedOnTheCopyStaysOpenForTheBotToAnswer()
        {
            var result = Think("duelist", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            // 상대(파랑)에게 물은 질문에 답마다 가지를 나눴고, 진짜 판에는 질문이 없다
            Assert.That(Seen(bot), Is.EqualTo(new[] { 13L, 23L, 0L }));
            Assert.That(((StrValue)bot.Stored("Mind", "note")).Value, Is.EqualTo("refused duel"));
            Assert.That(_world.OpenRequests, Is.Empty);
        }

        [Test]
        public void InsideTheBlockTheBotMayWriteAssumptionsOnTheCopy()
        {
            byte[] before = _world.Save();
            var result = Think("supposer", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            // 전차를 1로 가정하면 한 대에 죽고, 새 병사까지 시험 판에는 둘이다. 진짜 판은 둘 그대로다
            Assert.That(Seen(bot), Is.EqualTo(new[] { 1L, 2L, 2L, 5L }));
            Assert.That(_world.Save(), Is.EqualTo(before));
        }

        [Test]
        public void InsideTheBlockTimeRollsOnTheCopyOnly()
        {
            long tick = _world.Tick;
            var result = Think("waiter", out var bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            // 4틱에 체력이 3에서 7, 결투 질문이 시간을 막아 0틱, 진짜 판은 3 그대로
            Assert.That(Seen(bot), Is.EqualTo(new[] { 4L, 7L, 0L, 3L }));
            Assert.That(_world.Tick, Is.EqualTo(tick));
        }

        private List<long> Rolls(long worldSeed, long botSeed, out BotState bot)
        {
            var world = new World(LoadClean(), seed: worldSeed);
            var red = world.Create("core:Red");
            world.Seat(red, "bot", SeatKind.Ai);
            bot = world.NewBot("core.roller", red, seed: botSeed);
            var result = world.ViewAs("bot").Think(bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            return Seen(bot);
        }

        [Test]
        public void TheImaginedDiceFollowTheBotSeedNotTheWorldSeed()
        {
            var one = Rolls(1, 7, out var bot);
            var other = Rolls(2, 7, out _);
            Assert.That(other, Is.EqualTo(one)); // 월드 시드가 달라도 봇 시드가 같으면 같은 상상
            Assert.That(one[0], Is.Not.EqualTo(one[1])); // 블록마다 새 운
            Assert.That(one[2], Is.EqualTo(one[3])); // seed=가 같으면 같은 운
            Assert.That(bot.RandomCalls, Is.EqualTo(2)); // seed=는 봇 난수열을 쓰지 않는다
            Assert.That(Rolls(1, 8, out _).Take(2), Is.Not.EqualTo(one.Take(2)));
        }

        private static List<long> Made(BotState bot) =>
            ((ListValue)bot.Stored("Mind", "made")).Items.Select(v => ((EntityValue)v).Id.Value).ToList();

        [Test]
        public void ImaginedEntitiesGetIdsOfTheirOwnThatNeverRepeat()
        {
            var bot = _world.NewBot("core.maker", _red);
            var first = _view.Think(bot);
            Assert.That(first.Outcome, Is.EqualTo(ThinkOutcome.Thought), first.Error?.Message);
            long b = 1L << 62;
            // 안쪽 블록이 b를 쓰고 나면 바깥 블록은 b+1부터다
            Assert.That(((EntityValue)bot.Stored("Mind", "kill")).Id.Value, Is.EqualTo(b));
            Assert.That(Made(bot), Is.EqualTo(new[] { b + 1 }));
            // 저장했다 되살려도 이어 센다
            var again = _world.LoadBot(bot.Save(), out _);
            Assert.That(_view.Think(again).Outcome, Is.EqualTo(ThinkOutcome.Thought));
            Assert.That(((EntityValue)again.Stored("Mind", "kill")).Id.Value, Is.EqualTo(b + 2));
            Assert.That(Made(again), Is.EqualTo(new[] { b + 3 }));
            Assert.That(again.TrialIds, Is.EqualTo(b + 4));
            Assert.That(_world.IsAlive(new EntityId(b)), Is.False);
            Assert.That(_world.Create("core:Soldier").Value, Is.LessThan(b));
        }

        [Test]
        public void AFunctionThatWritesCanOnlyBeCalledInsideTheBlock()
        {
            Mod("core", schema: Schema, defs: "def weaken(u):\n    u.Hp.v = 1\n" +
                                              "@bot\ndef rash(me: Ref[Party]):\n    weaken(me)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("the @bot 'rash' changes the game; it can only change bot.… and submit commands"));
        }

        [TestCase("asker", "ask() cannot be used directly inside 'with imagine():'")]
        [TestCase("leaky", "submit() cannot be used inside 'with imagine():'")]
        [TestCase("loose", "play() can only be used inside 'with imagine():'")]
        public void ImagineAndPlayKeepToTheirPlaces(string bot, string message)
        {
            var result = Think(bot, out _);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.ScriptError));
            Assert.That(result.Error!.Code, Is.EqualTo(Codes.RunImagine));
            Assert.That(result.Error.Message, Does.Contain(message));
        }

        [Test]
        public void AnErrorInsideTheBlockDropsTheCopy()
        {
            Assert.That(Think("broken", out _).Outcome, Is.EqualTo(ThinkOutcome.ScriptError));
            var again = Think("carry", out var bot);
            Assert.That(again.Outcome, Is.EqualTo(ThinkOutcome.Thought), again.Error?.Message);
            Assert.That(Seen(bot), Is.EqualTo(new[] { 2L, 3L }));
        }

        [Test]
        public void GameCodeCannotImagine()
        {
            Mod("core", schema: Schema, defs: "@command(free=True, acts_for=None)\ndef Peek():\n    with imagine():\n        pass\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Does.Contain("uses bot state"));
        }
    }
}
