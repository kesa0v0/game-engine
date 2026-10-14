using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>커맨드 사전 검사: <c>with preconditions:</c>와 <c>require</c> (spec S6·C7, spec-gaps G110·G111).</summary>
    public class WorldPreconditionTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Walls\", walls=field(int, default=0))\n" +
            "component(\"Location\", city=ref(\"City\", optional=True))\n" +
            "component(\"Moves\", left=field(int, default=2))\n" +
            "deftype(\"City\", components=[\"Walls\"])\n" +
            "deftype(\"Unit\", components=[\"Location\", \"Moves\"])\n";

        private const string Defs =
            "City(\"Town\")\nUnit(\"Scout\")\n" +
            "@command(free=True)\n" +
            "def Fortify(unit):\n" +
            "    with preconditions:\n" +
            "        c = unit.Location.city\n" +
            "        require(c is not None, \"msg.not_in_city\", unit=unit)\n" +
            "        w = c.Walls.walls\n" +
            "        require(w < 5, \"msg.walls_full\", walls=w)\n" +
            "        require(unit.Moves.left > 0, \"msg.no_moves\", left=unit.Moves.left)\n" +
            "    return c\n" +
            "@command\n" +
            "def Rest(unit):\n" +
            "    return 0\n" +
            "@command\n" +
            "def Buggy(unit):\n" +
            "    with preconditions:\n" +
            "        require(unit.Moves.nope > 0, \"msg.first\")\n" +
            "        require(False, \"msg.second\")\n" +
            "    return 0\n";

        private World _world = null!;
        private EntityId _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _scout = _world.Create("Scout");
        }

        private EntityId PutInTown(int walls)
        {
            var town = _world.Create("Town");
            _world.Set(town, _world.Member("Walls", "walls"), new IntValue(walls));
            _world.Set(_scout, _world.Member("Location", "city"), new EntityValue(town));
            return town;
        }

        private PreconditionReport Check(string command) =>
            _world.CheckPreconditions(command, new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) });

        private static RequireStatus[] Statuses(PreconditionReport r) => r.Requires.Select(x => x.Status).ToArray();

        [Test]
        public void AllRequiresPass()
        {
            PutInTown(walls: 1);
            var report = Check("banking.Fortify");
            Assert.That(report.Passed, Is.True);
            Assert.That(report.Command, Is.EqualTo("banking.Fortify"));
            Assert.That(Statuses(report), Is.EqualTo(new[] { RequireStatus.Passed, RequireStatus.Passed, RequireStatus.Passed }));
            Assert.That(report.Requires[1].MessageKey, Is.EqualTo("msg.walls_full"));
            Assert.That(((IntValue)report.Requires[1].Args["walls"]).Value, Is.EqualTo(1));
            Assert.That(report.Requires[1].Span.Line, Is.EqualTo(9));
        }

        [Test]
        public void AFailureCarriesItsKeyAndArguments()
        {
            PutInTown(walls: 5);
            var report = Check("banking.Fortify");
            Assert.That(report.Passed, Is.False);
            var failure = report.Failures.Single();
            Assert.That(failure.MessageKey, Is.EqualTo("msg.walls_full"));
            Assert.That(((IntValue)failure.Args["walls"]).Value, Is.EqualTo(5));
        }

        [Test]
        public void ARequireThatDependsOnAnEarlierFailureIsNotEvaluated()
        {
            // c가 None이라 w = c.Walls.walls가 오류 → w를 읽는 require는 평가 안 됨. 기대지 않는 require는 그대로 평가된다
            var report = Check("banking.Fortify");
            Assert.That(Statuses(report),
                Is.EqualTo(new[] { RequireStatus.Failed, RequireStatus.NotEvaluated, RequireStatus.Passed }));
            Assert.That(report.Requires[1].MessageKey, Is.EqualTo("msg.walls_full"));
            Assert.That(report.Requires[1].Args, Is.Empty);
            Assert.That(report.Failures.Select(f => f.MessageKey), Is.EqualTo(new[] { "msg.not_in_city" }));
            // 엔티티 인자는 저장 가능한 값(EntityValue)으로 나온다
            Assert.That(((EntityValue)report.Failures[0].Args["unit"]).Id, Is.EqualTo(_scout));
        }

        [Test]
        public void EveryIndependentFailureIsReported()
        {
            _world.Set(_scout, _world.Member("Moves", "left"), new IntValue(0));
            var report = Check("banking.Fortify");
            Assert.That(report.Failures.Select(f => f.MessageKey), Is.EqualTo(new[] { "msg.not_in_city", "msg.no_moves" }));
            Assert.That(((IntValue)report.Failures[1].Args["left"]).Value, Is.EqualTo(0));
        }

        [Test]
        public void ACommandWithoutPreconditionsAlwaysPasses()
        {
            var report = Check("banking.Rest");
            Assert.That(report.Passed, Is.True);
            Assert.That(report.Requires, Is.Empty);
        }

        [Test]
        public void AnErrorWithNoEarlierFailureIsAScriptError()
        {
            var e = Assert.Throws<WorldException>(() => Check("banking.Buggy"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Message, Does.StartWith("computing the preconditions of banking.Buggy: 'Moves' has no member"));
        }

        [Test]
        public void UnknownCommandsAndMissingArgumentsAreRejected()
        {
            var unknown = Assert.Throws<WorldException>(() => Check("banking.Fortfy"))!;
            Assert.That(unknown.Code, Is.EqualTo(Codes.RunUnknownName));
            Assert.That(unknown.Hint, Is.EqualTo("did you mean 'banking.Fortify'?"));

            var missing = Assert.Throws<WorldException>(() =>
                _world.CheckPreconditions("banking.Fortify", new Dictionary<string, Value>()))!;
            Assert.That(missing.Message, Does.Contain("missing the argument 'unit'"));
        }
    }
}
