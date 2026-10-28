using System.Linq;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>USS 읽기 (G359~G363): 선택자와 우선순위, 값 나무, 진단과 줄 번호, 잘못된 것만 빠짐.</summary>
    public class UssReaderTests
    {
        private static UssSheet Ok(string uss)
        {
            var result = UssReader.Read("test.uss", uss);
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
            return result.Sheet;
        }

        private static string[] Problems(string uss) => UssReader.Read("test.uss", uss).Problems.Select(p => p.ToString()).ToArray();

        private static string Shape(UssSelector s) =>
            string.Join("", s.Compounds.Select(c => string.Concat(c.Parts) + (c.Next == UssCombinator.Child ? " > " : c.Next == UssCombinator.Descendant ? " " : "")));

        [Test]
        public void SelectorsKeepTheirPartsCombinatorsAndSpecificity()
        {
            var sheet = Ok(".card:hover .name, #top > Button.big, * { color: red; }");
            var selectors = sheet.Rules.Single().Selectors;
            Assert.That(selectors.Select(Shape), Is.EqualTo(new[] { ".card:hover .name", "#top > Button.big", "*" }));
            Assert.That(selectors.Select(s => s.Specificity), Is.EqualTo(new[] { 31, 112, 1 }));
            Assert.That(selectors[0].Compounds.Select(c => c.Next), Is.EqualTo(new[] { UssCombinator.Descendant, UssCombinator.None }));
            Assert.That(selectors[1].Compounds[1].Parts.Select(p => p.Kind), Is.EqualTo(new[] { UssSelectorPartKind.Type, UssSelectorPartKind.Class }));
        }

        [Test]
        public void SpacesAroundTheChildCombinatorDoNotMakeItADescendant()
        {
            var a = Ok(".a>.b { color: red; }").Rules[0].Selectors[0];
            var b = Ok(".a  >\n  .b { color: red; }").Rules[0].Selectors[0];
            Assert.That(Shape(a), Is.EqualTo(".a > .b"));
            Assert.That(Shape(b), Is.EqualTo(".a > .b"));
        }

        [Test]
        public void ValuesAreWordsStringsFunctionsAndCommas()
        {
            var d = Ok(".a { transition: opacity 0.2s ease-in, color 1s; background-image: url(\"skirmish/art/x.png\"); color: rgb(10, 20, 30); }")
                .Rules[0].Declarations;
            Assert.That(d.Select(x => x.Name), Is.EqualTo(new[] { "transition", "background-image", "color" }));
            Assert.That(d[0].Values.Select(v => v.ToString()), Is.EqualTo(new[] { "opacity", "0.2s", "ease-in", ",", "color", "1s" }));
            var url = d[1].Values.Single();
            Assert.That((url.Kind, url.Text, url.Arguments.Single().Kind, url.Arguments.Single().Text),
                Is.EqualTo((UssValueKind.Function, "url", UssValueKind.String, "skirmish/art/x.png")));
            Assert.That(d[2].Values.Single().Arguments.Select(v => v.Text), Is.EqualTo(new[] { "10", ",", "20", ",", "30" }));
        }

        [Test]
        public void VariablesAreDeclaredAndUsedWithAFallback()
        {
            var sheet = Ok(":root { --accent: #ff0; } .v { color: var(--accent, rgb(1, 2, 3)); }");
            Assert.That(sheet.Rules[0].Declarations.Single().Name, Is.EqualTo("--accent"));
            var v = sheet.Rules[1].Declarations.Single().Values.Single();
            Assert.That(v.ToString(), Is.EqualTo("var(--accent , rgb(1 , 2 , 3))"));
        }

        [Test]
        public void CommentsAreSkippedAndLinesStillCount()
        {
            var sheet = Ok("/* head\n   more */\n.a {\n  /* x */ color: red;\n  margin: 1px;\n}\n");
            Assert.That(sheet.Rules[0].Line, Is.EqualTo(3));
            Assert.That(sheet.Rules[0].Declarations.Select(d => d.Line), Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void AnUnknownPropertyIsReportedWithAHintAndOnlyThatLineIsDropped()
        {
            var result = UssReader.Read("ui/card.uss", ".a {\n  colr: red;\n  margin: 2px;\n}");
            Assert.That(result.Problems.Select(p => p.ToString()), Is.EqualTo(new[] { "ui/card.uss:2: 'colr' is not a USS property (did you mean 'color'?)" }));
            Assert.That(result.Sheet.Rules.Single().Declarations.Single().Name, Is.EqualTo("margin"));
        }

        [Test]
        public void CustomVariablesAreNotCheckedAgainstTheList()
        {
            Ok(".a { --anything-goes: 3; }");
        }

        [Test]
        public void ABadSelectorDropsItsRuleButTheNextRuleIsRead()
        {
            var result = UssReader.Read("test.uss", ".a + .b { color: red; }\n.c { color: blue; }");
            Assert.That(result.Problems.Single().ToString(), Does.StartWith("test.uss:1: '.a + .b': '+' is not part of USS selectors"));
            Assert.That(result.Sheet.Rules.Single().Selectors.Single().Compounds.Single().Parts.Single().Value, Is.EqualTo("c"));
        }

        [Test]
        public void UnknownPseudoClassesAndPseudoElementsAreReported()
        {
            Assert.That(Problems(".a:hovr { color: red; }").Single(), Does.Contain("':hovr' is not a USS pseudo-class (did you mean 'hover'?)"));
            Assert.That(Problems(".a::before { color: red; }").Single(), Does.Contain("pseudo-elements"));
            Assert.That(Problems("li:nth-child(2) { color: red; }").Single(), Does.Contain("':nth-child(…)' is not part of USS"));
            Assert.That(Problems("[x] { color: red; }").Single(), Does.Contain("'[' is not part of USS selectors"));
        }

        [Test]
        public void ImportAndOtherAtRulesAreReportedAndSkipped()
        {
            var result = UssReader.Read("test.uss", "@import url(\"a.uss\");\n@media screen { .x { color: red; } }\n.a { color: red; }");
            Assert.That(result.Problems.Select(p => p.ToString()), Is.EqualTo(new[]
            {
                "test.uss:1: @import is not supported yet (put each sheet in its own <Style src>)",
                "test.uss:2: '@media' is not part of USS",
            }));
            Assert.That(result.Sheet.Rules.Single().Line, Is.EqualTo(3));
        }

        [Test]
        public void ResourceIsRefusedInFavourOfUrl()
        {
            Assert.That(Problems(".a {\n background-image: resource(\"Icons/x\");\n}").Single(),
                Is.EqualTo("test.uss:2: background-image: resource() points inside the game build; use url(\"<path in the mods>\") in mod USS"));
        }

        [Test]
        public void UrlTakesOnlyModPaths()
        {
            Assert.That(Problems(".a { background-image: url(\"project://database/Assets/Art/x.png?fileID=2800000&guid=ab&type=3#x\"); }").Single(),
                Does.Contain("url(): 'project://database/Assets/Art/x.png?fileID=2800000&guid=ab&type=3#x' is a Unity project path (the form UI Builder writes); write the path inside the mods instead, like 'ui/x.png'"));
            Assert.That(Problems(".a { background-image: url(\"../art/x.png\"); }").Single(), Does.Contain("climbs out with '..'"));
            Assert.That(Problems(".a { background-image: url(\"C:/art/x.png\"); }").Single(), Does.Contain("is a Unity project path"));
            Assert.That(Problems(".a { background-image: url(\"skirmish/art/x.png\"); }"), Is.Empty);
        }

        [Test]
        public void ValueMistakesAreReported()
        {
            Assert.That(Problems(".a { color: red !important; }").Single(), Does.Contain("no !important"));
            Assert.That(Problems(".a { color: ; }").Single(), Does.Contain("the value is empty"));
            Assert.That(Problems(".a { color: rgb(1, 2; }").Single(), Does.Contain("not closed"));
            Assert.That(Problems(".a { color: var(accent); }").Single(), Does.Contain("starts with '--'"));
            Assert.That(Problems(".a { background-image: url(\"a\", \"b\"); }").Single(), Does.Contain("url() takes one path"));
            Assert.That(Problems(".a { color red; }").Single(), Does.Contain("is not 'name: value'"));
        }

        [Test]
        public void UnclosedThingsAreReported()
        {
            Assert.That(Problems(".a { color: red;").Single(), Is.EqualTo("test.uss:1: the rule is not closed ('}')"));
            Assert.That(Problems(".a").Single(), Is.EqualTo("test.uss:1: expected '{' after the selector"));
            Assert.That(Problems(".a { color: red; } /* open").Single(), Is.EqualTo("test.uss:1: the comment is not closed (*/)"));
            Assert.That(Problems("}\n.a { color: red; }").Single(), Is.EqualTo("test.uss:1: '}' without a rule to close"));
        }

        [Test]
        public void BracesInsideStringsDoNotEndTheRule()
        {
            var v = Ok(".a { background-image: url(\"odd}name{.png\"); }").Rules.Single().Declarations.Single().Values.Single();
            Assert.That(v.Arguments.Single().Text, Is.EqualTo("odd}name{.png"));
        }

        [Test]
        public void ThePropertyListMatchesUnitysCount()
        {
            Assert.That(UssProperties.Names.Count, Is.EqualTo(99));
            Assert.That(UssProperties.PseudoClasses, Does.Contain("hover").And.Contain("root"));
        }
    }
}
