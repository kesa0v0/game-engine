using System.Collections.Generic;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>구조 변경 뒤 스크립트 오류가 나면 흔적이 남지 않는다 (spec C8, spec-gaps G133·G134).</summary>
    public class WorldStructureRollbackTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=5))\n" +
            "component(\"Power\", base=field(int, default=1), value=derived(int, lambda p: p.base + len(contribs(p, \"value\"))))\n" +
            "component(\"Wounded\", turns=field(int, default=1), value=derived(int, lambda w: len(contribs(w, \"value\"))))\n" +
            "component(\"Escort\", leader=ref(\"Unit\", optional=True))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", level=field(int, default=0), " +
            "value=derived(int, lambda s: len(contribs(s, \"value\"))))\n" +
            "component(\"Opinion\", key=Ref[\"Unit\"], score=field(int, default=0))\n" +
            "component(\"Home\", city=ref(\"City\"))\n" +
            "component(\"Fragile\", n=field(int, default=lambda f: f.Home.city.Health.current // 0))\n" +
            "deftype(\"Glass\", components=[\"Home\", \"Fragile\"])\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"City\", components=[\"Health\"])\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Power\", \"Skill\"])\n" +
            "deftype(\"Settler\", components=[\"Health\", \"Home\"])\n";

        private const string Fail = "    unit.Health.current = \"oops\"\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nSkillDef(\"Mining\")\n" +
            "City(\"Town\")\nUnit(\"Scout\")\nSettler(\"Pioneer\")\nGlass(\"Vase\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def SpawnBroken(unit: Ref[Unit], at: Ref[City]):\n" +
            "    attach(unit, Escort())\n" +
            "    spawn(\"Vase\", Home=Home(city=at))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Spawn(unit: Ref[Unit], at: Ref[City]):\n" +
            "    spawn(\"Pioneer\", Home=Home(city=at))\n" + Fail +
            "@command(free=True, acts_for=None)\n" +
            "def Destroy(unit: Ref[Unit], victim: Ref[Unit]):\n" +
            "    destroy(victim)\n" + Fail +
            "@command(free=True, acts_for=None)\n" +
            "def Attach(unit: Ref[Unit], other: Ref[Unit]):\n" +
            "    attach(unit, Escort(leader=other))\n" + Fail +
            "@command(free=True, acts_for=None)\n" +
            "def Detach(unit: Ref[Unit]):\n" +
            "    detach(unit.Wounded)\n" +
            "    detach(unit.Escort)\n" + Fail +
            "@command(free=True, acts_for=None)\n" +
            "def AddRow(unit: Ref[Unit]):\n" +
            "    add_row(unit.Skill, \"Mining\", Skill(level=3))\n" + Fail +
            "@command(free=True, acts_for=None)\n" +
            "def RemoveRow(unit: Ref[Unit]):\n" +
            "    remove_row(unit.Skill, \"Mining\")\n" + Fail;

        private World _world = null!;
        private EntityId _town, _scout, _other;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _town = _world.Create("banking:Town");
            _scout = _world.Create("banking:Scout");
            _other = _world.Create("banking:Scout");
        }

        private void SubmitFailing(string command, Dictionary<string, Value>? extra = null)
        {
            var args = new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout) };
            if (extra != null)
                foreach (var pair in extra) args[pair.Key] = pair.Value;
            var r = _world.Submit("host", "banking." + command, args);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError), r.Error?.Message);
            Assert.That(r.Error!.Message, Does.Contain("Health.current"));
        }

        private long Int(EntityId id, string component, string member) =>
            ((IntValue)_world.Get(id, _world.Member(component, member))).Value;

        [Test]
        public void ASpawnIsUndoneAndUsesNoEntityId()
        {
            SubmitFailing("Spawn", new Dictionary<string, Value> { ["at"] = new EntityValue(_town) });
            Assert.That(_world.All("Settler"), Is.Empty);
            Assert.That(_world.Inverse(_town, _world.Member("Home", "city")), Is.Empty);
            // 실패한 커맨드는 id를 쓰지 않는다: 다음 엔티티는 바로 다음 번호다
            Assert.That(_world.Create("banking:Scout").Value, Is.EqualTo(_other.Value + 1));
        }

        [Test]
        public void ASpawnWhoseDefaultFailsIsUndoneTogetherWithTheEarlierChanges()
        {
            // spawn이 스스로 실패해 정리한 뒤 되감기가 한 번 더 지워도 같은 상태다
            var r = _world.Submit("host", "banking.SpawnBroken",
                new Dictionary<string, Value> { ["unit"] = new EntityValue(_scout), ["at"] = new EntityValue(_town) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Message, Does.Contain("Fragile.n"));
            Assert.That(_world.All("Glass"), Is.Empty);
            Assert.That(_world.Has(_scout, "Escort"), Is.False);
            Assert.That(_world.Inverse(_town, _world.Member("Home", "city")), Is.Empty);
            Assert.That(_world.Create("banking:Scout").Value, Is.EqualTo(_other.Value + 1));
        }

        [Test]
        public void ADestroyIsUndoneWithItsContributionsAndRows()
        {
            var power = _world.Member("Power", "value");
            _world.AddContribution(_other, power, "value", new IntValue(1));            // other를 겨냥
            _world.AddContribution(_scout, power, "value", new IntValue(1), _other);    // other가 출처
            _world.Add(_scout, "Opinion");
            _world.AddRow(_scout, "Opinion", _other, new Init(_world.Member("Opinion", "score"), new IntValue(7)));
            _world.Add(_scout, "Escort", new Init(_world.Member("Escort", "leader"), new EntityValue(_other)));

            SubmitFailing("Destroy", new Dictionary<string, Value> { ["victim"] = new EntityValue(_other) });

            Assert.That(_world.IsAlive(_other), Is.True);
            Assert.That(_world.All("Unit"), Is.EqualTo(new[] { _scout, _other }));
            Assert.That(Int(_other, "Power", "value"), Is.EqualTo(2));
            Assert.That(Int(_scout, "Power", "value"), Is.EqualTo(2));
            Assert.That(_world.HasRow(_scout, "Opinion", _other), Is.True);
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Opinion", "score"), _other)).Value, Is.EqualTo(7));
            Assert.That(_world.Inverse(_other, _world.Member("Escort", "leader")), Is.EqualTo(new[] { _scout }));
        }

        [Test]
        public void AnAttachIsUndone()
        {
            SubmitFailing("Attach", new Dictionary<string, Value> { ["other"] = new EntityValue(_other) });
            Assert.That(_world.Has(_scout, "Escort"), Is.False);
            Assert.That(_world.Inverse(_other, _world.Member("Escort", "leader")), Is.Empty);
        }

        [Test]
        public void ADetachIsUndoneWithItsValuesAndContributions()
        {
            _world.Add(_scout, "Wounded", new Init(_world.Member("Wounded", "turns"), new IntValue(4)));
            _world.AddContribution(_scout, _world.Member("Wounded", "value"), "value", new IntValue(1));
            _world.Add(_scout, "Escort", new Init(_world.Member("Escort", "leader"), new EntityValue(_other)));

            SubmitFailing("Detach");

            Assert.That(_world.Has(_scout, "Wounded"), Is.True);
            Assert.That(Int(_scout, "Wounded", "turns"), Is.EqualTo(4));
            Assert.That(Int(_scout, "Wounded", "value"), Is.EqualTo(1));
            Assert.That(_world.Has(_scout, "Escort"), Is.True);
            Assert.That(_world.Inverse(_other, _world.Member("Escort", "leader")), Is.EqualTo(new[] { _scout }));
        }

        [Test]
        public void AnAddedRowIsUndone()
        {
            SubmitFailing("AddRow");
            Assert.That(_world.HasRow(_scout, "Skill", "banking:Mining"), Is.False);
        }

        [Test]
        public void ARemovedRowIsUndoneWithItsValuesAndContributions()
        {
            _world.AddRow(_scout, "Skill", "banking:Mining", new Init(_world.Member("Skill", "level"), new IntValue(4)));
            _world.AddContribution(_scout, _world.Member("Skill", "value"), "banking:Mining", "value", new IntValue(1));

            SubmitFailing("RemoveRow");

            Assert.That(_world.HasRow(_scout, "Skill", "banking:Mining"), Is.True);
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Skill", "level"), "banking:Mining")).Value, Is.EqualTo(4));
            Assert.That(((IntValue)_world.GetRow(_scout, _world.Member("Skill", "value"), "banking:Mining")).Value, Is.EqualTo(1));
        }
    }
}
