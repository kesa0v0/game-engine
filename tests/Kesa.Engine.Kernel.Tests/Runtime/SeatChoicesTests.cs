using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>엔진 컴포넌트 <c>Seatable</c>와 <c>World.SeatChoices()</c> (spec S6, spec-gaps G239·G240).</summary>
    public class SeatChoicesTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Banner\", motto=field(str, default=\"\"))\n" +
            "deftype(\"Party\", components=[\"Banner\", \"Seatable\"])\n" +
            "deftype(\"Camp\", components=[\"Banner\"])\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\nCamp(\"Rebels\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Rise(camp: Ref[Camp]):\n" +
            "    attach(camp, Seatable())\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Fall(camp: Ref[Camp]):\n" +
            "    detach(camp.Seatable)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Wipe(party: Ref[Party]):\n" +
            "    destroy(party)\n";

        private static string Diag(Diagnostic d) => d.Code + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        private World NewWorld(out EntityId red, out EntityId rebels, out EntityId blue)
        {
            Mod("banking", schema: Schema, defs: Defs);
            var world = new World(LoadClean());
            red = world.Create("banking:Red");
            rebels = world.Create("banking:Rebels");
            blue = world.Create("banking:Blue");
            return world;
        }

        private static SubmitResult Run(World world, string command, string parameter, EntityId target) =>
            world.Submit("host", "banking." + command, new Dictionary<string, Value> { [parameter] = new EntityValue(target) });

        [Test]
        public void ChoicesAreTheLiveSeatableEntitiesInIdOrder()
        {
            var world = NewWorld(out var red, out _, out var blue);
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { red, blue }));
        }

        [Test]
        public void RulesAttachAndDetachIt()
        {
            var world = NewWorld(out var red, out var rebels, out var blue);
            Assert.That(Run(world, "Rise", "camp", rebels).Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { red, rebels, blue }));
            Assert.That(Run(world, "Fall", "camp", rebels).Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { red, blue }));
        }

        [Test]
        public void ADestroyedEntityIsNoLongerAChoice()
        {
            var world = NewWorld(out var red, out _, out var blue);
            Assert.That(Run(world, "Wipe", "party", red).Outcome, Is.EqualTo(SubmitOutcome.Succeeded));
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { blue }));
        }

        [Test]
        public void TheHostCanAddAndRemoveItToo()
        {
            var world = NewWorld(out var red, out var rebels, out var blue);
            world.Add(rebels, "Seatable");
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { red, rebels, blue }));
            world.Remove(rebels, "Seatable");
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { red, blue }));
        }

        [Test]
        public void OneFromTheDefStaysForGood()
        {
            // Def의 컴포넌트는 뗄 수 없다 (spec S4): 후보에서 빠질 수 있는 엔티티는 Def에 적지 않고 붙인다
            var world = NewWorld(out var red, out _, out _);
            var e = Assert.Throws<WorldException>(() => world.Remove(red, "Seatable"))!;
            Assert.That(e.Message, Does.Contain("cannot be removed"));
        }

        [Test]
        public void ChoicesDoNotLimitSeating()
        {
            var world = NewWorld(out _, out var rebels, out _);
            world.Seat(rebels, "ai-1");
            Assert.That(world.SeatOf(rebels), Is.EqualTo("ai-1"));
        }

        [Test]
        public void ChoicesSurviveASave()
        {
            var world = NewWorld(out _, out var rebels, out _);
            world.Add(rebels, "Seatable");
            var save = world.Save();
            var loaded = World.Load(LoadClean(), save).World!;
            Assert.That(loaded.SeatChoices(), Is.EqualTo(world.SeatChoices()));
            Assert.That(loaded.Save(), Is.EqualTo(save));
        }

        [Test]
        public void EveryModSeesItWithoutADependency()
        {
            Mod("banking", schema: Schema);
            Mod("scouts", schema: "deftype(\"Band\", components=[\"Seatable\"])\n", defs: "Band(\"Wolves\")\n");
            var world = new World(LoadClean());
            var wolves = world.Create("scouts:Wolves");
            Assert.That(world.SeatChoices(), Is.EqualTo(new[] { wolves }));
        }

        [Test]
        public void AModCannotDeclareSeatable()
        {
            Mod("banking", schema: "component(\"Seatable\", rank=field(int, default=0))\n");
            Load(out var diags);
            Assert.That(diags.Select(Diag), Has.Some.StartWith(Codes.SchDuplicateName +
                " 'Seatable' is a component declared by the engine | pick another name"));
        }
    }
}
