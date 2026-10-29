using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 그림과 놓을 곳의 함수 칸 (spec-gaps G388·G389·G393): <c>Look</c>·<c>Where</c> 칸은 화면 함수 등급이라 UI와 질문을 읽어도 되고 쓰면 안 된다.
    /// <c>Look</c>은 글자만 적어도 된다. 게임 코드는 그 칸을 읽지 못한다.
    /// </summary>
    public class LookSlotTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\n" +
            "deftype(\"Unit\", components=[\"Hp\"], look=const(Look[\"Unit\"]), where=const(Where[\"Unit\"]))\n" +
            "ui_state(\"Sel\", unit=ref(\"Unit\", optional=True))\n";

        [Test]
        public void ALookSlotTakesANameOrAScreenFunction()
        {
            Mod("core", schema: Schema,
                defs: "def picked(u):\n    return look(\"infantry\", picked=ui.Sel.unit == u, asked=len(questions()) > 0)\n" +
                      "def spot(u):\n    return u.Hp.v\n" +
                      "Unit(\"Foot\", look=picked, where=spot)\n" +
                      "Unit(\"Tree\", look=\"pine\", where=spot)\n");
            LoadClean();
        }

        [Test]
        public void AnEmptyNameIsNotALook()
        {
            Mod("core", schema: Schema, defs: "Unit(\"Foot\", look=\"\", where=lambda u: 0)\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Does.Contain("the name of a look cannot be empty"));
        }

        [TestCase("def hurt(u):\n    u.Hp.v -= 1\n    return \"pine\"\n", "changes the world")]
        [TestCase("def hurt(u):\n    submit(\"Go\")\n    return \"pine\"\n", "hurt")]
        [TestCase("def hurt(u):\n    return look(\"pine\", n=random.randint(0, 1))\n", "random numbers")]
        public void ALookSlotRefusesWhatAScreenFunctionCannotDo(string function, string message)
        {
            Mod("core", schema: Schema,
                defs: "@command(free=True, acts_for=None)\ndef Go():\n    pass\n" + function +
                      "Unit(\"Foot\", look=hurt, where=lambda u: 0)\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Does.Contain("Look[\"Unit\"]"));
            Assert.That(d.Message, Does.Contain(message));
        }

        [Test]
        public void GameCodeCannotReadALookSlot()
        {
            Mod("core", schema: Schema,
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: 0)\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    x = u.Unit.look\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Does.StartWith("the @command 'Go'"));
        }

        [Test]
        public void AnotherSlotWithTheSameNameIsStillGameData()
        {
            Mod("core", schema: Schema + "component(\"Order\", where=field(int, default=0))\ndeftype(\"Job\", components=[\"Order\"])\n",
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: 0)\n" +
                      "@command(free=True, acts_for=None)\ndef Go(j: Ref[Job]):\n    j.Order.where = j.Order.where + 1\n");
            LoadClean();
        }

        [Test]
        public void ALookValueCarriesItsNameAndPlainKeysInOrder()
        {
            Mod("core", schema: Schema,
                defs: "def picked(u):\n    return look(\"infantry\", team=\"#cc3333\", hurt=u.Hp.v < 3, gear=[u], scale=0.8)\n" +
                      "Unit(\"Foot\", look=picked, where=lambda u: 0)\n" +
                      "def show(u: Ref[Unit]):\n    return u.Unit.look(u)\n");
            var world = new World(LoadClean());
            var u = world.Create("core:Foot");
            var value = (LookValue)world.ViewAs("alice").Call(world.NewUiState(), "core.show", new EntityValue(u)).Value;
            Assert.That(value.Name, Is.EqualTo("infantry"));
            Assert.That(value.Keys.Keys, Is.EqualTo(new[] { "gear", "hurt", "scale", "team" }));
            Assert.That(((EntityValue)((ListValue)value.Keys["gear"]).Items.Single()).Id, Is.EqualTo(u));
        }

        [TestCase("look(\"\")", "non-empty str")]
        [TestCase("look(\"a\", \"b\")", "takes the name of a look")]
        [TestCase("look(\"a\", f=show)", "f= of look() cannot hold")]
        public void LookRefusesAMalformedValue(string call, string message)
        {
            Mod("core", schema: Schema,
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: 0)\n" +
                      "def show():\n    return " + call + "\n");
            var world = new World(LoadClean());
            var e = Assert.Throws<WorldException>(() => world.ViewAs("alice").Call("core.show"))!;
            Assert.That(e.Message, Does.Contain(message));
        }

        [Test]
        public void ALookCannotBeKept()
        {
            Mod("core", schema: Schema.Replace("v=field(int, default=3)", "v=field(int, default=3), seen=derived(str, lambda h: look(\"a\"))"),
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: 0)\n");
            var world = new World(LoadClean());
            var u = world.Create("core:Foot");
            var e = Assert.Throws<WorldException>(() => world.Get(u, world.Member("Hp", "seen")))!;
            Assert.That(e.Message, Does.EndWith("expected str, found look"));
        }
    }
}
