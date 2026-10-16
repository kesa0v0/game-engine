using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary>컴프리헨션과 제너레이터 식의 실행 (spec S2, spec-gaps G189).</summary>
    public class ComprehensionTests
    {
        private static Value Run(string body, int budget = PureEvaluator.DefaultStepBudget, string extra = "")
        {
            var bag = new DiagnosticBag();
            var module = Parser.Parse("def f():\n" + body + extra, "t.py", bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            var scope = new ModScope();
            foreach (var stmt in module.Body)
                if (stmt is FunctionDef d) scope.Bind(d.Name, new FunctionValue(d, "t"), d.Span);
            var evaluator = new PureEvaluator(new ScopeGlobals(new Dictionary<string, ModScope> { ["t"] = scope }),
                PureBuiltins.Create(), budget);
            scope.TryGet("f", out var f, out _);
            return evaluator.Run(f, Array.Empty<Value>());
        }

        private static string Show(Value v)
        {
            switch (v)
            {
                case IntValue i: return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case BoolValue b: return b.Value ? "True" : "False";
                case StrValue s: return "'" + s.Value + "'";
                case ListValue l: return "[" + string.Join(", ", l.Items.Select(Show)) + "]";
                case DictValue d: return "{" + string.Join(", ", d.Items.Select(i => Show(i.Key) + ": " + Show(i.Value))) + "}";
            }
            return v.KindName;
        }

        private static string Eval(string expression) => Show(Run("    return " + expression + "\n"));

        private static ScriptRuntimeException Fails(string body, int budget = PureEvaluator.DefaultStepBudget) =>
            Assert.Throws<ScriptRuntimeException>(() => Run(body, budget))!;

        [TestCase("[x * 2 for x in [1, 2, 3]]", "[2, 4, 6]")]
        [TestCase("[x for x in range(5) if x % 2 == 0]", "[0, 2, 4]")]
        [TestCase("[x for x in range(10) if x > 2 if x < 5]", "[3, 4]")]
        [TestCase("[a * 10 + b for a in range(3) if a != 1 for b in range(2)]", "[0, 1, 20, 21]")]
        [TestCase("[b for a in [[1, 2], [3]] for b in a]", "[1, 2, 3]")]
        [TestCase("[k for k in {'a': 1, 'b': 2}]", "['a', 'b']")]
        [TestCase("[x if x > 1 else 0 for x in [1, 2]]", "[0, 2]")]
        [TestCase("[x for x in []]", "[]")]
        public void AListComprehensionFollowsPython(string expression, string expected)
        {
            Assert.That(Eval(expression), Is.EqualTo(expected));
        }

        [TestCase("{x: x * x for x in range(3)}", "{0: 0, 1: 1, 2: 4}")]
        [TestCase("{x % 2: x for x in [1, 2, 3]}", "{1: 3, 0: 2}")]
        [TestCase("{s: s + s for s in ['ab', 'c'] if s != 'c'}", "{'ab': 'abab'}")]
        public void ADictComprehensionKeepsTheFirstPlaceAndTheLastValueOfAKey(string expression, string expected)
        {
            Assert.That(Eval(expression), Is.EqualTo(expected));
        }

        [Test]
        public void TheLoopVariableDoesNotLeak()
        {
            Assert.That(Show(Run("    x = 5\n    y = [x for x in [1, 2]]\n    return x\n")), Is.EqualTo("5"));
            Assert.That(Fails("    y = [i for i in [1]]\n    return i\n").Message, Does.Contain("name 'i' is not defined"));
        }

        [Test]
        public void TheFirstIterableIsComputedOutside()
        {
            Assert.That(Show(Run("    x = [1, 2]\n    return [x + 1 for x in x]\n")), Is.EqualTo("[2, 3]"));
        }

        [Test]
        public void ALambdaMadeInsideSeesTheLastValueLikePython()
        {
            Assert.That(Show(Run("    fs = [lambda: i for i in range(3)]\n    return fs[0]()\n")), Is.EqualTo("2"));
        }

        [TestCase("sum(x for x in range(4))", "6")]
        [TestCase("max(x for x in [3, 9, 2])", "9")]
        [TestCase("len([1 for x in range(3)])", "3")]
        [TestCase("sum((x for x in range(3)), 10)", "13")]
        public void AGeneratorExpressionFeedsABuiltin(string expression, string expected)
        {
            Assert.That(Eval(expression), Is.EqualTo(expected));
        }

        [Test]
        public void AUserFunctionGetsAGeneratorExpressionAsAList()
        {
            var v = Run("    return g(x for x in range(3))\n", extra: "def g(xs):\n    return len(xs) + xs[2]\n");
            Assert.That(Show(v), Is.EqualTo("5"));
        }

        [TestCase("any([])", "False")]
        [TestCase("all([])", "True")]
        [TestCase("any([0, None, 2])", "True")]
        [TestCase("all([1, 'a', 0])", "False")]
        [TestCase("any(range(1))", "False")]
        [TestCase("all({'a': 1})", "True")]
        [TestCase("any(x > 2 for x in range(5))", "True")]
        [TestCase("all(x < 5 for x in range(5))", "True")]
        public void AnyAndAllFollowPython(string expression, string expected)
        {
            Assert.That(Eval(expression), Is.EqualTo(expected));
        }

        [Test]
        public void AnyAndAllStopOnceTheAnswerIsKnown()
        {
            // 0으로 나누는 원소까지 가지 않는다 (파이썬과 같다). 리스트로 쓰면 먼저 다 만들어 오류다
            Assert.That(Eval("any(10 // x > 1 for x in [1, 0])"), Is.EqualTo("True"));
            Assert.That(Eval("all(10 // x > 100 for x in [1, 0])"), Is.EqualTo("False"));
            Assert.That(Fails("    return any([10 // x > 1 for x in [1, 0]])\n").Code, Is.EqualTo(Codes.RunArithmetic));
        }

        [Test]
        public void EveryRoundCountsASteps()
        {
            Assert.That(Fails("    return [0 for i in range(1000)]\n", budget: 500).Code, Is.EqualTo(Codes.RunBudget));
            Assert.That(Fails("    return any(False for i in range(1000))\n", budget: 500).Code, Is.EqualTo(Codes.RunBudget));
        }

        [TestCase("    return [x for x in 3]\n", "'int' is not iterable")]
        [TestCase("    return {[1]: 1 for x in [1]}\n", "dict keys must be str or int, not list")]
        [TestCase("    return any(3)\n", "any() needs a list, not int")]
        public void MistakesAreRuntimeErrors(string body, string message)
        {
            Assert.That(Fails(body).Message, Does.Contain(message));
        }
    }
}
