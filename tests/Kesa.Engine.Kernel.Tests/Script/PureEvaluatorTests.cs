using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary>순수 함수 실행기 (spec-gaps G95~G103).</summary>
    public class PureEvaluatorTests
    {
        private sealed class Bag : Value, IRuntimeAttributes
        {
            private readonly Dictionary<string, Value> _values;

            public Bag(params (string Name, Value Value)[] values) =>
                _values = values.ToDictionary(v => v.Name, v => v.Value);

            public override string KindName => "bag";

            public Value GetAttribute(string name)
            {
                if (_values.TryGetValue(name, out var v)) return v;
                throw new ScriptRuntimeException(Codes.RunScript, "no member '" + name + "'");
            }
        }

        private static (PureEvaluator Evaluator, Func<string, Value> Function) Build(
            string source, int budget = PureEvaluator.DefaultStepBudget,
            Action<SortedDictionary<string, Value>, Func<PureEvaluator>>? extend = null)
        {
            var bag = new DiagnosticBag();
            var module = Parser.Parse(source, "t.py", bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            var scope = new ModScope();
            foreach (var stmt in module.Body)
                if (stmt is FunctionDef d) scope.Bind(d.Name, new FunctionValue(d, "t"), d.Span);

            var builtins = PureBuiltins.Create();
            PureEvaluator? evaluator = null;
            extend?.Invoke(builtins, () => evaluator!);
            evaluator = new PureEvaluator(new ScopeGlobals(new Dictionary<string, ModScope> { ["t"] = scope }), builtins, budget);
            return (evaluator, name =>
            {
                Assert.That(scope.TryGet(name, out var f, out _), Is.True);
                return f;
            });
        }

        private static Value Call(string source, string function, params Value[] args)
        {
            var (evaluator, find) = Build(source);
            return evaluator.Run(find(function), args);
        }

        private static string Show(Value v)
        {
            switch (v)
            {
                case IntValue i: return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case FixedValue f: return f.Value.ToString();
                case BoolValue b: return b.Value ? "True" : "False";
                case StrValue s: return s.Value;
                case NoneValue _: return "None";
            }
            return v.KindName;
        }

        private static string Eval(string expression) => Show(Call("def f():\n    return " + expression + "\n", "f"));

        private static ScriptRuntimeException Fails(string source, string function, params Value[] args) =>
            Assert.Throws<ScriptRuntimeException>(() => Call(source, function, args))!;

        private static ScriptRuntimeException EvalFails(string expression) =>
            Fails("def f():\n    return " + expression + "\n", "f");

        // ---- 산술 (G95) ----

        [TestCase("1 + 2", "3")]
        [TestCase("7 // 2", "3")]
        [TestCase("-7 // 2", "-4")]
        [TestCase("7 % -3", "-2")]
        [TestCase("-7 % 3", "2")]
        [TestCase("10 / 100", "0.1")]
        [TestCase("1 / 3", "0.333333")]
        [TestCase("2 / 3", "0.666667")]
        [TestCase("-2 / 3", "-0.666667")]
        [TestCase("6 / 3", "2.0")]
        [TestCase("1.5 * 2", "3.0")]
        [TestCase("3 * 0.5", "1.5")]
        [TestCase("1 + 0.5", "1.5")]
        [TestCase("2.5 - 1", "1.5")]
        [TestCase("7.5 // 2", "3.0")]
        [TestCase("-7.5 // 2", "-4.0")]
        [TestCase("7.5 % 2", "1.5")]
        [TestCase("-7.5 % 2", "0.5")]
        [TestCase("-(3)", "-3")]
        [TestCase("+2.5", "2.5")]
        public void Arithmetic(string expression, string expected) => Assert.That(Eval(expression), Is.EqualTo(expected));

        [TestCase("1 / 0", Codes.RunArithmetic)]
        [TestCase("1.5 / 0.0", Codes.RunArithmetic)]
        [TestCase("1 // 0", Codes.RunArithmetic)]
        [TestCase("1 % 0", Codes.RunArithmetic)]
        [TestCase("9223372036854775807 + 1", Codes.RunArithmetic)]
        [TestCase("9223372036854775807 * 2", Codes.RunArithmetic)]
        [TestCase("-9223372036854775807 - 2", Codes.RunArithmetic)]
        [TestCase("9223372036854.775807 * 1000000.0", Codes.RunArithmetic)]
        [TestCase("9223372036854775807 * 0.5", Codes.RunArithmetic)]
        [TestCase("1 + 'a'", Codes.RunScript)]
        [TestCase("True + 1", Codes.RunScript)]
        [TestCase("None * 2", Codes.RunScript)]
        [TestCase("'a' * 2", Codes.RunScript)]
        [TestCase("-'a'", Codes.RunScript)]
        public void ArithmeticErrors(string expression, string code) => Assert.That(EvalFails(expression).Code, Is.EqualTo(code));

        [TestCase("1 == 1.0", "True")]
        [TestCase("1 != 2", "True")]
        [TestCase("2 > 1.5", "True")]
        [TestCase("1.5 <= 1", "False")]
        [TestCase("'a' < 'b'", "True")]
        [TestCase("'a' == 'a'", "True")]
        [TestCase("[1, 2] == [1, 2.0]", "True")]
        [TestCase("{'a': 1, 'b': 2} == {'b': 2, 'a': 1}", "True")]
        [TestCase("None == None", "True")]
        [TestCase("1 == 'a'", "False")]
        [TestCase("2 in [1, 2]", "True")]
        [TestCase("'x' not in {'x': 1}", "False")]
        [TestCase("'ab' in 'cabd'", "True")]
        [TestCase("4 in range(0, 10, 2)", "True")]
        [TestCase("5 in range(0, 10, 2)", "False")]
        [TestCase("None is None", "True")]
        [TestCase("1 is not None", "True")]
        [TestCase("'a' + 'b'", "ab")]
        [TestCase("len([1, 2] + [3])", "3")]
        [TestCase("1 if 2 > 1 else 0", "1")]
        [TestCase("not (1 == 1)", "False")]
        [TestCase("True and False", "False")]
        [TestCase("False or True", "True")]
        public void ComparisonsAndLogic(string expression, string expected) =>
            Assert.That(Eval(expression), Is.EqualTo(expected));

        // 참/거짓 판정은 파이썬과 같다 (spec-gaps G104)
        [TestCase("None", "no")]
        [TestCase("False", "no")]
        [TestCase("0", "no")]
        [TestCase("0.0", "no")]
        [TestCase("''", "no")]
        [TestCase("[]", "no")]
        [TestCase("{}", "no")]
        [TestCase("range(0)", "no")]
        [TestCase("1", "yes")]
        [TestCase("-0.5", "yes")]
        [TestCase("'a'", "yes")]
        [TestCase("[0]", "yes")]
        [TestCase("{'k': 0}", "yes")]
        [TestCase("range(2)", "yes")]
        [TestCase("True", "yes")]
        public void TruthinessFollowsPython(string value, string expected) =>
            Assert.That(Show(Call("def f():\n    if " + value + ":\n        return 'yes'\n    return 'no'\n", "f")), Is.EqualTo(expected));

        [TestCase("1 and 2", "2")]
        [TestCase("0 and 2", "0")]
        [TestCase("0 or 5", "5")]
        [TestCase("3 or 5", "3")]
        [TestCase("None or 'x'", "x")]
        [TestCase("[] or None", "None")]
        [TestCase("not 0", "True")]
        [TestCase("not [1]", "False")]
        [TestCase("1 if [] else 2", "2")]
        public void AndOrReturnOperandsLikePython(string expression, string expected) =>
            Assert.That(Eval(expression), Is.EqualTo(expected));

        [Test]
        public void HostValuesAreAlwaysTruthy()
        {
            Assert.That(Show(Call("def f(h):\n    return 'yes' if h else 'no'\n", "f", new Bag())), Is.EqualTo("yes"));
        }

        [Test]
        public void AndOrSkipTheRightSideWhenTheLeftDecides()
        {
            Assert.That(Eval("False and 1 / 0 > 0"), Is.EqualTo("False"));
            Assert.That(Eval("True or 1 / 0 > 0"), Is.EqualTo("True"));
            Assert.That(Eval("0 and 1 / 0"), Is.EqualTo("0"));
        }

        // ---- 내장 함수 (G97) ----

        [TestCase("floor(2.5)", "2")]
        [TestCase("floor(-2.5)", "-3")]
        [TestCase("floor(3)", "3")]
        [TestCase("ceil(2.1)", "3")]
        [TestCase("ceil(-2.1)", "-2")]
        [TestCase("round(2.5)", "3")]
        [TestCase("round(-2.5)", "-3")]
        [TestCase("round(0.49)", "0")]
        [TestCase("round(1.5)", "2")]
        [TestCase("abs(-3)", "3")]
        [TestCase("abs(-2.5)", "2.5")]
        [TestCase("min(3, 1, 2)", "1")]
        [TestCase("max(1, 2.5)", "2.5")]
        [TestCase("max(3, 2.5)", "3.0")]
        [TestCase("min([4, 2, 9])", "2")]
        [TestCase("clamp(15, 1, 9)", "9")]
        [TestCase("clamp(0, 1, 9)", "1")]
        [TestCase("clamp(5, 1, 9)", "5")]
        [TestCase("clamp(5, 1, 9.0)", "5.0")]
        [TestCase("len([1, 2, 3])", "3")]
        [TestCase("len({'a': 1})", "1")]
        [TestCase("len(range(5))", "5")]
        [TestCase("len(range(0, 10, 3))", "4")]
        [TestCase("len(range(10, 0, -3))", "4")]
        [TestCase("len(range(5, 0))", "0")]
        [TestCase("sum([1, 2, 3])", "6")]
        [TestCase("sum([1, 0.5])", "1.5")]
        [TestCase("sum(range(5))", "10")]
        [TestCase("sum([])", "0")]
        public void Builtins(string expression, string expected) => Assert.That(Eval(expression), Is.EqualTo(expected));

        [TestCase("clamp(1, 9, 1)")]
        [TestCase("floor('a')")]
        [TestCase("min([])")]
        [TestCase("max()")]
        [TestCase("len(3)")]
        [TestCase("range(0.5)")]
        [TestCase("range(1, 5, 0)")]
        [TestCase("floor(1, 2)")]
        [TestCase("sum(range(1000000))")]
        public void BuiltinMisuseIsAnError(string expression) => Assert.That(EvalFails(expression).Code, Is.AnyOf(Codes.RunScript, Codes.RunArithmetic));

        // ---- 문장 ----

        [Test]
        public void ForLoopWithBreakContinueAndRange()
        {
            const string source =
                "def f():\n    t = 0\n    for i in range(10):\n        if i == 3:\n            continue\n" +
                "        if i == 6:\n            break\n        t += i\n    return t\n";
            Assert.That(Show(Call(source, "f")), Is.EqualTo("12"));
        }

        [Test]
        public void ForLoopOverDictKeysAndNestedLoops()
        {
            const string source =
                "def f(d):\n    n = 0\n    for k in d:\n        for j in range(2):\n            n += d[k]\n    return n\n";
            var dict = new DictValue(new List<DictItem>
            {
                new DictItem(new StrValue("a"), new IntValue(1), SourceSpan.None),
                new DictItem(new StrValue("b"), new IntValue(10), SourceSpan.None),
            });
            Assert.That(Show(Call(source, "f", dict)), Is.EqualTo("22"));
        }

        [Test]
        public void ReturnInsideALoopLeavesTheFunction()
        {
            const string source = "def f():\n    for i in range(5):\n        if i == 2:\n            return i * 10\n    return -1\n";
            Assert.That(Show(Call(source, "f")), Is.EqualTo("20"));
        }

        [Test]
        public void ElifChainAndFallingOffTheEndReturnsNone()
        {
            const string source =
                "def pick(n):\n    if n < 0:\n        return 'neg'\n    elif n == 0:\n        return 'zero'\n    elif n < 10:\n" +
                "        return 'small'\n";
            Assert.That(Show(Call(source, "pick", new IntValue(-5))), Is.EqualTo("neg"));
            Assert.That(Show(Call(source, "pick", new IntValue(0))), Is.EqualTo("zero"));
            Assert.That(Show(Call(source, "pick", new IntValue(5))), Is.EqualTo("small"));
            Assert.That(Show(Call(source, "pick", new IntValue(50))), Is.EqualTo("None"));
        }

        [Test]
        public void CompoundAssignmentIncludingDivide()
        {
            Assert.That(Show(Call("def f():\n    x = 10\n    x /= 4\n    return x\n", "f")), Is.EqualTo("2.5"));
            Assert.That(Show(Call("def f():\n    x = 10\n    x //= 4\n    x *= 3\n    x -= 1\n    x %= 4\n    return x\n", "f")),
                Is.EqualTo("1"));
        }

        [Test]
        public void UserFunctionsAcceptKeywordArgumentsAndLambdasCaptureLocals()
        {
            const string source =
                "def sub(a, b):\n    return a - b\n\ndef f(x):\n    g = lambda y: x + y\n    return sub(b=1, a=g(2))\n";
            Assert.That(Show(Call(source, "f", new IntValue(10))), Is.EqualTo("11"));
        }

        [Test]
        public void LambdasReadEnclosingVariablesWhenCalledLikePython()
        {
            const string source = "def f():\n    x = 1\n    g = lambda: x\n    x = 2\n    return g()\n";
            Assert.That(Show(Call(source, "f")), Is.EqualTo("2"));
        }

        [TestCase("def f(a):\n    return a\n", 2, "takes 1")]
        [TestCase("def f(a, b):\n    return a\n", 1, "missing the argument 'b'")]
        public void WrongArgumentCountIsAnError(string source, int count, string message)
        {
            var args = Enumerable.Range(0, count).Select(i => (Value)new IntValue(i)).ToArray();
            var e = Fails(source, "f", args);
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Message, Does.Contain(message));
        }

        [Test]
        public void UnknownNameGetsADidYouMeanHint()
        {
            var e = Fails("def f():\n    total = 1\n    return totl\n", "f");
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Hint, Does.Contain("total"));
            Assert.That(e.Span.Line, Is.EqualTo(3));
        }

        // 함수 안 어디서든 대입하는 이름은 함수 전체에서 지역 변수다: 값이 없을 때 같은 이름의 전역·내장을 읽지 않는다 (파이썬, G104)
        [TestCase("def limit():\n    return 10\ndef f(h):\n    if h > 0:\n        limit = 5\n    return limit\n")]
        [TestCase("def f(h):\n    if h > 0:\n        max = 5\n    return max(1, 2)\n")]
        [TestCase("def f(h):\n    for limit in []:\n        pass\n    return limit\ndef limit():\n    return 10\n")]
        [TestCase("def limit():\n    return 10\ndef f(h):\n    g = lambda: limit\n    r = g()\n    limit = 1\n    return r\n")]
        public void ALocalReadBeforeItIsAssignedDoesNotFallBackToAGlobal(string source)
        {
            var e = Fails(source, "f", new IntValue(0));
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Message, Does.Match("local variable '(limit|max)' is used before it is assigned"));
        }

        [Test]
        public void ALocalAssignedOnTheTakenPathShadowsTheGlobal()
        {
            const string source = "def limit():\n    return 10\ndef f(h):\n    if h > 0:\n        limit = 5\n    return limit\n";
            Assert.That(Show(Call(source, "f", new IntValue(1))), Is.EqualTo("5"));
        }

        // 순수 함수의 값은 불변이다: 파이썬에서 그 자리에서 고치는 리스트 +=는 별칭에 보이므로 오류로 막는다 (G104)
        [Test]
        public void PlusEqualsOnAListIsAnError()
        {
            var e = Fails("def f():\n    a = [1]\n    b = a\n    a += [2]\n    return len(b)\n", "f");
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Message, Does.Contain("'+=' cannot be used on a list"));
            Assert.That(e.Hint, Does.Contain("a = a + [...]"));
            Assert.That(e.Span.Line, Is.EqualTo(4));
        }

        [Test]
        public void ListConcatenationAndPlusEqualsOnNumbersAndStringsStillWork()
        {
            Assert.That(Show(Call("def f():\n    a = [1]\n    a = a + [2]\n    return len(a)\n", "f")), Is.EqualTo("2"));
            Assert.That(Show(Call("def f():\n    s = \"a\"\n    s += \"b\"\n    return s\n", "f")), Is.EqualTo("ab"));
        }

        // ---- 호스트 값 (G103) ----

        [Test]
        public void HealthMaxExampleFromTheSchemaDesign()
        {
            const string source =
                "def health_max(h):\n    flat = 0\n    pct = 0\n    for r in contribs(h, 'max'):\n" +
                "        if r.op == 'add':\n            flat += r.value\n        elif r.op == 'pct':\n            pct += r.value\n" +
                "    return clamp(floor((h.base_max + flat) * (1 + pct / 100)), 1, 9999)\n";
            Value Row(string op, long value) => new Bag(("op", new StrValue(op)), ("value", new IntValue(value)));
            var rows = new ListValue(new[] { Row("add", 3), Row("pct", 10), Row("unknown", 99) });
            var (evaluator, find) = Build(source, extend: (builtins, _) =>
                builtins.Add("contribs", new PureBuiltin("contribs", a => rows)));
            var h = new Bag(("base_max", new IntValue(12)));
            // (12 + 3) * (1 + 10 / 100) = 16.5 -> 16
            Assert.That(Show(evaluator.Run(find("health_max"), new Value[] { h })), Is.EqualTo("16"));
        }

        [Test]
        public void HostErrorsGetThePositionOfTheExpressionAndACallTrace()
        {
            const string source = "def inner(h):\n    return h.nothing\n\ndef outer(h):\n    return inner(h)\n";
            var (evaluator, find) = Build(source);
            var e = Assert.Throws<ScriptRuntimeException>(() => evaluator.Run(find("outer"), new Value[] { new Bag() }))!;
            Assert.That(e.Message, Does.Contain("nothing"));
            Assert.That(e.Span.Line, Is.EqualTo(2));
            Assert.That(e.Trace, Has.Count.EqualTo(2));
            Assert.That(e.Trace[0], Does.StartWith("inner"));
            Assert.That(e.Trace[1], Does.StartWith("outer"));
        }

        [Test]
        public void AttributeOnAPlainValueIsAnError()
        {
            Assert.That(EvalFails("(1).x").Code, Is.EqualTo(Codes.RunScript));
        }

        // ---- 단계 예산 (G101) ----

        [Test]
        public void AnEndlessLookingLoopStopsAtTheStepBudget()
        {
            const string source = "def f():\n    t = 0\n    for i in range(1000000):\n        t += 1\n    return t\n";
            var e = Fails(source, "f");
            Assert.That(e.Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(e.Span.Line, Is.GreaterThan(0));
        }

        [Test]
        public void TheBudgetIsDeterministicAndPerRun()
        {
            const string source = "def f(n):\n    t = 0\n    for i in range(n):\n        t += 1\n    return t\n";
            var (evaluator, find) = Build(source, budget: 100);
            Assert.That(Show(evaluator.Run(find("f"), new Value[] { new IntValue(10) })), Is.EqualTo("10"));
            // 앞 실행이 쓴 단계는 다음 실행에 넘어가지 않는다
            Assert.That(Show(evaluator.Run(find("f"), new Value[] { new IntValue(10) })), Is.EqualTo("10"));
            Assert.That(Assert.Throws<ScriptRuntimeException>(() => evaluator.Run(find("f"), new Value[] { new IntValue(100) }))!.Code,
                Is.EqualTo(Codes.RunBudget));
        }

        // ---- 재귀 (G102) ----

        [Test]
        public void DirectRecursionIsAnErrorWithTheChain()
        {
            var e = Fails("def f(n):\n    return f(n)\n", "f", new IntValue(1));
            Assert.That(e.Code, Is.EqualTo(Codes.RunCycle));
            Assert.That(e.Message, Does.Contain("f -> f"));
        }

        [Test]
        public void MutualRecursionIsAnErrorWithTheChain()
        {
            var e = Fails("def a(n):\n    return b(n)\n\ndef b(n):\n    return a(n)\n", "a", new IntValue(1));
            Assert.That(e.Code, Is.EqualTo(Codes.RunCycle));
            Assert.That(e.Message, Does.Contain("a -> b -> a"));
        }

        // 호스트가 읽기 도중 같은 함수를 다른 엔티티에 대해 다시 부르는 것은 재귀가 아니다 (G100·G103)
        [Test]
        public void AHostCallbackThatRunsTheSameFunctionAgainIsNotRecursion()
        {
            const string source = "def f(n):\n    if n == 0:\n        return 0\n    return n + other(n - 1)\n";
            Func<string, Value>? findFunction = null;
            var (evaluator, find) = Build(source, extend: (builtins, getEvaluator) =>
                builtins.Add("other", new PureBuiltin("other", a => getEvaluator().Run(findFunction!("f"), a))));
            findFunction = find;
            Assert.That(Show(evaluator.Run(find("f"), new Value[] { new IntValue(3) })), Is.EqualTo("6"));
        }
    }
}
