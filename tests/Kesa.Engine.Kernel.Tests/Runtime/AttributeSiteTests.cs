using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 속성 자리의 캐시(<c>AttributeExpr.Cache</c>): 같은 자리 <c>c.amount</c>가 다른 컴포넌트, 엔티티, 쓸 수 없는 멤버를 번갈아 받아도
    /// 결과와 오류가 캐시 없을 때와 같다.
    /// </summary>
    public class AttributeSiteTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Food\", amount=field(int, default=0))\n" +
            "component(\"Water\", amount=field(int, default=100))\n" +
            "component(\"Gauge\", amount=derived(int, lambda g: 7))\n" +
            "component(\"Rock\", weight=field(int, default=1))\n" +
            "component(\"Total\", sum=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Food\", \"Water\", \"Gauge\", \"Rock\", \"Total\"])\n";

        private const string Defs =
            "Unit(\"Scout\")\n" +
            "def bump(c):\n    c.amount += 1\n" +
            "def read(c):\n    return c.amount\n" +
            "def food_of(x):\n    return x.Food\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Mix(u: Ref[Unit]):\n" +
            "    bump(u.Food)\n" +
            "    bump(u.Water)\n" +
            "    bump(u.Food)\n" +
            "    u.Total.sum = read(u.Food) * 1000 + read(u.Water) + read(u.Gauge) * 10000\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Siblings(u: Ref[Unit]):\n" +
            // 같은 자리 x.Food가 엔티티에서 한 번, 형제 컴포넌트를 찾는 컴포넌트에서 한 번
            "    a = food_of(u)\n" +
            "    b = food_of(u.Water)\n" +
            "    a.amount = 5\n" +
            "    u.Total.sum = b.amount\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ReadRock(u: Ref[Unit]):\n" +
            "    read(u.Food)\n" +
            "    read(u.Rock)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def BumpGauge(u: Ref[Unit]):\n" +
            "    bump(u.Food)\n" +
            "    bump(u.Gauge)\n";

        private World _world = null!;
        private EntityId _unit;

        [SetUp]
        public void Make()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _unit = _world.Create("Scout");
        }

        private SubmitResult Submit(string command) =>
            _world.Submit("host", "core." + command, new Dictionary<string, Value> { ["u"] = new EntityValue(_unit) });

        private long Get(string component, string member) =>
            ((IntValue)_world.Get(_unit, _world.Member(component, member))).Value;

        [Test]
        public void OneSiteReadsAndWritesEachComponentItIsGiven()
        {
            var r = Submit("Mix");
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(Get("Food", "amount"), Is.EqualTo(2));
            Assert.That(Get("Water", "amount"), Is.EqualTo(101));
            Assert.That(Get("Total", "sum"), Is.EqualTo(7 * 10000 + 2 * 1000 + 101));
            // 두 번째 제출은 캐시가 찬 자리를 지난다
            Assert.That(Submit("Mix").Succeeded, Is.True);
            Assert.That(Get("Food", "amount"), Is.EqualTo(4));
            Assert.That(Get("Water", "amount"), Is.EqualTo(102));
        }

        [Test]
        public void OneSiteOnAnEntityAndOnASiblingFindsTheSameComponent()
        {
            var r = Submit("Siblings");
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(Get("Total", "sum"), Is.EqualTo(5));
        }

        [Test]
        public void AMissingMemberIsStillAnErrorAfterTheSiteWasFilled()
        {
            var r = Submit("ReadRock");
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(r.Error.Message, Does.Contain("'Rock' has no member, sibling component or property 'amount'"));
        }

        [Test]
        public void ADerivedMemberIsStillNotWritableAfterTheSiteWasFilled()
        {
            var r = Submit("BumpGauge");
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
            // 실패한 커맨드는 앞의 쓰기도 되돌린다
            Assert.That(Get("Food", "amount"), Is.EqualTo(0));
        }
    }
}
