using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary><c>@command</c> 선언과 커맨드 id (spec S6, spec-gaps G109).</summary>
    public class CommandDeclarationTests : LoaderTestBase
    {
        [Test]
        public void CommandIdIsModIdDotFunctionName()
        {
            Mod("core", defs: "@command\ndef Move(n):\n    return n\n" +
                              "@command(free=True)\ndef EndTurn():\n    return 0\n" +
                              "def helper(x):\n    return x\n");
            var result = LoadClean();
            Assert.That(result.Commands.Keys, Is.EqualTo(new[] { "core.EndTurn", "core.Move" }));
            Assert.That(result.Commands["core.Move"].Free, Is.False);
            Assert.That(result.Commands["core.EndTurn"].Free, Is.True);
            Assert.That(result.Commands["core.Move"].Function.Definition!.Name, Is.EqualTo("Move"));
        }

        [Test]
        public void TwoModsCanDeclareCommandsWithTheSameFunctionName()
        {
            Mod("core", defs: "@command(free=True)\ndef Move():\n    return 0\n");
            Mod("naval", defs: "@command(free=True)\ndef Move():\n    return 1\n", extra: ", dependencies=[\"core\"]");
            var result = LoadClean();
            Assert.That(result.Commands.Keys, Is.EqualTo(new[] { "core.Move", "naval.Move" }));
            Assert.That(result.Commands["naval.Move"].Mod, Is.EqualTo("naval"));
        }

        [TestCase("@other\ndef f():\n    return 0\n", "unknown decorator; only @command is supported")]
        [TestCase("@command(True)\ndef f():\n    return 0\n", "@command takes no positional arguments")]
        [TestCase("@command(cost=1)\ndef f():\n    return 0\n", "@command has no argument 'cost'")]
        [TestCase("@command(free=1)\ndef f():\n    return 0\n", "free= must be True or False")]
        [TestCase("@command\n@command\ndef f():\n    return 0\n", "a function can have only one decorator")]
        [TestCase("@command.free\ndef f():\n    return 0\n", "unknown decorator; only @command is supported")]
        public void BadDecoratorsAreLoadErrors(string defs, string message)
        {
            Mod("core", defs: defs);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ADuplicateCommandNameIsANameErrorNotASecondCommand()
        {
            Mod("core", defs: "@command\ndef Move():\n    return 0\n@command\ndef Move():\n    return 1\n");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.EvlNameTaken }));
            Assert.That(result.Commands, Is.Empty);
        }

        // ---- with preconditions: (G111) ----

        [Test]
        public void ACommandWrittenLikeTheSpecExampleLoads()
        {
            Mod("core", schema: "component(\"Moves\", left=field(int, default=1))\n" +
                                "deftype(\"Unit\", components=[\"Moves\"])\ndeftype(\"City\", components=[\"Moves\"])\n",
                defs: "@command(free=True)\ndef Move(unit: Ref[Unit], dest: Ref[City]):\n" +
                      "    with preconditions:\n        require(unit.Moves.left > 0, \"msg.no_moves\")\n    return 0\n");
            var result = LoadClean();
            Assert.That(result.Commands.Keys, Is.EqualTo(new[] { "core.Move" }));
        }

        [Test]
        public void PreconditionLocalsAreVisibleInTheBody()
        {
            Mod("core", defs: "@command\ndef Move(u):\n    with preconditions:\n        c = u + 1\n" +
                              "        require(c > 0, \"msg.neg\", c=c)\n    return c\n");
            LoadClean();
        }

        [TestCase("@command\ndef f(u):\n    x = 1\n    with preconditions:\n        require(u, \"m\")\n    return x\n",
            "'with' can only be the first statement of a @command function")]
        [TestCase("def f(u):\n    with preconditions:\n        require(u, \"m\")\n    return 0\n",
            "'with' can only be the first statement of a @command function")]
        [TestCase("@command\ndef f(u):\n    if u:\n        with preconditions:\n            require(u, \"m\")\n    return 0\n",
            "'with' can only be the first statement of a @command function")]
        [TestCase("@command\ndef f(u):\n    with other:\n        require(u, \"m\")\n    return 0\n",
            "a command can only open 'with preconditions:'")]
        [TestCase("@command\ndef f(u):\n    with preconditions:\n        x = 1\n        x += 1\n    return 0\n",
            "'with preconditions:' can only hold require(...) calls and 'name = value' assignments")]
        [TestCase("@command\ndef f(u):\n    with preconditions:\n        if u:\n            require(u, \"m\")\n    return 0\n",
            "'with preconditions:' can only hold require(...) calls and 'name = value' assignments")]
        [TestCase("@command\ndef f(u):\n    with preconditions:\n        print(u)\n    return 0\n",
            "'with preconditions:' can only hold require(...) calls and 'name = value' assignments")]
        [TestCase("@command\ndef f(u):\n    require(u, \"m\")\n    return 0\n",
            "require() can only be used as a statement inside 'with preconditions:'")]
        [TestCase("@command\ndef f(u):\n    with preconditions:\n        x = require(u, \"m\")\n    return 0\n",
            "require() can only be used as a statement inside 'with preconditions:'")]
        [TestCase("@command\ndef f(u):\n    with preconditions:\n        require(u)\n    return 0\n",
            "require() takes a condition and a message key, got 1 positional argument")]
        [TestCase("with preconditions:\n    x = 1\n",
            "'with' can only be the first statement of a @command function")]
        public void PreconditionsPlacementAndContentAreChecked(string defs, string message)
        {
            Mod("core", defs: defs);
            var result = Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdPreconditions);
            Assert.That(d.Message, Is.EqualTo(message), string.Join("\n", diags));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [TestCase("cost")]
        [TestCase("choose")]
        public void CostAndChooseAreNotSupportedYet(string name)
        {
            Mod("core", defs: "@command\ndef f(u):\n    with preconditions:\n        " + name + "(u, 1)\n    return 0\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnsupported));
            Assert.That(d.Message, Is.EqualTo("'" + name + "' is not supported yet"));
        }

        [Test]
        public void CommandsOfADisabledModAreNotListed()
        {
            Mod("broken", defs: "@command(free=True)\ndef Move():\n    return 0\nx = nope\n");
            var result = Load(out _);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "broken" }));
            Assert.That(result.Commands, Is.Empty);
        }
    }
}
