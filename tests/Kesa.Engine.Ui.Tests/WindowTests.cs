using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>창 (G348~G354): 창 속성 읽기, 보임과 겹침, 모달 막이, ESC, 누름, 바깥 클릭, 끄는 손잡이, 해석기끼리 나누는 창 목록.</summary>
    public class WindowTests : UiTestBase
    {
        private const string Info =
            "<ui:VisualElement name=\"info\" window=\"normal\" bind-display=\"arena.selected()\" on-close=\"arena.clear()\" />";

        private const string Confirm =
            "<ui:VisualElement name=\"confirm\" window=\"modal\" bind-display=\"arena.asking()\" on-close=\"arena.answer()\" />";

        private static Dictionary<string, BridgeValue> Args(params (string, BridgeValue)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

        private async Task Select(long unit) => await Ui.ActAsync("arena.select", Args(("u", Entity(unit))));

        private List<string> ShownNames() => Renderer.Shown.Select(e => e.Attributes["name"]).ToList();

        [Test]
        public void TheReaderTakesTheWindowAttributes()
        {
            var root = Doc("<ui:VisualElement window=\"modal\" close-on-outside=\"true\" window-raise=\"false\" on-close=\"arena.answer()\">" +
                           "<ui:Label window-drag=\"true\" /></ui:VisualElement>").Root;
            Assert.That(root.Window!.Kind, Is.EqualTo(UiWindowKind.Modal));
            Assert.That(root.Window.CloseOnEscape, Is.True, "on-close makes ESC close it by default");
            Assert.That(root.Window.CloseOnOutside, Is.True);
            Assert.That(root.Window.Raise, Is.False);
            Assert.That(root.Attributes, Is.Empty, "window attributes are not passed to the renderer as fixed attributes");
            Assert.That(root.Children[0].DragHandle, Is.True);

            var plain = Doc("<ui:VisualElement window=\"normal\" />").Root.Window!;
            Assert.That(plain.CloseOnEscape, Is.False, "nothing to call");
            Assert.That(plain.CloseOnOutside, Is.False);
            Assert.That(plain.Raise, Is.True);
        }

        [TestCase("<ui:VisualElement window=\"modl\" />", "a window is normal or modal (did you mean 'modal'?)")]
        [TestCase("<ui:VisualElement on-close=\"arena.clear()\" />", "on-close is only for a window")]
        [TestCase("<ui:VisualElement window-raise=\"false\" />", "window-raise is only for a window")]
        [TestCase("<ui:VisualElement window=\"normal\" close-on-escape=\"true\" />", "close-on-escape=\"true\" needs an on-close to call")]
        [TestCase("<ui:VisualElement window=\"normal\" close-on-outside=\"yes\" />", "close-on-outside=\"yes\": must be true or false")]
        [TestCase("<ui:VisualElement><Include src=\"x.uxml\" window=\"normal\" /></ui:VisualElement>", "window cannot be on an Include")]
        public void TheReaderRejectsWrongWindowAttributes(string uxml, string message)
        {
            var result = UxmlReader.Read("test.uxml", uxml);
            Assert.That(result.Problems.Select(p => p.Message), Has.Some.Contains(message), string.Join("\n", result.Problems));
        }

        [Test]
        public async Task AWindowIsShownOnlyWhileItAndEverythingAboveItIsDisplayed()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"box\" bind-display=\"arena.asking()\">" + Info + "</ui:VisualElement></ui:VisualElement>");
            var info = Renderer.Named("info");
            Assert.That(Renderer.Windows, Does.Contain(info.Id));
            Assert.That(Renderer.Shown, Is.Empty);

            await Select(A);
            Assert.That(Renderer.Shown, Is.Empty, "the box holding the window is hidden");
            await Ui.ActAsync("arena.ask", Args());
            Assert.That(ShownNames(), Is.EqualTo(new[] { "info" }));
            Assert.That(info.Enabled, Is.True, "on-close is not a click, so it never greys the window");
        }

        [Test]
        public async Task AModalStaysAboveANormalWindowOpenedAfterItAndEscapeClosesTheModalFirst()
        {
            await Mount("<ui:VisualElement>" + Info + Confirm + "</ui:VisualElement>");
            await Ui.ActAsync("arena.ask", Args());
            await Select(A);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "info", "confirm" }), "the info window opened later but the modal stays on top");
            Assert.That(Renderer.BlockerAt, Is.EqualTo(1), "the blocker sits just under the modal");

            Assert.That(await Ui.Windows.Escape(), Is.True);
            Assert.That(Renderer.Named("confirm").Flags["display"], Is.False, "ESC ran the modal's on-close, which closed it through the UI record");
            Assert.That(ShownNames(), Is.EqualTo(new[] { "info" }));
            Assert.That(Renderer.BlockerAt, Is.EqualTo(-1));

            Assert.That(await Ui.Windows.Escape(), Is.True);
            Assert.That(Renderer.Shown, Is.Empty);
            Assert.That(await Ui.Windows.Escape(), Is.False, "no window took it, so the game may open its own menu");
        }

        [Test]
        public async Task EscapeSkipsANormalWindowThatDoesNotCloseOnEscapeButStopsAtSuchAModal()
        {
            await Mount("<ui:VisualElement>" + Info +
                        "<ui:VisualElement name=\"pinned\" window=\"normal\" bind-display=\"arena.title()\" on-close=\"arena.answer()\" close-on-escape=\"false\" />" +
                        "</ui:VisualElement>");
            await Select(A);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "pinned", "info" }));
            await Renderer.PressAt(Renderer.Named("pinned"));
            Assert.That(ShownNames(), Is.EqualTo(new[] { "info", "pinned" }), "pressing raises it");
            Assert.That(await Ui.Windows.Escape(), Is.True);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "pinned" }), "the pinned window is skipped and the info window under it closes");

            await Mount("<ui:VisualElement name=\"stuck\" window=\"modal\" bind-display=\"arena.title()\" />");
            await Select(B);
            Assert.That(await Ui.Windows.Escape(), Is.False, "a modal that ESC cannot close blocks ESC");
            Assert.That(ShownNames(), Does.Contain("info"));
        }

        [Test]
        public async Task PressingRaisesUnlessTheWindowSaysNoOrAModalIsUp()
        {
            await Mount("<ui:VisualElement>" +
                        "<ui:VisualElement name=\"one\" window=\"normal\" bind-display=\"arena.title()\" />" +
                        "<ui:VisualElement name=\"two\" window=\"normal\" bind-display=\"arena.title()\" window-raise=\"false\" />" +
                        Info + Confirm + "</ui:VisualElement>");
            await Select(A);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "one", "two", "info" }));

            await Renderer.PressAt(Renderer.Named("one"));
            Assert.That(ShownNames(), Is.EqualTo(new[] { "two", "info", "one" }));
            await Renderer.PressAt(Renderer.Named("two"));
            Assert.That(ShownNames(), Is.EqualTo(new[] { "two", "info", "one" }), "window-raise=\"false\"");

            await Select(B);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "two", "info", "one" }), "a refresh that sets display to true again does not raise the window");

            await Ui.ActAsync("arena.ask", Args());
            await Renderer.PressAt(Renderer.Named("info"));
            Assert.That(ShownNames(), Is.EqualTo(new[] { "two", "info", "one", "confirm" }), "behind a modal nothing is raised");
            Assert.That(Renderer.Named("confirm").Flags["display"], Is.True, "the modal does not close on an outside press by default");
        }

        [Test]
        public async Task CloseOnOutsideCallsOnCloseForAPressOutsideTheWindowOrOnTheBlocker()
        {
            await Mount("<ui:VisualElement>" +
                        "<ui:VisualElement name=\"info\" window=\"normal\" bind-display=\"arena.selected()\" on-close=\"arena.clear()\" close-on-outside=\"true\" />" +
                        "<ui:VisualElement name=\"other\" window=\"normal\" bind-display=\"arena.title()\" />" +
                        "<ui:VisualElement name=\"confirm\" window=\"modal\" bind-display=\"arena.asking()\" on-close=\"arena.answer()\" close-on-outside=\"true\" />" +
                        "</ui:VisualElement>");
            await Select(A);
            await Renderer.PressAt(Renderer.Named("info"));
            Assert.That(ShownNames(), Does.Contain("info"), "a press inside is not outside");
            await Renderer.PressAt(Renderer.Named("other"));
            Assert.That(ShownNames(), Is.EqualTo(new[] { "other" }), "a press on another window is outside");

            await Ui.ActAsync("arena.ask", Args());
            await Renderer.PressAt(Renderer.Named("confirm"));
            Assert.That(ShownNames(), Does.Contain("confirm"));
            await Renderer.PressAt(null);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "other" }), "a press on the blocker closes the modal");
        }

        [Test]
        public async Task ADragHandleMustBeInsideAWindowEvenAcrossAnInclude()
        {
            Files["bar.uxml"] = "<ui:VisualElement name=\"bar\" window-drag=\"true\" />";
            await Mount("<ui:VisualElement name=\"w\" window=\"normal\"><Include src=\"bar.uxml\" /></ui:VisualElement>");
            Assert.That(Renderer.DragHandles, Does.Contain(Renderer.Named("bar").Id));

            var outside = await Ui.MountAsync(Doc("<ui:VisualElement><Include src=\"bar.uxml\" /></ui:VisualElement>"), Args());
            Assert.That(outside.Panel, Is.Null);
            Assert.That(outside.Problems.Single().Message, Does.StartWith("window-drag is only inside a window"));
        }

        [Test]
        public async Task AWindowCannotBeInsideAnotherWindowEvenAcrossAnInclude()
        {
            Files["popup.uxml"] = "<ui:VisualElement name=\"popup\" window=\"normal\" />";
            var nested = await Ui.MountAsync(Doc("<ui:VisualElement window=\"modal\"><Include src=\"popup.uxml\" /></ui:VisualElement>"), Args());
            Assert.That(nested.Panel, Is.Null);
            Assert.That(nested.Problems.Single().Message, Does.StartWith("a window cannot be inside another window"));
            Assert.That(Renderer.Windows, Is.Empty, "nothing was built");

            await Mount("<ui:VisualElement><ui:VisualElement window=\"normal\" /><Include src=\"popup.uxml\" /></ui:VisualElement>");
            Assert.That(Renderer.Windows.Count, Is.EqualTo(2), "side by side is fine");
        }

        [Test]
        public async Task InterpretersSharingTheWindowListShareTheModalAndEscape()
        {
            await Mount("<ui:VisualElement>" + Info + "</ui:VisualElement>");
            var popupRenderer = new MemoryRenderer();
            var popup = new UiInterpreter(Session, popupRenderer, Format, null, Ui.Windows);
            await popup.MountAsync(Doc("<ui:VisualElement>" + Confirm + "</ui:VisualElement>"), Args());

            await Select(A);
            await popup.ActAsync("arena.ask", Args());
            await Ui.RefreshAsync();
            Assert.That(ShownNames(), Is.EqualTo(new[] { "info" }));
            Assert.That(Renderer.BlockerAt, Is.EqualTo(1), "the modal belongs to the other renderer, so the blocker covers all of this one");
            Assert.That(popupRenderer.BlockerAt, Is.EqualTo(0));

            Assert.That(await Ui.Windows.Escape(), Is.True);
            await popup.RefreshAsync();
            Assert.That(popupRenderer.Shown, Is.Empty, "ESC went to the top window, which the other interpreter owns");
            Assert.That(Renderer.BlockerAt, Is.EqualTo(-1));
        }

        [Test]
        public async Task InterpretersOnOneRendererGetOneWindowListEvenWhenNoneIsGiven()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"one\" window=\"normal\" bind-display=\"arena.title()\" /></ui:VisualElement>");
            var second = new UiInterpreter(Session, Renderer, Format);
            await second.MountAsync(Doc("<ui:VisualElement><ui:VisualElement name=\"two\" window=\"normal\" bind-display=\"arena.title()\" /></ui:VisualElement>"), Args());
            Assert.That(second.Windows, Is.SameAs(Ui.Windows));
            Assert.That(ShownNames(), Is.EqualTo(new[] { "one", "two" }), "neither list hides the other's windows");

            var other = new UiWindows();
            Assert.That(() => new UiInterpreter(Session, Renderer, Format, null, other), Throws.InvalidOperationException);
        }

        [Test]
        public async Task UnmountingAPanelTakesItsWindowsOffTheList()
        {
            var panel = await Mount("<ui:VisualElement>" + Info + "</ui:VisualElement>");
            await Select(A);
            Assert.That(ShownNames(), Is.EqualTo(new[] { "info" }));
            await Ui.UnmountAsync(panel);
            Assert.That(Renderer.Shown, Is.Empty);
            Assert.That(await Ui.Windows.Escape(), Is.False);
        }
    }
}
