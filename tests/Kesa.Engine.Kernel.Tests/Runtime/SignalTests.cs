using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>신호의 선언, <c>@on</c> 핸들러, 신호 값 (spec S5, spec-gaps G145).</summary>
    public class SignalTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=10))\n" +
            "component(\"Out\", n=field(int, default=0), s=field(str, default=\"\"))\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Out\"])\n" +
            "deftype(\"Terrain\")\n" +
            "signal(\"Damaged\", target=Ref[\"Unit\"], amount=int)\n";

        private static string Diag(Diagnostic d) => d.Code + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        private IReadOnlyList<string> Errors(string defs, string schema = Schema)
        {
            Mod("combat", schema: schema, defs: defs);
            Load(out var diags);
            return diags.Select(Diag).ToList();
        }

        // ---- 선언 ----

        [Test]
        public void AHandlerIsRegisteredForItsSignal()
        {
            Mod("combat", schema: Schema, defs: "@on(Damaged)\ndef bleed(target):\n    target.Health.current -= 1\n" +
                                                "@on(Damaged)\ndef shout():\n    return None\n");
            var result = LoadClean();
            Assert.That(result.Handlers.Select(h => h.Signal.Name + " " + h.Function.Name), Is.EqualTo(new[] { "Damaged bleed", "Damaged shout" }));
            Assert.That(result.Commands, Is.Empty, "a handler is not a command");
        }

        [Test]
        public void SignalsShareTheNamespaceOfTypesAndComponents()
        {
            var errors = Errors("", Schema + "signal(\"Health\", n=int)\n");
            Assert.That(errors, Has.Some.StartWith(Codes.SchDuplicateName + " 'Health' is declared more than once"));
        }

        [TestCase("signal(\"Hit\", n=field(int))\n", Codes.EvlType)]
        [TestCase("signal(\"Hit\", f=Cond[\"Unit\"])\n", Codes.SchBadDeclaration)]
        [TestCase("signal(\"Hit\", where=Ref[\"Terrain\"])\n", Codes.SchBadDeclaration)]
        [TestCase("signal(\"Hit\", who=Ref[\"Nope\"])\n", Codes.SchUnknownName)]
        [TestCase("signal(\"field\")\n", Codes.SchBadDeclaration)]
        public void BadSignalDeclarationsAreLoadErrors(string signal, string code)
        {
            var errors = Errors("", Schema + signal);
            Assert.That(errors.Select(e => e.Split(' ')[0]), Has.Member(code), string.Join("\n", errors));
        }

        [Test]
        public void AnotherModsSignalNeedsADependency()
        {
            Mod("combat", schema: Schema);
            Mod("medic", defs: "@on(Damaged)\ndef heal(target):\n    return None\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.StartWith(Codes.SchNotVisible + " 'Damaged' belongs to mod 'combat'"));
        }

        [Test]
        public void AnotherModCanHandleASignalItDependsOn()
        {
            Mod("combat", schema: Schema);
            Mod("medic", defs: "@on(Damaged)\ndef heal(target):\n    target.Health.current += 1\n", extra: ", dependencies=[\"combat\"]");
            Assert.That(LoadClean().Handlers.Single().Mod, Is.EqualTo("medic"));
        }

        [TestCase("@on(Health)\ndef f():\n    return None\n", "@on needs a signal, not component 'Health'")]
        [TestCase("@on\ndef f():\n    return None\n", "@on needs the signal it handles")]
        [TestCase("@on()\ndef f():\n    return None\n", "@on needs the signal it handles")]
        [TestCase("@on(Damaged, Damaged)\ndef f():\n    return None\n", "@on takes one signal")]
        [TestCase("@on(Damaged, stge=\"core.x\")\ndef f():\n    return None\n", "@on has no argument 'stge' | did you mean 'stage'?")]
        [TestCase("@on(Damaged, acts_for=lambda target: target)\ndef f(target):\n    return None\n",
            "@on has no acts_for: nobody submits a handler, so there is no gate to check | take whom it is about from the signal's values")]
        [TestCase("@on(Damaged)\ndef f(amout):\n    return None\n", "signal 'Damaged' has no value 'amout' | did you mean 'amount'?")]
        [TestCase("@on(Damaged)\ndef f(amount: int):\n    return None\n", "a handler's parameter takes its type from the signal, so it has no type annotation")]
        [TestCase("@on(Damaged)\ndef f(amount=1):\n    return None\n", "a handler's parameter takes its type from the signal, so it has no default")]
        public void BadHandlersAreLoadErrors(string defs, string message)
        {
            var errors = Errors(defs);
            Assert.That(errors.Single(), Does.StartWith(Codes.CmdBadDecorator + " " + message));
        }

        [Test]
        public void ASignalTypoInAFunctionBodyIsALoadError()
        {
            var errors = Errors("@command(free=True, acts_for=None)\ndef Hit(u: Ref[Unit]):\n    d = Damgaed(target=u, amount=1)\n");
            Assert.That(errors.Single(), Does.Contain("'Damgaed'").And.Contain("Damaged"));
        }

        [Test]
        public void MakingASignalValueAtLoadTimeIsAnError()
        {
            var errors = Errors("x = Damaged(amount=1)\n");
            Assert.That(errors.Single(), Does.Contain("signal values are made when the game runs, inside a function"));
        }

        [Test]
        public void AHandlerHasNoPreconditions()
        {
            var errors = Errors("@on(Damaged)\ndef f(target):\n    with preconditions:\n        require(True, text(\"ok\"))\n");
            Assert.That(errors.Single(), Does.Contain("'with' can only be the first statement of a @command function"));
        }

        [Test]
        public void LogicCannotCallAHandler()
        {
            var errors = Errors("@on(Damaged)\ndef bleed(target):\n    target.Health.current -= 1\n" +
                                "@command(free=True, acts_for=None)\ndef Hit(u: Ref[Unit]):\n    bleed(u)\n");
            Assert.That(errors.Single(), Is.EqualTo(Codes.CmdWriteEffect +
                                                    " 'bleed' is a @on handler, so logic cannot call it or use it as a value" +
                                                    " | move the shared part into a plain function that the commands call (spec-gaps G118)"));
        }

        [Test]
        public void StubsDeclareSignals()
        {
            Mod("combat", schema: Schema, defs: "Unit(\"Scout\")\n");
            var result = LoadClean();
            string stub = Kesa.Engine.Stubs.StubGenerator.Generate("combat", result);
            // 엔티티는 그 Def 타입의 클래스로 적는다 (G207)
            Assert.That(stub, Does.Contain("class Damaged:\n    target: Unit\n    amount: int\n" +
                                           "    def __init__(self, /, *, target: Unit, amount: int) -> None: ...\n"));
        }

        // ---- 신호 값 ----

        private World Run(string body, out SubmitResult result)
        {
            Mod("combat", schema: Schema, defs: "Unit(\"Scout\")\n" +
                                                "@command(free=True, acts_for=None)\n" +
                                                "def Probe(u: Ref[Unit], v: Ref[Unit]):\n" + body);
            var world = new World(LoadClean());
            var u = world.Create("Scout");
            var v = world.Create("Scout");
            result = world.Submit("host", "combat.Probe",
                new Dictionary<string, Value> { ["u"] = new EntityValue(u), ["v"] = new EntityValue(v) });
            return world;
        }

        [TestCase("Damaged(target=u, amount=3) == Damaged(amount=3, target=u)", true)]
        [TestCase("Damaged(target=u, amount=3) == Damaged(target=u, amount=4)", false)]
        [TestCase("Damaged(target=u, amount=3) == Damaged(target=v, amount=3)", false)]
        [TestCase("Damaged(target=u, amount=3).amount == 3", true)]
        [TestCase("Damaged(target=u, amount=3).target == u", true)]
        public void SignalValuesCompareByContentAndReadByName(string expression, bool expected)
        {
            var world = Run("    u.Out.s = \"yes\" if " + expression + " else \"no\"\n", out var r);
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(((StrValue)world.Get(new EntityId(1), world.Member("Out", "s"))).Value, Is.EqualTo(expected ? "yes" : "no"));
        }

        [TestCase("Damaged(target=u)", "making the signal Damaged: 'amount' (int) is missing")]
        [TestCase("Damaged(target=u, amount=1, kind=2)", "making the signal Damaged: signal Damaged has no value 'kind'")]
        [TestCase("Damaged(target=u, amout=1)", "making the signal Damaged: 'amount' (int) is missing")]
        [TestCase("Damaged(target=u, amount=\"3\")", "making the signal Damaged: 'amount' must be int, not str")]
        [TestCase("Damaged(target=3, amount=1)", "making the signal Damaged: 'target' must be")]
        [TestCase("Damaged(u, 1)", "Damaged(...) takes values by name, like Damaged(target=..., amount=...)")]
        [TestCase("Damaged(target=u, amount=1).amout", "signal Damaged has no value 'amout'")]
        public void BadSignalValuesAreScriptErrors(string expression, string message)
        {
            Run("    d = " + expression + "\n", out var r);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
        }

        // ---- emit (G146) ----

        [Test]
        public void ASignalWithoutHandlersIsDoneWhenTheSubmitEnds()
        {
            var world = Run("    emit(Damaged(target=u, amount=3))\n    emit(Damaged(target=v, amount=1))\n", out var r);
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(r.Reactions, Is.Empty);
            Assert.That(world.PendingSignalCount, Is.EqualTo(0));
            Assert.That(() => world.Save(), Throws.Nothing);
        }

        [Test]
        public void AFailedCommandTakesBackItsSignals()
        {
            var world = Run("    emit(Damaged(target=u, amount=3))\n    d = Damaged(target=u)\n", out var r);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(world.PendingSignalCount, Is.EqualTo(0), "a command that fails sends no signal (C8)");
        }

        [TestCase("emit(3)", "emit() takes a signal value, not int")]
        [TestCase("emit()", "emit() takes one signal value, got 0 positional and 0 named arguments")]
        [TestCase("emit(Damaged(target=u, amount=1), Damaged(target=u, amount=2))", "emit() takes one signal value, got 2 positional")]
        [TestCase("emit(signal=Damaged(target=u, amount=1))", "emit() takes one signal value, got 0 positional and 1 named")]
        public void BadEmitCallsAreScriptErrors(string call, string message)
        {
            var world = Run("    " + call + "\n", out var r);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain(message));
            Assert.That(world.PendingSignalCount, Is.EqualTo(0));
        }

        [TestCase("def f(u):\n    n = [emit(Damaged(target=u, amount=1))]\n")]
        [TestCase("def f(u):\n    if emit(Damaged(target=u, amount=1)):\n        pass\n")]
        [TestCase("g = lambda u: emit(Damaged(target=u, amount=1))\n")]
        [TestCase("@command(free=True, acts_for=None)\ndef F(u: Ref[Unit]):\n    with preconditions:\n" +
                  "        x = emit(Damaged(target=u, amount=1))\n    pass\n")]
        public void EmitInsideAnExpressionIsALoadError(string logic)
        {
            var errors = Errors(logic);
            Assert.That(errors, Has.Some.StartWith(Codes.CmdWriteEffect + " emit() sends a signal, so it can only be called as a statement"),
                string.Join("\n", errors));
        }

        [Test]
        public void AFunctionThatEmitsCannotBeCalledInAnExpression()
        {
            var errors = Errors("def warn(u):\n    emit(Damaged(target=u, amount=1))\n    return 1\n" +
                                "@command(free=True, acts_for=None)\ndef F(u: Ref[Unit]):\n    n = warn(u) + 1\n");
            Assert.That(errors, Has.Some.StartWith(Codes.CmdWriteEffect + " 'warn' changes the world"), string.Join("\n", errors));
        }

        [Test]
        public void AFunctionThatEmitsCannotBeADerivedFunction()
        {
            var errors = Errors("Unit(\"Scout\")\n", Schema +
                "def warn(h):\n    emit(Damaged(target=h.entity, amount=1))\n    return 1\n" +
                "component(\"Alarm\", value=derived(int, warn))\n");
            Assert.That(errors, Has.Some.Contains("'warn'"), string.Join("\n", errors));
        }

        [Test]
        public void EmitReachedThroughAPureCallIsARuntimeError()
        {
            var world = Run("    f = warn\n    n = f(u) + 1\n" +
                            "def warn(u):\n    emit(Damaged(target=u, amount=1))\n    return 1\n", out var r);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("emit() sends a signal, so it cannot be called here"));
            Assert.That(world.PendingSignalCount, Is.EqualTo(0));
        }

        [Test]
        public void ASavedSignalValueIsCheckedAgainstTheCurrentDeclaration()
        {
            Mod("combat", schema: Schema, defs: "Unit(\"Scout\")\n" +
                                                "@command(free=True, acts_for=None)\n" +
                                                "def Probe(u: Ref[Unit]):\n" +
                                                "    d = Damaged(target=u, amount=3)\n" +
                                                "    ask(u, \"Go\", choices=[\"ok\"])\n" +
                                                "    u.Out.n = d.amount\n");
            var world = new World(LoadClean());
            var u = world.Create("Scout");
            world.Submit("host", "combat.Probe", new Dictionary<string, Value> { ["u"] = new EntityValue(u) });
            var save = world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"signal\": {\n"));

            Mod("combat", schema: Schema.Replace("amount=int", "amount=str"), defs: "Unit(\"Scout\")\n" +
                "@command(free=True, acts_for=None)\n" +
                "def Probe(u: Ref[Unit]):\n" +
                "    d = Damaged(target=u, amount=3)\n" +
                "    ask(u, \"Go\", choices=[\"ok\"])\n" +
                "    u.Out.n = d.amount\n");
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV002 flow 1 (combat.Probe): signal Damaged: 'amount' must be str, not int",
            }));
        }
    }
}
