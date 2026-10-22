using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 여러 모드의 보기 규칙 <c>@view_rule(대상)</c> (spec-gaps G315): 대상마다 규칙을 걸고, 나중에 로드된 모드부터 물어 처음 나온
    /// <c>True</c>/<c>False</c>를 쓰고, <c>None</c>은 앞 규칙에 맡긴다. 같은 대상에 순서 없는 두 모드가 규칙을 걸면 로드 오류다.
    /// </summary>
    public class ViewRuleTests : LoaderTestBase
    {
        // 코어: 손패는 자기만 보고, 덮인 카드는 앞면을 아무도 못 본다
        private const string CoreSchema =
            "component(\"Hand\", cards=field(int, default=0))\n" +
            "component(\"Face\", n=field(int, default=0), up=field(bool, default=False))\n" +
            "component(\"Fog\", hidden=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Hand\"])\n" +
            "deftype(\"Card\", components=[\"Face\"])\n" +
            "deftype(\"Unit\", components=[\"Fog\"])\n" +
            "signal(\"Ping\")\n";

        private const string CoreDefs =
            "Party(\"Side\")\nCard(\"Back\")\nUnit(\"Scout\")\n" +
            "@view_rule(Hand)\ndef own_hand(viewer, entity):\n    return viewer == entity\n" +
            "@view_rule(Face)\ndef face_down(viewer, entity):\n    return entity.Face.up\n" +
            "@view_rule(Unit)\ndef fog(viewer, entity):\n    return not entity.Fog.hidden\n";

        private const string DependsOnCore = ", dependencies=[\"core\"]";

        private World _w = null!;
        private EntityId _a, _b, _card, _scout;

        private void Open(params (string Id, string? Schema, string Defs, string Extra)[] mods)
        {
            Mod("core", schema: CoreSchema, defs: CoreDefs);
            foreach (var m in mods) Mod(m.Id, schema: m.Schema, defs: m.Defs, extra: m.Extra);
            _w = new World(LoadClean());
            _a = _w.Create("core:Side");
            _b = _w.Create("core:Side");
            _w.Seat(_a, "A");
            _w.Seat(_b, "B");
            _w.Set(_a, _w.Member("Hand", "cards"), new IntValue(3));
            _card = _w.Create("core:Back");
            _scout = _w.Create("core:Scout");
            _w.Set(_scout, _w.Member("Fog", "hidden"), BoolValue.True);
        }

        private bool SeesHand(string conn, EntityId party) => _w.ViewAs(conn).Has(party, "Hand");

        // ---- 한 모드 ----

        [Test]
        public void EachRuleDecidesOnlyItsTarget()
        {
            Open();
            Assert.That(SeesHand("A", _a), Is.True);
            Assert.That(SeesHand("B", _a), Is.False);
            Assert.That(_w.ViewAs("A").Has(_card, "Face"), Is.False, "face down");
            Assert.That(_w.ViewAs("A").All("Card"), Is.EqualTo(new[] { _card }), "the card itself is seen: no rule on Card");
            Assert.That(_w.ViewAs("A").All("Unit"), Is.Empty, "the fogged scout is hidden at the entity layer");
            Assert.That(_w.ViewAs("A").All("Party"), Is.EqualTo(new[] { _a, _b }), "the rule on Unit does not touch parties");
        }

        // ---- 숨기기 더하기 (1) ----

        [Test]
        public void AnExpansionHidesItsOwnComponentWithoutTouchingTheCore()
        {
            // 확장이 카드에 제 컴포넌트를 붙여 덮인 동안 숨긴다. 코어의 앞면 규칙은 그대로다 (판데믹 brink의 변종 에픽 카드)
            Open(("strains", "component(\"Strain\", effect=field(int, default=0))\nextend(\"Card\", components=[\"Strain\"])\n",
                "@view_rule(Strain)\ndef strain_down(viewer, entity):\n    return entity.Face.up\n", DependsOnCore));
            var a = _w.ViewAs("A");
            Assert.That(a.Has(_card, "Strain"), Is.False);
            Assert.That(a.Has(_card, "Face"), Is.False);
            _w.Set(_card, _w.Member("Face", "up"), BoolValue.True);
            Assert.That(_w.ViewAs("A").Has(_card, "Strain"), Is.True);
            Assert.That(_w.ViewAs("A").Has(_card, "Face"), Is.True);
        }

        // ---- 보이게 하기 (2)와 맡기기 ----

        private const string SpyDefs =
            "@view_rule(Hand)\ndef spy(viewer, entity):\n" +
            "    if viewer is not None and viewer.Hand.cards >= 100:\n        return True\n" +
            "    return None\n";

        [Test]
        public void ALaterRuleCanShowWhatAnEarlierOneHidesAndLeavesTheRestToIt()
        {
            Open(("spies", null, SpyDefs, DependsOnCore));
            Assert.That(SeesHand("B", _a), Is.False, "None: the core's rule decides");
            Assert.That(SeesHand("A", _a), Is.True);
            _w.Set(_b, _w.Member("Hand", "cards"), new IntValue(100));
            Assert.That(SeesHand("B", _a), Is.True, "the spy rule answers first");
            Assert.That(_w.ViewAs("B").Diagnostics, Is.Empty);
        }

        [Test]
        public void WhenEveryRuleSaysNoneItIsSeen()
        {
            Mod("core", schema: CoreSchema, defs: "Party(\"Side\")\n@view_rule(Hand)\ndef shrug(viewer, entity):\n    return None\n");
            _w = new World(LoadClean());
            var p = _w.Create("core:Side");
            Assert.That(_w.ViewAs("X").Has(p, "Hand"), Is.True);
        }

        // ---- 다시 숨기기 (3): 층이 셋 ----

        [Test]
        public void ThreeLayersAreAskedFromTheLastLoaded()
        {
            // 코어: 안개 속이면 안 보임. 탐지: 탐지기가 있으면 보임. 은신(탐지 뒤에 로드): 숨은 유닛은 탐지기로도 안 보임
            Open(("detect", "component(\"Detector\", on=field(bool, default=False))\nextend(\"Party\", components=[\"Detector\"])\n",
                    "@view_rule(Unit)\ndef detector_sees(viewer, entity):\n" +
                    "    return True if viewer is not None and viewer.Detector.on else None\n", DependsOnCore),
                ("stealth", "component(\"Stealth\", on=field(bool, default=False))\nextend(\"Unit\", components=[\"Stealth\"])\n",
                    "@view_rule(Unit)\ndef hidden_unit(viewer, entity):\n    return False if entity.Stealth.on else None\n",
                    ", dependencies=[\"core\"], loadAfter=[\"detect\"]"));
            Assert.That(_w.ViewAs("A").All("Unit"), Is.Empty, "fog");
            _w.Set(_a, _w.Member("Detector", "on"), BoolValue.True);
            Assert.That(_w.ViewAs("A").All("Unit"), Is.EqualTo(new[] { _scout }), "the detector sees through the fog");
            _w.Set(_scout, _w.Member("Stealth", "on"), BoolValue.True);
            Assert.That(_w.ViewAs("A").All("Unit"), Is.Empty, "stealth is asked before the detector");
        }

        // ---- 모드 사이의 순서 ----

        [Test]
        public void TwoUnorderedModsOnTheSameTargetBothFail()
        {
            Mod("core", schema: CoreSchema, defs: CoreDefs);
            Mod("spies", defs: SpyDefs, extra: DependsOnCore);
            Mod("thieves", defs: "@view_rule(Hand)\ndef peek(viewer, entity):\n    return None\n", extra: DependsOnCore);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { Codes.CmdViewPolicy }));
            Assert.That(diags.First().Message, Is.EqualTo(
                "mods 'spies' and 'thieves' both put a view rule on 'Hand', but have no load order between them, so it is not clear which is asked first"));
            Assert.That(diags.First().Hint, Does.Contain("loadAfter"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "spies", "thieves" }));
            Assert.That(result.ViewRules.Select(r => r.Mod), Is.EqualTo(new[] { "core", "core", "core" }));
        }

        [Test]
        public void UnorderedModsOnDifferentTargetsAreFine()
        {
            Mod("core", schema: CoreSchema, defs: CoreDefs);
            Mod("spies", defs: SpyDefs, extra: DependsOnCore);
            Mod("thieves", defs: "@view_rule(Face)\ndef peek(viewer, entity):\n    return None\n", extra: DependsOnCore);
            Assert.That(LoadClean().ViewRules.Select(r => r.Mod + " " + r.Target), Is.EqualTo(new[]
            {
                "core Hand", "core Face", "core Unit", "spies Hand", "thieves Face",
            }));
        }

        [Test]
        public void LoadAfterIsEnoughOrder()
        {
            Mod("core", schema: CoreSchema, defs: CoreDefs);
            Mod("spies", defs: SpyDefs, extra: DependsOnCore);
            Mod("thieves", defs: "@view_rule(Hand)\ndef peek(viewer, entity):\n    return None\n",
                extra: ", dependencies=[\"core\"], loadAfter=[\"spies\"]");
            LoadClean();
        }

        // ---- 대상 검사 ----

        [TestCase("@view_rule(Hnad)\ndef f(viewer, entity):\n    return None\n", Codes.EvlUnknownName,
            "@view_rule: there is no component or Def type 'Hnad'")]
        [TestCase("@view_rule(Ping)\ndef f(viewer, entity):\n    return None\n", Codes.CmdBadDecorator,
            "@view_rule: 'Ping' is a signal; a rule decides a component or a Def type")]
        [TestCase("Hand = 1\n@view_rule(Hand)\ndef f(viewer, entity):\n    return None\n", Codes.CmdBadDecorator,
            "@view_rule: 'Hand' is this mod's own name, not a component or Def type")]
        [TestCase("@view_rule\ndef f(viewer, entity):\n    return None\n", Codes.CmdBadDecorator,
            "@view_rule needs the component or Def type it decides")]
        [TestCase("@view_rule(Hand, Face)\ndef f(viewer, entity):\n    return None\n", Codes.CmdBadDecorator,
            "@view_rule takes one component or Def type")]
        [TestCase("@view_rule(\"Hand\")\ndef f(viewer, entity):\n    return None\n", Codes.CmdBadDecorator,
            "@view_rule needs the bare name of a component or Def type")]
        [TestCase("@view_rule(Hand, override=True)\ndef f(viewer, entity):\n    return None\n", Codes.CmdBadDecorator,
            "@view_rule has no argument 'override'")]
        public void BadTargetsAreLoadErrors(string defs, string code, string message)
        {
            Mod("core", schema: CoreSchema, defs: defs);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code + " " + d.Message), Has.Some.EqualTo(code + " " + message),
                string.Join("\n", diags.Select(d => d.Code + " " + d.Message)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void AMisspelledTargetGetsASuggestion()
        {
            Mod("core", schema: CoreSchema, defs: "@view_rule(Hnad)\ndef f(viewer, entity):\n    return None\n");
            Load(out var diags);
            Assert.That(diags.Single().Hint, Does.Contain("'Hand'"));
        }

        [Test]
        public void ATargetOfAModNotDependedOnIsNotVisible()
        {
            Mod("core", schema: CoreSchema, defs: CoreDefs);
            Mod("spies", defs: SpyDefs, extra: ", loadAfter=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Has.Some.EqualTo(Codes.SchNotVisible));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "spies" }));
        }

        [Test]
        public void TheOverrideHintSaysWhatToDoInstead()
        {
            Mod("core", schema: CoreSchema, defs: "@view_rule(Hand, override=True)\ndef f(viewer, entity):\n    return None\n");
            Load(out var diags);
            Assert.That(diags.Single().Hint, Does.Contain("return None"));
        }

        [Test]
        public void LogicMayCallARuleFunction()
        {
            // 규칙 함수는 커맨드가 아니라 평범한 순수 함수다 (G118의 금지는 커맨드에만)
            Mod("core", schema: CoreSchema, defs: CoreDefs + "def check(v, e):\n    return own_hand(v, e)\n");
            LoadClean();
        }
    }
}
