using System;
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
    /// <summary>신호 핸들러 실행: 시점·순서·트랜잭션·결과 보고·세대 상한·멈춘 핸들러 Flow (spec S5, spec-gaps G146~G150).</summary>
    public class WorldReactionTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=10))\n" +
            "component(\"Out\", n=field(int, default=0), s=field(str, default=\"\"))\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Out\"])\n" +
            "signal(\"Ping\", target=Ref[\"Unit\"], tag=str)\n" +
            "signal(\"Pong\", target=Ref[\"Unit\"], tag=str)\n";

        private EntityId _u;

        private ContentLoadResult Content(string logic)
        {
            Mod("combat", schema: Schema, defs: "Unit(\"Scout\")\n" + logic);
            return LoadClean();
        }

        private World Make(string logic, int maxSignalDepth = World.DefaultMaxSignalDepth)
        {
            var world = new World(Content(logic), maxSignalDepth: maxSignalDepth);
            _u = world.Create("Scout");
            return world;
        }

        private SubmitResult Go(World world, string command = "combat.Go") =>
            world.Submit("host", command, new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });

        private string Log(World world) => ((StrValue)world.Get(_u, world.Member("Out", "s"))).Value;
        private long N(World world) => ((IntValue)world.Get(_u, world.Member("Out", "n"))).Value;

        private static string Command(string body) => "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n" + body;

        private static IEnumerable<string> Summary(IEnumerable<Reaction> reactions) =>
            reactions.Select(r => r.Signal + " " + r.Handler + " " + r.Generation + " " + r.Outcome);

        [Test]
        public void AHandlerRunsAfterTheCommandAndIsReported()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") +
                             "@on(Ping)\ndef log(target, tag):\n    target.Out.s = target.Out.s + tag\n");
            var r = Go(world);
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(Log(world), Is.EqualTo("a"));
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[] { "Ping combat.log 1 Finished" }));
            Assert.That(((StrValue)r.Reactions[0].Values["tag"]).Value, Is.EqualTo("a"));
            Assert.That(world.PendingSignalCount, Is.EqualTo(0));
        }

        [Test]
        public void HandlersRunAfterTheWholeCommandBody()
        {
            // 커맨드 본문이 emit 뒤에 쓴 값을 핸들러가 본다: 핸들러는 Flow가 끝난 뒤 돈다 (S5)
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n    u.Out.n = 7\n") +
                             "@on(Ping)\ndef see(target):\n    target.Out.s = \"late\" if target.Out.n == 7 else \"early\"\n");
            Go(world);
            Assert.That(Log(world), Is.EqualTo("late"));
        }

        [Test]
        public void SignalsRunInTheOrderTheyWereSentAndChainedOnesGoLast()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"1\"))\n    emit(Pong(target=u, tag=\"2\"))\n") +
                             "@on(Ping)\ndef first(target, tag):\n    target.Out.s = target.Out.s + \"P\" + tag\n" +
                             "    if tag == \"1\":\n        emit(Ping(target=target, tag=\"3\"))\n" +
                             "@on(Ping)\ndef second(target, tag):\n    target.Out.s = target.Out.s + \"p\" + tag\n" +
                             "@on(Pong)\ndef third(target, tag):\n    target.Out.s = target.Out.s + \"Q\" + tag\n");
            var r = Go(world);
            Assert.That(Log(world), Is.EqualTo("P1p1Q2P3p3"));
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[]
            {
                "Ping combat.first 1 Finished", "Ping combat.second 1 Finished", "Pong combat.third 1 Finished",
                "Ping combat.first 2 Finished", "Ping combat.second 2 Finished",
            }));
        }

        [Test]
        public void AFailingHandlerUndoesOnlyItsOwnEffects()
        {
            var world = Make(Command("    u.Out.n = 1\n    emit(Ping(target=u, tag=\"a\"))\n") +
                             "@on(Ping)\ndef broken(target):\n    target.Out.s = \"half\"\n    emit(Pong(target=target, tag=\"x\"))\n" +
                             "    target.Out.n = target.Out.n // 0\n" +
                             "@on(Ping)\ndef fine(target):\n    target.Out.n = target.Out.n + 10\n" +
                             "@on(Pong)\ndef never(target):\n    target.Out.s = \"pong\"\n");
            var r = Go(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded), "the command that sent the signal stays (S5)");
            Assert.That(N(world), Is.EqualTo(11));
            Assert.That(Log(world), Is.EqualTo(""), "the failed handler's write and its signal are gone");
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[] { "Ping combat.broken 1 ScriptError", "Ping combat.fine 1 Finished" }));
            Assert.That(r.Reactions[0].Error!.Message, Does.Contain("the handler combat.broken for Ping"));
        }

        [Test]
        public void AFailedCommandRunsNoHandler()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n    u.Out.n = u.Out.n // 0\n") +
                             "@on(Ping)\ndef log(target):\n    target.Out.s = \"ran\"\n");
            var r = Go(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Reactions, Is.Empty);
            Assert.That(Log(world), Is.EqualTo(""));
        }

        [Test]
        public void HandlersRunWhenTheCommandStopsAtAsk()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n    r = ask(u, \"Q\", choices=[\"ok\"])\n    u.Out.n = 5\n") +
                             "@on(Ping)\ndef log(target):\n    target.Out.s = \"ran\"\n");
            var r = Go(world);
            Assert.That(r.OpenedRequest, Is.Not.Null);
            Assert.That(Log(world), Is.EqualTo("ran"));
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[] { "Ping combat.log 1 Finished" }));
        }

        [Test]
        public void AHandlerCanAskAndIsAnsweredLater()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") +
                             "@on(Ping)\ndef choose(target):\n    r = ask(target, \"Q\", choices=[\"x\", \"y\"])\n" +
                             "    target.Out.s = r.choice\n    emit(Pong(target=target, tag=\"after\"))\n" +
                             "@on(Pong)\ndef after(target, tag):\n    target.Out.n = 9\n");
            var r = Go(world);
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[] { "Ping combat.choose 1 Suspended" }));
            var q = r.Reactions[0].OpenedRequest!;
            Assert.That(q.Command, Is.EqualTo("combat.choose"));
            Assert.That(world.OpenRequests.Single().Id, Is.EqualTo(q.Id));

            var a = world.Answer("host", q.Id, _u, 1);
            Assert.That(a.Succeeded, Is.True, a.Error?.ToString());
            Assert.That(Log(world), Is.EqualTo("y"));
            // 답이 이어서 실행한 구간이 낸 신호는 새 입력이라 다시 1세대다 (G147)
            Assert.That(Summary(a.Reactions), Is.EqualTo(new[] { "Pong combat.after 1 Finished" }));
            Assert.That(N(world), Is.EqualTo(9));
        }

        // ---- 사라진 대상 (G151) ----

        [Test]
        public void AHandlerWhoseEntityIsGoneIsCanceledNotFailed()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n    destroy(u)\n") +
                             "@on(Ping)\ndef bleed(target):\n    target.Out.n = 1\n" +
                             "@on(Ping)\ndef count(tag):\n    x = spawn(\"Scout\")\n    x.Out.s = tag\n");
            var r = Go(world);
            Assert.That(r.Succeeded, Is.True);
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[] { "Ping combat.bleed 1 Canceled", "Ping combat.count 1 Finished" }),
                "a handler that does not take the entity still runs");
            Assert.That(r.Reactions[0].Message, Is.EqualTo("'target' (#1) was destroyed before the handler ran, so it did not run"));
            Assert.That(r.Reactions[0].Error, Is.Null);
            Assert.That(((StrValue)world.Get(world.All("Unit").Single(), world.Member("Out", "s"))).Value, Is.EqualTo("a"));
        }

        [Test]
        public void AnEarlierHandlerCanDestroyTheEntityForALaterOne()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") +
                             "@on(Ping)\ndef kill(target):\n    destroy(target)\n" +
                             "@on(Ping)\ndef bleed(target):\n    target.Out.n = 1\n");
            var r = Go(world);
            Assert.That(Summary(r.Reactions), Is.EqualTo(new[] { "Ping combat.kill 1 Finished", "Ping combat.bleed 1 Canceled" }));
        }

        // ---- 세대 상한 (G147·G150) ----

        private const string Retaliate =
            "@on(Ping)\ndef retaliate(target):\n    target.Out.n = target.Out.n + 1\n    emit(Ping(target=target, tag=\"again\"))\n";

        [Test]
        public void ASelfLoopStopsAtTheDefaultLimit()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") + Retaliate);
            var r = Go(world);
            Assert.That(N(world), Is.EqualTo(64));
            Assert.That(r.Reactions.Count, Is.EqualTo(65));
            var dropped = r.Reactions.Last();
            Assert.That(dropped.Outcome, Is.EqualTo(ReactionOutcome.Dropped));
            Assert.That(dropped.Generation, Is.EqualTo(65));
            Assert.That(dropped.Message, Is.EqualTo(
                "signal Ping was not handled: it is generation 65, deeper than the limit 64; " +
                "the chain repeats: Ping → combat.retaliate → Ping"));
            Assert.That(dropped.Hint, Does.Contain("wait()").And.Contain("maxSignalDepth"));
            Assert.That(world.PendingSignalCount, Is.EqualTo(0));
        }

        [Test]
        public void TheHostSetsTheLimit()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") + Retaliate, maxSignalDepth: 3);
            var r = Go(world);
            Assert.That(N(world), Is.EqualTo(3));
            Assert.That(Summary(r.Reactions).Last(), Is.EqualTo("Ping combat.retaliate 4 Dropped"));
        }

        [Test]
        public void AMutualLoopShowsBothHandlers()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") +
                             "@on(Ping)\ndef ping(target):\n    emit(Pong(target=target, tag=\"b\"))\n" +
                             "@on(Pong)\ndef pong(target):\n    emit(Ping(target=target, tag=\"c\"))\n", maxSignalDepth: 5);
            var r = Go(world);
            // Ping 1세대 → Pong 2 → … → Pong 6이 넘친다. Pong을 받은 pong이 Ping을, Ping을 받은 ping이 Pong을 낸다
            Assert.That(r.Reactions.Last().Message, Does.EndWith(
                "generation 6, deeper than the limit 5; the chain repeats: Pong → combat.pong → Ping → combat.ping → Pong"));
        }

        [Test]
        public void AWideFanOutIsNotLimited()
        {
            var world = Make(Command("    for i in range(100):\n        emit(Ping(target=u, tag=\"a\"))\n") +
                             "@on(Ping)\ndef count(target):\n    target.Out.n = target.Out.n + 1\n", maxSignalDepth: 1);
            var r = Go(world);
            Assert.That(N(world), Is.EqualTo(100));
            Assert.That(r.Reactions.All(x => x.Outcome == ReactionOutcome.Finished), Is.True);
        }

        [Test]
        public void TheLimitMustBePositive()
        {
            var content = Content("");
            Assert.That(() => new World(content, maxSignalDepth: 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // ---- 멈춘 핸들러 Flow의 저장 ----

        private const string AskingHandler =
            "@on(Ping)\ndef choose(target):\n    r = ask(target, \"Q\", choices=[\"x\", \"y\"])\n    target.Out.s = r.choice\n";

        [Test]
        public void APausedHandlerFlowIsSavedAndContinuesAfterLoading()
        {
            string logic = Command("    emit(Ping(target=u, tag=\"a\"))\n") + AskingHandler;
            var world = Make(logic);
            var q = Go(world).Reactions.Single().OpenedRequest!;
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"handler\": \"combat.choose\""));

            var loaded = World.Load(Content(logic), save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            var again = loaded.World!;
            Assert.That(again.Save(), Is.EqualTo(save), "C4");
            Assert.That(again.Answer("host", q.Id, _u, 0).Succeeded, Is.True);
            Assert.That(world.Answer("host", q.Id, _u, 0).Succeeded, Is.True);
            Assert.That(again.Save(), Is.EqualTo(world.Save()), "C1");
            Assert.That(Log(again), Is.EqualTo("x"));
        }

        [Test]
        public void ARemovedHandlerRefusesTheSave()
        {
            var world = Make(Command("    emit(Ping(target=u, tag=\"a\"))\n") + AskingHandler);
            Go(world);
            var save = world.Save();
            var result = World.Load(Content(Command("    emit(Ping(target=u, tag=\"a\"))\n")), save);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message),
                Is.EqualTo(new[] { "SAV002 flow 1 (combat.choose): the handler no longer exists" }));
        }
    }
}
