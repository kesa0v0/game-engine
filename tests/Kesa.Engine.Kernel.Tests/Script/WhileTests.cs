using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary><c>while</c>의 실행: 파이썬 그대로, 상한은 단계 예산 (spec S2, spec-gaps G195).</summary>
    public class WhileTests
    {
        private static Value Run(string body, int budget = PureEvaluator.DefaultStepBudget)
        {
            var bag = new DiagnosticBag();
            var module = Parser.Parse("def f():\n" + body, "t.py", bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            var scope = new ModScope();
            foreach (var stmt in module.Body)
                if (stmt is FunctionDef d) scope.Bind(d.Name, new FunctionValue(d, "t"), d.Span);
            var evaluator = new PureEvaluator(new ScopeGlobals(new Dictionary<string, ModScope> { ["t"] = scope }),
                PureBuiltins.Create(), budget);
            scope.TryGet("f", out var f, out _);
            return evaluator.Run(f, Array.Empty<Value>());
        }

        private static long Int(string body) => ((IntValue)Run(body)).Value;

        [Test]
        public void ItRepeatsWhileTheConditionHolds()
        {
            Assert.That(Int("    n = 0\n    t = 0\n    while n < 5:\n        n += 1\n        t += n\n    return t\n"), Is.EqualTo(15));
        }

        [Test]
        public void AFalseConditionSkipsTheBody()
        {
            Assert.That(Int("    n = 7\n    while n < 5:\n        n = 100\n    return n\n"), Is.EqualTo(7));
        }

        [Test]
        public void BreakAndContinueWorkLikePython()
        {
            Assert.That(Int("    n = 0\n    t = 0\n    while True:\n        n += 1\n        if n % 2 == 0:\n            continue\n" +
                            "        if n > 7:\n            break\n        t += n\n    return t\n"), Is.EqualTo(1 + 3 + 5 + 7));
        }

        [Test]
        public void BreakLeavesOnlyTheInnerLoop()
        {
            Assert.That(Int("    t = 0\n    for i in range(3):\n        j = 0\n        while True:\n            j += 1\n" +
                            "            if j > i:\n                break\n            t += 1\n    return t\n"), Is.EqualTo(0 + 1 + 2));
        }

        [Test]
        public void ReturnInsideTheLoopEndsTheFunction()
        {
            Assert.That(Int("    n = 0\n    while True:\n        n += 3\n        if n > 10:\n            return n\n"), Is.EqualTo(12));
        }

        [Test]
        public void ALoopThatNeverEndsRunsOutOfTheStepBudget()
        {
            var e = Assert.Throws<ScriptRuntimeException>(() => Run("    while True:\n        pass\n", budget: 500))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunBudget));
        }

        [Test]
        public void TheConditionCountsEachRoundEvenWithAnEmptyLookingBody()
        {
            // 몸통이 continue뿐이어도 조건 검사가 단계를 쓴다
            var e = Assert.Throws<ScriptRuntimeException>(() => Run("    while True:\n        continue\n", budget: 500))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunBudget));
        }

        [Test]
        public void AnErrorInTheConditionPointsAtTheWhile()
        {
            var e = Assert.Throws<ScriptRuntimeException>(() => Run("    n = 0\n    while n < \"x\":\n        n += 1\n"))!;
            Assert.That(e.Span.Line, Is.EqualTo(3));
        }
    }
}
