using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 월드 물체 (spec-gaps G401·G403·G405): <c>look</c>·<c>where</c> 칸이 둘 다 있는 타입의 엔티티를 연결마다 UI 기록과 함께 계산하고,
    /// 칸 하나가 <c>None</c>이면 그리지 않는다. 영수증은 화면 함수와 같다.
    /// </summary>
    public class WorldObjectTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0), boarded=field(bool, default=False))\n" +
            "deftype(\"Unit\", components=[\"Pos\"], look=const(Look[\"Unit\"]), where=const(Where[\"Unit\"]))\n" +
            "deftype(\"Rule\", components=[\"Pos\"], look=const(Look[\"Rule\"]))\n" +
            "ui_state(\"Sel\", unit=ref(\"Unit\", optional=True))\n";

        private const string Defs =
            "def drawn(u):\n    return look(\"infantry\", picked=ui.Sel.unit == u)\n" +
            "def spot(u):\n    return None if u.Pos.boarded else at(\"hex\", u.Pos.x)\n" +
            "Unit(\"Foot\", look=drawn, where=spot)\n" +
            "Unit(\"Tree\", look=\"pine\", where=lambda u: at(0))\n" +
            "Unit(\"Odd\", look=lambda u: 3, where=lambda u: at(0))\n" +
            "Rule(\"Tax\", look=\"coin\")\n" +
            "@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Sel.unit = u\n";

        private World _world = null!;
        private WorldView _view = null!;
        private UiState _ui = null!;

        [SetUp]
        public void NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _view = _world.ViewAs("alice");
            _ui = _world.NewUiState();
        }

        private Value Object(EntityId id) => _view.WorldObject(_ui, id).Value;

        [Test]
        public void OnlyTypesWithBothSlotsAreListed()
        {
            var foot = _world.Create("core:Foot");
            _world.Create("core:Tax");
            var tree = _world.Create("core:Tree");
            Assert.That(_view.WorldEntities(), Is.EqualTo(new[] { foot, tree }));
        }

        [Test]
        public void AnObjectIsItsLookAndPlace()
        {
            var foot = _world.Create("core:Foot");
            _world.Set(foot, _world.Member("Pos", "x"), new IntValue(4));
            var items = ((ListValue)Object(foot)).Items;
            var look = (LookValue)items[0];
            Assert.That((look.Name, ((BoolValue)look.Keys["picked"]).Value), Is.EqualTo(("infantry", false)));
            var where = (WhereValue)items[1];
            Assert.That((where.Name, ((IntValue)where.Target!).Value), Is.EqualTo(("hex", 4L)));

            var tree = (LookValue)((ListValue)Object(_world.Create("core:Tree"))).Items[0];
            Assert.That((tree.Name, tree.Keys.Count), Is.EqualTo(("pine", 0)), "a name is a look without keys");
        }

        [Test]
        public void NoneLeavesItOffTheScreen()
        {
            var foot = _world.Create("core:Foot");
            _world.Set(foot, _world.Member("Pos", "boarded"), BoolValue.True);
            Assert.That(Object(foot), Is.SameAs(NoneValue.Instance));
            Assert.That(Object(_world.Create("core:Tax")), Is.SameAs(NoneValue.Instance), "a type without a where slot");
        }

        [Test]
        public void TheReceiptSeesTheGameTheUiAndDeath()
        {
            var foot = _world.Create("core:Foot");
            var receipt = _view.WorldObject(_ui, foot);
            Assert.That(receipt.Changed(_world), Is.False);
            _world.Set(foot, _world.Member("Pos", "x"), new IntValue(2));
            Assert.That(receipt.Changed(_world), Is.True, "where read Pos");

            receipt = _view.WorldObject(_ui, foot);
            Assert.That(_view.Act(_ui, "core.pick", new Dictionary<string, Value> { ["u"] = new EntityValue(foot) }).Succeeded, Is.True);
            Assert.That(receipt.Changed(_world), Is.True, "look read ui.Sel");
            Assert.That(((BoolValue)((LookValue)((ListValue)Object(foot)).Items[0]).Keys["picked"]).Value, Is.True);

            var tree = _world.Create("core:Tree");
            receipt = _view.WorldObject(_ui, tree);
            _world.Destroy(tree);
            Assert.That(receipt.Changed(_world), Is.True, "a look that reads nothing still sees death");
            Assert.That(Object(tree), Is.SameAs(NoneValue.Instance));
        }

        [Test]
        public void GameCodeThatReachesASlotThroughAVariableIsStoppedWhileRunning()
        {
            // 이름으로는 보이지 않는 길: 멈춘 흐름에 자리 값이 남으면 저장할 수 없다
            Mod("scouts", schema: "component(\"Life\", v=field(int, default=3))\ndeftype(\"Scout\", components=[\"Life\"], look=const(Look[\"Scout\"]), where=const(Where[\"Scout\"]))\n",
                defs: "Scout(\"Foot2\", look=\"pine\", where=lambda u: at(\"hex\", 1))\n" +
                      "@command(free=True, acts_for=None)\ndef Go(u: Ref[Scout]):\n    t = u.Scout\n    p = t.where(u)\n" +
                      "    ask(u, \"t\", name=\"q\", choices=[\"a\"])\n");
            var world = new World(LoadClean());
            var u = world.Create("scouts:Foot2");
            world.Seat(u, "alice");
            var result = world.Submit("alice", "scouts.Go", new Dictionary<string, Value> { ["u"] = new EntityValue(u) });
            Assert.That(result.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(result.Error!.Message, Does.Contain("at() can only be used on a screen"));
            Assert.That(world.Save(), Is.Not.Empty);
        }

        [Test]
        public void ASlotThatGivesSomethingElseIsAnError()
        {
            var odd = _world.Create("core:Odd");
            var e = Assert.Throws<WorldException>(() => Object(odd))!;
            Assert.That(e.Message, Is.EqualTo("'core:Odd.look' returned int, but a look slot gives look(...) or the name of a look, " +
                                              "or None to leave it off the screen (spec-gaps G405)"));
        }
    }
}
