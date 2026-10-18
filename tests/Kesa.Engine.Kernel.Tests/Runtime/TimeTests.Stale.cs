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
    /// <summary>멈춘 Flow의 대상 소멸(G163·G164), <c>about=</c>(G165), 스크립트의 <c>advance(n)</c>(G167·G168).</summary>
    public partial class TimeTests
    {
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
