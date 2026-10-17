using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary>
    /// 실행기 구조를 바꿀 때(G226) 바뀌면 안 되는 것을 고정한다: 실패한 실행의 코드·위치·흔적, 여러 문법을 쓰는 프로그램의 결과와 단계 수.
    /// 값은 바꾸기 전 실행기에서 잰 것이다.
    /// </summary>
    public class EvaluatorPinTests
    {
        private static (PureEvaluator Evaluator, Func<string, Value> Function) Build(string source)
        {
            var bag = new DiagnosticBag();
            var module = Parser.Parse(source, "t.py", bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            var scope = new ModScope();
            foreach (var stmt in module.Body)
                if (stmt is FunctionDef d) scope.Bind(d.Name, new FunctionValue(d, "t"), d.Span);

            var builtins = PureBuiltins.Create();
            PureEvaluator? evaluator = null;
            // 다른 실행을 안에서 부르는 내장 (derived 안에서 다른 derived를 읽는 것과 같은 길)
            builtins["read"] = new PureBuiltin("read", args =>
            {
                Assert.That(scope.TryGet(((StrValue)args[0]).Value, out var f, out _), Is.True);
                return evaluator!.Run(f, new Value[0], new SourceSpan("t.py", 99, 1));
            });
            evaluator = new PureEvaluator(new ScopeGlobals(new Dictionary<string, ModScope> { ["t"] = scope }), builtins, 2000);
            return (evaluator, name =>
            {
                Assert.That(scope.TryGet(name, out var f, out _), Is.True);
                return f;
            });
        }

        private static string Failure(string source)
        {
            var (evaluator, find) = Build(source);
            try
            {
                evaluator.Run(find("f"), new Value[0]);
            }
            catch (ScriptRuntimeException e)
            {
                return e.Code + " @" + e.Span.Line + ":" + e.Span.Col + " [" + string.Join(" / ", e.Trace) + "]";
            }
            return "no error";
        }

        [TestCase("def f():\n    return nothing + 1\n",
            "RUN015 @2:12 [f]")]
        [TestCase("def f():\n    if 1 > 2:\n        x = 1\n    return 3 + x\n",
            "RUN015 @4:16 [f]")]
        [TestCase("def f():\n    a = 1\n    return [a, a + \"s\"]\n",
            "RUN015 @3:18 [f]")]
        [TestCase("def f():\n    return 4 // (2 - 2)\n",
            "RUN012 @2:14 [f]")]
        [TestCase("def f():\n    xs = [1, 2]\n    return xs[5]\n",
            "RUN015 @3:14 [f]")]
        [TestCase("def f():\n    return (1).nope\n",
            "RUN015 @2:16 [f]")]
        [TestCase("def f():\n    return max()\n",
            "RUN015 @2:15 [f]")]
        [TestCase("def g(n):\n    return n.nope\ndef f():\n    x = 1\n    y = g(x)\n    return y\n",
            "RUN015 @2:14 [g (called at t.py:5:10) / f]")]
        [TestCase("def g(n):\n    return 1 // n\ndef f():\n    return 2 + g(0)\n",
            "RUN012 @2:14 [g (called at t.py:4:17) / f]")]
        [TestCase("def h():\n    return 1 // 0\ndef f():\n    return 1 + read(\"h\")\n",
            "RUN012 @2:14 [h (called at t.py:99:1) / f at t.py:4:20]")]
        [TestCase("def f():\n    t = 0\n    while True:\n        t += 1\n    return t\n",
            "RUN013 @3:11 [f]")]
        [TestCase("def f():\n    return sum([i * 2 for i in range(5000)])\n",
            "RUN013 @2:19 [f]")]
        [TestCase("def g():\n    return f()\ndef f():\n    return g()\n",
            "RUN014 @2:13 [g (called at t.py:4:13) / f]")]
        [TestCase("def f():\n    k = lambda x: x // 0\n    return k(3)\n",
            "RUN012 @2:21 [<lambda> (called at t.py:3:13) / f]")]
        [TestCase("def f():\n    return [y for y in range(3) if y // 0]\n",
            "RUN012 @2:38 [f]")]
        [TestCase("def g(a, b):\n    return a\ndef f():\n    return g(b=2)\n",
            "RUN015 @4:13 [f]")]
        [TestCase("def f():\n    return {\"a\": 1, \"a\": 2}\n",
            "RUN015 @2:12 [f]")]
        [TestCase("def f():\n    x = 5\n    x += \"s\"\n    return x\n",
            "RUN015 @3:7 [f]")]
        [TestCase("def f():\n    return -\"s\"\n",
            "RUN015 @2:12 [f]")]
        [TestCase("def f():\n    for i in 5:\n        pass\n",
            "RUN015 @2:5 [f]")]
        [TestCase("def f():\n    return 1 if (1).x else 2\n",
            "RUN015 @2:21 [f]")]
        public void AFailureKeepsItsCodePlaceAndTrace(string source, string expected)
        {
            Assert.That(Failure(source), Is.EqualTo(expected));
        }

        private const string Program =
            "def scale(v, by, plus):\n" +
            "    return v * by + plus\n" +
            "def pick(xs):\n" +
            "    best = None\n" +
            "    for x in xs:\n" +
            "        if best is None or x > best:\n" +
            "            best = x\n" +
            "    return best\n" +
            "def f(n):\n" +
            "    base = 10\n" +
            "    add = lambda x: x + base\n" +
            "    twice = lambda g: lambda y: g(g(y))\n" +
            "    base = 20\n" +
            "    squares = [i * i for i in range(n) if i % 2 == 0]\n" +
            "    pairs = {k: k * 2 for k in squares}\n" +
            "    nested = [a + b for a in range(3) for b in range(a)]\n" +
            "    t = 0\n" +
            "    while t < n:\n" +
            "        t += 1\n" +
            "        if t == 2:\n" +
            "            continue\n" +
            "        t = t + 0\n" +
            "    m = pick(squares)\n" +
            "    s = scale(m, 2, plus=1) + scale(plus=0, v=2, by=3)\n" +
            "    ok = any(q > 10 for q in squares) and not all(q > 10 for q in squares)\n" +
            "    label = \"big\" if s > 50 else \"small\"\n" +
            "    total = twice(add)(1) + len(pairs) + sum(nested) + s + t\n" +
            "    if ok and label == \"big\":\n" +
            "        total -= 1\n" +
            "    elif 2 in squares:\n" +
            "        total += 100\n" +
            "    else:\n" +
            "        total *= 2\n" +
            "    return [total, label, ok, pairs[4], min(squares), -t, n // 3, n % 4]\n";

        [Test]
        public void ALambdaKeepsTheLocalsOfACallWhoseFrameWasReused()
        {
            // 끝난 호출의 프레임은 다시 쓰지만(G226 호출 규약), 람다는 프레임이 아니라 그 호출의 지역 변수 표를 잡는다
            const string source =
                "def make(n):\n" +
                "    k = n * 10\n" +
                "    return lambda x: x + k + n\n" +
                "def f():\n" +
                "    a = make(1)\n" +
                "    b = make(2)\n" +
                "    c = make(3)\n" +
                "    return [a(0), b(0), c(0), make(4)(1)]\n";
            var (evaluator, find) = Build(source);
            var result = (ListValue)evaluator.Run(find("f"), new Value[0]);
            Assert.That(result.Items.Select(v => ((IntValue)v).Value), Is.EqualTo(new long[] { 11, 22, 33, 45 }));
            // 같은 실행기로 다시 불러도 같다 (다시 쓰는 세션과 프레임)
            result = (ListValue)evaluator.Run(find("f"), new Value[0]);
            Assert.That(result.Items.Select(v => ((IntValue)v).Value), Is.EqualTo(new long[] { 11, 22, 33, 45 }));
        }

        [Test]
        public void AProgramUsingMostSyntaxKeepsItsResultAndStepCount()
        {
            var (evaluator, find) = Build(Program);
            var meter = new StepMeter(100_000);
            var result = (ListValue)evaluator.Run(find("f"), new Value[] { new IntValue(9) }, meter: meter);
            var shown = new List<string>();
            foreach (var item in result.Items)
            {
                shown.Add(item switch
                {
                    IntValue i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StrValue s => s.Value,
                    BoolValue b => b.Value ? "True" : "False",
                    _ => item.KindName,
                });
            }
            Assert.That(string.Join(",", shown), Is.EqualTo("195,big,True,8,0,-9,3,1"));
            Assert.That(meter.Used, Is.EqualTo(443));
        }
    }
}
