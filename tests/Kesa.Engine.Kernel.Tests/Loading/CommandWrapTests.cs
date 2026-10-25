using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 다른 모드의 커맨드를 감싸기 (spec-gaps G316~G321): 감싸는 쪽의 사전 조건은 원래 것 뒤에 더해지고(합법 수에도), 본문은
    /// <c>original(...)</c> 앞뒤에 쓰며, 원래 본문은 사전 조건을 검사한 지역 변수 그대로 낸 인자로 한 번 돈다.
    /// </summary>
    public class CommandWrapTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Mark\", n=field(int, default=0))\n" +
            "deftype(\"Ship\", components=[\"Mark\"])\n";

        private const string Core =
            "@command(free=True, acts_for=None)\ndef Take(s: Ref[Ship], n: int):\n" +
            "    with preconditions:\n        require(s.Mark.n < 100, text(\"banking-full\"))\n        choose(n, from_=[1, 2, 3])\n" +
            "    s.Mark.n += n\n" +
            "@command(free=True, acts_for=None)\ndef Rest(s: Ref[Ship]):\n    pass\n" +
            "@command(free=True, acts_for=None)\ndef Keep(s: Ref[Ship]):\n" +
            "    with preconditions:\n        k = s.Mark.n\n    s.Mark.n = k + 1\n" +
            "@command(free=True, acts_for=None)\ndef Step(s: Ref[Ship]):\n" +
            "    with preconditions:\n        k = s.Mark.n\n    r = ask(s, \"Go\", choices=[1, 2])\n    s.Mark.n = k + r.choice\n" +
            "Ship(\"Sloop\")\n";

        private const string Depends = ", dependencies=[\"banking\"]";

        private World _world = null!;
        private EntityId _sloop;

        private void Start(params (string Id, string Defs, string Extra)[] mods)
        {
            Mod("banking", schema: Schema, defs: Core);
            foreach (var m in mods) Mod(m.Id, defs: m.Defs, extra: m.Extra);
            _world = new World(LoadClean());
            _sloop = _world.Create("banking:Sloop");
        }

        private SubmitResult Submit(string command, long? n = null)
        {
            var args = new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop) };
            if (n != null) args["n"] = new IntValue(n.Value);
            return _world.Submit("host", command, args);
        }

        private long N() => ((IntValue)_world.Get(_sloop, _world.Member("Mark", "n"))).Value;

        private void SetN(long n) => _world.Set(_sloop, _world.Member("Mark", "n"), new IntValue(n));

        private const string NotTwo =
            "@wrap(banking.Take)\ndef no_two(original, s, n):\n" +
            "    with preconditions:\n        require(n != 2, text(\"scouts-not-two\"))\n" +
            "    original(s, n)\n";

        [Test]
        public void AWrapperAddsAPrecondition()
        {
            Start(("scouts", NotTwo, Depends));
            var r = Submit("banking.Take", 2);
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.Preconditions!.Failures.Single().MessageKey, Is.EqualTo("scouts-not-two"));
            Assert.That(Submit("banking.Take", 3).Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(3));
        }

        [Test]
        public void LegalMovesKeepTheAddedPrecondition()
        {
            Start(("scouts", NotTwo, Depends));
            var moves = _world.LegalMoves("banking.Take", new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop) }, 10).Moves;
            Assert.That(moves.Select(m => ((IntValue)m["n"]).Value), Is.EqualTo(new[] { 1L, 3L }));
        }

        [Test]
        public void PreconditionsRunFromTheCommandOutwardAndAllAreReported()
        {
            // 원래 것, 안쪽 감싸기(scouts), 바깥 감싸기(pirates, scouts 뒤에 로드) 순. 모두 실패해도 모두 보고한다 (G318)
            Start(("scouts", "@wrap(banking.Take)\ndef a(original, s, n):\n    with preconditions:\n" +
                             "        require(n == 9, text(\"scouts-nine\"))\n    original(s, n)\n", Depends),
                ("pirates", "@wrap(banking.Take)\ndef b(original, s, n):\n    with preconditions:\n" +
                            "        require(False, text(\"pirates-never\"))\n    original(s, n)\n",
                    ", dependencies=[\"banking\", \"scouts\"]"));
            SetN(100);
            var r = Submit("banking.Take", 1);
            Assert.That(r.Preconditions!.Failures.Select(f => f.MessageKey),
                Is.EqualTo(new[] { "banking-full", "scouts-nine", "pirates-never" }));
        }

        [Test]
        public void TheBodyRunsAroundTheCommand()
        {
            Start(("scouts", "@wrap(banking.Take)\ndef around(original, s, n):\n    s.Mark.n += 100\n    original(s, n)\n    s.Mark.n *= 2\n",
                Depends));
            Assert.That(Submit("banking.Take", 1).Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(202));
        }

        [Test]
        public void BodiesNestInLoadOrder()
        {
            // 바깥(pirates)이 먼저: *10 다음 안쪽 scouts가 +1 다음 원래 +2 → 0*10+1+2
            Start(("scouts", "@wrap(banking.Take)\ndef a(original, s, n):\n    s.Mark.n += 1\n    original(s, n)\n", Depends),
                ("pirates", "@wrap(banking.Take)\ndef b(original, s, n):\n    s.Mark.n = 5\n    original(s, n)\n    s.Mark.n *= 10\n",
                    ", dependencies=[\"banking\", \"scouts\"]"));
            Assert.That(Submit("banking.Take", 2).Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(80));
        }

        [Test]
        public void AWrapperMaySkipTheCommand()
        {
            Start(("scouts", "@wrap(banking.Take)\ndef skip(original, s, n):\n    if n == 3:\n        return\n    original(s, n)\n", Depends));
            Assert.That(Submit("banking.Take", 3).Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(0));
            Assert.That(Submit("banking.Take", 1).Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(1));
        }

        [Test]
        public void AWrapperOfACommandThatChangesNothingMayChangeTheWorld()
        {
            // 본문이 pass뿐인 커맨드도 감싸는 쪽은 커맨드 본문처럼 쓴다 (G320)
            Start(("scouts", "@wrap(banking.Rest)\ndef rest(original, s):\n    original(s)\n    s.Mark.n = 7\n", Depends));
            Assert.That(Submit("banking.Rest").Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(7));
        }

        [Test]
        public void TheCommandKeepsTheValuesItsPreconditionsComputed()
        {
            // 사전 조건의 k는 0일 때 계산했다: 감싸는 쪽이 그 뒤에 50으로 바꿔도 원래 본문은 k = 0으로 돈다 (G321)
            Start(("scouts", "@wrap(banking.Keep)\ndef keep(original, s):\n    s.Mark.n = 50\n    original(s)\n", Depends));
            Assert.That(Submit("banking.Keep").Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(1));
        }

        [Test]
        public void TheCommandRunsOnlyOnce()
        {
            Start(("scouts", "@wrap(banking.Take)\ndef twice(original, s, n):\n    for i in range(2):\n        original(s, n)\n", Depends));
            var r = Submit("banking.Take", 1);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("already ran in this wrapper"));
            Assert.That(N(), Is.EqualTo(0), "rolled back");
        }

        [Test]
        public void TheCommandGetsTheSubmittedArguments()
        {
            Start(("scouts", "@wrap(banking.Take)\ndef swap(original, s, n):\n    n = 3\n    original(s, n)\n", Depends));
            var r = Submit("banking.Take", 1);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("must get the submitted 'n'"));
        }

        [Test]
        public void APauseBeforeTheCommandSavesAndLoads()
        {
            // 감싸는 쪽이 원래 커맨드 전에 묻고 멈춘다: 사전 조건을 마친 안쪽 프레임(k = 4)이 저장에 남는다
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: "@wrap(banking.Keep)\ndef first(original, s):\n    r = ask(s, \"Sure\", choices=[1, 2])\n" +
                                "    s.Mark.n = 50\n    original(s)\n    s.Mark.n += r.choice * 100\n", extra: Depends);
            var content = LoadClean();
            _world = new World(content);
            _sloop = _world.Create("banking:Sloop");
            SetN(4);
            Assert.That(Submit("banking.Keep").Succeeded, Is.True);
            var save = _world.Save();
            var loaded = World.Load(content, save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            Assert.That(_world.Save(), Is.EqualTo(save));
            _world.Seat(_sloop, "alice");
            var answer = _world.Answer("alice", _world.OpenRequests.Single().Id, _sloop, 1);
            Assert.That(answer.Succeeded, Is.True, answer.Outcome + " " + answer.Message + " " + answer.Error);
            Assert.That(N(), Is.EqualTo(205));
        }

        [Test]
        public void APauseInsideTheCommandSavesAndLoads()
        {
            // 원래 본문의 ask에서 멈춘다: 감싸는 쪽의 안쪽은 "이미 부름"으로 저장되어 다시 부를 수 없다
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: "@wrap(banking.Step)\ndef around(original, s):\n    original(s)\n    s.Mark.n *= 10\n", extra: Depends);
            var content = LoadClean();
            _world = new World(content);
            _sloop = _world.Create("banking:Sloop");
            SetN(3);
            Assert.That(Submit("banking.Step").Succeeded, Is.True);
            var save = _world.Save();
            Assert.That(System.Text.Encoding.UTF8.GetString(save), Does.Contain("\"used\""));
            var loaded = World.Load(content, save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            Assert.That(_world.Save(), Is.EqualTo(save));
            _world.Seat(_sloop, "alice");
            var answer = _world.Answer("alice", _world.OpenRequests.Single().Id, _sloop, 1);
            Assert.That(answer.Succeeded, Is.True, answer.Outcome + " " + answer.Message + " " + answer.Error);
            Assert.That(N(), Is.EqualTo(50));
        }

        // ---- 로드 오류 ----

        private Diagnostic Single(string scouts)
        {
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: scouts, extra: Depends);
            Load(out var diags);
            return diags.Single();
        }

        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    with preconditions:\n        choose(n, from_=[1])\n    original(s, n)\n",
            Codes.CmdPreconditions, "a @wrap only adds conditions, so it has no choose()")]
        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    with preconditions:\n        cost(s.Mark.n, 1)\n    original(s, n)\n",
            Codes.CmdPreconditions, "a @wrap only adds conditions, so it has no cost()")]
        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    original(s, 3)\n",
            Codes.CmdBadDecorator, "the inner command must get the submitted arguments as they are: original(s, n)")]
        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    original(n, s)\n",
            Codes.CmdBadDecorator, "the inner command must get the submitted arguments as they are: original(s, n)")]
        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    x = [original(s, n)]\n",
            Codes.CmdWriteEffect, "'Take' is a command, so the inner command can only be called as a statement")]
        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    f = original\n",
            Codes.CmdBadDecorator, "'original' is the inner command of @wrap, so it can only be called")]
        [TestCase("@wrap(banking.Take)\ndef w(original, s, n):\n    with preconditions:\n        r = random.randint(1, 2)\n    original(s, n)\n",
            Codes.CmdWriteEffect, "random numbers cannot be drawn in 'with preconditions:'")]
        public void BadCommandWrapsAreLoadErrors(string scouts, string code, string message)
        {
            var d = Single(scouts);
            Assert.That(d.Code, Is.EqualTo(code), d.Message);
            Assert.That(d.Message, Is.EqualTo(message));
        }

        [Test]
        public void KeywordArgumentsWithTheSameNamesAreTheSubmittedArguments()
        {
            Start(("scouts", "@wrap(banking.Take)\ndef w(original, s, n):\n    original(s=s, n=n)\n", Depends));
            Assert.That(Submit("banking.Take", 2).Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(2));
        }
    }
}
