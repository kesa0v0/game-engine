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
            return ((StrValue)_w.Get(city.Id, _w.Member("Site", "city"))).Value;
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
                string name = ((StrValue)_w.Get(city, _w.Member("Site", "city"))).Value;
                links[name] = ((ListValue)_w.Get(city, _w.Member("City", "neighbors"))).Items.Select(i => ((StrValue)i).Value).ToList();
            }
            foreach (var pair in links)
                foreach (var other in pair.Value)
                    Assert.That(links[other], Does.Contain(pair.Key), pair.Key + " -> " + other);
        }
    }
}
