using Kesa.Engine.Bridge;
using Kesa.Engine.ConsoleHost;
using NUnit.Framework;

namespace Kesa.Engine.ConsoleHost.Tests
{
    public class CommandTextTests
    {
        [Test]
        public void ValuesArePythonLiteralsAndEntityIds()
        {
            var typed = CommandText.Parse(
                "red-team: coup.Draw(unit=#12, cells=[[1, 2], [3, -4]], share=0.25, name=\"a\\\"b\", tags={'x': True, 2: None}, )",
                out string error);
            Assert.That(typed, Is.Not.Null, error);
            Assert.That(typed!.Seat, Is.EqualTo("red-team"));
            Assert.That(typed.Name, Is.EqualTo("coup.Draw"));
            Assert.That(BridgeJson.Write(typed.Arguments["unit"]), Is.EqualTo("{\"entity\":12}"));
            Assert.That(BridgeJson.Write(typed.Arguments["cells"]), Is.EqualTo("[[1,2],[3,-4]]"));
            Assert.That(BridgeJson.Write(typed.Arguments["share"]), Is.EqualTo("{\"fixed\":250000}"));
            Assert.That(BridgeJson.Write(typed.Arguments["name"]), Is.EqualTo("\"a\\\"b\""));
            Assert.That(BridgeJson.Write(typed.Arguments["tags"]), Is.EqualTo("{\"dict\":[[\"x\",true],[2,null]]}"));
        }

        [TestCase("Start()", null, "Start")]
        [TestCase("  skirmish.Start ( ) ", null, "skirmish.Start")]
        public void ASeatIsOptional(string line, string? seat, string name)
        {
            var typed = CommandText.Parse(line, out string error);
            Assert.That(typed, Is.Not.Null, error);
            Assert.That(typed!.Seat, Is.EqualTo(seat));
            Assert.That(typed.Name, Is.EqualTo(name));
        }

        [TestCase("Move(dir=north)", "'north' is not a value; quote a string")]
        [TestCase("Move(a=1, a=2)", "'a' is given twice")]
        [TestCase("Move(a=1.1234567)", "at most 6 decimal places")]
        [TestCase("Move(a=#x)", "an entity is # and its id")]
        [TestCase("Move(a=\"x)", "no closing")]
        [TestCase("Move(a=1) 2", "unexpected '2'")]
        [TestCase("Move(a=99999999999999999999)", "too large")]
        public void MistakesAreExplained(string line, string message)
        {
            Assert.That(CommandText.Parse(line, out string error), Is.Null);
            Assert.That(error, Does.Contain(message));
        }
    }
}
