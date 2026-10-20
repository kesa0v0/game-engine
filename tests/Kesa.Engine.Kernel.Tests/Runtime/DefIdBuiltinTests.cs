using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary><c>defid("Sloop")</c>: 쓴 코드의 모드 기준 전체 이름 (spec-gaps G261·G266).</summary>
    public class DefIdBuiltinTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Mark\", n=field(int, default=0), kind=field(DefId[\"Ship\"], default=\"Sloop\"), name=field(str, default=\"\"))\n" +
            "deftype(\"Ship\", components=[\"Mark\"])\n" +
            "deftype(\"Cargo\")\n";

        private const string Ships = "Ship(\"Sloop\")\nShip(\"Brig\")\nShip(\"Hull\", abstract=True)\nCargo(\"Salt\")\n";

        private const string Dep = ", dependencies=[\"banking\"]";

        private static string Diag(Diagnostic d) => d.Code + " " + d.Mod + " " + d.Message + (d.Hint == null ? "" : " | " + d.Hint);

        private static string Command(string body) =>
            "@command(free=True, acts_for=None)\ndef Check(s: Ref[Ship]):\n" + body;

        private SubmitResult Check(World world, EntityId ship, string mod = "banking") =>
            world.Submit("host", mod + ".Check", new Dictionary<string, Value> { ["s"] = new EntityValue(ship) });

        private long N(World world, EntityId ship) => ((IntValue)world.Get(ship, world.Member("Mark", "n"))).Value;

        [Test]
        public void AShortNameIsTheFullIdOfTheWritingMod()
        {
            // 값은 언제나 전체 이름이라 맨 문자열 "Sloop"과는 같지 않다
            Mod("banking", schema: Schema, defs: Ships + Command(
                "    s.Mark.n = (1 if s.Mark.kind == defid(\"Sloop\") else 0) + (2 if s.Mark.kind == \"Sloop\" else 0) +" +
                " (4 if defid(\"banking:Sloop\") == \"banking:Sloop\" else 0)\n"));
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.LnkBareDefId })); // == "Sloop"
            var world = new World(result);
            var sloop = world.Create("banking:Sloop");
            Assert.That(Check(world, sloop).Succeeded, Is.True);
            Assert.That(N(world, sloop), Is.EqualTo(5));
        }

        [Test]
        public void ItWorksInADefFileAsADictKey()
        {
            Mod("banking", schema: Schema,
                defs: Ships + "PRICES = {defid(\"Sloop\"): 8, defid(\"Brig\"): 12}\n" + Command("    s.Mark.n = PRICES[s.Mark.kind]\n"));
            var world = new World(LoadClean());
            var sloop = world.Create("banking:Sloop");
            Assert.That(Check(world, sloop).Succeeded, Is.True);
            Assert.That(N(world, sloop), Is.EqualTo(8));
        }

        [Test]
        public void AnotherModsDefNeedsItsFullId()
        {
            Mod("banking", schema: Schema, defs: Ships);
            Mod("addon", defs: "Ship(\"Sloop\")\n" + Command(
                "    s.Mark.n = (1 if s.Mark.kind == defid(\"banking:Sloop\") else 0) + (2 if s.Mark.kind == defid(\"Sloop\") else 0)\n"),
                extra: Dep);
            var world = new World(LoadClean());
            var sloop = world.Create("banking:Sloop");
            Assert.That(Check(world, sloop, "addon").Succeeded, Is.True);
            Assert.That(N(world, sloop), Is.EqualTo(1));
        }

        [TestCase("Command", "    s.Mark.n = 1 if s.Mark.kind == defid(\"Slop\") else 0\n")]
        [TestCase("DefFile", "X = defid(\"Slop\")\n")]
        public void ALiteralTypoIsALoadErrorWithASuggestion(string where, string code)
        {
            Mod("banking", schema: Schema, defs: Ships + (where == "Command" ? Command(code) : code));
            var result = Load(out var diags);
            Assert.That(diags.Select(Diag), Is.EqualTo(new[] { "LNK001 banking there is no Def 'banking:Slop' | did you mean 'banking:Sloop'?" }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
        }

        [Test]
        public void ALiteralMustBeAConcreteVisibleDefOfAnyType()
        {
            Mod("banking", schema: Schema, defs: Ships + "A = defid(\"Salt\")\nB = defid(\"Hull\")\n");
            Mod("lone", defs: "C = defid(\"banking:Sloop\")\n");
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code + " " + d.Mod), Is.EquivalentTo(new[] { Codes.LnkAbstract + " banking", Codes.LnkNotVisible + " lone" }));
        }

        [Test]
        public void ABadlyWrittenIdIsRejectedAtLoad()
        {
            Mod("banking", schema: Schema, defs: Ships + "X = defid(\"a b\")\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.DefBadId));
        }

        // DefId 매개변수의 choose(from_=...) 목록은 타입을 아는 자리라 짧은 이름이 그대로 맞는다 (G261)
        [Test]
        public void ChoicesOfADefIdParameterAreResolvedLikeOtherTypedPlaces()
        {
            Mod("banking", schema: Schema, defs: Ships +
                "@command(free=True, acts_for=None)\ndef Pick(s: Ref[Ship], k: DefId[Ship]):\n" +
                "    with preconditions:\n        choose(k, from_=[\"Sloop\"])\n    s.Mark.kind = k\n");
            var world = new World(LoadClean());
            var sloop = world.Create("banking:Sloop");
            SubmitResult Pick(string k) => world.Submit("host", "banking.Pick",
                new Dictionary<string, Value> { ["s"] = new EntityValue(sloop), ["k"] = new StrValue(k) });
            Assert.That(Pick("banking:Sloop").Succeeded, Is.True);
            Assert.That(Pick("banking:Brig").Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            var candidates = world.Candidates("banking.Pick", new Dictionary<string, Value> { ["s"] = new EntityValue(sloop) }, "k");
            Assert.That(candidates.Values.Select(v => ((StrValue)v).Value), Is.EqualTo(new[] { "banking:Sloop" }));
        }

        [Test]
        public void AMissingDefAtRunTimeIsAnErrorWithASuggestion()
        {
            Mod("banking", schema: Schema, defs: Ships + Command("    s.Mark.kind = defid(s.Mark.name)\n"));
            var world = new World(LoadClean());
            var sloop = world.Create("banking:Sloop");
            world.Set(sloop, world.Member("Mark", "name"), new StrValue("Brigg"));
            var r = Check(world, sloop);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("there is no Def 'banking:Brigg'"));
            Assert.That(r.Error!.Hint, Is.EqualTo("did you mean 'banking:Brig'?"));

            world.Set(sloop, world.Member("Mark", "name"), new StrValue("Brig"));
            Assert.That(Check(world, sloop).Succeeded, Is.True);
            Assert.That(((StrValue)world.Get(sloop, world.Member("Mark", "kind"))).Value, Is.EqualTo("banking:Brig"));
        }
    }
}
