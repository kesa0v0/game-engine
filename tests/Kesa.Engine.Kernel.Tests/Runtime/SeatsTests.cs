using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// <c>seats()</c> (G421): 보는 연결이 앉은 엔티티 목록이고 이름을 주면 그 타입만이다. 화면 쪽 코드에서만 쓰고 영수증은 배정 표의 바뀜을 본다.
    /// </summary>
    public class SeatsTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\n" +
            "deftype(\"Party\", components=[\"Hp\"])\ndeftype(\"Hero\", components=[\"Hp\"])\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\nHero(\"Ann\")\n" +
            "def mine():\n    return [e.Hp.v for e in seats()]\n" +
            "def parties():\n    return len(seats(Party))\n" +
            "def owns(p):\n    return p in seats()\n" +
            "def wrong():\n    return seats(1, 2)\n";

        private World _world = null!;
        private EntityId _red, _blue, _ann;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("core:Red");
            _blue = _world.Create("core:Blue");
            _ann = _world.Create("core:Ann");
            _world.Set(_ann, _world.Member("Hp", "v"), new IntValue(7));
            _world.Seat(_red, "alice");
            _world.Seat(_ann, "alice");
            _world.Seat(_blue, "bob");
        }

        private static long[] Ints(Value v) => ((ListValue)v).Items.Select(i => ((IntValue)i).Value).ToArray();

        [Test]
        public void EachConnectionGetsTheEntitiesItSitsAtAndANameFilters()
        {
            var alice = _world.ViewAs("alice");
            Assert.That(Ints(alice.Call("core.mine").Value), Is.EqualTo(new[] { 3L, 7L }));
            Assert.That(((IntValue)alice.Call("core.parties").Value).Value, Is.EqualTo(1));
            Assert.That(alice.Call("core.owns", new EntityValue(_red)).Value, Is.EqualTo(BoolValue.True));
            Assert.That(alice.Call("core.owns", new EntityValue(_blue)).Value, Is.EqualTo(BoolValue.False));
            Assert.That(Ints(_world.ViewAs("carol").Call("core.mine").Value), Is.Empty, "a spectator sits nowhere");
        }

        [Test]
        public void TheReceiptChangesWhenTheSeatsChange()
        {
            var alice = _world.ViewAs("alice");
            var call = alice.Call("core.parties");
            Assert.That(call.Changed(_world), Is.False);
            _world.Seat(_blue, "alice");
            Assert.That(call.Changed(_world), Is.True);
            Assert.That(((IntValue)alice.Call("core.parties").Value).Value, Is.EqualTo(2));
        }

        [Test]
        public void WrongArgumentsFailTheCall()
        {
            var e = Assert.Throws<WorldException>(() => _world.ViewAs("alice").Call("core.wrong"))!;
            Assert.That(e.Message, Does.Contain("seats() takes nothing or one type or component"));
        }

        [TestCase("@command(free=True, acts_for=None)\ndef Peek():\n    n = len(seats())\n", "the @command 'Peek'")]
        [TestCase("def helper():\n    return seats(Party)\n@command(free=True, acts_for=None)\ndef Peek():\n    n = len(helper())\n",
            "the @command 'Peek'")]
        public void GameCodeCannotReadSeats(string defs, string who)
        {
            Mod("core", schema: Schema, defs: "Party(\"Red\")\n" + defs);
            Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdUiEffect);
            Assert.That(d.Message, Is.EqualTo(who + " reads what one connection sees (questions() or seats()), so it cannot run as game code"));
        }
    }
}
