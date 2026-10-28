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
    /// 봇 한 번 생각하기 (spec-gaps G369~G375): 그 연결의 눈으로 읽고, 성공했을 때만 기억과 난수 카운터가 들어가며, 적은 수는 호스트가 낸다.
    /// 봇 난수는 시드로 정해지고 게임 난수와 따로다. 기억은 호스트가 바이트로 저장하고 되살린다.
    /// </summary>
    public class BotThinkTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\ncomponent(\"Cloak\", on=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Hp\", \"Cloak\"])\ndeftype(\"Unit\", components=[\"Hp\", \"Cloak\"])\n" +
            "bot_state(\"Mind\", target=ref(\"Unit\", optional=True), seen=field(list[Ref[\"Unit\"]], default=[]), " +
            "rolls=field(list[int], default=[]), turns=field(int, default=0))\n";

        private const string Defs =
            "Party(\"Blue\")\nUnit(\"Soldier\")\nUnit(\"Ghost\", Cloak=Cloak(on=True))\n" +
            "@view_rule(Unit)\ndef cloaked(viewer, entity):\n    return not entity.Cloak.on\n" +
            "@command(free=True, acts_for=None)\ndef Hit(target: Ref[Unit]):\n    target.Hp.v -= 1\n" +
            "def note(n):\n    bot.Mind.turns = n\n" +
            // 보이는 유닛을 기억하고 처음 것을 때린다
            "@bot\ndef hunter(me: Ref[Party], hits: int = 1):\n" +
            "    bot.Mind.seen = [u for u in query(Unit)]\n    note(bot.Mind.turns + 1)\n" +
            "    for i in range(hits):\n        submit(\"Hit\", target=bot.Mind.seen[0])\n" +
            "@bot\ndef dice(me: Ref[Party]):\n    bot.Mind.rolls = bot.Mind.rolls + [random.randint(1, 1000000)]\n" +
            "@bot\ndef spin(me: Ref[Party]):\n    bot.Mind.turns = 9\n    submit(\"Hit\", target=None)\n    while True:\n        pass\n" +
            "@bot\ndef idle(me: Ref[Party]):\n    pass\n" +
            "@command(free=True, acts_for=None)\ndef Duel(p: Ref[Party]):\n    r = ask(p, \"duel\", choices=[10, 20])\n    p.Hp.v += r.choice\n" +
            "@bot\ndef chooser(me: Ref[Party]):\n    submit(\"engine.pick\", choice=1)\n";

        private World _world = null!;
        private WorldView _view = null!;
        private EntityId _blue, _a;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _world.Create("core:Ghost");
            _a = _world.Create("core:Soldier");
            _blue = _world.Create("core:Blue");
            _world.Seat(_blue, "bot", SeatKind.Ai);
            _view = _world.ViewAs("bot");
        }

        private long Hp(EntityId id) => ((IntValue)_world.Get(id, _world.Member("Hp", "v"))).Value;

        private List<long> Rolls(BotState bot) => ((ListValue)bot.Stored("Mind", "rolls")).Items.Select(v => ((IntValue)v).Value).ToList();

        [Test]
        public void ABotReadsThroughItsSeatRemembersAndSubmits()
        {
            var bot = _world.NewBot("core.hunter", _blue, new Dictionary<string, Value> { ["hits"] = new IntValue(2) });
            var result = _view.Think(bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            // 망토 두른 유닛은 이 연결에게 안 보인다
            Assert.That(((ListValue)bot.Stored("Mind", "seen")).Items.Cast<EntityValue>().Select(e => e.Id), Is.EqualTo(new[] { _a }));
            Assert.That(((IntValue)bot.Stored("Mind", "turns")).Value, Is.EqualTo(1));
            Assert.That(result.Submissions.Select(s => s.Command), Is.EqualTo(new[] { "core.Hit", "core.Hit" }));
            Assert.That(Hp(_a), Is.EqualTo(3)); // 적어 두기만 했다
            Assert.That(_world.Deliver("bot", result.Submissions[0]).Submitted!.Succeeded, Is.True);
            Assert.That(Hp(_a), Is.EqualTo(2));
            Assert.That(_world.BotSettings("core.hunter").Single().Name, Is.EqualTo("hits"));
        }

        [Test]
        public void ABotAnswersAQuestionWithChoices()
        {
            var bot = _world.NewBot("core.chooser", _blue);
            var nothing = _world.Deliver("bot", _view.Think(bot).Submissions.Single());
            Assert.That(nothing.Refusal, Is.EqualTo("no open question with choices that bot can answer"));

            Assert.That(_world.Submit("host", "core.Duel", new Dictionary<string, Value> { ["p"] = new EntityValue(_blue) }).Succeeded, Is.True);
            var answered = _world.Deliver("bot", _view.Think(bot).Submissions.Single());
            Assert.That(answered.Answered!.Outcome, Is.EqualTo(AnswerOutcome.Succeeded));
            Assert.That(Hp(_blue), Is.EqualTo(23));
        }

        [Test]
        public void TheSameSeedDrawsTheSameNumbersApartFromTheGame()
        {
            var one = _world.NewBot("core.dice", _blue, seed: 7);
            var two = _world.NewBot("core.dice", _blue, seed: 7);
            var other = _world.NewBot("core.dice", _blue, seed: 8);
            for (int i = 0; i < 3; i++)
            {
                _view.Think(one);
                _view.Think(two);
                _view.Think(other);
            }
            Assert.That(Rolls(one), Is.EqualTo(Rolls(two)));
            Assert.That(Rolls(one).Distinct().Count(), Is.EqualTo(3)); // 카운터가 이어진다
            Assert.That(Rolls(other), Is.Not.EqualTo(Rolls(one)));
            Assert.That(one.RandomCalls, Is.EqualTo(3));
        }

        [Test]
        public void TheDefaultSeedComesFromTheWorldTheEntityAndTheBot()
        {
            var one = _world.NewBot("core.dice", _blue);
            var two = _world.NewBot("core.dice", _blue);
            _view.Think(one);
            _view.Think(two);
            Assert.That(one.Seed, Is.EqualTo(two.Seed));
            Assert.That(Rolls(one), Is.EqualTo(Rolls(two)));
            Assert.That(_world.NewBot("core.dice", _a).Seed, Is.Not.EqualTo(one.Seed));
        }

        [Test]
        public void OverTheBudgetNothingIsKept()
        {
            var bot = _world.NewBot("core.spin", _blue, stepBudget: 500);
            var result = _view.Think(bot);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.ScriptError));
            Assert.That(result.Error!.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(result.Submissions, Is.Empty);
            Assert.That(((IntValue)bot.Stored("Mind", "turns")).Value, Is.EqualTo(0));
            Assert.That(bot.Version, Is.EqualTo(0));
        }

        [Test]
        public void AHiddenOrGoneEntityIsNotPlayed()
        {
            var bot = _world.NewBot("core.idle", _a);
            _world.Destroy(_a);
            Assert.That(_view.Think(bot).Outcome, Is.EqualTo(ThinkOutcome.ArgumentsRejected));
        }

        [Test]
        public void UnknownSettingsAreRefused()
        {
            var e = Assert.Throws<WorldException>(() =>
                _world.NewBot("core.hunter", _blue, new Dictionary<string, Value> { ["hit"] = new IntValue(1) }))!;
            Assert.That(e.Message, Is.EqualTo("the bot core.hunter has no setting 'hit'"));
        }

        [Test]
        public void TheHostSavesAndRestoresABot()
        {
            var bot = _world.NewBot("core.hunter", _blue, new Dictionary<string, Value> { ["hits"] = new IntValue(1) }, seed: 3);
            _view.Think(bot);
            var dice = _world.NewBot("core.dice", _blue, seed: 3);
            _view.Think(dice);

            var back = _world.LoadBot(bot.Save(), out var warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(back.Bot, Is.EqualTo("core.hunter"));
            Assert.That(back.Me, Is.EqualTo(_blue));
            Assert.That(back.Seed, Is.EqualTo(3));
            Assert.That(((IntValue)back.Settings["hits"]).Value, Is.EqualTo(1));
            Assert.That(((IntValue)back.Stored("Mind", "turns")).Value, Is.EqualTo(1));
            Assert.That(back.Save(), Is.EqualTo(bot.Save()));

            // 난수 카운터도 이어진다: 되살린 봇의 다음 수는 원래 봇의 다음 수와 같다
            var diceBack = _world.LoadBot(dice.Save(), out _);
            _view.Think(dice);
            _view.Think(diceBack);
            Assert.That(Rolls(diceBack), Is.EqualTo(Rolls(dice)));
        }

        [Test]
        public void BotStateIsOnlyForBots()
        {
            Assert.That(_world.Bots, Is.EqualTo(new[] { "core.chooser", "core.dice", "core.hunter", "core.idle", "core.spin" }));
            var e = Assert.Throws<WorldException>(() => _world.Call("core.note", new IntValue(1)))!;
            Assert.That(e.Message, Does.Contain("only a @bot can use it"));
        }
    }
}
