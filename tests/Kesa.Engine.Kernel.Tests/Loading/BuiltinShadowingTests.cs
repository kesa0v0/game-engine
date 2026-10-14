using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>엔진이 주는 이름은 덮어쓸 수 있는 내장이다 (spec-gaps G112).</summary>
    public class BuiltinShadowingTests : LoaderTestBase
    {
        [Test]
        public void ARuntimeBuiltinCanBeShadowedInsideTheMod()
        {
            Mod("core", schema: "def len(x):\n    return 7\n" +
                                "component(\"C\", total=derived(int, lambda c: len([1, 2])))\n" +
                                "deftype(\"U\", components=[\"C\"])\n",
                defs: "U(\"A\")\n");
            var world = new World(LoadClean());
            var id = world.Create("A");
            Assert.That(((IntValue)world.Get(id, world.Member("C", "total"))).Value, Is.EqualTo(7));
        }

        [Test]
        public void AnUnrelatedModsNamesDoNotBlockTopLevelNames()
        {
            // banking과 scouts는 서로 의존하지 않는다: 함께 설치했다는 이유로 scouts가 깨지면 안 된다
            Mod("banking", schema: "component(\"Score\", n=field(int, default=0))\n");
            Mod("scouts", defs: "Score = 1\n");
            LoadClean();
        }

        [Test]
        public void ADependencysNamesAndEngineNamesCanBeShadowed()
        {
            Mod("banking", schema: "component(\"Score\", n=field(int, default=0))\n");
            Mod("scouts", defs: "Score = 1\nlist = [1]\nfield = 2\n", extra: ", dependencies=[\"banking\"]");
            LoadClean();
        }

        [Test]
        public void AModHelperNamedLikeAPreconditionBuiltinIsAnOrdinaryFunction()
        {
            Mod("core", schema: "def cost(x):\n    return x * 2\n" +
                                "component(\"C\", base=field(int, default=1), total=derived(int, lambda c: cost(c.base)))\n" +
                                "deftype(\"U\", components=[\"C\"])\n",
                defs: "U(\"A\")\n");
            var world = new World(LoadClean());
            var id = world.Create("A");
            Assert.That(((IntValue)world.Get(id, world.Member("C", "total"))).Value, Is.EqualTo(2));
        }

        [Test]
        public void AShadowedRequireCannotBeUsedInPreconditions()
        {
            Mod("core", defs: "@command\ndef Move(u):\n    with preconditions:\n        require(u, \"m\")\n    return 0\n" +
                              "def require(x, y):\n    return x\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdPreconditions));
            Assert.That(d.Message, Is.EqualTo("'require' here is this mod's own name, so the built-in require() cannot be used"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ALocalNamedRequireShadowsItToo()
        {
            Mod("core", defs: "@command\ndef Move(u):\n    with preconditions:\n        require = u\n        require(u, \"m\")\n    return 0\n");
            Load(out var diags);
            Assert.That(diags.Single().Message,
                Is.EqualTo("'require' here is a local variable, so the built-in require() cannot be used"));
        }

        [Test]
        public void AShadowedCommandCannotBeUsedAsTheDecorator()
        {
            Mod("core", defs: "command = 1\n@command\ndef Move():\n    return 0\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Is.EqualTo("this mod defines its own 'command', so the built-in @command cannot be used here"));
            Assert.That(result.Commands, Is.Empty);
        }

        [Test]
        public void AShadowedInheritIsAnOrdinaryValue()
        {
            // 내장 inherit이었다면 부모가 없어 오류다. 덮어쓴 뒤로는 모드의 값 5다
            Mod("core", schema: "deftype(\"T\", n=const(int, default=0))\n", defs: "inherit = 5\nT(\"A\", n=inherit)\n");
            LoadClean();
        }
    }
}
