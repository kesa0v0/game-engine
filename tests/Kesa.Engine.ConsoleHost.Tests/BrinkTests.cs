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
    }
}
