using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// UI 로컬 효과의 자리 (spec-gaps G278·G282): <c>ui.…</c>와 <c>submit</c>은 <c>@ui_action</c>과 UI를 쓰는 함수의 것이고,
    /// 게임 쪽 코드는 그런 함수를 부를 수 없으며, <c>@ui_action</c>은 게임을 바꾸지 않는다. <c>submit</c>의 대상은 로드 때 본다.
    /// </summary>
    public class UiEffectTests : LoaderTestBase
    {
        private const string Units =
            "component(\"Pos\", x=field(int, default=0))\ndeftype(\"Unit\", components=[\"Pos\"])\n" +
            "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True), count=field(int, default=0))\n";

        private const string Attack =
            "@command(free=True, acts_for=None)\ndef Attack(target: Ref[Unit], hard: bool = False):\n    target.Pos.x = 0\n";

        [Test]
        public void AUiActionReadsWritesAndSubmits()
        {
            Mod("core", schema: Units,
                defs: Attack +
                      "def remember(u):\n    ui.Selection.unit = u\n    ui.Selection.count += 1\n" +
                      "def chosen():\n    return ui.Selection.unit\n" +
                      "@ui_action\ndef pick(u: Ref[Unit]):\n" +
                      "    if chosen() is None:\n        remember(u)\n    else:\n        submit(\"Attack\", target=u)\n" +
                      "        submit(\"core.Attack\", target=chosen(), hard=True)\n");
            var result = LoadClean();
            Assert.That(result.UiActions.Keys, Is.EqualTo(new[] { "core.pick" }));
        }

        [Test]
        public void GameCodeCannotReadUiStateThroughAHelper()
        {
            Mod("core", schema: Units,
                defs: "def chosen():\n    return ui.Selection.unit\n" +
                      "@command(free=True, acts_for=None)\ndef Go():\n    u = chosen()\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Is.EqualTo("the @command 'Go' uses UI state (ui.… or submit), so it cannot run as game code"));
            Assert.That(d.Related.Single().Line, Is.EqualTo(5)); // 증거는 부른 곳
        }

        [Test]
        public void AHandlerCannotSubmit()
        {
            Mod("core", schema: Units + "signal(\"Hit\", who=Ref[\"Unit\"])\n",
                defs: Attack + "@on(Hit)\ndef react(who):\n    submit(\"Attack\", target=who)\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Message),
                Is.EqualTo(new[] { "the @on handler 'react' uses UI state (ui.… or submit), so it cannot run as game code" }));
        }

        [Test]
        public void AUiActionCannotChangeTheGame()
        {
            // 기록이 가리키는 엔티티에 쓰는 것도 게임 쓰기다
            Mod("core", schema: Units,
                defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Selection.unit.Pos.x = 3\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Is.EqualTo("the @ui_action 'pick' changes the game; it can only change ui.… and submit commands"));
        }

        [Test]
        public void AUiActionCannotDrawRandomNumbers()
        {
            Mod("core", schema: Units, defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Selection.count = random.int(0, 3)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Does.StartWith("the @ui_action 'pick' draws random numbers"));
        }

        [Test]
        public void AUiWritingFunctionIsCalledAsAStatement()
        {
            Mod("core", schema: Units,
                defs: "def bump():\n    ui.Selection.count += 1\n    return True\n" +
                      "@ui_action\ndef pick(u: Ref[Unit]):\n    if bump():\n        return\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'bump' changes UI state, so it can only be called as a statement"));
        }

        [Test]
        public void SubmitIsAStatement()
        {
            Mod("core", schema: Units,
                defs: Attack + "@ui_action\ndef pick(u: Ref[Unit]):\n    x = [submit(\"Attack\", target=u)]\n");
            Load(out var diags);
            Assert.That(diags.Single().Message,
                Is.EqualTo("submit() asks the host to submit a command, so it can only be called as a statement"));
        }

        [Test]
        public void UiPathsAreChecked()
        {
            Mod("core", schema: Units,
                defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Selcetion.unit = u\n    ui.Selection.unitt = u\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.EvlUnknownName, Codes.EvlUnknownName }));
            Assert.That(diags[0].Message, Is.EqualTo("there is no UI state 'Selcetion'"));
            Assert.That(diags[0].Hint, Does.Contain("Selection"));
            Assert.That(diags[1].Message, Is.EqualTo("UI state 'Selection' has no field 'unitt'"));
            Assert.That(diags[1].Hint, Does.Contain("unit"));
        }

        [Test]
        public void AUiStateCannotBeHeldAsAValue()
        {
            Mod("core", schema: Units, defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    s = ui.Selection\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Does.StartWith("write the UI state as ui.State.field"));
        }

        [Test]
        public void AnotherModsUiStateNeedsADependency()
        {
            Mod("core", schema: Units);
            Mod("scouts", defs: "@ui_action\ndef peek():\n    ui.Selection.count = 1\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(d.Message, Is.EqualTo("UI state 'Selection' belongs to mod 'core', which mod 'scouts' does not depend on"));
        }

        [Test]
        public void SubmitTargetsAreChecked()
        {
            Mod("core", schema: Units,
                defs: Attack +
                      "@ui_action\ndef other():\n    return\n" +
                      "@ui_action\ndef pick(u: Ref[Unit]):\n" +
                      "    submit(\"Atack\", target=u)\n" +
                      "    submit(\"Attack\", targt=u)\n" +
                      "    submit(\"other\")\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.All.EqualTo(Codes.CmdSubmit));
            Assert.That(diags.Select(d => d.Message), Is.EquivalentTo(new[]
            {
                "submit() names 'Atack', but there is no command 'core.Atack'",
                "command 'core.Attack' has no parameter 'targt'",
                "submit() of 'core.Attack' does not pass 'target', which has no default",
                "'other' is a @ui_action, so it cannot be submitted",
            }));
            Assert.That(diags.First(d => d.Message.Contains("Atack")).Hint, Does.Contain("Attack"));
            Assert.That(diags.First(d => d.Message.Contains("targt")).Hint, Does.Contain("target"));
        }

        [Test]
        public void SubmitTakesArgumentsByName()
        {
            Mod("core", schema: Units, defs: Attack + "@ui_action\ndef pick(u: Ref[Unit]):\n    submit(\"Attack\", u)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("submit() takes the command's arguments by name"));
        }

        [Test]
        public void AnotherModsCommandNeedsADependency()
        {
            Mod("banking", schema: Units, defs: Attack);
            Mod("scouts", defs: "@ui_action\ndef pick():\n    submit(\"banking.Attack\", target=None)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message,
                Is.EqualTo("command 'banking.Attack' belongs to mod 'banking', which mod 'scouts' does not depend on"));
        }

        [Test]
        public void TheHostCannotCallAUiFunction()
        {
            Mod("core", schema: Units, defs: "def bump():\n    ui.Selection.count += 1\ndef chosen():\n    return ui.Selection.unit\n");
            var world = new World(LoadClean());
            Assert.That(Assert.Throws<WorldException>(() => world.Call("core.bump"))!.Message, Does.Contain("it changes UI state"));
            Assert.That(Assert.Throws<WorldException>(() => world.Call("core.chosen"))!.Message, Does.Contain("it reads UI state"));
        }

        private const string GoalSchema =
            Units + "deftype(\"Goal\", check=const(Cond[\"Goal\"]), effect=const(Action[\"Goal\"]))\n";

        private const string GoalFunctions =
            "def ok(g):\n    return True\n" +
            "def peek(g):\n    return ui.Selection.unit is None\n";

        [Test]
        public void ADefValueInAGameSlotCannotUseUiState()
        {
            Mod("core", schema: GoalSchema, defs: GoalFunctions + "Goal(\"A\", check=peek, effect=ok)\nGoal(\"B\", check=ok, effect=peek)\n");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.ValType, Codes.ValType }));
            Assert.That(diags[0].Message, Does.StartWith("'check': 'peek' uses UI state (ui.… or submit) (at core/defs/defs.py:4:"));
            Assert.That(diags[0].Message, Does.EndWith("a game value cannot depend on a screen"));
            Assert.That(diags[1].Message, Does.StartWith("'effect': 'peek' uses UI state").And.Contain("Action[\"Goal\"]"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void APatchCannotPutAUiFunctionInAGameSlotAndItIsThePatchModsFault()
        {
            Mod("core", schema: GoalSchema, defs: "def ok(g):\n    return True\nGoal(\"A\", check=ok, effect=ok)\n");
            Mod("addon", defs: "def peek(g):\n    return ui.Selection.unit is None\npatch.set(\"core:A\", \"check\", peek)\n",
                extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadValue));
            Assert.That(d.Message, Does.StartWith("'peek' uses UI state (ui.… or submit) (at addon/defs/defs.py:2:"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon" }));
        }
    }
}
