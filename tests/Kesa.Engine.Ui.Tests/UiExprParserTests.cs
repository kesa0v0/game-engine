using System.Linq;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>UXML 식 (G332·G344): 호출 하나나 변수 하나, 변수에서 점으로 사전 키 읽기.</summary>
    public class UiExprParserTests
    {
        [Test]
        public void ACallHasAModDotFunctionNameAndArguments()
        {
            var call = (CallExpr)UiExprParser.Parse("mymod.hp_text(unit)");
            Assert.That(call.Function, Is.EqualTo("mymod.hp_text"));
            Assert.That(call.Arguments.Single(), Is.InstanceOf<VarExpr>());
            Assert.That(((VarExpr)call.Arguments[0]).Name, Is.EqualTo("unit"));
            Assert.That(((CallExpr)UiExprParser.Parse("mymod.now()")).Arguments, Is.Empty);
        }

        [Test]
        public void ArgumentsAreVariablesOrFixedValues()
        {
            var call = (CallExpr)UiExprParser.Parse("m.f(u, 'attack', 3, -2, 0.5, True, False, None, row.unit)");
            var literals = call.Arguments.OfType<LiteralExpr>().Select(l => l.Value).ToList();
            Assert.That(literals, Is.EqualTo(new BridgeValue[]
            {
                new BridgeStr("attack"), new BridgeInt(3), new BridgeInt(-2), new BridgeFixed(500_000),
                BridgeBool.True, BridgeBool.False, BridgeNone.Instance,
            }));
            var last = (VarExpr)call.Arguments[8];
            Assert.That((last.Name, last.Keys.Single()), Is.EqualTo(("row", "unit")));
        }

        [Test]
        public void ADecimalIsTheRawFixedValue()
        {
            var fixedValue = (BridgeFixed)((LiteralExpr)((CallExpr)UiExprParser.Parse("m.f(12.345678)")).Arguments[0]).Value;
            Assert.That(fixedValue.Raw, Is.EqualTo(12_345_678));
            var negative = (BridgeFixed)((LiteralExpr)((CallExpr)UiExprParser.Parse("m.f(-0.25)")).Arguments[0]).Value;
            Assert.That(negative.Raw, Is.EqualTo(-250_000));
        }

        [Test]
        public void AVariableReadsDictionaryKeysWithDots()
        {
            var plain = (VarExpr)UiExprParser.Parse("unit");
            Assert.That((plain.Name, plain.Keys.Count), Is.EqualTo(("unit", 0)));
            var path = (VarExpr)UiExprParser.Parse("row.stats.hp");
            Assert.That(path.Name, Is.EqualTo("row"));
            Assert.That(path.Keys, Is.EqualTo(new[] { "stats", "hp" }));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("mymod.f(")]
        [TestCase("mymod.f(a b)")]
        [TestCase("mymod.f(mymod.g(x))")]
        [TestCase("f(x)")]
        [TestCase("mymod.f(x) + 1")]
        [TestCase("mymod.f(x) > 1")]
        [TestCase("a.b(c).d")]
        [TestCase("True")]
        [TestCase("'text'")]
        [TestCase("mymod.f('open")]
        [TestCase("mymod.f(1.1234567)")]
        public void WhatTheGrammarDoesNotAllowIsAnError(string text)
        {
            Assert.Throws<UiSyntaxException>(() => UiExprParser.Parse(text));
        }

        [Test]
        public void TheErrorSaysWhereAndWhy()
        {
            var e = Assert.Throws<UiSyntaxException>(() => UiExprParser.Parse("mymod.f(mymod.g(x))"))!;
            Assert.That(e.Message, Does.Contain("a call cannot be an argument"));
            Assert.That(e.Column, Is.EqualTo(8));
        }
    }
}
