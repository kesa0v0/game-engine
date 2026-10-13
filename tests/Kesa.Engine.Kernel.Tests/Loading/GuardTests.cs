using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary><c>if mod_loaded("m"):</c> 가드 (spec-gaps G46)와 건너뛴 패치 알림 (G38).</summary>
    public class GuardTests : LoaderTestBase
    {
        private static string PatchLine(string path) => "    patch.set(\"A\", \"" + path + "\", 1)\n";

        // 이름이 서로 비슷하면 ModSorter가 "혹시 …?" 경고를 내므로(G9) 일부러 다른 이름을 쓴다
        private static string ThreeWay =>
            "if mod_loaded(\"banking\"):\n" + PatchLine("b") +
            "elif mod_loaded(\"scouts\"):\n" + PatchLine("c") +
            "else:\n" + PatchLine("d");

        private const string Both = ", optional=[\"banking\", \"scouts\"]";

        // 패치 대상 Def "A"를 가진 모드. 패치하는 모드는 이 모드에 의존한다 (③이 대상을 검사한다)
        private const string CoreDep = ", dependencies=[\"core\"]";

        private void Core() => Mod("core", schema: "deftype(\"Unit\")\n", defs: "Unit(\"A\")\n");

        private static void AssertNoErrors(System.Collections.Generic.IReadOnlyList<Diagnostic> diags) =>
            Assert.That(diags.Where(d => d.Severity == Severity.Error), Is.Empty, string.Join("\n", diags));

        [Test]
        public void GuardRunsTheFirstTrueBranch()
        {
            Core();
            Mod("scouts", defs: "\n");
            Mod("compat", defs: ThreeWay, extra: Both + CoreDep);
            var result = Load(out var diags);
            AssertNoErrors(diags);
            var patch = result.Patches.Single();
            Assert.That(patch.Path.Text, Is.EqualTo("c"));
            Assert.That(patch.Guards, Is.EqualTo(new[] { "scouts" }));
            // 앞 가지(banking)는 거짓이라 평가했고, 뒤 가지는 앞 가지가 참이어서 평가하지 않았다
            var info = diags.Single();
            Assert.That((info.Code, info.Severity, info.Mod), Is.EqualTo((Codes.PatSkipped, Severity.Info, "compat")));
            Assert.That(info.Message, Does.Contain("'banking'"));
            Assert.That(info.Line, Is.EqualTo(1));
        }

        [Test]
        public void ElseRunsWhenNoGuardMatchesAndHasNoGuard()
        {
            Core();
            Mod("compat", defs: ThreeWay, extra: Both + CoreDep);
            var result = Load(out var diags);
            AssertNoErrors(diags);
            var patch = result.Patches.Single();
            Assert.That(patch.Path.Text, Is.EqualTo("d"));
            Assert.That(patch.Guards, Is.Empty);
            Assert.That(diags.Select(d => (d.Code, d.Line)),
                Is.EqualTo(new[] { (Codes.PatSkipped, 1), (Codes.PatSkipped, 3) }));
        }

        [Test]
        public void GuardOfAMissingModSkipsItsBodyAndSaysSo()
        {
            Mod("compat", defs: "if mod_loaded(\"banking\"):\n" + PatchLine("b"), extra: ", optional=[\"banking\"]");
            var result = Load(out var diags);
            Assert.That(result.Mods.Select(m => m.Id), Is.EqualTo(new[] { "compat" }));
            Assert.That(result.Patches, Is.Empty);
            var info = diags.Single();
            Assert.That((info.Code, info.Severity, info.Mod), Is.EqualTo((Codes.PatSkipped, Severity.Info, "compat")));
            Assert.That(info.Message, Is.EqualTo("mod 'banking' is not loaded, so 1 patch call was not applied"));
        }

        [Test]
        public void SkippedCallsInOneGuardAreCountedTogetherIncludingNestedOnes()
        {
            Mod("compat",
                defs: "if mod_loaded(\"banking\"):\n" + PatchLine("b") + PatchLine("c") +
                      "    if mod_loaded(\"scouts\"):\n" + "    " + PatchLine("d") +
                      "if mod_loaded(\"banking\"):\n" + PatchLine("e"),
                extra: Both);
            var diags = LoadDiagnostics();
            var banking = diags.Single(d => d.Message.Contains("'banking'"));
            Assert.That(banking.Message, Does.Contain("4 patch calls were not applied"));
            Assert.That(banking.Line, Is.EqualTo(1));
        }

        private System.Collections.Generic.IReadOnlyList<Diagnostic> LoadDiagnostics()
        {
            Load(out var diags);
            AssertNoErrors(diags);
            return diags;
        }

        [Test]
        public void InstalledButFailedOptionalModCountsAsNotLoaded()
        {
            // spec-gaps G8: 로드에 실패한 선택 모드는 없는 것으로 본다
            Mod("banking", defs: "Nope(\"a\")\n");
            Mod("compat", defs: "if mod_loaded(\"banking\"):\n" + PatchLine("b"), extra: ", optional=[\"banking\"]");
            var result = Load(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
            Assert.That(result.Mods.Select(m => m.Id), Is.EqualTo(new[] { "compat" }));
            Assert.That(result.Patches, Is.Empty);
            Assert.That(diags.Where(d => d.Severity == Severity.Error).Select(d => d.Mod), Has.All.EqualTo("banking"));
        }

        [Test]
        public void RequiredDependencyIsAlwaysLoadedInsideAGuard()
        {
            Mod("base", schema: "deftype(\"Unit\")\n", defs: "Unit(\"A\")\n");
            Mod("compat", defs: "if mod_loaded(\"base\"):\n" + PatchLine("b"), extra: ", dependencies=[\"base\"]");
            var result = LoadClean();
            Assert.That(result.Patches.Single().Guards, Is.EqualTo(new[] { "base" }));
        }

        [Test]
        public void NestedGuardsAccumulateOutsideIn()
        {
            Core();
            Mod("banking", defs: "\n");
            Mod("scouts", defs: "\n");
            Mod("compat",
                defs: "if mod_loaded(\"banking\"):\n    if mod_loaded(\"scouts\"):\n    " + PatchLine("b") + PatchLine("c"),
                extra: Both + CoreDep);
            var result = LoadClean();
            Assert.That(result.Patches.Select(p => (p.Path.Text, string.Join(",", p.Guards))),
                Is.EqualTo(new[] { ("b", "banking,scouts"), ("c", "banking") }));
        }

        [Test]
        public void ModLoadedNeedsAModDeclaredInTheManifest()
        {
            Mod("compat", defs: "if mod_loaded(\"extr\"):\n" + PatchLine("b"), extra: ", optional=[\"extra\"]");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlModNotDeclared));
            Assert.That(d.Line, Is.EqualTo(1));
            Assert.That(d.Hint, Does.Contain("extra"));
        }

        [TestCase("mod_loaded()", Codes.EvlArguments)]
        [TestCase("mod_loaded(1)", Codes.EvlType)]
        [TestCase("mod_loaded(\"a\", \"b\")", Codes.EvlArguments)]
        [TestCase("mod_loaded(id=\"a\")", Codes.EvlArguments)]
        public void BadModLoadedArguments(string call, string code)
        {
            Mod("compat", defs: "if " + call + ":\n" + PatchLine("b"), extra: ", optional=[\"a\", \"b\"]");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(code));
        }

        [Test]
        public void GuardBodiesAllowOnlyPatchCallsAndNestedIfs_WhateverIsInstalled()
        {
            // banking이 없어도 같은 오류가 나야 check의 결과가 설치 상태와 무관하다
            Mod("compat",
                defs: "if mod_loaded(\"banking\"):\n    x = 1\n    print(\"a\")\nelse:\n    y = 2\n",
                extra: ", optional=[\"banking\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Line)),
                Is.EqualTo(new[] { (Codes.EvlGuardBody, 2), (Codes.EvlGuardBody, 3), (Codes.EvlGuardBody, 5) }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        [Test]
        public void ConditionMustBeACall()
        {
            Mod("compat", defs: "if True:\n" + PatchLine("b"));
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.EvlBadCondition));
        }

        [Test]
        public void ModLoadedCannotBeUsedInSchemaFiles()
        {
            Mod("core", schema: "x = mod_loaded(\"core\")\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchModLoadedInSchemaFile));
        }

        [Test]
        public void IfIsNotSupportedInSchemaFiles()
        {
            Mod("core", schema: "if mod_loaded(\"a\"):\n" + PatchLine("b"), extra: ", optional=[\"a\"]");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnsupported));
            Assert.That(d.Message, Does.Contain("schema files"));
        }

        [Test]
        public void ModLoadedIsABuiltInName()
        {
            Mod("core", defs: "mod_loaded = 1\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.EvlNameTaken));
        }
    }
}
