using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary><c>ask</c>로 멈추고 답으로 이어서 실행하기 (spec S2 평가 모델·X1·C2·C3·C8·C9, spec-gaps G136~G138).</summary>
    public class WorldAskTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\nParty(\"Green\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Branch(p: Ref[Party]):\n" +
            "    p.Score.v = 100\n" +
            "    r = ask(p, \"Q\", choices=[\"low\", \"high\"], hint=\"std.choice\", data={\"n\": 2, \"who\": p})\n" +
            "    if r.index == 1:\n" +
            "        x = 50\n" +
            "    else:\n" +
            "        x = 5\n" +
            "    p.Score.v = p.Score.v + x\n" +
            "def pick_three(p):\n" +
            "    total = 0\n" +
            "    for i in range(3):\n" +
            "        r = ask(p, text(\"Pick\", round=i), choices=[\"a\", \"b\"])\n" +
            "        total = total * 10 + r.index + 1\n" +
            "        p.Score.v = p.Score.v + 1\n" +
            "    return total\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Loop(p: Ref[Party]):\n" +
            "    n = pick_three(p)\n" +
            "    p.Score.v = n\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Target(p: Ref[Party], a: Ref[Party], b: Ref[Party]):\n" +
            "    r = ask([p, a], \"Hit\", choices=[a, b])\n" +
            "    r.choice.Score.v = 7\n" +
            "    r.by.Score.v = r.by.Score.v + 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Boom(p: Ref[Party]):\n" +
            "    r = ask(p, \"Q\", choices=[\"ok\", \"bad\"])\n" +
            "    p.Score.v = 1\n" +
            "    if r.index == 1:\n" +
            "        p.Score.v = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Silent(p: Ref[Party]):\n" +
            "    ask(p, \"Q\", choices=[\"ok\"])\n" +
            "    p.Score.v = 9\n" +
            // 응답 커맨드 (G139): 우선권 한 바퀴처럼 번갈아 묻고 둘 다 연속으로 넘기면 끝
            "@command(acts_for=lambda party: party)\n" +
            "def Pass(party: Ref[Party]):\n" +
            "    pass\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Bid(party: Ref[Party], amount: int):\n" +
            "    with preconditions:\n" +
            "        require(amount > 0, text(\"bid.positive\"))\n" +
            "    party.Score.v = party.Score.v + amount\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Crash(party: Ref[Party]):\n" +
            "    party.Score.v = 50\n" +
            "    party.Score.v = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Round(a: Ref[Party], b: Ref[Party]):\n" +
            "    passes = 0\n" +
            "    for i in range(6):\n" +
            "        p = a if i % 2 == 0 else b\n" +
            "        r = ask(p, \"Bid\", responses=[\"Pass\", \"Bid\", \"Crash\"], choices=[\"Fold\"])\n" +
            "        if r.response == \"Pass\":\n" +
            "            passes += 1\n" +
            "            if passes == 2:\n" +
            "                break\n" +
            "        elif r.response == \"Bid\":\n" +
            "            passes = 0\n" +
            "        else:\n" +
            "            r.by.Score.v = -1\n" +
            "            break\n" +
            "    a.Score.v = a.Score.v + 1000\n";

        private World _world = null!;
        private EntityId _red, _blue, _green;
        private MemberHandle _v = null!;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("banking:Red");
            _blue = _world.Create("banking:Blue");
            _green = _world.Create("banking:Green");
            foreach (var p in new[] { _red, _blue, _green }) _world.Seat(p, "host");
            _v = _world.Member("Score", "v");
        }

        private SubmitResult Submit(string command, params (string Name, EntityId Id)[] parties)
        {
            var args = new Dictionary<string, Value>();
            foreach (var (name, id) in parties) args[name] = new EntityValue(id);
            return _world.Submit("host", "banking." + command, args);
        }

        private long V(EntityId id) => ((IntValue)_world.Get(id, _v)).Value;

        [Test]
        public void TheBodyStopsAtAskAndKeepsWhatItDidSoFar()
        {
            var r = Submit("Branch", ("p", _red));
            Assert.That(r.Succeeded, Is.True);
            Assert.That(V(_red), Is.EqualTo(100));
            var q = r.OpenedRequest!;
            Assert.That(_world.OpenRequests.Single(), Is.SameAs(q));
            Assert.That(q.Command, Is.EqualTo("banking.Branch"));
            Assert.That(q.Who, Is.EqualTo(new[] { _red }));
            Assert.That(((StrValue)q.Title).Value, Is.EqualTo("Q"));
            Assert.That(q.Choices.Select(c => ((StrValue)c.Value).Value), Is.EqualTo(new[] { "low", "high" }));
            Assert.That(q.Hint, Is.EqualTo("std.choice"));
            var data = (DictValue)q.Data;
            Assert.That(((EntityValue)data.Items[1].Value).Id, Is.EqualTo(_red)); // 엔티티는 저장 가능한 id로
        }

        // C2: ask 뒤의 문장이 선택 분기가 바꾼 값을 본다
        [Test]
        public void TheStatementsAfterAskSeeWhatTheChosenBranchDid()
        {
            var q = Submit("Branch", ("p", _red)).OpenedRequest!;
            var a = _world.Answer("host", q.Id, _red, 1);
            Assert.That(a.Succeeded, Is.True, a.Message);
            Assert.That(a.OpenedRequest, Is.Null);
            Assert.That(V(_red), Is.EqualTo(150));
            Assert.That(_world.OpenRequests, Is.Empty);
        }

        // C3: 반복 중간에서 재개해도 끝난 바퀴를 다시 실행하지 않는다. 멈춤은 함수 호출 한 단계 아래에 있다 (G136)
        [Test]
        public void ALoopInsideAHelperResumesWhereItStopped()
        {
            var q = Submit("Loop", ("p", _red)).OpenedRequest!;
            var ids = new List<long> { q.Id };
            foreach (var pick in new[] { 1, 0 })
            {
                var a = _world.Answer("host", q.Id, _red, pick);
                Assert.That(a.Succeeded, Is.True, a.Message);
                q = a.OpenedRequest!;
                Assert.That(q.Flow, Is.EqualTo(_world.OpenRequests.Single().Flow));
                ids.Add(q.Id);
            }
            Assert.That(V(_red), Is.EqualTo(2)); // 두 바퀴만 끝났다
            Assert.That(((IntValue)((TextValue)q.Title).Args["round"]).Value, Is.EqualTo(2));
            Assert.That(_world.Answer("host", q.Id, _red, 1).Succeeded, Is.True);
            Assert.That(V(_red), Is.EqualTo(212)); // 2·1·2, 돌려준 값을 커맨드가 받았다
            Assert.That(ids.Distinct().Count(), Is.EqualTo(3)); // 질문마다 새 번호
        }

        [Test]
        public void ChoicesCanBeEntitiesAndTheAnswerSaysWhoAnswered()
        {
            var q = Submit("Target", ("p", _red), ("a", _blue), ("b", _green)).OpenedRequest!;
            Assert.That(q.Who, Is.EqualTo(new[] { _red, _blue }));
            Assert.That(q.Choices.Select(c => ((EntityValue)c.Value).Id), Is.EqualTo(new[] { _blue, _green }));
            Assert.That(_world.Answer("host", q.Id, _blue, 1).Succeeded, Is.True);
            Assert.That(V(_green), Is.EqualTo(7));
            Assert.That(V(_blue), Is.EqualTo(1));
        }

        [Test]
        public void AskOnItsOwnLineDiscardsTheAnswer()
        {
            var q = Submit("Silent", ("p", _red)).OpenedRequest!;
            Assert.That(_world.Answer("host", q.Id, _red, 0).Succeeded, Is.True);
            Assert.That(V(_red), Is.EqualTo(9));
        }

        // 이어서 실행하는 사본에서도 람다는 파이썬처럼 만든 함수의 지역 변수를 늦게 읽는다 (G104)
        [Test]
        public void ALambdaSeesLocalsAssignedAfterResume()
        {
            Mod("banking", schema: Schema, defs: "Party(\"Red\")\n@command(free=True, acts_for=None)\ndef Late(p: Ref[Party]):\n" +
                "    k = lambda: n\n    n = 1\n    r = ask(p, \"Q\", choices=[\"a\"])\n    n = 2\n    p.Score.v = k()\n");
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var q = world.Submit("host", "banking.Late", new Dictionary<string, Value> { ["p"] = new EntityValue(red) }).OpenedRequest!;
            Assert.That(world.Answer("host", q.Id, red, 0).Succeeded, Is.True);
            Assert.That(((IntValue)world.Get(red, world.Member("Score", "v"))).Value, Is.EqualTo(2));
        }

        private long RunLate(string body)
        {
            Mod("banking", schema: Schema, defs: "Party(\"Red\")\n" +
                "def make(h):\n    k = lambda: h()\n    return k\n" +
                "@command(free=True, acts_for=None)\ndef Late(p: Ref[Party]):\n" + body);
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var s = world.Submit("host", "banking.Late", new Dictionary<string, Value> { ["p"] = new EntityValue(red) });
            Assert.That(s.Succeeded, Is.True, s.Error?.ToString());
            var a = world.Answer("host", s.OpenedRequest!.Id, red, 0);
            Assert.That(a.Succeeded, Is.True, a.Error?.ToString());
            return ((IntValue)world.Get(red, world.Member("Score", "v"))).Value;
        }

        // 람다는 파이썬처럼 객체로 비교하고, 이어서 실행해도 같은 객체다 (G143)
        [Test]
        public void ALambdaKeepsItsIdentityAcrossResume()
        {
            Assert.That(RunLate(
                "    f = lambda: 1\n    g = f\n    h = lambda: 1\n    r = ask(p, \"Q\", choices=[\"a\"])\n" +
                "    p.Score.v = (10 if f == g else 0) + (1 if f == h else 0)\n"), Is.EqualTo(10));
        }

        // 컴포넌트 값·text 안의 람다도 이어서 실행한 사본에서 같은 객체다 (G143)
        [Test]
        public void ALambdaInsideAValueKeepsItsIdentityAcrossResume()
        {
            Assert.That(RunLate(
                "    lam = lambda: 1\n    c = Score(v=lam)\n    t = text(\"k\", f=lam)\n    r = ask(p, \"Q\", choices=[\"a\"])\n" +
                "    p.Score.v = (10 if c == Score(v=lam) else 0) + (1 if t == text(\"k\", f=lam) else 0)\n"), Is.EqualTo(11));
        }

        // 끝난 함수의 지역 변수가 잡은 람다도 이어서 실행한 사본의 지역 변수를 읽는다 (G143)
        [Test]
        public void ALambdaHeldByAFinishedFunctionSeesLocalsAfterResume()
        {
            Assert.That(RunLate(
                "    n = 1\n    h = lambda: n\n    k = make(h)\n    r = ask(p, \"Q\", choices=[\"a\"])\n    n = 2\n    p.Score.v = k()\n"),
                Is.EqualTo(2));
        }

        // ---- 거절 (C9) ----

        [Test]
        public void AConsumedOrUnknownRequestIsRejected()
        {
            var q = Submit("Branch", ("p", _red)).OpenedRequest!;
            Assert.That(_world.Answer("host", q.Id, _red, 0).Succeeded, Is.True);
            var again = _world.Answer("host", q.Id, _red, 1);
            Assert.That(again.Outcome, Is.EqualTo(AnswerOutcome.UnknownRequest));
            Assert.That(V(_red), Is.EqualTo(105));
            Assert.That(_world.Answer("host", 999, _red, 0).Outcome, Is.EqualTo(AnswerOutcome.UnknownRequest));
        }

        [Test]
        public void AnEntityNotAskedOrAChoiceOutOfRangeIsRejected()
        {
            var q = Submit("Branch", ("p", _red)).OpenedRequest!;
            var notListed = _world.Answer("host", q.Id, _blue, 0);
            Assert.That(notListed.Outcome, Is.EqualTo(AnswerOutcome.NotListed));
            Assert.That(notListed.Message, Does.Contain("allowed: [" + _red + "]"));
            Assert.That(_world.Answer("host", q.Id, _red, 2).Outcome, Is.EqualTo(AnswerOutcome.BadChoice));
            Assert.That(_world.Answer("host", q.Id, _red, -1).Outcome, Is.EqualTo(AnswerOutcome.BadChoice));
            Assert.That(_world.OpenRequests.Single(), Is.SameAs(q));
            Assert.That(V(_red), Is.EqualTo(100));
        }

        // C8·C9: 이어서 실행하다 오류가 나면 그 구간만 되감기고 질문은 그대로 열려 있다
        [Test]
        public void AScriptErrorAfterAnAnswerLeavesTheRequestOpen()
        {
            var q = Submit("Boom", ("p", _red)).OpenedRequest!;
            var bad = _world.Answer("host", q.Id, _red, 1);
            Assert.That(bad.Outcome, Is.EqualTo(AnswerOutcome.ScriptError));
            Assert.That(bad.Error!.Message, Does.Contain("banking.Boom"));
            Assert.That(V(_red), Is.EqualTo(0));
            Assert.That(_world.OpenRequests.Single(), Is.SameAs(q));
            // 같은 질문에 다시 답할 수 있다
            Assert.That(_world.Answer("host", q.Id, _red, 0).Succeeded, Is.True);
            Assert.That(V(_red), Is.EqualTo(1));
        }

        [Test]
        public void SeveralFlowsWaitAtOnceInFlowOrder()
        {
            var first = Submit("Branch", ("p", _red)).OpenedRequest!;
            var second = Submit("Branch", ("p", _blue)).OpenedRequest!;
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { first, second }));
            Assert.That(_world.Answer("host", second.Id, _blue, 0).Succeeded, Is.True);
            Assert.That(_world.OpenRequests, Is.EqualTo(new[] { first }));
            Assert.That(V(_blue), Is.EqualTo(105));
        }

        [Test]
        public void TheSeatTableGatesAnswers()
        {
            Mod("banking", schema: Schema, defs: Defs);
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var q = world.Submit("host", "banking.Branch",
                new Dictionary<string, Value> { ["p"] = new EntityValue(red) }).OpenedRequest!;
            var denied = world.Answer("guest", q.Id, red, 0);
            Assert.That(denied.Outcome, Is.EqualTo(AnswerOutcome.GateDenied));
            Assert.That(denied.Message, Is.EqualTo("'guest' is not seated at " + red + ", so it cannot answer request " + q.Id));
            Assert.That(world.Answer("host", q.Id, red, 0).Succeeded, Is.True);
        }

        // ---- 응답 커맨드 (G139) ----

        private AnswerResult Respond(long request, string command, EntityId party, params (string Name, Value Value)[] more)
        {
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(party) };
            foreach (var (name, value) in more) args[name] = value;
            return _world.Respond("host", request, "banking." + command, args);
        }

        [Test]
        public void ResponseCommandsRunAndAskSeesTheirName()
        {
            var q = Submit("Round", ("a", _red), ("b", _blue)).OpenedRequest!;
            Assert.That(q.Responses, Is.EqualTo(new[] { "banking.Pass", "banking.Bid", "banking.Crash" }));
            var bid = Respond(q.Id, "Bid", _red, ("amount", new IntValue(5)));
            Assert.That(bid.Succeeded, Is.True, bid.Message);
            Assert.That(V(_red), Is.EqualTo(5));
            q = bid.OpenedRequest!;
            Assert.That(q.Who, Is.EqualTo(new[] { _blue }));
            q = Respond(q.Id, "Pass", _blue).OpenedRequest!;
            var last = Respond(q.Id, "Pass", _red);
            Assert.That(last.Succeeded, Is.True, last.Message);
            Assert.That(last.OpenedRequest, Is.Null);
            Assert.That(V(_red), Is.EqualTo(1005));
        }

        [Test]
        public void ChoicesAndResponsesCanBeMixed()
        {
            var q = Submit("Round", ("a", _red), ("b", _blue)).OpenedRequest!;
            Assert.That(_world.Answer("host", q.Id, _red, 0).Succeeded, Is.True);
            Assert.That(V(_red), Is.EqualTo(999)); // r.response가 None이라 else, r.by = red
        }

        [Test]
        public void AResponseIsRejectedBeforeItRuns()
        {
            var q = Submit("Round", ("a", _red), ("b", _blue)).OpenedRequest!;
            Assert.That(Respond(q.Id, "Bid", _blue, ("amount", new IntValue(5))).Outcome, Is.EqualTo(AnswerOutcome.NotListed));
            var wrongArg = Respond(q.Id, "Bid", _red, ("amount", new StrValue("x")));
            Assert.That(wrongArg.Outcome, Is.EqualTo(AnswerOutcome.ArgumentsRejected));
            Assert.That(wrongArg.ArgumentRejections.Single().Parameter, Is.EqualTo("amount"));
            var failed = Respond(q.Id, "Bid", _red, ("amount", new IntValue(0)));
            Assert.That(failed.Outcome, Is.EqualTo(AnswerOutcome.PreconditionsFailed));
            Assert.That(failed.Preconditions!.Failures.Single().MessageKey, Is.EqualTo("bid.positive"));
            Assert.That(_world.OpenRequests.Single(), Is.SameAs(q));
            Assert.That(V(_red), Is.EqualTo(0));
        }

        [Test]
        public void AResponseCommandTheRequestDoesNotTakeIsRejected()
        {
            var q = Submit("Branch", ("p", _red)).OpenedRequest!;
            var r = Respond(q.Id, "Pass", _red);
            Assert.That(r.Outcome, Is.EqualTo(AnswerOutcome.NotAccepted));
            Assert.That(r.Message, Does.Contain("it accepts: [] and choices"));
        }

        [Test]
        public void AFailingResponseBodyIsUndoneAndTheRequestStaysOpen()
        {
            var q = Submit("Round", ("a", _red), ("b", _blue)).OpenedRequest!;
            var r = Respond(q.Id, "Crash", _red);
            Assert.That(r.Outcome, Is.EqualTo(AnswerOutcome.ScriptError));
            Assert.That(V(_red), Is.EqualTo(0));
            Assert.That(_world.OpenRequests.Single(), Is.SameAs(q));
        }

        [Test]
        public void AResponseCommandCannotBeSubmittedOnItsOwn()
        {
            var r = Submit("Pass", ("party", _red));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.NeedsRequest));
        }

        // ---- 잘못 쓴 ask ----

        private static string Command(string body) =>
            "Party(\"Red\")\n@command(free=True, acts_for=None)\ndef C(p: Ref[Party]):\n" + body;

        private SubmitResult Run(string body)
        {
            Mod("banking", schema: Schema, defs: Command(body));
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            return world.Submit("host", "banking.C", new Dictionary<string, Value> { ["p"] = new EntityValue(red) });
        }

        [TestCase("    r = ask(p, \"Q\")\n", "ask() needs choices=[...] or responses=[...]")]
        [TestCase("    r = ask(p, \"Q\", choices=[])\n", "non-empty list")]
        [TestCase("    r = ask(3, \"Q\", choices=[\"a\"])\n", "ask() asks entities, not int")]
        [TestCase("    r = ask(p, 3, choices=[\"a\"])\n", "title of ask()")]
        [TestCase("    r = ask(p, \"Q\", responses=[\"Nope\"])\n", "there is no command 'banking.Nope'")]
        [TestCase("    r = ask(p, \"Q\", responses=[\"C\"])\n", "banking.C is a free command, so it cannot answer a question")]
        [TestCase("    r = ask(p, \"Q\", responses=[3])\n", "takes command names (str), not int")]
        [TestCase("    r = ask(p, \"Q\", choices=[\"a\"], hnit=\"x\")\n", "ask() has no argument 'hnit'")]
        [TestCase("    r = ask(p, \"Q\", choices=[lambda x: x])\n", "cannot hold")]
        public void MisusedAskIsAScriptError(string body, string message)
        {
            var r = Run(body);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
            Assert.That(_world.OpenRequests, Is.Empty);
        }

        [Test]
        public void AnUnknownAttributeOfTheAnswerIsAScriptError()
        {
            Mod("banking", schema: Schema, defs: Command("    r = ask(p, \"Q\", choices=[\"a\"])\n    n = r.nope\n"));
            var world = new World(LoadClean());
            var red = world.Create("banking:Red");
            world.Seat(red, "host");
            var q = world.Submit("host", "banking.C", new Dictionary<string, Value> { ["p"] = new EntityValue(red) }).OpenedRequest!;
            var a = world.Answer("host", q.Id, red, 0);
            Assert.That(a.Outcome, Is.EqualTo(AnswerOutcome.ScriptError));
            Assert.That(a.Error!.Message, Does.Contain("an answer has no attribute 'nope'"));
        }

        private IReadOnlyList<Diagnostic> LoadWith(string defs)
        {
            Mod("banking", schema: Schema, defs: defs);
            Load(out var diagnostics);
            return diagnostics;
        }

        [Test]
        public void AskInsideAnExpressionIsALoadError()
        {
            var d = LoadWith(Command("    n = ask(p, \"Q\", choices=[\"a\"]).index + 1\n"))
                .Single(x => x.Code == Codes.CmdWriteEffect);
            Assert.That(d.Message, Is.EqualTo("ask() waits for an answer, so it can only be called as a statement"));
        }

        [Test]
        public void AskInPreconditionsIsALoadError()
        {
            var diags = LoadWith(Command(
                "    with preconditions:\n        r = ask(p, \"Q\", choices=[\"a\"])\n        require(True, text(\"m\"))\n    pass\n"));
            Assert.That(diags.Any(x => x.Code == Codes.CmdWriteEffect && x.Message.StartsWith("ask() waits")), Is.True,
                string.Join("\n", diags));
        }

        [Test]
        public void AFunctionThatAsksCannotBeAPureSlot()
        {
            var diags = LoadWith(Command("    pass\n") +
                "def helper(p):\n    r = ask(p, \"Q\", choices=[\"a\"])\n    return 1\n" +
                "@command(free=True, acts_for=None)\ndef D(p: Ref[Party]):\n    n = helper(p) + 1\n");
            Assert.That(diags.Any(x => x.Code == Codes.CmdWriteEffect && x.Message.Contains("'helper'")), Is.True,
                string.Join("\n", diags));
        }

        // 식 안의 호출을 이름으로 잡을 수 없는 길(함수 값)은 실행 중 안전망이 막는다 (G136)
        [Test]
        public void AskReachedThroughAPureCallIsARuntimeError()
        {
            var r = Run("    f = helper\n    n = f(p) + 1\n" +
                "def helper(p):\n    r = ask(p, \"Q\", choices=[\"a\"])\n    return 1\n");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("ask() waits for an answer, so it cannot be called here"));
        }
    }
}
