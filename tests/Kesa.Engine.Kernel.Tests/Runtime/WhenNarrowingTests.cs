using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// <c>@when</c> 조건은 읽은 (엔티티, 컴포넌트)가 바뀔 때만 다시 평가한다 (spec-gaps G290). 빠뜨림이 없는지는
    /// <see cref="WhenTrackingTests"/>가 전체 훑기와 견주어 보고, 여기서는 필요 없는 평가를 안 하는지 센다.
    /// </summary>
    public class WhenNarrowingTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Life\", hp=field(int, default=0))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True, inverse=\"units\"))\n" +
            "component(\"Mark\", x=field(int, default=0))\n" +
            "component(\"Side\", name=field(str, default=\"\"))\n" +
            "deftype(\"Party\", components=[\"Side\"])\n" +
            "deftype(\"Unit\", components=[\"Life\", \"Owner\"])\n";

        private const string Defs =
            "Party(\"Red\")\nUnit(\"Soldier\")\n" +
            "@when(lambda u: u.Life.hp >= 3)\ndef healthy(u: Ref[Unit]):\n    pass\n" +
            "@when(lambda u: u.Mark is not None)\ndef marked(u: Ref[Unit]):\n    pass\n" +
            "@when(lambda u: controller(u) is not None)\ndef seated(u: Ref[Unit]):\n    pass\n" +
            "@when(lambda p: len(p.units) >= 1)\ndef manned(p: Ref[Party]):\n    pass\n";

        private World _world = null!;
        private EntityId _unit, _red;

        [SetUp]
        public void Make()
        {
            Mod("ranks", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("ranks:Red");
            _unit = _world.Create("ranks:Soldier");
            _world.Advance(1); // 처음 평가를 끝낸다
        }

        private long EvaluationsAfter(System.Action write)
        {
            long before = _world.WhenEvaluations;
            write();
            _world.Advance(1);
            return _world.WhenEvaluations - before;
        }

        [Test]
        public void AWriteToAnotherComponentDoesNotReevaluate()
        {
            Assert.That(EvaluationsAfter(() => _world.Set(_unit, _world.Member("Life", "hp"), new IntValue(1))), Is.EqualTo(1),
                "only 'healthy' reads Life");
        }

        [Test]
        public void ReassigningARefOnlyWakesTheReadersOfItsInverse()
        {
            // Owner는 healthy·marked·seated 어느 조건도 읽지 않는다. 역방향 units는 manned만 읽는다
            Assert.That(EvaluationsAfter(() => _world.Set(_unit, _world.Member("Owner", "party"), new EntityValue(_red))), Is.EqualTo(1));
        }

        [Test]
        public void SeatingWakesOnlyTheSeatReaders()
        {
            Assert.That(EvaluationsAfter(() => _world.Seat(_unit, "p1")), Is.EqualTo(1));
        }

        [Test]
        public void AttachingAComponentWakesOnlyItsReaders()
        {
            Assert.That(EvaluationsAfter(() => _world.Add(_unit, "Mark")), Is.EqualTo(1));
        }
    }
}
