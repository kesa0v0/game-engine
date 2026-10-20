using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary><c>@ui_action</c> 선언: 커맨드 모양의 매개변수·사전 조건, 커맨드 목록과 따로 (spec-gaps G276·G280).</summary>
    public class UiActionDeclarationTests : LoaderTestBase
    {
        private const string Units =
            "component(\"Pos\", x=field(int, default=0))\ndeftype(\"Unit\", components=[\"Pos\"])\n" +
            "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True))\n";

        [Test]
        public void AUiActionHasTypedParametersAndIsNotACommand()
        {
            Mod("core", schema: Units,
                defs: "@ui_action\ndef pick(u: Ref[Unit], twice: bool = False):\n" +
                      "    with preconditions:\n        choose(u, where=lambda x: x.Pos.x >= 0)\n    return 0\n");
            var result = LoadClean();
            Assert.That(result.UiActions.Keys, Is.EqualTo(new[] { "core.pick" }));
            Assert.That(result.Commands.ContainsKey("core.pick"), Is.False);
            var action = result.UiActions["core.pick"];
            Assert.That(action.IsUiAction, Is.True);
            Assert.That(action.Parameters.Select(p => p.Type.Display), Is.EqualTo(new[] { "Ref[\"Unit\"]", "bool" }));
            Assert.That(action.Parameters[1].Default, Is.EqualTo(BoolValue.False));
        }

        [Test]
        public void TheWorldCannotSubmitAUiAction()
        {
            Mod("core", schema: Units, defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    return 0\n");
            var world = new World(LoadClean());
            Assert.Throws<WorldException>(() => world.Submit("host", "core.pick", new Dictionary<string, Value>()));
        }

        [Test]
        public void ParametersNeedTypes()
        {
            Mod("core", schema: Units, defs: "@ui_action\ndef pick(u):\n    return 0\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("parameter 'u' of UI action 'core.pick' needs a type annotation"));
        }

        [Test]
        public void ItTakesNoArguments()
        {
            Mod("core", schema: Units, defs: "@ui_action(acts_for=lambda u: u)\ndef pick(u: Ref[Unit]):\n    return 0\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Does.StartWith("@ui_action has no acts_for"));
        }

        [Test]
        public void ItHasNoCost()
        {
            Mod("core", schema: Units + "component(\"Purse\", gold=field(int, default=0))\ndeftype(\"House\", components=[\"Purse\"])\n",
                defs: "@ui_action\ndef pick(h: Ref[House]):\n    with preconditions:\n        cost(h.Purse.gold, 1)\n    return 0\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("a @ui_action pays nothing, so it has no cost()"));
        }

        [Test]
        public void LogicCannotCallAUiAction()
        {
            // G118과 같다: 입구는 값으로도 호출로도 쓸 수 없다
            Mod("core", schema: Units,
                defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    return 0\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    pick(u)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'pick' is a @ui_action, so logic cannot call it or use it as a value"));
        }

        [Test]
        public void TheHostCannotCallItAsAViewFunction()
        {
            Mod("core", schema: Units, defs: "@ui_action\ndef pick(u: Ref[Unit]):\n    return 0\n");
            var world = new World(LoadClean());
            Assert.That(world.CheckCall("core.pick", 1), Does.Contain("@ui_action"));
        }

        [Test]
        public void AModNameCalledUiActionShadowsTheDecorator()
        {
            Mod("core", schema: Units, defs: "ui_action = 1\n@ui_action\ndef pick(u: Ref[Unit]):\n    return 0\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("this mod defines its own 'ui_action', so the built-in @ui_action cannot be used here"));
        }
    }
}
