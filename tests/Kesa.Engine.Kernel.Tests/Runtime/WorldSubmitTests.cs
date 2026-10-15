using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>제출: 자유 커맨드 → 인자 → 입구 → 전제 조건 (spec S6, spec-gaps G122~G124·G131).</summary>
    public class WorldSubmitTests : LoaderTestBase
    {
        internal const string Schema =
            "component(\"Seat\", player=field(str, default=\"\"))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "component(\"Moves\", left=field(int, default=1))\n" +
            "deftype(\"Party\", components=[\"Seat\"])\n" +
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
            "@command(free=True, acts_for=lambda unit: unit.Moves.nope)\n" +
            "def Broken(unit: Ref[Unit]):\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Crash(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        require(party.Seat.nope > 0, text(\"msg.never\"))\n" +
            "    return 0\n";

        private const string Policy =
            "@submit_policy\n" +
            "def may_act(issuer, target):\n" +
            "    return target is None or target.Seat.player == issuer\n";

        private World _world = null!;
        private EntityId _red, _blue, _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Commands + Policy);
            _world = new World(LoadClean());
            var seat = _world.Member("Seat", "player");
            _red = _world.Create("Red");
            _world.Set(_red, seat, new StrValue("alice"));
            _blue = _world.Create("Blue");
            _world.Set(_blue, seat, new StrValue("bob"));
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
        public void ThePolicyDeniesActingForAnotherParty()
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
        public void ActsForNoneStillAsksThePolicyWithNone()
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

    /// <summary>정책 함수가 없거나 이상할 때 (G124·G131).</summary>
    public class WorldSubmitPolicyTests : LoaderTestBase
    {
        private static Dictionary<string, Value> Chat => new Dictionary<string, Value> { ["msg"] = new StrValue("hi") };

        [Test]
        public void WithoutAPolicyEveryIssuerMayAct()
        {
            Mod("banking", schema: WorldSubmitTests.Schema, defs: WorldSubmitTests.Commands);
            var world = new World(LoadClean());
            var red = world.Create("Red");
            var scout = world.Create("Scout");
            world.Set(scout, world.Member("Owner", "party"), new EntityValue(red));
            var r = world.Submit("anyone", "banking.Rest", new Dictionary<string, Value> { ["unit"] = new EntityValue(scout) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
        }

        [Test]
        public void TheHostCanRequireAPolicy()
        {
            Mod("banking", schema: WorldSubmitTests.Schema, defs: WorldSubmitTests.Commands);
            var loaded = LoadClean();
            var e = Assert.Throws<WorldException>(() => new World(loaded, requireSubmitPolicy: true));
            Assert.That(e!.Code, Is.EqualTo(Codes.RunNoSubmitPolicy));
        }

        [Test]
        public void ARequiredPolicyThatExistsIsFine()
        {
            Mod("banking", schema: WorldSubmitTests.Schema,
                defs: WorldSubmitTests.Commands + "@submit_policy\ndef gate(issuer, target):\n    return True\n");
            var world = new World(LoadClean(), requireSubmitPolicy: true);
            Assert.That(world.Submit("x", "banking.Chat", Chat).Succeeded, Is.True);
        }

        [Test]
        public void APolicyMustReturnABool()
        {
            Mod("banking", schema: WorldSubmitTests.Schema,
                defs: WorldSubmitTests.Commands + "@submit_policy\ndef gate(issuer, target):\n    return 1\n");
            var r = new World(LoadClean()).Submit("x", "banking.Chat", Chat);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("the submit policy 'gate' for banking.Chat: it must return True or False, not int"));
        }

        [Test]
        public void ThePolicySeesTheIssuerAsAString()
        {
            Mod("banking", schema: WorldSubmitTests.Schema,
                defs: WorldSubmitTests.Commands + "@submit_policy\ndef gate(issuer, target):\n    return issuer == \"host\"\n");
            var world = new World(LoadClean());
            Assert.That(world.Submit("host", "banking.Chat", Chat).Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(world.Submit("guest", "banking.Chat", Chat).Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
        }
    }
}
