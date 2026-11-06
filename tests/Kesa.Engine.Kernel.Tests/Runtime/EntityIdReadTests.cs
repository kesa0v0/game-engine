using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>스크립트가 엔티티 번호를 읽는다 (G474): 읽기 전용 정수이고, 같은 이름의 컴포넌트나 역방향 컬렉션이 있으면 그것이 먼저다.</summary>
    public class EntityIdReadTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Mark\", n=field(int, default=0), first=field(bool, default=False))\n" +
            "deftype(\"Unit\", components=[\"Mark\"])\n";

        private const string Defs =
            "Unit(\"Soldier\")\n" +
            "@command(free=True, acts_for=None)\ndef Note(u: Ref[Unit]):\n    u.Mark.n = u.id\n" +
            "@command(free=True, acts_for=None)\ndef Order(a: Ref[Unit], b: Ref[Unit]):\n    a.Mark.first = a.id < b.id\n";

        private static Dictionary<string, Value> Args(params (string Name, EntityId Id)[] args) =>
            args.ToDictionary(a => a.Name, a => (Value)new EntityValue(a.Id));

        [Test]
        public void AScriptReadsTheIdAndOrdersByIt()
        {
            Mod("banking", schema: Schema, defs: Defs);
            var world = new World(LoadClean());
            var a = world.Create("banking:Soldier");
            var b = world.Create("banking:Soldier");

            Assert.That(world.Submit("host", "banking.Note", Args(("u", b))).Succeeded, Is.True);
            Assert.That(((IntValue)world.Get(b, world.Member("Mark", "n"))).Value, Is.EqualTo(b.Value));

            Assert.That(world.Submit("host", "banking.Order", Args(("a", a), ("b", b))).Succeeded, Is.True);
            Assert.That(world.Get(a, world.Member("Mark", "first")), Is.EqualTo(BoolValue.True));
        }

        [Test]
        public void AssigningTheIdIsNotAllowed()
        {
            Mod("banking", schema: Schema,
                defs: "Unit(\"Soldier\")\n@command(free=True, acts_for=None)\ndef Bad(u: Ref[Unit]):\n    u.id = 3\n");
            var world = new World(LoadClean());
            var unit = world.Create("banking:Soldier");
            var result = world.Submit("host", "banking.Bad", Args(("u", unit)));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error, Is.Not.Null);
        }

        [Test]
        public void AComponentNamedIdStillWins()
        {
            Mod("banking",
                schema: "component(\"id\", v=field(int, default=7))\ndeftype(\"Unit\", components=[\"id\", \"Mark\"])\n" +
                        "component(\"Mark\", n=field(int, default=0))\n",
                defs: "Unit(\"Soldier\")\n@command(free=True, acts_for=None)\ndef Note(u: Ref[Unit]):\n    u.Mark.n = u.id.v\n");
            var world = new World(LoadClean());
            var unit = world.Create("banking:Soldier");
            Assert.That(world.Submit("host", "banking.Note", Args(("u", unit))).Succeeded, Is.True);
            Assert.That(((IntValue)world.Get(unit, world.Member("Mark", "n"))).Value, Is.EqualTo(7));
        }

        [Test]
        public void AComponentDoesNotAnswerForItsEntity()
        {
            Mod("banking", schema: Schema,
                defs: "Unit(\"Soldier\")\n@command(free=True, acts_for=None)\ndef Note(u: Ref[Unit]):\n    u.Mark.n = u.Mark.id\n");
            Load(out var diags);
            Assert.That(diags.Where(d => d.Severity == Severity.Error), Is.Not.Empty, "use u.Mark.entity.id");
        }
    }
}
