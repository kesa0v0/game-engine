using System.Linq;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>해석기 (G330~G345): 올리기, 영수증 갱신, 반복, 눌림, 버튼 확인.</summary>
    public class UiInterpreterTests : UiTestBase
    {
        [Test]
        public async Task ABindingShowsTheFunctionValueAndFollowsTheWorld()
        {
            await Mount("<ui:VisualElement params=\"u\"><ui:Label name=\"hp\" bind-text=\"arena.hp(u)\" text=\"-\" /></ui:VisualElement>",
                ("u", Entity(A)));
            var label = Renderer.Named("hp");
            Assert.That(label.Text, Is.EqualTo("3"));
            Assert.That(label.Attributes["text"], Is.EqualTo("-"), "the static attribute still reaches the renderer");

            SetHp(A, 1);
            await Ui.RefreshAsync();
            Assert.That(label.Text, Is.EqualTo("1"));
        }

        [Test]
        public async Task ARefreshTouchesOnlyWhatChanged()
        {
            await Mount("<ui:VisualElement params=\"a, b\"><ui:Label name=\"a\" bind-text=\"arena.hp(a)\" /><ui:Label name=\"b\" bind-text=\"arena.hp(b)\" /></ui:VisualElement>",
                ("a", Entity(A)), ("b", Entity(B)));
            Renderer.Named("b").Texts["text"] = "untouched";
            SetHp(A, 2);
            await Ui.RefreshAsync();
            Assert.That(Renderer.Named("a").Text, Is.EqualTo("2"));
            Assert.That(Renderer.Named("b").Text, Is.EqualTo("untouched"), "b's receipt did not change, so it was not called again");
        }

        [Test]
        public async Task TwoInterpretersOnOneSessionKeepTheirOwnReceipts()
        {
            const string uxml = "<ui:VisualElement params=\"u\"><ui:Label name=\"x\" bind-text=\"arena.hp(u)\" /></ui:VisualElement>";
            await Mount(uxml, ("u", Entity(A)));
            var otherRenderer = new MemoryRenderer();
            var other = new UiInterpreter(Session, otherRenderer, Format);
            var mounted = await other.MountAsync(Doc(uxml), new System.Collections.Generic.Dictionary<string, BridgeValue> { ["u"] = Entity(B) });
            Assert.That(mounted.Problems, Is.Empty);

            SetHp(A, 1);
            SetHp(B, 2);
            await Ui.RefreshAsync();
            await other.RefreshAsync();
            Assert.That(Renderer.Named("x").Text, Is.EqualTo("1"), "the second interpreter's panel did not take over the first one's receipt");
            Assert.That(otherRenderer.Named("x").Text, Is.EqualTo("2"));
        }

        [Test]
        public async Task ARepeatMakesOneGroupPerItemAndReadsDictionaryKeys()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"list\" for=\"row in arena.units()\"><ui:Label bind-text=\"row.hp\" /></ui:VisualElement></ui:VisualElement>");
            var list = Renderer.Named("list");
            Assert.That(list.Children.Select(c => c.Text), Is.EqualTo(new[] { "3", "3" }));

            SetHp(B, 1);
            await Ui.RefreshAsync();
            Assert.That(list.Children.Select(c => c.Text), Is.EqualTo(new[] { "3", "1" }), "the dict item changed, so its cells were read again");
        }

        [Test]
        public async Task ARepeatKeepsTheElementsOfEntitiesThatStayAndDropsTheRest()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"list\" for=\"u in arena.unit_list()\"><ui:Label bind-text=\"arena.hp(u)\" /></ui:VisualElement></ui:VisualElement>");
            var list = Renderer.Named("list");
            var first = list.Children[0];
            var second = list.Children[1];
            Assert.That(list.Children.Count, Is.EqualTo(2));

            long third = World.Create("arena:Soldier").Value;
            await Ui.RefreshAsync();
            Assert.That(list.Children.Count, Is.EqualTo(3));
            Assert.That(list.Children[0], Is.SameAs(first));
            Assert.That(list.Children[1], Is.SameAs(second));

            World.Destroy(new Kesa.Engine.Runtime.EntityId(A));
            await Ui.RefreshAsync();
            Assert.That(list.Children.Count, Is.EqualTo(2));
            Assert.That(list.Children[0], Is.SameAs(second), "the surviving elements are the same ones");
            Assert.That(first.Removed, Is.True);
            Assert.That(Hp(third), Is.EqualTo(3));
        }

        [Test]
        public async Task AClickRunsTheUiActionAndTheUiRecordChangesTheScreen()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"list\" for=\"u in arena.unit_list()\">" +
                        "<ui:Button on-click=\"arena.select(u)\" bind-class-picked=\"arena.is_selected(u)\" /></ui:VisualElement></ui:VisualElement>");
            var buttons = Renderer.Named("list").Children;
            Assert.That(buttons.Select(b => b.Classes.Contains("picked")), Is.EqualTo(new[] { false, false }));

            await buttons[1].Press();
            Assert.That(buttons.Select(b => b.Classes.Contains("picked")), Is.EqualTo(new[] { false, true }),
                "the world did not change, only the UI record, and the receipt saw it");
        }

        [Test]
        public async Task AButtonIsGrayWhenItsPreconditionsFailAndWakesWhenTheyPass()
        {
            await Mount("<ui:VisualElement params=\"u\"><ui:Button name=\"hit\" on-click=\"arena.Hit(u)\" /></ui:VisualElement>", ("u", Entity(A)));
            var hit = Renderer.Named("hit");
            Assert.That(hit.Enabled, Is.True);

            await hit.Press();
            await hit.Press();
            Assert.That(Hp(A), Is.EqualTo(1));
            Assert.That(hit.Enabled, Is.False, "hp is 1 now, so the require fails");

            SetHp(A, 3);
            await Ui.RefreshAsync();
            Assert.That(hit.Enabled, Is.True, "the world version moved, so the button was checked again");
        }

        [Test]
        public async Task ClassBindingsAddAndRemoveOnlyTheirOwnClasses()
        {
            await Mount("<ui:VisualElement params=\"u\"><ui:Label name=\"l\" class=\"card\" bind-class=\"arena.state(u)\" bind-class-low=\"arena.low(u)\" /></ui:VisualElement>",
                ("u", Entity(A)));
            var label = Renderer.Named("l");
            Assert.That(label.Classes, Is.EquivalentTo(new[] { "card", "ok", "calm" }));

            SetHp(A, 1);
            await Ui.RefreshAsync();
            Assert.That(label.Classes, Is.EquivalentTo(new[] { "card", "danger", "low" }), "the written class stays, the bound ones follow");
        }

        [Test]
        public async Task FlagAndNumberPropertiesConvertTheirValues()
        {
            await Mount("<ui:VisualElement params=\"u\"><ui:ProgressBar name=\"p\" bind-value=\"arena.ratio(u)\" bind-display=\"arena.selected()\" /></ui:VisualElement>",
                ("u", Entity(A)));
            var bar = Renderer.Named("p");
            Assert.That(bar.Numbers["value"], Is.EqualTo(750_000), "3/4 as the raw fixed value");
            Assert.That(bar.Flags["display"], Is.False, "None is false");

            await Session.Act("arena.select", new System.Collections.Generic.Dictionary<string, BridgeValue> { ["u"] = Entity(A) });
            await Ui.RefreshAsync();
            Assert.That(bar.Flags["display"], Is.True, "an entity is true");
        }

        [Test]
        public async Task AFailingBindingKeepsItsPreviousValueAndReportsOnce()
        {
            await Mount("<ui:VisualElement params=\"u\"><ui:Label name=\"l\" bind-text=\"arena.hp(u)\" bind-display=\"arena.boom(u)\" /></ui:VisualElement>",
                ("u", Entity(A)));
            Assert.That(Renderer.Named("l").Text, Is.EqualTo("3"), "the other binding is fine");
            Assert.That(Ui.Errors.Count, Is.EqualTo(1));
            await Ui.RefreshAsync();
            await Ui.RefreshAsync();
            Assert.That(Ui.Errors.Count, Is.EqualTo(1), "the same error is not added again");
            Assert.That(Ui.Errors[0].Message, Does.Contain("bind-display"));
        }

        [Test]
        public async Task WhatCannotBeMountedIsReportedAndNothingIsBuilt()
        {
            var result = await Ui.MountAsync(Doc(
                    "<ui:VisualElement params=\"u\"><ui:Label bind-text=\"arena.nope(u)\" /><ui:Button on-click=\"arena.Hitt(u)\" /></ui:VisualElement>"),
                new System.Collections.Generic.Dictionary<string, BridgeValue> { ["u"] = Entity(A), ["x"] = BridgeNone.Instance });
            Assert.That(result.Panel, Is.Null);
            var text = string.Join("\n", result.Problems);
            Assert.That(text, Does.Contain("bind-text: mod 'arena' has no function 'nope'"));
            Assert.That(text, Does.Contain("'arena.Hitt' is neither a command nor a UI action").And.Contain("did you mean 'arena.Hit'"));
            Assert.That(text, Does.Contain("does not take 'x'"));
            Assert.That(Renderer.Root, Is.Null);
        }

        [Test]
        public async Task AnIncludeTakesNamedArgumentsAndFollowsThemWhenTheyChange()
        {
            Files["UI/Row"] = "<ui:Label params=\"who\" name=\"row\" bind-text=\"arena.hp(who)\" />";
            await Mount("<ui:VisualElement><ui:VisualElement for=\"u in arena.unit_list()\"><ui:Include src=\"UI/Row\" who=\"u\" /></ui:VisualElement></ui:VisualElement>");
            Assert.That(Renderer.Walk(Renderer.Root!).Where(e => e.Kind == "Label").Select(e => e.Text), Is.EqualTo(new[] { "3", "3" }));
            SetHp(B, 2);
            await Ui.RefreshAsync();
            Assert.That(Renderer.Walk(Renderer.Root!).Where(e => e.Kind == "Label").Select(e => e.Text), Is.EqualTo(new[] { "3", "2" }));
        }

        [Test]
        public async Task AnIncludeMissingAnArgumentOrAFileIsAMountProblem()
        {
            Files["UI/Row"] = "<ui:Label params=\"who\" />";
            var missing = await Ui.MountAsync(Doc("<ui:VisualElement><ui:Include src=\"UI/Row\" /></ui:VisualElement>"),
                new System.Collections.Generic.Dictionary<string, BridgeValue>());
            Assert.That(string.Join("\n", missing.Problems), Does.Contain("needs the value 'who'"));
            var lost = await Ui.MountAsync(Doc("<ui:VisualElement><ui:Include src=\"UI/Nope\" /></ui:VisualElement>"),
                new System.Collections.Generic.Dictionary<string, BridgeValue>());
            Assert.That(string.Join("\n", lost.Problems), Does.Contain("was not found"));
        }

        [Test]
        public async Task AReorderedListMovesTheSameElements()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"list\" for=\"u in arena.hp_order()\"><ui:Label bind-text=\"arena.hp(u)\" /></ui:VisualElement></ui:VisualElement>");
            var list = Renderer.Named("list");
            var forA = list.Children[0];
            var forB = list.Children[1];
            Assert.That(list.Children.Select(c => c.Text), Is.EqualTo(new[] { "3", "3" }));

            SetHp(B, 1);
            await Ui.RefreshAsync();
            Assert.That(list.Children[0], Is.SameAs(forB), "the weak unit comes first now, and its element moved with it");
            Assert.That(list.Children[1], Is.SameAs(forA));
            Assert.That(list.Children.Select(c => c.Text), Is.EqualTo(new[] { "1", "3" }));
            Assert.That(Renderer.Log, Does.Contain("move"));
        }

        [Test]
        public async Task EqualValuesAreMatchedInOrderAndTheNewOneGoesBetween()
        {
            await Mount("<ui:VisualElement><ui:VisualElement name=\"list\" for=\"t in arena.tags()\"><ui:Label bind-text=\"t\" /></ui:VisualElement></ui:VisualElement>");
            var list = Renderer.Named("list");
            Assert.That(list.Children.Select(c => c.Text), Is.EqualTo(new[] { "x", "x" }));
            var first = list.Children[0];
            var second = list.Children[1];

            SetHp(A, 5);
            await Ui.RefreshAsync();
            Assert.That(list.Children.Select(c => c.Text), Is.EqualTo(new[] { "x", "y", "x" }));
            Assert.That(list.Children[0], Is.SameAs(first));
            Assert.That(list.Children[2], Is.SameAs(second), "the second 'x' is still the second 'x'");
        }

        [Test]
        public async Task AnIncludeArgumentFromACallFollowsItsReceipt()
        {
            Files["UI/Row"] = "<ui:Label params=\"who\" name=\"row\" bind-text=\"arena.hp(who)\" />";
            await Mount("<ui:VisualElement><ui:Include src=\"UI/Row\" who=\"arena.top()\" /></ui:VisualElement>");
            Assert.That(Renderer.Named("row").Text, Is.EqualTo("3"));
            SetHp(B, 7);
            await Ui.RefreshAsync();
            Assert.That(Renderer.Named("row").Text, Is.EqualTo("7"), "top() is now B, so the inner label reads B");
        }

        [Test]
        public async Task AnActionTheHostGivesDirectlyGraysAndWakesTheButtonsThroughTheInterpreter()
        {
            await Mount("<ui:VisualElement><ui:Button name=\"clear\" on-click=\"arena.clear()\" /></ui:VisualElement>");
            var clear = Renderer.Named("clear");
            Assert.That(clear.Enabled, Is.False, "nothing is chosen");

            await Ui.ActAsync("arena.select", new System.Collections.Generic.Dictionary<string, BridgeValue> { ["u"] = Entity(A) });
            Assert.That(clear.Enabled, Is.True, "the host's map click changed only the UI record");
        }

        [Test]
        public async Task AnActionGivenStraightToTheSessionIsNoticedByTheUiRecordStamp()
        {
            await Mount("<ui:VisualElement><ui:Button name=\"clear\" on-click=\"arena.clear()\" /></ui:VisualElement>");
            var clear = Renderer.Named("clear");
            Assert.That(clear.Enabled, Is.False);
            await Session.Act("arena.select", new System.Collections.Generic.Dictionary<string, BridgeValue> { ["u"] = Entity(A) });
            await Ui.RefreshAsync();
            Assert.That(clear.Enabled, Is.True, "the UI record stamp moved though the world stamp did not");
        }

        [Test]
        public async Task CheckSaysWhetherThisConnectionMayIssueTheCommand()
        {
            var mine = await Session.Check("arena.Rest", new System.Collections.Generic.Dictionary<string, BridgeValue> { ["hero"] = Entity(Red) });
            Assert.That(mine.Outcome, Is.EqualTo("Passed"));
            var theirs = await Session.Check("arena.Rest", new System.Collections.Generic.Dictionary<string, BridgeValue> { ["hero"] = Entity(Blue) });
            Assert.That((theirs.Succeeded, theirs.Outcome), Is.EqualTo((false, "GateDenied")));
            Assert.That(theirs.Reasons.Single().Message, Does.Contain("cannot issue arena.Rest"));
            var wrong = await Session.Check("arena.Rest", new System.Collections.Generic.Dictionary<string, BridgeValue> { ["hero"] = Entity(A) });
            Assert.That(wrong.Outcome, Is.EqualTo("ArgumentsRejected"), "wrong arguments come first");
        }

        [Test]
        public async Task AButtonForAnotherSidesCommandIsGrayFromTheStart()
        {
            await Mount("<ui:VisualElement params=\"mine, theirs\"><ui:Button name=\"mine\" on-click=\"arena.Rest(mine)\" />" +
                        "<ui:Button name=\"theirs\" on-click=\"arena.Rest(theirs)\" /></ui:VisualElement>", ("mine", Entity(Red)), ("theirs", Entity(Blue)));
            Assert.That(Renderer.Named("mine").Enabled, Is.True);
            Assert.That(Renderer.Named("theirs").Enabled, Is.False);
        }

        [Test]
        public async Task StyleSheetsGoToTheTopElementOfTheFileThatNamedThem()
        {
            Files["UI/Row"] = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Style src=\"row.uss\" /><ui:Label params=\"who\" /></ui:UXML>";
            await Mount("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Style src=\"a.uss\" /><ui:Style src=\"b.uss\" /><ui:Style src=\"a.uss\" />" +
                        "<ui:VisualElement name=\"top\" params=\"u\"><ui:Include src=\"UI/Row\" who=\"u\" /></ui:VisualElement></ui:UXML>", ("u", Entity(A)));
            Assert.That(Renderer.Named("top").StyleSheets, Is.EqualTo(new[] { "a.uss", "b.uss" }));
            Assert.That(Renderer.Named("top").Children.Single().StyleSheets, Is.EqualTo(new[] { "row.uss" }));
        }

        [Test]
        public async Task ClearedErrorsAreReportedAgainWhenTheyHappenAgain()
        {
            await Mount("<ui:VisualElement params=\"u\"><ui:Label bind-display=\"arena.boom(u)\" /></ui:VisualElement>", ("u", Entity(A)));
            Assert.That(Ui.Errors.Count, Is.EqualTo(1));
            Ui.ClearErrors();
            Assert.That(Ui.Errors, Is.Empty);
            await Ui.RefreshAsync();
            Assert.That(Ui.Errors.Count, Is.EqualTo(1), "the failing call is tried again on every refresh");
        }

        [Test]
        public async Task UnmountingRemovesTheElementsAndLetsGoOfTheReceipts()
        {
            var panel = await Mount("<ui:VisualElement params=\"u\"><ui:Label name=\"l\" bind-text=\"arena.hp(u)\" /></ui:VisualElement>", ("u", Entity(A)));
            var label = Renderer.Named("l");
            await Ui.UnmountAsync(panel);
            Assert.That(label.Removed, Is.True);
            SetHp(A, 1);
            Assert.That(await Session.Changed(), Is.Empty, "no receipt is left to report");
        }
    }
}
