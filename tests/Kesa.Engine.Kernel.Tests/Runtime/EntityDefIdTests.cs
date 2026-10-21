using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Stubs;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>엔티티의 <c>defid</c>: 자기 Def의 전체 이름 (spec-gaps G293).</summary>
    public class EntityDefIdTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Mark\", n=field(int, default=0))\n" +
            "deftype(\"Thing\", components=[\"Mark\"])\n";

        private const string Defs =
            "Thing(\"A\")\nThing(\"B\")\n" +
            "def kind(t):\n    return t.defid\n" +
            "def is_a(t):\n    return t.defid == defid(\"A\")\n";

        [Test]
        public void AnEntityKnowsItsDefId()
        {
            Mod("core", schema: Schema, defs: Defs);
            var world = new World(LoadClean());
            var b = world.Create("core:B");
            Assert.That(((StrValue)world.Call("core.kind", new EntityValue(b)).Value).Value, Is.EqualTo("core:B"));
            Assert.That(((BoolValue)world.Call("core.is_a", new EntityValue(b)).Value).Value, Is.False);
            Assert.That(((BoolValue)world.Call("core.is_a", new EntityValue(world.Create("core:A"))).Value).Value, Is.True);
        }

        [Test]
        public void ComparingItWithABareStringIsWarned()
        {
            Mod("core", schema: Schema, defs: Defs + "def bad(t: Ref[Thing]):\n    return t.defid == \"A\"\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain("LNK005"));
        }

        [Test]
        public void TheStubTypesItAsTheDefIdsOfThatType()
        {
            Mod("core", schema: Schema, defs: Defs);
            string stub = StubGenerator.Generate("core", LoadClean());
            Assert.That(stub, Does.Contain("def defid(self) -> "));
        }
    }
}
