using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 선택지 답도 <c>LegalResponses</c>의 수 <c>engine.pick(by=, choice=)</c>로 나오고 <c>Respond</c>로 낸다 (spec-gaps G272).
    /// 커널 안에서는 <c>Answer</c>와 같은 길이다.
    /// </summary>
    public class PickMoveTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0))\n" +
            "component(\"Fog\", hidden=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n" +
            "deftype(\"Unit\", components=[\"Fog\"])\n";

        private const string Defs =
            "Party(\"Side\")\nUnit(\"Scout\")\n" +
            "@view_rule(Unit)\n" +
            "def fog(viewer, entity):\n" +
            "    return not entity.Fog.hidden\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Open(a: Ref[Party], b: Ref[Party], u: Ref[Unit]):\n" +
            "    r = ask([a, b], \"go\", choices=[\"left\", u], responses=[\"Wait\"])\n" +
            "    r.by.Score.v = 1000 if r.response is None else 2000\n" +
            "    r.by.Score.v += r.index if r.index is not None else 0\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Wait(party: Ref[Party]):\n" +
            "    pass\n";

        private World _w = null!;
        private EntityId _a, _b, _scout;
        private long _q;

        [SetUp]
        public void NewGame()
        {
            Mod("core", schema: Schema, defs: Defs);
            _w = new World(LoadClean());
            _a = _w.Create("core:Side");
            _b = _w.Create("core:Side");
            _w.Seat(_a, "a");
            _w.Seat(_b, "b");
            _scout = _w.Create("core:Scout");
            var opened = _w.Submit("host", "core.Open", new Dictionary<string, Value>
            {
                ["a"] = new EntityValue(_a), ["b"] = new EntityValue(_b), ["u"] = new EntityValue(_scout),
            });
            Assert.That(opened.Succeeded, Is.True, opened.Error?.ToString());
            _q = opened.OpenedRequest!.Id;
        }

        private string Show(ResponseMove m) =>
            m.Command + " " + (m.By == _a ? "A" : "B") +
            (m.Command == World.PickCommand
                ? " " + ((IntValue)m.Arguments[World.PickChoice]).Value + (((EntityValue)m.Arguments[World.PickBy]).Id == m.By ? "" : " (by differs)")
                : "");

        private long Score(EntityId p) => ((IntValue)_w.Get(p, _w.Member("Score", "v"))).Value;

        [Test]
        public void ChoicesComeFirstAsPickMovesForEachAskedEntity()
        {
            Assert.That(_w.LegalResponses(_q, null, 100).Moves.Select(Show), Is.EqualTo(new[]
            {
                "engine.pick A 0", "engine.pick A 1", "engine.pick B 0", "engine.pick B 1", "core.Wait A", "core.Wait B",
            }));
            Assert.That(_w.LegalResponses(_q, _b, 100).Moves.Select(Show), Is.EqualTo(new[] { "engine.pick B 0", "engine.pick B 1", "core.Wait B" }));
        }

        [TestCase(6, 6, false)]
        [TestCase(5, 5, true)]
        [TestCase(4, 4, true)]
        [TestCase(3, 3, true)]
        public void PickMovesCountTowardsTheMaximum(int max, int count, bool truncated)
        {
            var list = _w.LegalResponses(_q, null, max);
            Assert.That((list.Moves.Count, list.Truncated), Is.EqualTo((count, truncated)));
        }

        [Test]
        public void APickMoveAnswersLikeAnswerAndTheFlowSeesNoResponse()
        {
            var move = _w.LegalResponses(_q, _b, 100).Moves[1];
            var r = _w.Respond("b", _q, move.Command, move.Arguments);
            Assert.That(r.Succeeded, Is.True, r.Message);
            Assert.That(Score(_b), Is.EqualTo(1001)); // r.response는 None, r.index는 1
        }

        [Test]
        public void TheGateStillDecidesWhoMayPick()
        {
            var move = _w.LegalResponses(_q, _b, 100).Moves[0];
            var r = _w.Respond("a", _q, move.Command, move.Arguments);
            Assert.That(r.Succeeded, Is.False, "connection a does not sit on B");
            Assert.That(Score(_b), Is.EqualTo(0));
        }

        [Test]
        public void AViewKeepsAPickOfAHiddenChoice()
        {
            _w.Set(_scout, _w.Member("Fog", "hidden"), BoolValue.True);
            Assert.That(_w.ViewAs("a").LegalResponses(_q, null, 100).Moves.Select(Show), Is.EqualTo(new[]
            {
                "engine.pick A 0", "engine.pick A 1", "core.Wait A",
            }), "the scout is hidden, but its choice keeps its place and can be picked by number");
        }

        [Test]
        public void AnOutOfRangePickIsABadChoice()
        {
            var r = _w.Respond("a", _q, World.PickCommand, new Dictionary<string, Value>
            {
                [World.PickBy] = new EntityValue(_a), [World.PickChoice] = new IntValue(2),
            });
            Assert.That((r.Outcome, r.Message), Is.EqualTo((AnswerOutcome.BadChoice, "request " + _q + " has 2 choices, so 2 is out of range")));
        }

        [TestCase("choice", null, "engine.pick needs by= with the answering entity")]
        [TestCase("by", null, "engine.pick needs choice= with the index of the choice")]
        [TestCase("by", "who", "engine.pick has no argument 'who'")]
        public void AMalformedPickIsACallerError(string keep, string? extra, string message)
        {
            var args = new Dictionary<string, Value>();
            if (keep == "by") args[World.PickBy] = new EntityValue(_a);
            if (keep == "choice") args[World.PickChoice] = new IntValue(0);
            if (extra != null)
            {
                args[World.PickChoice] = new IntValue(0);
                args[extra] = new IntValue(1);
            }
            var e = Assert.Throws<WorldException>(() => _w.Respond("a", _q, World.PickCommand, args))!;
            Assert.That(e.Message, Is.EqualTo(message));
        }
    }
}
