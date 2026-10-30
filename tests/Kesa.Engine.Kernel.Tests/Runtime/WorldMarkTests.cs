using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 표시 (spec-gaps G395·G396·G397): <c>@world_marks</c>는 매개변수 없는 화면 함수이고 <c>mark(그림, at=, click=[동작, 인자], key=)</c>의
    /// 리스트를 돌려준다. 영수증은 화면 함수와 같다.
    /// </summary>
    public class WorldMarkTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Pos\"])\n" +
            "ui_state(\"Sel\", unit=ref(\"Unit\", optional=True))\n";

        private const string ScoutSchema =
            "component(\"Spot\", x=field(int, default=0))\n" +
            "deftype(\"Scout\", components=[\"Spot\"])\n" +
            "ui_state(\"Pick\", unit=ref(\"Scout\", optional=True))\n";

        private const string Defs =
            "Unit(\"Foot\")\n" +
            "@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Sel.unit = u\n" +
            "@ui_action\ndef go(to: int):\n    pass\n" +
            "@world_marks\ndef moves():\n" +
            "    u = ui.Sel.unit\n" +
            "    if u is None:\n        return None\n" +
            "    return [mark(\"spot\", at=at(\"hex\", u.Pos.x + 1), click=[\"core.go\", {\"to\": u.Pos.x + 1}],\n" +
            "                 inputs={\"secondary\": [\"core.go\", {\"to\": 9}]}),\n" +
            "            mark(look(\"ring\", r=2), at=onto(u), key=\"ring\")]\n";

        private World _world = null!;
        private WorldView _view = null!;
        private UiState _ui = null!;
        private EntityId _foot;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _view = _world.ViewAs("alice");
            _ui = _world.NewUiState();
            _foot = _world.Create("core:Foot");
        }

        private void Pick() =>
            Assert.That(_view.Act(_ui, "core.pick", new Dictionary<string, Value> { ["u"] = new EntityValue(_foot) }).Succeeded, Is.True);

        private IReadOnlyList<Value> Marks() => ((ListValue)_view.WorldMarks(_ui, "core.moves").Value).Items;

        [Test]
        public void TheFunctionsAreListedById()
        {
            Assert.That(_world.WorldMarkFunctions, Is.EqualTo(new[] { "core.moves" }));
        }

        [Test]
        public void AMarkHoldsItsLookPlaceClickAndKey()
        {
            Assert.That(Marks(), Is.Empty, "None is no marks");
            Pick();
            var marks = Marks().Cast<MarkValue>().ToList();
            Assert.That(marks.Count, Is.EqualTo(2));
            Assert.That((marks[0].Look.Name, marks[0].Look.Keys.Count, marks[0].Where.Name), Is.EqualTo(("spot", 0, "hex")));
            Assert.That((marks[0].Action, ((IntValue)marks[0].Arguments!["to"]).Value, marks[0].Key), Is.EqualTo(("core.go", 1L, (Value?)null)));
            var secondary = marks[0].Inputs.Single();
            Assert.That((secondary.Key, secondary.Value.Action, ((IntValue)secondary.Value.Arguments["to"]).Value), Is.EqualTo(("secondary", "core.go", 9L)));
            Assert.That(marks[1].Inputs, Is.Empty);
            Assert.That((marks[1].Look.Name, marks[1].Where.Kind, marks[1].Action, ((StrValue)marks[1].Key!).Value),
                Is.EqualTo(("ring", WhereKind.Onto, (string?)null, "ring")));
        }

        [Test]
        public void TheReceiptSeesTheUiAndTheGame()
        {
            var receipt = _view.WorldMarks(_ui, "core.moves");
            Pick();
            Assert.That(receipt.Changed(_world), Is.True, "it read ui.Sel");
            receipt = _view.WorldMarks(_ui, "core.moves");
            Assert.That(receipt.Changed(_world), Is.False);
            _world.Set(_foot, _world.Member("Pos", "x"), new IntValue(4));
            Assert.That(receipt.Changed(_world), Is.True, "it read Pos");
        }

        [TestCase("[mark(\"a\", at=at(0), click=[\"core.og\", {}])]", "there is no @ui_action 'core.og'", "core.go")]
        [TestCase("[mark(\"a\", at=at(0), click=[\"core.Go\", {}])]", "there is no @ui_action 'core.Go'", "is a command")]
        [TestCase("[mark(\"a\", at=at(0), click=\"core.go\")]", "click= of mark() takes [the @ui_action, a dict of its arguments], not str", null)]
        [TestCase("[mark(\"a\", at=at(0), inputs=[\"core.go\", {}])]", "inputs= of mark() takes a dict from input names", null)]
        [TestCase("[mark(\"a\", at=at(0), inputs={1: [\"core.go\", {}]})]", "inputs= of mark() is keyed by input names (str), not int", null)]
        [TestCase("[mark(\"a\", at=at(0), inputs={\"secondary\": [\"core.og\", {}]})]",
            "there is no @ui_action 'core.og' for inputs[\"secondary\"]= of mark()", "core.go")]
        [TestCase("[mark(\"a\")]", "mark() needs at=", null)]
        [TestCase("[mark(\"a\", at=at(0), size=2)]", "mark() has no argument 'size'", null)]
        [TestCase("[look(\"a\")]", "'scouts.bad' returned a list holding look", null)]
        [TestCase("mark(\"a\", at=at(0))", "'scouts.bad' returned mark", null)]
        public void AWrongMarkFailsTheCall(string body, string message, string? hint)
        {
            Mod("scouts", schema: "", defs: "@command(free=True, acts_for=None)\ndef Go():\n    pass\n" +
                                            "@ui_action\ndef go(to: int):\n    pass\n" +
                                            "@world_marks\ndef bad():\n    return " + body.Replace("core.", "scouts.") + "\n");
            var world = new World(LoadClean());
            var e = Assert.Throws<WorldException>(() => world.ViewAs("alice").WorldMarks(world.NewUiState(), "scouts.bad"))!;
            Assert.That(e.Message, Does.Contain(message.Replace("core.", "scouts.")));
            if (hint != null) Assert.That(e.Hint, Does.Contain(hint.Replace("core.", "scouts.")));
        }

        [TestCase("@world_marks(1)\ndef m():\n    return []\n", "@world_marks takes no arguments")]
        [TestCase("@world_marks\ndef m(u):\n    return []\n", "a @world_marks function takes no parameters")]
        [TestCase("@world_marks\ndef m():\n    for u in query(Scout):\n        u.Spot.x = 1\n    return []\n", "cannot run on a screen: it changes the world")]
        [TestCase("@world_marks\ndef m():\n    ui.Pick.unit = None\n    return []\n", "cannot run on a screen: it changes UI state")]
        [TestCase("@command(free=True, acts_for=None)\ndef Go():\n    m = mark(\"a\", at=at(0))\n",
            "the @command 'Go' makes a look or a place (look(), at(), onto(), between(), mark() or fx())")]
        public void LoadingChecksTheShape(string defs, string message)
        {
            Mod("scouts", schema: ScoutSchema, defs: defs);
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator).Or.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Does.Contain(message));
        }
    }
}
