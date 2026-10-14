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
