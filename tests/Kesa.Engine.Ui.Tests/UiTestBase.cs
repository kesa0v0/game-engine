using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>진짜 <c>LocalHost</c>와 작은 시험 모드 <c>arena</c> 위에서 해석기를 시험한다 (spec-gaps G343).</summary>
    public abstract class UiTestBase
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\n" +
            "deftype(\"Hero\", components=[\"Hp\", \"Seatable\"])\n" +
            "deftype(\"Unit\", components=[\"Hp\"])\n" +
            "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True))\n" +
            "ui_state(\"Confirm\", open=field(bool, default=False))\n";

        private const string Defs =
            "Hero(\"Red\")\nHero(\"Blue\")\nUnit(\"Soldier\")\n" +
            "def hp(u):\n    return u.Hp.v\n" +
            "def units():\n    return [{\"unit\": u, \"hp\": u.Hp.v} for u in query(Unit)]\n" +
            "def unit_list():\n    return [u for u in query(Unit)]\n" +
            "def selected():\n    return ui.Selection.unit\n" +
            "def is_selected(u):\n    return ui.Selection.unit == u\n" +
            "def low(u):\n    return u.Hp.v < 2\n" +
            "def state(u):\n    return \"danger\" if u.Hp.v < 2 else [\"ok\", \"calm\"]\n" +
            "def ratio(u):\n    return u.Hp.v / 4\n" +
            "def boom(u):\n    return 1 // 0\n" +
            "def title():\n    return \"Arena\"\n" +
            "@ui_action\ndef select(u: Ref[Unit]):\n    ui.Selection.unit = u\n" +
            "@command(free=True, acts_for=None)\ndef Hit(target: Ref[Unit]):\n    with preconditions:\n" +
            "        choose(target, where=lambda t: True)\n" +
            "        require(target.Hp.v > 1, text(\"arena-too-weak\", hp=target.Hp.v))\n    target.Hp.v -= 1\n" +
            "@ui_action\ndef clear():\n    with preconditions:\n        require(ui.Selection.unit is not None, text(\"arena-none\"))\n    ui.Selection.unit = None\n" +
            "@command(free=True, acts_for=lambda hero: hero)\ndef Rest(hero: Ref[Hero]):\n    hero.Hp.v += 1\n" +
            "def hp_order():\n    return [u for u in query(Unit) if u.Hp.v < 3] + [u for u in query(Unit) if u.Hp.v >= 3]\n" +
            "def tags():\n    big = [u for u in query(Unit) if u.Hp.v > 3]\n    return [\"x\", \"y\", \"x\"] if len(big) > 0 else [\"x\", \"x\"]\n" +
            "@ui_action\ndef ask():\n    ui.Confirm.open = True\n" +
            "@ui_action\ndef answer():\n    ui.Confirm.open = False\n" +
            "def asking():\n    return ui.Confirm.open\n" +
            "def top():\n    best = None\n    for u in query(Unit):\n        if best is None or u.Hp.v > best.Hp.v:\n            best = u\n    return best\n";

        private string _root = "";
        protected LocalHost Host = null!;
        protected IHostSession Session = null!;
        protected MemoryRenderer Renderer = null!;
        protected UiInterpreter Ui = null!;
        protected long Red, Blue;
        protected long A, B;
        protected readonly Dictionary<string, string> Files = new Dictionary<string, string>();

        [SetUp]
        public async Task OpenWorld()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-ui-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("arena/schema/types.py", Schema);
            Write("arena/defs/defs.py", Defs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));

            var world = new World(content);
            Red = world.Create("arena:Red").Value;
            Blue = world.Create("arena:Blue").Value;
            A = world.Create("arena:Soldier").Value;
            B = world.Create("arena:Soldier").Value;
            Host = new LocalHost(content, world);
            await Host.Seat(Red, "alice");
            Session = await Host.Open("alice");
            Renderer = new MemoryRenderer();
            Ui = new UiInterpreter(Session, Renderer, Format, name => Files.TryGetValue(name, out var text) ? text : null);
        }

        [TearDown]
        public void CloseWorld()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        protected static string Format(BridgeValue value) => value is BridgeStr s ? s.Value : value.ToString();

        protected World World => Host.World;

        protected long Hp(long id) => ((Kesa.Engine.Script.IntValue)World.Get(new EntityId(id), World.Member("Hp", "v"))).Value;

        protected void SetHp(long id, long hp) =>
            World.Set(new EntityId(id), World.Member("Hp", "v"), new Kesa.Engine.Script.IntValue(hp));

        protected static UiDocument Doc(string uxml)
        {
            var result = UxmlReader.Read("test.uxml", uxml);
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
            return result.Document!;
        }

        protected async Task<UiPanel> Mount(string uxml, params (string, BridgeValue)[] arguments)
        {
            var result = await Ui.MountAsync(Doc(uxml), arguments.ToDictionary(p => p.Item1, p => p.Item2));
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
            return result.Panel!;
        }

        protected BridgeEntity Entity(long id) => new BridgeEntity(id, null);
    }
}
