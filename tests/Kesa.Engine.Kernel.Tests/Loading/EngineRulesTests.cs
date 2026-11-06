using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>게임 전체 상한은 엔진 Def <c>engine:Rules</c>이고 모드가 패치로 바꾼다 (G451).</summary>
    public class EngineRulesTests : LoaderTestBase
    {
        private static long Limit(ContentLoadResult content, string name)
        {
            Assert.That(content.Frozen.TryGet("engine:Rules", out var rules), Is.True);
            Assert.That(rules.Consts.TryGet(name, out var v), Is.True);
            return ((IntValue)v.Value).Value;
        }

        private static IEnumerable<string> Codes(IReadOnlyList<Diagnostic> diags) => diags.Select(d => d.Code + " " + d.Message);

        [Test]
        public void TheEngineGivesTheDefaults()
        {
            Mod("core", defs: "");
            var content = LoadClean();
            Assert.That(Limit(content, "step_budget"), Is.EqualTo(100_000));
            Assert.That(Limit(content, "action_step_budget"), Is.EqualTo(10_000_000));
            Assert.That(Limit(content, "heavy_warning_steps"), Is.EqualTo(100_000));
            Assert.That(Limit(content, "max_signal_depth"), Is.EqualTo(64));
            Assert.That(Limit(content, "max_advance_per_call"), Is.EqualTo(10_000));
            Assert.That(Limit(content, "max_settle_rounds"), Is.EqualTo(64));
            Assert.That(Limit(content, "max_call_depth"), Is.EqualTo(200));
        }

        // 호출 한 단이 C# 스택을 쓰므로 천장 위로는 못 올린다 (G452)
        [Test]
        public void TheCallDepthHasACeiling()
        {
            Mod("core", defs: Rule("max_call_depth", 300));
            Assert.That(Limit(LoadClean(), "max_call_depth"), Is.EqualTo(300));
            Mod("core", defs: Rule("max_call_depth", 301));
            Load(out var diags);
            Assert.That(Codes(diags), Has.Some.StartWith(Kesa.Engine.Diagnostics.Codes.ValRange));
        }

        [Test]
        public void AModChangesALimitWithAPatch()
        {
            Mod("core", defs: Rule("max_signal_depth", 200) + "patch.add(\"engine:Rules\", \"step_budget\", 5)\n");
            var content = LoadClean();
            Assert.That(Limit(content, "max_signal_depth"), Is.EqualTo(200));
            Assert.That(Limit(content, "step_budget"), Is.EqualTo(100_005));
        }

        [Test]
        public void ALimitBelowOneFailsThePatchingMod()
        {
            Mod("core", defs: Rule("max_settle_rounds", 0));
            Load(out var diags);
            Assert.That(Codes(diags), Has.Some.StartWith(Kesa.Engine.Diagnostics.Codes.ValRange));
            Assert.That(diags.Select(d => d.Mod), Has.All.EqualTo("core"));
        }

        [Test]
        public void TwoModsWithoutAnOrderCannotSetTheSameLimit()
        {
            Mod("banking", defs: Rule("max_signal_depth", 10));
            Mod("scouts", defs: Rule("max_signal_depth", 20));
            Load(out var diags);
            Assert.That(diags.Where(d => d.Severity == Severity.Error).Select(d => d.Mod).Distinct(),
                Is.EquivalentTo(new[] { "banking", "scouts" }));
        }

        [Test]
        public void TheLaterModWinsWhenOrdered()
        {
            Mod("banking", defs: Rule("max_signal_depth", 10));
            Mod("scouts", defs: Rule("max_signal_depth", 20), extra: ", loadAfter=[\"banking\"]");
            var content = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Kesa.Engine.Diagnostics.Codes.PatOverridden }), "only the note that it replaces");
            Assert.That(Limit(content, "max_signal_depth"), Is.EqualTo(20));
        }

        [Test]
        public void AModCannotDeclareTheEngineType()
        {
            Mod("core", schema: "deftype(\"EngineRules\")\n");
            Load(out var diags);
            Assert.That(Codes(diags), Has.Some.Contains("'EngineRules' is a Def type declared by the engine"));
        }

        [Test]
        public void AModCannotMakeADefOfTheEngineType()
        {
            Mod("core", defs: "EngineRules(\"Mine\", step_budget=5)\n");
            Load(out var diags);
            Assert.That(Codes(diags), Has.Some.Contains("mods cannot make Defs of the engine's type 'EngineRules'"));
        }
    }
}
