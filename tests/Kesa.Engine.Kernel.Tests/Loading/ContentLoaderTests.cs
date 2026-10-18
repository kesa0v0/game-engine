using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    public partial class ContentLoaderTests : LoaderTestBase
    {
        private static string[] ComponentsOf(DefType t) => t.Components.Select(s => s.Component.Name).ToArray();

        // ---- schema-design 4.1: 이 단계를 나눈 이유 ----

        [Test]
        public void ExtensionFromAModLoadedLaterReachesDefsOfAModLoadedEarlier()
        {
            Mod("core", schema: "component(\"Health\", hp=field(int, default=10))\n" +
                                "deftype(\"Unit\", components=[\"Health\"])\n");
            // aaa_scouts는 core에만 의존하고 morale보다 먼저 로드된다
            Mod("aaa_scouts", defs: "Unit(\"Scout\")\n", extra: ", dependencies=[\"core\"]");
            Mod("morale", schema: "component(\"Morale\", value=field(int, default=5))\n" +
                                  "extend(\"Unit\", components=[\"Morale\"])\n", extra: ", dependencies=[\"core\"]");

            var result = LoadClean();
            Assert.That(result.Mods.Select(m => m.Id), Is.EqualTo(new[] { "core", "aaa_scouts", "morale" }));
            Assert.That(result.Defs.TryGet("Scout", out var scout), Is.True);
            Assert.That(ComponentsOf(scout.Type), Is.EqualTo(new[] { "Health", "Morale" }));
            Assert.That(scout.Type.Components[1].Mod, Is.EqualTo("morale"));
        }

        // ---- 검증 시나리오 ③ (틱 경제 시뮬): schema-design 2.3의 PopGroup ----

        [Test]
        public void EconomySchemaAndDefs()
        {
            Mod("eco",
                schema:
                "def health_max(h: Health) -> int:\n" +
                "    flat = 0\n" +
                "    for r in contribs(h, \"max\"):\n" +
                "        flat += r.value\n" +
                "    return h.base_max + flat\n" +
                "\n" +
                "deftype(\"Need\", label=const(TextKey, default=\"\"))\n" +
                "component(\"Health\",\n" +
                "    base_max=const(int, default=10, min=1),\n" +
                "    max=derived(int, health_max),\n" +
                "    current=field(int, default_from=\"max\", min=0),\n" +
                ")\n" +
                "component(\"Wealth\", amount=field(fixed, default=0))\n" +
                "deftype(\"Resource\")\n" +
                "component(\"Stock\", key=\"Resource\", amount=field(fixed, default=0))\n" +
                "deftype(\"PopGroup\",\n" +
                "    label=const(TextKey, default=\"\"),\n" +
                "    population=const(int, default=1),\n" +
                "    needs=const(dict[DefId[\"Need\"], fixed]),\n" +
                "    tags=const(list[str], merge=\"append\"),\n" +
                "    components=[\"Wealth\", \"Health\", \"Stock\"],\n" +
                ")\n",
                defs:
                "Need(\"Hunger\", label=\"need.hunger\")\n" +
                "Resource(\"Food\")\n" +
                "PopGroup(\"SlumLaborers\",\n" +
                "    label=\"pop.slum_laborers\",\n" +
                "    needs={\"Hunger\": 1.2},\n" +
                "    Wealth=Wealth(amount=50),\n" +
                "    Health=Health(base_max=8),\n" +
                "    Stock={\"Food\": Stock(amount=3)},\n" +
                ")\n");

            var result = LoadClean();
            var schema = result.Schema;

            Assert.That(schema.TryGetType("Need", out var need), Is.True);
            Assert.That(need.IsDataOnly, Is.True);

            Assert.That(schema.TryGetComponent("Health", out var health), Is.True);
            Assert.That(health.TryGetMember("max", out var max), Is.True);
            Assert.That(max.Kind, Is.EqualTo(MemberKind.Derived));
            Assert.That(max.Spec.Function!.Name, Is.EqualTo("health_max"));
            Assert.That(health.TryGetMember("current", out var current), Is.True);
            Assert.That(current.Spec.DefaultFrom!.Value.Name, Is.EqualTo("max"));

            Assert.That(schema.TryGetComponent("Stock", out var stock), Is.True);
            Assert.That(stock.Key!.Name, Is.EqualTo("Resource"));

            Assert.That(schema.TryGetType("PopGroup", out var pop), Is.True);
            Assert.That(pop.Consts.Select(c => c.Name), Is.EqualTo(new[] { "label", "population", "needs", "tags" }));
            Assert.That(pop.Consts[2].Spec.Type.Display, Is.EqualTo("dict[DefId[\"Need\"], fixed]"));
            Assert.That(pop.Consts[3].Spec.Merge, Is.EqualTo(MergeMode.Append));

            Assert.That(result.Defs.Defs.Select(d => d.Id), Is.EqualTo(new[] { "Hunger", "Food", "SlumLaborers" }));
            Assert.That(result.Defs.TryGet("SlumLaborers", out var slum), Is.True);
            Assert.That(slum.Fields.Select(f => f.Name),
                Is.EqualTo(new[] { "label", "needs", "Wealth", "Health", "Stock" }));
            Assert.That(slum.TryGetField("Health", out var h), Is.True);
            var hv = (ComponentValue)h.Value;
            Assert.That(((IntValue)hv.Fields.Single().Value).Value, Is.EqualTo(8));
            Assert.That(slum.TryGetField("needs", out var needs), Is.True);
            Assert.That(((FixedValue)((DictValue)needs.Value).Items.Single().Value).Value.Raw, Is.EqualTo(1_200_000));
            Assert.That((needs.Span.File, needs.Span.Line), Is.EqualTo(("eco/defs/defs.py", 5)));
        }

        // ---- 람다 (G33): schema-design 3.3.1·3.3.3 예시 그대로 ----

        [Test]
        public void LambdasAreFunctionValues()
        {
            Mod("goals",
                schema:
                "component(\"Health\",\n" +
                "    base_max=const(int, default=10),\n" +
                "    max=derived(int, lambda h: h.base_max),\n" +
                "    current=field(int, default=lambda h: h.max // 2),\n" +
                ")\n" +
                "component(\"Progress\", value=field(int, default=0), target=const(int, default=1))\n" +
                "deftype(\"ConflictGoal\", succeeded=const(Cond[\"ConflictGoal\"]), components=[\"Progress\"])\n",
                defs:
                "ConflictGoal(\"InfluenceSlot\",\n" +
                "    Progress=Progress(target=10),\n" +
                "    succeeded=lambda g: g.Progress.value >= g.Progress.target,\n" +
                ")\n");
            var result = LoadClean();
            Assert.That(result.Schema.TryGetComponent("Health", out var health), Is.True);
            Assert.That(health.TryGetMember("max", out var max), Is.True);
            Assert.That(max.Spec.Function!.Name, Is.EqualTo(FunctionValue.LambdaName));
            Assert.That(health.TryGetMember("current", out var current), Is.True);
            Assert.That(current.Spec.Default, Is.InstanceOf<FunctionValue>());
            Assert.That(result.Defs.TryGet("InfluenceSlot", out var goal), Is.True);
            Assert.That(goal.TryGetField("succeeded", out var succeeded), Is.True);
            Assert.That(((FunctionValue)succeeded.Value).Parameters.Single().Name, Is.EqualTo("g"));
        }

        // ---- 키 컴포넌트의 키 종류 (G31) ----

        private const string OpinionSchema =
            "component(\"Treasury\", gold=field(int, default=0))\n" +
            "deftype(\"Faction\", components=[\"Treasury\", \"Opinion\"])\n" +
            "component(\"Opinion\", key=Ref[\"Faction\"], value=field(int, default=0))\n";

        [Test]
        public void EntityKeyedComponentIsAlwaysListed()
        {
            Mod("core", schema: OpinionSchema +
                                "deftype(\"SkillDef\")\n" +
                                "component(\"Skill\", key=\"SkillDef\", level=field(int, default=0))\n");
            var schema = LoadClean().Schema;
            Assert.That(schema.TryGetComponent("Opinion", out var opinion), Is.True);
            Assert.That((opinion.KeyKind, opinion.Keys, opinion.Key!.Name),
                Is.EqualTo((KeyKind.Entity, KeysMode.Listed, "Faction")));
            Assert.That(schema.TryGetComponent("Skill", out var skill), Is.True);
            Assert.That((skill.KeyKind, skill.Keys), Is.EqualTo((KeyKind.Def, KeysMode.All)));
        }

        [TestCase(OpinionSchema + "component(\"C\", key=Ref[\"Faction\"], keys=\"all\", v=field(int))\n",
            Codes.SchBadDeclaration)]
        [TestCase("deftype(\"SkillDef\")\ncomponent(\"C\", key=Ref[\"SkillDef\"], v=field(int))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", key=3, v=field(int))\n", Codes.EvlType)]
        public void BadKeys(string schema, string code)
        {
            Mod("core", schema: schema);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code), string.Join("\n", diags));
        }

        [TestCase("component(\"C\", entity=field(int, default=0))\n", "'entity' is reserved")]
        [TestCase("component(\"Health\", Health=field(int, default=0))\n", "named like itself")]
        public void MemberNamesThatCollideWithFunctionAccessAreRejected(string schema, string message)
        {
            Mod("core", schema: schema);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.SchBadDeclaration), string.Join("\n", diags));
            Assert.That(string.Join("\n", diags), Does.Contain(message));
        }

        // ---- Def 참조와 엔티티 참조 (G32) ----

        [Test]
        public void RuntimeColumnCanHoldADefId()
        {
            Mod("core", schema: "deftype(\"Recipe\")\n" +
                                "component(\"Factory\", recipe=field(DefId[\"Recipe\"]))\n" +
                                "deftype(\"Building\", upkeep=const(dict[DefId[\"Recipe\"], int]), components=[\"Factory\"])\n",
                defs: "Recipe(\"Bread\")\nBuilding(\"Bakery\", Factory=Factory(recipe=\"Bread\"))\n");
            var schema = LoadClean().Schema;
            Assert.That(schema.TryGetComponent("Factory", out var factory), Is.True);
            Assert.That(factory.TryGetMember("recipe", out var recipe), Is.True);
            Assert.That(recipe.Spec.Type.Display, Is.EqualTo("DefId[\"Recipe\"]"));
        }

        [TestCase(OpinionSchema + "deftype(\"T\", f=const(Ref[\"Faction\"]))\n", Codes.SchBadDeclaration)]
        [TestCase(OpinionSchema + "deftype(\"T\", f=const(list[Ref[\"Faction\"]]))\n", Codes.SchBadDeclaration)]
        [TestCase(OpinionSchema + "component(\"C\", f=field(Ref[\"Faction\"]))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"Need\")\ncomponent(\"C\", n=ref(\"Need\"))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\", n=const(dict[\"Need\", int]))\n", Codes.EvlType)]
        [TestCase("deftype(\"T\", n=const(dict[fixed, int]))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\", n=const(dict[TextKey, int]))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\", n=const(dict[list[str], int]))\n", Codes.SchBadDeclaration)]
        public void BadReferences(string schema, string code)
        {
            Mod("core", schema: schema);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code), string.Join("\n", diags));
        }

        // 엔티티가 되는지는 가리키는 모드에 보이는 컴포넌트로만 판정한다: 의존하지 않는 모드의 extend에 결과가 달리면 안 된다
        [TestCase("component(\"C\", s=ref(\"Sector\"))\n")]
        [TestCase("component(\"C\", key=Ref[\"Sector\"], v=field(int))\n")]
        public void EntityCapabilityIsJudgedWithinTheVisibleScope(string schema)
        {
            Mod("core", schema: "deftype(\"Sector\")\n");
            Mod("b", schema: "component(\"Pos\", x=field(int))\nextend(\"Sector\", components=[\"Pos\"])\n",
                extra: ", dependencies=[\"core\"]");
            Mod("a", schema: schema, extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchBadDeclaration));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a" }));

            Directory.Delete(Path.Combine(_root, "b"), true);
            result = Load(out diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchBadDeclaration));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a" }));

            Mod("b", schema: "component(\"Pos\", x=field(int))\nextend(\"Sector\", components=[\"Pos\"])\n",
                extra: ", dependencies=[\"core\"]");
            Write("a/mod.py", "Mod(id=\"a\", dependencies=[\"core\", \"b\"])\nuse_all(\"schema/\")\n");
            LoadClean();
        }

        [Test]
        public void EntityKeyedRowsCannotBeWrittenInADef()
        {
            Mod("core", schema: OpinionSchema, defs: "Faction(\"Rome\", Opinion={\"Carthage\": Opinion(value=-5)})\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.DefBadValue));
        }

        // ---- 검증 시나리오 ① (끼어들기 스택/카드): 순서 있는 더미 ----

        [Test]
        public void CardPileSchemaWithOrderedInverse()
        {
            Mod("cards",
                schema:
                "deftype(\"Pile\", components=[\"PileInfo\"])\n" +
                "component(\"PileInfo\", hidden=const(bool, default=False))\n" +
                "component(\"InPile\", pile=ref(\"Pile\", inverse=\"cards\", order_by=\"pos\"), pos=field(int, default=0))\n" +
                "deftype(\"ConflictGoal\", succeeded=const(Cond[\"ConflictGoal\"]),\n" +
                "    on_success=const(list[Action[\"ConflictGoal\"]], merge=\"append\"))\n" +
                "deftype(\"Card\", label=const(TextKey, default=\"\"), components=[\"InPile\"])\n",
                defs:
                "def take(g: ConflictGoal):\n    pass\n" +
                "def done(g: ConflictGoal) -> bool:\n    return True\n" +
                "Pile(\"Deck\")\n" +
                "ConflictGoal(\"Win\", succeeded=done, on_success=[take])\n" +
                "Card(\"Strike\", label=\"card.strike\", InPile=InPile(pile=\"Deck\"))\n");

            var result = LoadClean();
            Assert.That(result.Schema.TryGetType("Pile", out var pile), Is.True);
            var inverse = pile.Inverses.Single();
            Assert.That(inverse.Name, Is.EqualTo("cards"));
            Assert.That(inverse.Source.Name, Is.EqualTo("InPile"));
            Assert.That(result.Defs.TryGet("Win", out var win), Is.True);
            Assert.That(win.TryGetField("succeeded", out var cond), Is.True);
            Assert.That(((FunctionValue)cond.Value).Name, Is.EqualTo("done"));
        }

        // ---- 단계 분리 ----

        [Test]
        public void CreatingADefInASchemaFileIsAnError()
        {
            Mod("core", schema: "deftype(\"Unit\")\nUnit(\"Scout\")\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchDefInSchemaFile));
            Assert.That((d.File, d.Line), Is.EqualTo(("core/schema/types.py", 2)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void SchemaDeclarationInADefFileIsAnError()
        {
            Mod("core", defs: "deftype(\"Unit\")\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchSchemaInDefFile));
        }

        [Test]
        public void SchemaFilesRunBeforeDefFilesWhateverTheUseOrder()
        {
            Write("core/units.py", "Unit(\"Scout\")\n");
            Write("core/schema/types.py", "deftype(\"Unit\")\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse(\"units.py\")\nuse_all(\"schema/\")\n");
            var result = LoadClean();
            Assert.That(result.Defs.TryGet("Scout", out _), Is.True);
        }

        [Test]
        public void UseListExpandsInOrdinalOrderAndRunsEachFileOnce()
        {
            // 순서는 같은 이름을 쓰기 전에 정의해야 하는 규칙(G20)으로 확인한다
            Write("core/schema/base.py", "def f(t: T) -> bool:\n    return True\n");
            Write("core/schema/a/one.py", "deftype(\"T\", ok=const(Cond[\"T\"]))\nx = f\n");
            Write("core/schema/b.py", "y = x\n");
            Write("core/schema/types.pyi", "this is not python at all\n");
            Write("core/defs/z.py", "T(\"A\", ok=y)\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse(\"schema/base.py\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var result = LoadClean();
            Assert.That(result.Defs.TryGet("A", out _), Is.True);
        }

        // ---- 유니코드 정규화 (G213): macOS에서 온 자모 분리형(NFD) 이름도 완성형(NFC)으로 적은 use로 찾는다 ----

        private const string Nfc = "유닛";
        private static readonly string Nfd = Nfc.Normalize(System.Text.NormalizationForm.FormD);

        [Test]
        public void ANfdFileNameIsFoundByAnNfcUseAndReportedAsNfc()
        {
            Assert.That(Nfd, Is.Not.EqualTo(Nfc));
            Write("core/schema/types.py", "deftype(\"Unit\")\n");
            Write("core/defs/" + Nfd + ".py", "Unit(\"Scout\")\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse(\"defs/" + Nfc + ".py\")\n");
            var loaded = LoadClean();
            Assert.That(loaded.Defs.TryGet("Scout", out _), Is.True);
            // 내용 지문의 경로도 NFC다 (G212)
            Assert.That(loaded.Contents["core"].Files.Select(f => f.Path), Does.Contain("defs/" + Nfc + ".py"));

            // 오류 위치의 파일 이름도 NFC다
            Write("core/defs/" + Nfd + ".py", "Unit(\"Scout\"\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Span.File).Distinct(), Is.EqualTo(new[] { "core/defs/" + Nfc + ".py" }));
        }

        [Test]
        public void ANfdFolderIsFoundByUseAllAndAnNfdUseFindsAnNfcName()
        {
            Write("core/schema/types.py", "deftype(\"Unit\")\n");
            Write("core/" + Nfd + "/a.py", "Unit(\"Scout\")\n");
            Write("core/defs/" + Nfc + ".py", "Unit(\"Ranger\")\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"" + Nfc + "/\")\nuse(\"defs/" + Nfd + ".py\")\n");
            var result = LoadClean();
            Assert.That(result.Defs.TryGet("Scout", out _), Is.True);
            Assert.That(result.Defs.TryGet("Ranger", out _), Is.True);
        }

        [Test]
        public void TwoNamesThatDifferOnlyInNormalizationAreAnError()
        {
            Write("core/schema/types.py", "deftype(\"Unit\")\n");
            Write("core/defs/" + Nfc + ".py", "Unit(\"Scout\")\n");
            Write("core/defs/" + Nfd + ".py", "Unit(\"Ranger\")\n");
            if (System.IO.Directory.GetFiles(System.IO.Path.Combine(_root, "core", "defs")).Length != 2)
                Assert.Ignore("this file system treats the two names as one (macOS APFS)");
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse(\"defs/" + Nfc + ".py\")\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "MOD002 file 'defs/" + Nfc + ".py' matches 'defs/" + Nfc + ".py' and another name on disk that differ only in " +
                "Unicode normalization",
            }));

            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var result = Load(out diags);
            Assert.That(diags.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "LOD001 'defs/" + Nfc + ".py' is on disk twice, as names that differ only in Unicode normalization",
            }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void AWrongCaseHintIsWrittenInNfc()
        {
            Write("core/defs/" + Nfd + ".py", "x = 1\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse(\"DEFS/" + Nfc + ".py\")\n");
            Load(out var diags);
            Assert.That(diags.Single().Hint, Is.EqualTo("did you mean 'defs/" + Nfc + ".py'?"), diags.Single().Message);
        }

        [Test]
        public void ListingModPyIsAnError()
        {
            Write("core/mod.py", "Mod(id=\"core\")\nuse(\"mod.py\")\n");
            var result = Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.LodBadUse));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void SchemaFolderNameIsCaseSensitive()
        {
            Write("core/Schema/types.py", "deftype(\"T\")\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"Schema/\")\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.LodBadUse));
        }

        [Test]
        public void FunctionsDefinedInSchemaFilesAreVisibleToTheModsDefFiles()
        {
            Mod("core",
                schema: "def always(g: Goal) -> bool:\n    return True\ndeftype(\"Goal\", ok=const(Cond[\"Goal\"]))\n",
                defs: "Goal(\"G\", ok=always)\n");
            LoadClean();
        }
    }
}
