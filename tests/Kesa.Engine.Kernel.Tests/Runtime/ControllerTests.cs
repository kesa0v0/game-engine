using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>스크립트가 배정 표를 읽는 길 <c>controller(e)</c>: 사람·AI·빈자리, 꼬리표는 숨김 (spec S6, spec-gaps G241).</summary>
    public class ControllerTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Banner\", motto=field(str, default=\"\"), bonus=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Banner\", \"Seatable\"])\n";

        private const string Defs =
            "Party(\"Red\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Look(party: Ref[Party]):\n" +
            "    c = controller(party)\n" +
            "    party.Banner.motto = \"empty\" if c is None else c\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Wait():\n" +
            "    pass\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Odd(n: int):\n" +
            "    controller(n)\n" +
            // AI 진영에만 보너스: 앉은 종류가 바뀌면 조건을 다시 본다
            "@when(lambda party: controller(party) == \"ai\")\n" +
            "def ai_bonus(party: Ref[Party]):\n" +
            "    party.Banner.bonus = party.Banner.bonus + 1\n";

        private World _world = null!;
        private EntityId _red;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _red = _world.Create("banking:Red");
        }

        private string Look()
        {
            var r = _world.Submit("host", "banking.Look", new Dictionary<string, Value> { ["party"] = new EntityValue(_red) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.Succeeded), r.Error?.Message);
            return ((StrValue)_world.Get(_red, _world.Member("Banner", "motto"))).Value;
        }

        private long Bonus() => ((IntValue)_world.Get(_red, _world.Member("Banner", "bonus"))).Value;

        private void Wait() =>
            Assert.That(_world.Submit("host", "banking.Wait", new Dictionary<string, Value>()).Outcome, Is.EqualTo(SubmitOutcome.Succeeded));

        [Test]
        public void ScriptsReadTheKindNotTheTag()
        {
            Assert.That(Look(), Is.EqualTo("empty"));
            _world.Seat(_red, "alice");
            Assert.That(Look(), Is.EqualTo("human"), "a seat is human unless the host says otherwise");
            _world.Seat(_red, "ai-1", SeatKind.Ai);
            Assert.That(Look(), Is.EqualTo("ai"));
            Assert.That(_world.SeatKindOf(_red), Is.EqualTo(SeatKind.Ai));
            _world.Unseat(_red);
            Assert.That(Look(), Is.EqualTo("empty"));
            Assert.That(_world.SeatKindOf(_red), Is.Null);
        }

        [Test]
        public void AWhenConditionSeesTheSeatChange()
        {
            Wait();
            Assert.That(Bonus(), Is.EqualTo(0));
            _world.Seat(_red, "ai-1", SeatKind.Ai); // 사람이 나가 AI가 넘겨받음
            Wait();
            Assert.That(Bonus(), Is.EqualTo(1));
            _world.Seat(_red, "bob"); // 다시 사람이 들어옴: 조건이 거짓이 된다
            Wait();
            _world.Seat(_red, "ai-2", SeatKind.Ai);
            Wait();
            Assert.That(Bonus(), Is.EqualTo(2), "false then true again fires again");
        }

        [Test]
        public void TheKindIsSaved()
        {
            _world.Seat(_red, "ai-1", SeatKind.Ai);
            var save = _world.Save();
            Assert.That(Encoding.UTF8.GetString(save), Does.Contain("\"kind\": \"ai\""));
            var loaded = World.Load(LoadClean(), save).World!;
            Assert.That(loaded.SeatKindOf(_red), Is.EqualTo(SeatKind.Ai));
            Assert.That(loaded.Save(), Is.EqualTo(save));
        }

        [Test]
        public void ControllerTakesAnEntity()
        {
            var r = _world.Submit("host", "banking.Odd", new Dictionary<string, Value> { ["n"] = new IntValue(3) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("controller() takes one entity, got int"));
        }
    }
}
