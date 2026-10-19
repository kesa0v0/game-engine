using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>제출: 자유 커맨드 → 인자 → 입구(배정 표) → 전제 조건 (spec S6, spec-gaps G122·G123·G131·G236·G237).</summary>
    public class WorldSubmitTests : LoaderTestBase
    {
        internal const string Schema =
            "component(\"Banner\", motto=field(str, default=\"\"))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "component(\"Moves\", left=field(int, default=1))\n" +
            "deftype(\"Party\", components=[\"Banner\"])\n" +
            "deftype(\"Unit\", components=[\"Owner\", \"Moves\"])\n";

        internal const string Commands =
            "Party(\"Red\")\nParty(\"Blue\")\nUnit(\"Scout\")\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Move(party: Ref[Party], unit: Ref[Unit], n: int = 1):\n" +
            "    with preconditions:\n" +
            "        require(unit.Owner.party == party, text(\"msg.not_yours\"))\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=lambda unit: unit.Owner.party)\n" +
            "def Rest(unit: Ref[Unit]):\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Chat(msg: str):\n" +
            "    return 0\n" +
            "@command(acts_for=lambda n: n)\n" +
            "def Answer(n: int):\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=lambda n: n)\n" +
            "def Count(n: int):\n" +
            "    return 0\n" +
            // 리스트로 감싸 타입을 모르게 한다: 실행 중 안전망을 본다 (아니면 G228의 로드 오류)
            "@command(free=True, acts_for=lambda unit: [unit.Moves][0].nope)\n" +
            "def Broken(unit: Ref[Unit]):\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Crash(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        require([party.Banner][0].nope > 0, text(\"msg.never\"))\n" +
            "    return 0\n";

        private World _world = null!;
        private EntityId _red, _blue, _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Commands);
            _world = new World(LoadClean());
            _red = _world.Create("Red");
            _world.Seat(_red, "alice");
            _blue = _world.Create("Blue");
            _world.Seat(_blue, "bob");
            _scout = _world.Create("Scout");
            _world.Set(_scout, _world.Member("Owner", "party"), new EntityValue(_red));
        }

        private SubmitResult Move(string issuer, EntityId party) =>
            _world.Submit(issuer, "banking.Move",
                new Dictionary<string, Value> { ["party"] = new EntityValue(party), ["unit"] = new EntityValue(_scout) });

        [Test]
        public void AnAllowedSubmissionPassesEveryStep()
        {
            var r = Move("alice", _red);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(r.Succeeded, Is.True);
            Assert.That(r.Command, Is.EqualTo("banking.Move"));
            Assert.That(r.Issuer, Is.EqualTo("alice"));
            Assert.That(((EntityValue)r.Target!).Id, Is.EqualTo(_red));
            Assert.That(r.Preconditions!.Passed, Is.True);
            Assert.That(r.Error, Is.Null);
        }

        [Test]
        public void TheResultHoldsTheFilledArguments()
        {
            // 기본값까지 채운 전체 인자 (G116): 호스트가 커맨드 로그에 쓴다
            var r = Move("alice", _red);
            Assert.That(r.Arguments!.Keys, Is.EqualTo(new[] { "n", "party", "unit" }));
            Assert.That(((IntValue)r.Arguments["n"]).Value, Is.EqualTo(1));
        }

        [Test]
        public void TheSeatTableDeniesActingForAnotherParty()
        {
            var r = Move("bob", _red);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
            Assert.That(((EntityValue)r.Target!).Id, Is.EqualTo(_red));
            Assert.That(r.Preconditions, Is.Null); // 입구가 전제 조건보다 먼저 (G122)
        }

        [Test]
        public void OwnershipIsAPreconditionNotTheGate()
        {
            // bob은 Blue로 행동할 수 있지만 Red의 유닛을 움직이는 것은 전제 조건이 막는다 (G122)
            var r = Move("bob", _blue);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            Assert.That(r.Preconditions!.Failures.Single().MessageKey, Is.EqualTo("msg.not_yours"));
        }

        [Test]
        public void ActsForCanBeDerivedFromAnArgument()
        {
            var args = new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) };
            Assert.That(_world.Submit("alice", "banking.Rest", args).Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            var denied = _world.Submit("bob", "banking.Rest", args);
            Assert.That(denied.Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
            Assert.That(((EntityValue)denied.Target!).Id, Is.EqualTo(_red));
        }

        [Test]
        public void ActsForNoneLetsAnyConnectionIn()
        {
            var r = _world.Submit("carol", "banking.Chat", new Dictionary<string, Value> { ["msg"] = new StrValue("hi") });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(r.Target, Is.SameAs(NoneValue.Instance));
        }

        [Test]
        public void ACommandThatIsNotFreeNeedsARequest()
        {
            var r = _world.Submit("alice", "banking.Answer", new Dictionary<string, Value> { ["n"] = new IntValue(1) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.NeedsRequest));
            Assert.That(r.Arguments, Is.Null);
        }

        [Test]
        public void BadArgumentsAreRejectedBeforeTheGate()
        {
            var r = _world.Submit("bob", "banking.Move", new Dictionary<string, Value> { ["party"] = new IntValue(3) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ArgumentsRejected));
            Assert.That(r.ArgumentRejections.Select(x => x.Problem),
                Is.EqualTo(new[] { ArgumentProblem.WrongType, ArgumentProblem.Missing }));
            Assert.That(r.Target, Is.Null);
            Assert.That(r.Arguments, Is.Null);
        }

        [Test]
        public void ActsForMustGiveAnEntityOrNone()
        {
            var r = _world.Submit("alice", "banking.Count", new Dictionary<string, Value> { ["n"] = new IntValue(2) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunType));
            Assert.That(r.Error.Message, Does.Contain("acts_for of banking.Count: it must give an entity or None, not int"));
        }

        [Test]
        public void AScriptErrorInActsForIsAResult()
        {
            var r = _world.Submit("alice", "banking.Broken", new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.StartWith("computing acts_for of banking.Broken: "));
            Assert.That(r.Arguments, Is.Not.Null);
        }

        [Test]
        public void AScriptErrorInThePreconditionsIsAResult()
        {
            var r = _world.Submit("alice", "banking.Crash", new Dictionary<string, Value> { ["party"] = new EntityValue(_red) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.StartWith("computing the command banking.Crash: "));
        }

        [Test]
        public void AnUnknownCommandIsACallerError()
        {
            var e = Assert.Throws<WorldException>(() => _world.Submit("alice", "banking.Mvoe", new Dictionary<string, Value>()));
            Assert.That(e!.Code, Is.EqualTo(Codes.RunUnknownName));
        }
    }

    /// <summary>배정 표: 앉히기·비우기·죽음·되감기·저장 (G236·G237).</summary>
    public class WorldSeatTests : LoaderTestBase
    {
        private World _world = null!;
        private EntityId _red, _blue, _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: WorldSubmitTests.Schema, defs: WorldSubmitTests.Commands +
                "@command(free=True, acts_for=lambda party: party)\n" +
                "def Disband(party: Ref[Party], unit: Ref[Unit]):\n" +
                "    destroy(party)\n" +
                "    unit.Moves.left = unit.Moves.left // 0\n" +
                "@command(free=True, acts_for=lambda party: party)\n" +
                "def Quit(party: Ref[Party]):\n" +
                "    destroy(party)\n");
            _world = new World(LoadClean());
            _red = _world.Create("Red");
            _blue = _world.Create("Blue");
            _scout = _world.Create("Scout");
            _world.Set(_scout, _world.Member("Owner", "party"), new EntityValue(_red));
        }

        private SubmitResult Rest(string issuer) =>
            _world.Submit(issuer, "banking.Rest", new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) });

        private SubmitResult Quit(string issuer, EntityId party) =>
            _world.Submit(issuer, "banking.Quit", new Dictionary<string, Value> { ["party"] = new EntityValue(party) });

        [Test]
        public void AnEntityNobodySitsAtTakesNoCommands()
        {
            Assert.That(Rest("alice").Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
            _world.Seat(_red, "alice");
            Assert.That(Rest("alice").Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(Rest("Alice").Outcome, Is.EqualTo(SubmitOutcome.GateDenied), "connection tags compare exactly");
            _world.Unseat(_red);
            Assert.That(Rest("alice").Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
            _world.Unseat(_red); // 빈자리를 또 비워도 된다
        }

        [Test]
        public void ActsForNoneNeedsNoSeat()
        {
            var r = _world.Submit("spectator", "banking.Chat", new Dictionary<string, Value> { ["msg"] = new StrValue("hi") });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
        }

        [Test]
        public void SeatingAgainHandsTheEntityOver()
        {
            _world.Seat(_red, "alice");
            _world.Seat(_red, "ai-1"); // 사람이 나가고 AI가 넘겨받음
            Assert.That(_world.SeatOf(_red), Is.EqualTo("ai-1"));
            Assert.That(Rest("alice").Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
            Assert.That(Rest("ai-1").Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
        }

        [Test]
        public void OneConnectionCanSitAtManyEntities()
        {
            _world.Seat(_blue, "alice");
            _world.Seat(_red, "alice");
            Assert.That(_world.SeatsOf("alice"), Is.EqualTo(new[] { _red, _blue }), "in id order");
            Assert.That(_world.SeatsOf("bob"), Is.Empty);
            Assert.That(_world.SeatOf(_scout), Is.Null);
            Assert.That(Quit("alice", _blue).Succeeded, Is.True);
        }

        [Test]
        public void SeatingChecksItsInput()
        {
            var empty = Assert.Throws<WorldException>(() => _world.Seat(_red, ""))!;
            Assert.That(empty.Code, Is.EqualTo(Codes.RunSeat));
            Assert.That(empty.Message, Is.EqualTo("a connection tag must not be empty"));
            _world.Destroy(_blue);
            var dead = Assert.Throws<WorldException>(() => _world.Seat(_blue, "bob"))!;
            Assert.That(dead.Code, Is.EqualTo(Codes.RunSeat));
            Assert.That(dead.Message, Is.EqualTo("cannot seat a connection at " + _blue + ": it is not alive"));
        }

        [Test]
        public void SeatingStampsTheEntity()
        {
            long before = _world.Version(_red);
            _world.Seat(_red, "alice");
            long seated = _world.Version(_red);
            Assert.That(seated, Is.Not.EqualTo(before));
            _world.Unseat(_red);
            Assert.That(_world.Version(_red), Is.Not.EqualTo(seated));
        }

        [Test]
        public void ASeatDiesWithItsEntityAndComesBackOnRollback()
        {
            _world.Seat(_red, "alice");
            var failed = _world.Submit("alice", "banking.Disband", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_red), ["unit"] = new EntityValue(_scout),
            });
            Assert.That(failed.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(_world.SeatOf(_red), Is.EqualTo("alice"), "the rolled-back destroy keeps the seat");

            Assert.That(Quit("alice", _red).Succeeded, Is.True);
            Assert.That(_world.SeatOf(_red), Is.Null);
            Assert.That(_world.SeatsOf("alice"), Is.Empty);
        }

        [Test]
        public void SeatsAreSavedAndLoaded()
        {
            _world.Seat(_red, "alice");
            _world.Seat(_blue, "alice");
            var save = _world.Save();
            Assert.That(System.Text.Encoding.UTF8.GetString(save), Does.Contain("\"seats\": [\n    {\n      \"entity\": 1,\n      \"issuer\": \"alice\""));
            var loaded = World.Load(LoadClean(), save).World!;
            Assert.That(loaded.SeatsOf("alice"), Is.EqualTo(new[] { _red, _blue }));
            Assert.That(loaded.Save(), Is.EqualTo(save));
        }
    }
}
