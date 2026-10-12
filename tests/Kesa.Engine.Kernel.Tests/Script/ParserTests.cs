using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    public class ParserTests
    {
        private static (Module Module, IReadOnlyList<Diagnostic> Diagnostics) Parse(string text)
        {
            var bag = new DiagnosticBag();
            var module = Parser.Parse(text, "test.py", bag);
            return (module, bag.ToSortedList());
        }

        [Test]
        public void DefinitionCallWithKeywords()
        {
            var (module, diags) = Parse("Unit(\"Scout\", move=3, speed=1.5, tags=[\"fast\"], stats={\"hp\": 10})\n");
            Assert.That(diags, Is.Empty);
            var call = (CallExpr)((ExprStmt)module.Body.Single()).Expr;
            Assert.That(((NameExpr)call.Callee).Name, Is.EqualTo("Unit"));
            Assert.That(call.Arguments.Select(a => a.Name), Is.EqualTo(new string?[] { null, "move", "speed", "tags", "stats" }));
            Assert.That(((StringLiteral)call.Arguments[0].Value).Value, Is.EqualTo("Scout"));
            Assert.That(((IntLiteral)call.Arguments[1].Value).Value, Is.EqualTo(3));
            Assert.That(((FixedLiteral)call.Arguments[2].Value).Value.Raw, Is.EqualTo(1_500_000));
            Assert.That(((ListExpr)call.Arguments[3].Value).Items, Has.Count.EqualTo(1));
            Assert.That(((DictExpr)call.Arguments[4].Value).Entries, Has.Count.EqualTo(1));
        }

        [Test]
        public void AttributeCallAndAssignment()
        {
            var (module, diags) = Parse("patch.set(\"Scout\", \"move\", 4)\nx = -2 ** 2\ny += 1; z = a.b[0]\n");
            Assert.That(diags, Is.Empty);
            Assert.That(module.Body, Has.Count.EqualTo(4));
            var callee = (AttributeExpr)((CallExpr)((ExprStmt)module.Body[0]).Expr).Callee;
            Assert.That(callee.Name, Is.EqualTo("set"));
            var assign = (AssignStmt)module.Body[1];
            var neg = (UnaryExpr)assign.Value;
            Assert.That(neg.Op, Is.EqualTo("-"));
            Assert.That(((BinaryExpr)neg.Operand).Op, Is.EqualTo("**"));
            Assert.That(((AssignStmt)module.Body[2]).Op, Is.EqualTo("+="));
            Assert.That(((AssignStmt)module.Body[3]).Value, Is.InstanceOf<IndexExpr>());
        }

        [Test]
        public void AdjacentStringsConcatenate()
        {
            var (module, diags) = Parse("x = \"a\" 'b'\n");
            Assert.That(diags, Is.Empty);
            Assert.That(((StringLiteral)((AssignStmt)module.Body[0]).Value).Value, Is.EqualTo("ab"));
        }

        [Test]
        public void Precedence()
        {
            var (module, diags) = Parse("x = a or b and not c == d + e * f\n");
            Assert.That(diags, Is.Empty);
            var or = (BinaryExpr)((AssignStmt)module.Body[0]).Value;
            Assert.That(or.Op, Is.EqualTo("or"));
            var and = (BinaryExpr)or.Right;
            Assert.That(and.Op, Is.EqualTo("and"));
            var not = (UnaryExpr)and.Right;
            var eq = (BinaryExpr)not.Operand;
            Assert.That(eq.Op, Is.EqualTo("=="));
            var plus = (BinaryExpr)eq.Right;
            Assert.That(plus.Op, Is.EqualTo("+"));
            Assert.That(((BinaryExpr)plus.Right).Op, Is.EqualTo("*"));
        }

        [Test]
        public void NotInAndIsNot()
        {
            var (module, diags) = Parse("a = x not in y\nb = x is not None\n");
            Assert.That(diags, Is.Empty);
            Assert.That(((BinaryExpr)((AssignStmt)module.Body[0]).Value).Op, Is.EqualTo("not in"));
            Assert.That(((BinaryExpr)((AssignStmt)module.Body[1]).Value).Op, Is.EqualTo("is not"));
        }

        [Test]
        public void TrailingCommasAreAllowed()
        {
            var (_, diags) = Parse("f(1, a=2,)\nx = [1, 2,]\ny = {1: 2,}\n");
            Assert.That(diags, Is.Empty);
        }

        // 유효한 파이썬이 아니면 받아들이지 않는다
        [TestCase("f(a=1, a=2)\n", Codes.ParDuplicateKeyword)]
        [TestCase("f(a=1, 2)\n", Codes.ParPositionalAfterKeyword)]
        [TestCase("f(1 2)\n", Codes.ParSyntax)]
        [TestCase("1 = x\n", Codes.ParSyntax)]
        [TestCase("x = \n", Codes.ParSyntax)]
        [TestCase("  x = 1\n", Codes.ParSyntax)]
        [TestCase("x = if\n", Codes.ParSyntax)]
        [TestCase("x.class = 1\n", Codes.ParSyntax)]
        public void InvalidPythonIsRejected(string text, string code)
        {
            var (_, diags) = Parse(text);
            Assert.That(diags.Select(d => d.Code), Does.Contain(code));
        }

        // 유효한 파이썬이지만 아직 지원하지 않는 것은 구문 오류와 구분해 보고한다
        [TestCase("def f():\n    return 1\nx = 2\n")]
        [TestCase("if x:\n    y = 1\n")]
        [TestCase("import os\n")]
        [TestCase("x = (1, 2)\n")]
        [TestCase("x = [i for i in y]\n")]
        [TestCase("x = {1, 2}\n")]
        [TestCase("x = lambda: 1\n")]
        [TestCase("x = a if b else c\n")]
        [TestCase("x = a < b < c\n")]
        [TestCase("x: int = 1\n")]
        [TestCase("f(*args)\n")]
        [TestCase("x = y[1:2]\n")]
        [TestCase("@command\ndef Move():\n    pass\n")]
        [TestCase("@on(\"tick\", stage=\"core.economy\")\n@other\ndef f():\n    pass\n")]
        [TestCase("match x:\n    case 1:\n        pass\n")]
        public void UnsupportedPythonIsReportedSeparately(string text)
        {
            var (_, diags) = Parse(text);
            Assert.That(diags, Is.Not.Empty);
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.ParUnsupported));
        }

        [Test]
        public void MatchIsAnOrdinaryNameOutsideAMatchStatement()
        {
            var (module, diags) = Parse("match = 1\nx = match(y)\n");
            Assert.That(diags, Is.Empty);
            Assert.That(module.Body, Has.Count.EqualTo(2));
        }

        [Test]
        public void ParsingContinuesAfterDecoratedFunction()
        {
            var (module, _) = Parse("@command\ndef f():\n    pass\nx = 2\n");
            Assert.That(module.Body.Last(), Is.InstanceOf<AssignStmt>());
        }

        [Test]
        public void ParsingContinuesAfterUnsupportedBlock()
        {
            var (module, _) = Parse("def f():\n    return 1\nx = 2\n");
            Assert.That(module.Body.Last(), Is.InstanceOf<AssignStmt>());
        }

        [Test]
        public void ParsingContinuesAfterSyntaxError()
        {
            var (module, diags) = Parse("f(1 2)\ng(3)\n");
            Assert.That(diags.Count(d => d.Code == Codes.ParSyntax), Is.EqualTo(1));
            Assert.That(module.Body.OfType<ExprStmt>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void SyntaxErrorLocation()
        {
            var (_, diags) = Parse("x = 1\nf(1 2)\n");
            var d = diags.Single();
            Assert.That((d.File, d.Line, d.Col), Is.EqualTo(("test.py", 2, 5)));
        }
    }
}
