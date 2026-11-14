using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 월드가 Def의 컴포넌트를 컴포넌트 번호로 찾는 표. 다른 로드에서 온 핸들은 번호가 겹쳐도 다른 컴포넌트일 수 있어 읽기·쓰기 모두 거절한다 (G479·G493).
    /// </summary>
    public class DefLayoutTests : LoaderTestBase
    {
        private const string Defs = "Unit(\"Soldier\", Food=Food(amount=5), Health=Health(hp=7))\n";

        [Test]
        public void AHandleFromAnotherLoadWithTheSameNumberIsNotReadAsTheOtherComponent()
        {
            // 첫 로드는 Food가 0번, 둘째 로드는 Health가 0번이다
            Mod("banking", schema:
                "component(\"Food\", amount=field(int, default=0))\n" +
                "component(\"Health\", hp=field(int, default=1))\n" +
                "deftype(\"Unit\", components=[\"Food\", \"Health\"])\n", defs: Defs);
            var first = new World(LoadClean());
            Mod("banking", schema:
                "component(\"Health\", hp=field(int, default=1))\n" +
                "component(\"Food\", amount=field(int, default=0))\n" +
                "deftype(\"Unit\", components=[\"Food\", \"Health\"])\n", defs: Defs);
            var second = new World(LoadClean());

            var unit = second.Create("banking:Soldier");
            Assert.That(Assert.Throws<System.ArgumentException>(() => second.Get(unit, first.Member("Food", "amount")))!.Message,
                Does.Contain("another load"));
            Assert.That(((IntValue)second.Get(unit, second.Member("Food", "amount"))).Value, Is.EqualTo(5));
            Assert.That(((IntValue)second.Get(unit, second.Member("Health", "hp"))).Value, Is.EqualTo(7));
        }

        [Test]
        public void ReadingAndContributingThroughAHandleFromAnotherLoadIsRefused()
        {
            const string schema =
                "component(\"Food\", amount=field(int, default=0), total=derived(int, lambda f: f.amount))\n" +
                "component(\"Rank\", key=int, level=field(int, default=0))\n" +
                "deftype(\"Unit\", components=[\"Food\", \"Rank\"], cost=const(int, default=3))\n";
            Mod("banking", schema: schema, defs: "Unit(\"Soldier\")\n");
            var first = new World(LoadClean());
            var second = new World(LoadClean());
            var unit = second.Create("banking:Soldier");
            second.AddRow(unit, "Rank", 1L);
            var before = second.Save();

            void Refused(TestDelegate use) =>
                Assert.That(Assert.Throws<System.ArgumentException>(use)!.Message, Does.Contain("another load"));

            Refused(() => second.Get(unit, first.Member("Food", "amount")));
            Refused(() => second.Get(unit, first.Member("Unit", "cost")));
            Refused(() => second.GetRow(unit, first.Member("Rank", "level"), 1L));
            Refused(() => second.Explain(unit, first.Member("Food", "total")));
            Refused(() => second.AddContribution(unit, first.Member("Food", "total"), "add", new IntValue(1)));
            Assert.That(second.Save(), Is.EqualTo(before));
            Assert.That(((IntValue)second.Get(unit, second.Member("Unit", "cost"))).Value, Is.EqualTo(3));
        }

        [Test]
        public void WritingThroughAHandleFromAnotherLoadIsRefused()
        {
            const string schema =
                "component(\"Food\", amount=field(int, default=0))\n" +
                "component(\"Rank\", key=int, level=field(int, default=0))\n" +
                "deftype(\"Unit\", components=[\"Food\", \"Rank\"])\n";
            Mod("banking", schema: schema, defs: "Unit(\"Soldier\")\n");
            var first = new World(LoadClean());
            var second = new World(LoadClean());
            var unit = second.Create("banking:Soldier");
            second.AddRow(unit, "Rank", 1L);
            var before = second.Save();
            var food = first.Member("Food", "amount");
            var level = first.Member("Rank", "level");

            void Refused(TestDelegate write) =>
                Assert.That(Assert.Throws<System.ArgumentException>(write)!.Message, Does.Contain("another load"));

            Refused(() => second.Set(unit, food, new IntValue(9)));
            Refused(() => second.SetRow(unit, level, 1L, new IntValue(9)));
            Refused(() => second.Create("banking:Soldier", new Init(food, new IntValue(9))));
            Refused(() => second.AddRow(unit, "Rank", 2L, new Init(level, new IntValue(9))));
            Refused(() => second.WriteColumn(food, new[] { unit.Value }, new long[] { 9 }));
            Assert.That(second.Save(), Is.EqualTo(before));

            second.Set(unit, second.Member("Food", "amount"), new IntValue(9));
            Assert.That(((IntValue)second.Get(unit, second.Member("Food", "amount"))).Value, Is.EqualTo(9));
        }
    }
}
