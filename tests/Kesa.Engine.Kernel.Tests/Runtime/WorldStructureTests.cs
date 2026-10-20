using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>스크립트의 구조 변경: spawn·destroy·attach·detach·add_row·remove_row와 컴포넌트 값 생성자 (spec S4, spec-gaps G134).</summary>
    public class WorldStructureTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=5, min=0), max=const(int, default=10))\n" +
            "component(\"Wounded\", turns=field(int))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", level=field(int, default=0))\n" +
            "component(\"Opinion\", key=Ref[\"Unit\"], score=field(int, default=0))\n" +
            "component(\"Home\", city=ref(\"City\"))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"City\", components=[\"Health\"])\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Skill\"])\n" +
            "deftype(\"Settler\", components=[\"Health\", \"Home\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nSkillDef(\"Mining\")\n" +
            "City(\"Town\")\n" +
            "Unit(\"Scout\", Skill={\"Cooking\": Skill(level=2)})\n" +
            "Settler(\"Pioneer\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Recruit(at: Ref[City]):\n" +
            "    u = spawn(\"Scout\", Health=Health(current=3))\n" +
            "    u.Health.current += 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Settle(at: Ref[City]):\n" +
            "    spawn(\"Pioneer\", Home=Home(city=at))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def SettleNowhere(at: Ref[City]):\n" +
            "    spawn(\"Pioneer\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Disband(unit: Ref[Unit]):\n" +
            "    destroy(unit)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Wound(unit: Ref[Unit]):\n" +
            "    attach(unit, Wounded(turns=3))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Heal(unit: Ref[Unit]):\n" +
            "    detach(unit.Wounded)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def DetachDefComponent(unit: Ref[Unit]):\n" +
            "    detach(unit.Health)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Learn(unit: Ref[Unit], skill: DefId[SkillDef]):\n" +
            "    add_row(unit.Skill, skill, Skill(level=4))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def LearnPlain(unit: Ref[Unit], skill: DefId[SkillDef]):\n" +
            "    add_row(unit.Skill, skill)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Forget(unit: Ref[Unit], skill: DefId[SkillDef]):\n" +
            "    remove_row(unit.Skill, skill)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Befriend(unit: Ref[Unit], other: Ref[Unit]):\n" +
            "    attach(unit, Opinion())\n" +
            "    add_row(unit.Opinion, other, Opinion(score=7))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Typo(unit: Ref[Unit]):\n" +
            "    attach(unit, Wounded(turnz=3))\n" +
            "@command(free=True, acts_for=None)\n" +
            // 아래 셋은 spawn을 지역 변수에 담아 로드 시점 판정(G135)을 피한다: 실행 중 안전망이 막는다
            "def InExpression(unit: Ref[Unit]):\n" +
            "    g = spawn\n" +
            "    n = [g(\"Scout\")]\n" +
            "@command(free=True, acts_for=None)\n" +
            "def InPreconditions(unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        g = spawn\n" +
            "        u = g(\"Scout\")\n" +
            "    pass\n" +
            "@command(free=True, acts_for=None)\n" +
            "def ViaPure(unit: Ref[Unit]):\n" +
            "    g = spawn\n" +
            "    f = lambda: g(\"Scout\")\n" +
            "    f()\n" +
            "def make_scout(hp):\n" +
            "    u = spawn(\"Scout\")\n" +
            "    u.Health.current = hp\n" +
            "    return u\n" +
            "def wound(u):\n" +
            "    attach(u, Wounded(turns=1))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Draft(at: Ref[City]):\n" +
            "    u = make_scout(1)\n" +
            "    wound(u)\n";

        private World _world = null!;
        private EntityId _town, _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _town = _world.Create("banking:Town");
            _scout = _world.Create("banking:Scout");
        }

        private SubmitResult Submit(string command, string param, Value arg, Dictionary<string, Value>? extra = null)
        {
            var args = new Dictionary<string, Value> { [param] = arg };
            if (extra != null)
                foreach (var pair in extra) args[pair.Key] = pair.Value;
            return _world.Submit("host", "banking." + command, args);
        }

        private SubmitResult OnScout(string command, Dictionary<string, Value>? extra = null) =>
            Submit(command, "unit", new EntityValue(_scout), extra);

        private static Dictionary<string, Value> Skill(string id) => new Dictionary<string, Value> { ["skill"] = new StrValue(id) };

        private long Int(EntityId id, string component, string member) =>
            ((IntValue)_world.Get(id, _world.Member(component, member))).Value;

        [Test]
        public void SpawnCreatesAnEntityWithTheGivenValuesAndTheBodyCanWriteIt()
        {
            var r = Submit("Recruit", "at", new EntityValue(_town));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded), r.Error?.Message);
            var units = _world.All("Unit");
            Assert.That(units, Has.Count.EqualTo(2));
            Assert.That(Int(units[1], "Health", "current"), Is.EqualTo(4)); // 3 + 1
            Assert.That(_world.DefOf(units[1]), Is.EqualTo("banking:Scout"));
        }

        [Test]
        public void SpawnFillsARequiredRefFromTheComponentValue()
        {
            Assert.That(Submit("Settle", "at", new EntityValue(_town)).Succeeded, Is.True);
            var settler = _world.All("Settler").Single();
            Assert.That(((EntityValue)_world.Get(settler, _world.Member("Home", "city"))).Id, Is.EqualTo(_town));
        }

        [Test]
        public void SpawnWithoutARequiredRefIsAScriptError()
        {
            var r = Submit("SettleNowhere", "at", new EntityValue(_town));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("Home.city"));
        }

        [Test]
        public void DestroyRemovesTheEntity()
        {
            Assert.That(OnScout("Disband").Succeeded, Is.True);
            Assert.That(_world.IsAlive(_scout), Is.False);
        }

        [Test]
        public void AttachAndDetachARuntimeComponent()
        {
            Assert.That(OnScout("Wound").Succeeded, Is.True);
            Assert.That(_world.Has(_scout, "Wounded"), Is.True);
            Assert.That(Int(_scout, "Wounded", "turns"), Is.EqualTo(3));
            Assert.That(OnScout("Heal").Succeeded, Is.True);
            Assert.That(_world.Has(_scout, "Wounded"), Is.False);
        }

        [Test]
        public void DetachingAMissingComponentSaysItIsNotAttached()
        {
            var r = OnScout("Heal");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNoComponent));
            Assert.That(r.Error.Message, Does.Contain("not attached"));
        }

        [Test]
        public void AComponentFromTheDefCannotBeDetached()
        {
            var r = OnScout("DetachDefComponent");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("cannot be removed"));
        }

        [Test]
        public void AddAndRemoveARow()
        {
            var level = _world.Member("Skill", "level");
            Assert.That(OnScout("Learn", Skill("banking:Mining")).Succeeded, Is.True);
            Assert.That(((IntValue)_world.GetRow(_scout, level, "banking:Mining")).Value, Is.EqualTo(4));
            Assert.That(OnScout("Forget", Skill("banking:Mining")).Succeeded, Is.True);
            Assert.That(_world.HasRow(_scout, "Skill", "banking:Mining"), Is.False);
        }

        [Test]
        public void AddRowWithoutAValueUsesTheDefaults()
        {
            Assert.That(OnScout("LearnPlain", Skill("banking:Mining")).Succeeded, Is.True);
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Skill", "level"), "banking:Mining")).Value, Is.EqualTo(0));
        }

        [Test]
        public void ARowFromTheDefCannotBeRemoved()
        {
            var r = OnScout("Forget", Skill("banking:Cooking"));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(_world.HasRow(_scout, "Skill", "banking:Cooking"), Is.True);
        }

        [Test]
        public void AnEntityKeyedRowIsAddedAfterAttachingTheComponent()
        {
            var other = _world.Create("banking:Scout");
            var r = OnScout("Befriend", new Dictionary<string, Value> { ["other"] = new EntityValue(other) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded), r.Error?.Message);
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Opinion", "score"), other)).Value, Is.EqualTo(7));
        }

        [Test]
        public void AComponentValueWithAnUnknownMemberSuggestsTheRightName()
        {
            var r = OnScout("Typo");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunUnknownName));
            Assert.That(r.Error.Hint, Does.Contain("turns"));
        }

        // ---- 쓰기 자리 (G117): 문장 또는 이름 대입의 오른쪽 전체만 ----

        [Test]
        public void SpawnInsideAnExpressionIsNotAllowed()
        {
            var r = OnScout("InExpression");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
            Assert.That(_world.All("Unit"), Has.Count.EqualTo(1));
        }

        [Test]
        public void SpawnInPreconditionsIsNotAllowed()
        {
            var r = OnScout("InPreconditions");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
            Assert.That(_world.All("Unit"), Has.Count.EqualTo(1));
        }

        [Test]
        public void SpawnThroughALambdaIsNotAllowed()
        {
            var r = OnScout("ViaPure");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunNotWritable));
        }

        // ---- 로드 시점 이름 검사 (G108): 컴포넌트 값 생성자는 로직에서도 쓰는 이름이다 ----

        // ---- G135: 쓰는 내장을 부르면 쓰기 함수, 결과는 이름 대입으로 받는다 ----

        [Test]
        public void AHelperThatSpawnsCanReturnTheEntity()
        {
            var r = Submit("Draft", "at", new EntityValue(_town));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded), r.Error?.Message);
            var drafted = _world.All("Unit")[1];
            Assert.That(Int(drafted, "Health", "current"), Is.EqualTo(1));
            Assert.That(_world.Has(drafted, "Wounded"), Is.True);
        }

        [TestCase("def f(u):\n    n = [spawn(\"Scout\")]\n", "spawn")]
        [TestCase("def f(u):\n    if destroy(u):\n        pass\n", "destroy")]
        [TestCase("g = lambda u: detach(u.Wounded)\n", "detach")]
        [TestCase("@command(free=True, acts_for=None)\ndef F(u: Ref[Unit]):\n    with preconditions:\n" +
                  "        x = spawn(\"Scout\")\n    pass\n", "spawn")]
        public void AWritingBuiltinInsideAnExpressionIsALoadError(string logic, string name)
        {
            var d = LoadWithLogic(logic).Single(x => x.Code == Codes.CmdWriteEffect);
            Assert.That(d.Message, Is.EqualTo(name + "() changes the world, so it can only be called as a statement"));
        }

        [Test]
        public void AFunctionThatOnlyCallsAWritingBuiltinCannotBeADerivedFunction()
        {
            Mod("banking", schema: Schema +
                "def bad(h):\n    attach(h.entity, Wounded(turns=1))\n    return 1\n" +
                "component(\"Morale\", value=derived(int, bad))\n", defs: Defs);
            Load(out var diagnostics);
            var d = diagnostics.Single(x => x.Code == Codes.CmdWriteEffect);
            Assert.That(d.Message, Does.Contain("'bad', which changes the world"));
        }

        [Test]
        public void AShadowedWritingBuiltinIsAPlainFunction()
        {
            var diags = LoadWithLogic("def attach(a, b):\n    return a\ndef f(u):\n    return [attach(u, 1)]\n");
            Assert.That(diags.Where(x => x.Code == Codes.CmdWriteEffect), Is.Empty, string.Join("\n", diags));
        }

        // ---- ⑤ 링크: spawn에 리터럴로 적은 Def id ----

        private IReadOnlyList<Diagnostic> LoadWithLogic(string logic)
        {
            Mod("banking", schema: Schema, defs: Defs + logic);
            Load(out var diagnostics);
            return diagnostics;
        }

        [Test]
        public void ASpawnedDefIdTypoIsALoadErrorWithASuggestion()
        {
            var d = LoadWithLogic("def f():\n    spawn(\"Scoutt\")\n").Single(x => x.Code == Codes.LnkNotFound);
            Assert.That(d.Message, Is.EqualTo("there is no Def 'banking:Scoutt'"));
            Assert.That(d.Hint, Does.Contain("Scout"));
            Assert.That(d.Span.Line, Is.GreaterThan(0));
        }

        [Test]
        public void ADataOnlyDefCannotBeSpawned()
        {
            var d = LoadWithLogic("def f():\n    spawn(\"Cooking\")\n").Single(x => x.Code == Codes.LnkWrongType);
            Assert.That(d.Message, Does.Contain("cannot be spawned"));
        }

        [Test]
        public void AnAbstractDefCannotBeSpawned()
        {
            var diags = LoadWithLogic("Unit(\"Template\", abstract=True)\ndef f():\n    u = spawn(\"Template\")\n");
            Assert.That(diags.Select(x => x.Code), Has.Member(Codes.LnkAbstract));
        }

        [Test]
        public void ASpawnedDefOfAModThatIsNotADependencyIsNotVisible()
        {
            Mod("banking", schema: Schema, defs: Defs);
            Mod("scouts", defs: "def f():\n    spawn(\"banking:Scout\")\n");
            Load(out var diagnostics);
            Assert.That(diagnostics.Any(x => x.Code == Codes.LnkNotVisible && x.Mod == "scouts"), Is.True,
                string.Join("\n", diagnostics));
        }

        [Test]
        public void AShadowedSpawnIsNotLinked()
        {
            var diags = LoadWithLogic("def spawn(x):\n    return x\ndef f():\n    return spawn(\"Scuot\")\n");
            Assert.That(diags.Where(x => x.Code == Codes.LnkNotFound), Is.Empty);
        }

        [Test]
        public void AComponentOfAModThatIsNotADependencyIsReportedAsNotVisible()
        {
            // 컴포넌트 값 생성자가 로직의 이름으로 통과하는 것은 SetUp의 LoadClean이 확인한다
            Mod("scouts", defs: "def f(u):\n    attach(u, Wounded(turns=1))\n");
            Load(out var diagnostics);
            Assert.That(diagnostics.Any(d => d.Code == Codes.SchNotVisible && d.Message.Contains("Wounded")), Is.True,
                string.Join("\n", diagnostics));
        }
    }
}
