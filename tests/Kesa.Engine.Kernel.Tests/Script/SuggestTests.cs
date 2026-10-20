using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary>"혹시 …?" 후보 (spec S9): 이웃한 두 글자를 맞바꾼 오타도 한 번의 편집이고, 실행 중 이름 오류는 모드 전역도 후보로 본다.</summary>
    public class SuggestTests
    {
        [TestCase("lfet", "left", 1)]
        [TestCase("LIMTI", "LIMIT", 1)]
        [TestCase("left", "left", 0)]
        [TestCase("lef", "left", 1)]
        [TestCase("abc", "cab", 2)]
        [TestCase("", "abc", 3)]
        public void TheDistanceCountsASwapAsOneEdit(string a, string b, int distance)
        {
            Assert.That(Suggest.Distance(a, b), Is.EqualTo(distance));
            Assert.That(Suggest.Distance(b, a), Is.EqualTo(distance));
        }

        [Test]
        public void ASwappedTypoGetsAHint()
        {
            Assert.That(Suggest.Hint("lfet", new[] { "left", "max" }), Is.EqualTo("did you mean 'left'?"));
        }

        [Test]
        public void ARuntimeNameErrorSuggestsAModGlobal()
        {
            // 로드 검사(G108)를 거치지 않는 실행기 직접 실행: 실행 중 안전망의 후보
            var bag = new DiagnosticBag();
            var module = Parser.Parse("def helper():\n    return 1\ndef f():\n    return helpr()\n", "t.py", bag);
            var scope = new ModScope();
            foreach (var stmt in module.Body)
                if (stmt is FunctionDef d) scope.Bind(d.Name, new FunctionValue(d, "t"), d.Span);
            var evaluator = new PureEvaluator(new ScopeGlobals(new Dictionary<string, ModScope> { ["t"] = scope }),
                PureBuiltins.Create(), 2000);
            Assert.That(scope.TryGet("f", out var f, out _), Is.True);
            var e = Assert.Throws<ScriptRuntimeException>(() => evaluator.Run(f, Array.Empty<Value>()))!;
            Assert.That(e.Message, Is.EqualTo("name 'helpr' is not defined"));
            Assert.That(e.Hint, Is.EqualTo("did you mean 'helper'?"));
        }
    }
}
