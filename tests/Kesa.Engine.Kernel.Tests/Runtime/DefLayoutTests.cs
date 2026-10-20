using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 월드가 Def의 컴포넌트를 컴포넌트 번호로 찾는 표: 다른 로드에서 온 핸들은 번호가 겹쳐도 다른 컴포넌트일 수 있으므로 이름으로 찾는다.
    /// </summary>
    public class DefLayoutTests : LoaderTestBase
    {
        private const string Defs = "Unit(\"Soldier\", Food=Food(amount=5), Health=Health(hp=7))\n";

        [Test]
        public void AHandleFromAnotherLoadWithTheSameNumberFindsItsOwnComponent()
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
            var food = first.Member("Food", "amount"); // 첫 로드의 핸들
            Assert.That(((IntValue)second.Get(unit, food)).Value, Is.EqualTo(5));
            Assert.That(((IntValue)second.Get(unit, second.Member("Health", "hp"))).Value, Is.EqualTo(7));
        }
    }
}
