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

        private LookLibrary Looks()
        {
            var files = Directory.GetFiles(_modFolder, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, System.StringComparer.Ordinal)
                .Select(f => new LookSource("skirmish", Path.GetRelativePath(_modFolder, f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(f)));
            return LookLibrary.Load(files, LookKinds.Standard(), path => File.Exists(Path.Combine(_modFolder, path)));
        }

        /// <summary>
        /// 맞음은 질문에 답해 커맨드가 끝난 뒤 신호 처리에서 효과가 된다. 물러날 칸을 눌러 답한 판과 화면을 skirmish의 staged로 연다.
        /// </summary>
        private async Task<(Seat Alice, LookBoard Board, MemoryLookRenderer Renderer, long Target)> AnsweredStaged(long? hp = null)
        {
            var renderer = new MemoryLookRenderer();
            var alice = await Open("alice", _red);
            var bob = await Open("bob", _blue);
            var board = new LookBoard(alice.Session, new LookStage(Looks(), renderer), BridgePresentation.Staged);
            alice.Ui.Hold = board;
            await board.RefreshAsync();
            long target = await HitRed(alice, bob);
            if (hp != null) _world.Set(new EntityId(target), _world.Member("Life", "hp"), new IntValue(hp.Value));
            await alice.Ui.RefreshAsync();
            await board.RefreshAsync();
            board.Advance(250); // 시험이 옮겨 둔 자리로 미끄러져 가는 시간 (G432). 그동안 화면도 기다렸다 (G437)
            await alice.Ui.RefreshAsync();
            Assert.That((board.Holding, Hit(alice)), Is.EqualTo((false, true)), "the question is open; nothing to play yet");
            string spot = board.Stage.Keys.Single(k => k.StartsWith("mark:"));
            Assert.That((await board.PressAsync(new[] { spot }))!.Succeeded, Is.True);
            await alice.Ui.RefreshAsync();
            await board.RefreshAsync();
            return (alice, board, renderer, target);
        }

        private static bool Hit(Seat seat) => Cards(seat).Any(c => c.Classes.Contains("hit"));

        /// <summary>
        /// staged (G430·G437): 맞음의 고리·숫자를 보이는 동안 판(물러나기 전 자리, 물러날 칸)과 카드를 붙잡고, 숫자가 흐려지기 시작하면 판을 맞추고
        /// 유닛이 물러난 자리에 닿을 때까지 더 붙잡는다. 카드는 그 뒤에 바뀐다.
        /// </summary>
        [Test]
        public async Task StagedHoldsTheBoardAndTheScreenWhileTheHitPlays()
        {
            var (alice, board, renderer, target) = await AnsweredStaged();
            var stage = board.Stage;
            bool Effects() => stage.Keys.Any(k => k.StartsWith(LookStage.FxPrefix));
            bool Marked() => stage.Keys.Any(k => k.StartsWith("mark:"));
            Assert.That((board.Holding, Effects(), Hit(alice), Marked()), Is.EqualTo((true, true, true, true)),
                "the ring and the number play over the old board and screen");
            Assert.That(renderer.Dump("entity:" + target), Does.StartWith("@(2.0, -2.0, 0.0) "), "still where it was hit");
            board.Advance(500);
            await alice.Ui.RefreshAsync();
            await board.RefreshAsync();
            Assert.That((board.Holding, Hit(alice), Marked()), Is.EqualTo((true, true, true)), "the number holds until 0.55 s");
            board.Advance(100);
            await alice.Ui.RefreshAsync();
            await board.RefreshAsync();
            Assert.That((board.Holding, Effects(), Hit(alice), Marked()), Is.EqualTo((true, true, true, false)),
                "as the number fades the board catches up, and the slide back still holds the cards");
            Assert.That(renderer.Dump("entity:" + target), Does.StartWith("@(2.0, -2.0, 0.0) "), "it starts sliding back");
            board.Advance(250);
            await alice.Ui.RefreshAsync();
            Assert.That(renderer.Dump("entity:" + target), Does.StartWith("@(2.0, -3.0, 0.0) "), "it stepped back");
            Assert.That((board.Holding, Hit(alice)), Is.EqualTo((false, false)), "arrived: let go, and the answered cards show");
            Assert.That(stage.Problems, Is.Empty, string.Join(", ", stage.Problems));
        }

        /// <summary>퇴장 (G431): 쓰러진 유닛은 붙잡는 동안 맞은 자리에 남았다가, 놓을 때 unit-down이 그 그림으로 줄어들며 흐려진다.</summary>
        [Test]
        public async Task AFallenUnitShrinksAwayAfterTheStagedHit()
        {
            var (alice, board, renderer, target) = await AnsweredStaged(hp: 1);
            Assert.That(_world.IsAlive(new EntityId(target)), Is.False, "it fell");
            Assert.That(board.Stage.Keys, Does.Contain("entity:" + target), "held where it was hit");
            board.Advance(600);
            await board.RefreshAsync();
            var exit = board.Stage.Keys.Where(k => k.StartsWith(LookStage.FxPrefix)).Last();
            Assert.That(board.Stage.Keys, Does.Not.Contain("entity:" + target));
            Assert.That(renderer.Dump(exit), Does.Match(@"^@\(2\.0, -2\.0, 0\.0\) Sprite\[layer=1,size=0\.7,0\.7,src=art/\w+\.png,tint=#d04848ff\]$"),
                "the same picture where the board last showed it (it never saw the retreat)");
            board.Advance(400);
            Assert.That(renderer.Dump(exit), Does.Contain(" x0.68 a0.5 "), "shrinking 0.8 a second and half faded");
            Assert.That(board.Stage.Problems, Is.Empty, string.Join(", ", board.Stage.Problems));
        }

        /// <summary>G437: 효과가 끝나도 물러나 미끄러지는 동안은 붙잡으므로, 그때의 누름도 행동이 아니라 건너뛰기다.</summary>
        [Test]
        public async Task APressWhileAUnitSlidesSkipsItInsteadOfActing()
        {
            var (alice, board, renderer, target) = await AnsweredStaged();
            board.Advance(600);
            await alice.Ui.RefreshAsync();
            await board.RefreshAsync();
            Assert.That((board.Holding, board.Stage.Busy), Is.EqualTo((true, true)), "sliding back");

            var results = new List<BridgeResult>();
            alice.Ui.ActionResult += (_, r) => results.Add(r);
            await alice.Renderer.Named("endturn").Press();
            Assert.That((results.Count, board.Holding), Is.EqualTo((0, false)), "the press only skipped");
            Assert.That(renderer.Dump("entity:" + target), Does.StartWith("@(2.0, -3.0, 0.0) "), "it arrived at once");
            await alice.Ui.RefreshAsync();
            Assert.That(Hit(alice), Is.False, "the answered screen shows");
        }

        [Test]
        public async Task APressWhileStagedSkipsThePlayInsteadOfActing()
        {
            var (alice, board, _, _) = await AnsweredStaged();
            var stage = board.Stage;
            Assert.That(board.Holding, Is.True);

            var results = new List<BridgeResult>();
            alice.Ui.ActionResult += (_, r) => results.Add(r);
            await alice.Renderer.Named("endturn").Press();
            Assert.That((results.Count, board.Holding, stage.PlayingCount), Is.EqualTo((0, false, 0)), "the press skipped the play and did nothing else");
            await alice.Ui.RefreshAsync();
            Assert.That(Hit(alice), Is.False, "the answered screen shows at once");
            var inspect = await board.InputAsync(LookBoard.Secondary, new[] { "entity:" + UnitsOf(_red)[0] });
            Assert.That(inspect!.Succeeded, Is.True, "let go, so presses act again");
        }

        [Test]
        public async Task AHitUnitsCardLightsUpAndItsRetreatButtonAnswersTheQuestion()
        {
            var alice = await Open("alice", _red);
            var bob = await Open("bob", _blue);
            long target = await HitRed(alice, bob);

            await alice.Ui.RefreshAsync();
            await bob.Ui.RefreshAsync();
            var order = _world.All("Unit").Select(u => u.Value).ToList();
            MemoryRenderer.El Card(Seat seat, long unit) => Cards(seat)[order.IndexOf(unit)];
            MemoryRenderer.El Button(Seat seat, long unit, string name) =>
                Card(seat, unit).Children.Single(e => e.Attributes.GetValueOrDefault("name") == name);
            Assert.That(Cards(alice).Where(c => c.Classes.Contains("hit")), Is.EqualTo(new[] { Card(alice, target) }));
            Assert.That(Button(alice, target, "retreat").Flags["display"], Is.True);
            Assert.That(Button(alice, UnitsOf(_red)[1], "retreat").Flags["display"], Is.False);
            Assert.That(Button(alice, target, "retreat").Enabled, Is.True, "the button's check reads the question too");
            Assert.That(Button(alice, UnitsOf(_red)[1], "hold").Enabled, Is.False);
            Assert.That(Cards(bob).Any(c => c.Classes.Contains("hit")), Is.False, "the question is red's to answer");
            Assert.That(Button(bob, target, "retreat").Enabled, Is.False);

            var results = new List<BridgeResult>();
            alice.Ui.ActionResult += (_, r) => results.Add(r);
            await Button(alice, target, "retreat").Press();
            Assert.That(results.Single().Succeeded, Is.True, results.Single().ToString());
            Assert.That(_world.OpenRequests.Any(q => q.Name == "skirmish-hit"), Is.False);
            Assert.That(((IntValue)_world.Get(new EntityId(target), _world.Member("Pos", "y"))).Value, Is.EqualTo(3), "it stepped back");
            Assert.That(Card(alice, target).Classes.Contains("hit"), Is.False, "the question closed, so the card was read again");
            Assert.That(Button(alice, target, "retreat").Enabled, Is.False);
        }

        /// <summary>빗나감도 보인다: 맞을 뻔한 칸에 Miss 신호의 글이 떠오른다. 뒤 칸을 막아 맞아도 묻지 않고, 쓰러지지 않게 체력을 늘린다.</summary>
        [Test]
        public async Task AMissShowsItsWordWhereTheTargetStands()
        {
            var renderer = new MemoryLookRenderer();
            var alice = await Open("alice", _red);
            var bob = await Open("bob", _blue);
            var board = new LookBoard(alice.Session, new LookStage(Looks(), renderer));
            await board.RefreshAsync();
            await alice.Renderer.Named("ask-end").Press();
            await alice.Renderer.Named("endturn").Press();
            long target = UnitsOf(_red)[0];
            Place(target, 2, 2);
            Place(UnitsOf(_red)[1], 2, 3);
            _world.Set(new EntityId(target), _world.Member("Life", "hp"), new IntValue(100));
            foreach (long attacker in UnitsOf(_blue))
            {
                Place(attacker, 2, 1);
                var attack = await bob.Session.Deliver("skirmish.Attack", new Dictionary<string, BridgeValue>
                {
                    ["party"] = bob.Party, ["unit"] = new BridgeEntity(attacker, null), ["target"] = new BridgeEntity(target, null),
                });
                Assert.That(attack.Succeeded, Is.True, attack.ToString());
                Place(attacker, 0, 0);
                await board.RefreshAsync();
                var words = board.Stage.Keys.Where(k => k.StartsWith(LookStage.FxPrefix)).Select(renderer.Dump).Where(d => d.Contains("skirmish-miss")).ToList();
                if (words.Count == 0) continue;
                Assert.That(words.Single(), Does.Match(@"^@\(2\.0, -2\.0, 0\.0\) a0\.0 Text\["), "the word fades in where the target is");
                Assert.That(board.Stage.Problems, Is.Empty, string.Join(", ", board.Stage.Problems));
                return;
            }
            Assert.Fail("no attack missed with this seed; place more attackers");
        }

        /// <summary>파랑이 (2,1)에서 (2,2)의 빨강을 친다. 뒤 칸 (2,3)이 비어 있어 맞으면 버틸지 물러날지 묻는다. 빗나가면 다음 유닛으로.</summary>
        private async Task<long> HitRed(Seat alice, Seat bob)
        {
            await alice.Renderer.Named("ask-end").Press();
            await alice.Renderer.Named("endturn").Press();
            long target = UnitsOf(_red)[0];
            Place(target, 2, 2);
            foreach (long attacker in UnitsOf(_blue))
            {
                var at = (_world.Get(new EntityId(attacker), _world.Member("Pos", "x")), _world.Get(new EntityId(attacker), _world.Member("Pos", "y")));
                Place(attacker, 2, 1);
                var attack = await bob.Session.Deliver("skirmish.Attack", new Dictionary<string, BridgeValue>
                {
                    ["party"] = bob.Party, ["unit"] = new BridgeEntity(attacker, null), ["target"] = new BridgeEntity(target, null),
                });
                Assert.That(attack.Succeeded, Is.True, attack.ToString());
                if (_world.OpenRequests.Any(q => q.Name == "skirmish-hit")) break;
                Place(attacker, ((IntValue)at.Item1).Value, ((IntValue)at.Item2).Value);
            }
            Assert.That(_world.OpenRequests.Count(q => q.Name == "skirmish-hit"), Is.EqualTo(1), "one of the three attacks hit");
            return target;
        }

        [Test]
        public async Task AHitUnitsRetreatSquareIsMarkedAndClickingItAnswers()
        {
            var alice = await Open("alice", _red);
            var bob = await Open("bob", _blue);
            Assert.That((await alice.Session.WorldMarks(0)).Changed, Is.Empty);
            long target = await HitRed(alice, bob);

            var spot = (await alice.Session.WorldMarks(0)).Changed.Single();
            Assert.That((spot.Look!.Name, spot.Where), Is.EqualTo(("retreat-spot",
                BridgeWhere.At("square", new BridgeList(new BridgeValue[] { BridgeValue.Of(2), BridgeValue.Of(3) })))));
            Assert.That((await bob.Session.WorldMarks(0)).Changed, Is.Empty, "the question is red's to answer");

            var result = await alice.Session.Act(spot.Click!.Action, spot.Click.Arguments);
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(((IntValue)_world.Get(new EntityId(target), _world.Member("Pos", "y"))).Value, Is.EqualTo(3), "it stepped back");
            Assert.That((await alice.Session.WorldMarks(0)).Changed, Is.Empty, "the question closed, so the mark is gone");
        }

        [Test]
        public async Task TheBoardShowsTheUnitsAndPressingTheRetreatMarkAnswers()
        {
            var files = Directory.GetFiles(_modFolder, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, System.StringComparer.Ordinal)
                .Select(f => new LookSource("skirmish", Path.GetRelativePath(_modFolder, f).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(f)));
            var library = LookLibrary.Load(files, LookKinds.Standard(), path => File.Exists(Path.Combine(_modFolder, path)));
            Assert.That(library.Problems, Is.Empty, string.Join(", ", library.Problems));
            var renderer = new MemoryLookRenderer();
            var stage = new LookStage(library, renderer);

            var alice = await Open("alice", _red);
            var bob = await Open("bob", _blue);
            var board = new LookBoard(alice.Session, stage);
            await board.RefreshAsync();
            Assert.That(stage.Problems, Is.Empty, string.Join(", ", stage.Problems));
            var archer = UnitsOf(_red).Single(u => ((IntValue)_world.Get(new EntityId(u), _world.Member("Pos", "x"))).Value == 2);
            Assert.That(stage.Keys.Count(k => k.StartsWith("entity:")), Is.EqualTo(6), "six units, no marks yet");
            Assert.That(renderer.Dump("entity:" + archer), Is.EqualTo(
                "@(2.0, -4.0, 0.0) -Sprite[src=art/ring.png,tint=#fac846ff] Sprite[layer=1,size=0.7,0.7,src=art/archer.png,tint=#d04848ff] " +
                "-Sprite[layer=2,size=0.7,0.7,src=art/white.png,tint=#00000070]"));

            var opened = await board.InputAsync(LookBoard.Secondary, new[] { "entity:" + archer });
            Assert.That(opened!.Succeeded, Is.True, opened.ToString());
            var inspected = await alice.Session.Call("inspected", "skirmish.inspected", new BridgeValue[0]);
            Assert.That(((BridgeEntity)inspected.Value!).Id, Is.EqualTo(archer), "the right press opened the unit's window");

            var inputs = InputLibrary.Load(files, new[] { "I" });
            Assert.That((inputs.Problems.Count, inputs.Inputs["inspect"].Key.ToString()), Is.EqualTo((0, "I")));
            var other = UnitsOf(_red).First(u => u != archer);
            var keyed = await board.InputAsync("inspect", new[] { "entity:" + other });
            Assert.That(keyed!.Succeeded, Is.True, keyed.ToString());
            inspected = await alice.Session.Call("inspected", "skirmish.inspected", new BridgeValue[0]);
            Assert.That(((BridgeEntity)inspected.Value!).Id, Is.EqualTo(other), "the I key on a unit opens its window too");

            long target = await HitRed(alice, bob);
            await board.RefreshAsync();
            stage.Advance(250); // 시험이 옮겨 둔 자리로 미끄러져 가는 시간 (G432)
            Assert.That(stage.Problems, Is.Empty, string.Join(", ", stage.Problems));
            string spot = stage.Keys.Single(k => k.StartsWith("mark:"));
            Assert.That(renderer.Dump(spot), Is.EqualTo("@(2.0, -3.0, 0.0) Sprite[src=art/white.png,tint=#4080ff80]"));
            var result = await board.PressAsync(new[] { spot, "entity:" + target });
            Assert.That(result!.Succeeded, Is.True, result.ToString());
            await board.RefreshAsync();
            Assert.That(stage.Keys.Any(k => k.StartsWith("mark:")), Is.False, "answered, so the mark is gone");
            Assert.That(renderer.Dump("entity:" + target), Does.StartWith("@(2.0, -2.0, 0.0) "), "it slides back over 0.25 s");

            // 맞음 신호의 효과(fx.py·fx.look.xml)가 물러난 칸에 뜬다: 고리는 0.6초, 피해 숫자는 떠오르며 0.95초
            var effects = stage.Keys.Where(k => k.StartsWith(LookStage.FxPrefix)).ToList();
            Assert.That(effects.Count, Is.EqualTo(2));
            Assert.That(renderer.Dump(effects[0]), Is.EqualTo("@(2.0, -3.0, 0.0) a0.0 Sprite[layer=5,size=1.2,1.2,src=art/ring.png,tint=#ff4040ff]"));
            Assert.That(renderer.Dump(effects[1]), Does.Match(@"^@\(2\.0, -3\.0, 0\.0\) a0\.0 Text\[color=#ffe070ff,layer=6,offset=0\.0,0\.3,size=0\.45,text=-\d\]$"));
            stage.Advance(100);
            Assert.That(renderer.Dump(effects[0]), Does.StartWith("@(2.0, -3.0, 0.0) Sprite"), "fully shown");
            stage.Advance(500);
            Assert.That(stage.Keys.Where(k => k.StartsWith(LookStage.FxPrefix)), Is.EqualTo(new[] { effects[1] }));
            Assert.That(renderer.Dump(effects[1]), Does.StartWith("@(2.0, -2.52, 0.0) "), "the number rose 0.8 a second");
            stage.Advance(400);
            Assert.That(stage.Keys.Any(k => k.StartsWith(LookStage.FxPrefix)), Is.False);
            Assert.That(renderer.Dump("entity:" + target), Does.StartWith("@(2.0, -3.0, 0.0) "), "the unit stepped back onto the marked square");
        }

        [Test]
        public async Task PressingAUnitPicksItAndPressingAnEnemyInReachAttacks()
        {
            var renderer = new MemoryLookRenderer();
            var stage = new LookStage(LookLibrary.Load(new LookSource[0], LookKinds.Standard()), renderer);
            var alice = await Open("alice", _red);
            var board = new LookBoard(alice.Session, stage);
            long mine = UnitsOf(_red)[0], enemy = UnitsOf(_blue)[0];
            Place(mine, 2, 2);
            Place(enemy, 2, 1);

            var refused = await board.PressAsync(new[] { "entity:" + enemy });
            Assert.That(refused!.Succeeded, Is.False, "nothing is picked and it is not ours");
            Assert.That(refused.Outcome, Is.EqualTo("PreconditionsFailed"), refused.ToString());

            var picked = await board.PressAsync(new[] { "entity:" + mine });
            Assert.That(picked!.Succeeded, Is.True, picked.ToString());
            var chosen = await alice.Session.Call("chosen", "skirmish.chosen", new BridgeValue[0]);
            Assert.That(((BridgeEntity)chosen.Value!).Id, Is.EqualTo(mine));

            var attacked = await board.PressAsync(new[] { "entity:" + enemy });
            Assert.That(attacked!.Succeeded, Is.True, attacked.ToString());
            Assert.That(attacked.Deliveries.Single().Command, Is.EqualTo("skirmish.Attack"));
            Assert.That(attacked.Deliveries.Single().Result.Succeeded, Is.True, attacked.Deliveries.Single().Result.ToString());
        }

        [Test]
        public async Task PressingAnEmptySquareNextToThePickedUnitMovesIt()
        {
            var stage = new LookStage(LookLibrary.Load(new LookSource[0], LookKinds.Standard()), new MemoryLookRenderer());
            var alice = await Open("alice", _red);
            var board = new LookBoard(alice.Session, stage);
            long mine = UnitsOf(_red)[0];
            Place(mine, 2, 2);
            var taken = _world.All("Unit").Select(u => (X(u.Value), Y(u.Value))).ToHashSet();
            var free = new[] { (1, 2), (3, 2), (2, 1), (2, 3) }.First(c => !taken.Contains(c));
            BridgeWhere Square((int x, int y) c) => BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(c.x), new BridgeInt(c.y) }));

            var refused = await board.InputAsync(LookBoard.Primary, new string[0], Square(free));
            Assert.That(refused!.Succeeded, Is.False, "nothing is picked yet");
            Assert.That(await board.InputAsync(LookBoard.Primary, new string[0], BridgeWhere.At("hexes", new BridgeInt(1))), Is.Null,
                "no handler takes a place in another space");

            Assert.That((await board.PressAsync(new[] { "entity:" + mine }))!.Succeeded, Is.True);
            var moved = await board.InputAsync(LookBoard.Primary, new string[0], Square(free));
            Assert.That(moved!.Succeeded, Is.True, moved.ToString());
            Assert.That(moved.Deliveries.Single().Result.Succeeded, Is.True, moved.Deliveries.Single().Result.ToString());
            Assert.That((X(mine), Y(mine)), Is.EqualTo(free));
        }

        private long X(long unit) => ((IntValue)_world.Get(new EntityId(unit), _world.Member("Pos", "x"))).Value;

        private long Y(long unit) => ((IntValue)_world.Get(new EntityId(unit), _world.Member("Pos", "y"))).Value;

        [Test]
        public async Task ABotInBluesSeatPlaysItsTurnSoRedGetsTheNextOne()
        {
            await _host.Seat(_blue, "bot", ai: true);
            Assert.That(_world.SeatKindOf(new EntityId(_blue)), Is.EqualTo(SeatKind.Ai));
            var session = await _host.Open("bot");
            var skirmisher = await session.Bot("skirmish.skirmisher", _blue);
            var bot = new BotSeat(session, skirmisher);
            Assert.That(await bot.Run(), Is.EqualTo(0), "red's turn, nothing to answer");

            var alice = await Open("alice", _red);
            await alice.Renderer.Named("ask-end").Press();
            await alice.Renderer.Named("endturn").Press();
            Assert.That(alice.Renderer.Named("turn").Text, Is.EqualTo("2"));

            int acts = await bot.Run();
            Assert.That(acts, Is.GreaterThan(1), "blue moved or attacked before ending the turn");
            await alice.Ui.RefreshAsync();
            Assert.That(alice.Renderer.Named("turn").Text, Is.EqualTo("3"));
            Assert.That(alice.Renderer.Named("endturn").Enabled, Is.True, "red's turn again");
            Assert.That(await bot.Run(), Is.EqualTo(0));
        }

        [Test]
        public async Task TwoModBotsPlayAGameToTheEndAnsweringTheHitQuestions()
        {
            await _host.Seat(_red, "red-bot", ai: true);
            await _host.Seat(_blue, "blue-bot", ai: true);
            var redSession = await _host.Open("red-bot");
            var blueSession = await _host.Open("blue-bot");
            var red = new BotSeat(redSession, await redSession.Bot("skirmish.skirmisher", _red));
            var blue = new BotSeat(blueSession, await blueSession.Bot("skirmish.skirmisher", _blue));
            var match = _world.Member("Match", "winner");
            var game = _world.All("Game").Single();
            for (int round = 0; round < 200 && _world.Get(game, match) is NoneValue; round++)
                if (await red.Run() + await blue.Run() == 0) break;
            Assert.That(_world.Get(game, match), Is.Not.SameAs(NoneValue.Instance), "someone won; nobody got stuck on a question");
            Assert.That(_world.OpenRequests.Any(q => q.Choices.Count > 0), Is.False);
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
