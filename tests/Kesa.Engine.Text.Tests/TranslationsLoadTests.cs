using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary>
    /// 번역 파일 모으기 (spec-gaps G256·G257·G269): 자리, 같은 키, 순서 없는 모드의 경고, 구문 오류는 그 글만 뺌, 폴백, 다시 읽기.
    /// </summary>
    public class TranslationsLoadTests : TextTestBase
    {
        private static string Say(Translations texts, string key, params string[] languages) =>
            texts.For(languages).Format(new TextValue(key, new Dictionary<string, Value>()));

        [Test]
        public void AModsFilesAreFoundUnderLocaleAndFormatted()
        {
            Mod("harbor");
            Ftl("harbor", "en", "harbor-charter = Sign the charter?\nharbor-gold = { $n } gold\n");
            Ftl("harbor", "ko", "harbor-charter = 헌장에 서명할까요?\n", "sub/dir/more.ftl");
            var texts = LoadTextsClean();
            Assert.That(texts.Languages, Is.EqualTo(new[] { "en", "ko" }));
            Assert.That(Say(texts, "harbor-charter", "ko"), Is.EqualTo("헌장에 서명할까요?"));
            var gold = texts.For(new[] { "en" }).Format(new TextValue("harbor-gold",
                new Dictionary<string, Value> { ["n"] = new IntValue(5) }));
            Assert.That(gold, Is.EqualTo("5 gold"), "no U+2068/U+2069 isolation marks around placeables");
        }

        [Test]
        public void ALaterOrderedModWinsSilently()
        {
            Mod("harbor");
            Mod("harbor_ko", ", defaultLanguage=\"ko\", dependencies=[\"harbor\"]");
            Ftl("harbor", "ko", "harbor-charter = 기계 번역\n");
            Ftl("harbor_ko", "ko", "harbor-charter = 헌장에 서명할까요?\n");
            Ftl("harbor", "en", "harbor-charter = Sign?\n");
            Assert.That(Say(LoadTextsClean(), "harbor-charter", "ko"), Is.EqualTo("헌장에 서명할까요?"));
        }

        [Test]
        public void TwoUnorderedModsWarnAndTheLaterStillWins()
        {
            Mod("alpha");
            Mod("beta");
            Ftl("alpha", "en", "shared-key = from alpha\n");
            Ftl("beta", "en", "shared-key = from beta\n");
            var texts = LoadTexts(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Severity, d.Mod)), Is.EqualTo(new[] { ("LOC003", Severity.Warning, "beta") }));
            Assert.That(diags[0].Message, Does.Contain("'shared-key'"));
            Assert.That(Say(texts, "shared-key", "en"), Is.EqualTo("from beta"));
        }

        [Test]
        public void TheSameKeyTwiceInOneModIsAnErrorAndTheFirstStays()
        {
            Mod("harbor");
            Ftl("harbor", "en", "harbor-a = first\n", "a.ftl");
            Ftl("harbor", "en", "harbor-b = other\n\nharbor-a = second\n", "b.ftl");
            var texts = LoadTexts(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { "LOC002" }));
            Assert.That((Path.GetFileName(diags[0].File), diags[0].Line), Is.EqualTo(("b.ftl", 3)));
            Assert.That(Say(texts, "harbor-a", "en"), Is.EqualTo("first"));
            Assert.That(Say(texts, "harbor-b", "en"), Is.EqualTo("other"), "the rest of the mod's texts are kept (G257)");
        }

        [Test]
        public void ASyntaxErrorDropsOnlyThatTextAndGivesItsLine()
        {
            Mod("harbor");
            Ftl("harbor", "en", "harbor-a = fine\nharbor-b = { $n\nharbor-c = also fine\n");
            var texts = LoadTexts(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { "LOC001" }));
            Assert.That(diags[0].Line, Is.EqualTo(2));
            Assert.That(Say(texts, "harbor-a", "en"), Is.EqualTo("fine"));
            Assert.That(Say(texts, "harbor-c", "en"), Is.EqualTo("also fine"));
            Assert.That(Say(texts, "harbor-b", "en"), Is.EqualTo("harbor-b"));
        }

        [Test]
        public void FallbackGoesThroughShorterTagsThenTheOwnersDefaultLanguageThenTheKey()
        {
            Mod("harbor", ", defaultLanguage=\"zh\"");
            Ftl("harbor", "zh", "harbor-a = 中文\nharbor-b = 中文b\nharbor-c = 中文c\n");
            Ftl("harbor", "en", "harbor-b = english b\n");
            Ftl("harbor", "pt", "harbor-c = português\n");
            var texts = LoadTextsClean();
            var errors = new List<string>();
            var tr = texts.For(new[] { "pt-BR", "en" }, errors.Add);
            string Of(string key) => tr.Format(new TextValue(key, new Dictionary<string, Value>()));
            Assert.That(Of("harbor-c"), Is.EqualTo("português"), "pt-BR → pt");
            Assert.That(Of("harbor-b"), Is.EqualTo("english b"), "the second language of the list");
            Assert.That(Of("harbor-a"), Is.EqualTo("中文"), "the owner's defaultLanguage");
            Assert.That(errors, Is.Empty);
            Assert.That(Of("harbor-none"), Is.EqualTo("harbor-none"));
            Assert.That(errors, Is.EqualTo(new[] { "no translation for 'harbor-none'" }));
        }

        [Test]
        public void TheEngineGivesHiddenAndTheHostFolderIsOverriddenByMods()
        {
            Mod("harbor");
            Ftl("harbor", "en", "host-title = from the mod\n");
            Write("host/en/ui.ftl", "host-title = from the host\nhost-menu = Menu\n");
            var texts = LoadTextsClean(hostFolder: Path.Combine(_root, "host"));
            Assert.That(Say(texts, "host-title", "en"), Is.EqualTo("from the mod"));
            Assert.That(Say(texts, "host-menu", "en"), Is.EqualTo("Menu"));
            Assert.That(Say(texts, EngineTexts.Hidden, "ko"), Is.EqualTo("알 수 없음"));
            Assert.That(Say(texts, EngineTexts.Hidden, "fr"), Is.EqualTo("unknown"), "the engine's default language");
        }

        [Test]
        public void LoadingAgainSeesEditedFilesAndTheOldTranslationsStayAsTheyWere()
        {
            Mod("harbor");
            Ftl("harbor", "en", "harbor-a = before\n");
            var content = LoadContent();
            var before = LoadTextsClean(content);
            Ftl("harbor", "en", "harbor-a = after\n");
            var after = LoadTextsClean(content);
            Assert.That(Say(before, "harbor-a", "en"), Is.EqualTo("before"));
            Assert.That(Say(after, "harbor-a", "en"), Is.EqualTo("after"));
        }

        [Test]
        public void ALocaleFolderNeedsADefaultLanguageThatHasAFolderAndTagFolderNames()
        {
            Mod("alpha", "");
            Ftl("alpha", "en", "alpha-a = a\n");
            Mod("beta", ", defaultLanguage=\"ko\"");
            Ftl("beta", "en", "beta-a = b\n");
            Ftl("beta", "ko_KR", "beta-a = 비\n");
            LoadTexts(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EquivalentTo(new[] { ("LOC005", "alpha"), ("LOC005", "beta"), ("LOC004", "beta") }));
        }
    }
}
