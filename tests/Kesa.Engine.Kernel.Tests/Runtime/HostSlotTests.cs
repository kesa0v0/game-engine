using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>호스트 함수를 Def 값이나 패치로 <c>Cond</c> 칸에 넣는다 (spec-gaps G221·G291).</summary>
    public class HostSlotTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", points=field(int, default=0))\n" +
            "deftype(\"Goal\", components=[\"Score\"], check=const(Cond[\"Goal\"]), effect=const(Action[\"Goal\"]))\n";

        private const string Fns =
            "def done(g):\n    return g.Score.points >= 10\n" +
            "def reward(g):\n    g.Score.points = 0\n" +
            "def met(g):\n    return g.Goal.check(g)\n";

        private static HostFunctions Host()
        {
            var host = new HostFunctions();
            var core = host.For("core");
            core.Pure("def goals.reached(g: Ref[Goal], extra: int = 0) -> bool", "",
                (HostReader r, EntityId g, long extra) =>
                    ((IntValue)r.Get(g, r.Member("Score", "points"))).Value + extra >= 5);
            core.Pure("def goals.two(a: Ref[Goal], b: Ref[Goal]) -> bool", "", (EntityId a, EntityId b) => true);
            core.Pure("def goals.count(g: Ref[Goal]) -> int", "", (EntityId g) => 3L);
            core.Writes("def goals.zero(g: Ref[Goal]) -> None", "", (HostAccess a, EntityId g) =>
                a.Set(g, a.Member("Score", "points"), new IntValue(0)));
            return host;
        }

        private ContentLoadResult LoadWithHost(out IReadOnlyList<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var result = ContentLoader.Load(_root, order, bag, Host());
            diagnostics = bag.ToSortedList();
            return result;
        }

        private static string Goals(string check, string effect = "reward") =>
            Fns + "Goal(\"A\", check=" + check + ", effect=" + effect + ")\n";

        [Test]
        public void APureHostFunctionFillsACondSlotAndRunsInTheGame()
        {
            Mod("core", schema: Schema, defs: Goals("core.goals.reached"));
            var result = LoadWithHost(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            var world = new World(result);
            var goal = world.Create("core:A");
            Assert.That(((BoolValue)world.Call("core.met", new EntityValue(goal)).Value).Value, Is.False);
            world.Set(goal, world.Member("Score", "points"), new IntValue(7));
            Assert.That(((BoolValue)world.Call("core.met", new EntityValue(goal)).Value).Value, Is.True);
        }
        [Test]
        public void AWritingHostFunctionCannotFillACondSlot()
        {
            Mod("core", schema: Schema, defs: Goals("core.goals.zero"));
            LoadWithHost(out var diags);
            var d = diags.First();
            Assert.That(d.Message, Does.StartWith("'check': 'core.goals.zero' is a host function that changes the world"));
        }

        [Test]
        public void AHostFunctionCannotFillAnActionSlot()
        {
            Mod("core", schema: Schema, defs: Goals("done", "core.goals.zero"));
            LoadWithHost(out var diags);
            Assert.That(diags.First().Message, Does.StartWith("'effect': 'core.goals.zero' is a host function, but Action"));
        }

        [Test]
        public void TheSignatureMustFitTheSlot()
        {
            Mod("core", schema: Schema, defs: Goals("core.goals.two"));
            LoadWithHost(out var diags);
            Assert.That(diags.First().Message, Does.Contain("cannot be called with one Ref[\"Goal\"]"));
        }

        [Test]
        public void TheHostFunctionMustGiveABool()
        {
            Mod("core", schema: Schema, defs: Goals("core.goals.count"));
            LoadWithHost(out var diags);
            Assert.That(diags.First().Message, Does.Contain("returns int").And.Contain("needs a bool"));
        }

        [Test]
        public void APatchCanPutAHostFunctionInACondSlot()
        {
            Mod("core", schema: Schema, defs: Goals("done"));
            Mod("addon", defs: "patch.set(\"core:A\", \"check\", core.goals.reached)\n", extra: ", dependencies=[\"core\"]");
            var result = LoadWithHost(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            var world = new World(result);
            var goal = world.Create("core:A");
            world.Set(goal, world.Member("Score", "points"), new IntValue(6));
            Assert.That(((BoolValue)world.Call("core.met", new EntityValue(goal)).Value).Value, Is.True);
        }

        [Test]
        public void ANameThatIsNotAHostFunctionIsAnError()
        {
            Mod("core", schema: Schema, defs: Goals("core.goals.reaced"));
            LoadWithHost(out var diags);
            Assert.That(diags.First().Message, Does.Contain("core.goals has no host function 'reaced'"));
        }

        [Test]
        public void ContentCannotCallAHostFunctionWhileLoading()
        {
            Mod("core", schema: Schema, defs: Fns + "x = core.goals.count(None)\n");
            LoadWithHost(out var diags);
            Assert.That(diags.First().Message, Does.Contain("host function value is not callable"));
        }
    }
}
