using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>런타임 월드 (spec S4, spec-gaps G86~G93).</summary>
    public class WorldTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10, min=1), max=derived(int, lambda h: h.base_max),\n" +
            "    current=field(int, default_from=\"base_max\", min=0))\n" +
            "component(\"Wealth\", amount=field(fixed, default=0))\n" +
            "component(\"Location\", city=ref(\"City\", inverse=\"units\"))\n" +
            "component(\"Follow\", leader=ref(\"City\", optional=True))\n" +
            "component(\"Banner\", color=field(int, default=0), pos=field(int, default=0))\n" +
            "component(\"Poisoned\", turns=field(int, default=3), strength=const(int, default=1))\n" +
            "component(\"Cursed\", power=const(int))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=0, min=0, max=20), cap=const(int, default=20))\n" +
            "component(\"Need\", key=\"NeedDef\", keys=\"listed\", weight=const(int, default=1), met=field(bool, default=False))\n" +
            "component(\"Opinion\", key=Ref[\"City\"], score=field(int, default=0))\n" +
            "component(\"Tax\", rate=field(fixed))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"NeedDef\")\n" +
            "deftype(\"City\", components=[\"Banner\"])\n" +
            "deftype(\"Unit\", label=const(str, default=\"?\"),\n" +
            "    components=[\"Health\", \"Wealth\", \"Location\", \"Follow\", \"Skill\", \"Need\", \"Opinion\"])\n" +
            "deftype(\"Province\", components=[\"Tax\"])\n";

        private const string Defs =
            "SkillDef(\"Medicine\")\nSkillDef(\"Cooking\")\nSkillDef(\"Base\", abstract=True)\n" +
            "NeedDef(\"Food\")\nNeedDef(\"Luxury\")\n" +
            "City(\"Paris\")\nCity(\"Rome\")\n" +
            "Unit(\"Scout\", Health=Health(base_max=12), Wealth=Wealth(amount=5), Skill={\"Cooking\": Skill(level=4)},\n" +
            "    Need={\"Food\": Need()})\n" +
            "Unit(\"Guard\", label=\"g\", Health=Health(base_max=20, current=7))\n" +
            "Province(\"Gaul\")\n";

        private World _world = null!;
        private EntityId _paris;
        private EntityId _rome;

        private World NewWorld()
        {
            Mod("core", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            _paris = _world.Create("Paris");
            _rome = _world.Create("Rome");
            return _world;
        }

        private MemberHandle M(string owner, string member) => _world.Member(owner, member);

        private EntityId Unit(string def = "Scout", EntityId? city = null) =>
            _world.Create(def, new Init(M("Location", "city"), new EntityValue(city ?? _paris)));

        private static long Int(Value v) => ((IntValue)v).Value;

        private static WorldException Fails(TestDelegate action, string code)
        {
            var e = Assert.Throws<WorldException>(action)!;
            Assert.That(e.Code, Is.EqualTo(code), e.ToString());
            return e;
        }

        // ---- 읽기 (Flyweight) ----

        [Test]
        public void ReadsComeFromTheEntityThenTheDefThenTheSchemaDefault()
        {
            NewWorld();
            var scout = Unit();
            Assert.That(Int(_world.Get(scout, M("Health", "base_max"))), Is.EqualTo(12));        // Def
            Assert.That(((FixedValue)_world.Get(scout, M("Wealth", "amount"))).Value, Is.EqualTo(Fixed.FromRaw(5_000_000))); // Def 정수 → fixed
            Assert.That(((StrValue)_world.Get(scout, M("Unit", "label"))).Value, Is.EqualTo("?")); // Def 타입 const 기본값

            _world.Set(scout, M("Wealth", "amount"), new IntValue(9));
            Assert.That(((FixedValue)_world.Get(scout, M("Wealth", "amount"))).Value, Is.EqualTo(Fixed.FromRaw(9_000_000)));
            Assert.That(_world.DefOf(scout), Is.EqualTo("Scout"));
        }

        [Test]
        public void DefaultFromIsComputedOnceAtCreation()
        {
            NewWorld();
            Assert.That(Int(_world.Get(Unit("Scout"), M("Health", "current"))), Is.EqualTo(12)); // base_max에서
            Assert.That(Int(_world.Get(Unit("Guard"), M("Health", "current"))), Is.EqualTo(7));  // Def가 준 값
        }

        [Test]
        public void ConstAndDerivedCannotBeWrittenAndDerivedIsNotComputedYet()
        {
            NewWorld();
            var scout = Unit();
            Fails(() => _world.Set(scout, M("Health", "base_max"), new IntValue(1)), Codes.RunNotWritable);
            Fails(() => _world.Set(scout, M("Unit", "label"), new StrValue("x")), Codes.RunNotWritable);
            Fails(() => _world.Get(scout, M("Health", "max")), Codes.RunNotComputed);
        }

        // ---- 쓰기 검사 (G92) ----

        [Test]
        public void WritesOutOfRangeOrOfTheWrongTypeFailAndChangeNothing()
        {
            NewWorld();
            var scout = Unit();
            var e = Fails(() => _world.Set(scout, M("Health", "current"), new IntValue(-1)), Codes.RunRange);
            Assert.That(e.Message, Does.Contain("below min 0"));
            Fails(() => _world.Set(scout, M("Health", "current"), new FixedValue(Fixed.FromRaw(1))), Codes.RunType);
            Fails(() => _world.Set(scout, M("Health", "current"), new StrValue("3")), Codes.RunType);
            Assert.That(Int(_world.Get(scout, M("Health", "current"))), Is.EqualTo(12));
        }

        // ---- id와 순회 (G87·G88) ----

        [Test]
        public void IdsCountUpAndAreNeverReused()
        {
            NewWorld();
            var a = Unit();
            var b = Unit();
            _world.Destroy(a);
            var c = Unit();
            Assert.That((a.Value, b.Value, c.Value), Is.EqualTo((3L, 4L, 5L)));
            Assert.That(_world.All("Unit"), Is.EqualTo(new[] { b, c }));
            Assert.That(_world.IsAlive(a), Is.False);
            Assert.That(Fails(() => _world.Get(a, M("Wealth", "amount")), Codes.RunDeadEntity).Message, Does.Contain("destroyed"));
            Assert.That(Fails(() => _world.DefOf(new EntityId(99)), Codes.RunDeadEntity).Message, Does.Contain("never created"));
        }

        [Test]
        public void AllIsASnapshotTakenWhenCalled()
        {
            NewWorld();
            var a = Unit();
            var b = Unit();
            var seen = _world.All("Unit");
            _world.Destroy(a);
            Unit();
            Assert.That(seen, Is.EqualTo(new[] { a, b }));
            Assert.That(_world.All("Unit").Count, Is.EqualTo(2));
        }

        [Test]
        public void AFailedCreateLeavesNoTrace()
        {
            NewWorld();
            Fails(() => _world.Create("Scout"), Codes.RunMissingValue); // 필수 Ref 없음
            var scout = Unit();
            Assert.That(scout.Value, Is.EqualTo(3L));
            Assert.That(_world.All("Unit"), Is.EqualTo(new[] { scout }));
        }

        [Test]
        public void UnknownNamesSuggestTheClosest()
        {
            NewWorld();
            Assert.That(Fails(() => _world.Member("Helth", "current"), Codes.RunUnknownName).Hint, Is.EqualTo("did you mean 'Health'?"));
            Assert.That(Fails(() => _world.Member("Health", "curent"), Codes.RunUnknownName).Hint, Is.EqualTo("did you mean 'current'?"));
            Assert.That(Fails(() => _world.Create("Scot"), Codes.RunUnknownName).Hint, Is.EqualTo("did you mean 'Scout'?"));
            Fails(() => _world.Create("Medicine"), Codes.RunNotEntity);
            Fails(() => _world.All("Unt"), Codes.RunUnknownName);
        }

        // ---- 생성 순간의 값 (G89·G91·G93) ----

        [Test]
        public void AFieldWithoutADefaultMustBeGivenAtCreation()
        {
            NewWorld();
            var e = Fails(() => _world.Create("Gaul"), Codes.RunMissingValue);
            Assert.That(e.Message, Does.Contain("'Tax.rate' has no value"));
            var gaul = _world.Create("Gaul", new Init(M("Tax", "rate"), new IntValue(2)));
            Assert.That(((FixedValue)_world.Get(gaul, M("Tax", "rate"))).Value, Is.EqualTo(Fixed.FromRaw(2_000_000)));
        }

        [Test]
        public void InitialValuesAreCheckedLikeWrites()
        {
            NewWorld();
            var city = new Init(M("Location", "city"), new EntityValue(_paris));
            Fails(() => _world.Create("Scout", city, new Init(M("Health", "current"), new IntValue(-5))), Codes.RunRange);
            Fails(() => _world.Create("Scout", city, city), Codes.RunType);                                  // 두 번
            Fails(() => _world.Create("Scout", city, new Init(M("Tax", "rate"), new IntValue(1))), Codes.RunNoComponent);
            Fails(() => _world.Create("Scout", city, new Init(M("Skill", "level"), new IntValue(1))), Codes.RunNoComponent);
            var scout = _world.Create("Scout", city, new Init(M("Health", "current"), new IntValue(3)));
            Assert.That(Int(_world.Get(scout, M("Health", "current"))), Is.EqualTo(3));
        }

        // ---- Ref (G91) ----

        [Test]
        public void ARefToADestroyedEntityIsNoneIfOptionalAndAnErrorOtherwise()
        {
            NewWorld();
            var scout = Unit(city: _rome);
            _world.Set(scout, M("Follow", "leader"), new EntityValue(_rome));
            Assert.That(((EntityValue)_world.Get(scout, M("Location", "city"))).Id, Is.EqualTo(_rome));
            Assert.That(_world.Get(Unit(), M("Follow", "leader")), Is.SameAs(NoneValue.Instance)); // 안 준 optional

            _world.Destroy(_rome);
            Assert.That(_world.Get(scout, M("Follow", "leader")), Is.SameAs(NoneValue.Instance));
            Fails(() => _world.Get(scout, M("Location", "city")), Codes.RunDeadRef);
            Fails(() => _world.Set(scout, M("Location", "city"), new EntityValue(_rome)), Codes.RunDeadRef);
            Fails(() => _world.Set(scout, M("Location", "city"), NoneValue.Instance), Codes.RunType);
            Fails(() => _world.Set(scout, M("Location", "city"), new EntityValue(scout)), Codes.RunType); // Unit은 City가 아님
        }

        [Test]
        public void InverseListsTheLiveSourcesInIdOrder()
        {
            NewWorld();
            var a = Unit(city: _paris);
            var b = Unit(city: _rome);
            var c = Unit(city: _paris);
            var city = M("Location", "city");
            Assert.That(_world.Inverse(_paris, city), Is.EqualTo(new[] { a, c }));

            _world.Set(b, city, new EntityValue(_paris));
            _world.Destroy(a);
            Assert.That(_world.Inverse(_paris, city), Is.EqualTo(new[] { b, c }));
            Assert.That(_world.Inverse(_rome, city), Is.Empty);
            Fails(() => _world.Inverse(b, city), Codes.RunType);
        }

        [Test]
        public void InverseFollowsOrderByThenId()
        {
            Mod("core", schema:
                "component(\"Slot\", pile=ref(\"Pile\", inverse=\"cards\", order_by=\"pos\"), pos=field(int, default=0))\n" +
                "component(\"Size\", n=field(int, default=0))\n" +
                "deftype(\"Pile\", components=[\"Size\"])\ndeftype(\"Card\", components=[\"Slot\"])\n",
                defs: "Pile(\"Deck\")\nCard(\"Ace\")\n");
            _world = new World(LoadClean());
            var deck = _world.Create("Deck");
            var pile = M("Slot", "pile");
            var pos = M("Slot", "pos");
            EntityId Card(int p) =>
                _world.Create("Ace", new Init(pile, new EntityValue(deck)), new Init(pos, new IntValue(p)));
            var a = Card(5);
            var b = Card(1);
            var c = Card(5);
            Assert.That(_world.Inverse(deck, pile), Is.EqualTo(new[] { b, a, c }));
            _world.Set(b, pos, new IntValue(9));
            Assert.That(_world.Inverse(deck, pile), Is.EqualTo(new[] { a, c, b }));
        }

        [Test]
        public void DefaultsThatNeedTheEvaluatorFailAtCreationForNow()
        {
            Mod("core", schema:
                "component(\"A\", m=derived(int, lambda a: 1), cur=field(int, default_from=\"m\"))\n" +
                "component(\"B\", cur=field(int, default=lambda b: 2))\n" +
                "deftype(\"X\", components=[\"A\"])\ndeftype(\"Y\", components=[\"B\"])\n",
                defs: "X(\"x\")\nY(\"y\")\n");
            _world = new World(LoadClean());
            Assert.That(Fails(() => _world.Create("x"), Codes.RunNotComputed).Message, Does.Contain("default_from"));
            Assert.That(Fails(() => _world.Create("y"), Codes.RunNotComputed).Message, Does.Contain("function default"));
            var y = _world.Create("y", new Init(M("B", "cur"), new IntValue(4)));       // 직접 주면 된다
            Assert.That(Int(_world.Get(y, M("B", "cur"))), Is.EqualTo(4));
        }

        [TestCase("note=field(str)", "has no default")]
        [TestCase("base=const(int, default=0), cur=field(int, default_from=\"base\")", "uses default_from")]
        [TestCase("note=field(int, default=lambda s: 0)", "uses a function default")]
        [TestCase("note=ref(\"City\")", "is a required ref")]
        public void KeysAllRowsNeedFixedDefaultsBecauseTheyAreNeverCreated(string members, string problem)
        {
            Mod("core", schema:
                "component(\"Banner\", c=field(int, default=0))\ndeftype(\"City\", components=[\"Banner\"])\n" +
                "deftype(\"SkillDef\")\ncomponent(\"Skill\", key=\"SkillDef\", " + members + ")\n");
            Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.SchBadDeclaration);
            Assert.That(d.Message, Does.Contain(problem));
            Assert.That(d.Hint, Does.Contain("keys=\"listed\""));
        }

        [Test]
        public void KeysListedAndOptionalRefsMayLeaveRowValuesToCreation()
        {
            Mod("core", schema:
                "component(\"Banner\", c=field(int, default=0))\ndeftype(\"City\", components=[\"Banner\"])\n" +
                "deftype(\"SkillDef\")\n" +
                "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", note=field(str), at=ref(\"City\"))\n" +
                "component(\"Perk\", key=\"SkillDef\", home=ref(\"City\", optional=True))\n");
            LoadClean();
        }

        // ---- 런타임 컴포넌트 ----

        [Test]
        public void RuntimeComponentsReadSchemaDefaultsAndCanBeRemoved()
        {
            NewWorld();
            var scout = Unit();
            Assert.That(_world.Has(scout, "Poisoned"), Is.False);
            Fails(() => _world.Get(scout, M("Poisoned", "turns")), Codes.RunNoComponent);

            _world.Add(scout, "Poisoned", new Init(M("Poisoned", "turns"), new IntValue(5)));
            Assert.That(Int(_world.Get(scout, M("Poisoned", "turns"))), Is.EqualTo(5));
            Assert.That(Int(_world.Get(scout, M("Poisoned", "strength"))), Is.EqualTo(1));
            Fails(() => _world.Add(scout, "Poisoned"), Codes.RunNoComponent);

            _world.Remove(scout, "Poisoned");
            Assert.That(_world.Has(scout, "Poisoned"), Is.False);
            _world.Add(scout, "Poisoned");
            Assert.That(Int(_world.Get(scout, M("Poisoned", "turns"))), Is.EqualTo(3)); // 뗄 때 값도 사라졌다

            Fails(() => _world.Remove(scout, "Health"), Codes.RunNoComponent);           // Def의 컴포넌트
            Fails(() => _world.Add(scout, "Cursed"), Codes.RunMissingValue);             // 기본값 없는 const
            Assert.That(_world.Has(scout, "Cursed"), Is.False);
        }

        // ---- 키 컴포넌트 ----

        [Test]
        public void KeysAllRowsAlwaysExistAndCanBeWritten()
        {
            NewWorld();
            var scout = Unit();
            var level = M("Skill", "level");
            Assert.That(Int(_world.GetRow(scout, level, "Cooking")), Is.EqualTo(4));   // Def 줄
            Assert.That(Int(_world.GetRow(scout, level, "Medicine")), Is.EqualTo(0));  // 기본 줄
            Assert.That(Int(_world.GetRow(scout, M("Skill", "cap"), "Medicine")), Is.EqualTo(20));
            _world.SetRow(scout, level, "Medicine", new IntValue(8));
            Assert.That(Int(_world.GetRow(scout, level, "Medicine")), Is.EqualTo(8));
            Fails(() => _world.SetRow(scout, level, "Medicine", new IntValue(21)), Codes.RunRange);

            Assert.That(_world.RowKeys(scout, "Skill"), Is.EqualTo(new[] { "Cooking", "Medicine" }));
            Assert.That(Fails(() => _world.GetRow(scout, level, "Medicin"), Codes.RunUnknownName).Hint, Is.EqualTo("did you mean 'Medicine'?"));
            Fails(() => _world.GetRow(scout, level, "Base"), Codes.RunUnknownName); // abstract
            Fails(() => _world.AddRow(scout, "Skill", "Medicine"), Codes.RunNoRow);
            Fails(() => _world.Get(scout, level), Codes.RunNoRow);
        }

        [Test]
        public void ListedRowsComeFromTheDefOrAreAddedAtRuntime()
        {
            NewWorld();
            var scout = Unit();
            var met = M("Need", "met");
            Assert.That(_world.RowKeys(scout, "Need"), Is.EqualTo(new[] { "Food" }));
            Assert.That(_world.HasRow(scout, "Need", "Luxury"), Is.False);
            Fails(() => _world.GetRow(scout, met, "Luxury"), Codes.RunNoRow);

            _world.AddRow(scout, "Need", "Luxury", new Init(met, BoolValue.True));
            Assert.That(_world.GetRow(scout, met, "Luxury"), Is.SameAs(BoolValue.True));
            Assert.That(Int(_world.GetRow(scout, M("Need", "weight"), "Luxury")), Is.EqualTo(1));
            Assert.That(_world.RowKeys(scout, "Need"), Is.EqualTo(new[] { "Food", "Luxury" }));

            _world.RemoveRow(scout, "Need", "Luxury");
            Assert.That(_world.HasRow(scout, "Need", "Luxury"), Is.False);
            Fails(() => _world.RemoveRow(scout, "Need", "Food"), Codes.RunNoRow); // Def에서 온 줄
        }

        [Test]
        public void EntityKeyedRowsDisappearWithTheirKey()
        {
            NewWorld();
            var scout = Unit();
            var score = M("Opinion", "score");
            _world.AddRow(scout, "Opinion", _rome, new Init(score, new IntValue(-3)));
            _world.AddRow(scout, "Opinion", _paris);
            Assert.That(Int(_world.GetRow(scout, score, _rome)), Is.EqualTo(-3));
            Assert.That(_world.RowEntities(scout, "Opinion"), Is.EqualTo(new[] { _paris, _rome }));

            _world.Destroy(_rome);
            Assert.That(_world.RowEntities(scout, "Opinion"), Is.EqualTo(new[] { _paris }));
            Fails(() => _world.AddRow(scout, "Opinion", scout), Codes.RunType); // Unit은 City가 아님
            Fails(() => _world.AddRow(scout, "Opinion", "Paris"), Codes.RunType); // Def id가 아니라 엔티티 키
        }
    }
}
