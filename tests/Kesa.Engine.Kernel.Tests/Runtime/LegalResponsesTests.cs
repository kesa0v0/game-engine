using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>열린 질문에 응답 커맨드로 낼 수 있는 수 (spec-gaps G232).</summary>
    public class LegalResponsesTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Banner\", motto=field(str, default=\"\"))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "component(\"Pos\", x=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Banner\"])\n" +
            "deftype(\"Unit\", components=[\"Owner\", \"Pos\"])\n";

        private const string Defs =
            "Party(\"Side\")\nUnit(\"Pawn\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Open(a: Ref[Party], b: Ref[Party]):\n" +
            "    r = ask([a, b], \"go\", responses=[\"Step\", \"Push\", \"Wait\", \"Bid\"])\n" +
            // 답하는 진영이 매개변수 그대로: 엔진이 질문 대상으로 채운다
            "@command(acts_for=lambda party: party)\n" +
            "def Step(party: Ref[Party], unit: Ref[Unit], d: int):\n" +
            "    with preconditions:\n" +
            "        choose(unit, where=lambda u: u.Owner.party == party)\n" +
            "        choose(d, from_=[1, -1])\n" +
            "    unit.Pos.x += d\n" +
            // 답하는 진영을 인자에서 끌어냄: 펼친 뒤 질문 대상인 것만
            "@command(acts_for=lambda unit: unit.Owner.party)\n" +
            "def Push(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        choose(unit, where=lambda u: True)\n" +
            "    unit.Pos.x += 5\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Wait(party: Ref[Party]):\n" +
            "    pass\n" +
            // choose도 기본값도 없는 매개변수: 나열할 수 없다
            "@command(acts_for=lambda party: party)\n" +
            "def Bid(party: Ref[Party], amount: int):\n" +
            "    pass\n";

        private Kesa.Engine.Loading.ContentLoadResult _content = null!;
        private World _w = null!;
        private EntityId _a, _b, _c;
        private long _q;

        [SetUp]
        public void NewGame()
        {
            Mod("core", schema: Schema, defs: Defs);
            _content = LoadClean();
            _w = new World(_content);
            _a = Party("a");
            _b = Party("b");
            _c = Party("c");
            foreach (var p in new[] { _a, _b, _c }) _w.Create("core:Pawn", new Init(_w.Member("Owner", "party"), new EntityValue(p)));
            var opened = _w.Submit("host", "core.Open", new Dictionary<string, Value>
            {
                ["a"] = new EntityValue(_a), ["b"] = new EntityValue(_b),
            });
            Assert.That(opened.Succeeded, Is.True, opened.Error?.ToString());
            _q = opened.OpenedRequest!.Id;
        }

        private EntityId Party(string conn)
        {
            var p = _w.Create("core:Side");
            _w.Seat(p, conn);
            return p;
        }

        private string Name(EntityId id) => id == _a ? "A" : id == _b ? "B" : id == _c ? "C" : "unit of " + Name(Owner(id));

        private EntityId Owner(EntityId unit) => ((EntityValue)_w.Get(unit, _w.Member("Owner", "party"))).Id;

        private string Show(ResponseMove m)
        {
            var parts = new List<string> { m.Command.Substring("core.".Length), "by " + Name(m.By) };
            if (m.Arguments.TryGetValue("unit", out var u)) parts.Add(Name(((EntityValue)u).Id));
            if (m.Arguments.TryGetValue("d", out var d)) parts.Add(((IntValue)d).Value.ToString());
            return string.Join(" ", parts);
        }

        [Test]
        public void TheEngineFillsTheAnswererAndKeepsOnlyTheAskedEntities()
        {
            var list = _w.LegalResponses(_q, null, 100);
            Assert.That(list.Moves.Select(Show), Is.EqualTo(new[]
            {
                "Step by A unit of A 1", "Step by A unit of A -1", "Step by B unit of B 1", "Step by B unit of B -1",
                // C의 유닛도 choose 후보지만 acts_for가 C라 질문 대상이 아니다
                "Push by A unit of A", "Push by B unit of B",
                "Wait by A", "Wait by B",
            }));
            Assert.That(list.Truncated, Is.False);
            Assert.That(list.Needs.Select(n => n.Command + " " + string.Join(",", n.Parameters)), Is.EqualTo(new[] { "core.Bid amount" }));
        }

        [Test]
        public void EveryListedMoveIsAcceptedByRespond()
        {
            var save = _w.Save();
            foreach (var move in _w.LegalResponses(_q, null, 100).Moves)
            {
                var conn = move.By == _a ? "a" : "b";
                var r = _w.Respond(conn, _q, move.Command, move.Arguments);
                Assert.That(r.Succeeded, Is.True, Show(move) + ": " + r.Outcome + " " + r.Message);
                _w = World.Load(_content, save).World!;
            }
        }

        [Test]
        public void ByNarrowsToOneAskedEntity()
        {
            Assert.That(_w.LegalResponses(_q, _b, 100).Moves.Select(Show), Is.EqualTo(new[]
            {
                "Step by B unit of B 1", "Step by B unit of B -1", "Push by B unit of B", "Wait by B",
            }));
            Assert.That(_w.LegalResponses(_q, _c, 100).Moves, Is.Empty, "C is not asked");
        }

        [TestCase(8, 8, false)]
        [TestCase(7, 7, true)]
        [TestCase(4, 4, true)]
        [TestCase(1, 1, true)]
        public void TheMaximumCountsAllCommandsTogether(int max, int count, bool truncated)
        {
            var list = _w.LegalResponses(_q, null, max);
            Assert.That((list.Moves.Count, list.Truncated), Is.EqualTo((count, truncated)));
        }

        [Test]
        public void AViewListsOnlyWhatThatConnectionMayAnswer()
        {
            Assert.That(_w.ViewAs("a").LegalResponses(_q, null, 100).Moves.Select(Show), Is.EqualTo(new[]
            {
                "Step by A unit of A 1", "Step by A unit of A -1", "Push by A unit of A", "Wait by A",
            }));
            var e = Assert.Throws<WorldException>(() => _w.ViewAs("c").LegalResponses(_q, null, 100))!;
            Assert.That(e.Message, Does.Contain("there is no open request"));
        }

        [Test]
        public void AnUnknownRequestIsAnError()
        {
            var e = Assert.Throws<WorldException>(() => _w.LegalResponses(999, null, 10))!;
            Assert.That(e.Message, Is.EqualTo("there is no open request 999"));
        }
    }
}
