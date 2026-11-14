using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>
    /// 판 위 입력 (G416·G419·G420): 커서 아래 엔티티의 <c>@world_input</c>을 나중 모드부터 해 보아 사전 조건을 통과한 첫 것이 받는다.
    /// 모두 막히면 아래 엔티티로 넘어가고, 끝내 못 주면 처음 막힌 이유를 돌려준다.
    /// </summary>
    public class LocalWorldInputTests
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Pos\"])\n" +
            "deftype(\"Tower\", components=[\"Pos\"])\n" +
            "ui_state(\"Sel\", by=field(int, default=0))\n";

        private const string ArenaDefs =
            "Unit(\"Foot\")\nTower(\"Keep\")\n" +
            "@world_input(\"primary\", \"Unit\")\n@ui_action\ndef pick(u: Ref[Unit]):\n    ui.Sel.by = 1\n" +
            "def by():\n    return ui.Sel.by\n";

        // 나중에 불러오는 모드: 멀리 있는 유닛만 받고, 탑은 늘 막는다
        private const string ScoutsDefs =
            "@world_input(\"primary\", \"Unit\")\n@ui_action\ndef spot(u: Ref[Unit]):\n" +
            "    with preconditions:\n        require(u.Pos.x > 0, text(\"scouts-too-near\"))\n    ui.Sel.by = 2\n" +
            "@world_input(\"primary\", \"Tower\")\n@ui_action\ndef climb(t: Ref[Tower]):\n" +
            "    with preconditions:\n        require(False, text(\"scouts-locked\"))\n    ui.Sel.by = 3\n";

        private string _root = "";
        private World _world = null!;
        private IHostSession _alice = null!;
        private EntityId _foot;
        private EntityId _keep;

        [SetUp]
        public async Task Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", System.Environment.ProcessId + "-" + TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("arena/schema/types.py", Schema);
            Write("arena/defs/defs.py", ArenaDefs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            Write("scouts/defs/defs.py", ScoutsDefs);
            Write("scouts/mod.py", "Mod(id=\"scouts\", dependencies=[\"arena\"])\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _world = new World(content);
            _foot = _world.Create("arena:Foot");
            _keep = _world.Create("arena:Keep");
            var host = new LocalHost(content, _world);
            _alice = await host.Open("alice");
        }

        [TearDown]
        public void Close()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private async Task<long> By() => ((BridgeInt)(await _alice.Call("by", "arena.by", new BridgeValue[0])).Value!).Value;

        [Test]
        public async Task TheHandlersAreListedInLoadOrder()
        {
            var handlers = await _alice.WorldInputs();
            Assert.That(handlers.Select(h => (h.Input, h.Target, h.Action, h.Mod)), Is.EqualTo(new[]
            {
                ("primary", "Unit", "arena.pick", "arena"), ("primary", "Unit", "scouts.spot", "scouts"), ("primary", "Tower", "scouts.climb", "scouts"),
            }));
        }

        [Test]
        public async Task TheLaterModTakesItWhenItsPreconditionsPass()
        {
            _world.Set(_foot, _world.Member("Pos", "x"), new IntValue(3));
            var result = await _alice.WorldInput("primary", new[] { _foot.Value });
            Assert.That(result!.Succeeded, Is.True, result.ToString());
            Assert.That(await By(), Is.EqualTo(2));
        }

        [Test]
        public async Task ABlockedHandlerFallsThroughToTheEarlierMod()
        {
            var result = await _alice.WorldInput("primary", new[] { _foot.Value });
            Assert.That(result!.Succeeded, Is.True, result.ToString());
            Assert.That(await By(), Is.EqualTo(1));
        }

        [Test]
        public async Task ABlockedEntityFallsThroughToTheOneBelow()
        {
            var result = await _alice.WorldInput("primary", new[] { _keep.Value, _foot.Value });
            Assert.That(result!.Succeeded, Is.True, result.ToString());
            Assert.That(await By(), Is.EqualTo(1));
        }

        [Test]
        public async Task WhenNothingTakesItTheFirstRefusalComesBack()
        {
            var result = await _alice.WorldInput("primary", new[] { _keep.Value });
            Assert.That(result!.Succeeded, Is.False);
            Assert.That(result.Outcome, Is.EqualTo("PreconditionsFailed"));
            Assert.That(result.Reasons.Single().Text!.Key, Is.EqualTo("scouts-locked"));
            Assert.That(await By(), Is.EqualTo(0));
        }

        [Test]
        public async Task AnInputNoOneTakesIsNull()
        {
            Assert.That(await _alice.WorldInput("secondary", new[] { _foot.Value, _keep.Value }), Is.Null);
            Assert.That(await _alice.WorldInput("primary", new long[0]), Is.Null);
        }
    }
}
