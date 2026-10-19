using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>호스트가 모드의 순수 함수를 이름으로 부른다 (spec-gaps G244·G245·G247).</summary>
    public class WorldCallTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Life\", hp=field(int, default=0), max_hp=field(int, default=10))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "component(\"Side\", name=field(str, default=\"\"))\n" +
            "deftype(\"Party\", components=[\"Side\"])\n" +
            "deftype(\"Unit\", components=[\"Life\", \"Owner\"])\n" +
            "signal(\"Poked\")\n";

        private const string Defs =
            "Party(\"Red\")\nUnit(\"Soldier\")\n" +
            "LIMIT = 3\n" +
            "def low(u):\n    return u.Life.hp * 2 < u.Life.max_hp\n" +
            "def unit_row(u):\n    return {\"hp\": u.Life.hp, \"low\": low(u), \"owner\": u.Owner.party}\n" +
            "def units():\n    return [u for u in query(\"Unit\")]\n" +
            "def broken(u):\n    return u.Life.hp // 0\n" +
            "def forever():\n    n = 0\n    while True:\n        n += 1\n    return n\n" +
            "def heal(u):\n    u.Life.hp = u.Life.max_hp\n" +
            "def roll():\n    return random.randint(1, 6)\n" +
            "@command(free=True, acts_for=None)\ndef Hurt(u: Ref[Unit]):\n    u.Life.hp -= 1\n" +
            "@on(Poked)\ndef ouch():\n    return None\n" +
            "@when(lambda: False)\ndef never():\n    pass\n";

        private World Make(out EntityId unit, int stepBudget = World.DefaultStepBudget)
        {
            Mod("ranks", schema: Schema, defs: Defs);
            var world = new World(LoadClean(), stepBudget);
            var red = world.Create("Red");
            unit = world.Create("Soldier", new Init(world.Member("Owner", "party"), new EntityValue(red)));
            world.Set(unit, world.Member("Life", "hp"), new IntValue(4));
            return world;
        }

        [Test]
        public void CallsAPureFunctionAndGivesPlainValues()
        {
            var world = Make(out var unit);
            var red = world.Get(unit, world.Member("Owner", "party"));
            var row = (DictValue)world.Call("ranks.unit_row", new EntityValue(unit)).Value;
            Assert.That(row.Items.Select(i => ((StrValue)i.Key).Value), Is.EqualTo(new[] { "hp", "low", "owner" }));
            Assert.That(((IntValue)row.Items[0].Value).Value, Is.EqualTo(4));
            Assert.That(((BoolValue)row.Items[1].Value).Value, Is.True);
            Assert.That(((EntityValue)row.Items[2].Value).Id, Is.EqualTo(((EntityValue)red).Id));
            var list = (ListValue)world.Call("ranks.units").Value;
            Assert.That(list.Items.Select(i => ((EntityValue)i).Id), Is.EqualTo(new[] { unit }));
        }

        [Test]
        public void CallingChangesNothing()
        {
            var world = Make(out var unit);
            long before = world.Version();
            var save = world.Save();
            world.Call("ranks.unit_row", new EntityValue(unit));
            Assert.That(world.Version(), Is.EqualTo(before));
            Assert.That(world.Save(), Is.EqualTo(save));
        }

        [Test]
        public void UnknownNamesSuggestCallableFunctions()
        {
            var world = Make(out var unit);
            var e = Assert.Throws<WorldException>(() => world.Call("ranks.unit_rows", new EntityValue(unit)))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunUnknownName));
            Assert.That(e.Hint, Does.Contain("unit_row"));
            e = Assert.Throws<WorldException>(() => world.Call("rank.units"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunUnknownName));
            Assert.That(e.Hint, Does.Contain("ranks"));
            e = Assert.Throws<WorldException>(() => world.Call("units"))!;
            Assert.That(e.Message, Does.Contain("mod.function"));
            e = Assert.Throws<WorldException>(() => world.Call("ranks.LIMIT"))!;
            Assert.That(e.Message, Does.Contain("'LIMIT' is a int"));
        }

        [TestCase("ranks.heal", "changes the world")]
        [TestCase("ranks.roll", "random numbers")]
        [TestCase("ranks.Hurt", "@command")]
        [TestCase("ranks.ouch", "@on handler")]
        [TestCase("ranks.never", "@when reaction")]
        public void RefusesWhatIsNotPure(string function, string reason)
        {
            var world = Make(out var unit);
            var args = function == "ranks.roll" || function == "ranks.ouch" || function == "ranks.never"
                ? new Value[0] : new Value[] { new EntityValue(unit) };
            var e = Assert.Throws<WorldException>(() => world.Call(function, args))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunNotCallable));
            Assert.That(e.Message, Does.Contain(reason));
            Assert.That(world.CheckCall(function, args.Length), Does.Contain(reason));
        }

        [Test]
        public void ArgumentCountMustMatch()
        {
            var world = Make(out _);
            var e = Assert.Throws<WorldException>(() => world.Call("ranks.unit_row"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunNotCallable));
            Assert.That(e.Message, Does.Contain("takes 1 argument, not 0"));
        }

        [Test]
        public void ScriptErrorsAndTheBudgetThrowWithAPlace()
        {
            var world = Make(out var unit, stepBudget: 200);
            var e = Assert.Throws<WorldException>(() => world.Call("ranks.broken", new EntityValue(unit)))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunArithmetic));
            Assert.That(e.Message, Does.Contain("ranks.broken").And.Contain("defs.py:"));
            e = Assert.Throws<WorldException>(() => world.Call("ranks.forever"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunBudget));
            // 실패한 뒤에도 다음 칸은 그대로 부를 수 있다
            Assert.That(world.Call("ranks.units").Value, Is.InstanceOf<ListValue>());
        }

        [Test]
        public void CheckCallSaysNothingWhenFine()
        {
            var world = Make(out _);
            Assert.That(world.CheckCall("ranks.unit_row", 1), Is.Null);
            Assert.That(world.CheckCall("ranks.unit_rows", 1), Does.Contain("unit_row"));
            Assert.That(world.CheckCall("ranks.unit_row", 2), Does.Contain("takes 1 argument"));
        }
    }
}
