using System.Linq;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>보는 법 <c>.view.xml</c> 읽기 (G441·G442·G445·G446).</summary>
    public class ViewTests
    {
        private const string Flipped = "<Spaces><Space name=\"square\" kind=\"grid\" flip-y=\"true\" /></Spaces>";
        private const long One = BridgeFixed.Scale;

        private static LookLibrary Load(string views, string spaces = Flipped) => LookLibrary.Load(new[]
        {
            new LookSource("core", "core/board.space.xml", spaces),
            new LookSource("core", "core/board.view.xml", "<Views>" + views + "</Views>"),
        }, LookKinds.Standard());

        private static string[] Problems(string views, string spaces = Flipped) =>
            Load(views, spaces).Problems.Select(p => p.Message).ToArray();

        [Test]
        public void LookAtIsAPlaceOfTheSpaceAndOffsetPushesItAlongThatPlacesFrame()
        {
            var library = Load("<View name=\"main\" rig=\"top\" space=\"square\" look-at=\"2,1\" offset=\"0,-0.5\" size=\"4\" zoom=\"2,8\" />");
            Assert.That(library.Problems, Is.Empty);
            var view = library.Views["main"];
            // flip-y라 1번 줄은 y = -1이고, 남쪽(화면 아래)으로 반 칸 민다
            Assert.That(view.Target, Is.EqualTo(new LookPosition(2 * One, -3 * One / 2, 0)));
            Assert.That(view.Frame, Is.EqualTo(LookFrame.Screen));
            Assert.That(view.Rig, Is.EqualTo("top"));
            Assert.That(view.Number("size", 0), Is.EqualTo(4 * One));
            Assert.That(view.Settings["zoom"].Numbers, Is.EqualTo(new[] { 2 * One, 8 * One }));
            Assert.That(view.Number("turn", -1), Is.EqualTo(-1));

            // 3D 바닥에서 위는 높이다
            var floor = Load("<View name=\"main\" rig=\"tilted\" space=\"square\" look-at=\"1,1\" offset=\"0.5,0,2\" tilt=\"50\" distance=\"10\" />",
                "<Spaces><Space name=\"square\" kind=\"grid\" plane=\"xz\" /></Spaces>");
            Assert.That(floor.Problems, Is.Empty);
            Assert.That(floor.Views["main"].Target, Is.EqualTo(new LookPosition(3 * One / 2, 2 * One, One)));
        }

        [Test]
        public void ViewsAreCheckedAgainstTheirRig()
        {
            Assert.That(Problems("<View name=\"main\" rig=\"topp\" space=\"square\" look-at=\"0,0\" size=\"4\" />").Single(),
                Does.StartWith("rig=\"topp\": no camera rig is registered with this name (did you mean 'top'?)"));
            Assert.That(Problems("<View name=\"main\" rig=\"top\" space=\"square\" look-at=\"0,0\" size=\"4\" tilt=\"50\" />").Single(),
                Does.StartWith("tilt: the rig 'top' has no such setting"));
            Assert.That(Problems("<View name=\"main\" rig=\"tilted\" space=\"square\" look-at=\"0,0\" />"), Is.EqualTo(new[]
            {
                "the rig 'tilted' needs distance, like distance=\"10\"",
                "the rig 'tilted' needs tilt, like tilt=\"50\"",
            }));
            Assert.That(Problems("<View name=\"main\" rig=\"top\" size=\"4\" />"), Is.EqualTo(new[]
            {
                "<View> needs space, like space=\"square\"",
                "<View> needs look-at, like look-at=\"2,2\"",
            }));
            Assert.That(Problems("<View name=\"main\" rig=\"tilted\" space=\"square\" look-at=\"0,0\" tilt=\"95\" distance=\"0\" zoom=\"8,2\" fov=\"180\" />"),
                Is.EqualTo(new[]
                {
                    "tilt=\"95\": a tilt is from 0 (straight down) to 90 (level)",
                    "distance=\"0\": a distance is more than 0",
                    "zoom=\"8,2\": a zoom is \"least,most\", both more than 0 and least first",
                    "fov=\"180\": a fov is more than 0 and less than 180 degrees",
                }));
        }

        [Test]
        public void TheSpaceAndPlaceAreLookedUpAfterEveryModIsRead()
        {
            Assert.That(Problems("<View name=\"main\" rig=\"top\" space=\"squar\" look-at=\"0,0\" size=\"4\" />").Single(),
                Is.EqualTo("space=\"squar\": no space is named 'squar' (did you mean 'square'?)"));
            Assert.That(Problems("<View name=\"main\" rig=\"top\" space=\"square\" look-at=\"0.5,0\" size=\"4\" />").Single(),
                Does.StartWith("look-at: a grid place is a list of whole numbers"));
            Assert.That(Problems("<View name=\"main\" rig=\"top\" space=\"square\" look-at=\"north\" size=\"4\" />").Single(),
                Does.StartWith("look-at: a grid place is a list of 2 or 3 whole numbers"));
            Assert.That(Problems("<View name=\"main\" rig=\"top\" space=\"square\" look-at=\"1,x\" size=\"4\" />").Single(),
                Does.StartWith("look-at=\"1,x\": 'x' is not a number"));

            // 뒤 모드가 같은 이름의 공간을 바꾸면 그 공간으로 본다
            var library = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/board.space.xml", Flipped),
                new LookSource("core", "core/board.view.xml", "<Views><View name=\"main\" rig=\"top\" space=\"square\" look-at=\"0,1\" size=\"4\" /></Views>"),
                new LookSource("deep", "deep/board.space.xml", "<Spaces><Space name=\"square\" kind=\"grid\" plane=\"xz\" /></Spaces>"),
            }, LookKinds.Standard());
            Assert.That(library.Problems, Is.Empty);
            Assert.That(library.Views["main"].Target, Is.EqualTo(new LookPosition(0, 0, One)));
            Assert.That(library.Views["main"].Frame, Is.EqualTo(LookFrame.Floor));
        }

        [Test]
        public void TheSameNameInALaterModWinsAndInTheSameModIsAProblem()
        {
            const string top = "<View name=\"main\" rig=\"top\" space=\"square\" look-at=\"0,0\" size=\"4\" />";
            var library = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/board.space.xml", Flipped),
                new LookSource("core", "core/board.view.xml", "<Views>" + top + top + "</Views>"),
                new LookSource("deep", "deep/board.view.xml",
                    "<Views><View name=\"main\" rig=\"tilted\" space=\"square\" look-at=\"0,0\" tilt=\"50\" distance=\"10\" /></Views>"),
            }, LookKinds.Standard());
            Assert.That(library.Problems.Single().ToString(), Is.EqualTo("core/board.view.xml:1: the view 'main' is already defined at core/board.view.xml:1"));
            Assert.That(library.Views["main"].Rig, Is.EqualTo("tilted"));
        }
    }
}
