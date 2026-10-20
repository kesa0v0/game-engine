using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>영수증은 (엔티티, 컴포넌트) 단위로 다시 부를지 본다 (spec-gaps G246·G290).</summary>
    public class CallReceiptNarrowingTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Life\", hp=field(int, default=0))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True, inverse=\"units\"))\n" +
            "component(\"Side\", name=field(str, default=\"\"), gold=field(int, default=0))\n" +
            "component(\"Mark\", x=field(int, default=0))\n" +
            "component(\"Bag\", key=int, n=field(int, default=0))\n" +
            "component(\"Power\", base=field(int, default=1),\n" +
            "    total=derived(int, lambda p: p.base + (p.Owner.party.Side.gold if p.Owner.party is not None else 0)),\n" +
            "    boost=derived(int, lambda p: len(contribs(p, \"boost\"))))\n" +
            "deftype(\"Party\", components=[\"Side\"])\n" +
            "deftype(\"Unit\", components=[\"Life\", \"Owner\", \"Power\", \"Bag\"])\n";

        private const string Defs =
            "Party(\"Red\")\nUnit(\"Soldier\")\n" +
            "def hp(u):\n    return u.Life.hp\n" +
            "def owner_name(u):\n    return u.Owner.party.Side.name\n" +
            "def no_owner(u):\n    return u.Owner.party is None\n" +
            "def marked(u):\n    return u.Mark is not None\n" +
            "def bag(u):\n    return [k for k in u.Bag]\n" +
            "def bag_n(u):\n    return u.Bag[1].n if 1 in u.Bag else -1\n" +
            "def members(p):\n    return len(p.units)\n" +
            "def total(u):\n    return u.Power.total\n" +
            "def boost(u):\n    return u.Power.boost\n" +
            "def who(u):\n    return controller(u)\n";

        private static readonly string[] UnitFunctions =
            { "hp", "owner_name", "no_owner", "marked", "bag", "bag_n", "total", "boost", "who" };

        private World _world = null!;
        private EntityId _unit, _other, _red, _blue;

        [SetUp]
        public void Make()
        {
            Mod("ranks", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("ranks:Red");
            _blue = _world.Create("ranks:Red");
            _unit = _world.Create("ranks:Soldier", new Init(M("Owner", "party"), new EntityValue(_red)));
            _other = _world.Create("ranks:Soldier");
        }

        private MemberHandle M(string owner, string member) => _world.Member(owner, member);

        private CallResult Call(string function, EntityId arg) => _world.Call("ranks." + function, new EntityValue(arg));

        [Test]
        public void AComponentThatWasNotReadDoesNotAskForACall()
        {
            var hp = Call("hp", _unit);
            var who = Call("who", _unit);
            var members = Call("members", _red);
            _world.Set(_unit, M("Owner", "party"), new EntityValue(_blue));
            Assert.That(hp.Changed(_world), Is.False, "hp read only Life");
            _world.Set(_unit, M("Life", "hp"), new IntValue(3));
            Assert.That(who.Changed(_world), Is.False, "controller() does not read Life");
            Assert.That(hp.Changed(_world), Is.True);
            members = Call("members", _red);
            _world.Set(_red, M("Side", "name"), new StrValue("Crimson"));
            Assert.That(members.Changed(_world), Is.False, "the units collection is not the Side component");
        }

        [Test]
        public void ARefTargetIsReadOnlyForWhetherItIsAlive()
        {
            var none = Call("no_owner", _unit);
            _world.Set(_red, M("Side", "gold"), new IntValue(5));
            Assert.That(none.Changed(_world), Is.False);
            _world.Destroy(_red);
            Assert.That(none.Changed(_world), Is.True, "a dead target reads as None");
        }

        [Test]
        public void TheReadThingsStillAskForACall()
        {
            var name = Call("owner_name", _unit);
            _world.Set(_red, M("Side", "name"), new StrValue("Crimson"));
            Assert.That(name.Changed(_world), Is.True, "the followed entity's component");
            name = Call("owner_name", _unit);
            _world.Set(_unit, M("Owner", "party"), new EntityValue(_blue));
            Assert.That(name.Changed(_world), Is.True, "the ref itself");

            var marked = Call("marked", _unit);
            _world.Add(_unit, "Mark");
            Assert.That(marked.Changed(_world), Is.True, "attaching what was read as None");
            marked = Call("marked", _unit);
            _world.Remove(_unit, "Mark");
            Assert.That(marked.Changed(_world), Is.True);

            var keys = Call("bag", _unit);
            var n = Call("bag_n", _unit);
            _world.AddRow(_unit, "Bag", 1);
            Assert.That(keys.Changed(_world), Is.True);
            Assert.That(n.Changed(_world), Is.True);
            n = Call("bag_n", _unit);
            _world.SetRow(_unit, M("Bag", "n"), 1, new IntValue(4));
            Assert.That(n.Changed(_world), Is.True);

            var members = Call("members", _blue);
            _world.Set(_other, M("Owner", "party"), new EntityValue(_blue));
            Assert.That(members.Changed(_world), Is.True, "the inverse collection");

            var total = Call("total", _unit);
            _world.Set(_blue, M("Side", "gold"), new IntValue(7));
            Assert.That(total.Changed(_world), Is.True, "a derived value that reads another entity");

            var boost = Call("boost", _unit);
            _world.AddContribution(_unit, M("Power", "boost"), "add", new IntValue(1));
            Assert.That(boost.Changed(_world), Is.True, "a contribution row");

            var who = Call("who", _unit);
            _world.Seat(_unit, "red-player");
            Assert.That(who.Changed(_world), Is.True, "a seat");
        }

        /// <summary>
        /// 쓰기를 하나씩 하며 모든 영수증을 본다: 거짓이면 다시 불러도 값이 같아야 한다(다시 부를 일을 빠뜨리지 않음). 위에 적지 않은
        /// 읽는 길도 잡는다.
        /// </summary>
        [Test]
        public void ANotChangedReceiptAlwaysGivesTheSameValue()
        {
            var writes = new List<(string Name, Action Write)>
            {
                ("unit hp", () => _world.Set(_unit, M("Life", "hp"), new IntValue(2))),
                ("other hp", () => _world.Set(_other, M("Life", "hp"), new IntValue(8))),
                ("power base", () => _world.Set(_unit, M("Power", "base"), new IntValue(5))),
                ("red gold", () => _world.Set(_red, M("Side", "gold"), new IntValue(3))),
                ("red name", () => _world.Set(_red, M("Side", "name"), new StrValue("Crimson"))),
                ("blue gold", () => _world.Set(_blue, M("Side", "gold"), new IntValue(4))),
                ("attach mark", () => _world.Add(_unit, "Mark")),
                ("add row", () => _world.AddRow(_unit, "Bag", 1)),
                ("set row", () => _world.SetRow(_unit, M("Bag", "n"), 1, new IntValue(6))),
                ("other joins red", () => _world.Set(_other, M("Owner", "party"), new EntityValue(_red))),
                ("contribution", () => _world.AddContribution(_unit, M("Power", "boost"), "add", new IntValue(1))),
                ("seat", () => _world.Seat(_unit, "red-player")),
                ("unit moves to blue", () => _world.Set(_unit, M("Owner", "party"), new EntityValue(_blue))),
                ("detach mark", () => _world.Remove(_unit, "Mark")),
                ("unseat", () => _world.Unseat(_unit)),
                ("spawn", () => _world.Create("ranks:Soldier")),
                ("blue dies", () => _world.Destroy(_blue)),
            };
            var calls = new List<(string Function, EntityId Arg)>();
            foreach (var f in UnitFunctions)
            {
                calls.Add((f, _unit));
                calls.Add((f, _other));
            }
            calls.Add(("members", _red));
            calls.Add(("members", _blue));

            var receipts = calls.Select(c => Try(c.Function, c.Arg)).ToList();
            int unchanged = 0;
            foreach (var (name, write) in writes)
            {
                write();
                for (int i = 0; i < calls.Count; i++)
                {
                    var again = Try(calls[i].Function, calls[i].Arg);
                    if (receipts[i].Result != null && !receipts[i].Result!.Changed(_world))
                    {
                        unchanged++;
                        Assert.That(again.Shown, Is.EqualTo(receipts[i].Shown),
                            calls[i].Function + "(" + calls[i].Arg + ") after '" + name + "' said not changed");
                    }
                    receipts[i] = again;
                }
            }
            Assert.That(unchanged, Is.GreaterThan(100), "the sweep should see many receipts that did not change");
        }

        private (CallResult? Result, string Shown) Try(string function, EntityId arg)
        {
            try
            {
                var r = Call(function, arg);
                return (r, Show(r.Value));
            }
            catch (WorldException e)
            {
                return (null, "error " + e.Code);
            }
        }

        private static string Show(Value v)
        {
            switch (v)
            {
                case IntValue i: return i.Value.ToString();
                case StrValue s: return "'" + s.Value + "'";
                case BoolValue b: return b.Value ? "True" : "False";
                case NoneValue _: return "None";
                case EntityValue e: return e.Id.ToString();
                case ListValue l: return "[" + string.Join(", ", l.Items.Select(Show)) + "]";
            }
            return v.KindName;
        }
    }
}
