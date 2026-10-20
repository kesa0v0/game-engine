using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary><c>check</c>의 번역 검사 (spec-gaps G257·G262·G268·G273).</summary>
    public class TranslationCheckTests : TextTestBase
    {
        private const string Schema =
            "component(\"Hull\", hp=field(int, default=1))\n" +
            "deftype(\"Ship\", components=[\"Hull\"])\n";

        private IReadOnlyList<Diagnostic> Check(string defs, string en, string? ko = null)
        {
            Mod("harbor", schema: Schema, defs: "Ship(\"Sloop\")\n" + defs);
            Ftl("harbor", "en", en);
            if (ko != null) Ftl("harbor", "ko", ko);
            var content = LoadContent();
            var bag = new DiagnosticBag();
            var texts = Translations.Load(content, bag);
            TranslationCheck.Run(texts, content, bag);
            return bag.ToSortedList();
        }

        private static IEnumerable<string> Codes(IReadOnlyList<Diagnostic> diags) => diags.Select(d => d.Code);

        [Test]
        public void AnUnknownParticleIsAnError()
        {
            var diags = Check("", "harbor-a = { JOSA($x, \"을\") } { JOSA($x, \"에게\") }\n");
            Assert.That(diags.Select(d => (d.Code, d.Severity, d.Line)), Is.EqualTo(new[] { ("LOC006", Severity.Error, 1) }));
            Assert.That(diags[0].Message, Does.Contain("'에게'"));
        }

        [Test]
        public void DefAndEngineKeysAreReserved()
        {
            var diags = Check("",
                "def-harbor-Sloop = sloop\n" +
                "def-harbor-Nope = nope\n" +
                "engine-hidden = ???\n" +
                "engine-mine = mine\n");
            Assert.That(diags.Select(d => (d.Code, d.Line)), Is.EqualTo(new[] { ("LOC007", 2), ("LOC007", 4) }));
        }

        [Test]
        public void ScriptTextsMustExistAndGetTheVariablesTheirTextsUse()
        {
            var diags = Check(
                "def greet(k):\n" +
                "    a = text(\"harbor-hello\", n=1)\n" +
                "    b = text(\"harbor-none\")\n" +
                "    c = text(k)\n" +
                "    d = text(\"harbor-hello.tip\", who=1)\n" +
                "    return [a, b, c, d]\n" +
                "def shadowed(text):\n" +
                "    return text(\"harbor-also-none\")\n",
                "harbor-hello = { $who } has { $n }\n    .tip = { $who }\n");
            Assert.That(diags.Select(d => (d.Code, d.Line)), Is.EqualTo(new[] { ("LOC009", 3), ("LOC008", 4) }));
            Assert.That(diags[0].Message, Is.EqualTo("text(\"harbor-hello\") does not pass $who, which the en text uses"));
            Assert.That(diags[0].Mod, Is.EqualTo("harbor"));
        }

        [Test]
        public void AnotherLanguageMayNotUseVariablesTheDefaultLanguageDoesNot()
        {
            var diags = Check("", "harbor-a = { $n } gold\n", "harbor-a = { $n } 금화 { $extra }\n");
            Assert.That(Codes(diags), Is.EqualTo(new[] { "LOC010" }));
            Assert.That(diags[0].Message, Does.Contain("$extra"));
        }

        [Test]
        public void UntranslatedTextsAreCountedPerModAndLanguage()
        {
            var diags = Check("", "harbor-a = a\nharbor-b = b\nharbor-c = c\n", "harbor-a = 가\n");
            Assert.That(diags.Select(d => (d.Code, d.Severity, d.Message)),
                Is.EqualTo(new[] { ("LOC011", Severity.Info, "mod 'harbor' has 2 of 3 texts untranslated in ko") }));
        }

        [Test]
        public void ACleanModHasNothingToReport()
        {
            var diags = Check("def greet():\n    return text(\"harbor-hello\", n=1)\n",
                "harbor-hello = { JOSA($n, \"이\") } { $n ->\n    [one] one\n   *[other] many\n}\n",
                "harbor-hello = { $n }\n");
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }
    }
}
