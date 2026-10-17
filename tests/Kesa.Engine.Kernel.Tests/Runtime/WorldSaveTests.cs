using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
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

        /// <summary>UTF-8 바이트의 SHA-256 앞 128비트, 16진수 (G212를 엔진 코드 없이 다시 계산).</summary>
        private static string Hash(string text)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            return string.Concat(digest.Take(16).Select(b => b.ToString("x2")));
        }

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

            // 내용 지문은 엔진 코드를 쓰지 않고 G212의 규칙대로 따로 계산한다 (파일은 LF, BOM 없음)
            string manifest = Hash("Mod(id=\"banking\", version=\"1.0\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            string defs = Hash(Defs), schema = Hash(Schema);
            string mod = Hash("15:kesa-mod-hash/1" + "12:defs/defs.py4:defs32:" + defs + "6:mod.py8:manifest32:" + manifest +
                              "15:schema/types.py6:schema32:" + schema);
            string golden =
                "{\n" +
                "  \"format\": 1,\n" +
                "  \"mods\": [\n" +
                "    {\n" +
                "      \"id\": \"banking\",\n" +
                "      \"version\": \"1.0\",\n" +
                "      \"hash\": \"" + mod + "\",\n" +
                "      \"files\": [\n" +
                "        {\n" +
                "          \"path\": \"defs/defs.py\",\n" +
                "          \"kind\": \"defs\",\n" +
                "          \"hash\": \"" + defs + "\"\n" +
                "        },\n" +
                "        {\n" +
                "          \"path\": \"mod.py\",\n" +
                "          \"kind\": \"manifest\",\n" +
                "          \"hash\": \"" + manifest + "\"\n" +
                "        },\n" +
                "        {\n" +
                "          \"path\": \"schema/types.py\",\n" +
                "          \"kind\": \"schema\",\n" +
                "          \"hash\": \"" + schema + "\"\n" +
                "        }\n" +
                "      ]\n" +
                "    }\n" +
                "  ],\n" +
                "  \"tick\": 0,\n" +
                "  \"seed\": 0,\n" +
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
                "  \"flows\": [],\n" +
                "  \"when\": [],\n" +
                "  \"when_cooldown\": [],\n" +
                "  \"when_once\": [],\n" +
                "  \"rand\": []\n" +
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
            var messages = result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList();
            Assert.That(messages, Is.EquivalentTo(new[]
            {
                "SAV002 #2 (Scout): the component 'Wounded' no longer exists",
                "SAV002 #2 (Scout) Name.calm: the member no longer exists",
                "SAV002 #2 (Scout) Mood: there is no SkillDef 'Mining' to key a row by any more",
                "SAV002 #2 (Scout) Skill: there is no SkillDef 'Mining' to key a row by any more",
            }));
        }

        /// <summary>호스트가 허락하면 지워진 컴포넌트·멤버·Def 키 줄의 값을 버리고, 버린 곳마다 경고를 남기고 불러온다 (G210).</summary>
        [Test]
        public void TheHostCanDropWhatTheContentRemovedAndLoadWithWarnings()
        {
            BuildNastyWorld();
            var save = _world.Save();
            Mod("banking",
                schema: Schema.Replace("component(\"Wounded\", turns=field(int))\n", "")
                    .Replace(", calm=field(bool, default=True)", ""),
                defs: Defs.Replace("SkillDef(\"Mining\")\n", ""), extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save, dropRemoved: true);

            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Severity + " " + d.Message).ToList(), Is.EquivalentTo(new[]
            {
                "SAV005 Warning #2 (Scout): the component 'Wounded' no longer exists; its saved value was dropped",
                "SAV005 Warning #2 (Scout) Name.calm: the member no longer exists; its saved value was dropped",
                "SAV005 Warning #2 (Scout) Mood: there is no SkillDef 'Mining' to key a row by any more; its saved value was dropped",
                "SAV005 Warning #2 (Scout) Skill: there is no SkillDef 'Mining' to key a row by any more; its saved value was dropped",
            }));

            // 버리지 않은 값은 그대로다
            var w = result.World!;
            Assert.That(((StrValue)w.Get(_a, w.Member("Name", "text"))).Value, Is.EqualTo("검은 늑대 \"대장\"\n\\"));
            Assert.That(w.RowKeys(_a, "Skill"), Is.EqualTo(new[] { "Cooking" }));
            Assert.That(((IntValue)w.GetRow(_a, w.Member("Skill", "level"), "Cooking")).Value, Is.EqualTo(3));
            Assert.That(((IntValue)w.Get(_a, w.Member("Power", "total"))).Value, Is.EqualTo(2));

            // 버린 뒤의 저장은 지금 내용과 맞는다: 경고 없이 다시 불러오고 바이트가 같다
            var dropped = w.Save();
            var again = World.Load(LoadClean(), dropped);
            Assert.That(again.Diagnostics, Is.Empty, string.Join("\n", again.Diagnostics));
            Assert.That(again.World!.Save(), Is.EqualTo(dropped));
        }

        /// <summary>Def 목록에서 빠진 컴포넌트는 그 값과 그 컴포넌트의 기여 행을 함께 버린다 (G210).</summary>
        [Test]
        public void DroppingAComponentADefNoLongerListsDropsItsValuesAndContributions()
        {
            BuildNastyWorld();
            var save = _world.Save();
            Mod("banking",
                schema: Schema.Replace("\"Mood\", \"Home\", \"Power\"]", "\"Mood\"]"),
                defs: Defs, extra: ", version=\"1.0\"");

            var rejected = World.Load(LoadClean(), save);
            Assert.That(rejected.Succeeded, Is.False);

            var result = World.Load(LoadClean(), save, dropRemoved: true);
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            var messages = result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList();
            Assert.That(messages, Is.EquivalentTo(rejected.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => "SAV005 " + d.Message + "; its saved value was dropped")));
            Assert.That(messages, Has.Some.Contains("#2 (Scout): the entity no longer has 'Home'"));
            Assert.That(messages, Has.Some.Contains("to #2: the entity no longer has 'Power'"));
            Assert.That(messages, Has.Some.Contains("to #4: the entity no longer has 'Power'"));

            var dropped = result.World!.Save();
            var again = World.Load(LoadClean(), dropped);
            Assert.That(again.Diagnostics, Is.Empty, string.Join("\n", again.Diagnostics));
            Assert.That(Encoding.UTF8.GetString(dropped), Does.Not.Contain("\"Home\"").And.Not.Contain("\"Power\""));
        }

        /// <summary>버리기를 허락해도 타입이 바뀐 값은 버리지 않고 거부한다 (G210: 그것은 나중의 migrate 몫).</summary>
        [Test]
        public void DroppingDoesNotCoverAChangedType()
        {
            BuildNastyWorld();
            var save = _world.Save();
            Mod("banking",
                schema: Schema.Replace("component(\"Wounded\", turns=field(int))\n", "")
                    .Replace("text=field(str, default=\"\")", "text=field(int, default=0)"),
                defs: Defs, extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save, dropRemoved: true);

            Assert.That(result.Succeeded, Is.False);
            var codes = result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Severity).ToList();
            Assert.That(codes, Does.Contain("SAV005 Warning"));
            Assert.That(result.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message).Single(),
                Does.StartWith("#2 (Scout) Name.text:"));
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
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV002 #2 (Ranger): the Def 'Ranger' no longer exists",
            }));
        }

        private const string RefSchema =
            Schema + "component(\"Rival\", target=ref(\"Unit\", optional=True))\ncomponent(\"Leash\", target=ref(\"Unit\"))\n";

        private const string Brew = "@command(free=True, acts_for=None)\ndef Brew(u: Ref[Unit]):\n    wait(ticks=5)\n";

        /// <summary>
        /// 호스트가 허락하면 Def가 없어진 엔티티는 불러올 때 파괴된 것처럼 다룬다 (G211): 키 줄·기여 행·대상 Flow는 함께 버리고 경고,
        /// 남의 선택적 Ref 칸은 죽은 id로 남아 읽으면 None, 반응은 부르지 않는다.
        /// </summary>
        [Test]
        public void DroppingARemovedDefTreatsItsEntitiesAsDestroyedOnLoad()
        {
            Mod("banking", schema: RefSchema, defs: Defs + "Unit(\"Ranger\")\n" + Brew, extra: ", version=\"1.0\"");
            _world = new World(LoadClean());
            var a = _world.Create("Scout");
            var ranger = _world.Create("Ranger");
            _world.Add(a, "Opinion");
            _world.AddRow(a, "Opinion", ranger);
            _world.AddContribution(a, M("Power", "total"), "add", new IntValue(1), ranger);
            _world.AddContribution(ranger, M("Power", "total"), "add", new IntValue(1));
            _world.Add(a, "Rival", new Init(M("Rival", "target"), new EntityValue(ranger)));
            _world.Submit("host", "banking.Brew", new Dictionary<string, Value> { ["u"] = new EntityValue(ranger) });
            _world.Submit("host", "banking.Brew", new Dictionary<string, Value> { ["u"] = new EntityValue(a) });
            var save = _world.Save();

            Mod("banking", schema: RefSchema, defs: Defs + Brew, extra: ", version=\"1.0\"");
            var rejected = World.Load(LoadClean(), save);
            Assert.That(rejected.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV002 #2 (Ranger): the Def 'Ranger' no longer exists",
            }), "by default nothing changes");

            var result = World.Load(LoadClean(), save, dropRemoved: true);
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics));
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Severity + " " + d.Message).ToList(), Is.EquivalentTo(new[]
            {
                "SAV005 Warning #2 (Ranger): the Def 'Ranger' no longer exists; its saved value was dropped",
                "SAV005 Warning #1 (Scout) Opinion: the row keyed by #2 went with its entity",
                "SAV005 Warning contribution 1 to #1 from #2 went with its entity",
                "SAV005 Warning contribution 2 to #2 went with its entity",
                "SAV005 Warning flow 1 (banking.Brew) was canceled: its subject #2 was destroyed",
            }));

            var w = result.World!;
            Assert.That(w.IsAlive(ranger), Is.False);
            Assert.That(w.RowEntities(a, "Opinion"), Is.Empty);
            Assert.That(((IntValue)w.Get(a, w.Member("Power", "total"))).Value, Is.EqualTo(1));
            Assert.That(w.Get(a, w.Member("Rival", "target")), Is.InstanceOf<NoneValue>(), "an optional ref to it reads None (G91)");

            var dropped = w.Save();
            var again = World.Load(LoadClean(), dropped);
            Assert.That(again.Diagnostics, Is.Empty, string.Join("\n", again.Diagnostics));
            Assert.That(again.World!.Save(), Is.EqualTo(dropped));

            // 남은 Flow는 그대로 이어진다
            var woke = w.Advance(5).Reactions.Single();
            Assert.That(woke.Handler + " " + woke.Outcome, Is.EqualTo("banking.Brew Finished"));
        }

        /// <summary>버린 엔티티를 가리키는 필수 Ref는 허락해도 거부한다 (S8 "필수 Ref는 로드 오류", G211).</summary>
        [Test]
        public void ARequiredRefToADroppedEntityIsStillRejected()
        {
            Mod("banking", schema: RefSchema, defs: Defs + "Unit(\"Ranger\")\n", extra: ", version=\"1.0\"");
            _world = new World(LoadClean());
            var a = _world.Create("Scout");
            var ranger = _world.Create("Ranger");
            _world.Add(a, "Leash", new Init(M("Leash", "target"), new EntityValue(ranger)));
            var save = _world.Save();

            Mod("banking", schema: RefSchema, defs: Defs, extra: ", version=\"1.0\"");
            var result = World.Load(LoadClean(), save, dropRemoved: true);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV005 #2 (Ranger): the Def 'Ranger' no longer exists; its saved value was dropped",
                "SAV002 #1 (Scout) Leash.target: the required ref points at #2, whose Def no longer exists",
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
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
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
            Assert.That(result.Diagnostics.Where(d => d.Code != Codes.SaveModsChanged).Select(d => d.Code + " " + d.Message).ToList(), Is.EqualTo(new[]
            {
                "SAV002 #1 (Town) Health.current: 8 is above max 6",
                "SAV002 #1 (Town) Health.regen: 1.5 is below min 2",
            }));
        }

        // ---- 모드 내용 지문 (G212) ----

        [Test]
        public void TheContentListsEveryFileTheEngineReadAndNothingElse()
        {
            Write("banking/defs/notes.txt", "not read\n");
            Write("banking/unlisted.py", "x = 1\n");
            var content = LoadClean().Contents["banking"];
            Assert.That(content.Files.Select(f => f.Path + " " + ModFile.KindName(f.Kind)), Is.EqualTo(new[]
            {
                "defs/defs.py defs", "mod.py manifest", "schema/types.py schema",
            }));
            Assert.That(content.Files[0].Hash, Is.EqualTo(Hash(Defs)));
        }

        [Test]
        public void LineEndingsAndABomDoNotChangeTheHash()
        {
            var save = _world.Save();
            string before = LoadClean().Contents["banking"].Hash;
            Write("banking/defs/defs.py", "﻿" + Defs.Replace("\n", "\r\n"));
            Write("banking/schema/types.py", Schema.Replace("\n", "\r"));
            var loaded = LoadClean();
            Assert.That(loaded.Contents["banking"].Hash, Is.EqualTo(before));
            Assert.That(World.Load(loaded, save).Diagnostics, Is.Empty);
        }

        [Test]
        public void AChangedFileIsReportedEvenWhenTheVersionIsTheSame()
        {
            var save = _world.Save();
            Write("banking/defs/defs.py", Defs + "# a comment is content too\n");
            Write("banking/defs/extra.py", "City(\"Port\")\n");
            var result = World.Load(LoadClean(), save);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Severity + " " + d.Message), Is.EqualTo(new[]
            {
                "SAV003 Warning the mods differ from the save: 'banking' 1.0, content changed (changed defs/defs.py, added defs/extra.py)",
            }));

            Mod("banking", schema: Schema, extra: ", version=\"1.1\"");
            Assert.That(World.Load(LoadClean(), save).Diagnostics.Single().Message, Is.EqualTo(
                "the mods differ from the save: 'banking' 1.0 -> 1.1, content changed (removed defs/defs.py, changed mod.py)"));
        }

        [Test]
        public void ASaveWithoutContentHashesIsCorrupt()
        {
            string save = Encoding.UTF8.GetString(_world.Save());
            string stripped = System.Text.RegularExpressions.Regex.Replace(save, ",\n      \"hash\": \"[0-9a-f]{32}\",\n      \"files\": \\[[^\\]]*\\]", "");
            Assert.That(stripped, Is.Not.EqualTo(save));
            var result = World.Load(LoadClean(), Encoding.UTF8.GetBytes(stripped));
            Assert.That(result.Diagnostics.Select(d => d.Code + " " + d.Message), Is.EqualTo(new[] { "SAV001 the save is missing \"files\"" }));
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
