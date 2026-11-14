using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>
    /// 커서와 끌기 (G487·G488·G490): 호스트가 키마다 알린 값을 <c>cursor()</c>·<c>drag()</c>가 읽고, 그 키를 읽은 영수증만 바뀐다.
    /// 끝난 끌기는 엔티티 없는 <c>WorldInput</c>으로 공간 핸들러가 값 하나로 받는다 (G483·G484).
    /// </summary>
    public class LocalPointerTests
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Pos\"], look=const(Look[\"Unit\"]), where=const(Where[\"Unit\"]))\n" +
            "ui_state(\"Sel\", n=field(int, default=0))\n";

        private const string Defs =
            "Unit(\"Foot\", look=lambda u: look(\"foot\", hot=cursor(\"board\") == [u.Pos.x, 0]), where=lambda u: at(\"board\", [u.Pos.x, 0]))\n" +
            "def hover():\n    return cursor(\"board\")\n" +
            "def painting():\n    return drag(\"paint\")\n" +
            "def picked():\n    return ui.Sel.n\n" +
            "@world_marks\ndef preview():\n" +
            "    cells = drag(\"paint\")\n" +
            "    return [] if cells is None else [mark(\"wash\", at=at(\"board\", c)) for c in cells]\n" +
            "@world_input(\"paint\", \"board\")\n@ui_action\ndef paint(cells: list[list[int]]):\n    ui.Sel.n = len(cells)\n";

        private string _root = "";
        private World _world = null!;
        private IHostSession _alice = null!;

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
            _world.Create("arena:Foot");
            _alice = await new LocalHost(content, _world).Open("alice");
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

        private static BridgeList Cell(long x, long y) => new BridgeList(new BridgeValue[] { new BridgeInt(x), new BridgeInt(y) });

        private static BridgeList Cells(params BridgeList[] cells) => new BridgeList(cells);

        [Test]
        public async Task WithoutAPointerBothReadNone()
        {
            Assert.That((await _alice.Call("h", "arena.hover", new BridgeValue[0])).Value, Is.EqualTo(BridgeNone.Instance));
            Assert.That((await _alice.Call("d", "arena.painting", new BridgeValue[0])).Value, Is.EqualTo(BridgeNone.Instance));
        }

        [Test]
        public async Task OnlyAReceiptThatReadTheKeyChanges()
        {
            await _alice.Call("h", "arena.hover", new BridgeValue[0]);
            await _alice.Call("d", "arena.painting", new BridgeValue[0]);
            await _alice.Call("p", "arena.picked", new BridgeValue[0]);
            Assert.That(await _alice.Changed(), Is.Empty);

            await _alice.Cursor("minimap", Cell(1, 1));
            Assert.That(await _alice.Changed(), Is.Empty, "another space");

            await _alice.Cursor("board", Cell(2, 3));
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "h" }));
            Assert.That((await _alice.Call("h", "arena.hover", new BridgeValue[0])).Value, Is.EqualTo(Cell(2, 3)));

            await _alice.Cursor("board", Cell(2, 3));
            Assert.That(await _alice.Changed(), Is.Empty, "the same cell again");

            await _alice.Drag("paint", Cells(Cell(0, 0)));
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "d" }));

            await _alice.Cursor("board", null);
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "d", "h" }));
            Assert.That((await _alice.Call("h", "arena.hover", new BridgeValue[0])).Value, Is.EqualTo(BridgeNone.Instance));
        }

        [Test]
        public async Task TheDragPreviewFollowsTheDragAndGoesWhenItEnds()
        {
            var first = await _alice.WorldMarks(0);
            Assert.That(first.Changed, Is.Empty);

            await _alice.Drag("paint", Cells(Cell(0, 0), Cell(1, 0)));
            var drawing = await _alice.WorldMarks(first.Stamp);
            Assert.That(drawing.Changed.Select(m => BridgeJson.Write(m.Where!)),
                Is.EquivalentTo(new[] { "{\"at\":\"board\",\"value\":[0,0]}", "{\"at\":\"board\",\"value\":[1,0]}" }));

            await _alice.Drag("paint", null);
            var ended = await _alice.WorldMarks(drawing.Stamp);
            Assert.That(ended.Gone.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task AWorldObjectThatReadsTheCursorIsRedrawnWhenOnlyTheCursorMoves()
        {
            var first = await _alice.WorldObjects(0);
            Assert.That(BridgeJson.Write(first.Changed.Single().Look!), Does.Contain("\"hot\":false"));

            await _alice.Cursor("board", Cell(0, 0));
            var hot = await _alice.WorldObjects(first.Stamp);
            Assert.That(BridgeJson.Write(hot.Changed.Single().Look!), Does.Contain("\"hot\":true"));
        }

        [Test]
        public async Task AFinishedDragReachesTheSpaceHandlerAsOneValue()
        {
            var result = await _alice.WorldInput("paint", new long[0], BridgeWhere.At("board", Cells(Cell(0, 0), Cell(1, 0), Cell(1, 1))));
            Assert.That(result!.Succeeded, Is.True, result.ToString());
            Assert.That(((BridgeInt)(await _alice.Call("p", "arena.picked", new BridgeValue[0])).Value!).Value, Is.EqualTo(3));
        }
    }

    /// <summary><c>cursor()</c>·<c>drag()</c>는 <c>ui.…</c>처럼 화면 쪽에서만 읽는다 (G488).</summary>
    public class PointerPlaceTests
    {
        private string _root = "";

        [SetUp]
        public void Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [TearDown]
        public void Close()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private IReadOnlyList<Diagnostic> Load(string defs)
        {
            void Write(string relative, string text)
            {
                var path = Path.Combine(_root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }

            Write("arena/schema/types.py", "component(\"Pos\", x=field(int, default=0))\ndeftype(\"Unit\", components=[\"Pos\"])\n");
            Write("arena/defs/defs.py", defs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            return bag.ToSortedList();
        }

        [Test]
        public void GameCodeCannotReadTheCursor()
        {
            var diags = Load("def near():\n    return cursor(\"board\")\n@command\ndef Go():\n    x = near()\n");
            Assert.That(diags.Select(d => d.Message), Has.Some.Contains("'Go' uses UI state"));
        }

        [Test]
        public void ABotCannotReadADrag()
        {
            var diags = Load("@bot\ndef think():\n    d = drag(\"paint\")\n");
            Assert.That(diags.Select(d => d.Message), Has.Some.Contains("a bot has no screen"));
        }
    }
}
