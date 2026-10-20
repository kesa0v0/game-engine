using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary><c>match</c>의 실행: 리터럴·세로줄·<c>_</c>·가드, 대상은 한 번만 평가 (spec S2, spec-gaps G253).</summary>
    public class MatchTests
    {
        private static Value Run(string text, params Value[] args)
        {
            var bag = new DiagnosticBag();
            var module = Parser.Parse(text, "t.py", bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            var scope = new ModScope();
            foreach (var stmt in module.Body)
                if (stmt is FunctionDef d) scope.Bind(d.Name, new FunctionValue(d, "t"), d.Span);
            var evaluator = new PureEvaluator(new ScopeGlobals(new Dictionary<string, ModScope> { ["t"] = scope }),
                PureBuiltins.Create());
            scope.TryGet("f", out var f, out _);
            return evaluator.Run(f, args);
        }

        private const string Pick =
            "def f(x):\n" +
            "    match x:\n" +
            "        case \"a\" | \"b\":\n" +
            "            return 1\n" +
            "        case 3:\n" +
            "            return 2\n" +
            "        case -1 | 2.5:\n" +
            "            return 3\n" +
            "        case True:\n" +
            "            return 4\n" +
            "        case None:\n" +
            "            return 5\n" +
            "        case _:\n" +
            "            return 6\n";

        private static long Pick1(Value x) => ((IntValue)Run(Pick, x)).Value;

        [Test]
        public void LiteralsAndAlternativesPickTheFirstCaseThatMatches()
        {
            Assert.That(Pick1(new StrValue("b")), Is.EqualTo(1));
            Assert.That(Pick1(new IntValue(3)), Is.EqualTo(2));
            Assert.That(Pick1(new IntValue(-1)), Is.EqualTo(3));
            Assert.That(Pick1(Run("def f():\n    return 2.5\n")), Is.EqualTo(3));
            Assert.That(Pick1(BoolValue.True), Is.EqualTo(4));
            Assert.That(Pick1(NoneValue.Instance), Is.EqualTo(5));
            Assert.That(Pick1(new StrValue("z")), Is.EqualTo(6));
        }

        [Test]
        public void DifferentTypesDoNotMatch()
        {
            // True와 1은 서로 맞지 않는다 (== 그대로, G253)
            Assert.That(Pick1(new IntValue(1)), Is.EqualTo(6));
        }

        [Test]
        public void WithoutAMatchingCaseNothingHappens()
        {
            var v = Run("def f(x):\n    n = 0\n    match x:\n        case 1:\n            n = 1\n    return n\n", new IntValue(9));
            Assert.That(((IntValue)v).Value, Is.EqualTo(0));
        }

        [Test]
        public void AGuardIsCheckedAfterThePatternAndCanUseLocals()
        {
            const string text =
                "def f(x):\n    limit = 5\n    match x:\n        case 1 | 9 if x > limit:\n            return \"big\"\n" +
                "        case _ if x < 0:\n            return \"neg\"\n        case _:\n            return \"other\"\n";
            Assert.That(((StrValue)Run(text, new IntValue(9))).Value, Is.EqualTo("big"));
            Assert.That(((StrValue)Run(text, new IntValue(1))).Value, Is.EqualTo("other"));
            Assert.That(((StrValue)Run(text, new IntValue(-4))).Value, Is.EqualTo("neg"));
        }

        [Test]
        public void ALoneWildcardAlwaysRuns()
        {
            Assert.That(((IntValue)Run("def f(x):\n    match x:\n        case _:\n            return 7\n", new IntValue(0))).Value,
                Is.EqualTo(7));
        }

        [Test]
        public void BreakInsideACaseLeavesTheLoop()
        {
            const string text =
                "def f():\n    t = 0\n    for i in range(10):\n        match i:\n            case 3:\n                break\n" +
                "            case _:\n                t += i\n    return t\n";
            Assert.That(((IntValue)Run(text)).Value, Is.EqualTo(0 + 1 + 2));
        }

        [Test]
        public void NestedMatchesKeepTheirOwnSubjects()
        {
            const string text =
                "def f(a, b):\n    match a:\n        case 1:\n            match b:\n                case 2:\n                    return \"12\"\n" +
                "            return \"1?\"\n        case _:\n            return \"?\"\n";
            Assert.That(((StrValue)Run(text, new IntValue(1), new IntValue(2))).Value, Is.EqualTo("12"));
            Assert.That(((StrValue)Run(text, new IntValue(1), new IntValue(3))).Value, Is.EqualTo("1?"));
        }
    }
}
