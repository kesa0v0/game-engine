using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>호스트의 묶음 읽기·쓰기 (G478).</summary>
    public class ColumnPartsTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Body\", hp=field(int, default=1, min=0, max=1000), power=field(fixed, default=1.5), up=field(bool, default=True),\n" +
            "    name=field(str, default=\"x\"), twice=derived(int, lambda b: b.hp * 2), owner=ref(\"Unit\", optional=True))\n" +
            "component(\"Stun\", left=field(int, default=3))\n" +
            "component(\"Secret\", code=field(int, default=5))\n" +
            "component(\"Bag\", key=int, n=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Body\"], kind=const(int, default=0))\n" +
            "deftype(\"Agent\", components=[\"Body\", \"Secret\"])\n" +
            "bot_state(\"Mind\", seen=field(list[int], default=[]))\n";

        private const string Defs =
            "Unit(\"Grunt\")\n" +
            "Unit(\"Tank\", Body=Body(hp=50, power=2))\n" +
            "Agent(\"Spy\", Secret=Secret(code=9))\n" +
            "@view_rule(Secret)\ndef hidden(viewer, entity):\n    return False\n" +
            "@command(free=True, acts_for=None)\ndef BulkThenFail():\n    core.bulk.grow()\n    x = 1 // 0\n" +
            "@command(free=True, acts_for=None)\ndef Bulk():\n    core.bulk.grow()\n" +
            "@command(free=True, acts_for=None)\ndef KillBulkFail(u: Ref[Unit]):\n    core.bulk.grow()\n    destroy(u)\n    x = 1 // 0\n" +
            "def total():\n    return core.bulk.sum()\n" +
            "def secrets():\n    return core.bulk.secrets()\n" +
            "@bot\ndef peek(me: Ref[Unit]):\n    with imagine():\n        n = core.bulk.secrets()\n    bot.Mind.seen = [n]\n";

        private ContentLoadResult _loaded = null!;
        private World _world = null!;
        private MemberHandle _hp = null!, _power = null!, _up = null!;

        [SetUp]
        public void NewWorld() => (_world, _hp, _power, _up) = Make();

        private (World, MemberHandle, MemberHandle, MemberHandle) Make()
        {
            var host = new HostFunctions();
            var core = host.For("core");
            core.Writes("def bulk.grow() -> None", "", (HostAccess a) =>
            {
                var hp = a.Member("Body", "hp");
                foreach (var part in a.Parts("Unit", hp))
                {
                    var next = new long[part.Count];
                    var now = part.Longs(hp);
                    for (int i = 0; i < next.Length; i++) next[i] = now[i] + 1;
                    a.WriteColumn(hp, part.Ids, next);
                }
            });
            core.Pure("def bulk.sum() -> int", "", (HostReader r) =>
            {
                var hp = r.Member("Body", "hp");
                long sum = 0;
                foreach (var part in r.Parts("Unit", hp))
                    foreach (long v in part.Longs(hp))
                        sum += v;
                return sum;
            });
            core.Pure("def bulk.secrets() -> int", "", (HostReader r) =>
            {
                var code = r.Member("Secret", "code");
                long seen = 0;
                foreach (var part in r.Parts("Agent", code)) seen += part.Count;
                return seen;
            });

            Mod("core", schema: Schema, defs: Defs);
            var bag = new DiagnosticBag();
            var loaded = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag, host);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _loaded = loaded;
            var world = new World(loaded);
            return (world, world.Member("Body", "hp"), world.Member("Body", "power"), world.Member("Body", "up"));
        }

        private List<(long Id, long Hp)> ReadHp(World world, MemberHandle hp)
        {
            var seen = new List<(long, long)>();
            foreach (var part in world.Parts("Unit", hp))
            {
                var ids = part.Ids;
                var values = part.Longs(hp);
                for (int i = 0; i < ids.Length; i++) seen.Add((ids[i], values[i]));
            }
            return seen;
        }

        private long Hp(EntityId id) => ((IntValue)_world.Get(id, _hp)).Value;

        private SubmitResult Submit(string command, EntityId? unit = null)
        {
            var map = new Dictionary<string, Value>();
            if (unit != null) map["u"] = new EntityValue(unit.Value);
            return _world.Submit("host", "core." + command, map);
        }

        [Test]
        public void PartsShowWrittenDefAndDefaultValues()
        {
            var grunt = _world.Create("core:Grunt");
            var tank = _world.Create("core:Tank");
            var written = _world.Create("core:Grunt");
            _world.Set(written, _hp, new IntValue(7));
            _world.Set(written, _power, new FixedValue(Fixed.FromRaw(3_000_000)));
            _world.Set(written, _up, BoolValue.False);

            var hp = new Dictionary<long, long>();
            var power = new Dictionary<long, long>();
            var up = new Dictionary<long, long>();
            foreach (var part in _world.Parts("Unit", _hp, _power, _up))
            {
                var ids = part.Ids;
                for (int i = 0; i < ids.Length; i++)
                {
                    hp[ids[i]] = part.Longs(_hp)[i];
                    power[ids[i]] = part.Fixeds(_power)[i].Raw;
                    up[ids[i]] = part.Longs(_up)[i];
                }
            }
            Assert.That(hp[grunt.Value], Is.EqualTo(1));
            Assert.That(hp[tank.Value], Is.EqualTo(50));
            Assert.That(hp[written.Value], Is.EqualTo(7));
            Assert.That(power[grunt.Value], Is.EqualTo(1_500_000));
            Assert.That(power[tank.Value], Is.EqualTo(2_000_000), "Def의 int가 fixed 칸에서 맞는 단위로 펴진다");
            Assert.That(power[written.Value], Is.EqualTo(3_000_000));
            Assert.That(up[grunt.Value], Is.EqualTo(1));
            Assert.That(up[written.Value], Is.EqualTo(0));
        }

        [Test]
        public void DefaultsSurviveGrowthAndColumnsStartedLate()
        {
            var ids = new List<EntityId>();
            for (int i = 0; i < 3; i++) ids.Add(_world.Create("core:Tank"));
            _world.Set(ids[0], _hp, new IntValue(9)); // 칼럼이 만들어진다
            for (int i = 0; i < 100; i++) ids.Add(_world.Create("core:Tank")); // 칼럼이 는다
            var seen = ReadHp(_world, _hp);
            Assert.That(seen.Count, Is.EqualTo(ids.Count));
            for (int i = 0; i < ids.Count; i++)
                Assert.That(seen[i], Is.EqualTo((ids[i].Value, Hp(ids[i]))));
            Assert.That(seen[100].Hp, Is.EqualTo(50));
        }

        [Test]
        public void PartsComeInDefOrderAndIdOrderAndSkipTheDead()
        {
            var tank = _world.Create("core:Tank");
            var a = _world.Create("core:Grunt");
            var b = _world.Create("core:Grunt");
            var c = _world.Create("core:Grunt");
            _world.Destroy(b);

            var seen = ReadHp(_world, _hp).Select(p => p.Id).ToList();
            Assert.That(seen, Is.EqualTo(new[] { a.Value, c.Value, tank.Value }), "Grunt가 Tank보다 먼저 등록됐다");
            int parts = 0;
            foreach (var part in _world.Parts("Unit", _hp)) parts++;
            Assert.That(parts, Is.EqualTo(3), "죽은 줄에서 끊어진다");
        }

        [Test]
        public void TheOrderDoesNotChangeOverASaveAndLoad()
        {
            var tank = _world.Create("core:Tank");
            var grunt = _world.Create("core:Grunt");
            var doomed = _world.Create("core:Tank");
            _world.Destroy(tank);
            _world.Destroy(doomed);
            var other = _world.Create("core:Tank");

            var before = ReadHp(_world, _hp);
            Assert.That(before.Select(p => p.Id), Is.EqualTo(new[] { grunt.Value, other.Value }));

            var again = World.Load(_loaded, _world.Save()).World!;
            Assert.That(ReadHp(again, again.Member("Body", "hp")), Is.EqualTo(before));
        }

        [Test]
        public void SweepingKeepsPartsRight()
        {
            var ids = new List<EntityId>();
            for (int i = 0; i < 400; i++)
            {
                var id = _world.Create("core:Grunt");
                _world.Set(id, _hp, new IntValue(i % 900));
                ids.Add(id);
            }
            for (int i = 0; i < 300; i++) _world.Destroy(ids[i]);

            var seen = ReadHp(_world, _hp);
            Assert.That(seen.Select(p => p.Id), Is.EqualTo(ids.Skip(300).Select(i => i.Value)));
            Assert.That(seen.Select(p => p.Hp), Is.EqualTo(Enumerable.Range(300, 100).Select(i => (long)i)));
            var extra = _world.Create("core:Grunt");
            Assert.That(ReadHp(_world, _hp).Last(), Is.EqualTo((extra.Value, 1L)), "비운 자리에 옛 값이 남지 않는다");
        }

        [Test]
        public void ReadingAPartDoesNotAllocate()
        {
            for (int i = 0; i < 100; i++) _world.Create("core:Grunt");
            var hp = _hp;
            long sum = 0;
            var parts = _world.Parts("Unit", hp);
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var part in parts)
                foreach (long v in part.Longs(hp))
                    sum += v;
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
            Assert.That(sum, Is.EqualTo(100));
        }

        [TestCase("create")]
        [TestCase("destroy")]
        [TestCase("attach")]
        public void AStructureChangeEndsThePart(string change)
        {
            var id = _world.Create("core:Grunt");
            var other = _world.Create("core:Tank");
            InvalidOperationException? caught = null;
            foreach (var part in _world.Parts("Unit", _hp))
            {
                if (change == "create") _world.Create("core:Grunt");
                if (change == "destroy") _world.Destroy(other);
                if (change == "attach") _world.Add(id, "Stun");
                try
                {
                    var _ = part.Longs(_hp);
                }
                catch (InvalidOperationException e)
                {
                    caught = e;
                }
                break;
            }
            Assert.That(caught, Is.Not.Null);
            Assert.That(caught!.Message, Does.Contain("out of date"));
        }

        [Test]
        public void MovingToTheNextPartAfterAChangeFails()
        {
            _world.Create("core:Grunt");
            _world.Create("core:Tank");
            var parts = _world.Parts("Unit", _hp);
            Assert.That(parts.MoveNext(), Is.True);
            _world.Create("core:Tank");
            bool threw = false;
            try
            {
                parts.MoveNext();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert.That(threw, Is.True);
        }

        [Test]
        public void WritingAColumnKeepsThePartAndShowsTheNewValues()
        {
            var id = _world.Create("core:Grunt");
            foreach (var part in _world.Parts("Unit", _hp))
            {
                var view = part.Longs(_hp);
                _world.WriteColumn(_hp, part.Ids, new long[] { 77 });
                Assert.That(view[0], Is.EqualTo(77));
                Assert.That(part.Longs(_hp)[0], Is.EqualTo(77));
            }
            Assert.That(Hp(id), Is.EqualTo(77));
        }

        [Test]
        public void WriteColumnSavesLikeSet()
        {
            var ids = new long[3];
            for (int i = 0; i < 3; i++) ids[i] = _world.Create("core:Grunt").Value;
            _world.WriteColumn(_hp, ids, new long[] { 5, 600, 7 });
            _world.WriteColumn(_up, new[] { ids[1] }, new long[] { 0 });
            _world.WriteColumn(_power, new[] { ids[2] }, new[] { Fixed.FromRaw(2_500_000) });

            var (other, hp, power, up) = Make();
            for (int i = 0; i < 3; i++) other.Create("core:Grunt");
            other.Set(new EntityId(ids[0]), hp, new IntValue(5));
            other.Set(new EntityId(ids[1]), hp, new IntValue(600));
            other.Set(new EntityId(ids[2]), hp, new IntValue(7));
            other.Set(new EntityId(ids[1]), up, BoolValue.False);
            other.Set(new EntityId(ids[2]), power, new FixedValue(Fixed.FromRaw(2_500_000)));
            Assert.That(_world.Save(), Is.EqualTo(other.Save()));
        }

        [Test]
        public void ABadValueWritesNothing()
        {
            var a = _world.Create("core:Grunt");
            var b = _world.Create("core:Grunt");
            var before = _world.Save();
            var ids = new[] { a.Value, b.Value };

            var high = Assert.Throws<WorldException>(() => _world.WriteColumn(_hp, ids, new long[] { 5, 1001 }));
            Assert.That(high!.Code, Is.EqualTo(Codes.RunRange));
            var low = Assert.Throws<WorldException>(() => _world.WriteColumn(_hp, ids, new long[] { 5, -1 }));
            Assert.That(low!.Code, Is.EqualTo(Codes.RunRange));
            var flag = Assert.Throws<WorldException>(() => _world.WriteColumn(_up, ids, new long[] { 1, 2 }));
            Assert.That(flag!.Code, Is.EqualTo(Codes.RunType));
            _world.Destroy(b);
            before = _world.Save();
            var dead = Assert.Throws<WorldException>(() => _world.WriteColumn(_hp, ids, new long[] { 5, 6 }));
            Assert.That(dead!.Code, Is.EqualTo(Codes.RunDeadEntity));
            Assert.That(_world.Save(), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => _world.WriteColumn(_hp, new[] { a.Value }, new long[] { 1, 2 }));
        }

        [Test]
        public void AFailedCommandUndoesTheWholeWrite()
        {
            var written = _world.Create("core:Grunt");
            var untouched = _world.Create("core:Tank");
            _world.Set(written, _hp, new IntValue(10));
            var before = _world.Save();

            Assert.That(Submit("BulkThenFail").Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Hp(written), Is.EqualTo(10));
            Assert.That(Hp(untouched), Is.EqualTo(50));
            Assert.That(_world.Save(), Is.EqualTo(before), "쓴 적 없던 칸은 다시 쓴 적 없는 칸이 된다");
        }

        [Test]
        public void ACommandWritesAndAFailureAfterADestroyStillRewinds()
        {
            var a = _world.Create("core:Grunt");
            var b = _world.Create("core:Grunt");
            Assert.That(Submit("Bulk").Succeeded, Is.True);
            Assert.That(Hp(a), Is.EqualTo(2));
            var before = _world.Save();
            Assert.That(Submit("KillBulkFail", b).Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(_world.IsAlive(b), Is.True);
            Assert.That(Hp(b), Is.EqualTo(2));
            Assert.That(_world.Save(), Is.EqualTo(before));
        }

        [Test]
        public void HostWritesAndReceiptsSeeEachOther()
        {
            var id = _world.Create("core:Grunt");
            var result = _world.Call("core.total");
            Assert.That(((IntValue)result.Value).Value, Is.EqualTo(1));
            Assert.That(result.Changed(_world), Is.False);

            _world.WriteColumn(_hp, new[] { id.Value }, new long[] { 5 });
            Assert.That(result.Changed(_world), Is.True, "묶음으로 읽은 영수증도 묶음 쓰기로 낡는다");
            Assert.That(((IntValue)_world.Call("core.total").Value).Value, Is.EqualTo(5));
        }

        [Test]
        public void AViewerDoesNotGetHiddenRows()
        {
            _world.Create("core:Spy");
            var me = _world.Create("core:Grunt");
            _world.Seat(me, "me", SeatKind.Human);
            Assert.That(((IntValue)_world.Call("core.secrets").Value).Value, Is.EqualTo(1));
            Assert.That(((IntValue)_world.ViewAs("me").Call("core.secrets").Value).Value, Is.EqualTo(0));
        }

        [Test]
        public void ATrialBoardDoesNotGiveVeiledRows()
        {
            _world.Create("core:Spy");
            var me = _world.Create("core:Grunt");
            _world.Seat(me, "bot", SeatKind.Ai);
            var mind = _world.NewBot("core.peek", me);
            var result = _world.ViewAs("bot").Think(mind);
            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            Assert.That(((ListValue)mind.Stored("Mind", "seen")).Items.Select(v => ((IntValue)v).Value), Is.EqualTo(new long[] { 0 }));
        }

        [Test]
        public void WhatPartsCannotHoldIsRefused()
        {
            var id = _world.Create("core:Spy");
            WorldException Refused(MemberHandle member)
            {
                try
                {
                    _world.Parts("Unit", member);
                }
                catch (WorldException e)
                {
                    return e;
                }
                Assert.Fail("'" + member.Display + "' was accepted");
                return null!;
            }

            Assert.That(Refused(_world.Member("Body", "twice")).Code, Is.EqualTo(Codes.RunType), "derived");
            Assert.That(Refused(_world.Member("Body", "name")).Code, Is.EqualTo(Codes.RunType), "문자열");
            Assert.That(Refused(_world.Member("Body", "owner")).Code, Is.EqualTo(Codes.RunType), "Ref");
            Assert.That(Refused(_world.Member("Bag", "n")).Code, Is.EqualTo(Codes.RunNoRow), "키 컴포넌트");
            Assert.That(Refused(_world.Member("Unit", "kind")).Code, Is.EqualTo(Codes.RunType), "const");

            var left = _world.Member("Stun", "left");
            Assert.That(_world.Parts("Unit", left).MoveNext(), Is.False);
            _world.Add(id, "Stun");
            Assert.That(Refused(left).Code, Is.EqualTo(Codes.RunType), "런타임에 붙은 컴포넌트");
            Assert.Throws<WorldException>(() => _world.WriteColumn(left, new[] { id.Value }, new long[] { 1 }));
            _world.Remove(id, "Stun");
            Assert.That(_world.Parts("Unit", left).MoveNext(), Is.False, "떼면 다시 읽을 수 있다");
        }

        [Test]
        public void MisusedKindsAndHandlesAreRefused()
        {
            _world.Create("core:Grunt");
            var (other, otherHp, _, _) = Make();
            Assert.Throws<ArgumentException>(() => _world.Parts("Unit", otherHp));
            Assert.Throws<WorldException>(() => _world.WriteColumn(_hp, new long[0], new Fixed[0]));
            Assert.Throws<WorldException>(() => _world.WriteColumn(_power, new long[0], new long[0]));

            string? wrongKind = null, notAsked = null;
            foreach (var part in _world.Parts("Unit", _hp, _power))
            {
                try
                {
                    var _ = part.Longs(_power);
                }
                catch (ArgumentException e)
                {
                    wrongKind = e.Message;
                }
                try
                {
                    var _ = part.Longs(_up);
                }
                catch (ArgumentException e)
                {
                    notAsked = e.Message;
                }
            }
            Assert.That(wrongKind, Does.Contain("Fixeds"));
            Assert.That(notAsked, Does.Contain("was not asked for"));
            GC.KeepAlive(other);
        }
    }
}
