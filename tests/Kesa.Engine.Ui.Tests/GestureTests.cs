using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>모양 종류 <c>path</c>·<c>lasso</c> (G482·G483)와 <c>.input.xml</c>의 <c>kind</c> (G489).</summary>
    public class GestureTests
    {
        private static SpaceLayout Grid()
        {
            var problems = new List<string>();
            var layout = new GridSpace().Configure(new Dictionary<string, string>(), problems);
            Assert.That(problems, Is.Empty);
            return layout!;
        }

        private static BridgeValue C(long x, long y) => new BridgeList(new BridgeValue[] { new BridgeInt(x), new BridgeInt(y) });

        private static string Show(BridgeValue value) => BridgeJson.Write(value);

        private static GestureStroke Draw(GestureKind kind, SpaceLayout space, params BridgeValue[] places)
        {
            var shape = kind.Configure(new Dictionary<string, string>(), new List<string>())!;
            var stroke = shape.Begin(space, places[0], out string error);
            Assert.That(stroke, Is.Not.Null, error);
            foreach (var place in places.Skip(1)) stroke!.Add(place);
            return stroke!;
        }

        [Test]
        public void APathKeepsThePlacesInOrderAndFillsTheCellsAFrameSkipped()
        {
            var stroke = Draw(new PathGesture(), Grid(), C(0, 0), C(0, 0), C(3, 1), C(3, 1), C(3, 0));
            Assert.That(Show(stroke.Value), Is.EqualTo("[[0,0],[1,0],[2,1],[3,1],[3,0]]"));
        }

        [Test]
        public void APathThatCrossesItselfKeepsTheRepeats()
        {
            var stroke = Draw(new PathGesture(), Grid(), C(0, 0), C(1, 0), C(0, 0));
            Assert.That(Show(stroke.Value), Is.EqualTo("[[0,0],[1,0],[0,0]]"));
            Assert.That(stroke.Add(C(0, 0)), Is.False, "the same place as the last one adds nothing");
        }

        [Test]
        public void ALassoFillsWhatTheRingEncloses()
        {
            var square = Draw(new LassoGesture(), Grid(), C(0, 0), C(4, 0), C(4, 4), C(0, 4), C(0, 1));
            Assert.That(((BridgeList)square.Value).Items.Count, Is.EqualTo(25));
            Assert.That(Show(((BridgeList)square.Value).Items[0]), Is.EqualTo("[0,0]"), "rows from the top, cells from the left");
        }

        [Test]
        public void AnOpenLassoIsClosedStraightBackToItsStart()
        {
            var triangle = Draw(new LassoGesture(), Grid(), C(0, 0), C(4, 0), C(4, 4));
            var cells = ((BridgeList)triangle.Value).Items.Select(Show).ToList();
            Assert.That(cells.Count, Is.EqualTo(15), "x >= y on a 5x5 board");
            Assert.That(cells, Does.Contain("[2,2]"), "the closing line is part of the ring");
            Assert.That(cells, Does.Not.Contain("[0,1]"));
        }

        [Test]
        public void ALassoDrawnTwiceAroundStillFillsItsInside()
        {
            var twice = Draw(new LassoGesture(), Grid(), C(0, 0), C(2, 0), C(2, 2), C(0, 2), C(0, 0), C(2, 0), C(2, 2), C(0, 2));
            Assert.That(((BridgeList)twice.Value).Items.Count, Is.EqualTo(9));
        }

        private sealed class Open : SpaceLayout
        {
            public override long Unit => BridgeFixed.Scale;

            public override bool TryPlace(BridgeValue place, out LookPosition position, out string error)
            {
                position = default;
                error = "";
                return true;
            }
        }

        [Test]
        public void ALassoNeedsASpaceOfCellsButAPathDoesNot()
        {
            var lasso = new LassoGesture().Configure(new Dictionary<string, string>(), new List<string>())!;
            Assert.That(lasso.Begin(new Open(), new BridgeInt(3), out string error), Is.Null);
            Assert.That(error, Does.Contain("needs a space of cells"));
            var path = Draw(new PathGesture(), new Open(), new BridgeInt(3), new BridgeInt(9));
            Assert.That(Show(path.Value), Is.EqualTo("[3,9]"), "no cells to fill between");
        }

        private static InputLibrary Inputs(string text) =>
            InputLibrary.Load(new[] { new LookSource("coup", "coup/keys.input.xml", "<Inputs>\n" + text + "\n</Inputs>") }, new[] { "R" });

        [Test]
        public void KindPicksTheShapeOfAPointerInput()
        {
            var library = Inputs(
                "<Input name=\"paint\" key=\"primary\" kind=\"lasso\" />\n" +
                "<Input name=\"trace\" key=\"SHIFT + primary\" kind=\"path\" />\n" +
                "<Input name=\"turn\" key=\"R\" />");
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            Assert.That((library.Inputs["paint"].Kind, library.Inputs["trace"].Kind, library.Inputs["turn"].Kind), Is.EqualTo(("lasso", "path", (string?)null)));
            Assert.That(library.IsPointer("trace"), Is.True);
            Assert.That(library.IsPointer("turn"), Is.False);
        }

        [Test]
        public void AKindNeedsAPointerKeyAKnownNameAndKnownSettings()
        {
            var library = Inputs(
                "<Input name=\"a\" key=\"R\" kind=\"path\" />\n" +
                "<Input name=\"b\" key=\"primary\" kind=\"laso\" />\n" +
                "<Input name=\"c\" key=\"primary\" kind=\"lasso\" width=\"2\" />");
            var problems = library.Problems.Select(p => p.ToString()).ToList();
            Assert.That(problems, Has.Some.Contain("its key ends with primary or secondary, got key=\"R\""));
            Assert.That(problems, Has.Some.Contain("no input kind is registered with this name (did you mean 'lasso'?)"));
            Assert.That(problems, Has.Some.Contain("width: the input kind 'lasso' has no settings"));
            Assert.That(library.Inputs, Is.Empty);
        }

        [Test]
        public void ADrawnInputsHandlerTakesASpace()
        {
            var library = Inputs("<Input name=\"paint\" key=\"primary\" kind=\"lasso\" />");
            var problems = library.CheckHandlers(new[]
            {
                new BridgeWorldInput("paint", "board", "coup.paint", "coup"),
                new BridgeWorldInput("paint", "Unit", "coup.grab", "coup"),
                new BridgeWorldInput("primary", "Unit", "coup.pick", "coup"),
            }, new[] { "board" });
            Assert.That(problems.Select(p => p.ToString()), Is.EqualTo(new[]
            {
                "coup.grab: the input 'paint' is drawn (kind=\"lasso\"), so its handler takes a space, and no space is named 'Unit' " +
                "(@world_input(\"paint\", \"Unit\")) (spec-gaps G484)",
            }));
        }
    }
}
