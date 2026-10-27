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
    /// <summary>세계를 늘어놓는 읽기 (G329): 연결이 보는 엔티티와 컴포넌트만, 도장과 함께.</summary>
    public class LocalReadTests
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3), twice=derived(int, lambda h: h.v * 2))\n" +
            "component(\"Secret\", plan=field(int, default=7))\n" +
            "deftype(\"Hero\", components=[\"Hp\", \"Secret\", \"Seatable\"])\n" +
            "deftype(\"Unit\", components=[\"Hp\"])\n" +
            "deftype(\"Mine\", components=[\"Hp\"])\n";

        private const string Defs =
            "Hero(\"Red\")\nHero(\"Blue\")\nUnit(\"Soldier\")\nMine(\"Trap\")\n" +
            "@view_rule(Secret)\ndef own_secret(viewer, entity):\n    return viewer == entity\n" +
            "@view_rule(Mine)\ndef seen_mine(viewer, entity):\n    return viewer is not None\n";

        private string _root = "";
        private LocalHost _host = null!;
        private long _red, _blue, _soldier, _trap;

        [SetUp]
        public async Task Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("arena/schema/types.py", Schema);
            Write("arena/defs/defs.py", Defs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));

            var world = new World(content);
            _trap = world.Create("arena:Trap").Value;
            _soldier = world.Create("arena:Soldier").Value;
            _red = world.Create("arena:Red").Value;
            _blue = world.Create("arena:Blue").Value;
            _host = new LocalHost(content, world);
            await _host.Seat(_red, "alice");
            await _host.Seat(_blue, "bob");
        }

        [TearDown]
        public void Close()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        [Test]
        public async Task EntitiesComeInSchemaTypeOrderThenIdWithTheirDef()
        {
            var listed = await (await _host.Open("alice")).Entities();
            Assert.That(listed.Select(l => (l.Entity.Id, l.Entity.Def)), Is.EqualTo(new[]
            {
                (_red, "arena:Red"), (_blue, "arena:Blue"), (_soldier, "arena:Soldier"), (_trap, "arena:Trap"),
            }));
        }

        [Test]
        public async Task AnEntityTheViewRuleHidesIsNotListed()
        {
            var stranger = await _host.Open("stranger");
            var listed = await stranger.Entities();
            Assert.That(listed.Select(l => l.Entity.Id), Is.EqualTo(new[] { _red, _blue, _soldier }));
            Assert.That(await stranger.Read(_trap), Is.Null);
        }

        [Test]
        public async Task OnlyTheComponentsTheConnectionSeesAreRead()
        {
            var alice = await _host.Open("alice");
            var own = (await alice.Read(_red))!;
            Assert.That(own.Select(c => c.Name), Is.EquivalentTo(new[] { "Hp", "Secret", "Seatable" }));
            var secret = own.Single(c => c.Name == "Secret").Members.Single();
            Assert.That((secret.Key, secret.Value), Is.EqualTo(("plan", (BridgeValue)new BridgeInt(7))));
            Assert.That((await alice.Read(_blue))!.Select(c => c.Name), Is.EquivalentTo(new[] { "Hp", "Seatable" }), "the other hero's secret stays hidden");
            Assert.That((await (await _host.Open("stranger")).Read(_red))!.Select(c => c.Name), Is.EquivalentTo(new[] { "Hp", "Seatable" }));
        }

        [Test]
        public async Task MembersAreReadInDeclaredOrderWithDerivedValues()
        {
            var hp = (await (await _host.Open("alice")).Read(_soldier))!.Single();
            Assert.That(hp.Members.Select(m => (m.Key, m.Value)), Is.EqualTo(new[]
            {
                ("v", (BridgeValue)new BridgeInt(3)), ("twice", new BridgeInt(6)),
            }));
        }

        [Test]
        public async Task TheStampMovesWhenTheEntityChangesAndDerivedMarksItVolatile()
        {
            var alice = await _host.Open("alice");
            var before = (await alice.Entities()).Single(l => l.Entity.Id == _soldier);
            var other = (await alice.Entities()).Single(l => l.Entity.Id == _red);
            Assert.That(before.Volatile, Is.True, "Hp has a derived member");
            _host.World.Set(new EntityId(_soldier), _host.World.Member("Hp", "v"), new IntValue(9));
            var after = (await alice.Entities()).Single(l => l.Entity.Id == _soldier);
            Assert.That(after.Version, Is.GreaterThan(before.Version));
            Assert.That((await alice.Entities()).Single(l => l.Entity.Id == _red).Version, Is.EqualTo(other.Version));
        }
    }
}
