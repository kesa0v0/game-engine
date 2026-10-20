using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// UI 동작의 실행 (spec-gaps G278·G279·G282~G284): <see cref="WorldView.Act"/>가 연결의 <see cref="UiState"/>로 <c>@ui_action</c>을 돌리고,
    /// 적어 둔 <c>submit</c>은 <see cref="World.Deliver"/>가 자유 커맨드면 제출, 응답 커맨드면 짝지은 질문에 답으로 낸다.
    /// </summary>
    public class UiActTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\ncomponent(\"Cloak\", on=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Hp\", \"Cloak\"])\n" +
            "deftype(\"Unit\", components=[\"Hp\", \"Cloak\"])\n" +
            "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True), group=field(list[Ref[\"Unit\"]], default=[]), " +
            "count=field(int, default=0))\n";

        private const string Defs =
            "Party(\"Red\")\nUnit(\"Soldier\")\nUnit(\"Ghost\", Cloak=Cloak(on=True))\n" +
            // 망토를 두른 유닛은 아무에게도 안 보인다
            "@view_policy\ndef may_see(viewer, entity, component):\n    return not entity.Cloak.on\n" +
            "@ui_action\ndef grow(u: Ref[Unit]):\n    ui.Selection.group += [u]\n" +
            "@command(free=True, acts_for=None)\ndef Hide(target: Ref[Unit]):\n    target.Cloak.on = True\n" +
            "@command(free=True, acts_for=None)\ndef Hit(target: Ref[Unit]):\n    target.Hp.v -= 1\n" +
            "@command(free=True, acts_for=None)\ndef Kill(target: Ref[Unit]):\n    destroy(target)\n" +
            // 응답 커맨드: Turn이 연 질문의 답으로만 들어간다
            "@command(acts_for=lambda party: party)\ndef Strike(party: Ref[Party], target: Ref[Unit]):\n    target.Hp.v -= 2\n" +
            "@command(free=True, acts_for=None)\ndef Turn(p: Ref[Party]):\n    ask(p, \"turn\", responses=[\"Strike\"])\n" +
            // 첫 클릭은 고르기, 두 번째 클릭은 공격
            "@ui_action\ndef click(u: Ref[Unit]):\n" +
            "    if ui.Selection.unit is None:\n        ui.Selection.unit = u\n" +
            "    else:\n        submit(\"Hit\", target=u)\n        ui.Selection.unit = None\n" +
            "@ui_action\ndef pick_all(units: list[Ref[Unit]]):\n    ui.Selection.group = units\n" +
            "@ui_action\ndef count_group():\n    ui.Selection.count = len(ui.Selection.group)\n" +
            "@ui_action\ndef broken():\n    ui.Selection.count = 5\n    submit(\"Hit\", target=None)\n    x = 1 // 0\n" +
            "@ui_action\ndef guarded(u: Ref[Unit]):\n    with preconditions:\n        require(u.Hp.v > 5, text(\"too-weak\"))\n" +
            "    ui.Selection.count = 9\n" +
            "@ui_action\ndef strike(u: Ref[Unit], p: Ref[Party]):\n    submit(\"Strike\", party=p, target=u)\n" +
            "@ui_action\ndef bad_type():\n    ui.Selection.count = \"many\"\n" +
            "def hurt(u):\n    u.Hp.v = 0\n" +
            "def remember(u):\n    ui.Selection.unit = u\n" +
            "@ui_action\ndef sneaky(u: Ref[Unit]):\n    f = hurt\n    f(u)\n" +
            "@ui_action\ndef nested(u: Ref[Unit]):\n    f = remember\n    x = [f(u)]\n" +
            // 이름으로 풀리지 않는 호출은 로드 검사를 지나지만 실행 중 안전망이 막는다
            "def chosen():\n    return ui.Selection.unit\n" +
            "@command(free=True, acts_for=None)\ndef Peek():\n    f = chosen\n    x = f()\n";

        private World _world = null!;
        private WorldView _view = null!;
        private UiState _ui = null!;
        private EntityId _a, _b, _red;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _a = _world.Create("core:Soldier");
            _b = _world.Create("core:Soldier");
            _red = _world.Create("core:Red");
            _world.Seat(_red, "host");
            _view = _world.ViewAs("host");
            _ui = _world.NewUiState();
        }

        private static Dictionary<string, Value> Args(params (string, Value)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

        private long Hp(EntityId id) => ((IntValue)_world.Get(id, _world.Member("Hp", "v"))).Value;

        [Test]
        public void PickThenAttack()
        {
            var first = _view.Act(_ui, "core.click", Args(("u", new EntityValue(_a))));
            Assert.That(first.Succeeded, Is.True, first.Error?.Message);
            Assert.That(first.Submissions, Is.Empty);
            Assert.That(((EntityValue)_ui.Stored("Selection", "unit")).Id, Is.EqualTo(_a));

            var second = _view.Act(_ui, "core.click", Args(("u", new EntityValue(_b))));
            var s = second.Submissions.Single();
            Assert.That(s.Command, Is.EqualTo("core.Hit"));
            Assert.That(((EntityValue)s.Arguments["target"]).Id, Is.EqualTo(_b));
            Assert.That(_ui.Stored("Selection", "unit"), Is.SameAs(NoneValue.Instance));
            Assert.That(Hp(_b), Is.EqualTo(3)); // 적어 두기만 했다

            var delivered = _world.Deliver("host", s);
            Assert.That(delivered.Submitted!.Succeeded, Is.True);
            Assert.That(Hp(_b), Is.EqualTo(2));
        }

        [Test]
        public void AFailedActionLeavesNothing()
        {
            var result = _view.Act(_ui, "core.broken", Args());
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.ScriptError));
            Assert.That(result.Submissions, Is.Empty);
            Assert.That(((IntValue)_ui.Stored("Selection", "count")).Value, Is.EqualTo(0));
        }

        [Test]
        public void FailedPreconditionsWriteNothing()
        {
            var result = _view.Act(_ui, "core.guarded", Args(("u", new EntityValue(_a))));
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.PreconditionsFailed));
            Assert.That(((IntValue)_ui.Stored("Selection", "count")).Value, Is.EqualTo(0));
        }

        [Test]
        public void ArgumentsAreChecked()
        {
            var result = _view.Act(_ui, "core.click", Args(("u", new EntityValue(_red))));
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.ArgumentsRejected));
        }

        [Test]
        public void ADeadSelectionReadsAsNone()
        {
            _view.Act(_ui, "core.click", Args(("u", new EntityValue(_a))));
            _world.Submit("host", "core.Kill", Args(("target", new EntityValue(_a))));
            // 고른 것이 죽었으니 다시 고르기가 된다 (G279)
            var again = _view.Act(_ui, "core.click", Args(("u", new EntityValue(_b))));
            Assert.That(again.Submissions, Is.Empty);
            Assert.That(((EntityValue)_ui.Stored("Selection", "unit")).Id, Is.EqualTo(_b));
        }

        [Test]
        public void DeadEntitiesDropOutOfLists()
        {
            _view.Act(_ui, "core.pick_all", Args(("units", new ListValue(new Value[] { new EntityValue(_a), new EntityValue(_b) }))));
            _world.Submit("host", "core.Kill", Args(("target", new EntityValue(_a))));
            _view.Act(_ui, "core.count_group", Args());
            Assert.That(((IntValue)_ui.Stored("Selection", "count")).Value, Is.EqualTo(1)); // G284
            Assert.That(((ListValue)_ui.Stored("Selection", "group")).Items.Count, Is.EqualTo(2)); // 적힌 값은 그대로
        }

        [Test]
        public void AHiddenEntityAnswersLikeADeadOne()
        {
            var ghost = _world.Create("core:Ghost");
            var dead = _world.Create("core:Soldier");
            _world.Destroy(dead);
            var hidden = _view.Act(_ui, "core.click", Args(("u", new EntityValue(ghost))));
            var gone = _view.Act(_ui, "core.click", Args(("u", new EntityValue(dead))));
            Assert.That(hidden.Outcome, Is.EqualTo(ActOutcome.ArgumentsRejected));
            Assert.That(hidden.ArgumentRejections.Single().Problem, Is.EqualTo(gone.ArgumentRejections.Single().Problem));
            Assert.That(_ui.Stored("Selection", "unit"), Is.SameAs(NoneValue.Instance));
        }

        [Test]
        public void EntitiesHiddenLaterDropOutOfLists()
        {
            // 고를 때는 보였는데 나중에 망토를 둘렀다: 읽을 때 빠진다 (G284)
            _view.Act(_ui, "core.pick_all", Args(("units", new ListValue(new Value[] { new EntityValue(_a), new EntityValue(_b) }))));
            _world.Submit("host", "core.Hide", Args(("target", new EntityValue(_a))));
            _view.Act(_ui, "core.count_group", Args());
            Assert.That(((IntValue)_ui.Stored("Selection", "count")).Value, Is.EqualTo(1));
        }

        [Test]
        public void ListPlusEqualsIsRefusedLikeLocals()
        {
            var result = _view.Act(_ui, "core.grow", Args(("u", new EntityValue(_a))));
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.ScriptError));
            Assert.That(result.Error!.Message, Does.Contain("'+=' cannot be used on a list"));
            Assert.That(result.Error.Hint, Does.Contain("ui.Selection.group = ui.Selection.group + [...]"));
        }

        [Test]
        public void TheShownResultHasNoMessages()
        {
            var shown = _view.Show(_view.Act(_ui, "core.broken", Args()));
            Assert.That(shown.Error!.Message, Is.EqualTo("script error"));
            var rejected = _view.Show(_view.Act(_ui, "core.click", Args(("u", new EntityValue(_red)))));
            Assert.That(rejected.ArgumentRejections.Single().Message, Is.Empty);
        }

        [Test]
        public void UiFieldsKeepTheirTypes()
        {
            var result = _view.Act(_ui, "core.bad_type", Args());
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.ScriptError));
            Assert.That(result.Error!.Code, Is.EqualTo(Codes.RunType));
        }

        [Test]
        public void AUiActionCannotChangeTheGameThroughAFunctionValue()
        {
            var result = _view.Act(_ui, "core.sneaky", Args(("u", new EntityValue(_a))));
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.ScriptError));
            Assert.That(result.Error!.Message, Does.Contain("cannot change the game"));
            Assert.That(Hp(_a), Is.EqualTo(3));
        }

        [Test]
        public void UiWritesInsideAnExpressionAreRefusedAtRunTime()
        {
            var result = _view.Act(_ui, "core.nested", Args(("u", new EntityValue(_a))));
            Assert.That(result.Outcome, Is.EqualTo(ActOutcome.ScriptError));
            Assert.That(result.Error!.Message, Does.Contain("cannot change UI state here"));
            Assert.That(_ui.Stored("Selection", "unit"), Is.SameAs(NoneValue.Instance));
        }

        [Test]
        public void GameCodeCannotReachUiStateAtRunTime()
        {
            var result = _world.Submit("host", "core.Peek", Args());
            Assert.That(result.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(result.Error!.Message, Does.Contain("ui can only be used while a @ui_action runs"));
        }

        [Test]
        public void AResponseCommandAnswersTheOpenQuestion()
        {
            var turn = _world.Submit("host", "core.Turn", Args(("p", new EntityValue(_red))));
            Assert.That(turn.OpenedRequest, Is.Not.Null);
            var act = _view.Act(_ui, "core.strike", Args(("u", new EntityValue(_a)), ("p", new EntityValue(_red))));
            var delivered = _world.Deliver("host", act.Submissions.Single());
            Assert.That(delivered.Answered!.Succeeded, Is.True);
            Assert.That(Hp(_a), Is.EqualTo(1));
            Assert.That(_world.OpenRequests, Is.Empty);
        }

        [Test]
        public void AResponseCommandWithoutAQuestionIsRefused()
        {
            var act = _view.Act(_ui, "core.strike", Args(("u", new EntityValue(_a)), ("p", new EntityValue(_red))));
            var delivered = _world.Deliver("host", act.Submissions.Single());
            Assert.That(delivered.Refusal, Does.Contain("no open question"));
            Assert.That(Hp(_a), Is.EqualTo(3));
        }

        [Test]
        public void AnotherConnectionCannotAnswer()
        {
            _world.Submit("host", "core.Turn", Args(("p", new EntityValue(_red))));
            var act = _world.ViewAs("guest").Act(_world.NewUiState(), "core.strike",
                Args(("u", new EntityValue(_a)), ("p", new EntityValue(_red))));
            Assert.That(_world.Deliver("guest", act.Submissions.Single()).Refusal, Does.Contain("no open question"));
        }

        [Test]
        public void UnknownActionsHaveHints()
        {
            var e = Assert.Throws<WorldException>(() => _view.Act(_ui, "core.clik", Args()))!;
            Assert.That(e.Hint, Does.Contain("core.click"));
            var c = Assert.Throws<WorldException>(() => _view.Act(_ui, "core.Hit", Args()))!;
            Assert.That(c.Hint, Does.Contain("is a command"));
        }

        [Test]
        public void ResetGoesBackToDefaults()
        {
            _view.Act(_ui, "core.click", Args(("u", new EntityValue(_a))));
            _ui.Reset();
            Assert.That(_ui.Stored("Selection", "unit"), Is.SameAs(NoneValue.Instance));
        }
    }
}
