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
            "    n = core.debug.ghosts()\n" +
            // ---- 창구 (G217) ----
            "@when(lambda t: core.terrain.cost_of(t) >= 5)\n" +
            "def pricey(t: Ref[Tile]):\n" +
            "    pass\n" +
            "@command(free=True, acts_for=None)\n" +
            "def SetCost(at: Ref[Tile], n: int):\n" +
            "    at.Terrain.cost = n\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Raise(at: Ref[Tile]):\n" +
            "    core.terrain.raise_cost(at, 40)\n" +
            "    at.Terrain.cost = at.Terrain.cost + 2\n" +
            "@command(free=True, acts_for=None)\n" +
            "def RaiseThenFail(at: Ref[Tile]):\n" +
            "    core.terrain.raise_cost(at, 40)\n" +
            "    at.Terrain.cost = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Build(at: Ref[Tile]):\n" +
            "    core.terrain.build()\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Roll(at: Ref[Tile]):\n" +
            "    core.terrain.roll(at)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Sneak(at: Ref[Tile]):\n" +
            "    core.debug.sneak(at)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def SneakSubmit(at: Ref[Tile]):\n" +
            "    core.debug.sneak_submit()\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ReadDead(at: Ref[Tile]):\n" +
            "    n = core.debug.read_dead()\n";

        private World _world = null!;
        private EntityId _a, _b, _dead;
        private MemberHandle _cost = null!;
        private readonly List<string> _calls = new List<string>();
        private HostAccess? _access;
        private HostReader? _reader;
        private EntityId _built;

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

            core.Pure("def terrain.cost_of(t: Ref[Tile]) -> int", "", (HostReader r, EntityId t) =>
            {
                _reader = r;
                return ((IntValue)r.Get(t, r.Member("Terrain", "cost"))).Value;
            });
            core.Writes("def terrain.raise_cost(at: Ref[Tile], by: int) -> None", "", (HostAccess a, EntityId at, long by) =>
            {
                var cost = a.Member("Terrain", "cost");
                a.Set(at, cost, new IntValue(((IntValue)a.Get(at, cost)).Value + by));
            });
            core.Writes("def terrain.build() -> None", "", (HostAccess a) => { _built = a.Create("core:Grass"); });
            core.Writes("def terrain.roll(at: Ref[Tile]) -> None", "", (HostAccess a, EntityId at) =>
                a.Set(at, a.Member("Terrain", "cost"), new IntValue(a.RandInt(100, 105))));
            core.Writes("def debug.sneak(at: Ref[Tile]) -> None", "", (HostAccess a, EntityId at) =>
                _world.Set(at, _world.Member("Terrain", "cost"), new IntValue(99)));
            core.Writes("def debug.sneak_submit() -> None", "", (HostAccess a) =>
            {
                _world.Submit("host", "core.SetCost", new Dictionary<string, Value>());
            });
            core.Pure("def debug.read_dead() -> int", "", (HostReader r) =>
                ((IntValue)r.Get(_dead, r.Member("Terrain", "cost"))).Value);

            Mod("core", schema: Schema, defs: Defs);
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var loaded = ContentLoader.Load(_root, order, bag, host);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _world = new World(loaded);
            _a = _world.Create("core:Grass");
            _b = _world.Create("core:Grass");
            _dead = _world.Create("core:Grass");
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

        // ---- 창구 (G216·G217) ----

        [Test]
        public void WhatAPureHostFunctionReadsIsTrackedLikeAScriptRead()
        {
            // when 조건은 읽은 칸이 바뀔 때만 다시 본다: 창구의 읽기가 기록되지 않으면 반응이 영영 없다
            var low = Submit("SetCost", Arg("at", _a), ("n", new IntValue(3)));
            Assert.That(low.Reactions.Where(r => r.Handler.Contains("pricey")), Is.Empty);
            var high = Submit("SetCost", Arg("at", _a), ("n", new IntValue(9)));
            Assert.That(high.Reactions.Count(r => r.Handler.Contains("pricey")), Is.EqualTo(1),
                string.Join("\n", high.Reactions.Select(r => r.Handler)));
        }

        [Test]
        public void AWritingHostFunctionWritesThroughItsAccessor()
        {
            var r = Submit("Raise", Arg("at", _a));
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            // 1 + 40, 그 뒤 스크립트가 쓴 것을 본다: + 2
            Assert.That(Cost(_a), Is.EqualTo(43));
        }

        [Test]
        public void AccessorWritesRollBackWithTheCommand()
        {
            var r = Submit("RaiseThenFail", Arg("at", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Cost(_a), Is.EqualTo(1));
        }

        [Test]
        public void TheAccessorCreatesEntities()
        {
            int before = _world.All("Tile").Count;
            Assert.That(Submit("Build", Arg("at", _a)).Succeeded, Is.True);
            Assert.That(_world.IsAlive(_built), Is.True);
            Assert.That(_world.All("Tile").Count, Is.EqualTo(before + 1));
        }

        [Test]
        public void TheAccessorDrawsFromTheWorldRandomStream()
        {
            Assert.That(Submit("Roll", Arg("at", _a)).Succeeded, Is.True);
            Assert.That(Cost(_a), Is.InRange(100, 105));
        }

        [Test]
        public void TheAccessorCannotBeKeptAfterTheCall()
        {
            // when 조건이 cost_of를 부르며 창구를 건넨다. 테스트의 구현은 그것을 붙잡아 둔다
            Submit("SetCost", Arg("at", _a), ("n", new IntValue(4)));
            Assert.That(_reader, Is.Not.Null);
            var e = Assert.Throws<InvalidOperationException>(() => _reader!.IsAlive(_a))!;
            Assert.That(e.Message, Is.EqualTo("the accessor of host function core.terrain.cost_of was used after the call ended; " +
                                              "do not keep it (spec-gaps G217)"));
        }

        [Test]
        public void AHostFunctionCannotWriteTheWorldDirectly()
        {
            var r = Submit("Sneak", Arg("at", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunHostFailed));
            Assert.That(r.Error.Message, Does.Contain("host function core.debug.sneak failed: InvalidOperationException: " +
                                                      "World.Set cannot be called from inside a host function; use the HostAccess it was given"));
            Assert.That(Cost(_a), Is.EqualTo(1));
        }

        [Test]
        public void AHostFunctionCannotSubmit()
        {
            var r = Submit("SneakSubmit", Arg("at", _a));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunHostFailed));
            Assert.That(r.Error.Message, Does.Contain("World.Submit cannot be called from inside a host function"));
        }

        [Test]
        public void TheWorldIsWritableAgainAfterAHostFunctionFails()
        {
            Submit("Sneak", Arg("at", _a));
            _world.Set(_a, _cost, new IntValue(6));
            Assert.That(Cost(_a), Is.EqualTo(6));
        }

        [Test]
        public void AWorldErrorFromTheAccessorKeepsItsCode()
        {
            var r = Submit("ReadDead", Arg("at", _a));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunDeadEntity));
            Assert.That(r.Error.Message, Does.Contain("in host function core.debug.read_dead: "));
        }

        [Test]
        public void APureFunctionCannotTakeAWritingAccessor()
        {
            var e = Assert.Throws<ArgumentException>(() => new HostFunctions().For("core")
                .Pure("def f() -> int", "", (HostAccess a) => 1L))!;
            Assert.That(e.Message, Does.StartWith("host function core.f: a pure function reads the world through a HostReader, " +
                                                  "not a HostAccess"));
        }
    }
}
