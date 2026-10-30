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
    /// 표시를 한꺼번에 받고 바뀐 것만 다시 받는다 (G395·G396·G408). 열쇠는 그림 이름+자리 또는 <c>key=</c>이고, 겹치면 순번이 붙는다.
    /// 표시 함수 오류는 그 함수의 표시 하나로 실린다.
    /// </summary>
    public class LocalWorldMarkTests
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0), r=field(int, default=1))\n" +
            "deftype(\"Unit\", components=[\"Pos\"])\n";

        private const string Defs =
            "Unit(\"Foot\")\n" +
            "@ui_action\ndef go(u: Ref[Unit], to: int):\n    pass\n" +
            "@world_marks\ndef moves():\n" +
            "    return [mark(\"spot\", at=at(\"hex\", u.Pos.x + 1), click=[\"arena.go\", {\"u\": u, \"to\": u.Pos.x + 1}],\n" +
            "                 inputs={\"secondary\": [\"arena.go\", {\"u\": u, \"to\": 0}]}) for u in query(Unit)]\n" +
            "@world_marks\ndef rings():\n" +
            "    return [mark(look(\"ring\", r=u.Pos.r), at=at(\"hex\", u.Pos.x), key=\"ring\") for u in query(Unit)]\n" +
            "@world_marks\ndef twins():\n" +
            "    return [mark(\"dot\", at=at(0)), mark(\"dot\", at=at(0))]\n" +
            "@world_marks\ndef fragile():\n" +
            "    return [mark(\"x\", at=at(1 // u.Pos.r)) for u in query(Unit)]\n";

        private string _root = "";
        private World _world = null!;
        private LocalHost _host = null!;
        private IHostSession _alice = null!;
        private EntityId _foot;

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

        private void Set(string member, long value) => _world.Set(_foot, _world.Member("Pos", member), new IntValue(value));

        private static BridgeMark Of(BridgeWorldMarks marks, string function) => marks.Changed.Single(m => m.Key.StartsWith(function + "/"));

        [Test]
        public void TheFirstCallGivesEverythingAndTheNextOnlyWhatChanged()
        {
            var first = await_(_alice.WorldMarks(0));
            Assert.That(first.Changed.Select(m => m.Key), Is.EquivalentTo(new[]
            {
                "arena.moves/spot@{\"at\":\"hex\",\"value\":1}", "arena.rings/key=\"ring\"", "arena.twins/dot@{\"at\":null,\"value\":0}",
                "arena.twins/dot@{\"at\":null,\"value\":0}#1", "arena.fragile/x@{\"at\":null,\"value\":1}",
            }));
            var spot = Of(first, "arena.moves");
            Assert.That(spot.Click!.Action, Is.EqualTo("arena.go"));
            Assert.That(spot.Click.Arguments["to"], Is.EqualTo(BridgeValue.Of(1)));
            Assert.That(spot.Click.Arguments["u"], Is.EqualTo(new BridgeEntity(_foot.Value, "arena:Foot")));
            Assert.That(Of(first, "arena.rings").Click, Is.Null);
            var secondary = spot.Inputs.Single();
            Assert.That((secondary.Key, secondary.Value.Action, secondary.Value.Arguments["to"]),
                Is.EqualTo(("secondary", "arena.go", BridgeValue.Of(0))));
            Assert.That(Of(first, "arena.rings").Inputs, Is.Empty);

            var same = await_(_alice.WorldMarks(first.Stamp));
            Assert.That((same.Changed.Count, same.Gone.Count, same.Stamp), Is.EqualTo((0, 0, first.Stamp)));
        }

        [Test]
        public void AMarkWithoutAKeyThatMovesIsANewMark()
        {
            var first = await_(_alice.WorldMarks(0));
            string old = Of(first, "arena.moves").Key;
            Set("x", 3);
            var moved = await_(_alice.WorldMarks(first.Stamp));
            Assert.That(moved.Gone, Does.Contain(old));
            Assert.That(Of(moved, "arena.moves").Where, Is.EqualTo(BridgeWhere.At("hex", BridgeValue.Of(4))));
            // key=가 있는 고리는 같은 열쇠로 옮겨 간다
            Assert.That(Of(moved, "arena.rings").Key, Is.EqualTo("arena.rings/key=\"ring\""));
            Assert.That(moved.Gone, Has.None.StartsWith("arena.rings/"));
        }

        [Test]
        public void ChangedKeysOfALookKeepTheMark()
        {
            var first = await_(_alice.WorldMarks(0));
            Set("r", 2);
            var next = await_(_alice.WorldMarks(first.Stamp));
            var ring = Of(next, "arena.rings");
            Assert.That(ring.Look!.Keys.Single().Value, Is.EqualTo(BridgeValue.Of(2)));
            Assert.That(next.Gone, Has.None.StartsWith("arena.rings/"));
        }

        [Test]
        public void AFailingFunctionIsCarriedAsOneMark()
        {
            var first = await_(_alice.WorldMarks(0));
            string old = Of(first, "arena.fragile").Key;
            Set("r", 0);
            var failed = await_(_alice.WorldMarks(first.Stamp));
            var error = failed.Changed.Single(m => m.Key == "arena.fragile");
            Assert.That((error.Look, error.Where), Is.EqualTo(((BridgeLook?)null, (BridgeWhere?)null)));
            Assert.That(error.Error, Does.Contain("arena.fragile"));
            Assert.That(failed.Gone, Is.EqualTo(new[] { old }));
            Assert.That(await_(_alice.WorldMarks(failed.Stamp)).Changed, Is.Empty, "the same failure is not news");

            Set("r", 1);
            var back = await_(_alice.WorldMarks(failed.Stamp));
            Assert.That(back.Gone, Is.EqualTo(new[] { "arena.fragile" }));
            Assert.That(Of(back, "arena.fragile").Key, Is.EqualTo(old));
        }

        [Test]
        public void TheClickIsAnAction()
        {
            var spot = Of(await_(_alice.WorldMarks(0)), "arena.moves");
            var result = await_(_alice.Act(spot.Click!.Action, spot.Click.Arguments));
            Assert.That(result.Succeeded, Is.True);
        }

        private static T await_<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();
    }
}
