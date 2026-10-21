using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>스크립트가 기여 행을 넣고 빼는 내장 <c>add_contrib</c>·<c>remove_contrib</c>·<c>remove_contribs</c> (spec-gaps G299).</summary>
    public class ScriptContributionTests : LoaderTestBase
    {
        private const string Schema =
            "def health_max(h):\n" +
            "    return h.base_max + floor(sum([c.value for c in contribs(h, \"max\")]))\n" +
            "def first_id(h):\n" +
            "    for c in contribs(h, \"max\"):\n" +
            "        return c.id\n" +
            "    return 0\n" +
            "component(\"Health\", base_max=const(int, default=10), max=derived(int, health_max),\n" +
            "    first=derived(int, first_id), plain=field(int, default=0))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=1),\n" +
            "    eff=derived(int, lambda s: s.level + floor(sum([c.value for c in contribs(s, \"eff\")]))))\n" +
            "component(\"Mark\", n=field(int, default=0), count=field(int, default=0))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Skill\"])\n" +
            "deftype(\"Wall\", components=[\"Mark\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\n" +
            "Unit(\"Scout\", Skill={\"Cooking\": Skill(level=4)})\n" +
            "Wall(\"Stone\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Buff(u: Ref[Unit], w: Ref[Wall], n: int):\n" +
            "    row = add_contrib(u.Health, \"max\", \"add\", n, source=w)\n" +
            "    w.Mark.n = row\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Unbuff(w: Ref[Wall]):\n" +
            "    remove_contrib(w.Mark.n)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Clear(w: Ref[Wall]):\n" +
            "    n = remove_contribs(w)\n" +
            "    w.Mark.count = n\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Cook(u: Ref[Unit]):\n" +
            "    row = add_contrib(u.Skill.Cooking, \"eff\", \"add\", 2)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def BuffThenFail(u: Ref[Unit]):\n" +
            "    row = add_contrib(u.Health, \"max\", \"add\", 5)\n" +
            "    u.Health.plain = \"oops\"\n" +
            "@command(free=True, acts_for=None)\n" +
            "def BadMember(u: Ref[Unit]):\n" +
            "    row = add_contrib(u.Health, \"mxa\", \"add\", 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def AllRows(u: Ref[Unit]):\n" +
            "    row = add_contrib(u.Skill, \"eff\", \"add\", 1)\n" +
            "@command(free=True, acts_for=None)\n" +
            "def RemoveNone(u: Ref[Unit]):\n" +
            "    remove_contrib(None)\n";

        private World _world = null!;
        private EntityId _scout, _wall;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _scout = _world.Create("banking:Scout");
            _wall = _world.Create("banking:Stone");
        }

        private SubmitResult Submit(string command, params (string Name, Value Value)[] args) =>
            _world.Submit("host", "banking." + command, args.ToDictionary(a => a.Name, a => a.Value));

        private (string, Value) Unit => ("u", new EntityValue(_scout));
        private (string, Value) Wall => ("w", new EntityValue(_wall));

        private long Read(EntityId id, string component, string member) =>
            ((IntValue)_world.Get(id, _world.Member(component, member))).Value;

        private long Max => Read(_scout, "Health", "max");

        [Test]
        public void AddContribReturnsAnIdThatRemoveContribTakes()
        {
            Assert.That(Submit("Buff", Unit, Wall, ("n", new IntValue(3))).Succeeded, Is.True);
            Assert.That(Max, Is.EqualTo(13));
            long id = Read(_wall, "Mark", "n");
            Assert.That(id, Is.GreaterThan(0));
            Assert.That(Read(_scout, "Health", "first"), Is.EqualTo(id), "c.id is the id add_contrib returned");

            Assert.That(Submit("Unbuff", Wall).Succeeded, Is.True);
            Assert.That(Max, Is.EqualTo(10));

            var again = Submit("Unbuff", Wall);
            Assert.That(again.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(again.Error!.Code, Is.EqualTo(Codes.RunNoRow));
        }

        [Test]
        public void RemoveContribsTakesEverythingFromASourceAndCountsIt()
        {
            Submit("Buff", Unit, Wall, ("n", new IntValue(3)));
            Submit("Buff", Unit, Wall, ("n", new IntValue(4)));
            _world.AddContribution(_scout, _world.Member("Health", "max"), "add", new IntValue(1)); // 출처 없음: 남는다
            Assert.That(Max, Is.EqualTo(18));

            Assert.That(Submit("Clear", Wall).Succeeded, Is.True);
            Assert.That(Read(_wall, "Mark", "count"), Is.EqualTo(2));
            Assert.That(Max, Is.EqualTo(11));
        }

        [Test]
        public void AKeyedRowIsTargetedByPassingTheRow()
        {
            Assert.That(Read(_scout, "Skill", "eff", "banking:Cooking"), Is.EqualTo(4));
            Assert.That(Submit("Cook", Unit).Succeeded, Is.True);
            Assert.That(Read(_scout, "Skill", "eff", "banking:Cooking"), Is.EqualTo(6));
        }

        private long Read(EntityId id, string component, string member, string key) =>
            ((IntValue)_world.GetRow(id, _world.Member(component, member), key)).Value;

        [Test]
        public void AFailedCommandTakesItsContributionBack()
        {
            var r = Submit("BuffThenFail", Unit);
            Assert.That(r.Succeeded, Is.False);
            Assert.That(Max, Is.EqualTo(10));
            Assert.That(Read(_scout, "Health", "first"), Is.EqualTo(0));
        }

        [Test]
        public void WrongArgumentsSayWhatToWrite()
        {
            var member = Submit("BadMember", Unit);
            Assert.That(member.Error!.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(member.Error.Message, Does.Contain("no derived value 'mxa'"));
            Assert.That(member.Error.Hint, Does.Contain("max"));

            var rows = Submit("AllRows", Unit);
            Assert.That(rows.Error!.Code, Is.EqualTo(Codes.RunType));
            Assert.That(rows.Error.Hint, Does.Contain("pick one row"));

            var none = Submit("RemoveNone", Unit);
            Assert.That(none.Error!.Code, Is.EqualTo(Codes.RunType));
            Assert.That(none.Error.Hint, Does.Contain("G299"));
        }

        [Test]
        public void APureFunctionCannotAddContributions()
        {
            Mod("scouts", schema:
                "component(\"Look\", base=field(int, default=0), v=derived(int, lambda l: grow(l)))\n" +
                "def grow(l):\n    row = add_contrib(l, \"v\", \"add\", 1)\n    return row\n" +
                "deftype(\"Eye\", components=[\"Look\"])\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Does.StartWith("'grow' changes the world"));
        }
    }
}
