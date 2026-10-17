using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 읽기와 행동의 단계 예산 (spec-gaps G224): 커맨드 본문·핸들러는 큰 행동 예산을 쓰고, <c>derived</c>는 작은 읽기 예산 그대로다.
    /// 숫자는 테스트용으로 줄였다(읽기 300, 행동 2만). 유닛 100개를 한 바퀴에 몇 단계씩 돌면 읽기 예산은 넘고 행동 예산 안이다.
    /// </summary>
    public class WorldActionBudgetTests : LoaderTestBase
    {
        private const string Schema =
            "def everyone(c) -> int:\n" +
            "    n = 0\n" +
            "    for u in query(\"Unit\"):\n" +
            "        n += u.Food.amount\n" +
            "    return n\n" +
            "component(\"Food\", amount=field(int, default=0))\n" +
            "component(\"Census\", total=derived(int, everyone))\n" +
            "deftype(\"Unit\", components=[\"Food\"])\n" +
            "deftype(\"Office\", components=[\"Census\"])\n";

        private const string Defs =
            "Unit(\"Soldier\")\nOffice(\"Bureau\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def FeedAll():\n" +
            "    for u in query(\"Unit\"):\n" +
            "        u.Food.amount += 1\n" +
            "def count_units() -> int:\n" +
            "    n = 0\n" +
            "    for u in query(\"Unit\"):\n" +
            "        n += 1\n" +
            "    return n\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Count():\n" +
            "    with preconditions:\n" +
            "        n = count_units()\n" +
            "        require(n > 0, text(\"msg.none\"))\n" +
            "    pass\n" +
            "@command(free=True, acts_for=None)\n" +
            "def AskThenFeed(u: Ref[Unit]):\n" +
            "    r = ask(u, \"go?\", choices=[\"go\"])\n" +
            "    for x in query(\"Unit\"):\n" +
            "        x.Food.amount += 1\n";

        private const string Grow =
            "@on(Tick)\n" +
            "def grow():\n" +
            "    for u in query(\"Unit\"):\n" +
            "        u.Food.amount += 1\n";

        private const string Runaway =
            "@on(Tick)\n" +
            "def runaway():\n" +
            "    for u in query(\"Unit\"):\n" +
            "        u.Food.amount += 1\n" +
            "    while True:\n" +
            "        pass\n";

        private World _world = null!;
        private readonly List<EntityId> _units = new List<EntityId>();

        private void NewWorld(string handlers = "", int units = 100)
        {
            Mod("banking", schema: Schema, defs: Defs + handlers);
            _world = new World(LoadClean(), stepBudget: 300, actionStepBudget: 20_000);
            _units.Clear();
            for (int i = 0; i < units; i++) _units.Add(_world.Create("Soldier"));
        }

        private static void IsHeavy(IReadOnlyList<Diagnostic> warnings, string label)
        {
            Assert.That(warnings, Has.Count.EqualTo(1));
            var w = warnings[0];
            Assert.That(w.Code, Is.EqualTo(Codes.RunHeavy));
            Assert.That(w.Severity, Is.EqualTo(Severity.Warning));
            Assert.That(w.Message, Does.StartWith(label + " used "));
            Assert.That(w.Message, Does.Contain("evaluation steps, more than the read budget of 300 (it fails at 20000)"));
        }

        private long Food(EntityId id) => ((IntValue)_world.Get(id, _world.Member("Food", "amount"))).Value;

        private SubmitResult Submit(string command) =>
            _world.Submit("host", "banking." + command, new Dictionary<string, Value>());

        [Test]
        public void AHandlerOverEveryUnitRunsOnTheActionBudget()
        {
            NewWorld(Grow);
            var r = _world.Advance(1).Reactions;
            Assert.That(r, Has.Count.EqualTo(1));
            Assert.That(r[0].Error, Is.Null, r[0].Error?.Message);
            foreach (var u in _units) Assert.That(Food(u), Is.EqualTo(1));
        }

        [Test]
        public void ACommandBodyAndItsPreconditionsRunOnTheActionBudget()
        {
            NewWorld();
            var fed = Submit("FeedAll");
            Assert.That(fed.Succeeded, Is.True, fed.Error?.Message);
            Assert.That(Food(_units[99]), Is.EqualTo(1));
            // 사전 검사만 할 때도 제출과 같은 예산이다: 제출은 되는데 검사는 넘치는 일이 없다
            Assert.That(Submit("Count").Succeeded, Is.True);
            Assert.That(_world.CheckPreconditions("banking.Count", new Dictionary<string, Value>()).Passed, Is.True);
        }

        [Test]
        public void ADerivedReadKeepsTheSmallReadBudget()
        {
            NewWorld();
            var office = _world.Create("Bureau");
            var e = Assert.Throws<WorldException>(() => _world.Get(office, _world.Member("Census", "total")))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunBudget));
        }

        // ---- 경고 문턱 (G225): 읽기 예산을 넘은 행동은 실패하지 않고 결과에 경고가 붙는다 ----

        [Test]
        public void AHandlerOverTheReadBudgetGetsAWarningEveryTime()
        {
            NewWorld(Grow);
            for (int tick = 0; tick < 2; tick++)
            {
                var r = _world.Advance(1).Reactions[0];
                Assert.That(r.Outcome, Is.EqualTo(ReactionOutcome.Finished));
                IsHeavy(r.Warnings, "the handler banking.grow for Tick");
                Assert.That(r.Warnings[0].Span.Line, Is.GreaterThan(0), "it points at the handler");
            }
            Assert.That(Food(_units[0]), Is.EqualTo(2));
        }

        [Test]
        public void ALightHandlerHasNoWarning()
        {
            NewWorld(Grow, units: 10);
            var r = _world.Advance(1).Reactions[0];
            Assert.That(r.Warnings, Is.Empty);
        }

        [Test]
        public void ACommandOverTheReadBudgetGetsAWarningOnItsResult()
        {
            NewWorld(Grow);
            var fed = Submit("FeedAll");
            Assert.That(fed.Succeeded, Is.True);
            IsHeavy(fed.Warnings, "the command banking.FeedAll");
            // 연결마다 거른 결과에는 오류처럼 개발자용 글이 빠진다
            Assert.That(_world.ViewAs("host").Show(fed).Warnings, Is.Empty);
        }

        [Test]
        public void ContinuingAfterAnAnswerWarnsOnTheAnswer()
        {
            NewWorld();
            var asked = _world.Submit("host", "banking.AskThenFeed", new Dictionary<string, Value> { ["u"] = new EntityValue(_units[0]) });
            Assert.That(asked.Warnings, Is.Empty, "asking was light");
            var answered = _world.Answer("host", asked.OpenedRequest!.Id, _units[0], 0);
            Assert.That(answered.Succeeded, Is.True, answered.Message);
            IsHeavy(answered.Warnings, "the command banking.AskThenFeed (after an answer)");
        }

        [Test]
        public void AHandlerThatNeverEndsIsStillStoppedAndRolledBack()
        {
            NewWorld(Runaway);
            var r = _world.Advance(1).Reactions;
            Assert.That(r[0].Error!.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(r[0].Error!.Message, Does.Contain("20000 evaluation steps"));
            Assert.That(Food(_units[0]), Is.EqualTo(0), "the handler left nothing behind (C8)");
        }
    }
}
