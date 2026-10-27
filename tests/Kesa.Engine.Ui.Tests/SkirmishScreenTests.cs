using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>
    /// 첫 화면 <c>samples/skirmish/core/ui/screen.uxml</c> (G330~G345): 엔진 시험용 예시 게임의 유닛 목록과 고르기 → 공격을 UXML로
    /// 올려 두 연결이 각자 자기 눈으로 본다.
    /// </summary>
    public class SkirmishScreenTests
    {
        private static string Samples()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "samples", "skirmish");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("samples/skirmish");
        }

        private LocalHost _host = null!;
        private World _world = null!;
        private long _red, _blue;
        private UiDocument _screen = null!;

        private sealed class Seat
        {
            public IHostSession Session = null!;
            public MemoryRenderer Renderer = null!;
            public UiInterpreter Ui = null!;
            public BridgeEntity Party = null!;
        }

        private static string Format(BridgeValue value) => value is BridgeStr s ? s.Value : value.ToString();

        [SetUp]
        public async Task Start()
        {
            string root = Samples();
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(root, ModSorter.Sort(ModDiscovery.Discover(root, bag), bag), bag);
            Assert.That(bag.ToSortedList().Where(d => d.Severity == Severity.Error), Is.Empty);
            _world = new World(content, seed: 1);
            _host = new LocalHost(content, _world);
            Assert.That(_world.Submit("host", "skirmish.Start", new Dictionary<string, Value>()).Succeeded, Is.True);
            _red = _world.All("Party").Single(p => _world.DefOf(p) == "skirmish:Red").Value;
            _blue = _world.All("Party").Single(p => _world.DefOf(p) == "skirmish:Blue").Value;
            await _host.Seat(_red, "alice");
            await _host.Seat(_blue, "bob");

            var read = UxmlReader.Read("screen.uxml", File.ReadAllText(Path.Combine(root, "core", "ui", "screen.uxml")));
            Assert.That(read.Problems, Is.Empty, string.Join("\n", read.Problems));
            _screen = read.Document!;
        }

        private async Task<Seat> Open(string connection, long party)
        {
            var session = await _host.Open(connection);
            var renderer = new MemoryRenderer();
            var seat = new Seat { Session = session, Renderer = renderer, Ui = new UiInterpreter(session, renderer, Format), Party = new BridgeEntity(party, null) };
            var result = await seat.Ui.MountAsync(_screen, new Dictionary<string, BridgeValue> { [_screen.SeatParameter!] = seat.Party });
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
            return seat;
        }

        private static List<MemoryRenderer.El> Cards(Seat seat) => seat.Renderer.Named("units").Children;

        private void Place(long unit, long x, long y)
        {
            _world.Set(new EntityId(unit), _world.Member("Pos", "x"), new IntValue(x));
            _world.Set(new EntityId(unit), _world.Member("Pos", "y"), new IntValue(y));
        }

        private List<long> UnitsOf(long party) =>
            _world.All("Unit").Where(u => ((EntityValue)_world.Get(u, _world.Member("Owner", "party"))).Id.Value == party).Select(u => u.Value).ToList();

        [Test]
        public async Task EachConnectionSeesTheTurnAndOneCardPerUnit()
        {
            var alice = await Open("alice", _red);
            Assert.That(alice.Renderer.Named("turn").Text, Is.EqualTo("1"));
            Assert.That(Cards(alice).Count, Is.EqualTo(6));
            var hp = Cards(alice).Select(c => c.Children.Single(e => e.Attributes.GetValueOrDefault("name") == "hp").Text).ToList();
            Assert.That(hp.OrderBy(h => h, System.StringComparer.Ordinal), Is.EqualTo(new[] { "10", "10", "10", "10", "6", "6" }));
            Assert.That(Cards(alice).Count(c => c.Classes.Contains("mine")), Is.EqualTo(3), "the three of her side");
        }

        [Test]
        public async Task OnlyMyUnitsCanBeSelectedAndStrikeWaitsForAChosenUnitInReach()
        {
            var alice = await Open("alice", _red);
            var mine = UnitsOf(_red);
            var enemy = UnitsOf(_blue);
            var cards = Cards(alice);
            // 카드는 query(Unit) 순서(id 순)이고 id로 짝지을 수 있다
            var order = _world.All("Unit").Select(u => u.Value).ToList();
            MemoryRenderer.El Card(long unit) => cards[order.IndexOf(unit)];
            MemoryRenderer.El Button(long unit, string name) => Card(unit).Children.Single(e => e.Attributes.GetValueOrDefault("name") == name);

            Assert.That(Button(mine[0], "select").Enabled, Is.True, "my unit that can still move");
            Assert.That(Button(enemy[0], "select").Enabled, Is.False, "not mine");
            Assert.That(Button(enemy[0], "strike").Enabled, Is.False, "nothing is chosen yet");

            Place(enemy[0], _world.Get(new EntityId(mine[0]), _world.Member("Pos", "x")) is IntValue px ? px.Value : 0, 3);
            await alice.Ui.RefreshAsync();
            await Button(mine[0], "select").Press();
            Assert.That(Card(mine[0]).Classes.Contains("selected"), Is.True);
            Assert.That(Button(enemy[0], "strike").Enabled, Is.True, "chosen and in reach");
            Assert.That(Button(enemy[1], "strike").Enabled, Is.False, "out of reach");
        }

        [Test]
        public async Task StrikingRunsThroughTheUiActionAndTheCardShowsThatTheUnitActed()
        {
            var alice = await Open("alice", _red);
            var mine = UnitsOf(_red);
            var enemy = UnitsOf(_blue);
            long px = ((IntValue)_world.Get(new EntityId(mine[0]), _world.Member("Pos", "x"))).Value;
            Place(enemy[0], px, 3);
            await alice.Ui.RefreshAsync();

            var order = _world.All("Unit").Select(u => u.Value).ToList();
            var cards = Cards(alice);
            MemoryRenderer.El Button(long unit, string name) =>
                cards[order.IndexOf(unit)].Children.Single(e => e.Attributes.GetValueOrDefault("name") == name);
            await Button(mine[0], "select").Press();
            var results = new List<BridgeResult>();
            alice.Ui.ActionResult += (_, r) => results.Add(r);
            await Button(enemy[0], "strike").Press();

            Assert.That(results.Single().Succeeded, Is.True, results.Single().ToString());
            Assert.That(cards[order.IndexOf(mine[0])].Classes.Contains("acted"), Is.True, "the world changed, so the card was read again");
            Assert.That(cards[order.IndexOf(mine[0])].Classes.Contains("selected"), Is.False, "the action cleared the selection");
        }

        [Test]
        public async Task TheOtherConnectionSeesItsOwnSideAsMine()
        {
            var bob = await Open("bob", _blue);
            var order = _world.All("Unit").Select(u => u.Value).ToList();
            var mineSet = Cards(bob).Select((c, i) => (c, i)).Where(p => p.c.Classes.Contains("mine")).Select(p => order[p.i]).ToList();
            Assert.That(mineSet, Is.EquivalentTo(UnitsOf(_blue)));
        }
    }
}
