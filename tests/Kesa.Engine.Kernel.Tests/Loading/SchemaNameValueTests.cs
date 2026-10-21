using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 엔진이 넘기는 컴포넌트는 이름 문자열이 아니라 컴포넌트 값이다 (spec-gaps G306·G308): 보기 정책의 <c>component</c>,
    /// 기여 줄의 <c>c.component</c>. 맨 이름과 비교하고, 문자열 리터럴과 비교하면 경고한다.
    /// </summary>
    public class SchemaNameValueTests : LoaderTestBase
    {
        private const string Schema =
            "def total(h):\n    return h.base + floor(sum([c.value for c in contribs(h, \"max\") if c.component == Armor]))\n" +
            "component(\"Face\", n=field(int, default=0))\n" +
            "component(\"Health\", base=const(int, default=10), max=derived(int, total))\n" +
            "component(\"Armor\", bonus=const(list[Contrib], default=[contrib(\"Health.max\", \"add\", 1)]))\n" +
            "deftype(\"Card\", components=[\"Face\", \"Health\", \"Armor\"])\n";

        private static string Policy(string body) =>
            "@view_policy\ndef may_see(viewer, entity, component):\n" + body;

        [Test]
        public void ComparingWithBareNamesIsClean()
        {
            Mod("banking", schema: Schema, defs: Policy("    return component != Face\n"));
            LoadClean();
        }

        [TestCase("    return component != \"Face\"\n", "Face")]
        [TestCase("    return \"Face\" == component\n", "Face")]
        public void ComparingAPolicyComponentWithAStringWarns(string body, string name)
        {
            Mod("banking", schema: Schema, defs: Policy(body));
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlSchemaNameString));
            Assert.That(d.Severity, Is.EqualTo(Severity.Warning));
            Assert.That(d.Message, Is.EqualTo("a component or schema name here is a value, not a string, so it never equals \"" + name + "\""));
            Assert.That(d.Hint, Is.EqualTo("write " + name + " without quotes (spec-gaps G308)"));
            Assert.That(result.Disabled, Is.Empty);
        }

        [Test]
        public void ComparingAContributionComponentWithAStringWarns()
        {
            Mod("banking", schema: Schema.Replace("c.component == Armor", "c.component == \"Armor\""));
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.EvlSchemaNameString }));
        }

        [Test]
        public void AMisspelledComponentNameIsCaughtByTheNameCheck()
        {
            Mod("banking", schema: Schema, defs: Policy("    return component != Fcae\n"));
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Hint, Does.Contain("'Face'"));
        }

        [Test]
        public void AContributionHasSixAttributes()
        {
            Mod("banking", schema: Schema.Replace("c.value for c", "c.valu for c"));
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Message, Is.EqualTo("a contribution has no 'valu'"));
            Assert.That(d.Hint, Does.Contain("'value'"));
        }

        [Test]
        public void ADefTypeNameHasNoAttributesInLogic()
        {
            Mod("banking", schema: Schema + "def f():\n    return Card.n\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Message, Is.EqualTo("'Card' here is the Def type, which has no attribute 'n'"));
        }
    }
}
