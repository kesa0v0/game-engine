using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.ConsoleHost.Tests
{
    /// <summary>
    /// 판데믹 확장 모드 <c>samples/pandemic/brink</c>: 코어 파일을 건드리지 않고 도시와 역할을 더한다. 코어는 도시·질병을 <c>defs()</c>로 돌아
    /// 확장의 Def가 판에 그대로 깔린다 (spec-gaps G305).
    /// </summary>
    public class BrinkTests
    {
        private World _w = null!;
        private EntityId _alice, _bob;

        [SetUp]
        public void Open()
        {
            _w = new World(PandemicTests.Load(out _, "pandemic", "brink"), seed: 3);
            Assert.That(_w.Submit("host", "pandemic.Start", new Dictionary<string, Value>()).Succeeded, Is.True);
            _alice = _w.All("Player").Single(p => _w.DefOf(p) == "pandemic:Alice");
            _bob = _w.All("Player").Single(p => _w.DefOf(p) == "pandemic:Bob");
            _w.Seat(_alice, "alice");
            _w.Seat(_bob, "bob");
        }

        private EntityId City(string id) => _w.All("City").Single(e => _w.DefOf(e) == id);

        private string FaceCity(EntityId card) =>
            _w.Get(card, _w.Member("Face", "city")) is EntityValue city ? _w.DefOf(city.Id) : "";

        private AnswerResult Act(string seat, string command, params (string Name, Value Value)[] args) =>
            _w.Respond(seat, _w.OpenRequests.Single().Id, "pandemic." + command, args.ToDictionary(a => a.Name, a => a.Value));

        private long Actions(EntityId player, string member) => ((IntValue)_w.Get(player, _w.Member("Actions", member))).Value;

        [Test]
        public void BothModsLoadClean()
        {
            PandemicTests.Load(out var diagnostics, "pandemic", "brink");
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void TheNewCitiesAreOnTheBoardWithTheirCards()
        {
            var cities = _w.All("City").Select(c => _w.DefOf(c)).ToList();
            Assert.That(cities, Has.Count.EqualTo(14));
            Assert.That(cities.Skip(12), Is.EqualTo(new[] { "brink:BuenosAires", "brink:Santiago" }), "after the core's cities, in load order");
            Assert.That(_w.All("InfectionCard").Select(FaceCity), Does.Contain("brink:Santiago"));
            Assert.That(_w.All("PlayerCard").Select(FaceCity).Where(c => c.Length > 0).Distinct().Count(), Is.EqualTo(14));
        }

        [Test]
        public void ThePatchedLinksAreMutual()
        {
            var links = _w.All("City").ToDictionary(c => _w.DefOf(c),
                c => ((ListValue)_w.Get(c, _w.Member("City", "neighbors"))).Items.Select(i => ((StrValue)i).Value).ToList());
            foreach (var pair in links)
            foreach (var near in pair.Value)
                Assert.That(links[near], Does.Contain(pair.Key), pair.Key + " -> " + near);
            Assert.That(links["pandemic:Bogota"], Does.Contain("brink:BuenosAires"));
        }

        [Test]
        public void APawnWalksIntoANewCity()
        {
            _w.Set(_alice, _w.Member("At", "city"), new EntityValue(City("pandemic:Lima")));
            Assert.That(Act("alice", "Move", ("player", new EntityValue(_alice)), ("to", new StrValue("brink:Santiago"))).Succeeded, Is.True);
            Assert.That(((EntityValue)_w.Get(_alice, _w.Member("At", "city"))).Id, Is.EqualTo(City("brink:Santiago")));
        }

        [Test]
        public void AGeneralistGetsFiveActionsATurn()
        {
            foreach (var city in _w.All("City"))
                foreach (var d in new[] { "pandemic:Blue", "pandemic:Yellow" })
                    _w.SetRow(city, _w.Member("Cubes", "count"), d, new IntValue(0));
            _w.AddRow(_alice, "Roles", "brink:Generalist");
            Assert.That(Actions(_alice, "per_turn"), Is.EqualTo(5));
            Assert.That(Actions(_bob, "per_turn"), Is.EqualTo(4));
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(Actions(_alice, "left"), Is.EqualTo(5), "the next turn starts with per_turn actions");
        }

        // ---- 변종 바이러스 ----

        private EntityId Game() => _w.All("Game").Single();
        private long Outbreaks() => ((IntValue)_w.Get(Game(), _w.Member("Match", "outbreaks"))).Value;
        private EntityId Disease(string id) => _w.All("Disease").Single(e => _w.DefOf(e) == id);

        private IEnumerable<EntityId> EpidemicCards() =>
            _w.All("PlayerCard").Where(c => ((BoolValue)_w.Get(c, _w.Member("Face", "epidemic"))).Value);

        private string EffectOf(EntityId card) => ((StrValue)_w.Get(card, _w.Member("StrainCard", "effect"))).Value;

        private bool Done(EntityId card) => ((BoolValue)_w.Get(card, _w.Member("StrainCard", "done"))).Value;

        /// <summary>아직 마치지 않은 에픽 카드 하나에 이 효과를 붙이고 마친 것으로 둔다: 그 지속 효과가 켜진 판.</summary>
        private void InEffect(string effect)
        {
            var card = EpidemicCards().First(c => !Done(c));
            _w.Set(card, _w.Member("StrainCard", "effect"), new StrValue("brink:" + effect));
            _w.Set(card, _w.Member("StrainCard", "done"), BoolValue.True);
        }

        private void ClearCubes()
        {
            foreach (var city in _w.All("City"))
                foreach (var d in new[] { "pandemic:Blue", "pandemic:Yellow" })
                    _w.SetRow(city, _w.Member("Cubes", "count"), d, new IntValue(0));
        }

        /// <summary>감염 덱에서 이 도시들의 카드만 뽑을 더미에 남기고, 순서대로 맨 위부터 놓는다.</summary>
        private void DrawOnly(params string[] cities)
        {
            foreach (var card in _w.All("InfectionCard"))
            {
                int i = System.Array.IndexOf(cities, FaceCity(card));
                _w.Set(card, _w.Member("Card", "pile"), new IntValue(i >= 0 ? 0 : 1));
                if (i >= 0) _w.Set(card, _w.Member("Card", "slot"), new IntValue(-100 + i));
            }
        }

        [Test]
        public void AVirulentOutbreakCountsTwiceAndCanLoseAtOnce()
        {
            // 폭발 6에서 변종(파랑)이 터지면 8: 그 자리에서 지고 둘째 카드는 뽑지 않는다 (코어의 "여덟째 폭발" 시험과 같은 판)
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            InEffect("SlipperySlope");
            _w.Set(Game(), _w.Member("Match", "outbreaks"), new IntValue(6));
            _w.SetRow(City("pandemic:Atlanta"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(3));
            DrawOnly("pandemic:Atlanta", "pandemic:SaoPaulo");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(Outbreaks(), Is.EqualTo(8));
            Assert.That(((IntValue)_w.GetRow(City("pandemic:SaoPaulo"), _w.Member("Cubes", "count"), "pandemic:Yellow")).Value, Is.EqualTo(0),
                "lost before the second card");
            Assert.That(_w.OpenRequests, Is.Empty, "the game is over");
        }

        private long CubeCount(string city, string disease) =>
            ((IntValue)_w.GetRow(City(city), _w.Member("Cubes", "count"), disease)).Value;

        [Test]
        public void AVirulentInfectionOfACleanCityPlacesTwoCubes()
        {
            // 변종(파랑)은 빈 도시에 두 개, 이미 있는 도시에는 하나. 변종이 아닌 노랑은 빈 도시에도 하나
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            InEffect("ChronicEffect");
            _w.SetRow(City("pandemic:Chicago"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(1));
            DrawOnly("pandemic:Atlanta", "pandemic:Chicago");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Atlanta", "pandemic:Blue"), Is.EqualTo(2));
            Assert.That(CubeCount("pandemic:Chicago", "pandemic:Blue"), Is.EqualTo(2));

            ClearCubes();
            DrawOnly("pandemic:Lima", "pandemic:Miami");
            Assert.That(Act("bob", "EndTurn", ("player", new EntityValue(_bob))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Lima", "pandemic:Yellow"), Is.EqualTo(1));
        }

        [Test]
        public void AVirulentOutbreakPlacesOneCubeOnCleanNeighbours()
        {
            // Chronic Effect는 폭발에 닿지 않는다(규칙서 카드 문구): 빈 이웃에도 하나, 큐브가 있던 이웃에도 하나
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            InEffect("ChronicEffect");
            InEffect("SlipperySlope");
            _w.SetRow(City("pandemic:Atlanta"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(3));
            _w.SetRow(City("pandemic:Chicago"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(1));
            DrawOnly("pandemic:Atlanta", "pandemic:Lima");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Atlanta", "pandemic:Blue"), Is.EqualTo(3));
            Assert.That(CubeCount("pandemic:Chicago", "pandemic:Blue"), Is.EqualTo(2));
            Assert.That(CubeCount("pandemic:Washington", "pandemic:Blue"), Is.EqualTo(1));
            Assert.That(Outbreaks(), Is.EqualTo(2), "one outbreak, counted twice by the virulent strain");
        }

        private bool IsVirulent(string disease) => ((BoolValue)_w.Get(Disease(disease), _w.Member("Virulent", "on"))).Value;

        /// <summary>감염 덱을 모두 뽑을 더미에 두고, <paramref name="bottom"/>을 맨 밑, <paramref name="top"/>을 맨 위에 놓는다.
        /// 플레이어 덱 맨 위는 <paramref name="effect"/>가 붙은 에픽.</summary>
        private EntityId EpidemicNext(string bottom, string top, string effect = "ChronicEffect")
        {
            foreach (var card in _w.All("InfectionCard"))
            {
                var c = FaceCity(card);
                _w.Set(card, _w.Member("Card", "pile"), new IntValue(0));
                _w.Set(card, _w.Member("Card", "slot"), new IntValue(c == top ? -100 : c == bottom ? 100 : 0));
            }
            var epidemic = _w.All("PlayerCard").First(c => ((BoolValue)_w.Get(c, _w.Member("Face", "epidemic"))).Value
                && ((IntValue)_w.Get(c, _w.Member("Card", "pile"))).Value == 0);
            _w.Set(epidemic, _w.Member("Card", "slot"), new IntValue(-100));
            _w.Set(epidemic, _w.Member("StrainCard", "effect"), new StrValue("brink:" + effect));
            return epidemic;
        }

        [Test]
        public void EachEpidemicCardCarriesADifferentHiddenEffect()
        {
            // 규칙서: 변종 에픽 카드를 뒤집어 섞고 필요한 수만 쓴다. 덮인 동안은 효과도 앞면처럼 아무도 못 본다
            var cards = EpidemicCards().ToList();
            Assert.That(cards, Has.Count.EqualTo(2));
            Assert.That(cards.Select(EffectOf).Distinct().Count(), Is.EqualTo(2));
            Assert.That(_w.All("PlayerCard").Except(cards).Any(c => _w.Has(c, "StrainCard")), Is.False, "city cards have no effect");
            foreach (var card in cards)
            {
                Assert.That(Done(card), Is.False);
                Assert.That(_w.ViewAs("alice").Sees(card, "StrainCard"), Is.False);
            }
        }

        [Test]
        public void TheEffectsAreOffUntilTheirCardIsDrawn()
        {
            // 변종이 정해져도 그 효과 카드를 아직 안 뽑았으면 빈 도시에 하나, 폭발은 한 칸
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            _w.SetRow(City("pandemic:Atlanta"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(3));
            DrawOnly("pandemic:Atlanta", "pandemic:Montreal");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Montreal", "pandemic:Blue"), Is.EqualTo(1));
            Assert.That(Outbreaks(), Is.EqualTo(1));
        }

        [Test]
        public void AContinuingEffectStartsWhenItsEpidemicIsDone()
        {
            // 규칙서: 지속 효과는 에픽을 마친 뒤부터. 에픽의 Infect 단계가 Atlanta(파랑 2)를 채워 터진 것은 한 칸,
            // 같은 차례의 감염 단계에서 맨 위로 온 Atlanta가 다시 터진 것은 Slippery Slope로 두 칸
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            _w.SetRow(City("pandemic:Atlanta"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(2));
            var card = EpidemicNext("pandemic:Atlanta", "pandemic:Lima", "SlipperySlope");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(Outbreaks(), Is.EqualTo(3));
            Assert.That(Done(card), Is.True);
            Assert.That(_w.ViewAs("bob").Sees(card, "StrainCard"), Is.True, "a drawn card is face up as a reminder");
        }

        [Test]
        public void RateEffectDrawsOneMoreCardAfterAStrainCity()
        {
            // 감염 속도 2: Atlanta(변종 파랑)를 뽑았으니 Lima 다음에 한 장 더(Miami). 그 한 장이 노랑이든 아니든 더는 안 뽑는다
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            InEffect("RateEffect");
            DrawOnly("pandemic:Atlanta", "pandemic:Lima", "pandemic:Miami", "pandemic:Chicago");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Atlanta", "pandemic:Blue"), Is.EqualTo(1));
            Assert.That(CubeCount("pandemic:Lima", "pandemic:Yellow"), Is.EqualTo(1));
            Assert.That(CubeCount("pandemic:Miami", "pandemic:Yellow"), Is.EqualTo(1));
            Assert.That(CubeCount("pandemic:Chicago", "pandemic:Blue"), Is.EqualTo(0), "only one extra card");
        }

        [Test]
        public void RateEffectDrawsNothingMoreWithoutAStrainCity()
        {
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            InEffect("RateEffect");
            DrawOnly("pandemic:Lima", "pandemic:Miami", "pandemic:Atlanta");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Atlanta", "pandemic:Blue"), Is.EqualTo(0));
        }

        [Test]
        public void RateEffectStopsOnceTheStrainIsEradicated()
        {
            // 규칙서: 변종이 박멸 전일 때만. 박멸된 파랑의 Atlanta를 뽑아도 한 장 더 뽑지 않는다
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            _w.Set(Disease("pandemic:Blue"), _w.Member("Status", "cured"), BoolValue.True);
            _w.Set(Disease("pandemic:Blue"), _w.Member("Status", "eradicated"), BoolValue.True);
            InEffect("RateEffect");
            DrawOnly("pandemic:Atlanta", "pandemic:Lima", "pandemic:Miami");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Lima", "pandemic:Yellow"), Is.EqualTo(1));
            Assert.That(CubeCount("pandemic:Miami", "pandemic:Yellow"), Is.EqualTo(0));
        }

        [Test]
        public void UncountedPopulationsAddsACubeWhereThereIsExactlyOne()
        {
            // 규칙서: 이 카드의 Intensify 전에 변종 큐브가 꼭 하나인 도시마다 하나 더. 둘인 도시와 다른 색은 그대로
            ClearCubes();
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            _w.SetRow(City("pandemic:Chicago"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(1));
            _w.SetRow(City("pandemic:Montreal"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(2));
            _w.SetRow(City("pandemic:Atlanta"), _w.Member("Cubes", "count"), "pandemic:Yellow", new IntValue(1));
            var card = EpidemicNext("pandemic:Bogota", "pandemic:SanFrancisco", "UncountedPopulations");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(CubeCount("pandemic:Chicago", "pandemic:Blue"), Is.EqualTo(2));
            Assert.That(CubeCount("pandemic:Montreal", "pandemic:Blue"), Is.EqualTo(2));
            Assert.That(CubeCount("pandemic:Atlanta", "pandemic:Yellow"), Is.EqualTo(1));
            Assert.That(CubeCount("pandemic:SanFrancisco", "pandemic:Blue"), Is.EqualTo(1), "the infection after the epidemic is not doubled");
            Assert.That(Done(card), Is.True);
        }

        [Test]
        public void TheStrainIsChosenRightAfterTheEpidemicsInfectStep()
        {
            // 에픽이 Lima를 노랑 3개로 채우면 그 자리에서 노랑이 변종이다. 같은 차례의 감염 단계(Lima는 Intensify로 맨 위에 와서 터지고,
            // 다음 카드 LosAngeles)에서 이미 Chronic Effect가 닿아 빈 LosAngeles에 둘 (규칙서: Infect 단계 직후에 정한다)
            ClearCubes();
            EpidemicNext("pandemic:Lima", "pandemic:LosAngeles");
            Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
            Assert.That(IsVirulent("pandemic:Yellow"), Is.True);
            Assert.That(IsVirulent("pandemic:Blue"), Is.False);
            Assert.That(CubeCount("pandemic:LosAngeles", "pandemic:Yellow"), Is.EqualTo(2));
        }

        [Test]
        public void ATieForTheStrainIsBrokenAtRandom()
        {
            // 파랑 3(Montreal)과 노랑 3(에픽의 Lima)이 같다: 시드마다 둘 중 하나가 무작위로 변종이 되고, 둘 다 나온다
            var picked = new HashSet<string>();
            for (int seed = 1; seed <= 12; seed++)
            {
                _w = new World(PandemicTests.Load(out _, "pandemic", "brink"), seed: seed);
                Assert.That(_w.Submit("host", "pandemic.Start", new Dictionary<string, Value>()).Succeeded, Is.True);
                _alice = _w.All("Player").Single(p => _w.DefOf(p) == "pandemic:Alice");
                _w.Seat(_alice, "alice");
                ClearCubes();
                _w.SetRow(City("pandemic:Montreal"), _w.Member("Cubes", "count"), "pandemic:Blue", new IntValue(3));
                EpidemicNext("pandemic:Lima", "pandemic:SanFrancisco");
                Assert.That(Act("alice", "EndTurn", ("player", new EntityValue(_alice))).Succeeded, Is.True);
                Assert.That(IsVirulent("pandemic:Blue") ^ IsVirulent("pandemic:Yellow"), Is.True, "exactly one strain");
                picked.Add(IsVirulent("pandemic:Blue") ? "Blue" : "Yellow");
            }
            Assert.That(picked, Is.EquivalentTo(new[] { "Blue", "Yellow" }));
        }

        private EntityId PlayerCard(string city) => _w.All("PlayerCard").Single(c => FaceCity(c) == city);

        private void Hold(EntityId player, params string[] cities)
        {
            foreach (var card in _w.All("PlayerCard"))
            {
                bool mine = System.Array.IndexOf(cities, FaceCity(card)) >= 0;
                var holder = _w.Get(card, _w.Member("Held", "holder"));
                if (!mine && !(holder is EntityValue e && e.Id.Equals(player))) continue;
                _w.Set(card, _w.Member("Card", "pile"), new IntValue(mine ? 2 : 1));
                _w.Set(card, _w.Member("Held", "holder"), mine ? new EntityValue(player) : (Value)NoneValue.Instance);
            }
        }

        private AnswerResult Cure(string disease) =>
            Act("alice", "DiscoverCure", ("player", new EntityValue(_alice)), ("disease", new StrValue("pandemic:" + disease)));

        [Test]
        public void AVirulentCureNeedsOneMoreCard()
        {
            // 코어의 cure_cost를 brink가 감싼다: 변종(파랑)은 여섯 장, 다른 질병은 그대로 다섯 장
            _w.Set(Disease("pandemic:Blue"), _w.Member("Virulent", "on"), BoolValue.True);
            InEffect("ComplexMolecularStructure");
            _w.Set(City("pandemic:Atlanta"), _w.Member("Research", "station"), BoolValue.True);
            string[] blue = { "pandemic:Atlanta", "pandemic:Chicago", "pandemic:Montreal", "pandemic:NewYork", "pandemic:Washington" };
            string[] yellow = { "pandemic:LosAngeles", "pandemic:MexicoCity", "pandemic:Miami", "pandemic:Bogota", "pandemic:Lima" };
            Hold(_alice, blue.Concat(yellow).ToArray());
            Assert.That(Cure("Blue").Succeeded, Is.False, "five blue cards are not enough for the virulent strain");
            Assert.That(Cure("Yellow").Succeeded, Is.True);
            Hold(_alice, blue.Concat(new[] { "pandemic:SanFrancisco" }).ToArray());
            Assert.That(Cure("Blue").Succeeded, Is.True);
            Assert.That(blue.Concat(new[] { "pandemic:SanFrancisco" }).Count(c => ((IntValue)_w.Get(PlayerCard(c), _w.Member("Card", "pile"))).Value == 1),
                Is.EqualTo(6), "six cards are discarded");
        }
    }
}
