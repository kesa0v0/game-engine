using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>
    /// 월드 물체를 한꺼번에 받고 바뀐 것만 다시 받는다 (G401·G405·G407). 빠진 것은 사라짐·안 보임·<c>None</c>이고, 칸 함수 오류는 그 물체에 실린다.
    /// </summary>
    public class LocalWorldObjectTests
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0), boarded=field(bool, default=False))\n" +
            "deftype(\"Unit\", components=[\"Pos\"], look=const(Look[\"Unit\"]), where=const(Where[\"Unit\"]))\n" +
            "ui_state(\"Sel\", unit=ref(\"Unit\", optional=True))\n";

        private const string Defs =
            "def drawn(u):\n    return look(\"infantry\", picked=ui.Sel.unit == u)\n" +
            "def spot(u):\n    return None if u.Pos.boarded else at(\"hex\", u.Pos.x)\n" +
            "Unit(\"Foot\", look=drawn, where=spot)\n" +
            "Unit(\"Tree\", look=\"pine\", where=lambda u: at(0))\n" +
            "Unit(\"Odd\", look=lambda u: look(\"odd\", n=1 // u.Pos.x), where=lambda u: at(0))\n" +
            "@view_rule(Unit)\ndef far(viewer, entity):\n    return entity.Pos.x != 99\n" +
            "@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Sel.unit = u\n";

        private string _root = "";
        private World _world = null!;
        private LocalHost _host = null!;
        private IHostSession _alice = null!;
        private EntityId _foot, _tree;

        [SetUp]
        public async Task Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("arena/schema/types.py", Schema);
            Write("arena/defs/defs.py", Defs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _world = new World(content);
            _foot = _world.Create("arena:Foot");
            _tree = _world.Create("arena:Tree");
            _host = new LocalHost(content, _world);
            _alice = await _host.Open("alice");
        }

        [TearDown]
        public void Close()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private void Set(EntityId id, string member, Value value) => _world.Set(id, _world.Member("Pos", member), value);

        [Test]
        public void TheFirstCallGivesEverythingAndTheNextOnlyWhatChanged()
        {
            var first = await_(_alice.WorldObjects(0));
            Assert.That(first.Changed.Select(o => o.Entity.Id), Is.EqualTo(new[] { _foot.Value, _tree.Value }));
            Assert.That(first.Changed[0].Look, Is.EqualTo(new BridgeLook("infantry", new[] { new KeyValuePair<string, BridgeValue>("picked", BridgeValue.Of(false)) })));
            Assert.That(first.Changed[0].Entity.Def, Is.EqualTo("arena:Foot"));
            Assert.That(first.Changed[1].Where, Is.EqualTo(BridgeWhere.At(null, BridgeValue.Of(0))));
            Assert.That(first.Gone, Is.Empty);

            var same = await_(_alice.WorldObjects(first.Stamp));
            Assert.That((same.Changed.Count, same.Gone.Count, same.Stamp), Is.EqualTo((0, 0, first.Stamp)));

            Set(_foot, "x", Int(3));
            var moved = await_(_alice.WorldObjects(first.Stamp));
            Assert.That(moved.Changed.Single().Where, Is.EqualTo(BridgeWhere.At("hex", BridgeValue.Of(3))));

            Set(_tree, "x", Int(5)); // 나무의 자리는 Pos를 읽지 않으므로 그대로다
            Assert.That(await_(_alice.WorldObjects(moved.Stamp)).Changed, Is.Empty);
        }

        [Test]
        public void TheScreenStateChangesALook()
        {
            var first = await_(_alice.WorldObjects(0));
            await_(_alice.Act("arena.pick", new Dictionary<string, BridgeValue> { ["u"] = new BridgeEntity(_foot.Value, null) }));
            var next = await_(_alice.WorldObjects(first.Stamp));
            Assert.That(next.Changed.Single().Look!.Keys.Single().Value, Is.EqualTo(BridgeValue.Of(true)));
        }

        [Test]
        public void NoneHiddenAndDeadAreGoneAndComeBack()
        {
            var first = await_(_alice.WorldObjects(0));
            Set(_foot, "boarded", BoolValue.True);
            Set(_tree, "x", Int(99));
            var gone = await_(_alice.WorldObjects(first.Stamp));
            Assert.That((gone.Changed.Count, gone.Gone), Is.EqualTo((0, new[] { _foot.Value, _tree.Value })));

            Set(_foot, "boarded", BoolValue.False);
            var back = await_(_alice.WorldObjects(gone.Stamp));
            Assert.That((back.Changed.Single().Entity.Id, back.Gone.Count), Is.EqualTo((_foot.Value, 0)));

            _world.Destroy(_foot);
            Assert.That(await_(_alice.WorldObjects(back.Stamp)).Gone, Is.EqualTo(new[] { _foot.Value }));
            var fresh = await_(_alice.WorldObjects(0));
            Assert.That((fresh.Changed.Count, fresh.Gone.Count), Is.EqualTo((0, 0)), "from the start: nothing to draw and nothing to take away");
        }

        [Test]
        public void AFailingSlotIsCarriedOnItsObject()
        {
            var odd = _world.Create("arena:Odd");
            var all = await_(_alice.WorldObjects(0));
            var failed = all.Changed.Single(o => o.Entity.Id == odd.Value);
            Assert.That((failed.Look, failed.Where, failed.Entity.Def), Is.EqualTo(((BridgeLook?)null, (BridgeWhere?)null, "arena:Odd")));
            Assert.That(failed.Error, Does.Contain("arena:Odd.look"));
            Assert.That(await_(_alice.WorldObjects(all.Stamp)).Changed, Is.Empty, "the same failure is not news");

            Set(odd, "x", Int(1));
            var fixedNow = await_(_alice.WorldObjects(all.Stamp)).Changed.Single();
            Assert.That((fixedNow.Error, fixedNow.Look!.Name), Is.EqualTo(((string?)null, "odd")));
        }

        [Test]
        public void OneObjectKeepsAReceiptUnderItsKey()
        {
            var one = await_(_alice.WorldObject("tip", _foot.Value));
            Assert.That(one!.Look!.Name, Is.EqualTo("infantry"));
            Assert.That(await_(_alice.Changed()), Is.Empty);
            Set(_foot, "x", Int(2));
            Assert.That(await_(_alice.Changed()), Is.EqualTo(new[] { "tip" }));
            Set(_foot, "boarded", BoolValue.True);
            Assert.That(await_(_alice.WorldObject("tip", _foot.Value)), Is.Null);
        }

        private static IntValue Int(long n) => new IntValue(n);

        private static T await_<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();
    }
}
