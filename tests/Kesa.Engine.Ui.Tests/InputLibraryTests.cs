using System.Linq;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary><c>.input.xml</c>의 이름과 기본 키 (G417·G423).</summary>
    public class InputLibraryTests
    {
        private static readonly string[] Keys = { "R", "F5", "Digit1", "Enter", "LeftShift" };

        private static LookSource Source(string mod, string text, string file = "keys.input.xml") =>
            new LookSource(mod, mod + "/" + file, "<Inputs>\n" + text + "\n</Inputs>");

        private static InputLibrary Load(params LookSource[] sources) => InputLibrary.Load(sources, Keys);

        [Test]
        public void KeysAreModifiersAndOneKeyJoinedByPlus()
        {
            var library = Load(Source("tanks",
                "<Input name=\"rotate\" key=\"r\" />\n" +
                "<Input name=\"queue\" key=\"shift+secondary\" />\n" +
                "<Input name=\"save\" key=\"ALT + CTRL + F5\" />"));
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            Assert.That(library.Inputs["rotate"].Key.ToString(), Is.EqualTo("R"));
            Assert.That(library.Inputs["queue"].Key.ToString(), Is.EqualTo("SHIFT + secondary"));
            Assert.That(library.Inputs["queue"].Key.IsPointer, Is.True);
            Assert.That(library.Inputs["save"].Key.Modifiers, Is.EqualTo(new[] { InputModifier.Ctrl, InputModifier.Alt }));
            Assert.That(library.Inputs["save"].Place, Is.EqualTo("tanks/keys.input.xml:4"));
        }

        [TestCase("Digit 1", "'Digit 1' is not a key name")]
        [TestCase("Digt1", "'Digt1' is not a key name (did you mean 'Digit1'?)")]
        [TestCase("SHIFT + SHIFT + R", "SHIFT is written twice")]
        [TestCase("SHIFT + CTRL + ALT + R", "at most 2 modifiers")]
        [TestCase("CTRL + SHIFT", "ends with the modifier SHIFT")]
        [TestCase("R + SHIFT", "'R' is not a modifier (can be: SHIFT, CTRL, ALT; the key comes last)")]
        [TestCase("SHFT + R", "'SHFT' is not a modifier (did you mean 'SHIFT'?)")]
        [TestCase("SHIFT +", "joined by '+'")]
        public void AWrongKeyIsAProblemAndTheInputIsLeftOut(string key, string message)
        {
            var library = Load(Source("tanks", "<Input name=\"rotate\" key=\"" + key + "\" />"));
            Assert.That(library.Inputs, Is.Empty);
            Assert.That(library.Problems.Single().ToString(), Does.Contain(message));
        }

        [Test]
        public void AKeyWithMoreModifiersCoversTheSameKeyWithFewer()
        {
            var library = Load(Source("tanks",
                "<Input name=\"spin\" key=\"R\" />\n<Input name=\"back\" key=\"SHIFT + R\" />\n<Input name=\"far\" key=\"SHIFT + CTRL + R\" />\n" +
                "<Input name=\"alt\" key=\"ALT + R\" />\n<Input name=\"save\" key=\"SHIFT + F5\" />"));
            InputChord Of(string name) => library.Inputs[name].Key;
            Assert.That(Of("spin").IsCoveredBy(Of("back")), Is.True);
            Assert.That(Of("back").IsCoveredBy(Of("far")), Is.True);
            Assert.That(Of("back").IsCoveredBy(Of("back")), Is.False);
            Assert.That(Of("back").IsCoveredBy(Of("spin")), Is.False);
            Assert.That(Of("alt").IsCoveredBy(Of("far")), Is.False, "different modifiers");
            Assert.That(Of("spin").IsCoveredBy(Of("save")), Is.False, "different keys");
        }

        [Test]
        public void AHandlerOfAnUndeclaredInputIsAProblem()
        {
            var library = Load(Source("tanks", "<Input name=\"rotate\" key=\"R\" />"));
            var problems = library.CheckHandlers(new[]
            {
                new BridgeWorldInput("rotate", "Unit", "tanks.turn", "tanks"),
                new BridgeWorldInput("secondary", "square", "tanks.go", "tanks"),
                new BridgeWorldInput("rotat", "Unit", "planes.turn", "planes"),
            });
            Assert.That(problems.Select(p => p.ToString()), Is.EqualTo(new[]
            {
                "planes.turn: no .input.xml declares the input 'rotat' (did you mean 'rotate'?), so no key reaches it (@world_input(\"rotat\", \"Unit\"))",
            }));
        }

        [Test]
        public void TheLaterModWinsAndTheSameModTwiceIsAProblem()
        {
            var library = Load(
                Source("tanks", "<Input name=\"rotate\" key=\"R\" />\n<Input name=\"rotate\" key=\"F5\" />"),
                Source("tanks", "<Input name=\"ignored\" key=\"R\" />", "notes.xml"),
                Source("planes", "<Input name=\"rotate\" key=\"Enter\" />"));
            Assert.That(library.Problems.Single().ToString(), Does.Contain("already declared at tanks/keys.input.xml:2"));
            Assert.That((library.Inputs.Count, library.Inputs["rotate"].Key.Key, library.Inputs["rotate"].Mod), Is.EqualTo((1, "Enter", "planes")));
        }

        [Test]
        public void BuiltInsAreNotDeclaredAndOtherShapesAreProblems()
        {
            var library = Load(Source("tanks",
                "<Input name=\"primary\" key=\"R\" />\n" +
                "<Input name=\"rotate\" />\n" +
                "<Input name=\"turn\" key=\"R\" when=\"down\" />\n" +
                "<Look name=\"x\" />"));
            Assert.That(library.Problems.Select(p => p.ToString()), Has.Some.Contain("'primary' is a built-in input"));
            Assert.That(library.Problems.Select(p => p.ToString()), Has.Some.Contain("<Input> needs a key"));
            Assert.That(library.Problems.Select(p => p.ToString()), Has.Some.Contain("takes only name, key and kind, got 'when'"));
            Assert.That(library.Problems.Select(p => p.ToString()), Has.Some.Contain("got <Look>"));
            Assert.That(library.Inputs.Keys, Is.EqualTo(new[] { "turn" }));
        }
    }
}
