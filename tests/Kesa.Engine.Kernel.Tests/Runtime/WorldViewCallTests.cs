using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// <c>ViewAs(연결).Call</c> (spec-gaps G248): 함수 안의 읽기가 보는 쪽 세계를 거친다. 안개 속(<c>Fog.hidden</c>) 유닛은 주인만,
    /// 진영의 <c>Hand</c>는 그 진영만 본다.
    /// </summary>
    public class WorldViewCallTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hand\", cards=field(int, default=0))\n" +
            "component(\"Power\", total=derived(int, lambda c: sum([u.Life.hp for u in c.entity.units])))\n" +
            "component(\"Known\", key=Ref[\"Unit\"], note=field(int, default=0))\n" +
            "component(\"Life\", hp=field(int, default=0))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True, inverse=\"units\"))\n" +
            "component(\"Fog\", hidden=field(bool, default=False))\n" +
            "component(\"Spy\", on=ref(\"Unit\", optional=True))\n" +
            "deftype(\"Party\", components=[\"Hand\", \"Power\", \"Known\"])\n" +
            "deftype(\"Unit\", components=[\"Life\", \"Owner\", \"Fog\", \"Spy\"])\n";

        private const string Defs =
            "Party(\"Side\")\nUnit(\"Soldier\")\n" +
            "def omniscient(viewer):\n" +
            "    return viewer is not None and viewer.Hand.cards >= 100\n" +
            "@view_rule(Unit)\n" +
            "def fog(viewer, entity):\n" +
            "    return omniscient(viewer) or not entity.Fog.hidden or viewer == entity.Owner.party\n" +
            "@view_rule(Hand)\n" +
            "def own_hand(viewer, entity):\n" +
            "    return omniscient(viewer) or viewer == entity\n" +
            "def enemy_hp(me):\n    return sum([u.Life.hp for u in query(Unit) if u.Owner.party != me])\n" +
            "def hand(p):\n    return -1 if p.Hand is None else p.Hand.cards\n" +
            "def army(p):\n    return len(p.units)\n" +
            "def spied(u):\n    return u.Spy.on\n" +
            "def hp(u):\n    return u.Life.hp\n" +
            "def known(p):\n    return [k for k in p.Known]\n" +
            "def power(p):\n    return p.Power.total\n" +
            "def who(u):\n    return controller(u)\n" +
            "def boosters(p):\n    return [c.source for c in contribs(p.Power, \"total\")]\n";

        private World _w = null!;
        private EntityId _red, _blue, _r1, _b1, _b2;

        [SetUp]
        public void NewWorld()
        {
            Mod("scouts", schema: Schema, defs: Defs);
            _w = new World(LoadClean());
            _red = Party("red", 3);
            _blue = Party("blue", 5);
            _r1 = Unit(_red, 4, hidden: false);
            _b1 = Unit(_blue, 6, hidden: false);
            _b2 = Unit(_blue, 10, hidden: true);
            _w.Set(_r1, M("Spy", "on"), new EntityValue(_b2));
            _w.AddRow(_red, "Known", _b2, new Init(M("Known", "note"), new IntValue(1)));
            _w.AddRow(_red, "Known", _b1, new Init(M("Known", "note"), new IntValue(2)));
        }

        private EntityId Party(string conn, int cards)
        {
            var p = _w.Create("scouts:Side");
            _w.Seat(p, conn);
            _w.Set(p, M("Hand", "cards"), new IntValue(cards));
            return p;
        }

        private EntityId Unit(EntityId party, int hp, bool hidden)
        {
            var u = _w.Create("scouts:Soldier");
            _w.Set(u, M("Owner", "party"), new EntityValue(party));
            _w.Set(u, M("Life", "hp"), new IntValue(hp));
            _w.Set(u, M("Fog", "hidden"), hidden ? BoolValue.True : BoolValue.False);
            return u;
        }

        private MemberHandle M(string owner, string member) => _w.Member(owner, member);

        private static long Int(CallResult r) => ((IntValue)r.Value).Value;

        [Test]
        public void AQueryLeavesOutWhatTheViewerDoesNotSee()
        {
            Assert.That(Int(_w.Call("scouts.enemy_hp", new EntityValue(_red))), Is.EqualTo(16));
            Assert.That(Int(_w.ViewAs("red").Call("scouts.enemy_hp", new EntityValue(_red))), Is.EqualTo(6));
        }

        [Test]
        public void AnUnseenComponentIsAsIfNotAttached()
        {
            var red = _w.ViewAs("red");
            Assert.That(Int(red.Call("scouts.hand", new EntityValue(_red))), Is.EqualTo(3));
            Assert.That(Int(red.Call("scouts.hand", new EntityValue(_blue))), Is.EqualTo(-1));
        }

        [Test]
        public void CollectionsAndKeyedRowsLeaveOutUnseenEntities()
        {
            var red = _w.ViewAs("red");
            Assert.That(Int(red.Call("scouts.army", new EntityValue(_blue))), Is.EqualTo(1));
            Assert.That(Int(_w.ViewAs("blue").Call("scouts.army", new EntityValue(_blue))), Is.EqualTo(2));
            var keys = ((ListValue)red.Call("scouts.known", new EntityValue(_red)).Value).Items;
            Assert.That(keys.Select(k => ((EntityValue)k).Id), Is.EqualTo(new[] { _b1 }));
        }

        [Test]
        public void ARefToAnUnseenEntityIsHidden()
        {
            Assert.That(_w.ViewAs("red").Call("scouts.spied", new EntityValue(_r1)).Value, Is.SameAs(HiddenValue.Instance));
            Assert.That(((EntityValue)_w.ViewAs("blue").Call("scouts.spied", new EntityValue(_r1)).Value).Id, Is.EqualTo(_b2));
        }

        [Test]
        public void AnUnseenArgumentReadsLikeADeadOne()
        {
            var e = Assert.Throws<WorldException>(() => _w.ViewAs("red").Call("scouts.hp", new EntityValue(_b2)))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunDeadEntity));
            Assert.That(Int(_w.ViewAs("blue").Call("scouts.hp", new EntityValue(_b2))), Is.EqualTo(10));
        }

        [Test]
        public void AVisibleDerivedValueIsComputedFromTheRealState()
        {
            // G199: 보이는 derived는 숨긴 칸으로 계산돼도 그 값이 보인다
            Assert.That(Int(_w.ViewAs("red").Call("scouts.power", new EntityValue(_blue))), Is.EqualTo(16));
        }

        [Test]
        public void ADerivedThatReadsOtherEntitiesIsInTheReceiptButNotTheStamp()
        {
            // G251: 도장은 저장된 값의 것이라 진영의 도장은 그대로지만, 함수 안의 derived가 읽은 유닛은 영수증에 든다
            var r = _w.Call("scouts.power", new EntityValue(_blue));
            long stamp = _w.Version(_blue, "Power");
            Assert.That(r.Changed(_w), Is.False);
            _w.Set(_b1, M("Life", "hp"), new IntValue(1));
            Assert.That(_w.Version(_blue, "Power"), Is.EqualTo(stamp));
            Assert.That(r.Changed(_w), Is.True);
            Assert.That(Int(_w.Call("scouts.power", new EntityValue(_blue))), Is.EqualTo(11));
        }

        [Test]
        public void ControllerOfAnUnseenEntityIsNone()
        {
            _w.Seat(_b2, "bluebot", SeatKind.Ai);
            Assert.That(_w.ViewAs("red").Call("scouts.who", new EntityValue(_b2)).Value, Is.SameAs(NoneValue.Instance));
            Assert.That(((StrValue)_w.ViewAs("blue").Call("scouts.who", new EntityValue(_b2)).Value).Value, Is.EqualTo("ai"));
        }

        [Test]
        public void ContributionsFromUnseenSourcesAreLeftOut()
        {
            _w.AddContribution(_red, M("Power", "total"), "add", new IntValue(1), _b2);
            _w.AddContribution(_red, M("Power", "total"), "add", new IntValue(2), _r1);
            var seen = ((ListValue)_w.ViewAs("red").Call("scouts.boosters", new EntityValue(_red)).Value).Items;
            Assert.That(seen.Select(v => ((EntityValue)v).Id), Is.EqualTo(new[] { _r1 }));
            var all = ((ListValue)_w.ViewAs("blue").Call("scouts.boosters", new EntityValue(_red)).Value).Items;
            Assert.That(all.Select(v => ((EntityValue)v).Id), Is.EquivalentTo(new[] { _b2, _r1 }));
        }

        [Test]
        public void WhatThePolicyReadIsInTheReceipt()
        {
            var r = _w.ViewAs("red").Call("scouts.enemy_hp", new EntityValue(_red));
            Assert.That(r.Changed(_w), Is.False);
            _w.Set(_b2, M("Fog", "hidden"), BoolValue.False); // 안개가 걷혔다
            Assert.That(r.Changed(_w), Is.True);
            r = _w.ViewAs("red").Call("scouts.enemy_hp", new EntityValue(_red));
            Assert.That(Int(r), Is.EqualTo(16));
        }

        [Test]
        public void ASeatChangeAsksForACall()
        {
            var eye = Party("watcher", 100); // 다 보는 자리. red의 함수는 아직 이 엔티티를 읽은 적이 없다
            var r = _w.ViewAs("red").Call("scouts.enemy_hp", new EntityValue(_red));
            Assert.That(r.Changed(_w), Is.False);
            _w.Seat(eye, "red"); // red가 그 자리에도 앉으면 안개 속 유닛도 본다
            Assert.That(r.Changed(_w), Is.True);
            Assert.That(Int(_w.ViewAs("red").Call("scouts.enemy_hp", new EntityValue(_red))), Is.EqualTo(16));
        }

        [Test]
        public void CallingAsAViewerLeavesTheWorldUnfiltered()
        {
            _w.ViewAs("red").Call("scouts.enemy_hp", new EntityValue(_red));
            Assert.That(Int(_w.Call("scouts.enemy_hp", new EntityValue(_red))), Is.EqualTo(16));
            Assert.That(_w.ViewAs("red").CheckCall("scouts.enemy_hp", 1), Is.Null);
        }
    }
}
