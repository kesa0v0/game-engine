using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Reporting;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>check JSON 출력 (spec-gaps G75).</summary>
    public class CheckReportTests : LoaderTestBase
    {
        private const string Schema = "deftype(\"Resource\")\ndeftype(\"Unit\", cost=const(dict[DefId[\"Resource\"], int]))\n";

        [Test]
        public void ACleanLoadIsOkWithEmptyDiagnostics()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\nUnit(\"Scout\", cost={\"Wood\": 1})\n");
            var result = LoadClean();
            const string expected =
                "{\n" +
                "  \"version\": 1,\n" +
                "  \"ok\": true,\n" +
                "  \"counts\": {\"error\": 0, \"warning\": 0, \"info\": 0},\n" +
                "  \"mods\": {\"loaded\": [\"core\"], \"disabled\": []},\n" +
                "  \"diagnostics\": []\n" +
                "}\n";
            Assert.That(CheckReport.ToJson(new List<Diagnostic>(), result), Is.EqualTo(expected));
        }

        [Test]
        public void ADiagnosticHasEveryKeyWithNullsForMissingValues()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\nUnit(\"Scout\", cost={\"Wod\": 1})\n");
            var result = Load(out var diags);
            const string expected =
                "{\n" +
                "  \"version\": 1,\n" +
                "  \"ok\": false,\n" +
                "  \"counts\": {\"error\": 1, \"warning\": 0, \"info\": 0},\n" +
                "  \"mods\": {\"loaded\": [], \"disabled\": [\"core\"]},\n" +
                "  \"diagnostics\": [\n" +
                "    {\n" +
                "      \"code\": \"LNK001\",\n" +
                "      \"severity\": \"error\",\n" +
                "      \"message\": \"there is no Resource 'Wod'\",\n" +
                "      \"mod\": \"core\",\n" +
                "      \"file\": \"core/defs/defs.py\",\n" +
                "      \"line\": 2,\n" +
                "      \"col\": 25,\n" +
                "      \"hint\": \"did you mean 'Wood'?\",\n" +
                "      \"related\": []\n" +
                "    }\n" +
                "  ]\n" +
                "}\n";
            Assert.That(CheckReport.ToJson(diags, result), Is.EqualTo(expected));
        }

        [Test]
        public void RelatedLocationsAreListedAndWarningsDoNotBreakOk()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\n");
            var result = LoadClean();
            var span = new SourceSpan("core/defs/defs.py", 3, 4);
            var other = new SourceSpan("core/defs/other.py", 7, 1);
            var diags = new List<Diagnostic>
            {
                new Diagnostic("PAT011", Severity.Warning, "a warning", "core", span, null, new[] { other }),
                new Diagnostic("PAT002", Severity.Info, "an info", null, SourceSpan.None),
            };
            var json = CheckReport.ToJson(diags, result);
            Assert.That(json, Does.Contain("\"ok\": true"));
            Assert.That(json, Does.Contain("\"counts\": {\"error\": 0, \"warning\": 1, \"info\": 1}"));
            Assert.That(json, Does.Contain("\"related\": [{\"file\": \"core/defs/other.py\", \"line\": 7, \"col\": 1}]"));
            Assert.That(json, Does.Contain("\"mod\": null"));
        }

        [Test]
        public void StringsAreEscapedButKoreanIsLeftAlone()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\n");
            var result = LoadClean();
            var d = new Diagnostic("X001", Severity.Error, "따옴표 \" 역슬래시 \\ 줄바꿈 \n 탭 \t 제어 \u0001", "core",
                new SourceSpan("core/한글.py", 1, 1));
            var json = CheckReport.ToJson(new[] { d }, result);
            Assert.That(json, Does.Contain("\"message\": \"따옴표 \\\" 역슬래시 \\\\ 줄바꿈 \\n 탭 \\t 제어 \\u0001\""));
            Assert.That(json, Does.Contain("\"file\": \"core/한글.py\""));
        }

        [Test]
        public void TheSameInputGivesTheSameText()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\nUnit(\"Scout\", cost={\"Wod\": 1}, nope=1)\n");
            var result = Load(out var diags);
            Assert.That(CheckReport.ToJson(diags, result), Is.EqualTo(CheckReport.ToJson(diags, result)));
            Assert.That(diags.Count, Is.GreaterThan(0));
        }

        [Test]
        public void LoadedModsAreInLoadOrderAndDisabledOnesInIdOrder()
        {
            Mod("core", schema: Schema, defs: "Resource(\"Wood\")\n");
            Mod("zeta", defs: "Resource(\"Wood\")\n", extra: ", dependencies=[\"core\"]");
            Mod("alpha", defs: "Resource(\"Wood\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            var json = CheckReport.ToJson(diags, result);
            Assert.That(json, Does.Contain("\"mods\": {\"loaded\": [\"core\"], \"disabled\": [\"alpha\", \"zeta\"]}"));
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.DefDuplicateId));
        }
    }
}
