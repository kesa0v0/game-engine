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
    /// 호스트 경계의 같은 프로세스 구현 (spec-gaps G325·G327): 세션은 그 연결로 읽고 내며, 화면 함수의 영수증을 키마다 들고
    /// 바뀐 키를 준다(G246). 운영은 자리·틱·저장·불러오기.
    /// </summary>
    public class LocalHostTests
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\n" +
            "deftype(\"Hero\", components=[\"Hp\", \"Seatable\"])\n" +
            "deftype(\"Unit\", components=[\"Hp\"])\n" +
            "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True))\n" +
            "bot_state(\"Tally\", n=field(int, default=0))\n";

        private const string Defs =
            "Hero(\"Red\")\nHero(\"Blue\")\nUnit(\"Soldier\")\n" +
            "@command(free=True, acts_for=None)\ndef Hit(target: Ref[Unit]):\n    with preconditions:\n" +
            "        choose(target, where=lambda t: True)\n" +
            "        require(target.Hp.v > 1, text(\"arena-too-weak\", hp=target.Hp.v))\n    target.Hp.v -= 1\n" +
            "@command(free=True, acts_for=None)\ndef Duel(h: Ref[Hero]):\n    r = ask(h, \"arena-duel\", choices=[1, 2])\n    h.Hp.v += r.choice\n" +
            // 첫 클릭은 고르기, 두 번째 클릭은 때리기
            "@ui_action\ndef click(u: Ref[Unit]):\n" +
            "    if ui.Selection.unit is None:\n        ui.Selection.unit = u\n" +
            "    else:\n        submit(\"Hit\", target=u)\n        ui.Selection.unit = None\n" +
            "def hp(u):\n    return u.Hp.v\n" +
            "def selected():\n    return ui.Selection.unit\n" +
            "def boom():\n    return 1 // 0\n" +
            // 늘 첫 유닛을 때린다: 체력이 1이 되면 그 수는 거절된다
            "@bot\ndef striker(me: Ref[Hero], times: int = 1):\n    bot.Tally.n += 1\n" +
            "    first = [u for u in query(Unit)][0]\n" +
            "    for i in range(times):\n        submit(\"Hit\", target=first)\n";

        private string _root = "";
        private LocalHost _host = null!;
        private IHostSession _alice = null!;
        private long _red, _blue, _a, _b;

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
            _red = world.Create("arena:Red").Value;
            _blue = world.Create("arena:Blue").Value;
            _a = world.Create("arena:Soldier").Value;
            _b = world.Create("arena:Soldier").Value;
            _host = new LocalHost(content, world);
            await _host.Seat(_red, "alice");
            _alice = await _host.Open("alice");
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

        private static IReadOnlyDictionary<string, BridgeValue> Args(params (string, BridgeValue)[] pairs) =>
            pairs.ToDictionary(p => p.Item1, p => p.Item2);

        private static IReadOnlyDictionary<string, BridgeValue> Target(long id) => Args(("target", new BridgeEntity(id, null)));

        private long Hp(long id) => ((IntValue)_host.World.Get(new EntityId(id), _host.World.Member("Hp", "v"))).Value;

        [Test]
        public async Task TheSessionListsTheModsBots()
        {
            var bot = (await _alice.Bots()).Single();
            Assert.That(bot.Name, Is.EqualTo("arena.striker"));
            Assert.That(bot.Entity, Is.EqualTo("Ref[\"Hero\"]"));
            var times = bot.Settings.Single();
            Assert.That((times.Name, times.Type, ((BridgeInt)times.Default!).Value), Is.EqualTo(("times", "int", 1L)));
        }

        [Test]
        public async Task AModBotPlaysUntilEveryMoveItWritesIsRefused()
        {
            var striker = await _alice.Bot("arena.striker", _red);
            var seat = new BotSeat(_alice, striker);
            // 3 → 2 → 1, 그다음 수는 require가 막아 한 일이 없다 (G377)
            Assert.That(await seat.Run(), Is.EqualTo(2), string.Join("; ", striker.LastProblems));
            Assert.That(Hp(_a), Is.EqualTo(1));
            Assert.That(striker.LastProblems.Single().Kind, Is.EqualTo(BridgeReasonKind.Require));
            Assert.That(await seat.Run(), Is.EqualTo(0), "the world did not change, so the bot is not asked again");
        }

        [Test]
        public async Task TheHostSavesAModBotAndBringsItBack()
        {
            var striker = await _alice.Bot("arena.striker", _red, Args(("times", new BridgeInt(2))), seed: 5);
            await striker.Act(_alice);
            var back = await _alice.LoadBot(await striker.Save());
            Assert.That(back.LastProblems, Is.Empty);
            Assert.That((back.Bot, back.Entity), Is.EqualTo(("arena.striker", _red)));
            Assert.That(await back.Save(), Is.EqualTo(await striker.Save()));
            Assert.ThrowsAsync<System.ArgumentException>(async () => await _alice.Bot("arena.strike", _red));
        }

        [Test]
        public async Task TheSameConnectionGetsTheSameSession()
        {
            Assert.That(await _host.Open("alice"), Is.SameAs(_alice));
            Assert.That(await _host.Open("bob"), Is.Not.SameAs(_alice));
        }

        [Test]
        public async Task CommandsCarryTheirParameters()
        {
            var commands = await _alice.Commands();
            Assert.That(commands.Select(c => c.Name), Is.EqualTo(new[] { "arena.Duel", "arena.Hit" }));
            var hit = commands[1];
            Assert.That(hit.Free, Is.True);
            Assert.That(hit.Parameters.Select(p => (p.Name, p.Type)), Is.EqualTo(new[] { ("target", "Ref[\"Unit\"]") }));
            Assert.That((await _alice.UiActions()).Single().Name, Is.EqualTo("arena.click"));
        }

        [Test]
        public async Task ACallKeepsItsReceiptUnderTheKey()
        {
            var call = await _alice.Call("hp-a", "arena.hp", new BridgeValue[] { new BridgeEntity(_a, null) });
            Assert.That(call.Value, Is.EqualTo(new BridgeInt(3)));
            await _alice.Call("hp-b", "arena.hp", new BridgeValue[] { new BridgeEntity(_b, null) });
            Assert.That(await _alice.Changed(), Is.Empty);

            Assert.That((await _alice.Submit("arena.Hit", Target(_a))).Succeeded, Is.True);
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "hp-a" }));

            Assert.That((await _alice.Call("hp-a", "arena.hp", new BridgeValue[] { new BridgeEntity(_a, null) })).Value,
                Is.EqualTo(new BridgeInt(2)));
            Assert.That(await _alice.Changed(), Is.Empty);
        }

        [Test]
        public async Task AFailedCallKeepsComingBackUntilUnbound()
        {
            var call = await _alice.Call("bad", "arena.boom", new BridgeValue[0]);
            Assert.That(call.Succeeded, Is.False);
            Assert.That(call.Error, Is.Not.Empty);
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "bad" }));
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "bad" }));
            await _alice.Unbind("bad");
            Assert.That(await _alice.Changed(), Is.Empty);
        }

        [Test]
        public async Task EntitiesCrossWithTheirDef()
        {
            var seats = await _alice.Seats();
            Assert.That(seats.Single(), Is.EqualTo(new BridgeEntity(_red, null)));
            Assert.That(seats.Single().Def, Is.EqualTo("arena:Red"));
        }

        [Test]
        public async Task AFailedRequireCarriesTheModText()
        {
            await _alice.Submit("arena.Hit", Target(_a));
            await _alice.Submit("arena.Hit", Target(_a));
            var result = await _alice.Submit("arena.Hit", Target(_a));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Outcome, Is.EqualTo("PreconditionsFailed"));
            var reason = result.Reasons.Single();
            Assert.That(reason.Kind, Is.EqualTo(BridgeReasonKind.Require));
            Assert.That(reason.Text!.Key, Is.EqualTo("arena-too-weak"));
            Assert.That(reason.Text.Args.Single(), Is.EqualTo(new KeyValuePair<string, BridgeValue>("hp", new BridgeInt(1))));
        }

        [Test]
        public async Task WrongArgumentsAreReasons()
        {
            var result = await _alice.Submit("arena.Hit", Target(_red));
            Assert.That(result.Outcome, Is.EqualTo("ArgumentsRejected"));
            Assert.That(result.Reasons.Single().Kind, Is.EqualTo(BridgeReasonKind.Argument));
            Assert.That(result.Reasons.Single().Parameter, Is.EqualTo("target"));
        }

        [Test]
        public async Task CheckSeesThePreconditionsWithoutDoingAnything()
        {
            var passes = await _alice.Check("arena.Hit", Target(_a));
            Assert.That((passes.Succeeded, passes.Outcome, passes.Reasons.Count), Is.EqualTo((true, "Passed", 0)));
            Assert.That(Hp(_a), Is.EqualTo(3), "checking does not hit");

            _host.World.Set(new EntityId(_a), _host.World.Member("Hp", "v"), new IntValue(1));
            var fails = await _alice.Check("arena.Hit", Target(_a));
            Assert.That((fails.Succeeded, fails.Outcome), Is.EqualTo((false, "PreconditionsFailed")));
            var reason = fails.Reasons.Single();
            Assert.That(reason.Kind, Is.EqualTo(BridgeReasonKind.Require));
            Assert.That(reason.Text!.Key, Is.EqualTo("arena-too-weak"));
            Assert.That(Hp(_a), Is.EqualTo(1));
        }

        [Test]
        public async Task CheckTakesAUiActionByNameAndReportsWrongArgumentsAndUnknownNames()
        {
            var action = await _alice.Check("arena.click", Args(("u", new BridgeEntity(_a, null))));
            Assert.That(action.Outcome, Is.EqualTo("Passed"));

            var wrong = await _alice.Check("arena.Hit", Target(_red));
            Assert.That(wrong.Outcome, Is.EqualTo("ArgumentsRejected"));
            Assert.That(wrong.Reasons.Single().Parameter, Is.EqualTo("target"));

            var unknown = await _alice.Check("arena.nope", Target(_a));
            Assert.That(unknown.Succeeded, Is.False);
            Assert.That(unknown.Reasons.Single().Kind, Is.EqualTo(BridgeReasonKind.Error));
        }

        [Test]
        public async Task VersionMovesWhenTheWorldChanges()
        {
            long before = await _alice.Version();
            Assert.That(await _alice.Version(), Is.EqualTo(before), "reading does not move it");
            await _alice.Submit("arena.Hit", Target(_a));
            Assert.That(await _alice.Version(), Is.GreaterThan(before));
        }

        [Test]
        public async Task UiVersionMovesWhenThisConnectionsUiRecordChanges()
        {
            var bob = await _host.Open("bob");
            long world = await _alice.Version();
            long mine = await _alice.UiVersion();
            long theirs = await bob.UiVersion();
            await _alice.Act("arena.click", Args(("u", new BridgeEntity(_a, null))));
            Assert.That(await _alice.UiVersion(), Is.GreaterThan(mine));
            Assert.That(await _alice.Version(), Is.EqualTo(world), "a screen action does not move the world stamp");
            Assert.That(await bob.UiVersion(), Is.EqualTo(theirs), "each connection has its own UI record");
        }

        [Test]
        public async Task LegalMovesCrossAsData()
        {
            var moves = await _alice.LegalMoves("arena.Hit", Args(), 10);
            Assert.That(moves.Moves.Select(m => m.Arguments["target"]).Cast<BridgeEntity>().Select(e => e.Id), Is.EquivalentTo(new[] { _a, _b }));
            Assert.That(moves.Moves.All(m => m.Command == "arena.Hit"), Is.True);

            var unknown = await _alice.LegalMoves("arena.Nope", Args(), 10);
            Assert.That(unknown.Moves, Is.Empty);
            Assert.That(unknown.Problems.Single().Kind, Is.EqualTo(BridgeReasonKind.Error));
        }

        [Test]
        public async Task AnActionDeliversItsSubmissions()
        {
            await _alice.Call("sel", "arena.selected", new BridgeValue[0]);

            var first = await _alice.Act("arena.click", Args(("u", new BridgeEntity(_a, null))));
            Assert.That(first.Succeeded, Is.True, first.ToString());
            Assert.That(first.Deliveries, Is.Empty);
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "sel" }), "the session's UI state changed");

            var second = await _alice.Act("arena.click", Args(("u", new BridgeEntity(_b, null))));
            var delivery = second.Deliveries.Single();
            Assert.That(delivery.Command, Is.EqualTo("arena.Hit"));
            Assert.That(delivery.Arguments["target"], Is.EqualTo(new BridgeEntity(_b, null)));
            Assert.That(delivery.Result.Succeeded, Is.True, delivery.Result.ToString());
            Assert.That(Hp(_b), Is.EqualTo(2));
        }

        [Test]
        public async Task EachSessionHasItsOwnUiState()
        {
            await _host.Seat(_blue, "bob");
            var bob = await _host.Open("bob");
            await _alice.Act("arena.click", Args(("u", new BridgeEntity(_a, null))));
            Assert.That((await _alice.Call("s", "arena.selected", new BridgeValue[0])).Value, Is.EqualTo(new BridgeEntity(_a, null)));
            Assert.That((await bob.Call("s", "arena.selected", new BridgeValue[0])).Value, Is.EqualTo(BridgeNone.Instance));
        }

        [Test]
        public async Task ARequestIsAnsweredWithAPick()
        {
            Assert.That((await _alice.Submit("arena.Duel", Args(("h", new BridgeEntity(_red, null))))).Succeeded, Is.True);
            var request = (await _alice.OpenRequests()).Single();
            Assert.That(request.Who.Single().Def, Is.EqualTo("arena:Red"));
            Assert.That(request.Title, Is.EqualTo(new BridgeStr("arena-duel")));
            Assert.That(request.Choices.Select(c => c.Value), Is.EqualTo(new BridgeValue[] { new BridgeInt(1), new BridgeInt(2) }));

            var moves = await _alice.LegalResponses(request.Id, 10);
            var picks = moves.Moves.Where(m => m.Command == BridgeNames.Pick).ToList();
            Assert.That(picks.Select(m => m.Arguments[BridgeNames.PickChoice]), Is.EqualTo(new BridgeValue[] { new BridgeInt(0), new BridgeInt(1) }));
            Assert.That(picks.All(m => m.By!.Id == _red), Is.True);

            var answered = await _alice.Respond(request.Id, BridgeNames.Pick, picks[1].Arguments);
            Assert.That(answered.Succeeded, Is.True, answered.ToString());
            Assert.That(Hp(_red), Is.EqualTo(5));
            Assert.That(await _alice.OpenRequests(), Is.Empty);
        }

        [Test]
        public async Task OthersDoNotSeeTheRequest()
        {
            await _alice.Submit("arena.Duel", Args(("h", new BridgeEntity(_red, null))));
            var bob = await _host.Open("bob");
            Assert.That(await bob.OpenRequests(), Is.Empty);
            var moves = await bob.LegalResponses((await _alice.OpenRequests()).Single().Id, 10);
            Assert.That(moves.Moves, Is.Empty);
            Assert.That(moves.Problems, Is.Not.Empty);
        }

        [Test]
        public async Task SeatChoicesShowWhoSits()
        {
            var seats = await _host.SeatChoices();
            Assert.That(seats.Select(s => (s.Entity.Id, s.Entity.Def, s.Connection)),
                Is.EqualTo(new[] { (_red, "arena:Red", "alice"), (_blue, "arena:Blue", (string?)null) }));
            await _host.Unseat(_red);
            Assert.That(await _alice.Seats(), Is.Empty);
        }

        [Test]
        public async Task LoadingReplacesTheWorldAndEveryKeyIsChanged()
        {
            await _alice.Call("hp-a", "arena.hp", new BridgeValue[] { new BridgeEntity(_a, null) });
            var save = await _host.Save();
            await _alice.Submit("arena.Hit", Target(_a));
            Assert.That(Hp(_a), Is.EqualTo(2));

            var loaded = await _host.Load(save);
            Assert.That(loaded.Succeeded, Is.True, string.Join("\n", loaded.Problems));
            Assert.That(Hp(_a), Is.EqualTo(3));
            Assert.That(await _alice.Changed(), Is.EqualTo(new[] { "hp-a" }));
            Assert.That((await _alice.Seats()).Single().Id, Is.EqualTo(_red), "seats are saved");
        }

        [Test]
        public async Task ABadSaveLeavesTheWorld()
        {
            var before = _host.World;
            var loaded = await _host.Load(new byte[] { 1, 2, 3 });
            Assert.That(loaded.Succeeded, Is.False);
            Assert.That(loaded.Problems, Is.Not.Empty);
            Assert.That(_host.World, Is.SameAs(before));
        }

        [Test]
        public async Task AdvanceMovesTheClock()
        {
            var advanced = await _host.Advance(3);
            Assert.That(advanced.Tick, Is.EqualTo(3));
            Assert.That(await _alice.Tick(), Is.EqualTo(3));
        }
    }
}
