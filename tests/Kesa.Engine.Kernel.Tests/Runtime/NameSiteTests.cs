using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 이름 자리의 캐시(<c>NameExpr.Cache</c>)와 지역 변수 표: 같은 함수를 두 월드가 번갈아 써도 각 월드의 내장을 보고,
    /// 지역 변수와 이름이 같은 내장은 여전히 가려진다.
    /// </summary>
    public class NameSiteTests : LoaderTestBase
    {
        private const string Schema =
            "def stamp(c):\n    return now() * 10 + c.base\n" +
            "def shadowed(c):\n" +
            "    if c.base > 100:\n" +
            "        floor = 5\n" +
            "    return floor(c.base / 2)\n" +
            "component(\"Clock\", base=field(int, default=1), seen=derived(int, stamp), half=derived(int, shadowed))\n" +
            "deftype(\"Watch\", components=[\"Clock\"])\n";

        private Kesa.Engine.Loading.ContentLoadResult Load()
        {
            Mod("core", schema: Schema, defs: "Watch(\"Pocket\")\n");
            return LoadClean();
        }

        private static long Read(World world, EntityId id, string member) =>
            ((IntValue)world.Get(id, world.Member("Clock", member))).Value;

        [Test]
        public void TwoWorldsSharingAFunctionEachSeeTheirOwnBuiltins()
        {
            var content = Load();
            var early = new World(content);
            var late = new World(content);
            var a = early.Create("Pocket");
            var b = late.Create("Pocket");
            late.Advance(3);

            Assert.That(Read(early, a, "seen"), Is.EqualTo(1));
            Assert.That(Read(late, b, "seen"), Is.EqualTo(31));
            Assert.That(Read(early, a, "seen"), Is.EqualTo(1));
            early.Advance(1);
            Assert.That(Read(early, a, "seen"), Is.EqualTo(11));
        }

        [Test]
        public void ALocalWithABuiltinNameStillHidesItAfterTheSiteWasFilled()
        {
            var world = new World(Load());
            var id = world.Create("Pocket");
            world.Set(id, world.Member("Clock", "base"), new IntValue(7));
            // floor는 이 함수의 지역 변수라(어디선가 대입한다) 내장으로 넘어가지 않는다 (G104)
            var e = Assert.Throws<WorldException>(() => Read(world, id, "half"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Message, Does.Contain("local variable 'floor' is used before it is assigned"));
            Assert.Throws<WorldException>(() => Read(world, id, "half"));
        }
    }
}
