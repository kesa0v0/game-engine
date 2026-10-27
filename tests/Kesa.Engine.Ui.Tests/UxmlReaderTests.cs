using System.Linq;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>UXML 읽기 (G330~G345): 속성 분류, 변수 선언 검사, 오류에 줄 번호.</summary>
    public class UxmlReaderTests
    {
        private static UiDocument Ok(string uxml)
        {
            var result = UxmlReader.Read("test.uxml", uxml);
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
            return result.Document!;
        }

        private static string[] Problems(string uxml) => UxmlReader.Read("test.uxml", uxml).Problems.Select(p => p.ToString()).ToArray();

        [Test]
        public void TheTopElementDeclaresParamsAndSeat()
        {
            var doc = Ok("<ui:VisualElement params=\"house, extra\" seat=\"house\"><ui:Label name=\"a\" text=\"hi\" /></ui:VisualElement>");
            Assert.That(doc.Parameters, Is.EqualTo(new[] { "house", "extra" }));
            Assert.That(doc.SeatParameter, Is.EqualTo("house"));
            Assert.That(doc.Root.Kind, Is.EqualTo("VisualElement"));
            Assert.That(doc.Root.Children.Single().Attributes.Select(a => (a.Key, a.Value)), Is.EqualTo(new[] { ("name", "a"), ("text", "hi") }));
        }

        [Test]
        public void ASingleChildInsideTheUxmlWrapperIsTheTopElement()
        {
            var doc = Ok("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement params=\"u\"><ui:Label bind-text=\"m.f(u)\" /></ui:VisualElement></ui:UXML>");
            Assert.That(doc.Root.Kind, Is.EqualTo("VisualElement"));
            Assert.That(doc.Parameters, Is.EqualTo(new[] { "u" }));
            Assert.That(doc.Root.Children.Single().Bindings.Single().Name, Is.EqualTo("text"));
        }

        [Test]
        public void SeveralTopElementsAreWrappedInAContainer()
        {
            var doc = Ok("<ui:Label text=\"a\" /><ui:Label text=\"b\" />");
            Assert.That(doc.Root.Kind, Is.EqualTo("VisualElement"));
            Assert.That(doc.Root.Children.Count, Is.EqualTo(2));
        }

        [Test]
        public void BindingsEventsAndRepeatsAreSortedOutFromStaticAttributes()
        {
            var doc = Ok(
                "<ui:VisualElement params=\"house\">" +
                "<ui:VisualElement for=\"row in m.units(house)\" class=\"card\">" +
                "<ui:Label bind-text=\"row.name\" bind-display=\"m.alive(row.unit)\" bind-class-danger=\"m.low(row.unit)\" bind-class=\"m.state(row.unit)\" />" +
                "<ui:Button on-click=\"m.select(house, row.unit)\" text=\"pick\" />" +
                "</ui:VisualElement></ui:VisualElement>");
            var list = doc.Root.Children.Single();
            Assert.That(list.Repeat!.Variable, Is.EqualTo("row"));
            Assert.That(((CallExpr)list.Repeat.Source).Function, Is.EqualTo("m.units"));
            Assert.That(list.Attributes.Select(a => a.Key), Is.EqualTo(new[] { "class" }));

            var label = list.Children[0];
            Assert.That(label.Bindings.Select(b => (b.Kind, b.Name)), Is.EqualTo(new[]
            {
                (UiBindKind.Property, "text"), (UiBindKind.Property, "display"), (UiBindKind.ClassToggle, "danger"), (UiBindKind.ClassNames, ""),
            }));
            Assert.That(label.Bindings[0].Expression, Is.InstanceOf<VarExpr>());

            var button = list.Children[1];
            Assert.That(button.Events.Single().Name, Is.EqualTo("click"));
            Assert.That(button.Events.Single().Call.Function, Is.EqualTo("m.select"));
            Assert.That(button.Attributes.Single().Key, Is.EqualTo("text"));
        }

        [Test]
        public void AClassNameMayContainDashes()
        {
            var doc = Ok("<ui:Label params=\"u\" bind-class-hp-low=\"m.low(u)\" />");
            Assert.That(doc.Root.Bindings.Single().Name, Is.EqualTo("hp-low"));
        }

        [Test]
        public void AnIncludeKeepsItsSourceAndNamedArguments()
        {
            var doc = Ok("<ui:VisualElement params=\"house\"><ui:Include src=\"UI/Row\" unit=\"house\" /></ui:VisualElement>");
            var include = doc.Root.Children.Single().Include!;
            Assert.That(include.Source, Is.EqualTo("UI/Row"));
            Assert.That(include.Arguments.Single().Key, Is.EqualTo("unit"));
        }

        [Test]
        public void AnUndeclaredVariableIsAnErrorWithItsLine()
        {
            var problems = Problems("<ui:VisualElement params=\"house\">\n<ui:Label bind-text=\"m.f(hous)\" />\n</ui:VisualElement>");
            Assert.That(problems.Single(), Does.StartWith("test.uxml:2: ").And.Contain("'hous' is not declared").And.Contain("did you mean 'house'"));
        }

        [Test]
        public void ARepeatVariableIsOnlyVisibleInsideIt()
        {
            var problems = Problems("<ui:VisualElement><ui:VisualElement for=\"u in m.list()\"><ui:Label bind-text=\"m.f(u)\" /></ui:VisualElement>" +
                                    "<ui:Label bind-text=\"m.f(u)\" /></ui:VisualElement>");
            Assert.That(problems.Single(), Does.Contain("'u' is not declared"));
        }

        [Test]
        public void ARepeatSeesTheOuterVariables()
        {
            Ok("<ui:VisualElement params=\"house\"><ui:VisualElement for=\"u in m.list(house)\"><ui:VisualElement for=\"t in m.targets(u, house)\">" +
               "<ui:Button on-click=\"m.hit(u, t)\" /></ui:VisualElement></ui:VisualElement></ui:VisualElement>");
        }

        [Test]
        public void WrongAttributesAreExplained()
        {
            Assert.That(Problems("<ui:Label bind-txt=\"m.f()\" />").Single(), Does.Contain("'txt' is not a property").And.Contain("did you mean 'text'"));
            Assert.That(Problems("<ui:Button on-clik=\"m.f()\" />").Single(), Does.Contain("'clik' is not an event").And.Contain("did you mean 'click'"));
            Assert.That(Problems("<ui:Button on-click=\"u\" params=\"u\" />").Single(), Does.Contain("must be a call"));
            Assert.That(Problems("<ui:Label bind-text=\"m.f(\" />").Single(), Does.Contain("bind-text"));
            Assert.That(Problems("<ui:VisualElement for=\"nonsense\" />").Single(), Does.Contain("variable in mymod.function"));
            Assert.That(Problems("<ui:VisualElement params=\"a\"><ui:Label params=\"b\" /></ui:VisualElement>").Single(),
                Does.Contain("only be on the top element"));
            Assert.That(Problems("<ui:VisualElement params=\"a\" seat=\"b\" />").Single(), Does.Contain("seat: 'b' is not one of the params"));
            Assert.That(Problems("<ui:Include />").Single(), Does.Contain("needs a src"));
            Assert.That(Problems("<ui:Label").Single(), Does.Contain("not valid XML"));
        }

        [Test]
        public void EveryProblemIsReportedAtOnce()
        {
            var problems = Problems("<ui:VisualElement>\n<ui:Label bind-text=\"m.f(x)\" />\n<ui:Label bind-txt=\"m.f()\" />\n</ui:VisualElement>");
            Assert.That(problems.Length, Is.EqualTo(2));
            Assert.That(problems[0], Does.StartWith("test.uxml:2"));
            Assert.That(problems[1], Does.StartWith("test.uxml:3"));
        }
    }
}
