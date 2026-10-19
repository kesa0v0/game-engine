using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 멈추는 응답 커맨드 (spec X1, spec-gaps G243): 응답 본문이 <c>ask</c>·<c>wait</c>·<c>advance</c>에서 멈추면 그 본문이 새 Flow가 되고,
    /// 질문한 바깥 Flow는 그 Flow가 끝날 때까지 기다렸다가 같은 트랜잭션에서 <c>r.response</c>·<c>r.by</c>로 이어 간다.
    /// </summary>
    public class NestedResponseTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0), log=field(str, default=\"\"))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\nParty(\"Green\")\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Pass(party: Ref[Party]):\n" +
            "    pass\n" +
            // 공격받은 쪽에게 묻는다. 그 질문은 또 응답 커맨드(Counter)를 받는다
            "@command(acts_for=lambda party: party)\n" +
            "def Attack(party: Ref[Party], target: Ref[Party]):\n" +
            "    party.Score.log = party.Score.log + \"A\"\n" +
            "    r = ask(target, \"Block\", choices=[\"block\", \"take\"], responses=[\"Counter\"])\n" +
            "    if r.response == \"Counter\":\n" +
            "        party.Score.log = party.Score.log + \"c\"\n" +
            "    elif r.index == 0:\n" +
            "        party.Score.log = party.Score.log + \"b\"\n" +
            "    else:\n" +
            "        target.Score.v = target.Score.v - 3\n" +
            "        party.Score.log = party.Score.log + \"t\"\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Counter(party: Ref[Party]):\n" +
            "    party.Score.log = party.Score.log + \"C\"\n" +
            "    ask(party, \"Sure\", choices=[\"yes\"])\n" +
            "    party.Score.log = party.Score.log + \"!\"\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Nap(party: Ref[Party]):\n" +
            "    wait(ticks=2)\n" +
            "    party.Score.log = party.Score.log + \"n\"\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Hop(party: Ref[Party]):\n" +
            "    n = advance(2)\n" +
            "    party.Score.log = party.Score.log + \"h\"\n" +
            "    party.Score.v = party.Score.v + n\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Fumble(party: Ref[Party]):\n" +
            "    r = ask(party, \"Q\", choices=[\"ok\", \"bad\"])\n" +
            "    party.Score.log = party.Score.log + \"f\"\n" +
            "    if r.index == 1:\n" +
            "        party.Score.v = \"oops\"\n" +
            // 깨어나면 오류 (G157로 버려진다) / 한도를 넘는 advance (G168로 버려진다)
            "@command(acts_for=lambda party: party)\n" +
            "def Doze(party: Ref[Party]):\n" +
            "    party.Score.log = party.Score.log + \"z\"\n" +
            "    wait(ticks=1)\n" +
            "    party.Score.v = \"oops\"\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Leap(party: Ref[Party]):\n" +
            "    party.Score.log = party.Score.log + \"l\"\n" +
            "    advance(20000)\n" +
            "    party.Score.log = party.Score.log + \"?\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Round(a: Ref[Party], b: Ref[Party]):\n" +
            "    for i in range(3):\n" +
            "        p = a if i % 2 == 0 else b\n" +
            "        r = ask(p, \"Turn\", responses=[\"Attack\", \"Pass\", \"Nap\", \"Hop\", \"Fumble\", \"Doze\", \"Leap\"])\n" +
            "        a.Score.log = a.Score.log + \"[\" + r.response + (\"~\" if r.interrupted else \"\") + \"]\"\n" +
            "        r.by.Score.v = r.by.Score.v + 100\n" +
            "    a.Score.v = a.Score.v + 1000\n";

        private World _world = null!;
        private EntityId _red, _blue, _green;

        private ContentLoadResult Content()
        {
            Mod("banking", schema: Schema, defs: Defs);
            return LoadClean();
        }

        [SetUp]
        public void NewWorld()
        {
            _world = new World(Content());
            _red = _world.Create("Red");
            _blue = _world.Create("Blue");
            _green = _world.Create("Green");
            _world.Seat(_red, "host");
            _world.Seat(_blue, "host");
        }

        private Request Start() =>
            _world.Submit("host", "banking.Round",
                new Dictionary<string, Value> { ["a"] = new EntityValue(_red), ["b"] = new EntityValue(_blue) }).OpenedRequest!;

        private AnswerResult Respond(World world, long request, string command, EntityId party, EntityId? target = null)
        {
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(party) };
            if (target != null) args["target"] = new EntityValue(target.Value);
            return world.Respond("host", request, "banking." + command, args);
        }

        private AnswerResult Respond(long request, string command, EntityId party, EntityId? target = null) =>
            Respond(_world, request, command, party, target);

        private static string Log(World w, EntityId id) => ((StrValue)w.Get(id, w.Member("Score", "log"))).Value;
        private static long V(World w, EntityId id) => ((IntValue)w.Get(id, w.Member("Score", "v"))).Value;

        [Test]
        public void TheOuterAskReturnsAfterTheResponseBodyFinishes()
        {
            var turn = Start();
            var attack = Respond(turn.Id, "Attack", _red, _blue);
            Assert.That(attack.Succeeded, Is.True, attack.Message + " " + attack.Error);
            // 결과는 응답 본문이 연 질문을 보여 준다. 바깥 질문은 닫혔고 바깥은 아직 이어 가지 않았다
            var block = attack.OpenedRequest!;
            Assert.That(block.Command, Is.EqualTo("banking.Attack"));
            Assert.That(block.Who, Is.EqualTo(new[] { _blue }));
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { block }));
            Assert.That(Log(_world, _red), Is.EqualTo("A"));
            Assert.That(V(_world, _red), Is.EqualTo(0));

            // 안쪽이 끝나면 같은 호출 안에서 바깥이 r.response·r.by로 이어 가 다음 질문을 연다
            var take = _world.Answer("host", block.Id, _blue, 1);
            Assert.That(take.Succeeded, Is.True, take.Message + " " + take.Error);
            Assert.That(Log(_world, _red), Is.EqualTo("At[Attack]"));
            Assert.That(V(_world, _red), Is.EqualTo(100));
            Assert.That(V(_world, _blue), Is.EqualTo(-3));
            var next = take.OpenedRequest!;
            Assert.That(next.Command, Is.EqualTo("banking.Round"));
            Assert.That(next.Who, Is.EqualTo(new[] { _blue }));
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { next }));
        }

        [Test]
        public void ResponsesNestSeveralLevels()
        {
            var block = Respond(Start().Id, "Attack", _red, _blue).OpenedRequest!;
            var counter = Respond(block.Id, "Counter", _blue);
            Assert.That(counter.Succeeded, Is.True, counter.Message + " " + counter.Error);
            var sure = counter.OpenedRequest!;
            Assert.That(sure.Command, Is.EqualTo("banking.Counter"));
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { sure }));

            // 맨 안쪽이 끝나면 Attack이, 그다음 Round가 이어 간다
            var yes = _world.Answer("host", sure.Id, _blue, 0);
            Assert.That(yes.Succeeded, Is.True, yes.Message + " " + yes.Error);
            Assert.That(Log(_world, _blue), Is.EqualTo("C!"));
            Assert.That(Log(_world, _red), Is.EqualTo("Ac[Attack]"));
            Assert.That(yes.OpenedRequest!.Command, Is.EqualTo("banking.Round"));
        }

        [Test]
        public void AResponseBodyThatWaitsResumesTheOuterWhenItWakes()
        {
            var nap = Respond(Start().Id, "Nap", _red);
            Assert.That(nap.Succeeded, Is.True, nap.Message + " " + nap.Error);
            Assert.That(nap.OpenedRequest, Is.Null);
            Assert.That(nap.SleepsUntil, Is.EqualTo(2));
            Assert.That(_world.OpenRequests, Is.Empty);

            var woke = _world.Advance(2).Reactions.Single();
            Assert.That(woke.Handler + " " + woke.Outcome, Is.EqualTo("banking.Nap Suspended"));
            Assert.That(Log(_world, _red), Is.EqualTo("n[Nap]"));
            Assert.That(woke.OpenedRequest!.Command, Is.EqualTo("banking.Round"));
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { woke.OpenedRequest }));
        }

        [Test]
        public void AResponseBodyThatAdvancesResumesTheOuterInTheSameCall()
        {
            var hop = Respond(Start().Id, "Hop", _red);
            Assert.That(hop.Succeeded, Is.True, hop.Message + " " + hop.Error);
            Assert.That(_world.Tick, Is.EqualTo(2));
            Assert.That(Log(_world, _red), Is.EqualTo("h[Hop]"));
            Assert.That(V(_world, _red), Is.EqualTo(102));
            Assert.That(hop.Reactions.Single().OpenedRequest!.Command, Is.EqualTo("banking.Round"));
            Assert.That(_world.OpenRequests.Single().Who, Is.EqualTo(new[] { _blue }));
        }

        // 안쪽을 이어 가다 오류가 나면 그 구간만 되감기고, 안쪽 질문은 열려 있고 바깥은 계속 기다린다 (C8·C9)
        [Test]
        public void AnErrorInTheResponseBodyAfterItsAnswerLeavesBothWaiting()
        {
            var q = Respond(Start().Id, "Fumble", _red).OpenedRequest!;
            var bad = _world.Answer("host", q.Id, _red, 1);
            Assert.That(bad.Outcome, Is.EqualTo(AnswerOutcome.ScriptError));
            Assert.That(Log(_world, _red), Is.Empty);
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { q }));

            var ok = _world.Answer("host", q.Id, _red, 0);
            Assert.That(ok.Succeeded, Is.True, ok.Message + " " + ok.Error);
            Assert.That(Log(_world, _red), Is.EqualTo("f[Fumble]"));
            Assert.That(ok.OpenedRequest!.Command, Is.EqualTo("banking.Round"));
        }

        // ---- 끊긴 응답: 바깥은 r.interrupted로 이어 간다 (G243) ----

        [Test]
        public void AResponseBodyThatFailsAfterWakingLetsTheOuterGoOnInterrupted()
        {
            Assert.That(Respond(Start().Id, "Doze", _red).SleepsUntil, Is.EqualTo(1));
            var reactions = _world.Advance(1).Reactions;
            Assert.That(reactions.Select(r => r.Signal + " " + r.Handler + " " + r.Outcome), Is.EqualTo(new[]
            {
                "wait banking.Doze ScriptError",
                "interrupted banking.Round Suspended",
            }));
            Assert.That(reactions[1].OpenedRequest!.Who, Is.EqualTo(new[] { _blue }));
            // 멈추기 전에 한 일은 남고, 오류 난 구간만 되감겼다
            Assert.That(Log(_world, _red), Is.EqualTo("z[Doze~]"));
            Assert.That(V(_world, _red), Is.EqualTo(100));
        }

        [Test]
        public void AResponseBodyCanceledForAGoneTargetLetsTheOuterGoOn()
        {
            var block = Respond(Start().Id, "Attack", _red, _green).OpenedRequest!;
            Assert.That(block.Who, Is.EqualTo(new[] { _green }));
            _world.Destroy(_green);
            var reactions = _world.Advance(1).Reactions;
            Assert.That(reactions.Select(r => r.Signal + " " + r.Handler + " " + r.Outcome), Is.EqualTo(new[]
            {
                "stale banking.Attack Canceled",
                "interrupted banking.Round Suspended",
            }));
            Assert.That(Log(_world, _red), Is.EqualTo("A[Attack~]"));
            var next = _world.OpenRequests.Single();
            Assert.That(next.Who, Is.EqualTo(new[] { _blue }));

            // 바깥이 든 r도 끊김을 기억한 채 저장된다
            var save = _world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"interrupted\": true"));
            var loaded = World.Load(Content(), save);
            Assert.That(loaded.Succeeded, Is.True, string.Join("\n", loaded.Diagnostics));
            Assert.That(loaded.World!.Save(), Is.EqualTo(save));
        }

        [Test]
        public void AResponseBodyCutAtTheAdvanceLimitLetsTheOuterGoOnInTheSameCall()
        {
            var leap = Respond(Start().Id, "Leap", _red);
            Assert.That(leap.Succeeded, Is.True, leap.Message + " " + leap.Error);
            Assert.That(leap.Reactions.Select(r => r.Signal + " " + r.Handler + " " + r.Outcome), Is.EqualTo(new[]
            {
                "advance banking.Leap ScriptError",
                "interrupted banking.Round Suspended",
            }));
            Assert.That(Log(_world, _red), Is.EqualTo("l[Leap~]"));
            Assert.That(leap.OpenedRequest!.Who, Is.EqualTo(new[] { _blue }));
        }

        // 바깥이 먼저 사라졌으면 응답 본문은 혼자 끝난다
        [Test]
        public void AResponseBodyWhoseOuterIsGoneFinishesOnItsOwn()
        {
            var w = new World(Content());
            var red = w.Create("Red");
            var blue = w.Create("Blue");
            var green = w.Create("Green");
            foreach (var p in new[] { red, blue, green }) w.Seat(p, "host");
            var turn = w.Submit("host", "banking.Round",
                new Dictionary<string, Value> { ["a"] = new EntityValue(red), ["b"] = new EntityValue(green) }).OpenedRequest!;
            var block = Respond(w, turn.Id, "Attack", red, blue).OpenedRequest!;
            w.Destroy(green);
            Assert.That(w.Advance(1).Reactions.Select(r => r.Signal + " " + r.Handler + " " + r.Outcome),
                Is.EqualTo(new[] { "stale banking.Round Canceled" }));
            var take = w.Answer("host", block.Id, blue, 1);
            Assert.That(take.Succeeded, Is.True, take.Message + " " + take.Error);
            Assert.That(take.OpenedRequest, Is.Null);
            Assert.That(Log(w, red), Is.EqualTo("At"));
            Assert.That(w.OpenRequests, Is.Empty);
        }

        [Test]
        public void AnInterruptedFlagIsFalseOtherwise()
        {
            var turn = Start();
            Respond(turn.Id, "Pass", _red);
            Assert.That(Log(_world, _red), Is.EqualTo("[Pass]"));
        }

        // 불러오기는 스크립트를 돌리지 않으므로, 응답 본문을 취소하면 기다리던 바깥도 함께 취소한다 (G243·G214)
        [Test]
        public void CancelingAResponseBodyWhileLoadingCancelsTheFlowsAwaitingIt()
        {
            var block = Respond(Start().Id, "Attack", _red, _blue).OpenedRequest!;
            Respond(block.Id, "Counter", _blue);
            var save = _world.Save();

            Mod("banking", schema: Schema, defs: Defs.Replace("\"C\"\n", "\"K\"\n"));
            var result = World.Load(LoadClean(), save, cancelBrokenFlows: true);
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV006 flow 3 (banking.Counter) was canceled with its request 3: the function 'banking.Counter' changed since the save",
                "SAV006 flow 2 (banking.Attack) was canceled: the response it awaited, flow 3 (banking.Counter), was canceled",
                "SAV006 flow 1 (banking.Round) was canceled: the response it awaited, flow 2 (banking.Attack), was canceled",
            }));
            var w = result.World!;
            Assert.That(w.OpenRequests, Is.Empty);
            Assert.That(Log(w, _blue), Is.EqualTo("C"), "what the flows did before pausing stays");
            Assert.That(Encoding.UTF8.GetString(w.Save()), Does.Contain("\"flows\": []"));
        }

        [Test]
        public void ASaveWhoseAwaitedFlowIsMissingIsCorrupt()
        {
            Respond(Start().Id, "Attack", _red, _blue);
            var text = Encoding.UTF8.GetString(_world.Save());
            // 바깥이 저장에 없는 Flow 5를 기다린다
            var broken = text.Replace("\"awaits\": 2", "\"awaits\": 5").Replace("\"flow\": 3,\n    \"request\"", "\"flow\": 9,\n    \"request\"");
            Assert.That(broken, Does.Contain("\"awaits\": 5").And.Contain("\"flow\": 9,"));
            var result = World.Load(Content(), Encoding.UTF8.GetBytes(broken));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(string.Join("\n", result.Diagnostics), Does.Contain("flow 1 (banking.Round) awaits flow 5, which is not in the save"));
        }

        // ---- 저장 (C4) ----

        private static readonly (string Kind, string Command, long Choice, bool BlueActs, bool Target)[] Steps =
        {
            ("respond", "Attack", 0, false, true), // 빨강이 파랑을 공격 → 파랑에게 Block
            ("respond", "Counter", 0, true, false), // 파랑이 반격 → 파랑에게 Sure
            ("answer", "", 0, true, false), // Counter, Attack, Round 차례로 끝나 파랑에게 Turn
            ("respond", "Pass", 0, true, false), // 빨강에게 Turn
            ("respond", "Attack", 0, false, true), // 파랑에게 Block
            ("answer", "", 1, true, false), // 맞는다 → Round 끝
        };

        private (List<string> Log, string Final) Run(int saveBefore)
        {
            var world = new World(Content());
            var red = world.Create("Red");
            var blue = world.Create("Blue");
            world.Seat(red, "host");
            world.Seat(blue, "host");
            var log = new List<string>();
            for (int step = 0; step <= Steps.Length; step++)
            {
                if (step == saveBefore)
                {
                    var bytes = world.Save();
                    var result = World.Load(Content(), bytes);
                    Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
                    world = result.World!;
                    Assert.That(Encoding.UTF8.GetString(world.Save()), Is.EqualTo(Encoding.UTF8.GetString(bytes)), "C4 at step " + step);
                }
                if (step == 0)
                {
                    var r = world.Submit("host", "banking.Round",
                        new Dictionary<string, Value> { ["a"] = new EntityValue(red), ["b"] = new EntityValue(blue) });
                    log.Add(r.Outcome + " " + r.OpenedRequest?.Id);
                    continue;
                }
                var open = world.OpenRequests.Single();
                var s = Steps[step - 1];
                var who = s.BlueActs ? blue : red;
                var a = s.Kind == "answer"
                    ? world.Answer("host", open.Id, who, s.Choice)
                    : Respond(world, open.Id, s.Command, who, s.Target ? blue : (EntityId?)null);
                log.Add(a.Outcome + " " + a.Error + " " + a.OpenedRequest?.Id + " " + a.OpenedRequest?.Command);
            }
            log.Add(Log(world, red) + " " + V(world, red) + " " + Log(world, blue) + " " + V(world, blue));
            return (log, Encoding.UTF8.GetString(world.Save()));
        }

        [Test]
        public void NestedFlowsContinueTheSameAfterLoadingAtAnyStep()
        {
            var reference = Run(-1);
            Assert.That(reference.Log.Last(), Is.EqualTo("Ac[Attack][Pass]At[Attack] 1200 C! 97"), string.Join("\n", reference.Log));
            for (int k = 0; k <= Steps.Length; k++)
            {
                var run = Run(k);
                Assert.That(run.Log, Is.EqualTo(reference.Log), "saved before step " + k);
                Assert.That(run.Final, Is.EqualTo(reference.Final), "saved before step " + k);
            }
        }

        [Test]
        public void TheSaveRecordsBothSidesOfTheLink()
        {
            var block = Respond(Start().Id, "Attack", _red, _blue).OpenedRequest!;
            Respond(block.Id, "Counter", _blue);
            var text = Encoding.UTF8.GetString(_world.Save());
            Assert.That(text, Does.Contain("\"awaits\": 2"));
            Assert.That(text, Does.Contain("\"awaits\": 3"));
            Assert.That(text, Does.Contain("\"response\": \"Attack\""));
            Assert.That(text, Does.Contain("\"response\": \"Counter\""));
        }
    }
}
