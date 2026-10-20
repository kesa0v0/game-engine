using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 커맨드 본문의 <c>while</c> (spec-gaps G195): 단계 예산은 구간마다 새로 세므로 바퀴마다 묻는 반복은 걸리지 않고,
    /// 묻지 않고 혼자 도는 반복은 예산을 넘어 되감긴다. 멈춘 <c>while</c>은 저장·불러오기 뒤에도 조건을 다시 보며 이어 간다.
    /// </summary>
    public class WorldWhileTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n";

        private const string Defs =
            "Party(\"Red\")\n" +
            // 답이 "stop"일 때까지 계속 묻는다: 끝이 플레이어에게 달린 반복
            "@command(free=True, acts_for=None)\n" +
            "def Ask(p: Ref[Party]):\n" +
            "    while p.Score.v >= 0:\n" +
            "        r = ask(p, \"more?\", choices=[\"more\", \"stop\"])\n" +
            "        if r.index == 1:\n" +
            "            p.Score.v = -p.Score.v\n" +
            "        else:\n" +
            "            p.Score.v += 1\n" +
            "    p.Score.v -= 1000\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Spin(p: Ref[Party]):\n" +
            "    p.Score.v = 5\n" +
            "    while True:\n" +
            "        p.Score.v += 1\n";

        private World _world = null!;
        private ContentLoadResult _content = null!;
        private EntityId _red;

        private void NewWorld(int actionStepBudget = World.DefaultActionStepBudget)
        {
            Mod("banking", schema: Schema, defs: Defs);
            _content = LoadClean();
            _world = new World(_content, actionStepBudget: actionStepBudget);
            _red = _world.Create("banking:Red");
            _world.Seat(_red, "host");
        }

        private long V(World w) => ((IntValue)w.Get(_red, w.Member("Score", "v"))).Value;

        private SubmitResult Submit(World w, string command) =>
            w.Submit("host", "banking." + command, new Dictionary<string, Value> { ["p"] = new EntityValue(_red) });

        [Test]
        public void ALoopThatAsksEveryRoundIsNotStoppedByTheBudget()
        {
            NewWorld(actionStepBudget: 100);
            var q = Submit(_world, "Ask").OpenedRequest!;
            for (int i = 0; i < 200; i++) // 200바퀴 전체는 100단계를 훨씬 넘지만 구간마다 새로 센다
            {
                var a = _world.Answer("host", q.Id, _red, 0);
                Assert.That(a.Succeeded, Is.True, a.Message);
                q = a.OpenedRequest!;
            }
            Assert.That(V(_world), Is.EqualTo(200));
            Assert.That(_world.Answer("host", q.Id, _red, 1).Succeeded, Is.True);
            Assert.That(V(_world), Is.EqualTo(-200 - 1000));
            Assert.That(_world.OpenRequests, Is.Empty);
        }

        [Test]
        public void ALoopThatNeverAsksRunsOutOfTheBudgetAndRollsBack()
        {
            NewWorld(actionStepBudget: 500);
            var r = Submit(_world, "Spin");
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(V(_world), Is.EqualTo(0), "the command left nothing behind (C8)");
        }

        // C1: 반복 중간에 저장하고 불러와 이어도 저장 없이 한 것과 같다. 조건은 불러온 뒤 다시 평가한다
        [Test]
        public void SavingInsideAWhileGoesOnAfterLoading()
        {
            NewWorld();
            var picks = new[] { 0, 0, 0, 1 };
            var straight = new World(_content);
            straight.Seat(straight.Create("banking:Red"), "host");
            Play(straight, picks, stopAt: -1);
            Assert.That(V(straight), Is.EqualTo(-3 - 1000));

            for (int stop = 0; stop < picks.Length; stop++)
            {
                var w = new World(_content);
                w.Seat(w.Create("banking:Red"), "host");
                var done = Play(w, picks, stop);
                Assert.That(done.Save(), Is.EqualTo(straight.Save()), "saved before answer " + stop);
            }
        }

        private World Play(World w, int[] picks, int stopAt)
        {
            var q = Submit(w, "Ask").OpenedRequest!;
            for (int i = 0; i < picks.Length; i++)
            {
                if (i == stopAt)
                {
                    var bytes = w.Save();
                    var loaded = World.Load(_content, bytes);
                    Assert.That(loaded.Diagnostics, Is.Empty);
                    w = loaded.World!;
                    Assert.That(w.Save(), Is.EqualTo(bytes), "C4");
                    q = w.OpenRequests.Single();
                }
                var a = w.Answer("host", q.Id, _red, picks[i]);
                Assert.That(a.Succeeded, Is.True, a.Message);
                q = a.OpenedRequest!;
            }
            Assert.That(q, Is.Null);
            return w;
        }

        [Test]
        public void ChangingTheConditionRejectsTheSave()
        {
            NewWorld();
            var q = Submit(_world, "Ask").OpenedRequest!;
            Assert.That(q, Is.Not.Null);
            var bytes = _world.Save();
            Mod("banking", schema: Schema, defs: Defs.Replace("while p.Score.v >= 0:", "while p.Score.v >= 1:"));
            var loaded = World.Load(LoadClean(), bytes);
            Assert.That(loaded.Diagnostics.Select(d => d.Code), Has.Some.EqualTo(Codes.SaveFunctionChanged));
        }

        private List<Diagnostic> Diags(string defs)
        {
            Mod("banking", schema: Schema, defs: "Party(\"Red\")\n" + defs);
            Load(out var diags);
            return diags.ToList();
        }

        [Test]
        public void ATopLevelWhileIsALoadError()
        {
            var diags = Diags("while True:\n    pass\n");
            Assert.That(diags.Select(d => d.Code), Has.Some.EqualTo(Codes.EvlUnsupported), string.Join("\n", diags));
        }

        [Test]
        public void ANameTypoInTheConditionIsALoadError()
        {
            var diags = Diags("def f(p):\n    n = 0\n    while nn < 3:\n        n += 1\n    return n\n");
            Assert.That(diags.Select(d => d.Code + " " + d.Message), Has.Some.StartsWith("EVL001 name 'nn' is not defined"),
                string.Join("\n", diags));
        }

        [Test]
        public void AskInTheConditionIsALoadError()
        {
            var diags = Diags("@command(free=True, acts_for=None)\ndef C(p: Ref[Party]):\n" +
                              "    while ask(p, \"Q\", choices=[\"a\"]).index == 0:\n        pass\n");
            Assert.That(diags, Is.Not.Empty);
            Assert.That(diags.Select(d => d.Message), Has.Some.Contains("waits for an answer"), string.Join("\n", diags));
        }

        [Test]
        public void AWriteInsideTheBodyMakesTheFunctionAWriter()
        {
            // 쓰기 판정(G135)이 while 몸통 안도 본다: 순수 자리에서 부르면 로드 오류
            var diags = Diags("def bump(p):\n    while p.Score.v < 3:\n        p.Score.v += 1\n" +
                              "def g(p):\n    return 1 if bump(p) else 0\n");
            Assert.That(diags.Select(d => d.Code), Has.Some.EqualTo(Codes.CmdWriteEffect), string.Join("\n", diags));
        }
    }
}
