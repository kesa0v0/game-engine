using System.Linq;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>패치 경로 문법과 정규형 비교 (spec-gaps G47·G50).</summary>
    public class PatchPathTests
    {
        private static PatchPath Parse(string text)
        {
            Assert.That(PatchPath.TryParse(text, out var path, out string error), Is.True, text + ": " + error);
            return path;
        }

        [TestCase("move", "move")]
        [TestCase("Health.base_max", "Health.base_max")]
        [TestCase("Skill.Medicine.level", "Skill.Medicine.level")]
        [TestCase("Cost['core.Wood']", "Cost['core.Wood']")]
        [TestCase("Cost[\"core.Wood\"]", "Cost['core.Wood']")]
        [TestCase("Cost['Wood']", "Cost.Wood")]
        [TestCase("Skill['core.Medicine'].level", "Skill['core.Medicine'].level")]
        [TestCase("Table[3]", "Table[3]")]
        [TestCase("Table[-1][2]", "Table[-1][2]")]
        [TestCase("Cost['two words']", "Cost['two words']")]
        [TestCase("_x1.y_2", "_x1.y_2")]
        public void ValidPathsHaveACanonicalForm(string text, string canonical)
        {
            var path = Parse(text);
            Assert.That(path.Text, Is.EqualTo(text));
            Assert.That(path.Canonical, Is.EqualTo(canonical));
        }

        [TestCase("")]
        [TestCase(".a")]
        [TestCase("a.")]
        [TestCase("a..b")]
        [TestCase("1a")]
        [TestCase("a b")]
        [TestCase("a-b")]
        [TestCase("a[")]
        [TestCase("a[]")]
        [TestCase("a['x]")]
        [TestCase("a['x'")]
        [TestCase("['x']")]
        [TestCase("a['x']b")]
        [TestCase("a[1.5]")]
        [TestCase("a[x]")]
        [TestCase("a['x\\y']")]
        [TestCase("a[99999999999999999999]")]
        public void InvalidPathsAreRejectedWithAMessage(string text)
        {
            Assert.That(PatchPath.TryParse(text, out _, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void NamesAndStringKeysCompareByText_IntegersApart()
        {
            Assert.That(Parse("Cost.Wood").SameAs(Parse("Cost['Wood']")), Is.True);
            Assert.That(Parse("Cost.Wood").SameAs(Parse("Cost[\"Wood\"]")), Is.True);
            Assert.That(Parse("Cost.Wood").SameAs(Parse("Cost.Iron")), Is.False);
            Assert.That(Parse("Table[3]").SameAs(Parse("Table['3']")), Is.False);
            Assert.That(Parse("Table[3]").SameAs(Parse("Table[3]")), Is.True);
        }

        [Test]
        public void PrefixMeansOverlap()
        {
            Assert.That(Parse("Health").IsPrefixOf(Parse("Health.base_max")), Is.True);
            Assert.That(Parse("Health.base_max").IsPrefixOf(Parse("Health")), Is.False);
            Assert.That(Parse("Cost").IsPrefixOf(Parse("Cost['Wood']")), Is.True);
            Assert.That(Parse("Cost.Wood").IsPrefixOf(Parse("Cost['Wood']")), Is.True);
            Assert.That(Parse("Cost.Wood").IsPrefixOf(Parse("Cost.Iron")), Is.False);
            Assert.That(Parse("Skill.Medicine").IsPrefixOf(Parse("Skill.Medicine.level")), Is.True);
            Assert.That(Parse("Health").IsPrefixOf(Parse("Wealth")), Is.False);
        }

        [Test]
        public void SegmentsKeepTheirKind()
        {
            var path = Parse("Skill['core.Medicine'].level[2]");
            Assert.That(path.Segments.Select(s => s.IsNumber), Is.EqualTo(new[] { false, false, false, true }));
            Assert.That(path.Segments.Select(s => s.Bracketed), Is.EqualTo(new[] { false, true, false, true }));
            Assert.That(path.Segments[1].Text, Is.EqualTo("core.Medicine"));
            Assert.That(path.Segments[3].Number, Is.EqualTo(2));
        }
    }
}
