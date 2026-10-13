using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// ③ 패치 단계의 대상 검사 (spec-gaps G38, G43)와, ③에서 실패한 모드를 빼고 ②a부터 다시 돌리는 규칙 (G49).
    /// </summary>
    public class PatchTargetTests : LoaderTestBase
    {
        private const string CoreDep = ", dependencies=[\"core\"]";

        private void Core() =>
            Mod("core",
                schema: "component(\"Health\", hp=field(int, default=10))\n" +
                        "deftype(\"Unit\", components=[\"Health\"])\n",
                defs: "Unit(\"Scout\")\nUnit(\"Base\", abstract=True)\n");

        private static string Patch(string target) => "patch.set(\"" + target + "\", \"x\", 1)\n";

        // ---- G38: 대상 ----

        [Test]
        public void OwnDependencyAndTransitiveDependencyDefsAreVisible()
        {
            Core();
            Mod("mid", defs: "Unit(\"Mid\")\n", extra: CoreDep);
            Mod("top", defs: "Unit(\"Top\")\n" + Patch("Top") + Patch("Mid") + Patch("Scout"), extra: ", dependencies=[\"mid\"]");
            var result = LoadClean();
            Assert.That(result.Patches.Select(p => p.TargetId), Is.EqualTo(new[] { "Top", "Mid", "Scout" }));
        }

        [Test]
        public void AbstractDefsCanBePatched()
        {
            Core();
            Mod("compat", defs: Patch("Base"), extra: CoreDep);
            Assert.That(LoadClean().Patches.Single().TargetId, Is.EqualTo("Base"));
        }

        [Test]
        public void MissingTargetIsAnErrorWithASuggestion()
        {
            Core();
            Mod("compat", defs: Patch("Scoot"), extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.File, d.Line), Is.EqualTo((Codes.PatTargetNotFound, "compat", "compat/defs/defs.py", 1)));
            Assert.That(d.Hint, Does.Contain("Scout"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
            Assert.That(result.Patches, Is.Empty);
        }

        [Test]
        public void ADefOfAModThatIsNotADependencyIsNotVisible()
        {
            Core();
            Mod("compat", defs: Patch("Scout"));
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.PatTargetNotVisible));
            Assert.That(d.Hint, Does.Contain("\"core\""));
            Assert.That(d.Related, Has.Count.EqualTo(1));
        }

        [Test]
        public void LoadAfterDoesNotMakeADefVisible()
        {
            Core();
            Mod("compat", defs: Patch("Scout"), extra: ", loadAfter=[\"core\"]");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.PatTargetNotVisible));
        }

        [Test]
        public void DefsOfAnOptionalModNeedItsGuard()
        {
            Core();
            Mod("compat", defs: Patch("Scout"), extra: ", optional=[\"core\"]");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.PatNeedsGuard));
            Assert.That(d.Hint, Does.Contain("mod_loaded(\"core\")"));
        }

        [Test]
        public void GuardedPatchOfAnOptionalModDefIsAllowed()
        {
            Core();
            Mod("compat", defs: "if mod_loaded(\"core\"):\n    " + Patch("Scout"), extra: ", optional=[\"core\"]");
            var result = LoadClean();
            Assert.That(result.Patches.Single().Guards, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void AGuardForAnotherModDoesNotCover()
        {
            Core();
            Mod("scouts", defs: "\n");
            Mod("compat", defs: "if mod_loaded(\"scouts\"):\n    " + Patch("Scout"), extra: ", optional=[\"core\", \"scouts\"]");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.PatNeedsGuard));
        }

        // ---- G43: override와 패치 ----

        [Test]
        public void ReplacedDefIsPatchedThroughEitherOwner()
        {
            Core();
            Mod("tweak", defs: "Unit(\"Scout\", override=True)\n", extra: CoreDep);
            Mod("compat", defs: Patch("Scout"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Defs.TryGet("Scout", out var scout), Is.True);
            Assert.That(scout.Mod, Is.EqualTo("tweak"));
            Assert.That(result.Patches.Single().Mod, Is.EqualTo("compat"));
        }

        // ---- G49: ③에서 실패한 모드는 빼고 ②a부터 다시 ----

        [Test]
        public void GuardsAreEvaluatedAgainWithoutAModThatFailedAtThePatchStage()
        {
            Core();
            Mod("banking", defs: Patch("Zzz"), extra: CoreDep);
            Mod("compat", defs: "if mod_loaded(\"banking\"):\n    " + Patch("Scout"),
                extra: ", dependencies=[\"core\"], optional=[\"banking\"]");
            var result = Load(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
            Assert.That(result.Mods.Select(m => m.Id), Is.EqualTo(new[] { "core", "compat" }));
            // 첫 회차에서는 banking이 로드된 것으로 보여 기록했던 compat의 패치가 사라진다
            Assert.That(result.Patches, Is.Empty);
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EqualTo(new[] { (Codes.PatTargetNotFound, "banking"), (Codes.PatSkipped, "compat") }));
        }

        [Test]
        public void ExtensionsOfAModThatFailedAtThePatchStageLeaveTheSchema()
        {
            Core();
            Mod("morale",
                schema: "component(\"Morale\", value=field(int, default=5))\nextend(\"Unit\", components=[\"Morale\"])\n",
                defs: Patch("Zzz"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.PatTargetNotFound));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "morale" }));
            Assert.That(result.Schema.TryGetType("Unit", out var unit), Is.True);
            Assert.That(unit.Components.Select(c => c.Component.Name), Is.EqualTo(new[] { "Health" }));
            Assert.That(result.Schema.TryGetComponent("Morale", out _), Is.False);
        }

        [Test]
        public void DependentsOfAModThatFailedAtThePatchStageAreDisabledOnce()
        {
            Core();
            Mod("morale", defs: Patch("Zzz"), extra: CoreDep);
            Mod("dep", defs: "Unit(\"Dep\")\n", extra: ", dependencies=[\"core\", \"morale\"]");
            Mod("fine", defs: Patch("Scout"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "dep", "morale" }));
            Assert.That(result.Mods.Select(m => m.Id), Is.EqualTo(new[] { "core", "fine" }));
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EquivalentTo(new[] { (Codes.PatTargetNotFound, "morale"), (Codes.ModDependencyFailed, "dep") }));
        }

        [Test]
        public void IndependentFailuresAreAllExcludedAndReportedOnce()
        {
            Core();
            Mod("bad_one", defs: Patch("Zzz"), extra: CoreDep);
            Mod("bad_two", defs: Patch("Yyy"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "bad_one", "bad_two" }));
            Assert.That(diags.Select(d => d.Mod), Is.EquivalentTo(new[] { "bad_one", "bad_two" }));
        }

        [Test]
        public void FailuresBeforeAndAtThePatchStageAreEachReportedOnce()
        {
            Core();
            Mod("early", defs: "Nope()\n", extra: CoreDep);
            Mod("late", defs: Patch("Zzz"), extra: CoreDep);
            Mod("fine", defs: Patch("Scout"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "early", "late" }));
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EquivalentTo(new[] { (Codes.EvlUnknownName, "early"), (Codes.PatTargetNotFound, "late") }));
            Assert.That(result.Patches.Select(p => p.Mod), Is.EqualTo(new[] { "fine" }));
        }

        [Test]
        public void ResultIsTheSameAsLoadingWithoutTheFailedModAtAll()
        {
            // 재시작 결과가 처음부터 그 모드가 없던 것과 같다는 기준 (G49의 "증분 처리" 대비)
            Core();
            Mod("morale",
                schema: "component(\"Morale\", value=field(int, default=5))\nextend(\"Unit\", components=[\"Morale\"])\n",
                defs: Patch("Zzz"), extra: CoreDep);
            Mod("fine", defs: "if mod_loaded(\"morale\"):\n    " + Patch("Scout") + "    " + Patch("Scout"),
                extra: ", dependencies=[\"core\"], optional=[\"morale\"]");
            var withFailed = Load(out var diagsWith);

            System.IO.Directory.Delete(System.IO.Path.Combine(_root, "morale"), true);
            var without = Load(out var diagsWithout);

            Assert.That(withFailed.Mods.Select(m => m.Id), Is.EqualTo(without.Mods.Select(m => m.Id)));
            Assert.That(withFailed.Patches, Is.Empty);
            Assert.That(without.Patches, Is.Empty);
            // 실패한 모드의 오류만 더 있고, 나머지 진단은 같다
            Assert.That(diagsWith.Where(d => d.Mod != "morale").Select(d => d.ToString()),
                Is.EqualTo(diagsWithout.Select(d => d.ToString())));
        }
    }
}
