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
        private string _modFolder = null!;

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

            _modFolder = Path.Combine(root, "core");
            var read = UxmlReader.Read("screen.uxml", File.ReadAllText(Path.Combine(root, "core", "ui", "screen.uxml")));
            Assert.That(read.Problems, Is.Empty, string.Join("\n", read.Problems));
            _screen = read.Document!;
        }

        private async Task<Seat> Open(string connection, long party)
        {
            var session = await _host.Open(connection);
            var renderer = new MemoryRenderer();
            var ui = new UiInterpreter(session, renderer, Format, ModFile);
            var seat = new Seat { Session = session, Renderer = renderer, Ui = ui, Party = new BridgeEntity(party, null) };
            var result = await seat.Ui.MountAsync(_screen, new Dictionary<string, BridgeValue> { [_screen.SeatParameter!] = seat.Party });
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
            Assert.That(ui.Errors, Is.Empty, "the screen's USS reads cleanly: " + string.Join("\n", ui.Errors));
            return seat;
        }

        private string? ModFile(string path)
        {
            string file = Path.Combine(_modFolder, path);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }

        [Test]
        public async Task TheScreenCarriesTheModsStyleSheet()
        {
            var seat = await Open("alice", _red);
            Assert.That(seat.Renderer.Named("screen").StyleSheets, Is.EqualTo(new[] { "ui/screen.uss" }));
            var selectors = seat.Renderer.Sheets.Single().Rules.SelectMany(r => r.Selectors).Select(s => string.Concat(s.Compounds.SelectMany(c => c.Parts))).ToList();
            Assert.That(selectors, Does.Contain(".card.selected").And.Contain(".modal-blocker"));
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
        public async Task EndTurnIsAResponseCommandAndOnlyTheSideWhoseTurnItIsMayPressIt()
        {
            var alice = await Open("alice", _red);
            var bob = await Open("bob", _blue);
            Assert.That(alice.Renderer.Named("endturn").Enabled, Is.True, "red's turn");
            Assert.That(bob.Renderer.Named("endturn").Enabled, Is.False, "blue has no open question to answer");

            await alice.Renderer.Named("ask-end").Press();
            Assert.That(alice.Renderer.Shown.Select(e => e.Attributes["name"]), Is.EqualTo(new[] { "confirm" }));
            var results = new List<BridgeResult>();
            alice.Ui.ActionResult += (_, r) => results.Add(r);
            await alice.Renderer.Named("endturn").Press();
            Assert.That(results.Single().Succeeded, Is.True, results.Single().ToString());
            Assert.That(alice.Renderer.Named("turn").Text, Is.EqualTo("2"));
            Assert.That(alice.Renderer.Named("endturn").Enabled, Is.False, "now it is blue's turn");
            Assert.That(alice.Renderer.Shown, Is.Empty, "the question was asked on turn 1, so the modal closed by itself");

            await bob.Ui.RefreshAsync();
            Assert.That(bob.Renderer.Named("turn").Text, Is.EqualTo("2"));
            Assert.That(bob.Renderer.Named("endturn").Enabled, Is.True, "the world version moved, so bob's button woke up");
        }

        [Test]
        public async Task TheInfoWindowShowsTheUnitAndClosesWithEscapeAndTheModalSitsAboveIt()
        {
            var alice = await Open("alice", _red);
            var order = _world.All("Unit").Select(u => u.Value).ToList();
            var enemy = UnitsOf(_blue)[0];
            var info = Cards(alice)[order.IndexOf(enemy)].Children.Single(e => e.Attributes.GetValueOrDefault("name") == "info");
            List<string> Shown() => alice.Renderer.Shown.Select(e => e.Attributes["name"]).ToList();

            Assert.That(Shown(), Is.Empty);
            await info.Press();
            Assert.That(Shown(), Is.EqualTo(new[] { "unit-info" }));
            Assert.That(alice.Renderer.Named("info-hp").Text, Is.EqualTo(((IntValue)_world.Get(new EntityId(enemy), _world.Member("Life", "hp"))).Value.ToString()));
            Assert.That(info.Enabled, Is.False, "already showing that unit");
            Assert.That(alice.Renderer.DragHandles, Does.Contain(alice.Renderer.Named("info-title").Id));

            await alice.Renderer.Named("ask-end").Press();
            Assert.That(Shown(), Is.EqualTo(new[] { "unit-info", "confirm" }));
            Assert.That(alice.Renderer.BlockerAt, Is.EqualTo(1));
            Assert.That(await alice.Ui.Windows.Escape(), Is.True);
            Assert.That(Shown(), Is.EqualTo(new[] { "unit-info" }), "ESC closes the modal first through cancel_end_turn");
            Assert.That(await alice.Ui.Windows.Escape(), Is.True);
            Assert.That(Shown(), Is.Empty);
            Assert.That(await alice.Ui.Windows.Escape(), Is.False, "nothing left to close");
        }

        [Test]
        public async Task DeliverPairsAResponseCommandWithTheQuestionOrRefusesWithAReason()
        {
            var bob = await _host.Open("bob");
            var refused = await bob.Deliver("skirmish.EndTurn", new Dictionary<string, BridgeValue> { ["party"] = new BridgeEntity(_blue, null) });
            Assert.That((refused.Succeeded, refused.Outcome), Is.EqualTo((false, "Refused")));
            Assert.That(refused.Reasons.Single().Kind, Is.EqualTo(BridgeReasonKind.Refusal));
            Assert.That(refused.Reasons.Single().Message, Does.Contain("no open question"));

            var alice = await _host.Open("alice");
            var answered = await alice.Deliver("skirmish.EndTurn", new Dictionary<string, BridgeValue> { ["party"] = new BridgeEntity(_red, null) });
            Assert.That(answered.Succeeded, Is.True, answered.ToString());
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
