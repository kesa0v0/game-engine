using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>사전 조건의 <c>cost(칸, 금액)</c> (spec S2·S6, spec-gaps G185·G186).</summary>
    public class WorldCostTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Treasury\", gold=field(int, default=5, min=0), grain=field(fixed, default=2.5), " +
            "worth=derived(int, lambda t: t.gold * 2), name=field(str, default=\"\"))\n" +
            "component(\"Stock\", key=\"Good\", amount=field(int, default=0, min=0))\n" +
            "component(\"Log\", n=field(int, default=0))\n" +
            "deftype(\"Good\")\n" +
            "deftype(\"Party\", components=[\"Treasury\", \"Stock\", \"Log\"])\n";

        private const string Defs =
            "Good(\"Wood\")\nParty(\"Red\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Buy(party: Ref[Party], n: int):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.gold, n)\n" +
            "    party.Log.n = party.Treasury.gold\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Twice(party: Ref[Party], a: int, b: int):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.gold, a)\n" +
            "        t = party.Treasury\n" +
            "        cost(t.gold, b)\n" +
            "    party.Log.n = 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Mixed(party: Ref[Party], ok: bool):\n" +
            "    with preconditions:\n" +
            "        require(ok, text(\"msg.not_ok\"))\n" +
            "        cost(party.Treasury.gold, 3)\n" +
            "        cost(party.Treasury.grain, 1)\n" +
            "    party.Log.n = 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Grain(party: Ref[Party], n: fixed):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.grain, n)\n" +
            "    party.Log.n = 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Wood(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Stock[\"Wood\"].amount, 2)\n" +
            "    party.Log.n = 1\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Fail(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.gold, 2)\n" +
            "    party.Log.n = 1\n" +
            "    party.Log.n = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Derived(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.worth, 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Text(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.name, 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Typo(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            // 리스트로 감싸 타입을 모르게 한다: 실행 중 안전망을 본다 (아니면 G228의 로드 오류)
            "        cost([party.Treasury][0].gld, 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def OnEntity(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury, 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Half(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.gold, 0.5)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Word(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Treasury.gold, \"one\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Extra(party: Ref[Party], n: int):\n" +
            "    ok = pay(party.Treasury.gold, n)\n" +
            "    if ok:\n" +
            "        party.Log.n = 1\n" +
            "    else:\n" +
            "        party.Log.n = 2\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Plain(party: Ref[Party], n: int):\n" +
            "    pay(party.Treasury.gold, n)\n" +
            "    pay(party.Treasury.grain, 1)\n" +
            "def spend(p, n):\n" +
            "    ok = pay(p.Treasury.gold, n)\n" +
            "    return ok\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Helper(party: Ref[Party], n: int):\n" +
            "    first = spend(party, n)\n" +
            "    second = spend(party, n)\n" +
            "    party.Log.n = (10 if first else 0) + (1 if second else 0)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def PayThenFail(party: Ref[Party]):\n" +
            "    pay(party.Treasury.gold, 2)\n" +
            "    party.Log.n = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def PayValue(party: Ref[Party]):\n" +
            "    f = pay\n" +
            "    f(party.Treasury.gold, 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def PayNumber(party: Ref[Party]):\n" +
            "    f = pay\n" +
            "    f(3, 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def PayDerived(party: Ref[Party]):\n" +
            "    pay(party.Treasury.worth, 1)\n" +
            "@when(lambda p: p.Treasury.gold < 3)\n" +
            "def poor(p: Ref[Party]):\n" +
            "    p.Treasury.name = \"poor\"\n";

        private World _world = null!;
        private EntityId _red;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("Red");
        }

        private SubmitResult Submit(string command, params (string, Value)[] extra)
        {
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(_red) };
            foreach (var (name, value) in extra) args[name] = value;
            return _world.Submit("host", "banking." + command, args);
        }

        private PreconditionReport Check(string command, params (string, Value)[] extra)
        {
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(_red) };
            foreach (var (name, value) in extra) args[name] = value;
            return _world.CheckPreconditions("banking." + command, args);
        }

        private long Int(string component, string member) => ((IntValue)_world.Get(_red, _world.Member(component, member))).Value;

        private static string Row(RequireOutcome r) =>
            r.Kind + " " + r.Status + " " + r.Slot + " " + Show(r.Have) + " " + Show(r.Need);

        private static string Show(Value? v) =>
            v == null ? "-" : v is IntValue i ? i.Value.ToString() : ((FixedValue)v).Value.ToString();

        private static (string, Value) N(string name, long n) => (name, new IntValue(n));

        [Test]
        public void TheBodyStartsAfterTheCostIsTakenOut()
        {
            var r = Submit("Buy", N("n", 2));
            Assert.That(r.Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(3));
            Assert.That(Int("Log", "n"), Is.EqualTo(3), "the first body line already sees the paid amount");
            Assert.That(r.Preconditions!.Requires.Select(Row), Is.EqualTo(new[] { "Cost Passed #1.Treasury.gold 5 2" }));
        }

        [Test]
        public void TheWholeAmountMayBeSpent()
        {
            Assert.That(Submit("Buy", N("n", 5)).Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(0));
        }

        [Test]
        public void ANotEnoughCostIsAReportRowAndRunsNoBody()
        {
            var r = Submit("Buy", N("n", 6));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            Assert.That(r.Preconditions!.Failures.Select(Row), Is.EqualTo(new[] { "Cost Failed #1.Treasury.gold 5 6" }));
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(5));
            Assert.That(Int("Log", "n"), Is.EqualTo(0));
        }

        [Test]
        public void CheckingThePreconditionsTakesNothingOut()
        {
            Assert.That(Check("Buy", N("n", 2)).Passed, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(5));
        }

        [Test]
        public void CostsOnTheSameFieldAreAddedUpEvenThroughAnotherName()
        {
            // 3 + 3 = 6 > 5: 둘째 cost가 합으로 실패한다 (t는 같은 Treasury, G185)
            var report = Check("Twice", N("a", 3), N("b", 3));
            Assert.That(report.Requires.Select(Row), Is.EqualTo(new[]
            {
                "Cost Passed #1.Treasury.gold 5 3",
                "Cost Failed #1.Treasury.gold 5 6",
            }));

            Assert.That(Submit("Twice", N("a", 2), N("b", 3)).Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(0));
        }

        [Test]
        public void CostsOfDifferentEntitiesAreNotAddedUp()
        {
            var blue = _world.Create("Red");
            var gold = _world.Member("Treasury", "gold");
            _world.Set(blue, gold, new IntValue(1));
            Assert.That(Submit("Buy", N("n", 4)).Succeeded, Is.True);
            var r = _world.Submit("host", "banking.Buy",
                new Dictionary<string, Value> { ["party"] = new EntityValue(blue), ["n"] = new IntValue(1) });
            Assert.That(r.Succeeded, Is.True);
            Assert.That(((IntValue)_world.Get(blue, gold)).Value, Is.EqualTo(0));
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(1));
        }

        [Test]
        public void AFixedFieldTakesAnIntAmount()
        {
            var r = Submit("Mixed", ("ok", BoolValue.True));
            Assert.That(r.Succeeded, Is.True);
            Assert.That(((FixedValue)_world.Get(_red, _world.Member("Treasury", "grain"))).Value.ToString(), Is.EqualTo("1.5"));
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(2));
        }

        [Test]
        public void AFixedAmountIsComparedExactly()
        {
            var report = Check("Grain", ("n", new FixedValue(Fixed.FromRaw(2_500_001))));
            Assert.That(report.Failures.Select(Row), Is.EqualTo(new[] { "Cost Failed #1.Treasury.grain 2.5 2.500001" }));
        }

        [Test]
        public void ACostAfterAFailedRequireIsStillReported()
        {
            var report = Check("Mixed", ("ok", BoolValue.False));
            Assert.That(report.Requires.Select(r => r.Kind + " " + r.Status), Is.EqualTo(new[]
            {
                "Require Failed", "Cost Passed", "Cost Passed",
            }));
        }

        [Test]
        public void ACostCanTakeFromARowOfAKeyedComponent()
        {
            var amount = _world.Member("Stock", "amount");
            Assert.That(Check("Wood").Failures.Select(Row), Is.EqualTo(new[] { "Cost Failed #1.Stock['Wood'].amount 0 2" }));
            _world.SetRow(_red, amount, "Wood", new IntValue(3));
            Assert.That(Submit("Wood").Succeeded, Is.True);
            Assert.That(((IntValue)_world.GetRow(_red, amount, "Wood")).Value, Is.EqualTo(1));
        }

        [Test]
        public void AFailingBodyGivesThePaidAmountBack()
        {
            var r = Submit("Fail");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(5));
            Assert.That(Int("Log", "n"), Is.EqualTo(0));
        }

        [Test]
        public void PayingWakesAWhenReactionOnThatField()
        {
            // 5 → 2: gold < 3이 되어 poor가 돈다 (cost의 빼기도 Touch를 거치는 보통 쓰기다)
            Assert.That(Submit("Buy", N("n", 3)).Succeeded, Is.True);
            Assert.That(((StrValue)_world.Get(_red, _world.Member("Treasury", "name"))).Value, Is.EqualTo("poor"));
        }

        [TestCase("Derived", Codes.RunType, "cost() and pay() need an int or fixed field, but 'Treasury.worth' is a derived")]
        [TestCase("Text", Codes.RunType, "cost() and pay() need an int or fixed field, but 'Treasury.name' is a str field")]
        [TestCase("Typo", Codes.RunUnknownName, "'Treasury' has no member 'gld'")]
        [TestCase("OnEntity", Codes.RunType, "cost() takes a member of a component, not 'Treasury' of entity")]
        [TestCase("Half", Codes.RunType, "the amount of cost() is fixed, but the field is an int")]
        [TestCase("Word", Codes.RunType, "the amount of cost() must be int or fixed, not str")]
        public void AWrongFieldOrAmountIsARuntimeError(string command, string code, string message)
        {
            var e = Assert.Throws<WorldException>(() => Check(command))!;
            Assert.That(e.Code, Is.EqualTo(code));
            Assert.That(e.Message, Does.Contain(message));
        }

        [Test]
        public void ANegativeAmountIsAnError()
        {
            var e = Assert.Throws<WorldException>(() => Check("Buy", N("n", -1)))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunRange));
            Assert.That(e.Message, Does.Contain("the amount of cost() is -1, which is negative"));
        }

        // ---- 본문 중간의 pay (G187) ----

        [TestCase(2, 3, 1)]
        [TestCase(5, 0, 1)]
        [TestCase(6, 5, 2)]
        public void PayTakesTheAmountOutOnlyWhenThereIsEnough(long n, long gold, long branch)
        {
            Assert.That(Submit("Extra", N("n", n)).Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(gold));
            Assert.That(Int("Log", "n"), Is.EqualTo(branch));
        }

        [Test]
        public void PayOnItsOwnLineDropsTheAnswer()
        {
            Assert.That(Submit("Plain", N("n", 9)).Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(5));
            Assert.That(((FixedValue)_world.Get(_red, _world.Member("Treasury", "grain"))).Value.ToString(), Is.EqualTo("1.5"));
        }

        [Test]
        public void AFunctionThatPaysIsAWriterCalledOnItsOwnLine()
        {
            // 5 - 3 = 2, 둘째는 모자라 그대로
            Assert.That(Submit("Helper", N("n", 3)).Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(2));
            Assert.That(Int("Log", "n"), Is.EqualTo(10));
        }

        [Test]
        public void AFailingBodyGivesWhatPayTookBack()
        {
            Assert.That(Submit("PayThenFail").Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(5));
        }

        [Test]
        public void PayWakesAWhenReactionOnThatField()
        {
            Assert.That(Submit("Extra", N("n", 3)).Succeeded, Is.True);
            Assert.That(((StrValue)_world.Get(_red, _world.Member("Treasury", "name"))).Value, Is.EqualTo("poor"));
        }

        [Test]
        public void PayReachedThroughAFunctionValueStillTakesTheField()
        {
            Assert.That(Submit("PayValue").Succeeded, Is.True);
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(4));
        }

        [TestCase("PayNumber", Codes.RunScript, "pay() takes a field and an amount, such as pay(party.Treasury.gold, n)")]
        [TestCase("PayDerived", Codes.RunType, "cost() and pay() need an int or fixed field, but 'Treasury.worth' is a derived")]
        public void AWrongPayIsARuntimeError(string command, string code, string message)
        {
            var r = Submit(command);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(code));
            Assert.That(r.Error.Message, Does.Contain(message));
        }

        [Test]
        public void ANegativePayIsAnError()
        {
            var r = Submit("Extra", N("n", -2));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunRange));
            Assert.That(r.Error.Message, Does.Contain("the amount of pay() is -2, which is negative"));
            Assert.That(Int("Treasury", "gold"), Is.EqualTo(5));
        }
    }
}
