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
    public class ContentLoaderTests : LoaderTestBase
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

        // ---- 이름 풀이와 보이는 범위 ----

        [Test]
        public void UnknownComponentPointsAtTheListItem()
        {
            Mod("core", schema: "component(\"Health\")\ndeftype(\"Unit\", components=[\"Helth\"])\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchUnknownName));
            Assert.That((d.Line, d.Col), Is.EqualTo((2, 29)));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'Health'?"));
        }

        [Test]
        public void SchemaNamesAreOrderIndependentWithinAndAcrossFiles()
        {
            Write("core/schema/a.py", "deftype(\"Unit\", components=[\"Location\"])\n");
            Write("core/schema/b.py", "component(\"Location\", at=ref(\"Sector\", inverse=\"units\"))\n" +
                                      "deftype(\"Sector\", components=[\"Terrain\"])\ncomponent(\"Terrain\")\n");
            Write("core/mod.py", "Mod(id=\"core\")\nuse_all(\"schema/\")\n");
            var result = LoadClean();
            Assert.That(result.Schema.TryGetType("Sector", out var sector), Is.True);
            Assert.That(sector.Inverses.Single().Name, Is.EqualTo("units"));
        }

        [Test]
        public void UsingANameFromAModThatIsNotADependencyIsAnError()
        {
            Mod("core", schema: "component(\"Health\")\n");
            // loadAfter는 순서만 정하고 보이는 범위를 넓히지 않는다
            Mod("units", schema: "deftype(\"Unit\", components=[\"Health\"])\n", extra: ", loadAfter=[\"core\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(d.Hint, Is.EqualTo("add \"core\" to dependencies in mod.py"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "units" }));
        }

        [Test]
        public void TransitiveDependenciesAreVisible()
        {
            Mod("base", schema: "component(\"Health\")\n");
            Mod("mid", schema: "deftype(\"Unit\", components=[\"Health\"])\n", extra: ", dependencies=[\"base\"]");
            Mod("top", defs: "Unit(\"Scout\", Health=Health())\n", extra: ", dependencies=[\"mid\"]");
            LoadClean();
        }

        [Test]
        public void ConstructorOfANonDependencyIsNotVisibleInDefFiles()
        {
            Mod("core", schema: "deftype(\"Unit\")\n");
            Mod("other", defs: "Unit(\"Scout\")\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(d.Mod, Is.EqualTo("other"));
        }

        [Test]
        public void HiddenExtensionCannotBeWrittenButStillApplies()
        {
            Mod("core", schema: "deftype(\"Unit\")\n");
            Mod("morale", schema: "component(\"Morale\")\nextend(\"Unit\", components=[\"Morale\"])\n",
                extra: ", dependencies=[\"core\"]");
            Mod("scouts", defs: "Unit(\"Scout\", Morale=Morale())\n", extra: ", dependencies=[\"core\"]");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.SchNotVisible }));
        }

        // ---- 충돌 ----

        [Test]
        public void SameComponentNameInTwoModsDisablesBoth()
        {
            Mod("a", schema: "component(\"Morale\")\n");
            Mod("b", schema: "component(\"Morale\")\n");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EqualTo(new[] { (Codes.SchDuplicateName, "a"), (Codes.SchDuplicateName, "b") }));
            Assert.That(diags[0].Message, Does.Contain("(a, b)"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void TwoModsExtendingWithTheSameNameAreBothErrors()
        {
            Mod("core", schema: "deftype(\"Resource\")\n");
            Mod("a", schema: "extend(\"Resource\", trade_value=const(fixed, default=0))\n", extra: ", dependencies=[\"core\"]");
            Mod("b", schema: "extend(\"Resource\", trade_value=const(int, default=0))\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.SchMemberConflict));
            Assert.That(diags.Select(d => d.Mod), Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(diags[0].Message, Does.Contain("'a'").And.Contain("'b'"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(result.Schema.TryGetType("Resource", out var r), Is.True);
            Assert.That(r.Consts, Is.Empty);
        }

        // 충돌 상대가 어떤 이유로 실패했든 결과가 같아야 한다 (G28)
        [TestCase("deftype(\"T\"\n")]                                       // 구문 오류: 실행 전에 실패
        [TestCase("component(\"Morale\", x=ref(\"Nope\"))\n")]               // 이름 풀이 오류: 같은 회차에 실패
        public void ConflictPartnerThatFailsOnItsOwnDoesNotTakeTheOtherModDown(string extraSchema)
        {
            Mod("core", schema: "deftype(\"Resource\")\n", defs: "Resource(\"Food\")\n");
            Mod("a", schema: "extend(\"Resource\", trade_value=const(fixed, default=0))\n" + extraSchema,
                defs: "Resource(\"Gold\")\n", extra: ", dependencies=[\"core\"]");
            Mod("b", schema: "extend(\"Resource\", trade_value=const(int, default=0))\n",
                defs: "Resource(\"Gold\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a" }));
            Assert.That(diags.Select(d => d.Mod), Has.All.EqualTo("a"));
            Assert.That(result.Defs.TryGet("Gold", out var gold), Is.True);
            Assert.That(gold.Mod, Is.EqualTo("b"));
        }

        [Test]
        public void ExtendingWithANameTheTypeAlreadyHasBlamesOnlyTheExtender()
        {
            Mod("core", schema: "deftype(\"Resource\", value=const(int))\n");
            Mod("a", schema: "extend(\"Resource\", value=const(int))\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Single().Mod, Is.EqualTo("a"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a" }));
        }

        [Test]
        public void InverseCollidingWithAComponentNameIsAnError()
        {
            Mod("core", schema: "deftype(\"Sector\", components=[\"Units\"])\ncomponent(\"Units\")\n" +
                                "component(\"Location\", at=ref(\"Sector\", inverse=\"Units\"))\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchMemberConflict));
        }

        // ---- 선언 모양 ----

        [TestCase("deftype(\"Unit\", hp=field(int))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", tags=field(list[str]))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", hp=const(int, merge=\"append\"))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", hp=const(str, min=1))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", hp=field(int, max=\"mx\"), mx=derived(int, lambda c: 5))\n", Codes.EvlType)]
        [TestCase("def cap(c: C) -> int:\n    return 5\ncomponent(\"C\", hp=field(int, max=cap))\n", Codes.EvlType)]
        [TestCase("component(\"C\", hp=field(int, defualt=1))\n", Codes.EvlArguments)]
        [TestCase("component(\"C\", hp=field(int, default_from=\"mx\"))\n", Codes.SchUnknownName)]
        [TestCase("component(\"C\", a=field(int, default_from=\"b\"), b=field(int, default=1))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", hp=3)\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"override\", v=field(int))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"Lock\", key=field(int))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"Lock\", keys=field(int))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\", components=const(int))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\")\nextend(\"T\", components=const(int))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", f=field(Cond[\"X\"]))\n", Codes.SchBadDeclaration)]
        [TestCase("component(\"C\", x=ref(\"Nope\"))\n", Codes.SchUnknownName)]
        [TestCase("component(\"C\", x=ref(\"T\", order_by=\"pos\"))\ndeftype(\"T\")\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\", parent=const(str))\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"my-type\")\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"int\")\n", Codes.SchBadDeclaration)]
        [TestCase("deftype(\"T\", n=const(DefId[Unit]))\n", Codes.EvlUnknownName)]
        [TestCase("component(\"C\", m=derived(int, later))\ndef later(c: C) -> int:\n    return 1\n", Codes.EvlUnknownName)]
        [TestCase("x = 1\nx = 2\n", Codes.EvlNameTaken)]
        [TestCase("component = 1\ncomponent(\"C\")\n", Codes.EvlType)] // 덮어쓴 뒤로는 모드의 값이다 (G112)
        [TestCase("deftype(\"T\", n=const(int, default=1 + 2))\n", Codes.EvlUnsupported)]
        public void BadSchemaDeclarations(string schema, string code)
        {
            Mod("core", schema: schema);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code), string.Join("\n", diags));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        // 함수 본문을 읽게 된 뒤에도 최상위 콘텐츠 파일에서는 반복·조건식·return이 로드 오류로 남는다 (spec-gaps G102)
        [TestCase("for x in [1]:\n    pass\n", Codes.EvlUnsupported)]
        [TestCase("T(\"A\", n=1 if True else 2)\n", Codes.EvlUnsupported)]
        [TestCase("return 1\n", Codes.ParSyntax)]
        [TestCase("break\n", Codes.ParSyntax)]
        [TestCase("if mod_loaded(\"other\"):\n    for x in [1]:\n        pass\n", Codes.EvlGuardBody)]
        public void FunctionOnlyStatementsAtTheTopLevelOfADefFileAreErrors(string defs, string code)
        {
            Mod("core", schema: "deftype(\"T\", n=const(int, default=0))\n", defs: defs);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code), string.Join("\n", diags));
        }

        // 로직에서 허용하지 않는 연산자·대입·재귀는 함수를 정의할 때 로드 오류다 (spec-gaps G96·G102)
        [TestCase("def f(h) -> int:\n    return 2 ** 3\n")]
        [TestCase("def f(h) -> int:\n    return 1 | 2\n")]
        [TestCase("def f(h) -> int:\n    return 1 << 2\n")]
        [TestCase("def f(h) -> int:\n    return ~1\n")]
        [TestCase("def f(h) -> int:\n    x = 1\n    x **= 2\n    return x\n")]
        [TestCase("def f(h) -> int:\n    h[0] += 1\n    return 1\n")]
        [TestCase("def f(h) -> int:\n    return f(h)\n")]
        [TestCase("def f(h) -> int:\n    for i in range(3):\n        if i is 3:\n            return 1\n    return 2\n")]
        [TestCase("g = lambda h: h ** 2\n")]
        public void ForbiddenLogicConstructsAreLoadErrors(string schema)
        {
            Mod("core", schema: schema);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.EvlUnsupported), string.Join("\n", diags));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        // ---- 함수 본문의 이름 검사 (spec-gaps G108) ----

        [Test]
        public void TypoInAFunctionBodyIsALoadErrorEvenInABranchThatNeverRuns()
        {
            Mod("core", schema: "BONUS = 3\n" +
                                "def f(h) -> int:\n    total = h\n    if total:\n        return totl + BONUS\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Message, Is.EqualTo("name 'totl' is not defined"));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'total'?"));
            Assert.That(d.Span.Line, Is.EqualTo(5));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void FunctionBodiesSeeParametersLocalsLaterGlobalsAndRuntimeBuiltins()
        {
            // LIMIT은 Def 파일에서, 함수보다 나중에 정의된다. 속성 이름(h.Armor.bonus)은 이름 검사 대상이 아니다
            Mod("core",
                schema: "def f(h) -> int:\n" +
                        "    s = h.Armor.bonus\n" +
                        "    for i in range(3):\n" +
                        "        s += i + later\n" +
                        "    later = 1\n" +
                        "    g = lambda x: (lambda y: x + y + s)(LIMIT)\n" +
                        "    return clamp(g(h), 0, LIMIT) + len(contribs(h, \"max\")) + helper(h)\n" +
                        "def helper(h) -> int:\n    return floor(h / 2)\n",
                defs: "LIMIT = 10\n");
            Load(out var diags);
            Assert.That(diags.Where(d => d.Severity == Severity.Error), Is.Empty, string.Join("\n", diags));
        }

        [TestCase("def f(h) -> int:\n    return field\n", "field")]
        // 컴포넌트 값 생성자(C(hp=1))는 로직에서도 쓰는 이름이다 (G134). Def 생성자는 로드 시점에만 있다
        [TestCase("def f(h) -> int:\n    return T(\"x\")\ndeftype(\"T\")\n", "T")]
        [TestCase("def f(h) -> int:\n    patch.set(\"X\", \"n\", 1)\n    return 1\n", "patch")]
        [TestCase("def f(h) -> int:\n    return int(h)\n", "int")]
        public void LoadTimeNamesInAFunctionBodyAreExplained(string schema, string name)
        {
            Mod("core", schema: schema);
            var result = Load(out var diags);
            Assert.That(diags.Count(x => x.Code == Codes.EvlUnknownName), Is.EqualTo(1), string.Join("\n", diags));
            var d = diags.Single(x => x.Code == Codes.EvlUnknownName);
            Assert.That(d.Message, Is.EqualTo("'" + name + "' is only available while content files load, not inside a logic function"));
            Assert.That(d.Hint, Does.Contain("contribs"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void TopLevelLambdaBodiesAreChecked()
        {
            Mod("core", schema: "component(\"C\", hp=field(int, default=0), m=derived(int, lambda c: c.hp + hpp))\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Message, Is.EqualTo("name 'hpp' is not defined"));
        }

        [Test]
        public void AFailedTopLevelAssignmentDoesNotCascadeIntoFunctionBodies()
        {
            Mod("core", schema: "X = nope\ndef f(h) -> int:\n    return X\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Message), Is.EqualTo(new[] { "unknown name 'nope'" }));
        }

        [Test]
        public void TypeAnnotationsMayUseTheUnionOperator()
        {
            Mod("core", schema: "def f(h: int | None) -> int | None:\n    return 1\n");
            Load(out var diags);
            Assert.That(diags.Where(d => d.Severity == Severity.Error), Is.Empty, string.Join("\n", diags));
        }

        // ---- Def 등록 ----

        private const string UnitSchema =
            "def hmax(h: Health) -> int:\n    return h.base_max\n" +
            "component(\"Health\", base_max=const(int, default=10), max=derived(int, hmax), current=field(int))\n" +
            "deftype(\"Unit\", label=const(TextKey, default=\"\"), tags=const(list[str]), components=[\"Health\"])\n";

        [Test]
        public void UnknownDefFieldSuggestsAName()
        {
            Mod("core", schema: UnitSchema, defs: "Unit(\"Scout\", labl=\"x\")\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.DefUnknownField));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'label'?"));
        }

        [TestCase("Unit(\"Scout\", Health=Health(max=3))\n", Codes.DefBadValue)]
        [TestCase("Unit(\"Scout\", Health={\"a\": Health()})\n", Codes.DefBadValue)]
        [TestCase("Unit(\"Scout\", Health=Health(hp=3))\n", Codes.DefUnknownField)]
        [TestCase("Unit(Health=Health())\n", Codes.EvlArguments)]
        [TestCase("Unit(\"\")\n", Codes.DefBadValue)]
        [TestCase("x = Unit(\"A\")\nUnit(\"B\", label=x)\n", Codes.DefBadValue)]
        [TestCase("x = Unit(\"A\")\nUnit(\"B\", tags=[x])\n", Codes.DefBadValue)]
        [TestCase("x = Unit(\"A\")\nUnit(\"B\", Health=Health(base_max={\"k\": [x]}))\n", Codes.DefBadValue)]
        [TestCase("Unit(\"B\", label=Unit)\n", Codes.DefBadValue)]
        [TestCase("Unit(\"B\", tags=[Health()])\n", Codes.DefBadValue)]
        [TestCase("Unit(\"A\", abstract=1)\n", Codes.EvlType)]
        [TestCase("Unit(\"A\", tags=inherit)\nx = inherit.tags\n", Codes.EvlUnsupported)]
        public void BadDefs(string defs, string code)
        {
            Mod("core", schema: UnitSchema, defs: defs);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code), string.Join("\n", diags));
        }

        [Test]
        public void SchemaAliasesCannotBeDefValues()
        {
            // 같은 모드의 이름 공간은 스키마 파일과 Def 파일이 공유하지만 (G20), 타입은 Def 값이 아니다 (G26)
            Mod("core", schema: UnitSchema + "T = list[str]\n", defs: "Unit(\"B\", tags=T)\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchSchemaInDefFile));
        }

        [Test]
        public void DefKeepsInheritanceMetadataAndDefersInheritExpressions()
        {
            Mod("core", schema: UnitSchema,
                defs: "Unit(\"Base\", abstract=True, tags=[\"a\"])\n" +
                      "Unit(\"Scout\", parent=\"Base\", tags=inherit.tags + [\"fast\"])\n");
            var result = LoadClean();
            Assert.That(result.Defs.TryGet("Base", out var b), Is.True);
            Assert.That(b.IsAbstract, Is.True);
            Assert.That(result.Defs.TryGet("Scout", out var s), Is.True);
            Assert.That(s.Parent!.Value.Name, Is.EqualTo("Base"));
            Assert.That(s.TryGetField("tags", out var tags), Is.True);
            Assert.That(tags.Value, Is.InstanceOf<DeferredValue>());
            Assert.That(s.TryGetField("parent", out _), Is.False);
        }

        [Test]
        public void DuplicateDefIdInOneModIsAnError()
        {
            Mod("core", schema: UnitSchema, defs: "Unit(\"Scout\")\nUnit(\"Scout\")\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.DefDuplicateId, 2)));
            Assert.That(d.Related.Single().Line, Is.EqualTo(1));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void DefIdsAreGlobalAcrossTypes()
        {
            Mod("core", schema: UnitSchema + "deftype(\"Building\")\n", defs: "Unit(\"Barracks\")\nBuilding(\"Barracks\")\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.DefDuplicateId));
        }

        [Test]
        public void RedefiningADependencysDefSuggestsOverride()
        {
            Mod("core", schema: UnitSchema, defs: "Unit(\"Scout\")\n");
            Mod("rebalance", defs: "Unit(\"Scout\", label=\"x\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod), Is.EqualTo((Codes.DefDuplicateId, "rebalance")));
            Assert.That(d.Hint, Is.EqualTo("add override=True to replace it"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "rebalance" }));
        }

        [Test]
        public void RedefiningAnOverriddenDefIsJudgedAgainstTheOriginalOwner()
        {
            // c는 core만 보므로 b의 교체와 무관하게 c 자신의 실수다: b는 살아남는다
            Mod("core", schema: UnitSchema, defs: "Unit(\"Scout\")\n");
            Mod("b", defs: "Unit(\"Scout\", override=True)\n", extra: ", dependencies=[\"core\"]");
            Mod("c", defs: "Unit(\"Scout\")\n", extra: ", dependencies=[\"core\"], loadAfter=[\"b\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod), Is.EqualTo((Codes.DefDuplicateId, "c")));
            Assert.That(d.Message, Does.Contain("mod 'core'"));
            Assert.That(d.Hint, Is.EqualTo("add override=True to replace it"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public void IndependentModsDefiningTheSameIdAreBothDisabled()
        {
            Mod("core", schema: UnitSchema);
            Mod("a", defs: "Unit(\"Scout\")\n", extra: ", dependencies=[\"core\"]");
            Mod("b", defs: "Unit(\"Scout\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Mod), Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void OverrideReplacesTheDefInPlace()
        {
            Mod("core", schema: UnitSchema, defs: "Unit(\"Scout\", label=\"a\")\nUnit(\"Tank\")\n");
            Mod("rebalance", defs: "Unit(\"Scout\", override=True, label=\"b\")\n", extra: ", dependencies=[\"core\"]");
            var result = LoadClean();
            Assert.That(result.Defs.Defs.Select(d => d.Id), Is.EqualTo(new[] { "Scout", "Tank" }));
            var scout = result.Defs.Defs[0];
            Assert.That(scout.Mod, Is.EqualTo("rebalance"));
            Assert.That(scout.Replaced!.Mod, Is.EqualTo("core"));
        }

        [TestCase("Unit(\"Scuot\", override=True)\n", Codes.DefBadOverride)]
        [TestCase("Building(\"Scout\", override=True)\n", Codes.DefBadOverride)]
        public void BadOverrides(string defs, string code)
        {
            Mod("core", schema: UnitSchema + "deftype(\"Building\")\n", defs: "Unit(\"Scout\")\n");
            Mod("rebalance", defs: defs, extra: ", dependencies=[\"core\"]");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(code));
        }

        [Test]
        public void UnorderedDoubleOverrideIsAnErrorButOrderedOneWins()
        {
            Mod("core", schema: UnitSchema, defs: "Unit(\"Scout\")\n");
            Mod("a", defs: "Unit(\"Scout\", override=True, label=\"a\")\n", extra: ", dependencies=[\"core\"]");
            Mod("b", defs: "Unit(\"Scout\", override=True, label=\"b\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.DefBadOverride));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "a", "b" }));

            Write("b/mod.py", "Mod(id=\"b\", dependencies=[\"core\"], loadAfter=[\"a\"])\nuse_all(\"defs/\")\n");
            result = LoadClean();
            Assert.That(result.Defs.Defs.Single().Mod, Is.EqualTo("b"));
        }

        // ---- 실패의 전파 ----

        // x와 y가 같은 이름으로 충돌하고 x에 구문 오류가 있으면, 오류가 어느 단계의 파일에 있든 x만 빠진다 (G28)
        [TestCase("component(\"Foo\", v=field(int))\nx = (\n", null)]
        [TestCase("component(\"Foo\", v=field(int))\n", "x = (\n")]
        public void ConflictPartnerWithASyntaxErrorFailsAloneInEitherStage(string schema, string? defs)
        {
            Mod("x", schema: schema, defs: defs);
            Mod("y", schema: "component(\"Foo\", v=field(int))\n");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Mod), Has.All.EqualTo("x"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "x" }));
        }

        [Test]
        public void ModFailingInSchemaStageDisablesDependentsWithoutNoise()
        {
            Mod("core", schema: "deftype(\"Unit\", components=[\"Nope\"])\n");
            Mod("scouts", defs: "Unit(\"Scout\")\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EquivalentTo(new[] { (Codes.SchUnknownName, "core"), (Codes.ModDependencyFailed, "scouts") }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core", "scouts" }));
            Assert.That(result.Schema.Types, Is.Empty);
        }

        [Test]
        public void ModFailingInDefStageTakesItsExtensionsWithIt()
        {
            Mod("core", schema: "component(\"Health\", hp=field(int))\n" +
                                "component(\"Skill\", key=\"Unit\", level=field(int, default=0))\n" +
                                "deftype(\"Unit\", components=[\"Health\", \"Skill\"])\n",
                defs: "Unit(\"Scout\", Health=Health(hp=3), Skill={\"Scout\": Skill(level=1)})\n");
            Mod("morale", schema: "component(\"Morale\")\nextend(\"Unit\", components=[\"Morale\"])\n",
                defs: "Unit(\"Broken\", nope=1)\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.DefUnknownField));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "morale" }));
            Assert.That(result.Schema.TryGetType("Unit", out var unit), Is.True);
            Assert.That(ComponentsOf(unit), Is.EqualTo(new[] { "Health", "Skill" }));
            Assert.That(result.Schema.TryGetComponent("Morale", out _), Is.False);
            Assert.That(result.Defs.Defs.Select(d => d.Id), Is.EqualTo(new[] { "Scout" }));

            // 남은 Def는 최종 스키마의 객체를 가리켜야 한다 (실패한 모드의 확장이 남은 옛 객체가 아니라)
            var scout = result.Defs.Defs.Single();
            Assert.That(scout.Type, Is.SameAs(unit));
            Assert.That(result.Schema.TryGetComponent("Health", out var health), Is.True);
            Assert.That(scout.TryGetField("Health", out var h), Is.True);
            Assert.That(((ComponentValue)h.Value).Component, Is.SameAs(health));
            Assert.That(result.Schema.TryGetComponent("Skill", out var skill), Is.True);
            Assert.That(scout.TryGetField("Skill", out var s), Is.True);
            Assert.That(((ComponentValue)((DictValue)s.Value).Items.Single().Value).Component, Is.SameAs(skill));
            Assert.That(skill.Key, Is.SameAs(unit));
        }

        [Test]
        public void ParseErrorsInContentFilesCarryTheMod()
        {
            Mod("core", schema: "deftype(\"Unit\"\ndef f(:\n    pass\nx = f\n");
            var result = Load(out var diags);
            Assert.That(diags, Is.Not.Empty);
            Assert.That(diags.Select(d => d.Mod), Has.All.EqualTo("core"));
            Assert.That(diags.Select(d => d.Code), Has.None.StartsWith("EVL"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void DiagnosticsAreTheSameOnEveryRun()
        {
            Mod("a", schema: "component(\"Morale\")\ndeftype(\"T\", components=[\"X\"])\n", defs: "T(\"x\", y=1)\n");
            Mod("b", schema: "component(\"Morale\")\n", defs: "Q(\"y\")\n");
            Load(out var first);
            Load(out var second);
            Assert.That(second.Select(d => d.ToString()), Is.EqualTo(first.Select(d => d.ToString())));
        }
    }
}
