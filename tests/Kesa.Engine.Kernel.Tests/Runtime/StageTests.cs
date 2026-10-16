using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>단계(stage): 선언, 참조와 의존, 위상 정렬, 사이클, 핸들러 순서 (spec S5, spec-gaps G152·G153).</summary>
    public class StageTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Out\", s=field(str, default=\"\"))\n" +
            "deftype(\"Unit\", components=[\"Out\"])\n" +
            "signal(\"Tick\", target=Ref[\"Unit\"])\n";

        private const string Dep = ", dependencies=[\"core\"]";

        private static string Diag(Diagnostic d) => d.Code + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        private IReadOnlyList<string> Errors()
        {
            Load(out var diags);
            return diags.Select(Diag).ToList();
        }

        private IEnumerable<string> Order() => LoadClean().Schema.StageOrder.Select(s => s.Id);

        // ---- 선언과 정렬 ----

        [Test]
        public void StagesAreSortedByAfterAndBeforeWithTiesById()
        {
            Mod("core", schema: Schema +
                "stage(\"core.politics\", after=[\"core.economy\"])\n" +
                "stage(\"core.economy\", after=[\"core.admin\"])\n" +
                "stage(\"core.admin\")\n" +
                "stage(\"core.zeta\")\n" +
                "stage(\"core.beta\", before=[\"core.admin\"])\n");
            // beta·zeta는 서로 관계가 없어 id 순, admin은 beta 뒤에야 나온다
            Assert.That(Order(), Is.EqualTo(new[] { "core.beta", "core.admin", "core.economy", "core.politics", "core.zeta" }));
        }

        [Test]
        public void AnotherModPutsItsStageBetweenOthers()
        {
            Mod("core", schema: Schema +
                "stage(\"core.admin\")\nstage(\"core.economy\", after=[\"core.admin\"])\nstage(\"core.politics\", after=[\"core.economy\"])\n");
            Mod("smuggling", schema: "stage(\"smuggling.black_market\", after=[\"core.economy\"], before=[\"core.politics\"])\n", extra: Dep);
            Assert.That(Order(), Is.EqualTo(new[] { "core.admin", "core.economy", "smuggling.black_market", "core.politics" }));
        }

        [Test]
        public void AStageIdNeedsTheModPrefix()
        {
            Mod("core", schema: Schema + "stage(\"economy\")\n");
            Assert.That(Errors(), Has.Some.StartWith(Codes.SchBadDeclaration + " the stage id 'economy' must start with this mod's id: 'core.…'"));
        }

        [Test]
        public void AModCannotDeclareAnotherModsStage()
        {
            Mod("core", schema: Schema);
            Mod("smuggling", schema: "stage(\"core.market\")\n", extra: Dep);
            Assert.That(Errors(), Has.Some.StartWith(Codes.SchBadDeclaration + " the stage id 'core.market' must start with this mod's id: 'smuggling.…'"));
        }

        [Test]
        public void ADuplicateStageIsAnError()
        {
            Mod("core", schema: Schema + "stage(\"core.a\")\nstage(\"core.a\")\n");
            Assert.That(Errors(), Has.Some.StartWith(Codes.SchDuplicateName + " stage 'core.a' is declared more than once"));
        }

        [Test]
        public void AnUnknownStageIsAnErrorWithASuggestion()
        {
            Mod("core", schema: Schema + "stage(\"core.economy\")\nstage(\"core.politics\", after=[\"core.ecnomy\"])\n");
            Assert.That(Errors(), Has.Some.EqualTo(Codes.SchUnknownName + " unknown stage 'core.ecnomy' | did you mean 'core.economy'?"));
        }

        [Test]
        public void AStageCannotBeOrderedAgainstItself()
        {
            Mod("core", schema: Schema + "stage(\"core.a\", after=[\"core.a\"])\n");
            Assert.That(Errors(), Has.Some.StartWith(Codes.SchBadDeclaration + " stage 'core.a' cannot be ordered against itself"));
        }

        [Test]
        public void UnknownStageArgumentsAreErrors()
        {
            Mod("core", schema: Schema + "stage(\"core.a\", afer=[\"core.b\"])\n");
            Assert.That(Errors(), Has.Some.EqualTo(Codes.EvlArguments + " stage() got an unexpected keyword argument 'afer' | did you mean 'after'?"));
        }

        // ---- 의존 (G153) ----

        [Test]
        public void ARequiredReferenceToAnotherModNeedsADependency()
        {
            Mod("core", schema: Schema + "stage(\"core.economy\")\n");
            Mod("smuggling", schema: "stage(\"smuggling.market\", after=[\"core.economy\"])\n");
            Assert.That(Errors(), Has.Some.StartWith(Codes.SchNotVisible +
                " stage 'core.economy' belongs to mod 'core', which mod 'smuggling' does not depend on"));
        }

        [Test]
        public void AnOptionalReferenceNeedsNoDependencyAndMayBeMissing()
        {
            Mod("core", schema: Schema + "stage(\"core.economy\")\n");
            Mod("smuggling", schema: "stage(\"smuggling.market\", optional_after=[\"core.economy\", \"trade.ports\"], " +
                                     "optional_before=[\"nope.x\"])\n");
            Assert.That(Order(), Is.EqualTo(new[] { "core.economy", "smuggling.market" }));
        }

        // ---- 사이클 ----

        [Test]
        public void ACycleIsALoadErrorWithItsPath()
        {
            Mod("core", schema: Schema +
                "stage(\"core.b\", after=[\"core.a\"])\nstage(\"core.c\", after=[\"core.b\"])\nstage(\"core.a\", after=[\"core.c\"])\n" +
                "stage(\"core.d\", after=[\"core.a\"])\n");
            Load(out var diags);
            var cycles = diags.Where(d => d.Message.StartsWith("stages are ordered in a cycle")).ToList();
            // 사이클 뒤에 매달린 core.d는 따로 사이클로 보고되지 않는다
            Assert.That(cycles.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                Codes.SchBadDeclaration + " stages are ordered in a cycle: core.a → core.b → core.c → core.a",
            }));
            Assert.That(cycles[0].Related.Count, Is.EqualTo(3), "each arrow points at the after=/before= that made it");
        }

        [Test]
        public void ACycleAcrossModsThroughBefore()
        {
            Mod("core", schema: Schema + "stage(\"core.a\")\nstage(\"core.b\", after=[\"core.a\"])\n");
            Mod("smuggling", schema: "stage(\"smuggling.x\", after=[\"core.b\"], before=[\"core.a\"])\n", extra: Dep);
            Assert.That(Errors(), Has.Some.StartWith(Codes.SchBadDeclaration +
                " stages are ordered in a cycle: core.a → core.b → smuggling.x → core.a"));
        }

        // ---- 핸들러의 stage= ----

        [Test]
        public void AHandlerStageMustExist()
        {
            Mod("core", schema: Schema + "stage(\"core.economy\")\n",
                defs: "@on(Tick, stage=\"core.ecnomy\")\ndef pay(target):\n    pass\n");
            Assert.That(Errors(), Has.Some.EqualTo(Codes.SchUnknownName + " unknown stage 'core.ecnomy' | did you mean 'core.economy'?"));
        }

        [Test]
        public void AHandlerInAnotherModsStageNeedsADependency()
        {
            Mod("core", schema: Schema + "stage(\"core.economy\")\n");
            // 신호는 자기 모드 것이라 단계만 남의 것이다
            Mod("smuggling", schema: "signal(\"Alarm\", n=int)\n", defs: "@on(Alarm, stage=\"core.economy\")\ndef pay(n):\n    pass\n");
            var errors = Errors();
            Assert.That(errors, Is.EqualTo(new[]
            {
                Codes.SchNotVisible + " stage 'core.economy' belongs to mod 'core', which mod 'smuggling' does not depend on" +
                " | add \"core\" to dependencies in mod.py (spec-gaps G153)",
            }));
        }

        [Test]
        public void AHandlerStageIsAString()
        {
            Mod("core", schema: Schema, defs: "@on(Tick, stage=3)\ndef pay(target):\n    pass\n");
            Assert.That(Errors(), Has.Some.StartWith(Codes.CmdBadDecorator + " stage= takes a stage id string"));
        }

        // ---- 실행 순서 ----

        [Test]
        public void HandlersRunInStageOrderThenUnstagedLast()
        {
            Mod("core", schema: Schema +
                    "stage(\"core.admin\")\nstage(\"core.economy\", after=[\"core.admin\"])\nstage(\"core.politics\", after=[\"core.economy\"])\n",
                defs: "Unit(\"Scout\")\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    emit(Tick(target=u))\n" +
                      "@on(Tick)\ndef loose(target):\n    target.Out.s = target.Out.s + \"L\"\n" +
                      "@on(Tick, stage=\"core.politics\")\ndef vote(target):\n    target.Out.s = target.Out.s + \"P\"\n" +
                      "@on(Tick, stage=\"core.admin\")\ndef census(target):\n    target.Out.s = target.Out.s + \"A\"\n" +
                      "@on(Tick, stage=\"core.admin\")\ndef tax(target):\n    target.Out.s = target.Out.s + \"a\"\n");
            Mod("smuggling", schema: "stage(\"smuggling.market\", after=[\"core.economy\"], before=[\"core.politics\"])\n",
                defs: "@on(Tick)\ndef late(target):\n    target.Out.s = target.Out.s + \"l\"\n" +
                      "@on(Tick, stage=\"smuggling.market\")\ndef deal(target):\n    target.Out.s = target.Out.s + \"S\"\n" +
                      "@on(Tick, stage=\"core.admin\")\ndef bribe(target):\n    target.Out.s = target.Out.s + \"b\"\n",
                extra: Dep);
            var world = new World(LoadClean());
            var u = world.Create("Scout");
            var r = world.Submit("host", "core.Go", new Dictionary<string, Value> { ["u"] = new EntityValue(u) });
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            // admin(core의 census·tax → smuggling의 bribe) → market → politics → 단계 없음(core → smuggling)
            Assert.That(((StrValue)world.Get(u, world.Member("Out", "s"))).Value, Is.EqualTo("AabSPLl"));
            Assert.That(r.Reactions.Select(x => x.Handler), Is.EqualTo(new[]
            {
                "core.census", "core.tax", "smuggling.bribe", "smuggling.deal", "core.vote", "core.loose", "smuggling.late",
            }));
        }
    }
}
