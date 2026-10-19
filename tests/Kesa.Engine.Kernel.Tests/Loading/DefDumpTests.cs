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
        public void AnInheritedValueThatTheParentsPatchChangedNamesThePatchingMod()
        {
            // Scout는 move를 자기가 패치하지 않고 Base의 패치된 값을 물려받는다
            var json = Dump("patch.set(\"Base\", \"move\", 5)\n");
            int scout = json.IndexOf("\"id\": \"Scout\"", System.StringComparison.Ordinal);
            string scoutPart = json.Substring(scout);
            Assert.That(scoutPart, Does.Contain("\"value\": 5,\n          \"origin\": \"inherited\",\n          \"source\": {\n" +
                                                "            \"def\": \"Base\",\n            \"mod\": \"addon\",\n" +
                                                "            \"file\": \"addon/defs/defs.py\""));
        }

        /// <summary>Scout의 그 칸(기본 move)에 나온 패치들의 (def, op).</summary>
        private static string[] ScoutPatches(string json, string field = "move")
        {
            string scout = json.Substring(json.IndexOf("\"id\": \"Scout\"", System.StringComparison.Ordinal));
            string move = scout.Substring(scout.IndexOf("\"" + field + "\": {", System.StringComparison.Ordinal));
            move = move.Substring(0, move.IndexOf("\n        }", System.StringComparison.Ordinal));
            var found = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(move,
                         "\"def\": \"(\\w+)\",\\s*\"op\": \"(\\w+)\""))
                found.Add(m.Groups[1].Value + " " + m.Groups[2].Value);
            return found.ToArray();
        }

        private string DumpWith(string schema, string defs, string patches)
        {
            Mod("core", schema: schema, defs: defs);
            Mod("addon", defs: patches, extra: ", dependencies=[\"core\"]");
            return DefDump.ToJson(LoadClean());
        }

        [Test]
        public void AParentPatchThatTheChildOverwroteIsNotListed()
        {
            // 자식이 직접 적은 값, 자식의 통째 set이 덮은 값에는 부모 패치가 영향을 주지 않았다
            const string schema = "deftype(\"Unit\", move=const(int, default=1))\n";
            Assert.That(ScoutPatches(DumpWith(schema, "Unit(\"Base\", move=2)\nUnit(\"Scout\", parent=\"Base\", move=3)\n",
                "patch.set(\"Base\", \"move\", 5)\n")), Is.Empty);
            TearDown();
            SetUp();
            Assert.That(ScoutPatches(DumpWith(schema, "Unit(\"Base\", move=2)\nUnit(\"Scout\", parent=\"Base\")\n",
                "patch.set(\"Base\", \"move\", 5)\npatch.set(\"Scout\", \"move\", 7)\n")), Is.EqualTo(new[] { "Scout set" }));
        }

        [Test]
        public void ParentPatchesUnderTheChildsValueAreListedParentFirst()
        {
            // 물려받은 값, inherit 식이 읽은 값, 그 위에 쌓은 add
            const string schema = "deftype(\"Unit\", move=const(int, default=1))\n";
            Assert.That(ScoutPatches(DumpWith(schema, "Unit(\"Base\", move=2)\nUnit(\"Scout\", parent=\"Base\", move=inherit.move + 1)\n",
                "patch.add(\"Scout\", \"move\", 1)\npatch.set(\"Base\", \"move\", 5)\n")), Is.EqualTo(new[] { "Base set", "Scout add" }));
        }

        [Test]
        public void AnAppendMergedListKeepsTheParentsPatches()
        {
            // 자식의 값은 부모의 (패치된) 리스트 뒤에 붙인 것이다
            Assert.That(ScoutPatches(DumpWith("deftype(\"Unit\", tags=const(list[str], merge=\"append\"))\n",
                    "Unit(\"Base\", tags=[\"a\"])\nUnit(\"Scout\", parent=\"Base\", tags=[\"b\"])\n",
                    "patch.append(\"Base\", \"tags\", [\"c\"])\n"), "tags"),
                Is.EqualTo(new[] { "Base append" }));
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
