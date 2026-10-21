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
    /// 즉시 핸들러 <c>@on(X, now=True)</c> (spec-gaps G310): <c>emit</c>하는 순간 낸 흐름 안에서 함수처럼 돈다. 안에서는 시간을 막는
    /// <c>ask</c>만 멈출 수 있다.
    /// </summary>
    public class ImmediateHandlerTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Out\", n=field(int, default=0), s=field(str, default=\"\"))\n" +
            "deftype(\"Unit\", components=[\"Out\"])\n" +
            "signal(\"Damaged\", target=Ref[\"Unit\"], amount=int)\n";

        private const string Probe =
            "Unit(\"Scout\")\n" +
            "def note(u, s):\n    u.Out.s = u.Out.s + s\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Probe(u: Ref[Unit], v: Ref[Unit]):\n" +
            "    emit(Damaged(target=u, amount=3))\n" +
            "    note(v, \"c\")\n";

        private World _world = null!;
        private EntityId _u, _v;

        private SubmitResult Start(string handlers)
        {
            Mod("combat", schema: Schema, defs: Probe + handlers);
            return Submit(LoadClean());
        }

        private SubmitResult Submit(Kesa.Engine.Loading.ContentLoadResult content)
        {
            _world = new World(content);
            _u = _world.Create("combat:Scout");
            _v = _world.Create("combat:Scout");
            return _world.Submit("host", "combat.Probe",
                new Dictionary<string, Value> { ["u"] = new EntityValue(_u), ["v"] = new EntityValue(_v) });
        }

        private string S(EntityId e) => ((StrValue)_world.Get(e, _world.Member("Out", "s"))).Value;
        private long N(EntityId e) => ((IntValue)_world.Get(e, _world.Member("Out", "n"))).Value;

        [Test]
        public void ImmediateHandlersRunAtTheEmitBeforeTheNextLine()
        {
            // 보통 핸들러는 흐름이 끝난 뒤에, 즉시 핸들러는 정의 순서로 emit 자리에서 돈다
            var r = Start(
                "@on(Damaged)\ndef later(target):\n    note(target, \"o\")\n" +
                "@on(Damaged, now=True)\ndef first(target, amount):\n    note(target, \"a\" if amount == 3 else \"?\")\n" +
                "@on(Damaged, now=True)\ndef second(target):\n    note(target, \"b\")\n" +
                "@on(Damaged)\ndef seen(target):\n    target.Out.n = 1 if target.Out.s == \"abo\" else 2\n");
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(S(_u), Is.EqualTo("abo"));
            Assert.That(N(_u), Is.EqualTo(1));
            Assert.That(S(_v), Is.EqualTo("c"));
            Assert.That(r.Reactions.Select(x => x.Handler), Is.EqualTo(new[] { "combat.later", "combat.seen" }),
                "an immediate handler is part of the emitting flow, not a reaction of its own");
        }

        [Test]
        public void AnImmediateHandlerSeesWhatTheEmitterWroteAndTheEmitterSeesWhatItWrote()
        {
            Mod("combat", schema: Schema, defs: "Unit(\"Scout\")\n" +
                "@command(free=True, acts_for=None)\n" +
                "def Probe(u: Ref[Unit], v: Ref[Unit]):\n" +
                "    u.Out.n = 5\n" +
                "    emit(Damaged(target=u, amount=3))\n" +
                "    v.Out.n = u.Out.n\n" +
                "@on(Damaged, now=True)\ndef double(target):\n    target.Out.n = target.Out.n * 2\n");
            var r = Submit(LoadClean());
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(N(_v), Is.EqualTo(10));
        }

        [Test]
        public void AnErrorInAnImmediateHandlerTakesBackTheWholeCommand()
        {
            var r = Start("@on(Damaged, now=True)\ndef bad(target):\n    target.Out.n = 7\n    d = Damaged(target=target)\n" +
                          "@on(Damaged)\ndef later(target):\n    target.Out.n = 9\n");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(N(_u), Is.EqualTo(0));
            Assert.That(S(_v), Is.EqualTo(""));
            Assert.That(r.Reactions, Is.Empty, "the command sent no signal");
            Assert.That(_world.PendingSignalCount, Is.EqualTo(0));
        }

        [Test]
        public void AnImmediateHandlerThatEmitsAgainCallsItsOwnImmediateHandlers()
        {
            Mod("combat", schema: Schema + "signal(\"Healed\", target=Ref[\"Unit\"])\n", defs: Probe +
                "@on(Damaged, now=True)\ndef hurt(target):\n    note(target, \"h\")\n    emit(Healed(target=target))\n    note(target, \"H\")\n" +
                "@on(Healed, now=True)\ndef heal(target):\n    note(target, \"+\")\n");
            var r = Submit(LoadClean());
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(S(_u), Is.EqualTo("h+H"));
        }

        [Test]
        public void AnImmediateHandlerCannotRunInsideItself()
        {
            var r = Start("@on(Damaged, now=True)\ndef again(target, amount):\n    emit(Damaged(target=target, amount=amount + 1))\n");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunCycle));
            Assert.That(r.Error.Message, Does.Contain("function 'again' calls itself"));
        }

        [Test]
        public void AHandlerWhoseEntityIsGoneDoesNotRun()
        {
            // 앞 핸들러가 지운 엔티티를 받는 핸들러는 돌지 않고, 받지 않는 핸들러는 돈다 (G151과 같은 뜻)
            var r = Start(
                "@on(Damaged, now=True)\ndef kill(target):\n    destroy(target)\n" +
                "@on(Damaged, now=True)\ndef touch(target):\n    note(target, \"x\")\n" +
                "@on(Damaged, now=True)\ndef count(amount):\n    pass\n");
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(_world.IsAlive(_u), Is.False);
            Assert.That(S(_v), Is.EqualTo("c"));
        }

        // ---- 멈춤 ----

        private const string Asks =
            "@on(Damaged, now=True)\ndef first(target):\n    note(target, \"a\")\n" +
            "    r = ask(target, \"Go\", choices=[\"x\", \"y\"])\n    note(target, r.choice)\n" +
            "@on(Damaged, now=True)\ndef second(target):\n    note(target, \"b\")\n";

        [Test]
        public void AnAskInAnImmediateHandlerPausesTheEmitter()
        {
            var r = Start(Asks);
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(S(_u), Is.EqualTo("a"));
            Assert.That(S(_v), Is.EqualTo(""), "the line after emit waits for the answer");
            _world.Seat(_u, "alice");
            var answer = _world.Answer("alice", _world.OpenRequests.Single().Id, _u, 1);
            Assert.That(answer.Succeeded, Is.True, answer.Outcome + " " + answer.Message + " " + answer.Error);
            Assert.That(S(_u), Is.EqualTo("ayb"));
            Assert.That(S(_v), Is.EqualTo("c"));
        }

        [Test]
        public void APausedImmediateHandlerSavesAndLoads()
        {
            Mod("combat", schema: Schema, defs: Probe + Asks);
            var content = LoadClean();
            Assert.That(Submit(content).Succeeded, Is.True);
            var loaded = World.Load(content, _world.Save());
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            Assert.That(_world.Save(), Is.EqualTo(World.Load(content, _world.Save()).World!.Save()), "saving again gives the same bytes");
            _world.Seat(_u, "alice");
            var answer = _world.Answer("alice", _world.OpenRequests.Single().Id, _u, 0);
            Assert.That(answer.Succeeded, Is.True, answer.Outcome + " " + answer.Message + " " + answer.Error);
            Assert.That(S(_u), Is.EqualTo("axb"));
            Assert.That(S(_v), Is.EqualTo("c"));
        }

        [TestCase("    wait(ticks=1)\n", "wait() cannot be used in an @on(..., now=True) handler")]
        [TestCase("    pause()\n", "wait() cannot be used in an @on(..., now=True) handler")]
        [TestCase("    ask(target, \"Go\", choices=[\"x\"], deadline=3, on_timeout=\"x\")\n",
            "ask() with a deadline or blocks_time=False cannot be used in an @on(..., now=True) handler")]
        [TestCase("    ask(target, \"Go\", choices=[\"x\"], blocks_time=False)\n",
            "ask() with a deadline or blocks_time=False cannot be used in an @on(..., now=True) handler")]
        [TestCase("    advance(1)\n", "advance() cannot be used in an @on(..., now=True) handler")]
        public void TimeCannotPassInsideAnImmediateHandler(string body, string message)
        {
            var r = Start("def pause():\n    wait(ticks=1)\n@on(Damaged, now=True)\ndef slow(target):\n" + body);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
            Assert.That(r.Error.Hint, Does.Contain("G310"));
        }

        [Test]
        public void APlainHandlerCanStillWait()
        {
            var r = Start("@on(Damaged)\ndef slow(target):\n    wait(ticks=1)\n    note(target, \"w\")\n");
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            _world.Advance(1);
            Assert.That(S(_u), Is.EqualTo("w"));
        }

        [TestCase("@on(Damaged, now=1)\ndef f():\n    pass\n", "now= takes True or False")]
        [TestCase("@on(Damaged, nw=True)\ndef f():\n    pass\n", "@on has no argument 'nw' | did you mean 'now'?")]
        public void BadNowIsALoadError(string defs, string message)
        {
            Mod("combat", schema: Schema, defs: defs);
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint), Does.StartWith(Codes.CmdBadDecorator + " " + message));
        }
    }
}
