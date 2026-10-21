using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary><c>defdata(id)</c>: 엔티티가 없는 Def의 <c>const</c>를 Def id로 읽는다 (spec-gaps G303·G304).</summary>
    public class DefDataTests : LoaderTestBase
    {
        private const string Schema =
            "def power(m):\n    return defdata(m.kind).Ship.speed * 10\n" +
            "component(\"Mark\", n=field(int, default=0), kind=field(DefId[\"Ship\"], default=\"Sloop\"), name=field(str, default=\"\")," +
            " power=derived(int, power))\n" +
            "deftype(\"Ship\", components=[\"Mark\"], speed=const(int, default=1))\n" +
            "deftype(\"Cargo\", weight=const(int, default=0))\n";

        private const string Ships = "Ship(\"Sloop\", speed=3)\nShip(\"Brig\", speed=2)\nCargo(\"Salt\", weight=5)\n";

        private static string Command(string body) =>
            "@command(free=True, acts_for=None)\ndef Check(s: Ref[Ship]):\n" + body;

        private World _world = null!;
        private EntityId _sloop;

        private void Start(string defs, string mod = "banking")
        {
            Mod(mod, schema: Schema, defs: Ships + defs);
            _world = new World(LoadClean());
            _sloop = _world.Create(mod + ":Sloop");
        }

        private SubmitResult Check() =>
            _world.Submit("host", "banking.Check", new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop) });

        private long N() => ((IntValue)_world.Get(_sloop, _world.Member("Mark", "n"))).Value;

        [Test]
        public void ADefsConstsAreReadThroughItsTypeName()
        {
            // 짧은 이름은 쓴 모드 기준(G261), 다른 타입이면 None (G107과 같음)
            Start(Command("    s.Mark.n = defdata(\"Brig\").Ship.speed * 100 + defdata(\"banking:Salt\").Cargo.weight +" +
                          " (1000 if defdata(\"Salt\").Ship == None else 0)\n"));
            Assert.That(Check().Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(1205));
        }

        [Test]
        public void ADefIdValueWorksAndADerivedExplainsTheRead()
        {
            Start("");
            var power = _world.Member("Mark", "power");
            Assert.That(((IntValue)_world.Get(_sloop, power)).Value, Is.EqualTo(30));
            Assert.That(_world.Explain(_sloop, power).Render(), Does.EndWith("\n  Ship.speed[banking:Sloop] = 3"));
            _world.Set(_sloop, _world.Member("Mark", "kind"), new StrValue("banking:Brig"));
            Assert.That(((IntValue)_world.Get(_sloop, power)).Value, Is.EqualTo(20));
        }

        [Test]
        public void AMissingDefAtRunTimeIsAnErrorWithASuggestion()
        {
            Start(Command("    s.Mark.n = defdata(s.Mark.name).Ship.speed\n"));
            _world.Set(_sloop, _world.Member("Mark", "name"), new StrValue("Brigg"));
            var r = Check();
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("there is no Def 'banking:Brigg'"));
            Assert.That(r.Error!.Hint, Is.EqualTo("did you mean 'banking:Brig'?"));
        }

        [TestCase("    s.Mark.n = defdata(s.Mark.kind).Shp.speed\n", "no Def type 'Shp'")]
        [TestCase("    s.Mark.n = defdata(s.Mark.kind).Ship.sped\n", "sped")]
        public void ATypoAfterDefdataIsALoadError(string body, string message)
        {
            Mod("banking", schema: Schema, defs: Ships + Command(body));
            Load(out var diags);
            Assert.That(diags.Single().Message, Does.Contain(message));
        }

        [Test]
        public void ALiteralIdIsCheckedAtLoad()
        {
            Mod("banking", schema: Schema, defs: Ships + Command("    s.Mark.n = defdata(\"Slop\").Ship.speed\n"));
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code + " " + d.Message + " | " + d.Hint), Is.EqualTo(new[]
                { "LNK001 there is no Def 'banking:Slop' | did you mean 'banking:Sloop'?" }));
        }

        [Test]
        public void AnUnknownTypeAtRunTimeIsAnError()
        {
            // 리스트를 거치면 로드 검사가 타입을 잃는다
            Start(Command("    s.Mark.n = [defdata(\"Sloop\")][0].Shp.speed\n"));
            var r = Check();
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("has no Def type 'Shp'"));
            Assert.That(r.Error!.Hint, Is.EqualTo("did you mean 'Ship'?"));
        }

        // ---- defs (G305) ----

        [Test]
        public void DefsListsEveryConcreteDefOfATypeInLoadOrder()
        {
            // 부른 모드(banking)는 addon에 의존하지 않지만 addon의 배도 나온다. 추상 Hull은 빠진다
            Mod("banking", schema: Schema, defs: Ships + "Ship(\"Hull\", abstract=True)\n" + Command(
                "    n = 0\n    for k in defs(\"Ship\"):\n        n = n * 10 + defdata(k).Ship.speed\n" +
                "    s.Mark.n = n * 10 + len(defs(\"Cargo\"))\n"));
            Mod("addon", defs: "Ship(\"Galleon\", speed=4)\n", extra: ", dependencies=[\"banking\"]");
            _world = new World(LoadClean());
            _sloop = _world.Create("banking:Sloop");
            Assert.That(Check().Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(3241));
        }

        [TestCase("Shp", "there is no Def type 'Shp'", "did you mean 'Ship'?")]
        [TestCase("Mark", "'Mark' is a component, not a Def type", null)]
        public void ALiteralTypeNameIsCheckedAtLoad(string name, string message, string? hint)
        {
            Mod("banking", schema: Schema, defs: Ships + Command("    s.Mark.n = len(defs(\"" + name + "\"))\n"));
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.LnkNotFound));
            Assert.That(d.Message, Is.EqualTo(message));
            if (hint != null) Assert.That(d.Hint, Is.EqualTo(hint));
        }

        [Test]
        public void AnUnknownTypeNameAtRunTimeIsAnError()
        {
            Start(Command("    s.Mark.n = len(defs(s.Mark.name))\n"));
            _world.Set(_sloop, _world.Member("Mark", "name"), new StrValue("Shipp"));
            var r = Check();
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("there is no Def type 'Shipp'"));
            Assert.That(r.Error!.Hint, Is.EqualTo("did you mean 'Ship'?"));
        }

        [Test]
        public void ItsIdsAreFullNamesSoABareComparisonWarns()
        {
            Mod("banking", schema: Schema, defs: Ships + Command(
                "    for k in defs(\"Ship\"):\n        if k == \"Sloop\":\n            s.Mark.n = 1\n"));
            Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(new[] { Codes.LnkBareDefId }));
        }

        [Test]
        public void APausedFlowHoldingDefdataSavesAndLoads()
        {
            Mod("banking", schema: Schema, defs: Ships +
                "@command(free=True, acts_for=None)\ndef Hold(s: Ref[Ship]):\n" +
                "    d = defdata(\"Brig\")\n    t = d.Ship\n    wait(ticks=2)\n    s.Mark.n = d.Ship.speed * 10 + t.speed\n");
            var content = LoadClean();
            _world = new World(content);
            _sloop = _world.Create("banking:Sloop");
            Assert.That(_world.Submit("host", "banking.Hold",
                new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop) }).Succeeded, Is.True);
            var loaded = World.Load(content, _world.Save());
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            _world.Advance(2);
            Assert.That(N(), Is.EqualTo(22));
        }
    }
}
