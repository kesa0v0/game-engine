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
    /// <summary><c>@when</c> 반응과 정착 (spec S5, spec-gaps G169~G171).</summary>
    public class WhenTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Out\", s=field(str, default=\"\"), n=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Out\"])\n";

        private const string SetN = "@command(free=True, acts_for=None)\ndef SetN(u: Ref[Unit], n: int):\n    u.Out.n = n\n";

        private EntityId _a;
        private EntityId _b;

        private ContentLoadResult Content(string defs, string schema = Schema)
        {
            Mod("core", schema: schema, defs: "Unit(\"Scout\")\n" + SetN + defs);
            return LoadClean();
        }

        private World Make(string defs, int maxSettleRounds = World.DefaultMaxSettleRounds)
        {
            var world = new World(Content(defs), maxSettleRounds: maxSettleRounds);
            _a = world.Create("Scout");
            _b = world.Create("Scout");
            return world;
        }

        private static SubmitResult Set(World w, EntityId u, long n) =>
            w.Submit("host", "core.SetN", new Dictionary<string, Value> { ["u"] = new EntityValue(u), ["n"] = new IntValue(n) });

        private static string S(World w, EntityId u) => ((StrValue)w.Get(u, w.Member("Out", "s"))).Value;

        private static string Show(Reaction r) =>
            r.Signal + " " + r.Handler + " " + string.Join(",", r.Values.Select(p => p.Key + "=" + ((EntityValue)p.Value).Id)) + " " + r.Outcome;

        private static string Diag(Diagnostic d) => d.Code + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        private const string Mark = "@when(lambda u: u.Out.n >= 5)\ndef mark(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n";

        [Test]
        public void AReactionFiresWhenItsConditionTurnsTrueForEachEntity()
        {
            var world = Make(Mark);
            var r = Set(world, _a, 5);
            Assert.That(r.Reactions.Select(Show), Is.EqualTo(new[] { "when core.mark u=" + _a + " Finished" }));
            Assert.That(S(world, _a), Is.EqualTo("!"));
            Assert.That(Set(world, _a, 6).Reactions, Is.Empty, "true to true does not fire again");
            Set(world, _a, 0);
            Set(world, _a, 7);
            Assert.That(S(world, _a), Is.EqualTo("!!"), "false then true fires again");
            Assert.That(Set(world, _b, 5).Reactions.Select(Show), Is.EqualTo(new[] { "when core.mark u=" + _b + " Finished" }),
                "each entity has its own record (G169)");
            Assert.That(S(world, _a), Is.EqualTo("!!"));
        }

        [Test]
        public void AValueThatGoesUpAndBackInsideOneCommandIsNotAnEdge()
        {
            var world = Make(Mark + "@command(free=True, acts_for=None)\ndef Blip(u: Ref[Unit]):\n    u.Out.n = 9\n    u.Out.n = 0\n");
            var r = world.Submit("host", "core.Blip", new Dictionary<string, Value> { ["u"] = new EntityValue(_a) });
            Assert.That(r.Reactions, Is.Empty, "when is only looked at when the command is over (S5)");
        }

        [Test]
        public void AConditionAlreadyTrueAtTheFirstSettleFires()
        {
            // 기록이 없으면 지난번은 거짓이다 (G170): 처음부터 참인 엔티티도 놓치지 않는다
            var world = Make(Mark + "@command(free=True, acts_for=None)\ndef Make9():\n    x = spawn(\"Scout\")\n    x.Out.n = 9\n");
            var r = world.Submit("host", "core.Make9", new Dictionary<string, Value>());
            var made = world.All("Unit").Single(id => id != _a && id != _b);
            Assert.That(r.Reactions.Select(Show), Is.EqualTo(new[] { "when core.mark u=" + made + " Finished" }));
        }

        [Test]
        public void AGlobalConditionIsWatchedOnceAndSettlesAtTheEndOfEachTick()
        {
            var world = Make("@when(lambda: now() >= 2)\ndef late():\n    spawn(\"Scout\")\n");
            Assert.That(world.Advance(1).Reactions, Is.Empty);
            var r = world.Advance(1);
            Assert.That(r.Reactions.Select(x => x.Signal + " " + x.Handler + " " + x.Values.Count + " " + x.Outcome),
                Is.EqualTo(new[] { "when core.late 0 Finished" }));
            world.Advance(3);
            Assert.That(world.All("Unit").Count(), Is.EqualTo(3), "it stays true, so it fired once");
        }

        [Test]
        public void AReactionThatMakesAnotherTrueRunsInTheSameSettle()
        {
            var world = Make(Mark + "@when(lambda u: u.Out.s == \"!\")\ndef second(u: Ref[Unit]):\n    u.Out.n = 100\n");
            var r = Set(world, _a, 5);
            Assert.That(r.Reactions.Select(Show),
                Is.EqualTo(new[] { "when core.mark u=" + _a + " Finished", "when core.second u=" + _a + " Finished" }));
            Assert.That(((IntValue)world.Get(_a, world.Member("Out", "n"))).Value, Is.EqualTo(100));
        }

        [Test]
        public void ReactionsInOneRoundRunInStageOrder()
        {
            const string stages = "stage(\"core.first\")\nstage(\"core.second\", after=[\"core.first\"])\n";
            Mod("core", schema: Schema + stages, defs: "Unit(\"Scout\")\n" + SetN +
                "@when(lambda u: u.Out.n >= 5, stage=\"core.second\")\ndef b(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"b\"\n" +
                "@when(lambda u: u.Out.n >= 5)\ndef c(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"c\"\n" +
                "@when(lambda u: u.Out.n >= 5, stage=\"core.first\")\ndef a(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"a\"\n");
            var world = new World(LoadClean());
            _a = world.Create("Scout");
            Set(world, _a, 5);
            Assert.That(S(world, _a), Is.EqualTo("abc"), "stages first, then reactions without a stage (S5)");
        }

        private const string Flip =
            "@when(lambda u: u.Out.n <= 0)\ndef revive(u: Ref[Unit]):\n    u.Out.n = 10\n" +
            "@when(lambda u: u.Out.n >= 10)\ndef curse(u: Ref[Unit]):\n    u.Out.n = 0\n";

        [Test]
        public void SettlingStopsAtTheRoundLimitAndKeepsWhatRan()
        {
            var world = Make(Flip, maxSettleRounds: 4);
            var r = Set(world, _a, 5);
            var dropped = r.Reactions.Where(x => x.Outcome == ReactionOutcome.Dropped).ToList();
            Assert.That(dropped.Count, Is.GreaterThan(0));
            Assert.That(dropped[0].Message, Does.Contain("settling went past 4 rounds; the chain was: "));
            Assert.That(dropped[0].Message, Does.EndWith("core.revive → core.curse → core.revive → core.curse → core.revive"));
            Assert.That(dropped[0].Hint, Does.Contain("maxSettleRounds (spec-gaps G171)"));
            Assert.That(r.Reactions.Count(x => x.Outcome == ReactionOutcome.Finished), Is.GreaterThanOrEqualTo(4), "earlier rounds stay");
            // 고리는 처음부터 HP 0인 _b에서 돈다. _a를 건드리는 다음 제출의 정착에서 _b의 고리가 다시 시도된다
            Assert.That(dropped.All(x => ((EntityValue)x.Values["u"]).Id == _b), Is.True);
            Assert.That(Set(world, _a, 6).Reactions.Any(x => x.Outcome == ReactionOutcome.Dropped && ((EntityValue)x.Values["u"]).Id == _b),
                Is.True, "a dropped reaction is not recorded as true, so the next settle tries it again");
        }

        [Test]
        public void AReactionSeesWhatTheOneBeforeItDidInTheSameSettle()
        {
            // 본문 하나가 끝날 때마다 다시 본다 (G172): 부활이 HP를 10으로 올린 순간을 보아야 저주 뒤에 다시 발동한다.
            // 차례마다 한 번만 보면 0 → 10 → 0을 놓쳐 조용히 끝났다 (재현했던 것)
            var world = Make(Flip, maxSettleRounds: 6);
            var r = Set(world, _a, 5);
            Assert.That(r.Reactions.Where(x => ((EntityValue)x.Values["u"]).Id == _b && x.Outcome == ReactionOutcome.Finished)
                    .Select(x => x.Handler),
                Is.EqualTo(new[] { "core.revive", "core.curse", "core.revive", "core.curse", "core.revive", "core.curse" }));
        }

        [Test]
        public void ASecondReactionIsSkippedWhenTheFirstMadeItFalse()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5)\ndef eat(u: Ref[Unit]):\n    u.Out.n = u.Out.n - 5\n    u.Out.s = u.Out.s + \"a\"\n" +
                             "@when(lambda u: u.Out.n >= 5)\ndef also(u: Ref[Unit]):\n    u.Out.n = u.Out.n - 5\n    u.Out.s = u.Out.s + \"b\"\n");
            Set(world, _a, 5);
            Assert.That(S(world, _a), Is.EqualTo("a"), "after eat the condition of also is false, so it does not run");
        }

        [Test]
        public void ManyEntitiesAtOnceAreOneRound()
        {
            // 회차는 세대다: 한꺼번에 참이 된 것이 많아도 상한에 걸리지 않는다
            var world = Make(Mark + "@command(free=True, acts_for=None)\ndef Many():\n    for i in range(10):\n        x = spawn(\"Scout\")\n        x.Out.n = 9\n",
                maxSettleRounds: 2);
            var r = world.Submit("host", "core.Many", new Dictionary<string, Value>());
            Assert.That(r.Reactions.Count(x => x.Outcome == ReactionOutcome.Finished), Is.EqualTo(10));
            Assert.That(r.Reactions.Any(x => x.Outcome == ReactionOutcome.Dropped), Is.False);
        }

        [Test]
        public void AChainThatKeepsMakingEntitiesStopsAtTheLimit()
        {
            var world = Make("@when(lambda u: u.Out.n == 3)\ndef breed(u: Ref[Unit]):\n    x = spawn(\"Scout\")\n    x.Out.n = 3\n",
                maxSettleRounds: 5);
            var r = Set(world, _a, 3);
            Assert.That(r.Reactions.Count(x => x.Outcome == ReactionOutcome.Finished), Is.EqualTo(5));
            Assert.That(r.Reactions.Last().Outcome, Is.EqualTo(ReactionOutcome.Dropped));
            Assert.That(r.Reactions.Last().Message, Does.EndWith("the chain was: core.breed → core.breed → core.breed → core.breed → core.breed → core.breed"));
        }

        private const string Repeat = "@when(lambda u: u.Out.n >= 5, repeat=True)\ndef nag(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n";

        [Test]
        public void ARepeatingReactionRunsOnceEachTickWhileTrue()
        {
            var world = Make(Repeat);
            Assert.That(Set(world, _a, 5).Reactions, Is.Empty, "a command's settle does not look at repeat reactions (G173)");
            var r = world.Advance(3);
            Assert.That(r.Reactions.Select(Show), Is.EqualTo(Enumerable.Repeat("when core.nag u=" + _a + " Finished", 3)),
                "once each tick, and it never reaches the round limit");
            Assert.That(S(world, _a), Is.EqualTo("!!!"));
            Set(world, _a, 0);
            world.Advance(2);
            Assert.That(S(world, _a), Is.EqualTo("!!!"), "false, so it stops");
        }

        [Test]
        public void ARepeatingReactionRunsAtMostOnceInATickEvenIfItTurnsTrueAgain()
        {
            // 본문이 조건을 거짓으로 만들고 다른 반응이 다시 참으로 만들어도 그 틱에는 한 번이다
            var world = Make("@when(lambda u: u.Out.n >= 5, repeat=True)\ndef nag(u: Ref[Unit]):\n    u.Out.n = 0\n    u.Out.s = u.Out.s + \"!\"\n" +
                             "@when(lambda u: u.Out.n == 0)\ndef refill(u: Ref[Unit]):\n    u.Out.n = 9\n");
            Set(world, _a, 9);
            Set(world, _b, 9);
            world.Advance(1);
            Assert.That(S(world, _a), Is.EqualTo("!"));
            world.Advance(1);
            Assert.That(S(world, _a), Is.EqualTo("!!"));
        }

        [Test]
        public void RepeatingReactionsLeaveNoRecordInTheSave()
        {
            var world = Make(Repeat);
            Set(world, _a, 5);
            world.Advance(1);
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Contain("\"when\": []"));
        }

        private const string Cool = "@when(lambda u: u.Out.n >= 5, cooldown=5)\ndef mark(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n";

        [Test]
        public void ATurnTrueDuringTheCooldownFiresWhenItEnds()
        {
            // 쿨다운 동안은 거짓으로 본다 (G174): 틱 0에 발동, 틱 5까지 쉰다
            var world = Make(Cool);
            Set(world, _a, 5);
            Set(world, _a, 0);
            Assert.That(Set(world, _a, 9).Reactions, Is.Empty, "cooling down");
            Assert.That(world.Advance(4).Reactions, Is.Empty);
            Assert.That(world.Advance(1).Reactions.Select(Show), Is.EqualTo(new[] { "when core.mark u=" + _a + " Finished" }),
                "still true when the cooldown ends, so it fires then");
            Assert.That(S(world, _a), Is.EqualTo("!!"));
        }

        [Test]
        public void EachEntityHasItsOwnCooldown()
        {
            var world = Make(Cool);
            Set(world, _a, 5);
            Assert.That(Set(world, _b, 5).Reactions.Select(Show), Is.EqualTo(new[] { "when core.mark u=" + _b + " Finished" }));
        }

        [Test]
        public void RepeatWithACooldownRunsEveryFewTicks()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5, repeat=True, cooldown=3)\ndef nag(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n");
            Set(world, _a, 5);
            var r = world.Advance(7);
            Assert.That(r.Reactions.Count, Is.EqualTo(3), "ticks 1, 4 and 7");
        }

        [Test]
        public void TheCooldownIsSavedAndLoaded()
        {
            var content = Content(Cool);
            var world = new World(content);
            _a = world.Create("Scout");
            Set(world, _a, 5);
            Set(world, _a, 0);
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"until\": 5"));
            var again = World.Load(content, save).World!;
            Assert.That(Set(again, _a, 9).Reactions, Is.Empty, "still cooling down after loading");
            Assert.That(again.Advance(5).Reactions.Count, Is.EqualTo(1));
        }

        [Test]
        public void OnceRunsOnceForEachEntity()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5, once=True)\ndef riot(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n");
            Set(world, _a, 5);
            Set(world, _a, 0);
            Set(world, _a, 9);
            Assert.That(S(world, _a), Is.EqualTo("!"), "false then true again, but it ran once already (G175)");
            Assert.That(Set(world, _b, 5).Reactions.Select(Show), Is.EqualTo(new[] { "when core.riot u=" + _b + " Finished" }),
                "another entity has its own once");
        }

        [Test]
        public void OnceGameRunsOnceInTheWholeGame()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5, once=\"game\")\ndef first(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n" +
                             "@command(free=True, acts_for=None)\ndef Both(a: Ref[Unit], b: Ref[Unit]):\n    a.Out.n = 5\n    b.Out.n = 5\n");
            var r = world.Submit("host", "core.Both", new Dictionary<string, Value> { ["a"] = new EntityValue(_a), ["b"] = new EntityValue(_b) });
            Assert.That(r.Reactions.Select(Show), Is.EqualTo(new[] { "when core.first u=" + _a + " Finished" }),
                "both turned true at once; the first by entity order runs and that ends it");
            Set(world, _a, 0);
            Set(world, _a, 9);
            Assert.That(S(world, _a) + S(world, _b), Is.EqualTo("!"));
        }

        [Test]
        public void OnceIsSavedAndLoaded()
        {
            var content = Content("@when(lambda u: u.Out.n >= 5, once=True)\ndef riot(u: Ref[Unit]):\n    u.Out.s = u.Out.s + \"!\"\n" +
                                  "@when(lambda: now() >= 1, once=\"game\")\ndef dawn():\n    pass\n");
            var world = new World(content);
            _a = world.Create("Scout");
            Set(world, _a, 5);
            Set(world, _a, 0);
            world.Advance(1);
            var save = Encoding.UTF8.GetString(world.Save());
            Assert.That(save, Does.Contain("\"when_once\": [\n    {\n      \"reaction\": \"core.dawn\",\n      \"entity\": null\n    },\n    {\n      \"reaction\": \"core.riot\",\n      \"entity\": 1\n    }\n  ]"));
            var again = World.Load(content, world.Save()).World!;
            Assert.That(Set(again, _a, 9).Reactions, Is.Empty);
            Assert.That(again.Advance(1).Reactions, Is.Empty);
        }

        [Test]
        public void TheRoundLimitMustBePositive()
        {
            Assert.That(() => new World(Content(""), maxSettleRounds: 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void AFailingBodyIsRolledBackButCountsAsFired()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5)\ndef bad(u: Ref[Unit]):\n    u.Out.s = \"half\"\n    u.Out.n = 1 // 0\n");
            var r = Set(world, _a, 5);
            var only = r.Reactions.Single();
            Assert.That(only.Outcome, Is.EqualTo(ReactionOutcome.ScriptError));
            Assert.That(only.Error!.Message, Does.Contain("the reaction core.bad for " + _a));
            Assert.That(S(world, _a), Is.EqualTo(""), "the body's effects are rolled back (S5)");
            Assert.That(((IntValue)world.Get(_a, world.Member("Out", "n"))).Value, Is.EqualTo(5), "the command stays");
            Assert.That(Set(world, _a, 6).Reactions, Is.Empty, "it does not fail again on every settle");
        }

        [Test]
        public void AConditionThatIsNotBoolIsAnErrorOncePerSettle()
        {
            var world = Make("@when(lambda u: u.Out.n)\ndef odd(u: Ref[Unit]):\n    pass\n");
            var r = Set(world, _a, 5);
            Assert.That(r.Reactions.Select(x => x.Outcome), Is.EqualTo(new[] { ReactionOutcome.ScriptError, ReactionOutcome.ScriptError }),
                "one for each entity");
            Assert.That(r.Reactions[0].Error!.Message, Does.Contain("the condition of core.odd for " + _a + ": it must return True or False, not int"));
        }

        [Test]
        public void AdvanceCannotBeCalledFromAReaction()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5)\ndef skip(u: Ref[Unit]):\n    advance(1)\n");
            var r = Set(world, _a, 5);
            Assert.That(r.Reactions.Single().Error!.Message, Does.Contain("advance() cannot be called from a @when reaction"));
            Assert.That(world.Tick, Is.EqualTo(0));
        }

        [Test]
        public void AReactionCanAskAndItsFlowWaits()
        {
            var world = Make("@when(lambda u: u.Out.n >= 5)\ndef warn(u: Ref[Unit]):\n    a = ask(u, \"Careful?\", choices=[\"ok\"])\n    u.Out.s = \"seen\"\n");
            var r = Set(world, _a, 5);
            var held = r.Reactions.Single();
            Assert.That(held.Outcome, Is.EqualTo(ReactionOutcome.Suspended));
            Assert.That(held.OpenedRequest, Is.Not.Null);
            world.Answer("host", held.OpenedRequest!.Id, _a, 0);
            Assert.That(S(world, _a), Is.EqualTo("seen"));
        }

        [Test]
        public void TheRecordIsSavedAndLoaded()
        {
            var content = Content(Mark);
            var world = new World(content);
            _a = world.Create("Scout");
            Set(world, _a, 5);
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"reaction\": \"core.mark\""));
            var loaded = World.Load(content, save);
            Assert.That(loaded.Diagnostics.Where(d => d.Severity == Severity.Error), Is.Empty);
            var again = loaded.World!;
            Assert.That(Set(again, _a, 6).Reactions, Is.Empty, "it was true before saving, so it does not fire after loading");
        }

        [Test]
        public void ADestroyedEntityLeavesNoRecord()
        {
            var world = Make(Mark);
            Set(world, _a, 5);
            world.Destroy(_a);
            Set(world, _b, 1);
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Contain("\"when\": []"));
        }

        [TestCase("@when(lambda u, v: True)\ndef f(u: Ref[Unit]):\n    pass\n", "the condition takes 2 parameters, but the reaction 'f' takes 1")]
        [TestCase("@when(lambda: True)\ndef f(u: Ref[Unit]):\n    pass\n", "the condition takes 0 parameters, but the reaction 'f' takes 1")]
        [TestCase("@when(lambda u: True)\ndef f(u):\n    pass\n", "the parameter of a @when reaction needs its entity type")]
        [TestCase("@when(lambda u: True)\ndef f(u: int):\n    pass\n", "it must be Ref[...], not int")]
        [TestCase("@when(lambda u, v: True)\ndef f(u: Ref[Unit], v: Ref[Unit]):\n    pass\n", "a @when reaction takes no parameter or one entity, not 2")]
        [TestCase("@when\ndef f():\n    pass\n", "@when needs the condition it watches")]
        [TestCase("@when(lambda: True, cooldown=0)\ndef f():\n    pass\n", "cooldown= takes a number of ticks of at least 1")]
        [TestCase("@when(lambda: True, cooldown=\"3\")\ndef f():\n    pass\n", "cooldown= takes a number of ticks of at least 1")]
        [TestCase("@when(lambda: True, once=\"forever\")\ndef f():\n    pass\n", "once= must be True or \"game\"")]
        [TestCase("@when(lambda: True, once=True, repeat=True)\ndef f():\n    pass\n", "once= cannot go with repeat=True")]
        [TestCase("@when(lambda: True, once=True, cooldown=3)\ndef f():\n    pass\n", "once= cannot go with cooldown=")]
        [TestCase("@when(lambda: True, oncee=True)\ndef f():\n    pass\n", "did you mean 'once'")]
        [TestCase("@when(lambda: True, repeat=1)\ndef f():\n    pass\n", "repeat= must be True or False")]
        [TestCase("@when(lambda: True, repeats=True)\ndef f():\n    pass\n", "did you mean 'repeat'")]
        [TestCase("@when(lambda: True, stage=\"core.nope\")\ndef f():\n    pass\n", "unknown stage 'core.nope'")]
        [TestCase("def g(u):\n    u.Out.n = 1\n    return True\n@when(g)\ndef f(u: Ref[Unit]):\n    pass\n", "is 'g', which changes the world; it can only read")]
        [TestCase("@when(lambda: True)\ndef f():\n    with preconditions:\n        require(True)\n", "preconditions")]
        public void BadDeclarationsAreLoadErrors(string defs, string expected)
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\")\n" + defs);
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.Contains(expected));
        }

        [Test]
        public void AReactionCannotBeCalledFromLogic()
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\")\n@when(lambda: True)\ndef f():\n    pass\n" +
                                               "@command(free=True, acts_for=None)\ndef Go():\n    f()\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.Contains("'f' is a @when reaction, so logic cannot call it"));
        }
    }
}
