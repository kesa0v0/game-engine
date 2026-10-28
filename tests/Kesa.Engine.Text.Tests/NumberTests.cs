using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary>번역문의 <c>NUMBER</c> (spec-gaps G334·G356~G358): 옵션, 0에서 먼 쪽 반올림, 언어별 기호 글.</summary>
    public class NumberTests : TextTestBase
    {
        private const string English =
            "n-plain = { NUMBER($n) }\n" +
            "n-whole = { NUMBER($n, maximumFractionDigits: 0) }\n" +
            "n-two = { NUMBER($n, minimumFractionDigits: 2) }\n" +
            "n-one = { NUMBER($n, maximumFractionDigits: 1) }\n" +
            "n-flat = { NUMBER($n, useGrouping: \"false\") }\n" +
            "n-percent = { NUMBER($n, style: \"percent\") }\n" +
            "n-percent-one = { NUMBER($n, style: \"percent\", maximumFractionDigits: 1) }\n" +
            "n-always = { NUMBER($n, signDisplay: \"always\") }\n" +
            "n-except = { NUMBER($n, signDisplay: \"exceptZero\") }\n" +
            "n-twice = { NUMBER(NUMBER($n, maximumFractionDigits: 0), minimumFractionDigits: 1) }\n" +
            "n-plural = { NUMBER($n, maximumFractionDigits: 0) ->\n    [one] one ship\n   *[other] ships\n}\n";

        private const string German =
            "engine-number-decimal = ,\n" +
            "engine-number-group = .\n" +
            "engine-number-percent = { $n } %\n" +
            "n-plain = { NUMBER($n) }\n" +
            "n-percent-one = { NUMBER($n, style: \"percent\", maximumFractionDigits: 1) }\n";

        private const string French =
            "engine-number-decimal = ,\n" +
            "engine-number-group = {\" \"}\n" +
            "n-plain = { NUMBER($n) }\n";

        private List<string> _errors = null!;

        private Translator Load(string language)
        {
            Mod("harbor");
            Ftl("harbor", "en", English);
            Ftl("harbor", "de", German);
            Ftl("harbor", "fr", French);
            var texts = LoadTextsClean();
            _errors = new List<string>();
            return texts.For(new[] { language }, _errors.Add);
        }

        private static BridgeValue Fixed(string text)
        {
            bool negative = text.StartsWith("-");
            string[] parts = text.TrimStart('-').Split('.');
            long raw = long.Parse(parts[0]) * BridgeFixed.Scale + (parts.Length > 1 ? long.Parse(parts[1].PadRight(6, '0')) : 0);
            return new BridgeFixed(negative ? -raw : raw);
        }

        private string Say(Translator tr, string key, BridgeValue n)
        {
            string text = tr.Text(key, ("n", n));
            Assert.That(_errors, Is.Empty, string.Join("\n", _errors));
            return text;
        }

        [Test]
        public void ByDefaultThreeDecimalsAtMostAndThousandsAreGrouped()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-plain", Fixed("12.345678")), Is.EqualTo("12.346"));
            Assert.That(Say(tr, "n-plain", Fixed("2.5")), Is.EqualTo("2.5"));
            Assert.That(Say(tr, "n-plain", new BridgeInt(1234567)), Is.EqualTo("1,234,567"));
            Assert.That(Say(tr, "n-plain", new BridgeInt(-1234)), Is.EqualTo("-1,234"));
            Assert.That(Say(tr, "n-plain", new BridgeInt(123)), Is.EqualTo("123"));
            Assert.That(Say(tr, "n-flat", new BridgeInt(1234567)), Is.EqualTo("1234567"));
        }

        [Test]
        public void IntegersBeyondDoublePrecisionStayExact()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-plain", new BridgeInt(9007199254740993)), Is.EqualTo("9,007,199,254,740,993"));
        }

        [Test]
        public void HalvesRoundAwayFromZeroAndANegativeThatRoundsToZeroShowsZero()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-whole", Fixed("2.5")), Is.EqualTo("3"));
            Assert.That(Say(tr, "n-whole", Fixed("3.5")), Is.EqualTo("4"));
            Assert.That(Say(tr, "n-whole", Fixed("-2.5")), Is.EqualTo("-3"));
            Assert.That(Say(tr, "n-whole", Fixed("2.499999")), Is.EqualTo("2"));
            Assert.That(Say(tr, "n-whole", Fixed("-0.4")), Is.EqualTo("0"));
            Assert.That(Say(tr, "n-one", Fixed("0.25")), Is.EqualTo("0.3"));
            Assert.That(Say(tr, "n-one", Fixed("9.96")), Is.EqualTo("10"));
        }

        [Test]
        public void MinimumFractionDigitsPadsWithZeros()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-two", new BridgeInt(3)), Is.EqualTo("3.00"));
            Assert.That(Say(tr, "n-two", Fixed("1.5")), Is.EqualTo("1.50"));
            Assert.That(Say(tr, "n-two", Fixed("1.23456")), Is.EqualTo("1.235"), "the default maximum stays 3");
        }

        [Test]
        public void PercentMovesTheDecimalPointAndRoundsToWholeByDefault()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-percent", Fixed("0.125")), Is.EqualTo("13%"));
            Assert.That(Say(tr, "n-percent-one", Fixed("0.125")), Is.EqualTo("12.5%"));
            Assert.That(Say(tr, "n-percent", Fixed("-0.5")), Is.EqualTo("-50%"));
        }

        [Test]
        public void SignDisplayAddsAPlus()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-always", new BridgeInt(3)), Is.EqualTo("+3"));
            Assert.That(Say(tr, "n-always", new BridgeInt(0)), Is.EqualTo("+0"));
            Assert.That(Say(tr, "n-always", new BridgeInt(-3)), Is.EqualTo("-3"));
            Assert.That(Say(tr, "n-except", new BridgeInt(3)), Is.EqualTo("+3"));
            Assert.That(Say(tr, "n-except", new BridgeInt(0)), Is.EqualTo("0"));
            Assert.That(Say(tr, "n-except", Fixed("0.0001")), Is.EqualTo("0"), "rounds to zero first");
        }

        [Test]
        public void TheResultIsStillANumberForPluralsAndForAnotherNumber()
        {
            var tr = Load("en");
            Assert.That(Say(tr, "n-plural", Fixed("1.2")), Is.EqualTo("one ship"));
            Assert.That(Say(tr, "n-plural", Fixed("1.6")), Is.EqualTo("ships"));
            Assert.That(Say(tr, "n-twice", Fixed("1234.6")), Is.EqualTo("1,235.0"));
        }

        [Test]
        public void EachLanguageGivesItsOwnSymbolsAndOthersFallBackToTheEngineDefaults()
        {
            var de = Load("de");
            Assert.That(Say(de, "n-plain", Fixed("1234.5")), Is.EqualTo("1.234,5"));
            Assert.That(Say(de, "n-percent-one", Fixed("0.125")), Is.EqualTo("12,5 %"));
            var fr = Load("fr");
            Assert.That(Say(fr, "n-plain", Fixed("1234.5")), Is.EqualTo("1 234,5"), "a space is written as a string literal");
            var ko = Load("ko");
            Assert.That(Say(ko, "n-plain", Fixed("1234.5")), Is.EqualTo("1,234.5"));
        }

        [Test]
        public void ReplacingTheSymbolTextsIsNotAReservedKeyError()
        {
            Mod("harbor");
            Ftl("harbor", "en", English);
            Ftl("harbor", "de", German);
            var content = LoadContent();
            var bag = new DiagnosticBag();
            TranslationCheck.Run(LoadTextsClean(content), content, bag);
            Assert.That(bag.ToSortedList().Where(d => d.Code == Codes.LocReservedKey || d.Code == Codes.LocNumberOption), Is.Empty);
        }

        [Test]
        public void TheCheckReportsUnknownOptionsAndBadValues()
        {
            Mod("harbor");
            Ftl("harbor", "en",
                "n-currency = { NUMBER($n, currency: \"USD\") }\n" +
                "n-many = { NUMBER($n, maximumFractionDigits: 30) }\n" +
                "n-style = { NUMBER($n, style: \"money\") }\n" +
                "n-range = { NUMBER($n, minimumFractionDigits: 3, maximumFractionDigits: 1) }\n" +
                "n-two-args = { NUMBER($n, $m) }\n" +
                "n-fine = { NUMBER($n, maximumFractionDigits: 2, signDisplay: \"always\") }\n");
            var content = LoadContent();
            var texts = LoadTextsClean(content);
            var bag = new DiagnosticBag();
            TranslationCheck.Run(texts, content, bag);
            var found = bag.ToSortedList().Where(d => d.Code == Codes.LocNumberOption).Select(d => d.Message).ToList();
            Assert.That(found.Count, Is.EqualTo(5), string.Join("\n", found));
            Assert.That(found, Has.Some.Contains("no option 'currency'"));
            Assert.That(found, Has.Some.Contains("'n-many'"));
            Assert.That(found, Has.Some.Contains("'n-style'"));
            Assert.That(found, Has.Some.Contains("larger than"));
            Assert.That(found, Has.Some.Contains("takes one number"));
            Assert.That(found, Has.None.Contains("'n-fine'"));
        }
    }
}
