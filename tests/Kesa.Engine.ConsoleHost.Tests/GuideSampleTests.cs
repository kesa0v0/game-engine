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
    /// <summary>
    /// 모더 안내서(<c>docs/modding</c>)의 예시 모드 <c>samples/guide</c> (spec-gaps G252). 안내서가 보여 주는 코드가 실제로 불리고
    /// 안내서가 말한 대로 도는지 본다. 이 테스트가 깨지면 안내서도 고친다.
    /// </summary>
    public class GuideSampleTests
    {
        private static string Root()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "samples", "guide");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("samples/guide");
        }

        private World _w = null!;
        private EntityId _gull, _crane;

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
            _w = new World(Load(out _), seed: 7);
            Assert.That(_w.Submit("host", "harbor.Open", new Dictionary<string, Value>()).Succeeded, Is.True);
            _gull = _w.All("House").Single(h => _w.DefOf(h) == "Gull");
            _crane = _w.All("House").Single(h => _w.DefOf(h) == "Crane");
            _w.Seat(_gull, "gull");
            _w.Seat(_crane, "crane");
        }

        private MemberHandle M(string owner, string member) => _w.Member(owner, member);

        private long Int(EntityId id, string owner, string member) => ((IntValue)_w.Get(id, M(owner, member))).Value;

        private SubmitResult Submit(string seat, string command, params (string Name, Value Value)[] args) =>
            _w.Submit(seat, "harbor." + command, args.ToDictionary(a => a.Name, a => a.Value));

        private EntityId Buy(string seat, EntityId house, string kind)
        {
            var r = Submit(seat, "Buy", ("house", new EntityValue(house)), ("kind", new StrValue(kind)));
            Assert.That(r.Succeeded, Is.True, r.Outcome.ToString());
            return _w.Inverse(house, M("Owner", "house")).Last();
        }

        [Test]
        public void TheModsLoadWithOnlyTheGuardNote()
        {
            Load(out var diagnostics);
            Assert.That(diagnostics.Select(d => d.Code), Is.EqualTo(new[] { "PAT002" }));
        }

        [Test]
        public void DefsInheritAndThePatchesApply()
        {
            var galleon = Buy("gull", _gull, "Galleon");
            var sloop = Buy("crane", _crane, "Sloop");
            Assert.That(Int(galleon, "Ship", "base_hp"), Is.EqualTo(7)); // inherit.base_hp + 4
            Assert.That(Int(galleon, "Hull", "hp"), Is.EqualTo(7));
            Assert.That(Int(galleon, "Ship", "speed"), Is.EqualTo(2)); // 1 + lighthouse의 patch.add
            var tags = ((ListValue)_w.Get(sloop, M("Ship", "tags"))).Items.Select(t => ((StrValue)t).Value);
            Assert.That(tags, Is.EqualTo(new[] { "wood", "fast", "lit" }));
            Assert.That(Int(_gull, "Purse", "gold"), Is.EqualTo(5)); // 20 - 15
            Assert.That(Int(_gull, "Fleet", "strength"), Is.EqualTo(7));
        }

        [Test]
        public void ACostThatCannotBePaidIsRefused()
        {
            Buy("gull", _gull, "Galleon");
            var r = Submit("gull", "Buy", ("house", new EntityValue(_gull)), ("kind", new StrValue("Galleon")));
            Assert.That(r.Succeeded, Is.False);
            Assert.That(Int(_gull, "Purse", "gold"), Is.EqualTo(5));
        }

        [Test]
        public void OnlyTheSeatedConnectionActsForAHouse()
        {
            var r = Submit("crane", "Buy", ("house", new EntityValue(_gull)), ("kind", new StrValue("Sloop")));
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.GateDenied));
        }

        [Test]
        public void ASunkShipPaysTheInsuranceAndIsGone()
        {
            var sloop = Buy("crane", _crane, "Sloop");
            Buy("gull", _gull, "Galleon");
            for (int i = 0; i < 10 && _w.IsAlive(sloop); i++)
                Assert.That(Submit("gull", "Raid", ("house", new EntityValue(_gull)), ("target", new EntityValue(sloop))).Succeeded);
            Assert.That(_w.IsAlive(sloop), Is.False);
            Assert.That(Int(_crane, "Purse", "gold"), Is.EqualTo(20 - 8 + 3));
        }

        [Test]
        public void AContributionRaisesTheDerivedMaximum()
        {
            var sloop = Buy("crane", _crane, "Sloop");
            _w.AddContribution(sloop, M("Hull", "max_hp"), "add", new IntValue(2));
            Assert.That(Int(sloop, "Hull", "max_hp"), Is.EqualTo(5));
        }

        [Test]
        public void ACharterIsAskedAndTheFishArriveTwoTicksLater()
        {
            Assert.That(Submit("crane", "Charter", ("house", new EntityValue(_crane))).Succeeded);
            var q = _w.OpenRequests.Single();
            Assert.That(q.Who, Is.EqualTo(new[] { _crane }));
            _w.Answer("crane", q.Id, _crane, 0); // accept
            Assert.That(Int(_crane, "Purse", "gold"), Is.EqualTo(25));
            _w.Advance(2);
            Assert.That(((IntValue)_w.GetRow(_crane, M("Stock", "amount"), "Fish")).Value, Is.EqualTo(3));
            Assert.That(Int(_w.All("Harbor").Single(), "Port", "day"), Is.EqualTo(2));
        }

        [Test]
        public void AnUnansweredCharterIsDeclinedAtTheDeadline()
        {
            Submit("crane", "Charter", ("house", new EntityValue(_crane)));
            _w.Advance(3);
            Assert.That(_w.OpenRequests, Is.Empty);
            Assert.That(Int(_crane, "Purse", "gold"), Is.EqualTo(20));
        }

        [Test]
        public void ARichHouseMakesAPlanThatOnlyItSees()
        {
            _w.Set(_gull, M("Purse", "gold"), new IntValue(60));
            _w.Advance(1);
            Assert.That(Int(_gull, "Secret", "plan"), Is.EqualTo(1));
            var seen = ((ListValue)_w.ViewAs("crane").Call("harbor.plans", new EntityValue(_crane)).Value).Items
                .Cast<DictValue>().Select(d => d.Items.Single(p => ((StrValue)p.Key).Value == "plan").Value).ToList();
            Assert.That(seen.Select(v => v is StrValue s ? s.Value : ((IntValue)v).Value.ToString()), Is.EqualTo(new[] { "?", "0" }));
        }

        [Test]
        public void TheConsoleShowsThePanels()
        {
            var screen = new StringWriter();
            var errors = new StringWriter();
            int code = Play.Run(new[] { Root(), "--seat", "gull=Gull", "--seat", "crane=Crane" },
                new StringReader("1\n:q\n"), screen, errors);
            Assert.That(code, Is.EqualTo(0), errors.ToString());
            Assert.That(screen.ToString(), Does.Contain("[gull] #2 Gull").And.Contain("{gold: 20, fleet: 0, day: 0}"));
        }
    }
}
