using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 숨긴 정보가 있는 게임의 시험 두기 (spec-gaps G462·G464): 시험 판은 봇이 본 판이다. 못 보는 엔티티는 없고, 못 보는 컴포넌트는 값이 지워져
    /// 붙지 않은 것처럼 보이며, 봇은 attach로 추측을 채운다.
    /// </summary>
    public class ImagineHiddenTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Card\", face=field(int, default=0))\ncomponent(\"Up\", on=field(bool, default=False))\n" +
            "deftype(\"Side\", components=[\"Up\"])\ndeftype(\"Deck\", components=[\"Card\", \"Up\"])\ndeftype(\"Ghost\", components=[\"Up\"])\n" +
            "bot_state(\"Mind\", seen=field(list[int], default=[]), note=field(str, default=\"\"))\n";

        private const string Defs =
            "Side(\"Red\")\nDeck(\"Card\")\nGhost(\"Ghost\")\n" +
            // 뒤집지 않은 카드의 앞면과 유령은 아무도 못 본다
            "@view_rule(Card)\ndef face_down(viewer, entity):\n    return entity.Up.on\n" +
            "@view_rule(Ghost)\ndef unseen(viewer, entity):\n    return False\n" +
            "@command(free=True, acts_for=None)\ndef Flip(c: Ref[Deck]):\n    c.Up.on = True\n    c.Up.on = c.Card.face > 0\n" +
            "@bot\ndef peek(me: Ref[Side]):\n" +
            "    c = [d for d in query(Deck)][0]\n" +
            "    with imagine():\n" +
            "        hidden = 1 if c.Card is None else 0\n" +
            "        ghosts = len([g for g in query(Ghost)]) + len([d for d in query(Card)])\n" +
            "        r = play(\"Flip\", c=c)\n" +
            "        bot.Mind.note = r.outcome\n" +
            "        attach(c, Card(face=7))\n" +
            "        r2 = play(\"Flip\", c=c)\n" +
            "        guessed = c.Card.face if r2.ok else -1\n" +
            "        with imagine():\n" +
            "            inner = c.Card.face\n" +
            "    bot.Mind.seen = [hidden, ghosts, guessed, inner, 1 if c.Card is None else 0]\n";

        private const string Tell =
            "Side(\"Blue\")\n" +
            "@command(free=True, acts_for=None)\ndef Tell(p: Ref[Side]):\n" +
            "    c = [d for d in query(Deck)][0]\n" +
            "    r = ask(p, \"tell\", choices=[c.Card.face, 0], name=\"tell\", data={\"face\": c.Card.face})\n" +
            // 질문의 뼈대(이름·선택지 번호)는 읽히고 번호로 답할 수 있다
            "@bot\ndef shape(me: Ref[Side]):\n" +
            "    with imagine():\n" +
            "        q = questions()[0]\n" +
            "        a = play(q, \"engine.pick\", choice=q.choices[1].index)\n" +
            "        seen = [len(q.choices), q.choices[1].index, 1 if a.ok else 0, 1 if q.name == \"tell\" else 0]\n" +
            "    bot.Mind.seen = seen\n" +
            "@bot\ndef snoop(me: Ref[Side]):\n    with imagine():\n        x = questions()[0].data[\"face\"]\n" +
            "@bot\ndef snoop_choice(me: Ref[Side]):\n    with imagine():\n        x = questions()[0].choices[0].value + 1\n";

        private (World World, BotState Shape, ThinkResult Snoop, ThinkResult SnoopChoice) Told()
        {
            Mod("core", schema: Schema, defs: Defs + Tell);
            var world = new World(LoadClean());
            var card = world.Create("core:Card");
            world.Set(card, world.Member("Card", "face"), new IntValue(42));
            var red = world.Create("core:Red");
            var blue = world.Create("core:Blue");
            world.Seat(red, "bot", SeatKind.Ai);
            world.Seat(blue, "other", SeatKind.Human);
            // 파랑에게 숨긴 앞면으로 계산한 질문을 연다. 빨강 봇은 블록 밖에서 이 질문을 못 본다
            Assert.That(world.Submit("host", "core.Tell", new Dictionary<string, Value> { ["p"] = new EntityValue(blue) }).Succeeded, Is.True);
            var view = world.ViewAs("bot");
            var shape = world.NewBot("core.shape", red);
            var result = view.Think(shape);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            return (world, shape, view.Think(world.NewBot("core.snoop", red)), view.Think(world.NewBot("core.snoop_choice", red)));
        }

        [Test]
        public void AQuestionToAnotherSeatShowsOnlyItsShapeOnTheCopy()
        {
            var (_, shape, snoop, snoopChoice) = Told();
            Assert.That(((ListValue)shape.Stored("Mind", "seen")).Items.Select(v => ((IntValue)v).Value), Is.EqualTo(new[] { 2L, 1L, 1L, 1L }));
            // 내용(data, 선택지 값)은 가려짐이라 숨긴 앞면 42를 읽을 수 없다
            Assert.That(snoop.Outcome, Is.EqualTo(ThinkOutcome.ScriptError));
            Assert.That(snoop.Error!.Message, Does.Not.Contain("42"));
            Assert.That(snoopChoice.Outcome, Is.EqualTo(ThinkOutcome.ScriptError));
            Assert.That(snoopChoice.Error!.Message, Does.Contain("hidden"));
        }

        [Test]
        public void TheCopyHoldsOnlyWhatTheBotSees()
        {
            Mod("core", schema: Schema, defs: Defs);
            var world = new World(LoadClean());
            var card = world.Create("core:Card");
            world.Set(card, world.Member("Card", "face"), new IntValue(42));
            world.Create("core:Ghost");
            var red = world.Create("core:Red");
            world.Seat(red, "bot", SeatKind.Ai);
            byte[] before = world.Save();

            var bot = world.NewBot("core.peek", red);
            var result = world.ViewAs("bot").Think(bot);

            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            // 앞면은 가려져 있고 유령은 없다. 모르는 카드를 뒤집는 커맨드는 None을 읽어 실패한다. 추측 7을 붙이면 그 값으로 돈다.
            // 안쪽 블록은 바깥 시험 판의 추측을 그대로 본다. 진짜 앞면 42는 어디에도 나오지 않는다
            Assert.That(((ListValue)bot.Stored("Mind", "seen")).Items.Select(v => ((IntValue)v).Value), Is.EqualTo(new[] { 1L, 0L, 7L, 7L, 1L }));
            Assert.That(((StrValue)bot.Stored("Mind", "note")).Value, Is.EqualTo("script_error"));
            Assert.That(world.Save(), Is.EqualTo(before));
        }
    }
}
