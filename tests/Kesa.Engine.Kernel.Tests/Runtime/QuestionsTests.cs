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
    /// 스크립트가 열린 질문을 읽는다 (spec-gaps G381·G383): <c>questions()</c>는 이 연결이 답할 수 있는 질문 목록이고, 이름을 주면 그 종류만이다.
    /// 화면 함수·<c>@ui_action</c>·<c>@bot</c>에서만 쓰고, 화면 함수의 영수증은 질문이 열리고 닫히면 바뀐다.
    /// </summary>
    public class QuestionsTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\ndeftype(\"Party\", components=[\"Hp\"])\n" +
            "ui_state(\"Seen\", count=field(int, default=0))\n" +
            "bot_state(\"Mind\", count=field(int, default=0))\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\n" +
            "@command(free=True, acts_for=None)\ndef Strike(p: Ref[Party], n: int):\n" +
            "    r = ask(p, \"hit-title\", name=\"hit\", choices=[option(\"hold\", style=\"calm\"), \"retreat\"], data={\"n\": n})\n" +
            "    p.Hp.v += r.index\n" +
            "@command(acts_for=lambda p: p)\ndef Move(p: Ref[Party]):\n    pass\n" +
            "@command(free=True, acts_for=None)\ndef Turn(p: Ref[Party]):\n    ask(p, \"turn\", name=\"turn\", responses=[\"Move\"], hint=\"bar\")\n" +
            "def hits():\n    return [q.data[\"n\"] for q in questions(\"hit\")]\n" +
            "def names():\n    return [q.name for q in questions()]\n" +
            "def shape():\n    q = questions(\"hit\")[0]\n    return [[c.index, c.value, c.style] for c in q.choices] + [q.hint, q.title]\n" +
            "def turn():\n    q = questions(\"turn\")[0]\n    return [q.responses, q.hint, [w.Hp.v for w in q.who]]\n" +
            "def wrong():\n    return questions(3)\n" +
            "@ui_action\ndef count():\n    ui.Seen.count = len(questions(\"hit\"))\n" +
            "@ui_action\ndef keep():\n    submit(\"Strike\", p=questions()[0], n=1)\n" +
            "@bot\ndef counter(me: Ref[Party]):\n    bot.Mind.count = len(questions())\n";

        private World _world = null!;
        private EntityId _red, _blue;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("core:Red");
            _blue = _world.Create("core:Blue");
            _world.Seat(_red, "alice");
            _world.Seat(_blue, "bob");
        }

        private void Strike(EntityId p, long n) =>
            Assert.That(_world.Submit("alice", "core.Strike",
                new Dictionary<string, Value> { ["p"] = new EntityValue(p), ["n"] = new IntValue(n) }).Succeeded, Is.True);

        private static object?[] Plain(Value v) => ((ListValue)v).Items.Select(Unwrap).ToArray();

        private static object? Unwrap(Value v) => v switch
        {
            IntValue i => i.Value,
            StrValue s => s.Value,
            NoneValue _ => null,
            ListValue l => Plain(l),
            _ => v.KindName,
        };

        [Test]
        public void EachConnectionSeesOnlyTheQuestionsItCanAnswerAndANamePicksAKind()
        {
            Strike(_red, 1);
            Strike(_blue, 2);
            Strike(_red, 3);
            Assert.That(_world.Submit("alice", "core.Turn", new Dictionary<string, Value> { ["p"] = new EntityValue(_red) }).Succeeded, Is.True);
            var alice = _world.ViewAs("alice");
            Assert.That(Plain(alice.Call("core.hits").Value), Is.EqualTo(new object[] { 1L, 3L }));
            Assert.That(Plain(alice.Call("core.names").Value), Is.EqualTo(new object[] { "hit", "hit", "turn" }));
            Assert.That(Plain(_world.ViewAs("bob").Call("core.hits").Value), Is.EqualTo(new object[] { 2L }));
            Assert.That(Plain(_world.ViewAs("carol").Call("core.names").Value), Is.Empty);
        }

        [Test]
        public void AQuestionShowsItsChoicesResponsesAndWhoAnswers()
        {
            Strike(_red, 1);
            Assert.That(_world.Submit("alice", "core.Turn", new Dictionary<string, Value> { ["p"] = new EntityValue(_red) }).Succeeded, Is.True);
            var alice = _world.ViewAs("alice");
            Assert.That(Plain(alice.Call("core.shape").Value),
                Is.EqualTo(new object?[] { new object?[] { 0L, "hold", "calm" }, new object?[] { 1L, "retreat", null }, null, "hit-title" }));
            Assert.That(Plain(alice.Call("core.turn").Value),
                Is.EqualTo(new object[] { new object[] { "core.Move" }, "bar", new object[] { 3L } }));
        }

        [Test]
        public void AScreenFunctionThatReadsQuestionsIsRedoneWhenOneOpensOrCloses()
        {
            var alice = _world.ViewAs("alice");
            var before = alice.Call("core.hits");
            Assert.That(before.Changed(_world), Is.False);
            Strike(_red, 1);
            Assert.That(before.Changed(_world), Is.True);
            var open = alice.Call("core.hits");
            Assert.That(Plain(open.Value), Is.EqualTo(new object[] { 1L }));
            Assert.That(open.Changed(_world), Is.False);
            Assert.That(_world.Answer("alice", _world.OpenRequests.Single().Id, _red, 0).Succeeded, Is.True);
            Assert.That(open.Changed(_world), Is.True);
        }

        [Test]
        public void AUiActionAndABotReadTheirOwnQuestions()
        {
            Strike(_red, 1);
            Strike(_red, 2);
            Strike(_blue, 3);
            var ui = _world.NewUiState();
            Assert.That(_world.ViewAs("alice").Act(ui, "core.count", new Dictionary<string, Value>()).Succeeded, Is.True);
            Assert.That(((IntValue)ui.Stored("Seen", "count")).Value, Is.EqualTo(2));
            var bot = _world.NewBot("core.counter", _blue, new Dictionary<string, Value>());
            Assert.That(_world.ViewAs("bob").Think(bot).Outcome, Is.EqualTo(ThinkOutcome.Thought));
            Assert.That(((IntValue)bot.Stored("Mind", "count")).Value, Is.EqualTo(1));
        }

        [Test]
        public void AQuestionCannotBeKeptOrSubmitted()
        {
            Strike(_red, 1);
            var act = _world.ViewAs("alice").Act(_world.NewUiState(), "core.keep", new Dictionary<string, Value>());
            Assert.That(act.Succeeded, Is.False);
            Assert.That(act.Error!.Message, Does.Contain("holds a question from questions(), which cannot be kept"));
        }

        [Test]
        public void ANameMustBeAStr()
        {
            var e = Assert.Throws<WorldException>(() => _world.ViewAs("alice").Call("core.wrong"))!;
            Assert.That(e.Message, Does.Contain("questions() takes nothing or the name of a kind of question (str)"));
        }

        [Test]
        public void WithoutAConnectionThereAreNoQuestionsToRead()
        {
            var e = Assert.Throws<WorldException>(() => _world.Call("core.names"))!;
            Assert.That(e.Message, Does.Contain("questions() can only be used on a screen, in a @ui_action or in a @bot"));
        }

        [TestCase("@command(free=True, acts_for=None)\ndef Peek():\n    n = len(questions())\n", "the @command 'Peek'")]
        [TestCase("def helper():\n    return questions()\n@command(free=True, acts_for=None)\ndef Peek():\n    n = len(helper())\n",
            "the @command 'Peek'")]
        [TestCase("@on(Tick)\ndef tick(s):\n    n = questions\n", "the @on handler 'tick'")]
        public void GameCodeCannotReadQuestions(string defs, string who)
        {
            Mod("core", schema: Schema, defs: "Party(\"Red\")\n" + defs);
            Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdUiEffect);
            Assert.That(d.Message, Is.EqualTo(who + " reads the open questions of a connection (questions()), so it cannot run as game code"));
        }
    }
}
