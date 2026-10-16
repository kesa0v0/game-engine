using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>사전 조건의 <c>choose</c>가 제출된 인자를 검사한다 (spec S2·S6·C7, spec-gaps G183).</summary>
    public class WorldChooseTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "component(\"Place\", city=ref(\"City\", optional=True), next=ref(\"City\", optional=True), " +
            "home=ref(\"City\", optional=True))\n" +
            "component(\"Moves\", left=field(int, default=1))\n" +
            "deftype(\"Party\", components=[\"Moves\"])\n" +
            "deftype(\"City\", components=[\"Moves\"])\n" +
            "deftype(\"Unit\", components=[\"Owner\", \"Place\", \"Moves\"])\n";

        private const string Defs =
            "Party(\"Red\")\nCity(\"Rome\")\n" +
            "Unit(\"Scout\")\n" +
            "def others(items, x):\n    out = []\n    for i in items:\n        if i != x:\n            out = out + [i]\n    return out\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Move(party: Ref[Party], unit: Ref[Unit], dest: Ref[City]):\n" +
            "    with preconditions:\n" +
            "        choose(unit, where=lambda u: u.Owner.party == party)\n" +
            "        here = unit.Place.city\n" +
            "        choose(dest, from_=others([unit.Place.next, unit.Place.home], here))\n" +
            "        require(unit.Moves.left > 0, text(\"msg.no_moves\"))\n" +
            "    unit.Place.city = dest\n" +
            "    unit.Moves.left -= 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Pick(n: int, word: str = \"a\"):\n" +
            "    with preconditions:\n" +
            "        choose(n, from_=[1, 2, 3])\n" +
            "        choose(word, from_=[\"a\", \"b\"])\n" +
            "    pass\n" +
            "def mine(u):\n    return u.Moves.left > 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Named(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        choose(unit, where=mine)\n" +
            "    pass\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Twice(n: int):\n" +
            "    with preconditions:\n" +
            "        choose(n, from_=[2, 1, 2])\n" +
            "    pass\n" +
            "@command(free=True, acts_for=None)\n" +
            "def NotAList(n: int):\n" +
            "    with preconditions:\n" +
            "        choose(n, from_=3)\n" +
            "    pass\n";

        private World _world = null!;
        private EntityId _red, _blue, _scout, _rome, _ostia;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("Red");
            _blue = _world.Create("Red");
            _rome = _world.Create("Rome");
            _ostia = _world.Create("Rome");
            _scout = _world.Create("Scout");
            _world.Set(_scout, _world.Member("Owner", "party"), new EntityValue(_red));
            _world.Set(_scout, _world.Member("Place", "city"), new EntityValue(_rome));
            _world.Set(_scout, _world.Member("Place", "next"), new EntityValue(_ostia));
            _world.Set(_scout, _world.Member("Place", "home"), new EntityValue(_rome));
        }

        private static string Row(RequireOutcome r) => r.Kind + " " + r.Status + (r.Parameter == null ? "" : " " + r.Parameter);

        private Dictionary<string, Value> MoveArgs(EntityId party, EntityId dest) => new Dictionary<string, Value>
        {
            ["party"] = new EntityValue(party), ["unit"] = new EntityValue(_scout), ["dest"] = new EntityValue(dest),
        };

        [Test]
        public void ACandidatePassesAndTheBodyRuns()
        {
            var r = _world.Submit("host", "core.Move", MoveArgs(_red, _ostia));
            Assert.That(r.Succeeded, Is.True);
            Assert.That(r.Preconditions!.Requires.Select(Row), Is.EqualTo(new[] { "Choose Passed unit", "Choose Passed dest", "Require Passed" }));
            Assert.That(((EntityValue)_world.Get(_scout, _world.Member("Place", "city"))).Id, Is.EqualTo(_ostia));
        }

        [Test]
        public void AnArgumentThatIsNotACandidateIsRejectedLikeAFailedRequire()
        {
            // 남의 유닛(조작된 입구)과 지금 있는 도시: 둘 다 후보가 아니다
            var r = _world.Submit("host", "core.Move", MoveArgs(_blue, _rome));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            Assert.That(r.Preconditions!.Failures.Select(Row), Is.EqualTo(new[] { "Choose Failed unit", "Choose Failed dest" }));
            Assert.That(((EntityValue)_world.Get(_scout, _world.Member("Place", "city"))).Id, Is.EqualTo(_rome));
        }

        [Test]
        public void CheckingThePreconditionsReportsTheSameRows()
        {
            var report = _world.CheckPreconditions("core.Move", MoveArgs(_red, _rome));
            Assert.That(report.Requires.Select(Row), Is.EqualTo(new[] { "Choose Passed unit", "Choose Failed dest", "Require Passed" }));
        }

        [TestCase(2, "b", true)]
        [TestCase(4, "a", false)]
        [TestCase(1, "c", false)]
        public void FromTakesAListOfPlainValues(long n, string word, bool passed)
        {
            var report = _world.CheckPreconditions("core.Pick",
                new Dictionary<string, Value> { ["n"] = new IntValue(n), ["word"] = new StrValue(word) });
            Assert.That(report.Passed, Is.EqualTo(passed));
        }

        [Test]
        public void ADefaultArgumentIsCheckedToo()
        {
            var report = _world.CheckPreconditions("core.Pick", new Dictionary<string, Value> { ["n"] = new IntValue(1) });
            Assert.That(report.Requires.Select(Row), Is.EqualTo(new[] { "Choose Passed n", "Choose Passed word" }));
        }

        [Test]
        public void WhereTakesANamedFunction()
        {
            var args = new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) };
            Assert.That(_world.CheckPreconditions("core.Named", args).Passed, Is.True);
            _world.Set(_scout, _world.Member("Moves", "left"), new IntValue(0));
            Assert.That(_world.CheckPreconditions("core.Named", args).Failures.Select(Row), Is.EqualTo(new[] { "Choose Failed unit" }));
        }

        // ---- 후보와 합법 수 (G184·G188) ----

        private static Dictionary<string, Value> Args(params (string, Value)[] pairs)
        {
            var d = new Dictionary<string, Value>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        private static (string, Value) E(string name, EntityId id) => (name, new EntityValue(id));

        private static string Show(Value v) =>
            v is EntityValue e ? e.Id.ToString() : v is IntValue i ? i.Value.ToString() : ((StrValue)v).Value;

        private static string Show(IReadOnlyDictionary<string, Value> move) =>
            string.Join(" ", move.OrderBy(p => p.Key, System.StringComparer.Ordinal).Select(p => p.Key + "=" + Show(p.Value)));

        private EntityId SecondUnit(long moves)
        {
            var other = _world.Create("Scout");
            _world.Set(other, _world.Member("Owner", "party"), new EntityValue(_red));
            _world.Set(other, _world.Member("Place", "city"), new EntityValue(_ostia));
            _world.Set(other, _world.Member("Place", "next"), new EntityValue(_rome));
            _world.Set(other, _world.Member("Place", "home"), new EntityValue(_ostia));
            _world.Set(other, _world.Member("Moves", "left"), new IntValue(moves));
            return other;
        }

        [Test]
        public void WhereCandidatesAreTheLiveEntitiesOfTheTypeThatPassInIdOrder()
        {
            var other = SecondUnit(0);
            var blues = _world.Create("Scout"); // 주인 없음
            var list = _world.Candidates("core.Move", Args(E("party", _red)), "unit");
            Assert.That(list.Ready, Is.True);
            Assert.That(list.Values.Select(Show), Is.EqualTo(new[] { _scout.ToString(), other.ToString() }));
            Assert.That(blues, Is.Not.EqualTo(other));
        }

        [Test]
        public void ACandidateListNamesTheParametersItStillNeeds()
        {
            Assert.That(_world.Candidates("core.Move", Args(), "unit").Needs, Is.EqualTo(new[] { "party" }));
            // dest의 식은 unit만 읽는다 (here를 거쳐서): party는 필요 없다
            var dest = _world.Candidates("core.Move", Args(), "dest");
            Assert.That(dest.Needs, Is.EqualTo(new[] { "unit" }));
            Assert.That(dest.Values, Is.Empty);
        }

        [Test]
        public void FromCandidatesFollowTheBlockLocalsTheyRead()
        {
            var list = _world.Candidates("core.Move", Args(E("unit", _scout)), "dest");
            Assert.That(list.Values.Select(Show), Is.EqualTo(new[] { _ostia.ToString() }));
        }

        [Test]
        public void ACandidateListDoesNotLookAtRequireOrCost()
        {
            _world.Set(_scout, _world.Member("Moves", "left"), new IntValue(0));
            Assert.That(_world.Candidates("core.Move", Args(E("party", _red)), "unit").Values.Count, Is.EqualTo(1));
        }

        [Test]
        public void FromCandidatesKeepTheFirstOfEqualValues()
        {
            Assert.That(_world.Candidates("core.Twice", Args(), "n").Values.Select(Show), Is.EqualTo(new[] { "2", "1" }));
        }

        [Test]
        public void ARejectedArgumentComesBackInsteadOfCandidates()
        {
            _world.Destroy(_red);
            var list = _world.Candidates("core.Move", Args(E("party", _red)), "unit");
            Assert.That(list.Arguments.Single().Problem, Is.EqualTo(ArgumentProblem.DeadEntity));
            Assert.That(list.Values, Is.Empty);
        }

        [Test]
        public void AParameterWithoutChooseHasNoCandidates()
        {
            var e = Assert.Throws<WorldException>(() => _world.Candidates("core.Move", Args(), "party"))!;
            Assert.That(e.Message, Is.EqualTo("core.Move has no choose(party, ...)"));
        }

        [Test]
        public void LegalMovesExpandEveryChooseAndKeepWhatPassesThePreconditions()
        {
            // 둘째 유닛은 움직일 수 없어(Moves 0) 빠진다
            SecondUnit(0);
            var moves = _world.LegalMoves("core.Move", Args(E("party", _red)), 10);
            Assert.That(moves.Moves.Select(Show), Is.EqualTo(new[]
            {
                "dest=" + _ostia + " party=" + _red + " unit=" + _scout,
            }));
            Assert.That(moves.Truncated, Is.False);
        }

        [Test]
        public void LegalMovesStopAtTheMaximumAndSaySo()
        {
            var other = SecondUnit(1);
            var all = _world.LegalMoves("core.Move", Args(E("party", _red)), 2);
            Assert.That(all.Moves.Count, Is.EqualTo(2));
            Assert.That(all.Truncated, Is.False);

            var cut = _world.LegalMoves("core.Move", Args(E("party", _red)), 1);
            Assert.That(cut.Moves.Select(Show), Is.EqualTo(new[] { "dest=" + _ostia + " party=" + _red + " unit=" + _scout }));
            Assert.That(cut.Truncated, Is.True);
            Assert.That(other, Is.Not.EqualTo(_scout));
        }

        [Test]
        public void LegalMovesExpandAChooseWithADefaultButKeepAGivenValue()
        {
            Assert.That(_world.LegalMoves("core.Pick", Args(), 10).Moves.Select(Show), Is.EqualTo(new[]
            {
                "n=1 word=a", "n=1 word=b", "n=2 word=a", "n=2 word=b", "n=3 word=a", "n=3 word=b",
            }));
            Assert.That(_world.LegalMoves("core.Pick", Args(("word", new StrValue("b"))), 10).Moves.Select(Show),
                Is.EqualTo(new[] { "n=1 word=b", "n=2 word=b", "n=3 word=b" }));
        }

        [Test]
        public void ALegalMoveCanBeSubmittedAsItIs()
        {
            var move = _world.LegalMoves("core.Move", Args(E("party", _red)), 10).Moves.Single();
            Assert.That(_world.Submit("host", "core.Move", move).Succeeded, Is.True);
        }

        [Test]
        public void LegalMovesNeedTheParametersWithoutChoose()
        {
            var e = Assert.Throws<WorldException>(() => _world.LegalMoves("core.Move", Args(), 10))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunMissingValue));
            Assert.That(e.Message, Is.EqualTo("LegalMoves of core.Move needs the argument 'party' (Ref[\"Party\"])"));
        }

        [Test]
        public void NothingChangesWhileListing()
        {
            SecondUnit(1);
            var before = _world.Save();
            _world.LegalMoves("core.Move", Args(E("party", _red)), 10);
            _world.Candidates("core.Move", Args(E("party", _red)), "unit");
            Assert.That(_world.Save(), Is.EqualTo(before));
        }

        [Test]
        public void FromThatIsNotAListIsARuntimeErrorWhenListingToo()
        {
            var e = Assert.Throws<WorldException>(() => _world.Candidates("core.NotAList", Args(), "n"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunType));
            Assert.That(e.Message, Does.Contain("from_= of choose() must be a list, not int"));
        }

        [Test]
        public void FromThatIsNotAListIsARuntimeError()
        {
            var e = Assert.Throws<WorldException>(() =>
                _world.CheckPreconditions("core.NotAList", new Dictionary<string, Value> { ["n"] = new IntValue(3) }))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunType));
            Assert.That(e.Message, Does.Contain("from_= of choose() must be a list, not int"));
        }
    }
}
