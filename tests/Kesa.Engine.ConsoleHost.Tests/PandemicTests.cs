using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.ConsoleHost.Tests
{
    /// <summary>예시 게임 <c>samples/pandemic</c>(협력 보드게임 따라 해 보기) 1조각: 지도와 말.</summary>
    public class PandemicTests
    {
        private static string Root()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "samples", "pandemic");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("samples/pandemic");
        }

        private World _w = null!;
        private EntityId _alice, _bob;

        private static ContentLoadResult Load(out List<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            string root = Root();
            var content = ContentLoader.Load(root, ModSorter.Sort(ModDiscovery.Discover(root, bag), bag), bag);
            diagnostics = bag.ToSortedList().ToList();
            return content;
        }

        [SetUp]
        public void Open()
        {
            _w = new World(Load(out _), seed: 3);
            Assert.That(_w.Submit("host", "pandemic.Start", new Dictionary<string, Value>()).Succeeded, Is.True);
            _alice = _w.All("Player").Single(p => _w.DefOf(p) == "pandemic:Alice");
            _bob = _w.All("Player").Single(p => _w.DefOf(p) == "pandemic:Bob");
            _w.Seat(_alice, "alice");
            _w.Seat(_bob, "bob");
        }

        private string CityOf(EntityId player)
        {
            var city = (EntityValue)_w.Get(player, _w.Member("At", "city"));
            return _w.DefOf(city.Id);
        }

        private long Left(EntityId player) => ((IntValue)_w.Get(player, _w.Member("Actions", "left"))).Value;

        private AnswerResult Move(string seat, EntityId player, string to) =>
            _w.Respond(seat, _w.OpenRequests.Single().Id, "pandemic.Move", new Dictionary<string, Value>
                { ["player"] = new EntityValue(player), ["to"] = new StrValue(to) });

        [Test]
        public void TheModLoadsClean()
        {
            Load(out var diagnostics);
            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
        }

        [Test]
        public void BothPawnsStartInAtlantaWithFourActions()
        {
            Assert.That(CityOf(_alice), Is.EqualTo("pandemic:Atlanta"));
            Assert.That(CityOf(_bob), Is.EqualTo("pandemic:Atlanta"));
            Assert.That(Left(_alice), Is.EqualTo(4));
        }

        [Test]
        public void AMoveFollowsALinkAndCostsAnAction()
        {
            Assert.That(Move("alice", _alice, "pandemic:Chicago").Succeeded, Is.True);
            Assert.That(CityOf(_alice), Is.EqualTo("pandemic:Chicago"));
            Assert.That(Left(_alice), Is.EqualTo(3));
        }

        [Test]
        public void ACityWithoutALinkCannotBeReached()
        {
            Assert.That(Move("alice", _alice, "pandemic:Lima").Succeeded, Is.False);
            Assert.That(CityOf(_alice), Is.EqualTo("pandemic:Atlanta"));
        }

        [Test]
        public void AfterFourMovesTheTurnPassesToTheOtherPlayer()
        {
            foreach (var to in new[] { "Chicago", "Atlanta", "Chicago", "Atlanta" })
                Assert.That(Move("alice", _alice, "pandemic:" + to).Succeeded, Is.True);
            Assert.That(Left(_alice), Is.EqualTo(4));
            Assert.That(Move("bob", _bob, "pandemic:Miami").Succeeded, Is.True);
            Assert.That(Left(_bob), Is.EqualTo(3));
        }

        [Test]
        public void ThePlayerWhoseTurnItIsNotCannotMove()
        {
            Assert.That(Move("bob", _bob, "pandemic:Miami").Succeeded, Is.False);
        }

        [Test]
        public void EveryLinkIsMutual()
        {
            var links = new Dictionary<string, List<string>>();
            foreach (var city in _w.All("City"))
            {
                string name = _w.DefOf(city);
                links[name] = ((ListValue)_w.Get(city, _w.Member("City", "neighbors"))).Items.Select(i => ((StrValue)i).Value).ToList();
            }
            foreach (var pair in links)
                foreach (var other in pair.Value)
                    Assert.That(links[other], Does.Contain(pair.Key), pair.Key + " -> " + other);
        }

        // ---- 감염 (2조각) ----

        private EntityId City(string name) => _w.All("City").Single(e => _w.DefOf(e) == "pandemic:" + name);

        private long Cubes(string city, string disease) =>
            ((IntValue)_w.GetRow(City(city), _w.Member("Cubes", "count"), "pandemic:" + disease)).Value;

        private void SetCubes(string city, string disease, long n) =>
            _w.SetRow(City(city), _w.Member("Cubes", "count"), "pandemic:" + disease, new IntValue(n));

        private long Total(string city) => ((IntValue)_w.Get(City(city), _w.Member("Load", "total"))).Value;

        private EntityId Game() => _w.All("Game").Single();

        private long Outbreaks() => ((IntValue)_w.Get(Game(), _w.Member("Match", "outbreaks"))).Value;


        private IEnumerable<EntityId> Cards() => _w.All("InfectionCard");

        private string CardCity(EntityId card)
        {
            var city = (EntityValue)_w.Get(card, _w.Member("Face", "city"));
            return _w.DefOf(city.Id).Substring("pandemic:".Length);
        }

        /// <summary>감염 덱을 이 도시들의 카드만 뽑을 더미에 남긴다.</summary>
        private void DrawOnly(params string[] cities)
        {
            foreach (var card in Cards())
                _w.Set(card, _w.Member("Card", "pile"), new IntValue(cities.Contains(CardCity(card)) ? 0 : 1));
        }

        private void ClearCubes()
        {
            foreach (var city in _w.All("City"))
                foreach (var d in new[] { "Blue", "Yellow" })
                    _w.SetRow(city, _w.Member("Cubes", "count"), "pandemic:" + d, new IntValue(0));
        }

        private AnswerResult EndTurn(string seat, EntityId player) =>
            _w.Respond(seat, _w.OpenRequests.Single().Id, "pandemic.EndTurn", new Dictionary<string, Value> { ["player"] = new EntityValue(player) });

        [Test]
        public void TheGameStartsWithNineInfectedCities()
        {
            var counts = _w.All("City").Select(c => ((IntValue)_w.Get(c, _w.Member("Load", "total"))).Value).OrderBy(n => n).ToList();
            Assert.That(counts, Is.EqualTo(new long[] { 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3 }));
            Assert.That(Cards().Count(c => ((IntValue)_w.Get(c, _w.Member("Card", "pile"))).Value == 1), Is.EqualTo(9));
        }

        [Test]
        public void EndingTheTurnInfectsAsManyCitiesAsTheRate()
        {
            long before = _w.All("City").Sum(c => ((IntValue)_w.Get(c, _w.Member("Load", "total"))).Value);
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            long after = _w.All("City").Sum(c => ((IntValue)_w.Get(c, _w.Member("Load", "total"))).Value);
            Assert.That(after - before, Is.EqualTo(2)); // 남은 세 장은 큐브가 없는 도시라 폭발이 없다
        }

        [Test]
        public void TheInfectionRateFollowsTheEpidemicsDerived()
        {
            var rate = _w.Member("Match", "rate");
            var epidemics = _w.Member("Match", "epidemics");
            foreach (var (count, expected) in new[] { (0L, 2L), (3L, 3L), (5L, 4L), (9L, 4L) })
            {
                _w.Set(Game(), epidemics, new IntValue(count));
                Assert.That(((IntValue)_w.Get(Game(), rate)).Value, Is.EqualTo(expected), "epidemics " + count);
            }
        }

        [Test]
        public void ADangerousCityHasThreeOfADisease()
        {
            ClearCubes();
            var danger = _w.Member("Load", "danger");
            Assert.That(((BoolValue)_w.Get(City("Lima"), danger)).Value, Is.False);
            SetCubes("Lima", "Yellow", 2);
            Assert.That(((BoolValue)_w.Get(City("Lima"), danger)).Value, Is.False);
            SetCubes("Lima", "Yellow", 3);
            Assert.That(((BoolValue)_w.Get(City("Lima"), danger)).Value, Is.True);
            Assert.That(Total("Lima"), Is.EqualTo(3));
        }

        [Test]
        public void AnOutbreakSpreadsToNeighboursAndChainsOncePerCity()
        {
            ClearCubes();
            SetCubes("Atlanta", "Blue", 3);
            SetCubes("Washington", "Blue", 3);
            DrawOnly("Atlanta", "SaoPaulo");
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            // 애틀랜타가 터져 이웃(시카고·워싱턴·마이애미)에 파랑 하나씩, 워싱턴이 넘쳐 터지고(연쇄), 애틀랜타는 다시 안 터진다
            Assert.That(Outbreaks(), Is.EqualTo(2));
            Assert.That(Cubes("Chicago", "Blue"), Is.EqualTo(1));
            Assert.That(Cubes("Miami", "Blue"), Is.EqualTo(2)); // 애틀랜타와 워싱턴 둘 다의 이웃
            Assert.That(Cubes("Montreal", "Blue"), Is.EqualTo(1));
            Assert.That(Cubes("NewYork", "Blue"), Is.EqualTo(1));
            Assert.That(Cubes("Atlanta", "Blue"), Is.EqualTo(3));
            Assert.That(Cubes("SaoPaulo", "Yellow"), Is.EqualTo(1));
        }


        [Test]
        public void EachInfectionCardStartsItsOwnChain()
        {
            ClearCubes();
            SetCubes("Atlanta", "Blue", 3);
            SetCubes("Washington", "Blue", 3);
            DrawOnly("Atlanta", "Washington");
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            // 첫 카드의 연쇄(애틀랜타·워싱턴 둘) 뒤 둘째 카드가 새 연쇄를 시작해, 이미 터졌던 도시도 다시 터진다
            // (이어서 마이애미가 3개에서 넘쳐 같이 터진다): 2 + 3
            Assert.That(Outbreaks(), Is.EqualTo(5));
        }

        // ---- 숨은 덱 (3조각) ----

        private IEnumerable<EntityId> DrawPile() =>
            Cards().Where(c => ((IntValue)_w.Get(c, _w.Member("Card", "pile"))).Value == 0);

        [Test]
        public void TheDrawPileIsFaceDownForEveryone()
        {
            var pile = DrawPile().ToList();
            Assert.That(pile, Has.Count.EqualTo(3));
            foreach (var seat in new[] { "alice", "bob" })
                foreach (var card in pile)
                {
                    Assert.That(_w.ViewAs(seat).Sees(card), Is.True); // 카드가 있다는 것과
                    Assert.That(_w.ViewAs(seat).Sees(card, "Face"), Is.False); // 어느 도시인지는 모른다
                }
            Assert.That(_w.ViewAs("watcher").Sees(pile[0], "Face"), Is.False);
        }

        [Test]
        public void TheDiscardPileIsFaceUp()
        {
            var discard = Cards().Except(DrawPile()).ToList();
            Assert.That(discard, Has.Count.EqualTo(9));
            foreach (var card in discard) Assert.That(_w.ViewAs("alice").Sees(card, "Face"), Is.True);
        }

        [Test]
        public void ADrawnCardTurnsFaceUp()
        {
            var next = DrawPile().OrderBy(c => ((IntValue)_w.Get(c, _w.Member("Card", "slot"))).Value).First();
            string city = CardCity(next); // 규칙은 실제 상태를 다 읽는다
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            Assert.That(_w.ViewAs("bob").Sees(next, "Face"), Is.True);
            Assert.That(CardCity(next), Is.EqualTo(city));
        }

        [Test]
        public void TheScreenShowsOnlyTheFaceUpCards()
        {
            var seen = _w.ViewAs("alice").Call("pandemic.infection_deck", new EntityValue(_alice)).Value;
            var items = ((DictValue)seen).Items;
            Assert.That(((IntValue)items.Single(i => ((StrValue)i.Key).Value == "draw").Value).Value, Is.EqualTo(3));
            var faces = ((ListValue)items.Single(i => ((StrValue)i.Key).Value == "discard").Value).Items.Select(v => ((StrValue)v).Value).ToList();
            Assert.That(faces, Has.Count.EqualTo(9));
            foreach (var card in DrawPile()) Assert.That(faces, Does.Not.Contain("pandemic:" + CardCity(card)));
        }

        [Test]
        public void CardsAreDrawnFromTheTopOfTheDeck()
        {
            var order = DrawPile().OrderBy(c => ((IntValue)_w.Get(c, _w.Member("Card", "slot"))).Value).Select(CardCity).ToList();
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            var drawn = Cards().Where(c => order.Take(2).Contains(CardCity(c)))
                .Select(c => ((IntValue)_w.Get(c, _w.Member("Card", "pile"))).Value);
            Assert.That(drawn, Is.All.EqualTo(1));
            Assert.That(DrawPile().Select(CardCity), Is.EqualTo(new[] { order[2] }));
        }

        // ---- 플레이어 덱, 손패, 행동 (4a조각) ----

        private IEnumerable<EntityId> PlayerCards() => _w.All("PlayerCard");

        private long PileOf(EntityId card) => ((IntValue)_w.Get(card, _w.Member("Card", "pile"))).Value;

        private string FaceCity(EntityId card)
        {
            var city = (EntityValue)_w.Get(card, _w.Member("Face", "city"));
            return _w.DefOf(city.Id).Substring("pandemic:".Length);
        }

        private List<EntityId> HandOf(EntityId player) =>
            PlayerCards().Where(c => PileOf(c) == 2 && ((EntityValue)_w.Get(c, _w.Member("Held", "holder"))).Id.Equals(player)).ToList();

        private long DeckSize() => PlayerCards().Count(c => PileOf(c) == 0);

        /// <summary>그 도시의 플레이어 카드를 이 플레이어의 손에 쥐여 준다 (이미 어디 있든).</summary>
        private EntityId Give(string city, EntityId player)
        {
            var card = PlayerCards().Single(c => FaceCity(c) == city);
            _w.Set(card, _w.Member("Card", "pile"), new IntValue(2));
            _w.Set(card, _w.Member("Held", "holder"), new EntityValue(player));
            return card;
        }

        private AnswerResult Act(string seat, string command, params (string Name, Value Value)[] args) =>
            _w.Respond(seat, _w.OpenRequests.Single().Id, "pandemic." + command, args.ToDictionary(a => a.Name, a => a.Value));

        private bool DeckOut() => ((BoolValue)_w.Get(Game(), _w.Member("Match", "deck_out"))).Value;

        [Test]
        public void EachPlayerStartsWithFourCardsAndTheRestIsTheDeck()
        {
            Assert.That(HandOf(_alice), Has.Count.EqualTo(4));
            Assert.That(HandOf(_bob), Has.Count.EqualTo(4));
            Assert.That(DeckSize(), Is.EqualTo(4));
            Assert.That(((IntValue)_w.Get(_alice, _w.Member("Hand", "size"))).Value, Is.EqualTo(4));
        }

        [Test]
        public void HandsAreOpenButTheDeckIsFaceDown()
        {
            foreach (var card in HandOf(_alice)) Assert.That(_w.ViewAs("bob").Sees(card, "Face"), Is.True);
            foreach (var card in PlayerCards().Where(c => PileOf(c) == 0)) Assert.That(_w.ViewAs("bob").Sees(card, "Face"), Is.False);
        }

        [Test]
        public void EndingTheTurnDrawsTwoPlayerCards()
        {
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            Assert.That(HandOf(_alice), Has.Count.EqualTo(6));
            Assert.That(DeckSize(), Is.EqualTo(2));
        }

        [Test]
        public void AnEmptyDeckLosesTheGame()
        {
            ClearCubes();
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            Assert.That(EndTurn("bob", _bob).Succeeded, Is.True);
            Assert.That(DeckSize(), Is.EqualTo(0));
            Assert.That(DeckOut(), Is.False);
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            Assert.That(DeckOut(), Is.True);
            Assert.That(_w.OpenRequests, Is.Empty);
        }

        [Test]
        public void AHandOverTheLimitMustDiscardBeforeTheTurnPasses()
        {
            ClearCubes();
            foreach (var card in HandOf(_bob).Take(3)) // 밥의 카드 셋을 받아 4 + 3 = 7장, 뽑으면 9장
                _w.Set(card, _w.Member("Held", "holder"), new EntityValue(_alice));
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            var ask = _w.OpenRequests.Single();
            Assert.That(ask.Who, Does.Contain(_alice));
            Assert.That(((BoolValue)_w.Get(_alice, _w.Member("Hand", "over"))).Value, Is.True);
            var first = HandOf(_alice).First();
            Assert.That(Act("alice", "Discard", ("player", new EntityValue(_alice)), ("card", new EntityValue(first))).Succeeded, Is.True);
            Assert.That(_w.OpenRequests.Single().Who, Does.Contain(_alice)); // 8장이라 한 장 더
            var second = HandOf(_alice).First();
            Assert.That(Act("alice", "Discard", ("player", new EntityValue(_alice)), ("card", new EntityValue(second))).Succeeded, Is.True);
            Assert.That(_w.OpenRequests.Single().Who, Does.Contain(_bob)); // 7장이 되어 차례가 넘어감
            Assert.That(HandOf(_alice), Has.Count.EqualTo(7));
        }

        [Test]
        public void ADirectFlightSpendsTheCardOfTheDestination()
        {
            var miami = Give("Miami", _alice);
            Assert.That(Act("alice", "DirectFlight", ("player", new EntityValue(_alice)), ("card", new EntityValue(miami))).Succeeded, Is.True);
            Assert.That(CityOf(_alice), Is.EqualTo("pandemic:Miami"));
            Assert.That(PileOf(miami), Is.EqualTo(1));
            Assert.That(Left(_alice), Is.EqualTo(3));
        }

        [Test]
        public void ADirectFlightCannotUseTheCardOfTheCurrentCity()
        {
            var here = Give("Atlanta", _alice);
            Assert.That(Act("alice", "DirectFlight", ("player", new EntityValue(_alice)), ("card", new EntityValue(here))).Succeeded, Is.False);
        }

        [Test]
        public void ABuildingNeedsTheCardOfTheCurrentCity()
        {
            var atlanta = Give("Atlanta", _alice);
            Assert.That(Act("alice", "BuildStation", ("player", new EntityValue(_alice)), ("card", new EntityValue(atlanta))).Succeeded, Is.True);
            Assert.That(((BoolValue)_w.Get(City("Atlanta"), _w.Member("Research", "station"))).Value, Is.True);
            Assert.That(PileOf(atlanta), Is.EqualTo(1));
            var other = Give("Miami", _alice);
            Assert.That(Act("alice", "BuildStation", ("player", new EntityValue(_alice)), ("card", new EntityValue(other))).Succeeded, Is.False);
        }

        [Test]
        public void TreatingTakesOneCubeOfThatDisease()
        {
            ClearCubes();
            SetCubes("Atlanta", "Blue", 2);
            var blue = new StrValue("pandemic:Blue");
            Assert.That(Act("alice", "Treat", ("player", new EntityValue(_alice)), ("disease", new StrValue("pandemic:Yellow"))).Succeeded, Is.False);
            Assert.That(Act("alice", "Treat", ("player", new EntityValue(_alice)), ("disease", blue)).Succeeded, Is.True);
            Assert.That(Cubes("Atlanta", "Blue"), Is.EqualTo(1));
            Assert.That(Left(_alice), Is.EqualTo(3));
        }
        [Test]
        public void TooManyOutbreaksLoseTheGame()
        {
            foreach (var city in _w.All("City"))
                foreach (var d in new[] { "Blue", "Yellow" })
                    _w.SetRow(city, _w.Member("Cubes", "count"), "pandemic:" + d, new IntValue(3));
            Assert.That(EndTurn("alice", _alice).Succeeded, Is.True);
            Assert.That(Outbreaks(), Is.EqualTo(12)); // 첫 카드의 연쇄가 지도 전체를 훑고, 졌으니 둘째 카드는 뽑지 않는다
            Assert.That(((BoolValue)_w.Get(Game(), _w.Member("Match", "lost"))).Value, Is.True);
            Assert.That(_w.OpenRequests, Is.Empty);
        }
    }
}
