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
    /// <summary>틱, <c>Advance</c>, 엔진 신호 <c>Tick</c>, <c>now()</c> (spec X2, spec-gaps G154~G156).</summary>
    public class TimeTests : LoaderTestBase
    {
        private const string Schema =
            "def age_of(h):\n    return now() - h.born\n" +
            "component(\"Out\", s=field(str, default=\"\"), n=field(int, default=0))\n" +
            "component(\"Life\", born=field(int, default=0), age=derived(int, age_of))\n" +
            "deftype(\"Unit\", components=[\"Out\", \"Life\"])\n";

        private EntityId _u;

        private ContentLoadResult Content(string defs)
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\")\n" + defs);
            return LoadClean();
        }

        private World Make(string defs)
        {
            var world = new World(Content(defs));
            _u = world.Create("Scout");
            return world;
        }

        private string S(World w) => ((StrValue)w.Get(_u, w.Member("Out", "s"))).Value;
        private long N(World w) => ((IntValue)w.Get(_u, w.Member("Out", "n"))).Value;

        private static string Diag(Diagnostic d) => d.Code + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        [Test]
        public void AdvanceRaisesTheTickAndRunsTickHandlersEachTick()
        {
            var world = Make("@on(Tick)\ndef mark():\n    pass\n" +
                             "@command(free=True, acts_for=None)\ndef Stamp(u: Ref[Unit]):\n    u.Out.n = u.Out.n * 10 + now()\n");
            Assert.That(world.Tick, Is.EqualTo(0));
            var r = world.Advance(3);
            Assert.That(r.Advanced, Is.EqualTo(3));
            Assert.That(r.Tick, Is.EqualTo(3));
            Assert.That(world.Tick, Is.EqualTo(3));
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Handler + " " + x.Generation + " " + x.Outcome),
                Is.EqualTo(Enumerable.Repeat("Tick core.mark 1 Finished", 3)));
            var stamped = world.Submit("host", "core.Stamp", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(stamped.SleepsUntil, Is.Null, "a command that finished did not sleep");
            Assert.That(N(world), Is.EqualTo(3), "now() reads the tick in a command");
        }

        [Test]
        public void ATickHandlerSeesTheRaisedTick()
        {
            // 틱 +1이 핸들러보다 먼저다 (X2 순서). 핸들러가 만든 엔티티에 그때의 now()를 남긴다
            var world = Make("@on(Tick)\ndef mark():\n    x = spawn(\"Scout\")\n    x.Out.n = now()\n");
            world.Advance(2);
            var made = world.All("Unit").Where(id => id != _u).Select(id => ((IntValue)world.Get(id, world.Member("Out", "n"))).Value);
            Assert.That(made, Is.EqualTo(new long[] { 1, 2 }));
        }

        [Test]
        public void TickIsVisibleToEveryModWithoutADependency()
        {
            Mod("core", schema: Schema);
            Mod("weather", defs: "@on(Tick)\ndef rain():\n    pass\n");
            Assert.That(LoadClean().Handlers.Single().Signal.Name, Is.EqualTo("Tick"));
        }

        [Test]
        public void AModCannotDeclareTick()
        {
            Mod("core", schema: Schema + "signal(\"Tick\", n=int)\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.StartWith(Codes.SchDuplicateName + " 'Tick' is a signal declared by the engine"));
        }

        [Test]
        public void MakingTickInAFunctionIsALoadError()
        {
            Mod("core", schema: Schema, defs: "@command(free=True, acts_for=None)\ndef Rest(u: Ref[Unit]):\n    emit(Tick())\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.StartWith(Codes.CmdWriteEffect +
                " Tick is sent by the engine when time advances, so scripts cannot make it; the host calls World.Advance"));
        }

        [Test]
        public void MakingTickThroughAFunctionValueIsARuntimeError()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Rest(u: Ref[Unit]):\n    f = Tick\n    emit(f())\n");
            var r = world.Submit("host", "core.Rest", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("Tick is sent by the engine"));
        }

        [Test]
        public void AFailingTickHandlerDoesNotStopTime()
        {
            var world = Make("@on(Tick)\ndef broken():\n    x = 1 // 0\n");
            var r = world.Advance(2);
            Assert.That(world.Tick, Is.EqualTo(2));
            Assert.That(r.Reactions.Select(x => x.Outcome), Is.EqualTo(new[] { ReactionOutcome.ScriptError, ReactionOutcome.ScriptError }));
        }

        [Test]
        public void ADerivedValueCanReadTheTickAndExplainShowsIt()
        {
            var world = Make("");
            world.Advance(5);
            var explain = world.Explain(_u, world.Member("Life", "age"));
            Assert.That(explain.Value, Is.EqualTo(new IntValue(5)).Using<Value>((a, b) => ((IntValue)a).Value == ((IntValue)b).Value));
            Assert.That(explain.Render(), Is.EqualTo("Life.age of #1 = 5\n  now() = 5\n  Life.born of #1 = 0"));
            Assert.That(explain.Reads[0].Kind, Is.EqualTo(ExplainKind.Time));
        }

        [Test]
        public void TheTickIsSaved()
        {
            var world = Make("");
            world.Advance(7);
            var save = world.Save();
            var loaded = World.Load(Content(""), save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            Assert.That(loaded.World!.Tick, Is.EqualTo(7));
            Assert.That(loaded.World.Save(), Is.EqualTo(save), "C4");
        }

        [Test]
        public void AdvanceNeedsAtLeastOneTick()
        {
            var world = Make("");
            Assert.That(() => world.Advance(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ChainsFromATickStartAtGenerationOneEachTick()
        {
            // 틱마다 새로 1세대라 상한 2에서도 매 틱의 연쇄(Tick 1 → Echo 2)는 모두 돈다 (G147)
            Mod("core", schema: Schema + "signal(\"Echo\", n=int)\n",
                defs: "@on(Tick)\ndef start():\n    emit(Echo(n=now()))\n@on(Echo)\ndef echo(n):\n    pass\n");
            var world = new World(LoadClean(), maxSignalDepth: 2);
            var r = world.Advance(3);
            Assert.That(r.Reactions.Select(x => x.Handler + " " + x.Generation + " " + x.Outcome), Is.EqualTo(new[]
            {
                "core.start 1 Finished", "core.echo 2 Finished",
                "core.start 1 Finished", "core.echo 2 Finished",
                "core.start 1 Finished", "core.echo 2 Finished",
            }));
        }

        // ---- wait (spec X2·S5 자는 Flow, spec-gaps G157) ----

        private const string Waiter =
            "@command(free=True, acts_for=None)\ndef Brew(u: Ref[Unit]):\n    u.Out.s = \"start\"\n    wait(ticks=2)\n" +
            "    u.Out.s = \"done\"\n    u.Out.n = now()\n";

        private SubmitResult Brew(World w) =>
            w.Submit("host", "core.Brew", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });

        [Test]
        public void WaitSleepsForTheGivenTicksThenGoesOn()
        {
            var world = Make(Waiter);
            var r = Brew(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(r.OpenedRequest, Is.Null, "a sleeping flow holds no request");
            Assert.That(r.SleepsUntil, Is.EqualTo(2), "the host learns when it wakes (G158)");
            Assert.That(world.OpenRequests, Is.Empty);
            Assert.That(S(world), Is.EqualTo("start"), "effects before wait stay");

            Assert.That(world.Advance().Reactions, Is.Empty);
            Assert.That(S(world), Is.EqualTo("start"));
            var woke = world.Advance();
            Assert.That(S(world), Is.EqualTo("done"));
            Assert.That(N(world), Is.EqualTo(2), "it wakes at tick 0 + 2");
            Assert.That(woke.Reactions.Select(x => x.Signal + " " + x.Handler + " " + x.Outcome + " " + x.Flow),
                Is.EqualTo(new[] { "wait core.Brew Finished 1" }));
            Assert.That(world.Advance(3).Reactions, Is.Empty, "a finished flow is gone");
        }

        [Test]
        public void AWokenFlowRunsBeforeTickHandlers()
        {
            // X2 순서: 틱 +1 → 깨우기 → Tick 핸들러. 깨어난 Flow가 쓴 값을 같은 틱의 핸들러가 본다
            var world = Make(Waiter + "@on(Tick)\ndef look():\n    pass\n");
            Brew(world);
            var r = world.Advance(2);
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Handler),
                Is.EqualTo(new[] { "Tick core.look", "wait core.Brew", "Tick core.look" }));
        }

        [Test]
        public void SeveralSleepersWakeInFlowOrderAndCanSleepAgain()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Beat(u: Ref[Unit], d: int):\n" +
                             "    for i in range(2):\n        wait(ticks=d)\n        u.Out.n = u.Out.n * 10 + d\n");
            Submit(world, 2);
            Submit(world, 1);
            var r = world.Advance(4);
            // Flow 1은 d=2, Flow 2는 d=1. 틱 1: 2번이 깨어 틱 2까지 다시 잠 / 틱 2: 번호 순으로 1번(틱 4까지 잠), 2번(끝) / 틱 4: 1번(끝)
            Assert.That(r.Reactions.Select(x => x.Flow + " " + x.Outcome + " " + x.SleepsUntil), Is.EqualTo(new[]
            {
                "2 Suspended 2", "1 Suspended 4", "2 Finished ", "1 Finished ",
            }));
            Assert.That(N(world), Is.EqualTo(1212));

            void Submit(World w, int d) => w.Submit("host", "core.Beat",
                new Dictionary<string, Value> { ["u"] = new EntityValue(_u), ["d"] = new IntValue(d) });
        }

        [Test]
        public void AFailingWokenFlowIsRolledBackAndDropped()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Brew(u: Ref[Unit]):\n    wait(ticks=1)\n" +
                             "    u.Out.s = \"half\"\n    x = 1 // 0\n");
            Brew(world);
            var r = world.Advance();
            var woke = r.Reactions.Single();
            Assert.That(woke.Signal + " " + woke.Handler + " " + woke.Outcome, Is.EqualTo("wait core.Brew ScriptError"));
            Assert.That(woke.Error!.Message, Does.Contain("the command core.Brew (after waking)"));
            Assert.That(S(world), Is.EqualTo(""), "the woken part is rolled back");
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Contain("\"flows\": []"), "the flow is dropped");
            Assert.That(world.Advance().Reactions, Is.Empty);
        }

        [Test]
        public void AHandlerCanWait()
        {
            Mod("core", schema: Schema + "signal(\"Ring\", u=Ref[\"Unit\"])\n",
                defs: "Unit(\"Scout\")\n@on(Ring)\ndef later(u):\n    wait(ticks=1)\n    u.Out.s = \"rang\"\n" +
                      "@command(free=True, acts_for=None)\ndef Bell(u: Ref[Unit]):\n    emit(Ring(u=u))\n");
            var world = new World(LoadClean());
            _u = world.Create("Scout");
            var r = world.Submit("host", "core.Bell", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(r.Reactions.Single().Outcome, Is.EqualTo(ReactionOutcome.Suspended), "sleeping is not finishing");
            Assert.That(r.Reactions.Single().OpenedRequest, Is.Null);
            Assert.That(r.Reactions.Single().SleepsUntil, Is.EqualTo(1));
            var woke = world.Advance().Reactions.Single();
            Assert.That(woke.Signal + " " + woke.Handler + " " + woke.Outcome, Is.EqualTo("wait core.later Finished"));
            Assert.That(S(world), Is.EqualTo("rang"));
        }

        [Test]
        public void ASleepingFlowIsSavedAndWakesAfterLoading()
        {
            var world = Make(Waiter);
            Brew(world);
            world.Advance();
            var save = world.Save();
            var text = Encoding.UTF8.GetString(save);
            Assert.That(text, Does.Contain("\"wake_at\": 2"));
            Assert.That(text, Does.Not.Contain("\"request\": {"));
            var loaded = World.Load(Content(Waiter), save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            var back = loaded.World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");
            back.Advance();
            Assert.That(S(back), Is.EqualTo("done"));
            Assert.That(N(back), Is.EqualTo(2));
        }

        [Test]
        public void AFlowCanSwitchBetweenAskingAndSleepingAcrossASave()
        {
            // ask → 답 → wait(자는 동안 저장·불러오기) → 깨어나 ask → 답. "request"와 "wake_at"이 서로 바뀌는 저장 (C1·C4)
            const string defs = "@command(free=True, acts_for=None)\ndef Quest(u: Ref[Unit]):\n" +
                                "    r = ask(u, \"Go?\", choices=[\"a\", \"b\"])\n    u.Out.s = r.choice\n    wait(ticks=2)\n" +
                                "    q = ask(u, \"Again?\", choices=[\"c\", \"d\"])\n    u.Out.s = u.Out.s + q.choice\n";
            var world = Make(defs);
            var first = world.Submit("host", "core.Quest", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) }).OpenedRequest!;
            var a = world.Answer("host", first.Id, _u, 1);
            Assert.That(a.Succeeded, Is.True);
            Assert.That(a.SleepsUntil, Is.EqualTo(2), "answered at tick 0, it sleeps 2 ticks");
            Assert.That(world.OpenRequests, Is.Empty, "after the answer it sleeps");
            world.Advance();

            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"wake_at\": 2"));
            var loaded = World.Load(Content(defs), save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            var back = loaded.World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");

            var woke = back.Advance().Reactions.Single();
            Assert.That(woke.Outcome, Is.EqualTo(ReactionOutcome.Suspended));
            var second = woke.OpenedRequest!;
            Assert.That(back.OpenRequests.Select(q => q.Id), Is.EqualTo(new[] { second.Id }));
            var resaved = back.Save();
            Assert.That(Encoding.UTF8.GetString(resaved), Does.Contain("\"request\": {").And.Not.Contain("wake_at"));
            Assert.That(World.Load(Content(defs), resaved).World!.Save(), Is.EqualTo(resaved), "C4 while asking again");

            Assert.That(back.Answer("host", second.Id, _u, 0).Succeeded, Is.True);
            Assert.That(S(back), Is.EqualTo("bc"));
        }

        [Test]
        public void SignalsFromAWokenFlowStartAgainAtGenerationOne()
        {
            // G147 힌트의 약속: 핸들러 연쇄가 wait를 거치면 다시 1세대다. 상한 1에서도 깨어난 뒤 낸 신호의 핸들러가 돈다
            Mod("core", schema: Schema + "signal(\"Ring\", u=Ref[\"Unit\"])\n",
                defs: "Unit(\"Scout\")\n@on(Ring)\ndef again(u):\n    u.Out.n = u.Out.n + 1\n    wait(ticks=1)\n    emit(Ring(u=u))\n" +
                      "@command(free=True, acts_for=None)\ndef Bell(u: Ref[Unit]):\n    emit(Ring(u=u))\n");
            var world = new World(LoadClean(), maxSignalDepth: 1);
            _u = world.Create("Scout");
            world.Submit("host", "core.Bell", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            var r = world.Advance(2);
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Generation + " " + x.Outcome), Is.EqualTo(new[]
            {
                "wait 0 Finished", "Ring 1 Suspended", "wait 0 Finished", "Ring 1 Suspended",
            }));
            Assert.That(N(world), Is.EqualTo(3));
        }

        // ---- blocks_time (spec X2, spec-gaps G159) ----

        private const string Asker =
            "@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit], stop: bool):\n" +
            "    r = ask(u, \"Raid?\", choices=[\"fight\", \"hide\"], blocks_time=stop)\n    u.Out.s = r.choice\n" +
            "@command(free=True, acts_for=None)\ndef Plain(u: Ref[Unit]):\n" +
            "    r = ask(u, \"Raid?\", choices=[\"fight\", \"hide\"])\n    u.Out.s = r.choice\n";

        private SubmitResult Raid(World w, bool stop) => w.Submit("host", "core.Raid",
            new Dictionary<string, Value> { ["u"] = new EntityValue(_u), ["stop"] = stop ? BoolValue.True : BoolValue.False });

        [Test]
        public void AQuestionBlocksTimeByDefault()
        {
            var world = Make(Asker);
            var q = world.Submit("host", "core.Plain", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) }).OpenedRequest!;
            Assert.That(q.BlocksTime, Is.True, "no deadline, so it blocks (G159)");
            var r = world.Advance(3);
            Assert.That(r.Blocked, Is.True);
            Assert.That(r.Advanced, Is.EqualTo(0));
            Assert.That(r.BlockedBy.Select(x => x.Id), Is.EqualTo(new[] { q.Id }));
            Assert.That(world.Tick, Is.EqualTo(0), "time did not move");

            world.Answer("host", q.Id, _u, 0);
            var after = world.Advance(3);
            Assert.That(after.Blocked, Is.False);
            Assert.That(after.Advanced, Is.EqualTo(3));
        }

        [Test]
        public void AQuestionThatDoesNotBlockLetsTimeFlow()
        {
            var world = Make(Asker);
            var q = Raid(world, false).OpenedRequest!;
            Assert.That(q.BlocksTime, Is.False);
            var r = world.Advance(3);
            Assert.That(r.Blocked, Is.False);
            Assert.That(world.Tick, Is.EqualTo(3));
            Assert.That(world.OpenRequests.Select(x => x.Id), Is.EqualTo(new[] { q.Id }), "still open while time flows");
        }

        [Test]
        public void AQuestionOpenedDuringAdvanceStopsTheRestOfIt()
        {
            // 매 틱 전에 다시 본다 (X2): 틱 2에 깨어난 Flow가 막는 질문을 열면 advance(5)는 2틱에서 멈춘다
            var world = Make("@command(free=True, acts_for=None)\ndef Later(u: Ref[Unit]):\n    wait(ticks=2)\n" +
                             "    r = ask(u, \"Now?\", choices=[\"yes\"])\n    u.Out.s = r.choice\n");
            world.Submit("host", "core.Later", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            var r = world.Advance(5);
            Assert.That(r.Advanced, Is.EqualTo(2));
            Assert.That(r.Tick, Is.EqualTo(2));
            var opened = r.Reactions.Single().OpenedRequest!;
            Assert.That(r.BlockedBy.Select(x => x.Id), Is.EqualTo(new[] { opened.Id }));
        }

        [Test]
        public void BlocksTimeIsSaved()
        {
            var world = Make(Asker);
            Raid(world, false);
            Raid(world, true);
            var save = world.Save();
            var text = Encoding.UTF8.GetString(save);
            Assert.That(text, Does.Contain("\"blocks_time\": false").And.Contain("\"blocks_time\": true"));
            var back = World.Load(Content(Asker), save).World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");
            Assert.That(back.OpenRequests.Select(x => x.BlocksTime), Is.EqualTo(new[] { false, true }));
            Assert.That(back.Advance().Blocked, Is.True);
        }

        [Test]
        public void BlocksTimeMustBeABool()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
                             "    r = ask(u, \"Raid?\", choices=[\"fight\"], blocks_time=1)\n");
            var r = world.Submit("host", "core.Raid", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("blocks_time= of ask() must be True or False, not int"));
        }

        // ---- deadline (spec X2, spec-gaps G159·G160) ----

        private World Deadlined(string extra) => Make("@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
                                                       "    r = ask(u, \"Raid?\", choices=[\"fight\", \"hide\"]" + extra + ")\n");

        private SubmitResult Raid(World w) =>
            w.Submit("host", "core.Raid", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });

        [Test]
        public void ADeadlineIsCountedFromWhenTheQuestionOpensAndDoesNotBlock()
        {
            var world = Deadlined(", deadline=3, on_timeout=\"hide\"");
            world.Advance(10);
            var q = Raid(world).OpenedRequest!;
            Assert.That(q.Deadline, Is.EqualTo(13), "opened at tick 10, 3 ticks later (G160)");
            Assert.That(q.BlocksTime, Is.False, "a deadline means it does not block (G159)");
            Assert.That(world.Advance(2).Blocked, Is.False);
        }

        [Test]
        public void NoDeadlineMeansNone()
        {
            var world = Deadlined(", blocks_time=False");
            var q = Raid(world).OpenedRequest!;
            Assert.That(q.Deadline, Is.Null);
            Assert.That(q.BlocksTime, Is.False);
        }

        [Test]
        public void BlockingWithADeadlineIsAnError()
        {
            var world = Deadlined(", deadline=3, on_timeout=\"hide\", blocks_time=True");
            var r = Raid(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("the deadline is never reached"));
        }

        [TestCase("0", "not 0")]
        [TestCase("-2", "not -2")]
        [TestCase("1.5", "not 1.5")]
        [TestCase("\"3\"", "not str")]
        public void ADeadlineIsAPositiveTickCount(string value, string shown)
        {
            var r = Raid(Deadlined(", deadline=" + value));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("deadline= of ask() must be a number of ticks of at least 1, " + shown));
        }

        [Test]
        public void TheDeadlineIsSaved()
        {
            var world = Deadlined(", deadline=4, on_timeout=\"hide\"");
            world.Advance();
            Raid(world);
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"deadline\": 5"));
            var back = World.Load(Content("@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
                                          "    r = ask(u, \"Raid?\", choices=[\"fight\", \"hide\"], deadline=4, on_timeout=\"hide\")\n"), save).World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");
            Assert.That(back.OpenRequests.Single().Deadline, Is.EqualTo(5));
        }

        // ---- on_timeout (spec X2, spec-gaps G126·G161) ----

        private const string Timed =
            "@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
            "    r = ask(u, \"Raid?\", choices=[\"fight\", \"hide\"], deadline=2, on_timeout=\"hide\")\n" +
            "    u.Out.s = r.choice\n    u.Out.n = r.index\n    if r.by is None:\n        u.Out.n = r.index + 10\n";

        [Test]
        public void WhenTheDeadlinePassesTheDefaultChoiceIsGiven()
        {
            var world = Make(Timed);
            var q = Raid(world).OpenedRequest!;
            Assert.That(q.TimeoutChoice, Is.EqualTo(1));
            Assert.That(world.Advance().Reactions, Is.Empty, "tick 1 is before the deadline");
            var r = world.Advance();
            var timed = r.Reactions.Single();
            Assert.That(timed.Signal + " " + timed.Handler + " " + timed.Outcome + " " + timed.Flow,
                Is.EqualTo("timeout core.Raid Finished 1"));
            Assert.That(S(world) + " " + N(world), Is.EqualTo("hide 11"), "index 1, no entity answered (G126)");
            Assert.That(world.OpenRequests, Is.Empty);
        }

        [Test]
        public void AnAnswerBeforeTheDeadlineWins()
        {
            var world = Make(Timed);
            var q = Raid(world).OpenedRequest!;
            world.Answer("host", q.Id, _u, 0);
            Assert.That(world.Advance(3).Reactions, Is.Empty);
            Assert.That(S(world) + " " + N(world), Is.EqualTo("fight 0"), "answered by the unit");
        }

        [Test]
        public void TheDefaultCanBeAResponse()
        {
            var world = Make("@command(acts_for=lambda u: u)\ndef Pass(u: Ref[Unit]):\n    u.Out.n = 99\n" +
                             "@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
                             "    r = ask(u, \"Raid?\", responses=[\"Pass\"], deadline=1, on_timeout=\"Pass\")\n" +
                             "    u.Out.s = r.response\n    if r.by is None:\n        u.Out.s = r.response + \" None\"\n");
            Assert.That(Raid(world).OpenedRequest!.TimeoutResponse, Is.EqualTo(0));
            world.Advance();
            Assert.That(S(world), Is.EqualTo("Pass None"));
            Assert.That(N(world), Is.EqualTo(0), "the response command body does not run (G161)");
        }

        [Test]
        public void TimeoutsAtTheSameTickRunInFlowOrderWithSleepers()
        {
            var world = Make(Timed + "@command(free=True, acts_for=None)\ndef Nap(u: Ref[Unit]):\n    wait(ticks=2)\n");
            world.Submit("host", "core.Nap", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Raid(world);
            var r = world.Advance(2);
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Flow), Is.EqualTo(new[] { "wait 1", "timeout 2" }));
        }

        [Test]
        public void AFailingTimeoutIsRolledBackAndDropped()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
                             "    r = ask(u, \"Raid?\", choices=[\"fight\"], deadline=1, on_timeout=\"fight\")\n" +
                             "    u.Out.s = \"half\"\n    x = 1 // 0\n");
            Raid(world);
            var timed = world.Advance().Reactions.Single();
            Assert.That(timed.Outcome, Is.EqualTo(ReactionOutcome.ScriptError));
            Assert.That(timed.Error!.Message, Does.Contain("the command core.Raid (after its deadline)"));
            Assert.That(S(world), Is.EqualTo(""));
            Assert.That(world.OpenRequests, Is.Empty, "dropped, not timed out again every tick (as G157)");
            Assert.That(world.Advance().Reactions, Is.Empty);
        }

        [Test]
        public void OnTimeoutIsSavedAndStillFiresAfterLoading()
        {
            var world = Make(Timed);
            Raid(world);
            world.Advance();
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"on_timeout\": {\n"));
            var back = World.Load(Content(Timed), save).World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");
            back.Advance();
            Assert.That(S(back) + " " + N(back), Is.EqualTo("hide 11"));
        }

        [Test]
        public void ASaveWithAPassedDeadlineIsRejected()
        {
            var world = Make(Timed);
            world.Advance();
            Raid(world); // 마감 틱 3
            var text = Encoding.UTF8.GetString(world.Save());
            Assert.That(text, Does.Contain("\"deadline\": 3"));
            var bad = Encoding.UTF8.GetBytes(text.Replace("\"deadline\": 3", "\"deadline\": 1"));
            var loaded = World.Load(Content(Timed), bad);
            Assert.That(loaded.World, Is.Null);
            Assert.That(loaded.Diagnostics.Select(d => d.Message), Has.Some.Contains("should have timed out at tick 1"));
        }

        // ---- 멈춘 Flow의 대상 소멸 (S5 on_stale 기본 취소, spec-gaps G163·G164) ----

        private const string Kill = "@command(free=True, acts_for=None)\ndef Kill(u: Ref[Unit]):\n    destroy(u)\n";

        private static SubmitResult Destroy(World w, EntityId id) =>
            w.Submit("host", "core.Kill", new Dictionary<string, Value> { ["u"] = new EntityValue(id) });

        private static string Line(Reaction x) => x.Signal + " " + x.Handler + " " + x.Outcome + " " + x.Flow;

        [Test]
        public void ASleepingFlowWhoseSubjectIsDestroyedIsCanceledRightAway()
        {
            var world = Make(Waiter + Kill);
            Brew(world);
            var kill = Destroy(world, _u);
            // 파괴한 제출의 결과에 바로 나온다 (G164). 오류가 아니다
            var canceled = kill.Reactions.Single();
            Assert.That(Line(canceled), Is.EqualTo("stale core.Brew Canceled 1"));
            Assert.That(canceled.Message, Is.EqualTo("flow 1 was canceled: its subject " + _u + " was destroyed"));
            Assert.That(canceled.Error, Is.Null);
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Contain("\"flows\": []"));
            Assert.That(world.Advance(3).Reactions, Is.Empty);
        }

        [Test]
        public void TheSubjectIsWhatTheFlowWasGivenEvenIfTheVariableChanges()
        {
            var world = Make(Kill + "@command(free=True, acts_for=None)\ndef Swap(u: Ref[Unit], v: Ref[Unit]):\n" +
                             "    u = v\n    wait(ticks=1)\n    u.Out.s = \"woke\"\n");
            var other = world.Create("Scout");
            world.Submit("host", "core.Swap",
                new Dictionary<string, Value> { ["u"] = new EntityValue(_u), ["v"] = new EntityValue(other) });
            Assert.That(Destroy(world, _u).Reactions.Single().Outcome, Is.EqualTo(ReactionOutcome.Canceled),
                "u was given at the entry; the body pointing u elsewhere does not change the subject (G163)");
            world.Advance();
            Assert.That(((StrValue)world.Get(other, world.Member("Out", "s"))).Value, Is.EqualTo(""));
        }

        [Test]
        public void AnEntityMadeInTheBodyIsNotASubject()
        {
            var world = Make(Kill + "@command(free=True, acts_for=None)\ndef Make(u: Ref[Unit]):\n" +
                             "    x = spawn(\"Scout\")\n    destroy(x)\n    wait(ticks=1)\n    u.Out.s = \"woke\"\n");
            var made = world.Submit("host", "core.Make", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(made.Reactions, Is.Empty, "a destroy happened, but not of a subject");
            Assert.That(world.Advance().Reactions.Single().Outcome, Is.EqualTo(ReactionOutcome.Finished));
            Assert.That(S(world), Is.EqualTo("woke"));
        }

        [Test]
        public void ACommandThatDestroysItsOwnSubjectAndWaitsIsCanceledInItsOwnResult()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Doom(u: Ref[Unit]):\n    destroy(u)\n    wait(ticks=1)\n");
            var r = world.Submit("host", "core.Doom", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(r.SleepsUntil, Is.Null, "it was canceled before the call returned");
            Assert.That(Line(r.Reactions.Single()), Is.EqualTo("stale core.Doom Canceled 1"));
        }

        [Test]
        public void AQuestionWhoseAnswerersAreAllGoneIsCanceledAndStopsBlockingTime()
        {
            // 재현했던 것: 답할 엔티티가 모두 사라진 막는 질문이 Advance를 0틱에 멈추고, 파괴된 엔티티로서 한 답이 받아졌다
            var world = Make(Kill + "@command(free=True, acts_for=None)\ndef Ask():\n" +
                             "    x = spawn(\"Scout\")\n    r = ask(x, \"Raid?\", choices=[\"fight\"])\n    spawn(\"Scout\")\n");
            var q = world.Submit("host", "core.Ask", new Dictionary<string, Value>()).OpenedRequest!;
            var x = q.Who.Single();
            var canceled = Destroy(world, x).Reactions.Single();
            Assert.That(canceled.Signal + " " + canceled.Outcome, Is.EqualTo("stale Canceled"));
            Assert.That(canceled.Message, Does.Contain("everyone who could answer request " + q.Id + " was destroyed"));
            Assert.That(world.OpenRequests, Is.Empty);
            Assert.That(world.Advance(3).Advanced, Is.EqualTo(3), "it no longer blocks time");
            Assert.That(world.Answer("host", q.Id, x, 0).Succeeded, Is.False, "the request is gone");
            Assert.That(world.All("Unit"), Is.EqualTo(new[] { _u }), "the rest of the flow did not run");
        }

        [Test]
        public void AQuestionKeepsGoingWhileSomeoneCanStillAnswer()
        {
            var world = Make(Kill + "@command(free=True, acts_for=None)\ndef Ask(u: Ref[Unit]):\n" +
                             "    x = spawn(\"Scout\")\n    r = ask([x, u], \"Raid?\", choices=[\"fight\"])\n");
            var q = world.Submit("host", "core.Ask", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) }).OpenedRequest!;
            var x = q.Who.First(id => id != _u);
            Assert.That(Destroy(world, x).Reactions, Is.Empty);
            Assert.That(world.OpenRequests.Select(r => r.Id), Is.EqualTo(new[] { q.Id }));
        }

        [Test]
        public void AnEntityDestroyedByTheHostIsSweptWhenTimeAdvances()
        {
            var world = Make(Waiter);
            Brew(world);
            world.Destroy(_u); // 실행 밖: 결과가 없어 다음 실행의 결과에 남는다
            var r = world.Advance();
            Assert.That(Line(r.Reactions.Single()), Is.EqualTo("stale core.Brew Canceled 1"));
        }

        [Test]
        public void SubjectsAreSaved()
        {
            var world = Make(Waiter + Kill);
            Brew(world);
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"subjects\": [\n        " + _u.Value + "\n      ]"));
            var back = World.Load(Content(Waiter + Kill), save).World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");
            Assert.That(Destroy(back, _u).Reactions.Single().Outcome, Is.EqualTo(ReactionOutcome.Canceled));
        }

        [Test]
        public void ASaveWithAStaleFlowIsRejected()
        {
            var world = Make(Waiter + Kill);
            var gone = world.Create("Scout");
            Destroy(world, gone);
            Brew(world);
            var text = Encoding.UTF8.GetString(world.Save());
            var bad = Encoding.UTF8.GetBytes(text.Replace("\"subjects\": [\n        " + _u.Value + "\n      ]",
                "\"subjects\": [\n        " + gone.Value + "\n      ]"));
            var loaded = World.Load(Content(Waiter + Kill), bad);
            Assert.That(loaded.World, Is.Null);
            Assert.That(loaded.Diagnostics.Select(d => d.Message), Has.Some.Contains("should have been canceled: its subject"));
        }

        // ---- about= : 멈추는 자리에서 적는 대상 (spec-gaps G165) ----

        private EntityId _ruler;

        /// <summary>마을(<see cref="_u"/>)과 그 군주(<see cref="_ruler"/>). 커맨드 Go(town)가 <paramref name="body"/>를 실행한다.</summary>
        private World Ruled(string body)
        {
            Mod("core", schema: "component(\"Rule\", ruler=ref(\"Unit\", optional=True))\n" +
                                Schema.Replace("components=[\"Out\", \"Life\"]", "components=[\"Out\", \"Life\", \"Rule\"]"),
                defs: "Unit(\"Scout\")\n" + Kill + "@command(free=True, acts_for=None)\ndef Go(town: Ref[Unit]):\n" + body);
            var world = new World(LoadClean());
            _ruler = world.Create("Scout");
            _u = world.Create("Scout", new Init(world.Member("Rule", "ruler"), new EntityValue(_ruler)));
            world.Submit("host", "core.Go", new Dictionary<string, Value> { ["town"] = new EntityValue(_u) });
            return world;
        }

        private string RulerText(World w) => ((StrValue)w.Get(_ruler, w.Member("Out", "s"))).Value;

        [Test]
        public void AboutCatchesAnIndirectSubject()
        {
            // 재현했던 것: about 없이는 군주만 사라지면 깨어나 "destroyed" 스크립트 오류였다
            var world = Ruled("    r = town.Rule.ruler\n    wait(ticks=1, about=r)\n    r.Out.s = \"taxed\"\n");
            var canceled = Destroy(world, _ruler).Reactions.Single();
            Assert.That(canceled.Signal + " " + canceled.Outcome, Is.EqualTo("stale Canceled"));
            Assert.That(canceled.Message, Does.Contain("its subject " + _ruler + " was destroyed"));
        }

        [Test]
        public void AboutReplacesTheEntrySubjects()
        {
            // 관심사가 군주면 마을이 사라져도 계속한다
            var world = Ruled("    r = town.Rule.ruler\n    wait(ticks=1, about=[r])\n    r.Out.s = \"taxed\"\n");
            Assert.That(Destroy(world, _u).Reactions, Is.Empty);
            Assert.That(world.Advance().Reactions.Single().Outcome, Is.EqualTo(ReactionOutcome.Finished));
            Assert.That(RulerText(world), Is.EqualTo("taxed"));
        }

        [Test]
        public void AnEmptyAboutWatchesNothing()
        {
            var world = Ruled("    r = town.Rule.ruler\n    wait(ticks=1, about=[])\n    r.Out.s = \"taxed\"\n");
            Assert.That(Destroy(world, _u).Reactions, Is.Empty);
            world.Advance();
            Assert.That(RulerText(world), Is.EqualTo("taxed"));
        }

        [Test]
        public void AboutHoldsOnlyForThatPause()
        {
            // 첫 멈춤은 군주만, 둘째 멈춤은 about이 없어 다시 입구 인자(마을)를 본다
            var world = Ruled("    r = town.Rule.ruler\n    wait(ticks=1, about=[r])\n    wait(ticks=1)\n    town.Out.s = \"done\"\n");
            world.Advance();
            Assert.That(Destroy(world, _ruler).Reactions, Is.Empty, "the second pause does not watch the ruler");
            world.Advance();
            Assert.That(S(world), Is.EqualTo("done"));
        }

        [Test]
        public void AQuestionStillEndsWhenNobodyCanAnswerWhateverItIsAbout()
        {
            var world = Ruled("    r = ask(town.Rule.ruler, \"Tax?\", choices=[\"yes\"], about=[])\n");
            Assert.That(Destroy(world, _ruler).Reactions.Single().Message, Does.Contain("everyone who could answer"));
        }

        [TestCase("about=1", "about= of wait() takes entities, not int")]
        [TestCase("about=[town, 2]", "about= of wait() takes entities, not int")]
        [TestCase("abuot=[town]", "wait() has no argument 'abuot'")]
        public void ABadAboutIsAnError(string arg, string message)
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\")\n@command(free=True, acts_for=None)\ndef Go(town: Ref[Unit]):\n" +
                                              "    wait(ticks=1, " + arg + ")\n");
            var world = new World(LoadClean());
            _u = world.Create("Scout");
            var r = world.Submit("host", "core.Go", new Dictionary<string, Value> { ["town"] = new EntityValue(_u) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
        }

        [Test]
        public void AboutADestroyedEntityIsAnError()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    x = spawn(\"Scout\")\n" +
                             "    destroy(x)\n    wait(ticks=1, about=x)\n");
            var r = world.Submit("host", "core.Go", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Assert.That(r.Error!.Message, Does.Contain("which is already destroyed"));
        }

        [TestCase("@command(free=True, acts_for=None, on_stale=\"keep\")\ndef Go(u: Ref[Unit]):\n    pass\n", "@command")]
        [TestCase("@on(Tick, on_stale=\"keep\")\ndef go():\n    pass\n", "@on")]
        public void OnStaleIsReservedButNotSupported(string defs, string decorator)
        {
            Mod("core", schema: Schema, defs: defs);
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.StartWith(Codes.CmdBadDecorator + " " + decorator +
                "(on_stale=...) is not supported yet: a paused flow whose subject is destroyed is always canceled | " +
                "to keep going when an entity is destroyed, leave it out of about= on wait() or ask()"));
        }

        [Test]
        public void AboutIsSaved()
        {
            const string body = "    r = town.Rule.ruler\n    wait(ticks=1, about=[r])\n    r.Out.s = \"taxed\"\n";
            var world = Ruled(body);
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"about\": [\n        " + _ruler.Value + "\n      ]"));
            var content = LoadClean();
            var back = World.Load(content, save).World!;
            Assert.That(back.Save(), Is.EqualTo(save), "C4");
            Assert.That(Destroy(back, _ruler).Reactions.Single().Outcome, Is.EqualTo(ReactionOutcome.Canceled));
        }

        // ---- 스크립트의 advance(n) (spec X2, spec-gaps G167) ----

        private SubmitResult Go(World w) =>
            w.Submit("host", "core.Go", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });

        [Test]
        public void AdvanceFromACommandMovesTimeThenGoesOn()
        {
            var world = Make("@on(Tick)\ndef mark():\n    pass\n" +
                             "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    u.Out.s = \"before\"\n" +
                             "    n = advance(3)\n    u.Out.n = n * 100 + now()\n");
            var r = Go(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(world.Tick, Is.EqualTo(3));
            Assert.That(N(world), Is.EqualTo(303), "advance() gives the ticks it moved; now() is after them");
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Handler + " " + x.Outcome), Is.EqualTo(new[]
            {
                "Tick core.mark Finished", "Tick core.mark Finished", "Tick core.mark Finished", "advance core.Go Finished",
            }));
        }

        [Test]
        public void WhatCameBeforeAdvanceIsSeenByTickHandlers()
        {
            // advance에서 멈추면 거기까지가 확정되고, 그 틱에 깨어나는 Flow가 그 상태를 본다
            var world = Make("@command(free=True, acts_for=None)\ndef Watch(u: Ref[Unit]):\n    wait(ticks=1)\n    if u.Out.s == \"abcd\":\n        u.Out.n = 4\n" +
                             "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    u.Out.s = \"abcd\"\n    advance(1)\n");
            world.Submit("host", "core.Watch", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            var r = Go(world);
            Assert.That(N(world), Is.EqualTo(4));
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Handler), Is.EqualTo(new[] { "wait core.Watch", "advance core.Go" }));
        }

        [Test]
        public void ATurnLoopAsksThenAdvances()
        {
            // 표준 모드의 턴 루프 모양 (U3): 차례를 묻고(시간을 막는다), 답이 오면 한 틱 진행하고 다시 묻는다
            var world = Make("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    for i in range(2):\n" +
                             "        r = ask(u, \"your_turn\", choices=[\"end\"])\n        advance(1)\n    u.Out.s = \"over\"\n");
            var q = Go(world).OpenedRequest!;
            Assert.That(world.Advance().Blocked, Is.True, "the turn question blocks time");
            var a = world.Answer("host", q.Id, _u, 0);
            Assert.That(world.Tick, Is.EqualTo(1));
            var next = a.OpenedRequest!;
            Assert.That(a.Reactions.Single().Signal, Is.EqualTo("advance"));
            Assert.That(next.Id, Is.Not.EqualTo(q.Id));
            world.Answer("host", next.Id, _u, 0);
            Assert.That(world.Tick, Is.EqualTo(2));
            Assert.That(S(world), Is.EqualTo("over"));
        }

        [Test]
        public void ABlockingQuestionStopsAScriptAdvance()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Hold(u: Ref[Unit]):\n    r = ask(u, \"Q\", choices=[\"a\"])\n" +
                             "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    n = advance(3)\n    u.Out.n = n + 10\n");
            world.Submit("host", "core.Hold", new Dictionary<string, Value> { ["u"] = new EntityValue(_u) });
            Go(world);
            Assert.That(world.Tick, Is.EqualTo(0));
            Assert.That(N(world), Is.EqualTo(10), "advance() gave 0: no tick moved");
        }

        [Test]
        public void AdvanceInATickHandlerIsAnError()
        {
            var world = Make("@on(Tick)\ndef again():\n    advance(1)\n");
            var r = world.Advance();
            Assert.That(world.Tick, Is.EqualTo(1));
            var failed = r.Reactions.Single();
            Assert.That(failed.Outcome, Is.EqualTo(ReactionOutcome.ScriptError));
            Assert.That(failed.Error!.Message, Does.Contain("advance() cannot be called while time is already advancing"));
        }

        [Test]
        public void AdvanceInASignalHandlerIsAnError()
        {
            Mod("core", schema: Schema + "signal(\"Ring\", u=Ref[\"Unit\"])\n",
                defs: "Unit(\"Scout\")\n@on(Ring)\ndef later(u):\n    advance(1)\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    emit(Ring(u=u))\n");
            var world = new World(LoadClean());
            _u = world.Create("Scout");
            var failed = Go(world).Reactions.Single();
            Assert.That(failed.Error!.Message, Does.Contain("advance() cannot be called from a signal handler"));
            Assert.That(world.Tick, Is.EqualTo(0));
        }

        [Test]
        public void AdvanceInAWokenFlowIsAnError()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    wait(ticks=1)\n    u.Out.s = \"x\"\n    advance(1)\n");
            Go(world);
            var woke = world.Advance().Reactions.Single();
            Assert.That(woke.Outcome, Is.EqualTo(ReactionOutcome.ScriptError));
            Assert.That(woke.Error!.Message, Does.Contain("while time is already advancing"));
            Assert.That(S(world), Is.EqualTo(""), "the woken part is rolled back (G157)");
        }

        [Test]
        public void AFailureAfterAdvanceKeepsTheTicksAndWhatCameBefore()
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    u.Out.s = \"kept\"\n    advance(2)\n" +
                             "    u.Out.n = 1\n    x = 1 // 0\n");
            var r = Go(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded), "the command's own part succeeded");
            var failed = r.Reactions.Single();
            Assert.That(failed.Signal + " " + failed.Outcome, Is.EqualTo("advance ScriptError"));
            Assert.That(failed.Error!.Message, Does.Contain("(after advancing)"));
            Assert.That(world.Tick, Is.EqualTo(2));
            Assert.That(S(world) + " " + N(world), Is.EqualTo("kept 0"));
        }

        [Test]
        public void AdvanceInAnExpressionIsALoadError()
        {
            Mod("core", schema: Schema, defs: "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    x = [advance(1)]\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.Contains("advance() makes time pass, so it can only be called as a statement"));
        }

        private World Limited(string defs, int limit)
        {
            var world = new World(Content(defs), maxAdvancePerCall: limit);
            _u = world.Create("Scout");
            return world;
        }

        [Test]
        public void ALoopOfAdvanceStopsAtThePerCallLimit()
        {
            // 질문 없이 advance만 되풀이하는 루프는 호출당 상한에서 잘린다 (G168)
            var world = Limited("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    u.Out.s = \"start\"\n" +
                                "    for i in range(1000000):\n        advance(1)\n        u.Out.n = u.Out.n + 1\n", 5);
            var r = Go(world);
            Assert.That(world.Tick, Is.EqualTo(5));
            Assert.That(N(world), Is.EqualTo(5), "what came before the limit stays");
            var failed = r.Reactions.Last();
            Assert.That(failed.Signal + " " + failed.Outcome, Is.EqualTo("advance ScriptError"));
            Assert.That(failed.Error!.Message, Does.Contain("called advance(1) after 5 ticks asked in this call, past the limit of 5 ticks per call"));
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Contain("\"flows\": []"));
        }

        [Test]
        public void OneBigAdvancePastTheLimitIsAnError()
        {
            var world = Limited("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    advance(1000000)\n", 100);
            var r = Go(world);
            Assert.That(world.Tick, Is.EqualTo(0));
            Assert.That(r.Reactions.Single().Error!.Message, Does.Contain("called advance(1000000) after 0 ticks"));
        }

        [Test]
        public void TheLimitIsPerCall()
        {
            var world = Limited("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    advance(3)\n", 3);
            Go(world);
            Go(world);
            Assert.That(world.Tick, Is.EqualTo(6), "each submit gets its own 3 ticks");
        }

        [Test]
        public void TheLimitMustBePositive()
        {
            Assert.That(() => new World(Content(""), maxAdvancePerCall: 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase("advance()", "advance() takes the number of ticks")]
        [TestCase("advance(0)", "advance() needs a number of ticks of at least 1, not 0")]
        [TestCase("advance(ticks=1)", "advance() takes the number of ticks")]
        public void ABadAdvanceIsAnError(string call, string message)
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    " + call + "\n");
            var r = Go(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
        }

        [TestCase(", deadline=2", "ask() has a deadline but no on_timeout=")]
        [TestCase(", deadline=2, on_timeout=\"flee\"", "on_timeout= of ask() must be one of its choices or responses, not str")]
        [TestCase(", on_timeout=\"hide\", blocks_time=False", "ask() has on_timeout= but no deadline, so it never times out")]
        public void ABadOnTimeoutIsAnError(string extra, string message)
        {
            var r = Raid(Deadlined(extra));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
        }

        [Test]
        public void AnOnTimeoutThatIsBothAChoiceAndAResponseIsAnError()
        {
            var world = Make("@command(acts_for=lambda u: u)\ndef Pass(u: Ref[Unit]):\n    pass\n" +
                             "@command(free=True, acts_for=None)\ndef Raid(u: Ref[Unit]):\n" +
                             "    r = ask(u, \"Raid?\", choices=[\"Pass\"], responses=[\"Pass\"], deadline=1, on_timeout=\"Pass\")\n");
            var r = Raid(world);
            Assert.That(r.Error!.Message, Does.Contain("is both a choice and a response"));
        }

        [Test]
        public void WaitInAnExpressionIsALoadError()
        {
            Mod("core", schema: Schema, defs: "@command(free=True, acts_for=None)\ndef Brew(u: Ref[Unit]):\n    x = [wait(ticks=1)]\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.Contains("wait() waits for time to pass, so it can only be called as a statement"));
        }

        [TestCase("wait()")]
        [TestCase("wait(ticks=0)")]
        [TestCase("wait(ticks=1.5)")]
        [TestCase("wait(2)")]
        public void WaitNeedsAPositiveTickCount(string call)
        {
            var world = Make("@command(free=True, acts_for=None)\ndef Brew(u: Ref[Unit]):\n    u.Out.s = \"x\"\n    " + call + "\n");
            var r = Brew(world);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("wait("));
            Assert.That(S(world), Is.EqualTo(""));
        }
    }
}
