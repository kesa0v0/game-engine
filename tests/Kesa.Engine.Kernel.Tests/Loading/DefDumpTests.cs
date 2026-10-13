using Kesa.Engine.Reporting;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>최종 Def 덤프 + 필드별 출처 (spec-gaps G76).</summary>
    public class DefDumpTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10))\n" +
            "component(\"Morale\", value=const(int, default=3))\n" +
            "deftype(\"Resource\")\n" +
            "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=const(int, default=1))\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Skill\"], move=const(int, default=1),\n" +
            "    ratio=const(fixed, default=1.5), tags=const(list[str]), cost=const(dict[DefId[\"Resource\"], int]))\n";

        private const string Defs =
            "Resource(\"Wood\")\n" +
            "Unit(\"Base\", abstract=True, move=2, tags=[\"a\"])\n" +
            "Unit(\"Scout\", parent=\"Base\", Health=Health(base_max=12), cost={\"Wood\": 2})\n";

        private string Dump(string? patches = null)
        {
            Mod("core", schema: Schema, defs: Defs);
            if (patches != null) Mod("addon", defs: patches, extra: ", dependencies=[\"core\"]");
            return DefDump.ToJson(LoadClean());
        }

        [Test]
        public void AMinimalDumpHasTheFullShape()
        {
            Mod("core", schema: "deftype(\"Item\", weight=const(int, default=1))\n", defs: "Item(\"Rock\")\n");
            const string expected =
                "{\n" +
                "  \"version\": 1,\n" +
                "  \"defs\": [\n" +
                "    {\n" +
                "      \"id\": \"Rock\",\n" +
                "      \"type\": \"Item\",\n" +
                "      \"mod\": \"core\",\n" +
                "      \"abstract\": false,\n" +
                "      \"parent\": null,\n" +
                "      \"consts\": {\n" +
                "        \"weight\": {\n" +
                "          \"value\": 1,\n" +
                "          \"origin\": \"default\",\n" +
                "          \"source\": {\n" +
                "            \"def\": null,\n" +
                "            \"mod\": null,\n" +
                "            \"file\": \"core/schema/types.py\",\n" +
                "            \"line\": 1,\n" +
                "            \"col\": 17\n" +
                "          },\n" +
                "          \"patches\": []\n" +
                "        }\n" +
                "      },\n" +
                "      \"components\": {}\n" +
                "    }\n" +
                "  ]\n" +
                "}\n";
            Assert.That(DefDump.ToJson(LoadClean()), Is.EqualTo(expected));
        }

        [Test]
        public void AbstractDefsAreIncludedInRegistrationOrder()
        {
            var json = Dump();
            int wood = json.IndexOf("\"id\": \"Wood\"", System.StringComparison.Ordinal);
            int baseAt = json.IndexOf("\"id\": \"Base\"", System.StringComparison.Ordinal);
            int scout = json.IndexOf("\"id\": \"Scout\"", System.StringComparison.Ordinal);
            Assert.That(wood, Is.GreaterThanOrEqualTo(0).And.LessThan(baseAt));
            Assert.That(baseAt, Is.LessThan(scout));
            Assert.That(json, Does.Contain("\"abstract\": true"));
            Assert.That(json, Does.Contain("\"parent\": \"Base\""));
        }

        [Test]
        public void OriginsAreOwnInheritedAndDefault()
        {
            var json = Dump();
            // Scout: move는 Base에서 물려받음, ratio는 스키마 기본값, base_max는 자기가 적음
            Assert.That(json, Does.Contain("\"value\": 2,\n          \"origin\": \"inherited\""));
            Assert.That(json, Does.Contain("\"value\": \"1.5\",\n          \"origin\": \"default\""));
            Assert.That(json, Does.Contain("\"value\": 12,\n              \"origin\": \"own\""));
            Assert.That(json, Does.Contain("\"value\": [\"a\"],\n          \"origin\": \"inherited\""));
            Assert.That(json, Does.Contain("\"value\": {\n            \"Wood\": 2\n          }"));
        }

        [Test]
        public void APatchedValueNamesThePatchAndItsModAndShowsTheHistoryParentFirst()
        {
            var json = Dump("patch.set(\"Base\", \"move\", 5)\npatch.add(\"Scout\", \"move\", 1)\n");
            Assert.That(json, Does.Contain("\"value\": 6,\n          \"origin\": \"patched\""));
            // 부모 Base의 set이 먼저, Scout의 add가 그 뒤
            int set = json.IndexOf("\"op\": \"set\"", System.StringComparison.Ordinal);
            int add = json.IndexOf("\"op\": \"add\"", System.StringComparison.Ordinal);
            Assert.That(set, Is.GreaterThanOrEqualTo(0).And.LessThan(add));
            Assert.That(json, Does.Contain("\"status\": \"applied\""));
            Assert.That(json, Does.Contain("\"def\": \"Scout\",\n              \"op\": \"add\""));
        }

        [Test]
        public void AttachedComponentsAndRowsShowTheirPatch()
        {
            var json = Dump("patch.set(\"Scout\", \"Skill.Wood\", Skill(level=4))\n");
            Assert.That(json, Does.Contain("\"attachedBy\": {\n                \"mod\": \"addon\""));
            Assert.That(json, Does.Contain("\"rows\": {\n            \"Wood\": {"));
            Assert.That(json, Does.Contain("\"value\": 4,"));
        }

        [Test]
        public void ComponentValuesInPatchesAreShownStructured()
        {
            var json = Dump("patch.set(\"Scout\", \"Skill.Wood\", Skill(level=4))\n");
            Assert.That(json, Does.Contain("\"value\": {\n                  \"component\": \"Skill\""));
        }

        [Test]
        public void TheDumpIsDeterministicAndDoesNotListFailedPatches()
        {
            Mod("core", schema: Schema, defs: Defs);
            Mod("addon", defs: "patch.set(\"Scout\", \"move\", 9)\n", extra: ", dependencies=[\"core\"]");
            Mod("broken", defs: "patch.remove(\"Scout\", \"tags\", \"zzz\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out _);
            var json = DefDump.ToJson(result);
            Assert.That(json, Is.EqualTo(DefDump.ToJson(result)));
            Assert.That(json, Does.Not.Contain("broken"));
            Assert.That(json, Does.Not.Contain("\"status\": \"failed\""));
            Assert.That(json, Does.Contain("\"value\": 9"));
        }
    }
}
