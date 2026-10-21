using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>컴포넌트·신호 이름은 그것이 보이는 모드 안에서 엔진 내장을 이긴다 (spec-gaps G230).</summary>
    public class SchemaNameShadowingTests : LoaderTestBase
    {
        private const string Units =
            "component(\"Moves\", left=field(int, default=0))\ndeftype(\"Unit\", components=[\"Moves\"])\n";

        [Test]
        public void AComponentNamedRandomIsItsConstructorOnlyWhereItIsSeen()
        {
            // banking이 random 컴포넌트를 선언한다. 의존하지 않는 scouts에서 random은 여전히 엔진의 random이다
            Mod("banking", schema: Units + "component(\"random\", n=field(int, default=0))\n",
                defs: "Unit(\"Clerk\")\n" +
                      "@command(free=True, acts_for=None)\ndef Count(u: Ref[Unit]):\n" +
                      "    u.Moves.left = 4 if random(n=4) == random(n=4) else 1\n");
            Mod("scouts", schema: "component(\"Steps\", left=field(int, default=0))\ndeftype(\"Walker\", components=[\"Steps\"])\n",
                defs: "Walker(\"Guide\")\n" +
                      "@command(free=True, acts_for=None)\ndef Roll(w: Ref[Walker]):\n    w.Steps.left = random.randint(5, 5)\n");
            var world = new World(LoadClean(), seed: 1);
            var u = world.Create("banking:Clerk");
            var w = world.Create("scouts:Guide");
            Assert.That(world.Submit("host", "banking.Count", new Dictionary<string, Value> { ["u"] = new EntityValue(u) }).Succeeded,
                Is.True);
            Assert.That(((IntValue)world.Get(u, world.Member("Moves", "left"))).Value, Is.EqualTo(4));
            Assert.That(world.Submit("host", "scouts.Roll", new Dictionary<string, Value> { ["w"] = new EntityValue(w) }).Succeeded,
                Is.True);
            Assert.That(((IntValue)world.Get(w, world.Member("Steps", "left"))).Value, Is.EqualTo(5));
        }

        [Test]
        public void AComponentNamedLikeAPureBuiltinWinsInDerivedFormulas()
        {
            // 예전에는 내장 floor가 생성자를 조용히 가렸다
            Mod("core", schema: "component(\"floor\", n=field(int, default=3))\n" +
                                "component(\"C\", total=derived(int, lambda c: 7 if floor(n=7) == floor(n=7) else 0))\n" +
                                "deftype(\"U\", components=[\"C\"])\n",
                defs: "U(\"A\")\n");
            var world = new World(LoadClean());
            var id = world.Create("core:A");
            Assert.That(((IntValue)world.Get(id, world.Member("C", "total"))).Value, Is.EqualTo(7));
        }

        [Test]
        public void AComponentNamedRequireShadowsThePreconditionBuiltin()
        {
            Mod("core", schema: "component(\"require\", n=field(int, default=0))\n",
                defs: "@command(free=True, acts_for=None)\ndef Move(u: bool):\n    with preconditions:\n        require(u, text(\"m\"))\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdPreconditions));
            Assert.That(d.Message,
                Is.EqualTo("'require' here is a Def type, component or signal this mod sees, so the built-in require() cannot be used"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ASignalNamedCommandShadowsTheDecorator()
        {
            Mod("core", schema: "signal(\"command\", n=int)\n", defs: "@command(free=True, acts_for=None)\ndef Move():\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Is.EqualTo("this mod sees a component or signal named 'command', so the built-in @command cannot be used here"));
            Assert.That(result.Commands, Is.Empty);
        }

        [Test]
        public void ReadingABuiltinThroughAComponentOfTheSameNameIsALoadError()
        {
            // 이 모드에서 random은 컴포넌트 생성자다: random.randint는 실행하면 반드시 실패한다
            Mod("core", schema: Units + "component(\"random\", n=field(int, default=0))\n",
                defs: "@command(free=True, acts_for=None)\ndef Roll(u: Ref[Unit]):\n    u.Moves.left = random.randint(1, 6)\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Message, Is.EqualTo("'random' here is the component constructor, which has no attribute 'randint'"));
            Assert.That(d.Hint, Is.EqualTo("this mod sees a component named 'random', so the built-in random is not available here; " +
                                           "rename the component (spec-gaps G230)"));
            Assert.That(d.Span.Line, Is.EqualTo(3));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ReadingAMemberOffAComponentOrSignalNameIsALoadError()
        {
            Mod("core", schema: Units + "signal(\"Hit\", amount=int)\n",
                defs: "@command(free=True, acts_for=None)\ndef Move(u: Ref[Unit]):\n    u.Moves.left = Moves.left\n" +
                      "def size():\n    return Hit.amount\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Message), Is.EqualTo(new[]
            {
                "'Moves' here is the component constructor, which has no attribute 'left'",
                "'Hit' here is the signal constructor, which has no attribute 'amount'",
            }));
            Assert.That(diags[0].Hint, Is.EqualTo("make a value first, like Moves(...), and read it from the entity or the signal"));
        }

        [Test]
        public void ALocalOrAModBindingOfTheSameNameIsNotAConstructor()
        {
            Mod("core", schema: Units + "component(\"random\", n=field(int, default=0))\n",
                defs: "def pick(Moves):\n    return Moves.left\n" +
                      "def roll():\n    random = [1]\n    return random.count\n");
            LoadClean();
        }

        [Test]
        public void AConstructorReachedThroughAValueNamesItselfAtRunTime()
        {
            // 로드 검사가 타입을 모르는 길([random][0]): 실행 오류가 무엇인지 말한다
            Mod("core", schema: Units + "component(\"random\", n=field(int, default=0))\n",
                defs: "Unit(\"Clerk\")\n@command(free=True, acts_for=None)\ndef Roll(u: Ref[Unit]):\n" +
                      "    u.Moves.left = [random][0].randint(1, 6)\n");
            var world = new World(LoadClean(), seed: 1);
            var u = world.Create("core:Clerk");
            var r = world.Submit("host", "core.Roll", new Dictionary<string, Value> { ["u"] = new EntityValue(u) });
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.Error!.Message, Does.Contain("'component constructor random' has no attribute 'randint'"));
        }

        [Test]
        public void AnUnrelatedModsComponentDoesNotShadowTheDecorator()
        {
            Mod("banking", schema: "component(\"command\", n=field(int, default=0))\n");
            Mod("scouts", defs: "@command(free=True, acts_for=None)\ndef Move():\n    return 0\n");
            var result = LoadClean();
            Assert.That(result.Commands.Count, Is.EqualTo(1));
        }
    }
}
