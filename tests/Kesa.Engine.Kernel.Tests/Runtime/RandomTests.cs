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
    /// <summary>난수 (spec S2 난수, C1·C6·C10, spec-gaps G178~G182).</summary>
    public class RandomTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Roll\", a=field(int, default=0), b=field(int, default=0), c=field(int, default=0), " +
            "d=field(int, default=0), e=field(int, default=0), f=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Roll\"])\n";

        private const string Commands =
            "Unit(\"Scout\")\n" +
            "@command(free=True, acts_for=None)\ndef Roll(u: Ref[Unit], ok: bool = True, boom: bool = False):\n" +
            "    with preconditions:\n        require(ok, text(\"no\"))\n" +
            "    u.Roll.a = random.randint(1, 1000000)\n    u.Roll.b = random.randint(1, 1000000)\n" +
            "    if boom:\n        x = 1 // 0\n" +
            "@command(free=True, acts_for=None)\ndef Other(u: Ref[Unit]):\n    u.Roll.c = random.randint(1, 1000000)\n" +
            "@command(free=True, acts_for=None)\ndef Duel(u: Ref[Unit]):\n" +
            "    u.Roll.a = random.randint(1, 1000000)\n    r = ask(u, \"Q\", choices=[\"x\", \"y\"])\n" +
            "    u.Roll.b = random.randint(1, 1000000)\n    u.Roll.c = roll()\n" +
            "def roll():\n    return random.randint(1, 1000000)\n";

        private EntityId _u;

        private ContentLoadResult Content(string extra = "", string schema = Schema)
        {
            Mod("core", schema: schema, defs: Commands + extra);
            return LoadClean();
        }

        private World Make(ContentLoadResult content, long seed = 0)
        {
            var world = new World(content, seed: seed);
            _u = world.Create("Scout");
            return world;
        }

        private SubmitResult Run(World w, string command, params (string, Value)[] more)
        {
            var args = new Dictionary<string, Value> { ["u"] = new EntityValue(_u) };
            foreach (var (k, v) in more) args[k] = v;
            return w.Submit("host", "core." + command, args);
        }

        private long Get(World w, string member) => ((IntValue)w.Get(_u, w.Member("Roll", member))).Value;

        private long[] Rolled(World w) => new[] { Get(w, "a"), Get(w, "b"), Get(w, "c") };

        // ---- 골든 값 (C6): 시드·키·n이 같으면 플랫폼·프로세스와 무관하게 같은 값 ----

        [Test]
        public void TheValuesAreFixedForASeed()
        {
            var content = Content();
            var world = Make(content);
            Run(world, "Roll");
            var first = new[] { Get(world, "a"), Get(world, "b") };
            Run(world, "Roll");
            var second = new[] { Get(world, "a"), Get(world, "b") };
            var other = Make(content, seed: 42);
            Run(other, "Roll");
            // ChaCha20 키 = SHA-256(시드, 실행 횟수, "core.Roll"), nonce = n (G181). 바뀌면 예전 세이브가 다른 난수를 낸다
            Assert.That(first, Is.EqualTo(new long[] { 9871, 268220 }));
            Assert.That(second, Is.EqualTo(new long[] { 986051, 193027 })); // 같은 커맨드를 다시 내면 다른 값 (U9의 버그)
            Assert.That(new[] { Get(other, "a"), Get(other, "b") }, Is.EqualTo(new long[] { 156781, 215899 }));
        }

        [Test]
        public void TheSameSeedAndInputsGiveTheSameValues()
        {
            var content = Content();
            var a = Make(content, seed: 7);
            var b = Make(content, seed: 7);
            foreach (var w in new[] { a, b })
            {
                Run(w, "Roll");
                Run(w, "Other");
            }
            Assert.That(Rolled(a), Is.EqualTo(Rolled(b)));
        }

        // ---- 정의마다 난수열 (G182) ----

        [Test]
        public void AnotherCommandDoesNotShiftTheValues()
        {
            var content = Content();
            var plain = Make(content);
            Run(plain, "Roll");
            var mixed = Make(content);
            Run(mixed, "Other");
            Run(mixed, "Other");
            Run(mixed, "Roll");
            Assert.That(Get(mixed, "a"), Is.EqualTo(Get(plain, "a")));
            Assert.That(Get(mixed, "b"), Is.EqualTo(Get(plain, "b")));
        }

        // ---- 실패한 입력은 난수를 소비하지 않는다 (C10) ----

        [Test]
        public void AFailedInputDoesNotUseUpTheRandomNumbers()
        {
            var content = Content();
            var plain = Make(content);
            Run(plain, "Roll");
            var expected = new[] { Get(plain, "a"), Get(plain, "b") };

            var world = Make(content);
            Assert.That(Run(world, "Roll", ("ok", BoolValue.False)).Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            var boom = Run(world, "Roll", ("boom", BoolValue.True));
            Assert.That(boom.Outcome, Is.EqualTo(SubmitOutcome.ScriptError)); // 난수를 뽑은 뒤 오류: 카운터와 n도 되감긴다
            Assert.That(Get(world, "a"), Is.EqualTo(0));
            Run(world, "Roll");
            Assert.That(new[] { Get(world, "a"), Get(world, "b") }, Is.EqualTo(expected));
        }

        // ---- 멈췄다 이어도, 저장했다 불러와도 같은 난수열 (spec S2, C1) ----

        [Test]
        public void AFlowKeepsItsStreamAcrossAQuestionAndASave()
        {
            var content = Content();
            var plain = Make(content);
            var q = Run(plain, "Duel").OpenedRequest!;
            Run(plain, "Other"); // 그 사이 다른 커맨드
            plain.Answer("host", q.Id, _u, 0);
            var expected = Rolled(plain);

            var world = Make(content);
            q = Run(world, "Duel").OpenedRequest!;
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"rand\": {\n        \"count\": 1,\n        \"calls\": 1\n      }"));
            var loaded = World.Load(content, save).World!;
            Run(loaded, "Other");
            loaded.Answer("host", q.Id, _u, 0);
            Assert.That(Rolled(loaded), Is.EqualTo(expected));
            Assert.That(expected.Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void AFailedAnswerLeavesTheStreamWhereItWas()
        {
            // 이어 간 구간이 실패하면 Flow는 답하기 전으로 돌아간다(C9): 다시 답하면 같은 값이어야 한다
            var content = Content("@command(free=True, acts_for=None)\ndef Risky(u: Ref[Unit]):\n" +
                                  "    r = ask(u, \"Q\", choices=[\"bad\", \"good\"])\n    u.Roll.a = random.randint(1, 1000000)\n" +
                                  "    if r.index == 0:\n        x = 1 // 0\n");
            var plain = Make(content);
            var q = Run(plain, "Risky").OpenedRequest!;
            plain.Answer("host", q.Id, _u, 1);
            var world = Make(content);
            q = Run(world, "Risky").OpenedRequest!;
            world.Answer("host", q.Id, _u, 0);
            Assert.That(world.OpenRequests.Single().Id, Is.EqualTo(q.Id));
            world.Answer("host", q.Id, _u, 1);
            Assert.That(Get(world, "a"), Is.EqualTo(Get(plain, "a")));
        }

        [Test]
        public void HandlersAndReactionsHaveTheirOwnStreams()
        {
            var content = Content("@when(lambda u: u.Roll.a > 0)\ndef after(u: Ref[Unit]):\n    u.Roll.f = random.randint(1, 1000000)\n");
            var world = Make(content);
            Run(world, "Roll");
            Assert.That(Get(world, "f"), Is.Not.EqualTo(0));
            var save = Encoding.UTF8.GetString(world.Save());
            Assert.That(save, Does.Contain("\"rand\": [\n    {\n      \"id\": \"core.Roll\",\n      \"count\": 1\n    },\n" +
                                           "    {\n      \"id\": \"core.after\",\n      \"count\": 1\n    }\n  ]"));
            var again = Make(content);
            Run(again, "Roll");
            Assert.That(Get(again, "f"), Is.EqualTo(Get(world, "f")));
        }

        [Test]
        public void TheSeedIsSavedAndLoaded()
        {
            var content = Content();
            var world = Make(content, seed: -12345);
            var loaded = World.Load(content, world.Save()).World!;
            Assert.That(loaded.Seed, Is.EqualTo(-12345));
            Run(world, "Roll");
            Run(loaded, "Roll");
            Assert.That(Rolled(loaded), Is.EqualTo(Rolled(world)));
        }

        // ---- 파이썬과 같은 API (G179) ----

        private const string Api =
            "@command(free=True, acts_for=None)\ndef Api(u: Ref[Unit]):\n" +
            "    u.Roll.a = random.randint(1, 6)\n" +
            "    u.Roll.b = random.randrange(0, 10, 3)\n" +
            "    u.Roll.c = random.choice([10, 20, 30])\n" +
            "    u.Roll.d = random.choices([7, 8], weights=[0, 1], k=3)[2]\n" +
            "    s = 0\n    q = 0\n" +
            "    for x in random.sample(range(5), 5):\n        s += x\n        q += x * x\n" +
            "    u.Roll.e = s * 100 + q\n" +
            "    u.Roll.f = floor(random.random() * 1000)\n";

        [Test]
        public void TheFunctionsBehaveLikePythons()
        {
            var world = Make(Content(Api));
            var seen = new Dictionary<string, SortedSet<long>>();
            foreach (var m in new[] { "a", "b", "c", "d", "e", "f" }) seen[m] = new SortedSet<long>();
            for (int i = 0; i < 300; i++)
            {
                Assert.That(Run(world, "Api").Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
                foreach (var pair in seen) pair.Value.Add(Get(world, pair.Key));
            }
            Assert.That(seen["a"], Is.EqualTo(new long[] { 1, 2, 3, 4, 5, 6 })); // 양 끝 포함
            Assert.That(seen["b"], Is.EqualTo(new long[] { 0, 3, 6, 9 })); // 끝 제외, 걸음
            Assert.That(seen["c"], Is.EqualTo(new long[] { 10, 20, 30 }));
            Assert.That(seen["d"], Is.EqualTo(new long[] { 8 })); // 가중치 0은 뽑히지 않는다
            Assert.That(seen["e"], Is.EqualTo(new long[] { 10 * 100 + 30 })); // 0..4를 한 번씩
            Assert.That(seen["f"].Min, Is.GreaterThanOrEqualTo(0));
            Assert.That(seen["f"].Max, Is.LessThan(1000));
            Assert.That(seen["f"].Count, Is.GreaterThan(200));
        }

        [Test]
        public void SampleGivesEveryOrder()
        {
            var world = Make(Content("@command(free=True, acts_for=None)\ndef Shuffle(u: Ref[Unit]):\n" +
                                     "    xs = random.sample([1, 2, 3], 3)\n    u.Roll.a = xs[0] * 100 + xs[1] * 10 + xs[2]\n"));
            var orders = new SortedSet<long>();
            for (int i = 0; i < 200; i++)
            {
                Run(world, "Shuffle");
                orders.Add(Get(world, "a"));
            }
            Assert.That(orders, Is.EqualTo(new long[] { 123, 132, 213, 231, 312, 321 }));
        }

        [TestCase("random.randint(6, 1)", "random.randint(6, 1): the range is empty")]
        [TestCase("random.randrange(5, 5)", "random.randrange(5, 5, 1): the range is empty")]
        [TestCase("random.randrange(0, 5, 0)", "random.randrange() step must not be zero")]
        [TestCase("random.choice([])", "random.choice() cannot choose from an empty sequence")]
        [TestCase("random.choice({\"a\": 1})", "random.choice() needs a list or a range, not dict")]
        [TestCase("random.choices([1, 2], weights=[1])[0]", "random.choices() got 1 weights for 2 items")]
        [TestCase("random.choices([1, 2], weights=[0, 0])[0]", "random.choices() the weights add up to zero")]
        [TestCase("random.choices([1, 2], cum_weights=[1, 2])[0]", "random.choices() does not take the keyword argument 'cum_weights'")]
        [TestCase("random.sample([1, 2], 3)[0]", "random.sample() k must be between 0 and the number of items (2), got 3")]
        [TestCase("random.randint(1, 2.5)", "random.randint() needs an int for b, not fixed")]
        public void BadArgumentsAreScriptErrors(string call, string message)
        {
            var world = Make(Content("@command(free=True, acts_for=None)\ndef Bad(u: Ref[Unit]):\n    u.Roll.a = " + call + "\n"));
            var r = Run(world, "Bad");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
        }

        [TestCase("random.shuffle", "random.sample(seq, len(seq)) gives a shuffled copy")]
        [TestCase("random.seed", "the world seed is set by the host")]
        [TestCase("random.randit", "randint")]
        public void MissingFunctionsExplainThemselves(string name, string hint)
        {
            var world = Make(Content("@command(free=True, acts_for=None)\ndef Bad(u: Ref[Unit]):\n    f = " + name + "\n"));
            var r = Run(world, "Bad");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Hint, Does.Contain(hint));
        }

        // ---- 부를 수 있는 곳 (G178) ----

        [TestCase(
            "component(\"Roll\", a=field(int, default=0), luck=derived(int, lambda r: random.randint(1, 6)))\n" +
            "deftype(\"Unit\", components=[\"Roll\"])\n", "", "derived 'Roll.luck' uses")]
        [TestCase(Schema, "def d6():\n    return random.randint(1, 6)\ndef luck(u):\n    return d6() + 1\n" +
                          "@when(lambda u: luck(u) > 3)\ndef lucky(u: Ref[Unit]):\n    pass\n",
            "the condition of @when on 'lucky' is '<lambda>', which draws random numbers")]
        [TestCase(Schema, "@command(free=True, acts_for=None)\ndef Try(u: Ref[Unit]):\n" +
                          "    with preconditions:\n        require(random.randint(1, 6) > 3, text(\"no\"))\n    u.Roll.a = 1\n",
            "random numbers cannot be drawn in 'with preconditions:'")]
        [TestCase(Schema, "def d6():\n    return random.randint(1, 6)\n@command(free=True, acts_for=None)\ndef Try(u: Ref[Unit]):\n" +
                          "    with preconditions:\n        x = d6()\n    u.Roll.a = x\n",
            "'d6' draws random numbers, so it cannot be used in 'with preconditions:'")]
        [TestCase(Schema, "@submit_policy\ndef may_act(issuer, target):\n    return random.randint(0, 1) == 1\n",
            "the submit policy 'may_act' draws random numbers")]
        [TestCase(
            "component(\"Roll\", a=field(int, default=0))\ndeftype(\"Unit\", ok=const(Cond[\"Unit\"]), components=[\"Roll\"])\n",
            "Unit(\"Lucky\", ok=lambda u: random.randint(0, 1) == 1)\n", "draws random numbers")]
        public void RandomNumbersInAPlaceThatMustGiveTheSameAnswerAreALoadError(string schema, string defs, string message)
        {
            Mod("core", schema: schema, defs: defs);
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code + " " + d.Message), Has.Some.Contains(message));
        }

        [Test]
        public void RandomNumbersAreFineInExpressionsAndHelpersOfABody()
        {
            // 쓰기와 달리 식 안에서도 된다 (G178)
            var world = Make(Content("@command(free=True, acts_for=None)\ndef Hit(u: Ref[Unit]):\n" +
                                     "    if random.randint(1, 6) >= 1:\n        u.Roll.a = 5 + roll() % 3\n"));
            Assert.That(Run(world, "Hit").Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(Get(world, "a"), Is.InRange(5, 7));
        }

        [Test]
        public void TheRuntimeNetCatchesAPathTheNamesDoNotShow()
        {
            // derived가 Def의 함수 값을 부르면 이름으로는 난수 함수인지 알 수 없다: 실행 중 안전망이 막는다
            const string schema =
                "component(\"Roll\", a=field(int, default=0), luck=derived(int, lambda r: r.entity.Unit.act(r.entity)))\n" +
                "deftype(\"Unit\", act=const(Action[\"Unit\"]), components=[\"Roll\"])\n";
            Mod("core", schema: schema, defs: "Unit(\"Scout\", act=lambda u: random.randint(1, 6))\n");
            var world = new World(LoadClean());
            var u = world.Create("Scout");
            var e = Assert.Throws<WorldException>(() => world.Get(u, world.Member("Roll", "luck")))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunNotWritable));
            Assert.That(e.Message, Does.Contain("random.randint() draws a random number, so it cannot be called here"));
        }
    }
}
