using System.Collections.Generic;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary><c>==</c>는 런타임 값을 가리키는 대상이나 내용으로 비교한다 (spec-gaps G142).</summary>
    public class WorldEqualityTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=5))\n" +
            "component(\"Wounded\", turns=field(int, default=0), depth=field(int, default=0))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"all\", level=field(int, default=0))\n" +
            "component(\"Out\", bits=field(str, default=\"\"))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Unit\", cost=const(int, default=1), components=[\"Health\", \"Skill\", \"Out\"])\n";

        [TestCase("u.Health == u.Health", true)]
        [TestCase("u.Health == v.Health", false)]
        [TestCase("u.Skill == u.Skill", true)]
        [TestCase("u.Skill == v.Skill", false)]
        [TestCase("u.Skill.Cooking == u.Skill[\"Cooking\"]", true)]
        [TestCase("u.Skill.Cooking == u.Skill.Mining", false)]
        [TestCase("u.Skill.Cooking == u.Health", false)]
        [TestCase("u.Unit == u.Unit", true)]
        [TestCase("u.Unit == v.Unit", false)]
        [TestCase("range(3) == range(3)", true)]
        [TestCase("range(0) == range(5, 5)", true)]
        [TestCase("range(1, 2) == range(1, 8, 7)", true)]
        [TestCase("range(3) == range(0, 6, 2)", false)]
        [TestCase("range(3) == [0, 1, 2]", false)]
        [TestCase("Wounded(turns=3, depth=1) == Wounded(depth=1, turns=3)", true)]
        [TestCase("Wounded(turns=3) == Wounded(turns=4)", false)]
        [TestCase("Wounded(turns=3) == Wounded(turns=3, depth=0)", false)]
        [TestCase("u.Health in [v.Health, u.Health]", true)]
        public void RuntimeValuesCompareByWhatTheyPointAtOrHold(string expression, bool equal)
        {
            Mod("banking", schema: Schema,
                defs: "SkillDef(\"Cooking\")\nSkillDef(\"Mining\")\nUnit(\"Scout\")\n" +
                      "@command(free=True, acts_for=None)\n" +
                      "def Probe(u: Ref[Unit], v: Ref[Unit]):\n" +
                      "    u.Out.bits = \"yes\" if " + expression + " else \"no\"\n");
            var world = new World(LoadClean());
            var u = world.Create("Scout");
            var v = world.Create("Scout");
            var result = world.Submit("host", "banking.Probe",
                new Dictionary<string, Value> { ["u"] = new EntityValue(u), ["v"] = new EntityValue(v) });
            Assert.That(result.Succeeded, Is.True, result.Error?.ToString());
            Assert.That(((StrValue)world.Get(u, world.Member("Out", "bits"))).Value, Is.EqualTo(equal ? "yes" : "no"));
        }
    }
}
