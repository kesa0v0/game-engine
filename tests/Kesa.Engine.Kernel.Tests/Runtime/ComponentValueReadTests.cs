using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>붙기 전의 컴포넌트 값 읽기 (spec-gaps G288): 준 칸만 읽히고, 안 준 칸은 붙을 때 정해지므로 오류다.</summary>
    public class ComponentValueReadTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Moves\", left=field(int, default=1), max=field(int, default=5))\n" +
            "component(\"Home\", city=ref(\"City\", optional=True))\n" +
            "deftype(\"City\", components=[\"Moves\"])\n" +
            "deftype(\"Unit\", components=[\"Moves\", \"Home\"])\n";

        private const string Defs =
            "City(\"Town\")\n" +
            "Unit(\"Scout\", Moves=Moves(max=8))\n" +
            "def given():\n    return Moves(left=3).left\n" +
            "def not_given():\n    return Moves(left=3).max\n" +
            "def typo():\n    return Moves(left=3).leftt\n" +
            "def home_of(c):\n    return Home(city=c).city\n" +
            // 도우미가 값을 받아 그 값으로 새 값을 만든다
            "def one_more(m):\n    return Moves(left=m.left + 1)\n" +
            "@command(free=True, acts_for=None)\ndef Make():\n    spawn(\"Scout\", Moves=one_more(Moves(left=3)))\n" +
            "@command(free=True, acts_for=None)\ndef Change():\n    m = Moves(left=3)\n    m.left = 4\n";

        private World _world = null!;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
        }

        private long Int(EntityId id, string component, string member) =>
            ((IntValue)_world.Get(id, _world.Member(component, member))).Value;

        [Test]
        public void AGivenValueIsRead()
        {
            Assert.That(((IntValue)_world.Call("core.given").Value).Value, Is.EqualTo(3));
            var town = _world.Create("core:Town");
            Assert.That(((EntityValue)_world.Call("core.home_of", new EntityValue(town)).Value).Id, Is.EqualTo(town));
        }

        [Test]
        public void AValueNotGivenIsNotKnownYet()
        {
            // 붙으면 Def의 값(8)이 된다: 스키마 기본값(5)으로 답하면 틀린다
            var e = Assert.Throws<WorldException>(() => _world.Call("core.not_given"))!;
            Assert.That(e.Message, Does.Contain("Moves(...) was not given 'max'"));
            Assert.That(e.Hint, Does.Contain("G288"));

            Assert.That(_world.Submit("host", "core.Make", new Dictionary<string, Value>()).Succeeded, Is.True);
            var scout = _world.All("Unit").Single();
            Assert.That(Int(scout, "Moves", "left"), Is.EqualTo(4));
            Assert.That(Int(scout, "Moves", "max"), Is.EqualTo(8));
        }

        [Test]
        public void AnUnknownNameSuggestsTheMember()
        {
            var e = Assert.Throws<WorldException>(() => _world.Call("core.typo"))!;
            Assert.That(e.Message, Does.Contain("component 'Moves' has no value 'leftt'"));
            Assert.That(e.Hint, Does.Contain("left"));
        }

        [Test]
        public void AValueCannotBeChanged()
        {
            var result = _world.Submit("host", "core.Change", new Dictionary<string, Value>());
            Assert.That(result.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(result.Error!.Message, Does.Contain("a Moves(...) value cannot be changed"));
            Assert.That(result.Error.Hint, Does.Contain("Moves(left=...)"));
        }
    }
}
