using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>
    /// 판의 포인터 (G485~G490): 포인터 단추의 누름은 뗄 때 나고, 문턱을 넘으면 누름 대신 끌기다. 끌기는 그리는 동안 <c>drag()</c>로 읽히고
    /// 끝나면 공간 핸들러가 값 하나로 받는다. 커서는 <c>cursor()</c>로 읽힌다. 진짜 <see cref="LocalHost"/>로 돌린다.
    /// </summary>
    public class LookPointerTests
    {
        private const string Defs =
            "def hover():\n    return cursor(\"board\")\n" +
            "def painting():\n    return drag(\"paint\")\n" +
            "def state():\n    return [ui.Sel.picked, ui.Sel.painted, ui.Sel.traced]\n" +
            "@world_marks\ndef preview():\n" +
            "    cells = drag(\"paint\")\n" +
            "    return [] if cells is None else [mark(\"wash\", at=at(\"board\", c)) for c in cells]\n" +
            "@world_input(\"primary\", \"board\")\n@ui_action\ndef pick(cell: list[int]):\n    ui.Sel.picked = cell[0] * 10 + cell[1] + 1\n" +
            "@world_input(\"paint\", \"board\")\n@ui_action\ndef paint(cells: list[list[int]]):\n    ui.Sel.painted = len(cells)\n" +
            "@world_input(\"trace\", \"board\")\n@ui_action\ndef trace(cells: list[list[int]]):\n    ui.Sel.traced = len(cells)\n";

        private string _root = "";
        private IHostSession _alice = null!;
        private LookBoard _board = null!;
        private MemoryLookRenderer _renderer = null!;

        [SetUp]
        public async Task Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", System.Environment.ProcessId + "-" + TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("coup/schema/types.py",
                "ui_state(\"Sel\", picked=field(int, default=0), painted=field(int, default=0), traced=field(int, default=0))\n");
            Write("coup/defs/defs.py", Defs);
            Write("coup/mod.py", "Mod(id=\"coup\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _alice = await new LocalHost(content, new World(content)).Open("alice");

            var sources = new[]
            {
                new LookSource("coup", "coup/board.space.xml", "<Spaces><Space name=\"board\" kind=\"grid\" /></Spaces>"),
                new LookSource("coup", "coup/wash.look.xml", "<Looks><Look name=\"wash\"><Sprite src=\"w.png\" /></Look></Looks>"),
                new LookSource("coup", "coup/keys.input.xml",
                    "<Inputs><Input name=\"paint\" key=\"primary\" kind=\"lasso\" /><Input name=\"trace\" key=\"SHIFT + primary\" kind=\"path\" /></Inputs>"),
            };
            var library = LookLibrary.Load(sources, LookKinds.Standard(), _ => true);
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            var inputs = InputLibrary.Load(sources, new string[0]);
            Assert.That(inputs.Problems, Is.Empty, string.Join("\n", inputs.Problems));
            Assert.That(inputs.CheckHandlers(await _alice.WorldInputs(), library.Spaces.Keys), Is.Empty);
            _renderer = new MemoryLookRenderer();
            _board = new LookBoard(_alice, new LookStage(library, _renderer, inputs));
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

        private static BridgeWhere At(long x, long y) => BridgeWhere.At("board", new BridgeList(new BridgeValue[] { new BridgeInt(x), new BridgeInt(y) }));

        private async Task<string> Read(string function) => BridgeJson.Write((await _alice.Call(function, "coup." + function, new BridgeValue[0])).Value!);

        private int Washes => _board.Stage.Keys.Count(k => k.StartsWith("mark:"));

        [Test]
        public async Task APressIsSentWhenTheButtonComesUpAndNotBefore()
        {
            await _board.PointAsync(At(1, 2));
            var pressed = await _board.PressedAsync(new[] { LookBoard.Primary, "paint" }, new string[0], At(1, 2));
            Assert.That(pressed, Is.Empty);
            Assert.That(await Read("state"), Is.EqualTo("[0,0,0]"), "nothing yet");

            var released = await _board.ReleaseAsync();
            Assert.That(released.Single().Succeeded, Is.True, released.Single().ToString());
            Assert.That(await Read("state"), Is.EqualTo("[13,0,0]"), "the press reached the cell; a drawn input is not a press");
        }

        [Test]
        public async Task ADragPastTheThresholdDrawsALassoAndHandsItOverWhenReleased()
        {
            await _board.PointAsync(At(0, 0));
            await _board.PressedAsync(new[] { LookBoard.Primary, "paint" }, new string[0], At(0, 0));
            await _board.DragAsync();
            Assert.That((_board.Pressing, _board.Dragging), Is.EqualTo((false, true)));
            foreach (var (x, y) in new[] { (2, 0), (2, 2), (0, 2) }) await _board.PointAsync(At(x, y));

            Assert.That(await Read("painting"), Does.StartWith("[[0,0],[1,0],[2,0],[0,1]"), "the filled lasso so far");
            await _board.RefreshAsync();
            Assert.That(Washes, Is.EqualTo(9), "the preview marks read drag()");

            var released = await _board.ReleaseAsync();
            Assert.That(released.Single().Succeeded, Is.True, released.Single().ToString());
            Assert.That(await Read("state"), Is.EqualTo("[0,9,0]"), "the lasso arrived and the press did not");
            Assert.That(await Read("painting"), Is.EqualTo("null"));
            await _board.RefreshAsync();
            Assert.That(Washes, Is.EqualTo(0));
        }

        [Test]
        public async Task TheInputIsTheOnePressedEvenIfTheModifierChangesLater()
        {
            await _board.PressedAsync(new[] { "trace" }, new string[0], At(0, 0));
            await _board.DragAsync();
            await _board.PointAsync(At(3, 0));
            await _board.ReleaseAsync();
            Assert.That(await Read("state"), Is.EqualTo("[0,0,4]"), "a path of four cells went to the SHIFT input");
        }

        [Test]
        public async Task PlacesOutsideTheStartingSpaceAreLeftOut()
        {
            await _board.PressedAsync(new[] { "trace" }, new string[0], At(0, 0));
            await _board.DragAsync();
            await _board.PointAsync(BridgeWhere.At("minimap", new BridgeInt(4)));
            await _board.PointAsync(null);
            await _board.PointAsync(At(1, 0));
            await _board.ReleaseAsync();
            Assert.That(await Read("state"), Is.EqualTo("[0,0,2]"));
        }

        [Test]
        public async Task ACancelledDragHandsNothingOver()
        {
            await _board.PressedAsync(new[] { "paint" }, new string[0], At(0, 0));
            await _board.DragAsync();
            await _board.PointAsync(At(2, 2));
            await _board.CancelPointerAsync();
            Assert.That(await Read("painting"), Is.EqualTo("null"));
            Assert.That(await _board.ReleaseAsync(), Is.Empty);
            Assert.That(await Read("state"), Is.EqualTo("[0,0,0]"));
        }

        [Test]
        public async Task TheCursorIsReadPerSpaceAndGoesWhenItLeaves()
        {
            await _board.PointAsync(At(3, 3));
            Assert.That(await Read("hover"), Is.EqualTo("[3,3]"));
            await _board.PointAsync(BridgeWhere.At("minimap", new BridgeInt(4)));
            Assert.That(await Read("hover"), Is.EqualTo("null"), "over another space");
            await _board.PointAsync(At(1, 1));
            await _board.PointAsync(null);
            Assert.That(await Read("hover"), Is.EqualTo("null"), "off the board");
        }
    }
}
