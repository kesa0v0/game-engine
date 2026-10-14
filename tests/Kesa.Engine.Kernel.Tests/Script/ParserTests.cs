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
        [TestCase("def f(x=1):\n    return x\n")]
        [TestCase("def f(*args):\n    pass\n")]
        [TestCase("for k, v in y:\n    pass\n")]
        [TestCase("for (a, b) in y:\n    pass\n")]
        [TestCase("for x in y:\n    pass\nelse:\n    pass\n")]
        [TestCase("def f():\n    def g():\n        pass\n")]
        [TestCase("def f():\n    return 1, 2\n")]
        [TestCase("while x:\n    pass\n")]
        [TestCase("import os\n")]
        [TestCase("x = (1, 2)\n")]
        [TestCase("x = [i for i in y]\n")]
        [TestCase("x = {1, 2}\n")]
        [TestCase("x = lambda x=1: x\n")]
        [TestCase("x = lambda *a: 1\n")]
        [TestCase("x = a < b < c\n")]
        [TestCase("x: int = 1\n")]
        [TestCase("f(*args)\n")]
        [TestCase("x = y[1:2]\n")]
        [TestCase("with a as b:\n    pass\n")]
        [TestCase("with a, b:\n    pass\n")]
        [TestCase("@command\nclass C:\n    pass\n")]
        [TestCase("def f():\n    @command\n    def g():\n        pass\n")]
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
        public void DecoratorsAreParsedAsExpressionsOnTheFunction()
        {
            var (module, diags) = Parse("@command\n@on(\"tick\", stage=\"core.economy\")\ndef f():\n    pass\nx = 2\n");
            Assert.That(diags, Is.Empty);
            var def = (FunctionDef)module.Body[0];
            Assert.That(def.Decorators, Has.Count.EqualTo(2));
            Assert.That(((NameExpr)def.Decorators[0]).Name, Is.EqualTo("command"));
            Assert.That(((CallExpr)def.Decorators[1]).Arguments.Select(a => a.Name), Is.EqualTo(new[] { null, "stage" }));
            Assert.That(module.Body.Last(), Is.InstanceOf<AssignStmt>());
        }

        [Test]
        public void DecoratedFunctionsKeepTheirDefaultValues()
        {
            // 기본값은 커맨드(데코레이터가 붙은 def)에만 (spec-gaps G116)
            var (module, diags) = Parse("@command\ndef f(a: int, b: int = -1, c: str = \"x\"):\n    pass\n");
            Assert.That(diags, Is.Empty);
            var def = (FunctionDef)module.Body[0];
            Assert.That(def.Parameters.Select(p => p.Default == null), Is.EqualTo(new[] { true, false, false }));
            Assert.That(def.Parameters[1].Default, Is.InstanceOf<UnaryExpr>());
        }

        [Test]
        public void AParameterWithoutADefaultCannotFollowOneWithADefault()
        {
            var (_, diags) = Parse("@command\ndef f(a: int = 1, b: int):\n    pass\n");
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.ParSyntax));
            Assert.That(d.Message, Is.EqualTo("parameter 'b' without a default follows one with a default"));
        }

        [Test]
        public void PlainFunctionDefaultsPointAtCommands()
        {
            var (_, diags) = Parse("def f(x=1):\n    return x\n");
            Assert.That(diags.Single().Message, Is.EqualTo("default parameter values are only supported on @command functions"));
        }

        [TestCase("@\ndef f():\n    pass\nx = 2\n")]
        [TestCase("@command x\ndef f():\n    pass\nx = 2\n")]
        [TestCase("@command\nx = 2\n")]
        public void BadDecoratorSyntaxIsReportedAndParsingContinues(string text)
        {
            var (module, diags) = Parse(text);
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.ParSyntax));
            Assert.That(diags, Is.Not.Empty);
            Assert.That(module.Body.Last(), Is.InstanceOf<AssignStmt>());
        }

        [Test]
        public void ParsingContinuesAfterUnsupportedBlock()
        {
            var (module, _) = Parse("while x:\n    y = 1\nx = 2\n");
            Assert.That(module.Body.Last(), Is.InstanceOf<AssignStmt>());
        }

        // ---- if / elif / else (spec-gaps G46) ----

        [Test]
        public void IfElifElseWithIndentedBlocks()
        {
            var (module, diags) = Parse(
                "if a(1):\n    f(1)\n    g(2)\nelif b():\n    h()\nelse:\n    k()\nx = 2\n");
            Assert.That(diags, Is.Empty);
            Assert.That(module.Body, Has.Count.EqualTo(2));
            var ifs = (IfStmt)module.Body[0];
            Assert.That(ifs.Branches, Has.Count.EqualTo(2));
            Assert.That(ifs.Branches[0].Body, Has.Count.EqualTo(2));
            Assert.That(ifs.Branches[1].Body, Has.Count.EqualTo(1));
            Assert.That(ifs.OrElse, Has.Count.EqualTo(1));
            Assert.That((ifs.Span.Line, ifs.Branches[1].Span.Line), Is.EqualTo((1, 4)));
            Assert.That(module.Body[1], Is.InstanceOf<AssignStmt>());
        }

        [Test]
        public void IfWithSameLineBlockAndNesting()
        {
            var (module, diags) = Parse("if a(): f()\nelse: g(); h()\nif a():\n    if b():\n        f()\n    g()\n");
            Assert.That(diags, Is.Empty);
            Assert.That(module.Body, Has.Count.EqualTo(2));
            var first = (IfStmt)module.Body[0];
            Assert.That(first.Branches[0].Body, Has.Count.EqualTo(1));
            Assert.That(first.OrElse, Has.Count.EqualTo(2));
            var outer = (IfStmt)module.Body[1];
            Assert.That(outer.Branches[0].Body, Has.Count.EqualTo(2));
            Assert.That(outer.Branches[0].Body[0], Is.InstanceOf<IfStmt>());
        }

        [TestCase("elif a():\n    f()\n")]
        [TestCase("else:\n    f()\n")]
        [TestCase("if a():\n    f()\nelse:\n    g()\nelse:\n    h()\n")]
        [TestCase("if a()\n    f()\n")]
        [TestCase("if a():\nf()\n")]
        public void BrokenIfIsASyntaxError(string text)
        {
            var (_, diags) = Parse(text);
            Assert.That(diags, Is.Not.Empty);
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.ParSyntax));
        }

        [Test]
        public void ParsingContinuesAfterABrokenIfAndItsElseChain()
        {
            var (module, diags) = Parse("if :\n    f()\nelif b():\n    g()\nelse:\n    h()\nx = 2\n");
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.ParSyntax));
            Assert.That(diags, Has.Count.EqualTo(1));
            Assert.That(module.Body.Last(), Is.InstanceOf<AssignStmt>());
        }

        [Test]
        public void FunctionDefHeaderAndBodyAreParsed()
        {
            var (module, diags) = Parse(
                "def health_max(h: Health) -> int:\n    for r in contribs(h, \"max\"):\n        pass\n    return 1\nx = 2\n" +
                "def g(a, b,): return a\n");
            Assert.That(diags, Is.Empty);
            Assert.That(module.Body, Has.Count.EqualTo(3));
            var f = (FunctionDef)module.Body[0];
            Assert.That(f.Name, Is.EqualTo("health_max"));
            Assert.That(f.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "h" }));
            Assert.That(((NameExpr)f.Parameters[0].Annotation!).Name, Is.EqualTo("Health"));
            Assert.That(((NameExpr)f.Returns!).Name, Is.EqualTo("int"));
            Assert.That(f.Body, Has.Count.EqualTo(2));
            Assert.That(f.Body[0], Is.InstanceOf<ForStmt>());
            Assert.That(((ForStmt)f.Body[0]).Body.Single(), Is.InstanceOf<PassStmt>());
            Assert.That(((ReturnStmt)f.Body[1]).Value, Is.InstanceOf<IntLiteral>());
            Assert.That(module.Body[1], Is.InstanceOf<AssignStmt>());
            var g = (FunctionDef)module.Body[2];
            Assert.That(g.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(((ReturnStmt)g.Body.Single()).Value, Is.InstanceOf<NameExpr>());
        }

        // schema-design 3.3.1의 대표 예제가 진단 없이 읽혀야 한다
        [Test]
        public void HealthMaxExampleFromTheSchemaDesignParsesCleanly()
        {
            var (module, diags) = Parse(
                "def health_max(h: Health) -> int:\n" +
                "    flat = 0\n" +
                "    pct = 0\n" +
                "    for r in contribs(h, \"max\"):\n" +
                "        if r.op == \"add\":\n" +
                "            flat += r.value\n" +
                "        elif r.op == \"pct\":\n" +
                "            pct += r.value\n" +
                "    return clamp(floor((h.base_max + flat) * (1 + pct / 100)), 1, 9999)\n");
            Assert.That(diags, Is.Empty);
            var f = (FunctionDef)module.Body.Single();
            Assert.That(f.Body.Select(s => s.GetType()), Is.EqualTo(new[]
            {
                typeof(AssignStmt), typeof(AssignStmt), typeof(ForStmt), typeof(ReturnStmt),
            }));
            var loop = (ForStmt)f.Body[2];
            Assert.That(loop.Target.Name, Is.EqualTo("r"));
            var branches = ((IfStmt)loop.Body.Single()).Branches;
            Assert.That(branches, Has.Count.EqualTo(2));
            Assert.That(((AssignStmt)branches[0].Body.Single()).Op, Is.EqualTo("+="));
        }

        [Test]
        public void ConditionalExpressionIsParsed()
        {
            var (module, diags) = Parse("x = a if b else c if d else e\ny = lambda g: 1 if g else 2\n");
            Assert.That(diags, Is.Empty);
            var x = (ConditionalExpr)((AssignStmt)module.Body[0]).Value;
            Assert.That(x.Condition, Is.InstanceOf<NameExpr>());
            Assert.That(x.WhenFalse, Is.InstanceOf<ConditionalExpr>());
            var lambda = (LambdaExpr)((AssignStmt)module.Body[1]).Value;
            Assert.That(lambda.Body, Is.InstanceOf<ConditionalExpr>());
        }

        [Test]
        public void BreakAndContinueAreAllowedInsideLoops()
        {
            var (module, diags) = Parse(
                "def f(xs):\n    for x in xs:\n        if x:\n            break\n        continue\n    return\n");
            Assert.That(diags, Is.Empty);
            var f = (FunctionDef)module.Body.Single();
            Assert.That(((ReturnStmt)f.Body[1]).Value, Is.Null);
            var loop = (ForStmt)f.Body[0];
            Assert.That(loop.Body[1], Is.InstanceOf<ContinueStmt>());
        }

        [TestCase("return 1\n")]
        [TestCase("if a:\n    return\n")]
        [TestCase("break\n")]
        [TestCase("def f():\n    break\n")]
        [TestCase("def f():\n    for x in y:\n        pass\n    continue\n")]
        [TestCase("for x in y\n    pass\n")]
        [TestCase("for x y:\n    pass\n")]
        [TestCase("for in y:\n    pass\n")]
        public void MisplacedControlFlowIsASyntaxError(string text)
        {
            var (_, diags) = Parse(text);
            Assert.That(diags, Is.Not.Empty);
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.ParSyntax));
        }

        [Test]
        public void LoopDepthDoesNotLeakOutOfAFunction()
        {
            var (_, diags) = Parse("for x in y:\n    pass\ndef f():\n    break\n");
            Assert.That(diags.Select(d => d.Code), Has.All.EqualTo(Codes.ParSyntax));
            Assert.That(diags, Has.Count.EqualTo(1));
        }

        [Test]
        public void LambdaIsParsedWithItsBody()
        {
            var (module, diags) = Parse(
                "f = lambda g: g.Progress.value >= g.Progress.target\n" +
                "Unit(\"A\", default=lambda: 1, k=lambda a, b: a + b)\n");
            Assert.That(diags, Is.Empty);
            var f = (LambdaExpr)((AssignStmt)module.Body[0]).Value;
            Assert.That(f.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "g" }));
            Assert.That(((BinaryExpr)f.Body).Op, Is.EqualTo(">="));
            var call = (CallExpr)((ExprStmt)module.Body[1]).Expr;
            Assert.That(((LambdaExpr)call.Arguments[1].Value).Parameters, Is.Empty);
            Assert.That(((LambdaExpr)call.Arguments[2].Value).Parameters.Select(p => p.Name), Is.EqualTo(new[] { "a", "b" }));
        }

        [TestCase("x = lambda x, x: 1\n")]
        [TestCase("x = lambda x 1\n")]
        public void BadLambdaIsASyntaxError(string text)
        {
            var (_, diags) = Parse(text);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.ParSyntax));
        }

        [TestCase("def (x):\n    pass\n")]
        [TestCase("def f(x):\npass\n")]
        [TestCase("def f(x, x):\n    pass\n")]
        public void BadFunctionDefIsASyntaxError(string text)
        {
            var (_, diags) = Parse(text);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.ParSyntax));
        }

        [Test]
        public void SubscriptWithSeveralIndexes()
        {
            var (module, diags) = Parse("t = dict[Ref[\"Resource\"], fixed]\n");
            Assert.That(diags, Is.Empty);
            var index = (IndexExpr)((AssignStmt)module.Body.Single()).Value;
            Assert.That(index.Indices, Has.Count.EqualTo(2));
            Assert.That(index.Indices[0], Is.InstanceOf<IndexExpr>());
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
