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
            "deftype(\"Unit\", components=[\"Location\", \"Moves\"])\n" +
            "deftype(\"Resource\")\n";

        private const string Defs =
            "City(\"Town\")\nUnit(\"Scout\")\n" +
            "@command(free=True, acts_for=lambda unit: unit)\n" +
            "def Fortify(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        c = unit.Location.city\n" +
            "        require(c is not None, text(\"msg.not_in_city\", unit=unit))\n" +
            "        w = c.Walls.walls\n" +
            "        require(w < 5, text(\"msg.walls_full\", walls=w))\n" +
            "        require(unit.Moves.left > 0, text(\"msg.no_moves\", left=unit.Moves.left))\n" +
            "    return c\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Rest(unit: Ref[Unit]):\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Buggy(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            // 리스트로 감싸 타입을 모르게 한다: 실행 중 안전망을 본다 (아니면 G228의 로드 오류)
            "        require([unit.Moves][0].nope > 0, text(\"msg.first\"))\n" +
            "        require(False, text(\"msg.second\"))\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Lazy(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        c = unit.Location.city\n" +
            "        require(c is None or c.Walls.walls < 5, text(\"msg.walls_full\", walls=c.Walls.walls))\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def NotText(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        m = \"msg.plain\"\n" +
            "        require(False, m)\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Spin(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        require(False, text(\"msg.first\"))\n" +
            "        n = spin()\n" +
            "    return 0\n" +
            "def spin():\n" +
            "    t = 0\n" +
            "    for i in range(100000):\n" +
            "        t += i\n" +
            "    return t\n" +
            "Resource(\"Iron\")\nResource(\"Wood\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Trade(unit: Ref[Unit], goods: dict[DefId[Resource], int], rate: fixed, path: list[Ref[City]],\n" +
            "          escort: Ref[Unit] | None):\n" +
            "    with preconditions:\n" +
            "        require(rate > 0, text(\"msg.rate\"))\n" +
            "    return 0\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Wait(unit: Ref[Unit], turns: int = 2, pace: fixed = 1):\n" +
            "    with preconditions:\n" +
            "        require(turns <= 2, text(\"msg.too_long\", turns=turns))\n" +
            "        require(pace == 1, text(\"msg.pace\"))\n" +
            "    return 0\n";

        private World _world = null!;
        private EntityId _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean(), actionStepBudget: World.DefaultStepBudget); // spin()이 넘도록 작게
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
            // 통과한 require는 메시지를 평가하지 않는다: 키는 리터럴에서, 인자는 없다 (G113)
            Assert.That(report.Requires[1].MessageKey, Is.EqualTo("msg.walls_full"));
            Assert.That(report.Requires[1].Args, Is.Empty);
            Assert.That(report.Requires[1].Span.Line, Is.EqualTo(9));
        }

        [Test]
        public void TheMessageIsEvaluatedOnlyWhenTheConditionFails()
        {
            // 도시가 없으면 조건이 참이라 메시지(c.Walls.walls)를 평가하지 않는다 (파이썬 assert와 같음, G113)
            Assert.That(Check("banking.Lazy").Passed, Is.True);
            PutInTown(walls: 7);
            var failure = Check("banking.Lazy").Failures.Single();
            Assert.That(((IntValue)failure.Args["walls"]).Value, Is.EqualTo(7));
        }

        [Test]
        public void TheMessageMustBeATextValue()
        {
            var e = Assert.Throws<WorldException>(() => Check("banking.NotText"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunType));
            Assert.That(e.Message, Does.Contain("the message of require() must be a text(...) value, not str"));
        }

        [Test]
        public void BudgetErrorsAreScriptErrorsEvenAfterAFailure()
        {
            // 앞에 실패가 있어도 단계 예산 초과는 앞 조건에 기대서 생긴 오류가 아니다 (G110 보강)
            var e = Assert.Throws<WorldException>(() => Check("banking.Spin"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunBudget));
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
            Assert.That(report.Requires[1].MessageKey, Is.EqualTo("msg.walls_full")); // 평가 안 됨도 리터럴 키는 있다
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
        public void AnUnknownCommandIsACallerError()
        {
            var unknown = Assert.Throws<WorldException>(() => Check("banking.Fortfy"))!;
            Assert.That(unknown.Code, Is.EqualTo(Codes.RunUnknownName));
            Assert.That(unknown.Hint, Is.EqualTo("did you mean 'banking.Fortify'?"));
        }

        // ---- 인자 검사 (G115) ----

        private Dictionary<string, Value> TradeArgs(EntityId town) => new Dictionary<string, Value>
        {
            ["unit"] = new EntityValue(_scout),
            ["goods"] = new DictValue(new[]
            {
                new DictItem(new StrValue("Iron"), new IntValue(3), SourceSpan.None),
                new DictItem(new StrValue("Wood"), new IntValue(1), SourceSpan.None),
            }),
            ["rate"] = new IntValue(2),
            ["path"] = new ListValue(new Value[] { new EntityValue(town) }),
            ["escort"] = NoneValue.Instance,
        };

        private PreconditionReport Trade(Dictionary<string, Value> args) => _world.CheckPreconditions("banking.Trade", args);

        private static ArgumentRejection[] Rejections(PreconditionReport r) => r.Arguments.ToArray();

        [Test]
        public void GoodArgumentsPassAndIntIsRaisedToFixed()
        {
            // rate=2(int)가 fixed로 올라가 rate > 0이 평가된다 (G95)
            var report = Trade(TradeArgs(_world.Create("Town")));
            Assert.That(report.Arguments, Is.Empty);
            Assert.That(report.Passed, Is.True);
            Assert.That(Statuses(report), Is.EqualTo(new[] { RequireStatus.Passed }));
        }

        [Test]
        public void MissingAndUnexpectedArgumentsAreRejectedTogether()
        {
            var args = TradeArgs(_world.Create("Town"));
            args.Remove("rate");
            args["rat"] = new IntValue(1);
            var report = Trade(args);
            Assert.That(report.Passed, Is.False);
            Assert.That(report.Requires, Is.Empty); // 인자가 틀리면 사전 조건은 평가하지 않는다
            var r = Rejections(report);
            Assert.That(r.Select(x => x.Problem), Is.EqualTo(new[] { ArgumentProblem.Missing, ArgumentProblem.Unexpected }));
            Assert.That(r[0].Parameter, Is.EqualTo("rate"));
            Assert.That(r[0].Expected, Is.EqualTo("fixed"));
            Assert.That(r[0].Message, Is.EqualTo("banking.Trade is missing the argument 'rate' (fixed)"));
            Assert.That(r[1].Message, Is.EqualTo("banking.Trade has no parameter 'rat'; did you mean 'rate'?"));
            Assert.That(r[1].Expected, Is.Null);
        }

        [Test]
        public void ADeadEntityIsRejectedNotAnError()
        {
            var args = TradeArgs(_world.Create("Town"));
            var ghost = _world.Create("Scout");
            _world.Destroy(ghost);
            args["escort"] = new EntityValue(ghost);
            var r = Rejections(Trade(args)).Single();
            Assert.That(r.Problem, Is.EqualTo(ArgumentProblem.DeadEntity));
            Assert.That(r.Path, Is.EqualTo("escort"));
            Assert.That(r.Expected, Is.EqualTo("Ref[\"Unit\"] | None"));
        }

        [Test]
        public void ProblemsInsideCollectionsCarryTheirPosition()
        {
            var town = _world.Create("Town");
            var args = TradeArgs(town);
            args["path"] = new ListValue(new Value[] { new EntityValue(town), new EntityValue(_scout) });
            args["goods"] = new DictValue(new[]
            {
                new DictItem(new StrValue("Irom"), new IntValue(3), SourceSpan.None),
                new DictItem(new StrValue("Town"), new IntValue(1), SourceSpan.None),
                new DictItem(new StrValue("Wood"), new FixedValue(Kesa.Engine.Numerics.Fixed.FromRaw(1)), SourceSpan.None),
            });
            var r = Rejections(Trade(args));
            Assert.That(r.Select(x => x.Problem), Is.EqualTo(new[]
            {
                ArgumentProblem.UnknownDef, ArgumentProblem.WrongDefType, ArgumentProblem.WrongType,
                ArgumentProblem.WrongEntityType,
            }));
            Assert.That(r.Select(x => x.Path), Is.EqualTo(new[]
            {
                "goods key \"Irom\"", "goods key \"Town\"", "goods[\"Wood\"]", "path[1]",
            }));
            Assert.That(r[0].Message, Is.EqualTo("'goods key \"Irom\"' is 'Irom', which is not a Def; did you mean 'Iron'?"));
            Assert.That(r[2].Message, Is.EqualTo("'goods[\"Wood\"]' must be int, not fixed (a fixed value is not turned into an int)"));
            Assert.That(r[3].Message, Does.EndWith("a Unit, not a City"));
            Assert.That(r.All(x => x.Parameter == "goods" || x.Parameter == "path"));
        }

        [Test]
        public void MissingArgumentsWithDefaultsAreFilledIn()
        {
            // turns=2(기본값), pace=1(fixed로 올린 기본값)으로 채워져 둘 다 통과 (G116)
            Assert.That(Check("banking.Wait").Passed, Is.True);

            var report = _world.CheckPreconditions("banking.Wait",
                new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout), ["turns"] = new IntValue(5) });
            Assert.That(((IntValue)report.Failures.Single().Args["turns"]).Value, Is.EqualTo(5));
        }

        [TestCase("rate", "bool")]
        [TestCase("unit", "int")]
        [TestCase("path", "str")]
        public void WrongKindsOfValuesAreRejected(string parameter, string kind)
        {
            var args = TradeArgs(_world.Create("Town"));
            args[parameter] = kind == "bool" ? BoolValue.True : kind == "int" ? new IntValue(1) : (Value)new StrValue("x");
            var r = Rejections(Trade(args)).Single();
            Assert.That(r.Problem, Is.EqualTo(ArgumentProblem.WrongType));
            Assert.That(r.Message, Does.EndWith(", not " + kind));
        }
    }
}
