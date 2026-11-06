using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>칼럼 저장이 새로 틀릴 수 있는 곳: 줄 비우기, 곁표 메우기, 되감기, 되감긴 id의 재사용 (G476).</summary>
    public class ColumnStoreTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Body\", hp=field(int, default=1), tag=field(int, default=7))\n" +
            "component(\"Stun\", left=field(int, default=3), power=field(int, default=1))\n" +
            "deftype(\"Unit\", components=[\"Body\"])\n";

        private const string Defs =
            "Unit(\"Grunt\")\n" +
            "Unit(\"Tank\", Body=Body(hp=50))\n" +
            "@command(free=True, acts_for=None)\ndef KillThenFail(u: Ref[Unit]):\n    destroy(u)\n    x = 1 // 0\n" +
            "@command(free=True, acts_for=None)\ndef StunThenFail(u: Ref[Unit]):\n    attach(u, Stun(left=9))\n    x = 1 // 0\n" +
            "@command(free=True, acts_for=None)\ndef UnstunThenFail(u: Ref[Unit]):\n    detach(u.Stun)\n    x = 1 // 0\n" +
            "@command(free=True, acts_for=None)\ndef SpawnStunnedThenFail():\n    u = spawn(\"core:Grunt\")\n    attach(u, Stun(left=9))\n    x = 1 // 0\n" +
            "@command(free=True, acts_for=None)\ndef KillAll():\n    for u in query(Unit):\n        destroy(u)\n";

        private World _world = null!;
        private MemberHandle _hp = null!;
        private MemberHandle _left = null!;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(Loaded());
            _hp = _world.Member("Body", "hp");
            _left = _world.Member("Stun", "left");
        }

        private ContentLoadResult Loaded()
        {
            var bag = new DiagnosticBag();
            var loaded = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            return loaded;
        }

        private long Hp(EntityId id) => ((IntValue)_world.Get(id, _hp)).Value;
        private long Left(EntityId id) => ((IntValue)_world.Get(id, _left)).Value;

        private SubmitResult Submit(string command, params (string Name, EntityId Id)[] args)
        {
            var map = new Dictionary<string, Value>();
            foreach (var (name, id) in args) map[name] = new EntityValue(id);
            return _world.Submit("host", "core." + command, map);
        }

        private List<EntityId> Grunts(int count, long firstHp = 100)
        {
            var ids = new List<EntityId>();
            for (int i = 0; i < count; i++)
            {
                var id = _world.Create("core:Grunt");
                _world.Set(id, _hp, new IntValue(firstHp + i));
                ids.Add(id);
            }
            return ids;
        }

        [Test]
        public void ValuesSurviveGrowingPastTheFirstCapacity()
        {
            var ids = Grunts(100);
            for (int i = 0; i < ids.Count; i++) Assert.That(Hp(ids[i]), Is.EqualTo(100 + i));
        }

        [Test]
        public void AColumnFirstWrittenLateReadsDefaultsForEarlierRows()
        {
            var ids = new List<EntityId>();
            for (int i = 0; i < 20; i++) ids.Add(_world.Create("core:Grunt"));
            _world.Set(ids[15], _hp, new IntValue(9));
            Assert.That(Hp(ids[0]), Is.EqualTo(1));
            Assert.That(Hp(ids[15]), Is.EqualTo(9));
            Assert.That(Hp(_world.Create("core:Grunt")), Is.EqualTo(1));
        }

        [Test]
        public void DefsDoNotShareValues()
        {
            var grunt = _world.Create("core:Grunt");
            var tank = _world.Create("core:Tank");
            _world.Set(grunt, _hp, new IntValue(5));
            Assert.That(Hp(tank), Is.EqualTo(50));
            _world.Set(tank, _hp, new IntValue(6));
            Assert.That(Hp(grunt), Is.EqualTo(5));
        }

        [Test]
        public void SweepingDeadRowsKeepsTheLiveOnesAndTheirOrder()
        {
            var ids = Grunts(400);
            for (int i = 0; i < ids.Count; i++)
                if (i % 4 != 0) _world.Destroy(ids[i]);

            var live = ids.Where((_, i) => i % 4 == 0).ToList();
            Assert.That(_world.All("Unit"), Is.EqualTo(live));
            foreach (var id in live) Assert.That(Hp(id), Is.EqualTo(100 + ids.IndexOf(id)));
            foreach (var id in live) _world.Set(id, _hp, new IntValue(Hp(id) + 1));
            var extra = _world.Create("core:Grunt");
            Assert.That(Hp(extra), Is.EqualTo(1));
            foreach (var id in live) Assert.That(Hp(id), Is.EqualTo(101 + ids.IndexOf(id)));
        }

        [Test]
        public void ACommandThatDestroysAndFailsBringsTheValuesBack()
        {
            var ids = Grunts(10);
            Assert.That(Submit("KillThenFail", ("u", ids[3])).Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(_world.IsAlive(ids[3]), Is.True);
            Assert.That(Hp(ids[3]), Is.EqualTo(103));
        }

        [Test]
        public void AFailedCommandRestoresAfterRowsMoved()
        {
            var ids = Grunts(300);
            for (int i = 0; i < 200; i++) _world.Destroy(ids[i]); // 뒤쪽 줄이 앞으로 옮겨 간다
            var survivor = ids[250];
            Assert.That(Hp(survivor), Is.EqualTo(350));
            Assert.That(Submit("KillThenFail", ("u", survivor)).Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Hp(survivor), Is.EqualTo(350));
        }

        [Test]
        public void DetachingFillsTheHoleWithoutMixingUpOthers()
        {
            var ids = Grunts(10);
            for (int i = 0; i < ids.Count; i++) _world.Add(ids[i], "Stun", new Init(_left, new IntValue(20 + i)));
            _world.Remove(ids[2], "Stun");
            _world.Remove(ids[0], "Stun");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i == 0 || i == 2)
                {
                    Assert.That(_world.Has(ids[i], "Stun"), Is.False);
                    continue;
                }
                Assert.That(Left(ids[i]), Is.EqualTo(20 + i));
            }
            _world.Add(ids[2], "Stun");
            Assert.That(Left(ids[2]), Is.EqualTo(3), "다시 붙이면 옛 값이 아니라 기본값이다");
        }

        [Test]
        public void AFailedAttachLeavesNoValueBehind()
        {
            var id = _world.Create("core:Grunt");
            Assert.That(Submit("StunThenFail", ("u", id)).Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(_world.Has(id, "Stun"), Is.False);
            _world.Add(id, "Stun");
            Assert.That(Left(id), Is.EqualTo(3));
        }

        [Test]
        public void AFailedDetachBringsTheValueBack()
        {
            var id = _world.Create("core:Grunt");
            _world.Add(id, "Stun", new Init(_left, new IntValue(8)));
            Assert.That(Submit("UnstunThenFail", ("u", id)).Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Left(id), Is.EqualTo(8));
        }

        [Test]
        public void AnIdGivenBackByAFailedCommandStartsClean()
        {
            Assert.That(Submit("SpawnStunnedThenFail").Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            var id = _world.Create("core:Grunt");
            Assert.That(id.Value, Is.EqualTo(1), "실패한 커맨드의 id를 다시 쓴다");
            Assert.That(_world.Has(id, "Stun"), Is.False);
            _world.Add(id, "Stun");
            Assert.That(Left(id), Is.EqualTo(3));
            Assert.That(Hp(id), Is.EqualTo(1));
        }

        [Test]
        public void ACommandThatKillsManyLeavesAWorkingStore()
        {
            var ids = Grunts(200);
            Assert.That(Submit("KillAll").Succeeded, Is.True);
            Assert.That(_world.All("Unit"), Is.Empty);
            var next = Grunts(5, 500);
            for (int i = 0; i < 5; i++) Assert.That(Hp(next[i]), Is.EqualTo(500 + i));
            Assert.That(ids.Any(_world.IsAlive), Is.False);
        }

        [Test]
        public void ASaveRoundTripKeepsDefAndSideValues()
        {
            var ids = Grunts(20);
            _world.Add(ids[5], "Stun", new Init(_left, new IntValue(11)));
            var bytes = _world.Save();
            var again = World.Load(Loaded(), bytes).World;
            Assert.That(again!.Save(), Is.EqualTo(bytes));
            Assert.That(((IntValue)again.Get(ids[5], again.Member("Stun", "left"))).Value, Is.EqualTo(11));
            Assert.That(((IntValue)again.Get(ids[6], again.Member("Body", "hp"))).Value, Is.EqualTo(106));
        }

        [Test]
        public void SweepingLeavesASaveThatRoundTrips()
        {
            var ids = Grunts(400);
            _world.Add(ids[399], "Stun", new Init(_left, new IntValue(11)));
            for (int i = 0; i < 300; i++) _world.Destroy(ids[i]);

            var bytes = _world.Save();
            var again = World.Load(Loaded(), bytes).World;
            Assert.That(again!.Save(), Is.EqualTo(bytes));
            Assert.That(((IntValue)again.Get(ids[399], again.Member("Stun", "left"))).Value, Is.EqualTo(11));
            Assert.That(((IntValue)again.Get(ids[300], again.Member("Body", "hp"))).Value, Is.EqualTo(400));
        }
    }
}
