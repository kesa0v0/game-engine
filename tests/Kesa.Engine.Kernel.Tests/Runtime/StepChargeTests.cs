using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 크기를 따르는 일은 그 양만큼 단계를 센다 (spec-gaps G467): 판 복사, 엔티티 묶음, 목록을 만들고 훑는 연산. 식 노드 수가 적어도
    /// 큰 판에서는 예산이 걸린다.
    /// </summary>
    public class StepChargeTests : LoaderTestBase
    {
        private const string Defs =
            "Party(\"Red\")\nUnit(\"Soldier\")\n" +
            "@bot\ndef copy(me: Ref[Party]):\n    with imagine():\n        pass\n" +
            "@bot\ndef group(me: Ref[Party]):\n    g = query(Unit)\n" +
            // 붙이기 2+4+…+1024와 훑기 1024
            "@bot\ndef scan(me: Ref[Party]):\n    xs = [1]\n    for i in range(10):\n        xs = xs + xs\n    b = 2 in xs\n";

        private World _world = null!;
        private EntityId _red;

        [SetUp]
        public void Board()
        {
            Mod("core", schema: "component(\"Hp\", v=field(int, default=3))\ndeftype(\"Party\", components=[\"Hp\"])\n" +
                                "deftype(\"Unit\", components=[\"Hp\"])\n", defs: Defs);
            _world = new World(LoadClean());
            for (int i = 0; i < 100; i++) _world.Create("core:Soldier");
            _red = _world.Create("core:Red");
            _world.Seat(_red, "bot", SeatKind.Ai);
        }

        private ThinkResult Think(string bot, int budget) => _world.ViewAs("bot").Think(_world.NewBot("core." + bot, _red, stepBudget: budget));

        [Test]
        public void CopyingTheBoardCountsItsSize()
        {
            // 엔티티 101개와 그 값: 노드는 몇 개뿐이지만 복사는 수만 걸음이다
            Assert.That(Think("copy", 20_000).Error?.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(Think("copy", 200_000).Outcome, Is.EqualTo(ThinkOutcome.Thought));
        }

        [Test]
        public void AGroupCountsItsMembers()
        {
            Assert.That(Think("group", 50).Error?.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(Think("group", 200).Outcome, Is.EqualTo(ThinkOutcome.Thought));
        }

        [Test]
        public void MakingAndScanningAListCountsItsLength()
        {
            Assert.That(Think("scan", 2_500).Error?.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(Think("scan", 4_000).Outcome, Is.EqualTo(ThinkOutcome.Thought));
        }
    }
}
