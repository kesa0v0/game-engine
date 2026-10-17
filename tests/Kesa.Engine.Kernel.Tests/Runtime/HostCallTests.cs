using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>스크립트에서 호스트 함수를 부른다 (spec S7, spec-gaps G216·G218·G219·G222).</summary>
    public class HostCallTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Terrain\", cost=field(int, default=1), doubled=derived(int, lambda t: core.math.double(t.cost)))\n" +
            "deftype(\"Tile\", components=[\"Terrain\"])\n";

        private const string Defs =
            "Tile(\"Grass\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Walk(start: Ref[Tile], goal: Ref[Tile]):\n" +
            "    path = core.path.find(start, goal)\n" +
            "    start.Terrain.cost = len(path) * 10 + path[1].Terrain.cost\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Short(start: Ref[Tile], goal: Ref[Tile]):\n" +
            "    path = core.path.find(goal=goal, start=start, max_steps=3)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ByValue(start: Ref[Tile], goal: Ref[Tile]):\n" +
            "    f = core.path.find\n" +
            "    start.Terrain.cost = len(f(start, goal))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def BadArg(start: Ref[Tile]):\n" +
            "    core.path.find(start, \"far\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Missing(start: Ref[Tile]):\n" +
            "    core.path.find(start)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Unknown(start: Ref[Tile]):\n" +
            "    core.path.find(start, start, max_step=1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Tax(at: Ref[Tile]):\n" +
            "    at.Terrain.cost = 7\n" +
            "    core.economy.levy(at)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def TaxInExpression(at: Ref[Tile]):\n" +
            "    f = core.economy.levy\n" +
            "    n = [f(at)]\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Crash(at: Ref[Tile]):\n" +
            "    at.Terrain.cost = 7\n" +
            "    n = core.debug.crash()\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Liar(at: Ref[Tile]):\n" +
            "    n = core.debug.liar()\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Null(at: Ref[Tile]):\n" +
            "    n = core.debug.null()\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Ghost(at: Ref[Tile]):\n" +
            "    n = core.debug.ghost()\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Ghosts(at: Ref[Tile]):\n" +
            "    n = core.debug.ghosts()\n";

        private World _world = null!;
        private EntityId _a, _b, _dead;
        private MemberHandle _cost = null!;
        private readonly List<string> _calls = new List<string>();
        private HostAccess? _access;

        [SetUp]
        public void NewWorld()
        {
            _calls.Clear();
            var host = new HostFunctions();
            var core = host.For("core");
            core.Pure("def path.find(start: Ref[Tile], goal: Ref[Tile], max_steps: int = 50) -> list[Ref[Tile]]", "",
                (EntityId start, EntityId goal, long maxSteps) =>
                {
                    _calls.Add("find " + start + " " + goal + " " + maxSteps);
                    return new List<EntityId> { start, goal };
                });
            core.Pure("def math.double(n: int) -> int", "", (long n) => n * 2);
            core.Writes("def economy.levy(at: Ref[Tile]) -> None", "", (HostAccess access, EntityId at) =>
            {
                _access = access;
                _calls.Add("levy " + at);
            });
            core.Pure<long>("def debug.crash() -> int", "", () => throw new InvalidOperationException("no path"));
            core.Pure("def debug.liar() -> int", "", () => (Value)new StrValue("many"));
            core.Pure("def debug.null() -> list[int]", "", () => (List<long>)null!);
            core.Pure("def debug.ghost() -> Ref[Tile]", "", () => _dead);
            core.Pure("def debug.ghosts() -> list[Ref[Tile]]", "", () => new[] { _a, _dead });

            Mod("core", schema: Schema, defs: Defs);
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var loaded = ContentLoader.Load(_root, order, bag, host);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _world = new World(loaded);
            _a = _world.Create("Grass");
            _b = _world.Create("Grass");
            _dead = _world.Create("Grass");
            _world.Destroy(_dead);
            _cost = _world.Member("Terrain", "cost");
        }

        private SubmitResult Submit(string command, params (string Name, Value Value)[] args)
        {
            var map = new Dictionary<string, Value>();
            foreach (var (name, value) in args) map[name] = value;
            return _world.Submit("host", "core." + command, map);
        }

        private long Cost(EntityId id) => ((IntValue)_world.Get(id, _cost)).Value;

        private static (string, Value) Arg(string name, EntityId id) => (name, new EntityValue(id));

        [Test]
        public void AScriptCallsAHostFunctionAndUsesTheEntitiesItReturns()
        {
            _world.Set(_b, _cost, new IntValue(4));
            var r = Submit("Walk", Arg("start", _a), Arg("goal", _b));
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(_calls, Is.EqualTo(new[] { "find " + _a + " " + _b + " 50" }));
            // 길이 2 * 10 + 돌려받은 둘째 엔티티의 cost 4
            Assert.That(Cost(_a), Is.EqualTo(24));
        }

        [Test]
        public void KeywordArgumentsBindByName()
        {
            Assert.That(Submit("Short", Arg("start", _a), Arg("goal", _b)).Succeeded, Is.True);
            Assert.That(_calls, Is.EqualTo(new[] { "find " + _a + " " + _b + " 3" }));
        }

        [Test]
        public void AHostFunctionIsAValue()
        {
            Assert.That(Submit("ByValue", Arg("start", _a), Arg("goal", _b)).Succeeded, Is.True);
            Assert.That(Cost(_a), Is.EqualTo(2));
        }

        [Test]
        public void APureHostFunctionWorksInADerivedValue()
        {
            _world.Set(_a, _cost, new IntValue(21));
            Assert.That(((IntValue)_world.Get(_a, _world.Member("Terrain", "doubled"))).Value, Is.EqualTo(42));
        }

        [TestCase("BadArg", Codes.RunType, "core.path.find(): 'goal' must be Ref[\"Tile\"], not str")]
        [TestCase("Missing", Codes.RunScript, "core.path.find() is missing the argument 'goal'")]
        [TestCase("Unknown", Codes.RunScript, "core.path.find() has no parameter 'max_step'")]
        public void ArgumentsAreCheckedLikeCommandArguments(string command, string code, string message)
        {
            var r = Submit(command, Arg("start", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(code));
            Assert.That(r.Error.Message, Does.Contain(message));
            Assert.That(_calls, Is.Empty);
        }

        [Test]
        public void AnUnknownKeywordGetsASuggestion()
        {
            var r = Submit("Unknown", Arg("start", _a));
            Assert.That(r.Error!.Hint, Is.EqualTo("did you mean 'max_steps'?"));
        }

        [Test]
        public void AWritingHostFunctionGetsAnAccessorAsAStatement()
        {
            var r = Submit("Tax", Arg("at", _a));
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(_calls, Is.EqualTo(new[] { "levy " + _a }));
            Assert.That(_access, Is.Not.Null);
            Assert.That(Cost(_a), Is.EqualTo(7));
        }

        [Test]
        public void AWritingHostFunctionValueCannotBeCalledInsideAnExpression()
        {
            var r = Submit("TaxInExpression", Arg("at", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
            Assert.That(r.Error.Message, Does.Contain("core.economy.levy() changes the world, so it cannot be called here"));
            Assert.That(_calls, Is.Empty);
        }

        [Test]
        public void AnExceptionInTheHostIsAScriptErrorThatRollsBack()
        {
            var r = Submit("Crash", Arg("at", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunHostFailed));
            Assert.That(r.Error.Message, Does.Contain("host function core.debug.crash failed: InvalidOperationException: no path"));
            Assert.That(r.Error.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(Cost(_a), Is.EqualTo(1));
        }

        [TestCase("Liar", "host function core.debug.liar returned str, but its signature says int")]
        [TestCase("Null", "host function core.debug.null returned None, but its signature says list[int]")]
        public void AReturnValueThatBreaksTheSignatureIsAHostError(string command, string message)
        {
            var r = Submit(command, Arg("at", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunHostReturn));
            Assert.That(r.Error.Message, Does.Contain(message));
        }

        [Test]
        public void ReturningADeadEntityIsAHostError()
        {
            var r = Submit("Ghost", Arg("at", _a));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunHostReturn));
            Assert.That(r.Error.Message, Does.Contain(
                "host function core.debug.ghost returned a value that does not fit its signature Ref[\"Tile\"]: 'return' is " +
                _dead + ", which no longer exists"));
        }

        [Test]
        public void AWrongElementInAReturnedListIsNamed()
        {
            var r = Submit("Ghosts", Arg("at", _a));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunHostReturn));
            Assert.That(r.Error.Message, Does.Contain(
                "host function core.debug.ghosts returned a value that does not fit its signature list[Ref[\"Tile\"]]: " +
                "'return[1]' is " + _dead + ", which no longer exists"));
        }
    }
}
