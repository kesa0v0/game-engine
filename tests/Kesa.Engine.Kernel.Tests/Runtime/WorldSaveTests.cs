using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>월드 상태의 저장과 불러오기 (spec S8, C4, spec-gaps G140).</summary>
    public class WorldSaveTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=5, min=0), max=const(int, default=10), regen=field(fixed, default=1))\n" +
            "component(\"Name\", text=field(str, default=\"\"), calm=field(bool, default=True))\n" +
            "component(\"Skill\", key=\"SkillDef\", keys=\"listed\", level=field(int, default=0))\n" +
            "component(\"Opinion\", key=Ref[\"Unit\"], score=field(int, default=0))\n" +
            "component(\"Die\", key=int, value=field(int, default=1))\n" +
            "component(\"Mood\", key=\"SkillDef\", keys=\"all\", v=field(int, default=0))\n" +
            "component(\"Home\", city=ref(\"City\", optional=True))\n" +
            "component(\"Wounded\", turns=field(int))\n" +
            "component(\"Power\", base=const(int, default=1), total=derived(int, lambda p: p.base + len(contribs(p, \"total\"))))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"City\", components=[\"Health\"])\n" +
            "deftype(\"Unit\", components=[\"Health\", \"Name\", \"Skill\", \"Mood\", \"Home\", \"Power\"])\n";

        private const string Defs =
            "SkillDef(\"Cooking\")\nSkillDef(\"Mining\")\n" +
            "City(\"Town\")\n" +
            "Unit(\"Scout\", Skill={\"Cooking\": Skill(level=2)})\n";

        private World _world = null!;
        private EntityId _town, _a, _b, _c;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs, extra: ", version=\"1.0\"");
            _world = new World(LoadClean());
        }

        /// <summary>
        /// id 틈, 기본값과 같게 쓴 값, 세 가지 키의 줄(붙인 줄·Def 줄·keys="all"의 처음 쓴 줄), fixed, 한글과 이스케이프 문자,
        /// 죽은 엔티티를 가리키는 Ref, 기여 행.
        /// </summary>
        private void BuildNastyWorld()
        {
            _town = _world.Create("Town");
            _a = _world.Create("Scout");
            _b = _world.Create("Scout");
            _c = _world.Create("Scout");
            _world.Destroy(_b);

            _world.Set(_a, M("Health", "current"), new IntValue(5)); // 기본값과 같아도 쓴 값이다
            _world.Set(_a, M("Health", "regen"), new FixedValue(Fixed.FromRaw(1_500_000)));
            _world.Set(_a, M("Name", "text"), new StrValue("검은 늑대 \"대장\"\n\\"));
            _world.Set(_a, M("Name", "calm"), BoolValue.False);
            _world.Set(_a, M("Home", "city"), new EntityValue(_town));
            _world.SetRow(_a, M("Skill", "level"), "Cooking", new IntValue(3)); // Def에서 온 줄
            _world.AddRow(_a, "Skill", "Mining");                               // 붙인 줄, 값 없음
            _world.Add(_a, "Opinion");
            _world.AddRow(_a, "Opinion", _c, new Init(M("Opinion", "score"), new IntValue(-2)));
            _world.Add(_a, "Wounded", new Init(M("Wounded", "turns"), new IntValue(3)));
            _world.Add(_c, "Die");
            _world.AddRow(_c, "Die", 3);
            _world.AddRow(_c, "Die", -1, new Init(M("Die", "value"), new IntValue(6)));
            _world.AddContribution(_a, M("Power", "total"), "add", new IntValue(2), _c);
            _world.AddContribution(_c, M("Power", "total"), "add", new FixedValue(Fixed.FromRaw(250_000)));
            _world.SetRow(_a, M("Mood", "v"), "Mining", new IntValue(4)); // keys="all": 처음 쓸 때 기록이 생긴다
            var ruin = _world.Create("Town");
            _world.Set(_c, M("Home", "city"), new EntityValue(ruin));
            _world.Destroy(ruin); // _c의 칸은 죽은 #5를 가리킨 채 남는다 (G91)
        }

        private MemberHandle M(string owner, string member) => _world.Member(owner, member);

        private World Reload(byte[] save)
        {
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Diagnostics, Is.Empty);
            return result.World!;
        }

        [Test]
        public void SaveLoadSaveIsByteIdentical()
        {
            BuildNastyWorld();
            var first = _world.Save();
            var second = Reload(first).Save();
            Assert.That(Encoding.UTF8.GetString(second), Is.EqualTo(Encoding.UTF8.GetString(first)));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void TheLoadedWorldReadsTheSame()
        {
            BuildNastyWorld();
            var w = Reload(_world.Save());
            MemberHandle H(string owner, string member) => w.Member(owner, member);

            Assert.That(w.IsAlive(_b), Is.False);
            Assert.That(w.All("Unit"), Is.EqualTo(new[] { _a, _c }));
            Assert.That(((IntValue)w.Get(_a, H("Health", "current"))).Value, Is.EqualTo(5));
            Assert.That(((FixedValue)w.Get(_a, H("Health", "regen"))).Value.Raw, Is.EqualTo(1_500_000));
            Assert.That(((FixedValue)w.Get(_c, H("Health", "regen"))).Value.Raw, Is.EqualTo(Fixed.Scale));
            Assert.That(((StrValue)w.Get(_a, H("Name", "text"))).Value, Is.EqualTo("검은 늑대 \"대장\"\n\\"));
            Assert.That(((BoolValue)w.Get(_a, H("Name", "calm"))).Value, Is.False);
            Assert.That(((EntityValue)w.Get(_a, H("Home", "city"))).Id, Is.EqualTo(_town));
            Assert.That(w.Inverse(_town, H("Home", "city")), Is.EqualTo(new[] { _a }));

            Assert.That(w.RowKeys(_a, "Skill"), Is.EqualTo(new[] { "Cooking", "Mining" }));
            Assert.That(((IntValue)w.GetRow(_a, H("Skill", "level"), "Cooking")).Value, Is.EqualTo(3));
            Assert.That(((IntValue)w.GetRow(_a, H("Skill", "level"), "Mining")).Value, Is.EqualTo(0));
            Assert.That(w.RowEntities(_a, "Opinion"), Is.EqualTo(new[] { _c }));
            Assert.That(((IntValue)w.GetRow(_a, H("Opinion", "score"), _c)).Value, Is.EqualTo(-2));
            Assert.That(w.RowInts(_c, "Die"), Is.EqualTo(new long[] { -1, 3 }));
            Assert.That(((IntValue)w.GetRow(_c, H("Die", "value"), -1)).Value, Is.EqualTo(6));
            Assert.That(w.Has(_a, "Wounded"), Is.True);
            Assert.That(w.Has(_c, "Wounded"), Is.False);
            Assert.That(((IntValue)w.Get(_a, H("Power", "total"))).Value, Is.EqualTo(2));
            Assert.That(((IntValue)w.GetRow(_a, H("Mood", "v"), "Mining")).Value, Is.EqualTo(4));
            Assert.That(((IntValue)w.GetRow(_a, H("Mood", "v"), "Cooking")).Value, Is.EqualTo(0));
            Assert.That(w.Get(_c, H("Home", "city")), Is.InstanceOf<NoneValue>()); // 죽은 대상은 선택적 Ref에서 None

            // Def에서 온 줄은 여전히 뗄 수 없고, 붙인 줄은 뗄 수 있다
            Assert.Throws<WorldException>(() => w.RemoveRow(_a, "Skill", "Cooking"));
            w.RemoveRow(_a, "Skill", "Mining");
        }

        [Test]
        public void CountersSurviveSoIdsAreNotReused()
        {
            BuildNastyWorld();
            var w = Reload(_world.Save());
            Assert.That(w.Create("Scout"), Is.EqualTo(new EntityId(6)));
            Assert.That(w.AddContribution(_a, w.Member("Power", "total"), "add", new IntValue(1)), Is.EqualTo(3));
        }

        [Test]
        public void IndexesAreRebuiltSoDestroyStillCleansUp()
        {
            BuildNastyWorld();
            var w = Reload(_world.Save());
            w.Destroy(_c);
            // 죽은 키의 줄과 그 엔티티가 낸 기여 행이 지워진다
            Assert.That(w.RowEntities(_a, "Opinion"), Is.Empty);
            Assert.That(((IntValue)w.Get(_a, w.Member("Power", "total"))).Value, Is.EqualTo(1));
            Assert.That(w.Save(), Is.Not.EqualTo(_world.Save()));
        }

        [Test]
        public void TheFormatIsFixed()
        {
            var town = _world.Create("Town");
            _world.Set(town, M("Health", "regen"), new FixedValue(Fixed.FromRaw(1_500_000)));
            _world.Create("Scout");
            _world.Destroy(_world.All("Unit")[0]);
            _world.Add(town, "Die");
            _world.AddRow(town, "Die", 2);

            const string golden =
                "{\n" +
                "  \"format\": 1,\n" +
                "  \"mods\": [\n" +
                "    {\n" +
                "      \"id\": \"banking\",\n" +
                "      \"version\": \"1.0\"\n" +
                "    }\n" +
                "  ],\n" +
                "  \"counters\": {\n" +
                "    \"entity\": 3,\n" +
                "    \"contribution\": 1,\n" +
                "    \"flow\": 1,\n" +
                "    \"request\": 1\n" +
                "  },\n" +
                "  \"entities\": [\n" +
                "    {\n" +
                "      \"id\": 1,\n" +
                "      \"def\": \"Town\",\n" +
                "      \"runtime\": [\n" +
                "        \"Die\"\n" +
                "      ],\n" +
                "      \"values\": {\n" +
                "        \"Health\": {\n" +
                "          \"regen\": {\n" +
                "            \"fixed\": 1500000\n" +
                "          }\n" +
                "        }\n" +
                "      },\n" +
                "      \"rows\": {\n" +
                "        \"Die\": [\n" +
                "          {\n" +
                "            \"key\": 2,\n" +
                "            \"attached\": true,\n" +
                "            \"values\": {}\n" +
                "          }\n" +
                "        ]\n" +
                "      }\n" +
                "    }\n" +
                "  ],\n" +
                "  \"contributions\": [],\n" +
                "  \"flows\": []\n" +
                "}\n";
            Assert.That(Encoding.UTF8.GetString(_world.Save()), Is.EqualTo(golden));
        }

        [Test]
        public void ChangedContentIsRejectedWithEveryProblemListed()
        {
            BuildNastyWorld();
            var save = _world.Save();
            // 다음 판: Wounded가 없어지고 Name.calm이 없어지고 Mining이 없어졌다
            Mod("banking",
                schema: Schema.Replace("component(\"Wounded\", turns=field(int))\n", "")
                    .Replace(", calm=field(bool, default=True)", ""),
                defs: Defs.Replace("SkillDef(\"Mining\")\n", ""), extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save);

            Assert.That(result.Succeeded, Is.False);
            var messages = result.Diagnostics.Select(d => d.Code + " " + d.Message).ToList();
            Assert.That(messages, Is.EquivalentTo(new[]
            {
                "SAV002 #2 (Scout): the component 'Wounded' no longer exists",
                "SAV002 #2 (Scout) Name.calm: the member no longer exists",
                "SAV002 #2 (Scout) Mood: there is no SkillDef 'Mining' to key a row by any more",
                "SAV002 #2 (Scout) Skill: there is no SkillDef 'Mining' to key a row by any more",
            }));
        }

        [Test]
        public void ARemovedDefIsReportedOnceAndWhatPointsAtItDoesNotLookCorrupt()
        {
            Mod("banking", schema: Schema, defs: Defs + "Unit(\"Ranger\")\n", extra: ", version=\"1.0\"");
            _world = new World(LoadClean());
            var a = _world.Create("Scout");
            var ranger = _world.Create("Ranger");
            _world.Add(a, "Opinion");
            _world.AddRow(a, "Opinion", ranger);
            _world.AddContribution(a, M("Power", "total"), "add", new IntValue(1), ranger);
            _world.AddContribution(ranger, M("Power", "total"), "add", new IntValue(1));
            _world.Set(a, M("Home", "city"), new EntityValue(_world.Create("Town")));
            var save = _world.Save();

            Mod("banking", schema: Schema, defs: Defs, extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV002 #2 (Ranger): the Def 'Ranger' no longer exists",
            }));
        }

        [Test]
        public void AMemberAddedWithoutAStaticDefaultIsRejected()
        {
            var town = _world.Create("Town");
            _world.Create("Scout");
            var save = _world.Save();
            Mod("banking",
                schema: Schema.Replace("calm=field(bool, default=True)", "calm=field(bool, default=True), age=field(int)")
                    .Replace("component(\"Wounded\", turns=field(int))\n",
                        "component(\"Wounded\", turns=field(int))\ncomponent(\"Fresh\", n=field(int, default=0), m=field(int))\n")
                    .Replace("deftype(\"City\", components=[\"Health\"])", "deftype(\"City\", components=[\"Health\", \"Fresh\"])"),
                defs: Defs, extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False, result.Succeeded ? "loaded; reading gives: " + ReadOrError(result.World!, town) : "");
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV002 #1 (Town) Fresh.m: the member is new and has no default to fill the saved entity with",
                "SAV002 #2 (Scout) Name.age: the member is new and has no default to fill the saved entity with",
            }));
        }

        private static string ReadOrError(World w, EntityId town)
        {
            try
            {
                return World.Show(w.Get(town, w.Member("Fresh", "m")));
            }
            catch (WorldException e)
            {
                return e.Message;
            }
        }

        [Test]
        public void AValueOutsideATightenedRangeIsRejected()
        {
            var town = _world.Create("Town");
            _world.Set(town, M("Health", "current"), new IntValue(8));
            _world.Set(town, M("Health", "regen"), new FixedValue(Fixed.FromRaw(1_500_000)));
            var save = _world.Save();
            // 다음 판에서 범위가 좁아졌다: 저장된 값을 조용히 받거나 자르지 않는다 (G92)
            Mod("banking",
                schema: Schema.Replace("current=field(int, default=5, min=0)", "current=field(int, default=5, min=0, max=6)")
                    .Replace("regen=field(fixed, default=1)", "regen=field(fixed, default=2, min=2)"),
                defs: Defs, extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV002 #1 (Town) Health.current: 8 is above max 6",
                "SAV002 #1 (Town) Health.regen: 1.5 is below min 2",
            }));
        }

        [Test]
        public void AChangedModVersionIsOnlyAWarning()
        {
            BuildNastyWorld();
            var save = _world.Save();
            Mod("banking", schema: Schema, defs: Defs, extra: ", version=\"1.1\"");
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.True);
            var d = result.Diagnostics.Single();
            Assert.That(d.Severity, Is.EqualTo(Severity.Warning));
            Assert.That(d.Code, Is.EqualTo(Codes.SaveModsChanged));
            Assert.That(d.Message, Does.Contain("'banking' 1.0 -> 1.1"));
        }

        [TestCase("not json", "not valid JSON")]
        [TestCase("{\"format\": 2}", "this engine reads format 1")]
        [TestCase("{\"format\": 1.5}", "only whole numbers")]
        [TestCase("[]", "should be an object")]
        public void ABrokenSaveIsRejected(string text, string expected)
        {
            var result = World.Load(LoadClean(), Encoding.UTF8.GetBytes(text));
            Assert.That(result.Succeeded, Is.False);
            var d = result.Diagnostics.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SaveCorrupt));
            Assert.That(d.Message, Does.Contain(expected));
        }

        [Test]
        public void ANumberBeyondDoublePrecisionSurvives()
        {
            var town = _world.Create("Town");
            const long big = 9_007_199_254_740_993; // 2^53 + 1: double로 거치면 깨진다
            _world.Set(town, M("Health", "current"), new IntValue(big));
            var w = Reload(_world.Save());
            Assert.That(((IntValue)w.Get(town, w.Member("Health", "current"))).Value, Is.EqualTo(big));
        }
    }
}
