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
                      "def spot(u):\n    return None if u.Hp.v == 0 else at(\"hex\", [u.Hp.v, 0])\n" +
                      "Unit(\"Foot\", look=picked, where=spot)\n" +
                      "Unit(\"Tree\", look=\"pine\", where=spot)\n");
            LoadClean();
        }

        [Test]
        public void AnEmptyNameIsNotALook()
        {
            Mod("core", schema: Schema, defs: "Unit(\"Foot\", look=\"\", where=lambda u: at(0))\n");
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
                      "Unit(\"Foot\", look=hurt, where=lambda u: at(0))\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Does.Contain("Look[\"Unit\"]"));
            Assert.That(d.Message, Does.Contain(message));
        }

        [Test]
        public void GameCodeCannotReadALookSlot()
        {
            Mod("core", schema: Schema,
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    x = u.Unit.look\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdUiEffect));
            Assert.That(d.Message, Does.StartWith("the @command 'Go'"));
        }

        [TestCase("@command(free=True, acts_for=None)\ndef Go(u: Ref[Unit]):\n    p = spot(u)\n    u.Hp.v = 1\n",
            "the @command 'Go' makes a look or a place (look(), at(), onto(), between(), mark() or fx()), so it cannot run as game code")]
        [TestCase("Goal(\"G\", ok=lambda g: spot(g) is not None)\n",
            "'ok': '<lambda>' makes a look or a place (at core/defs/defs.py:4:28), but Cond[\"Goal\"] holds functions that run as game code, " +
            "and looks and places only go to the screen")]
        public void GameCodeCannotMakeALookOrAPlace(string defs, string message)
        {
            // 멈춘 흐름에 남으면 저장할 수 없는 값이다
            Mod("core", schema: Schema + "deftype(\"Goal\", ok=const(Cond[\"Goal\"]))\n",
                defs: "def spot(u):\n    return at(\"hex\", 1)\n" + "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" + defs);
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo(message));
        }

        [Test]
        public void AnotherSlotWithTheSameNameIsStillGameData()
        {
            Mod("core", schema: Schema + "component(\"Order\", where=field(int, default=0))\ndeftype(\"Job\", components=[\"Order\"])\n",
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" +
                      "@command(free=True, acts_for=None)\ndef Go(j: Ref[Job]):\n    j.Order.where = j.Order.where + 1\n");
            LoadClean();
        }

        [Test]
        public void ALookValueCarriesItsNameAndPlainKeysInOrder()
        {
            Mod("core", schema: Schema,
                defs: "def picked(u):\n    return look(\"infantry\", team=\"#cc3333\", hurt=u.Hp.v < 3, gear=[u], scale=0.8)\n" +
                      "Unit(\"Foot\", look=picked, where=lambda u: at(0))\n" +
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
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" +
                      "def show():\n    return " + call + "\n");
            var world = new World(LoadClean());
            var e = Assert.Throws<WorldException>(() => world.ViewAs("alice").Call("core.show"))!;
            Assert.That(e.Message, Does.Contain(message));
        }

        [Test]
        public void PlacesNameASpaceAnEntityOrTwoPlaces()
        {
            Mod("core", schema: Schema,
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" +
                      "def show(u: Ref[Unit]):\n    return between(at(\"hex\", [1, 2]), onto(u, \"deck\"))\n" +
                      "def bare(u: Ref[Unit]):\n    return [at(u), onto(u)]\n");
            var world = new World(LoadClean());
            var u = world.Create("core:Foot");
            var view = world.ViewAs("alice");
            var place = (WhereValue)view.Call("core.show", new EntityValue(u)).Value;
            Assert.That(place.Kind, Is.EqualTo(WhereKind.Between));
            Assert.That((place.First!.Kind, place.First.Name), Is.EqualTo((WhereKind.At, "hex")));
            Assert.That((place.Second!.Kind, place.Second.Name, ((EntityValue)place.Second.Target!).Id), Is.EqualTo((WhereKind.Onto, "deck", u)));
            var bare = ((ListValue)view.Call("core.bare", new EntityValue(u)).Value).Items.Cast<WhereValue>().ToArray();
            Assert.That(bare.Select(b => (b.Kind, b.Name)), Is.EqualTo(new[] { (WhereKind.At, (string?)null), (WhereKind.Onto, (string?)null) }));
            Assert.That(((EntityValue)bare[0].Target!).Id, Is.EqualTo(u));
        }

        [Test]
        public void AnEntityTheViewerCannotSeeIsHiddenInPlace()
        {
            // 화면 쪽에서 못 보는 엔티티는 읽는 순간 가려진 값이 되고, 그림과 자리는 그것을 그대로 싣는다
            Mod("core", schema: Schema.Replace("components=[\"Hp\"]", "components=[\"Hp\", \"Aim\"]") +
                                "component(\"Aim\", target=ref(\"Unit\", optional=True))\n",
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" +
                      "@view_rule(Unit)\ndef unseen(viewer, entity):\n    return entity.Hp.v != 1\n" +
                      "def show(a: Ref[Unit]):\n    t = a.Aim.target\n    return [look(\"x\", who=t), onto(t), at(\"hex\", [a, t])]\n");
            var world = new World(LoadClean());
            var a = world.Create("core:Foot");
            var b = world.Create("core:Foot");
            world.Set(b, world.Member("Hp", "v"), new IntValue(1));
            world.Set(a, world.Member("Aim", "target"), new EntityValue(b));
            Assert.That(world.ViewAs("alice").Sees(b), Is.False, "precondition");
            var items = ((ListValue)world.ViewAs("alice").Call("core.show", new EntityValue(a)).Value).Items;
            Assert.That(((LookValue)items[0]).Keys["who"], Is.SameAs(HiddenValue.Instance));
            Assert.That(((WhereValue)items[1]).Target, Is.SameAs(HiddenValue.Instance));
            Assert.That(((ListValue)((WhereValue)items[2]).Target!).Items[1], Is.SameAs(HiddenValue.Instance), "kept in its place");
        }

        [TestCase("at()", "at() takes a space and a value, got 0")]
        [TestCase("at(\"\", 1)", "the space name as a non-empty str")]
        [TestCase("at(\"hex\", show)", "cannot hold")]
        [TestCase("onto(3)", "onto() takes an entity, not int")]
        [TestCase("between(at(1), 2)", "between() takes two places")]
        [TestCase("at(1, space=\"hex\")", "at() has no argument 'space'")]
        public void APlaceRefusesAMalformedValue(string call, string message)
        {
            Mod("core", schema: Schema,
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n" +
                      "def show():\n    return " + call + "\n");
            var world = new World(LoadClean());
            var e = Assert.Throws<WorldException>(() => world.ViewAs("alice").Call("core.show"))!;
            Assert.That(e.Message, Does.Contain(message));
        }

        [Test]
        public void ALookCannotBeKept()
        {
            Mod("core", schema: Schema.Replace("v=field(int, default=3)", "v=field(int, default=3), seen=derived(str, lambda h: look(\"a\"))"),
                defs: "Unit(\"Foot\", look=\"pine\", where=lambda u: at(0))\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Does.StartWith("derived 'Hp.seen' uses '<lambda>', which makes a look or a place"));
        }
    }
}
