using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary>
    /// 번역기 (spec-gaps G267·G269·G270): 번역 인자 값 표, 제목·선택지 자리(<c>Label</c>), 속성(<c>Attr</c>), 화면 값(<c>Value</c>).
    /// </summary>
    public class TranslatorTests : TextTestBase
    {
        private const string Schema =
            "component(\"Hull\", hp=field(int, default=1))\n" +
            "deftype(\"Ship\", components=[\"Hull\"])\n";

        private const string Defs = "Ship(\"Sloop\")\nShip(\"Brig\")\nShip(\"Raft\")\n";

        private const string Texts =
            "def-harbor-Sloop = sloop\n" +
            "    .desc = a small boat\n" +
            "def-harbor-Brig = brig\n" +
            "harbor-bought = bought { $ship } for { $n } gold ({ $f })\n" +
            "harbor-said = { $who } said: { $what }\n" +
            "harbor-flag = { $b ->\n    [true] yes\n   *[other] no\n} { $z }\n" +
            "harbor-count = { $n ->\n    [one] one ship\n   *[other] { $n } ships\n}\n" +
            "harbor-accept = Accept\n" +
            "    .desc = take the deal\n" +
            "harbor-offer = Offer from { $who }\n" +
            "    .desc = { $who } offers a deal\n";

        private List<string> _errors = null!;
        private Translator _tr = null!;
        private World _world = null!;

        [SetUp]
        public void NewTranslator()
        {
            Mod("harbor", schema: Schema, defs: Defs);
            Ftl("harbor", "en", Texts);
            var content = LoadContent();
            _world = new World(content);
            _errors = new List<string>();
            _tr = LoadTextsClean(content).For(new[] { "en" }, _errors.Add);
        }

        private static TextValue T(string key, Dictionary<string, Value> args, Dictionary<string, string>? defs = null) =>
            new TextValue(key, args, defs);

        // 번역기는 경계 값만 받는다: 호스트 세션(LocalSession)처럼 커널 값을 바꿔 넘긴다
        private static BridgeValue B(Value value, World? world = null) =>
            KernelValues.ToBridge(value, id => world != null && world.IsAlive(id) ? world.DefOf(id) : null);

        private string Format(TextValue text) => _tr.Format((BridgeText)B(text));

        private string Label(Value value, World? world = null) => _tr.Label(B(value, world));

        private string Attr(Value value, string attribute, World? world = null) => _tr.Attr(B(value, world), attribute);

        private string Shown(Value value) => _tr.Value(B(value));

        [Test]
        public void NumbersStringsAndDefIdsFillTheirPlaces()
        {
            string s = Format(T("harbor-bought", new Dictionary<string, Value>
            {
                ["ship"] = new StrValue("harbor:Sloop"), ["n"] = new IntValue(5), ["f"] = new FixedValue(Fixed.FromRaw(1_500_000)),
            }));
            Assert.That(s, Is.EqualTo("bought sloop for 5 gold (1.5)"));
            string literal = Format(T("harbor-said", new Dictionary<string, Value>
            {
                ["who"] = new StrValue("Sloop"), ["what"] = new StrValue("harbor:Nothing"),
            }));
            Assert.That(literal, Is.EqualTo("Sloop said: harbor:Nothing"), "only a loaded Def's full id becomes its name");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void NumbersKeepTheirDecimalText()
        {
            string Of(Value n) => Format(T("harbor-bought", new Dictionary<string, Value>
                { ["ship"] = new StrValue("x"), ["n"] = n, ["f"] = new FixedValue(Fixed.FromRaw(1)) }));
            Assert.That(Of(new IntValue(1234567)), Is.EqualTo("bought x for 1234567 gold (0.000001)"));
            Assert.That(Of(new IntValue(1234567890123456789)), Does.Contain(" 1234567890123456789 "), "past 2^53 a double would round");
        }

        [Test]
        public void IntsPickPluralBranchesAndBoolsAndNonePickStrings()
        {
            string Count(long n) => Format(T("harbor-count", new Dictionary<string, Value> { ["n"] = new IntValue(n) }));
            Assert.That((Count(1), Count(3)), Is.EqualTo(("one ship", "3 ships")));
            string flag = Format(T("harbor-flag", new Dictionary<string, Value> { ["b"] = BoolValue.True, ["z"] = NoneValue.Instance }));
            Assert.That(flag, Is.EqualTo("yes none"));
        }

        [Test]
        public void EntitiesUseTheRecordedDefHiddenValuesAndNestedTexts()
        {
            var said = T("harbor-said", new Dictionary<string, Value>
            {
                ["who"] = new EntityValue(new EntityId(99)), // 없어진 엔티티: 적어 둔 Def로
                ["what"] = T("harbor-bought", new Dictionary<string, Value>
                    { ["ship"] = HiddenValue.Instance, ["n"] = new IntValue(2), ["f"] = new IntValue(0) }),
            }, new Dictionary<string, string> { ["who"] = "harbor:Brig" });
            Assert.That(Format(said), Is.EqualTo("brig said: bought unknown for 2 gold (0)"));
        }

        [Test]
        public void LabelTreatsStringsAsKeysOrDefsAndValueShowsThemAsWritten()
        {
            var sloop = _world.Create("harbor:Sloop");
            Assert.That(Label(new StrValue("harbor-accept")), Is.EqualTo("Accept"));
            Assert.That(Label(new StrValue("harbor:Brig")), Is.EqualTo("brig"));
            Assert.That(Label(new EntityValue(sloop), _world), Is.EqualTo("sloop"));
            Assert.That(Label(HiddenValue.Instance), Is.EqualTo("unknown"));
            Assert.That(Shown(new StrValue("harbor-accept")), Is.EqualTo("harbor-accept"), "a screen value is shown as written");
            Assert.That(Shown(new StrValue("harbor:Brig")), Is.EqualTo("brig"));
            Assert.That(Shown(new IntValue(-3)), Is.EqualTo("-3"));
            Assert.That(Shown(new EntityValue(sloop)), Is.EqualTo("unknown"), "without a world the entity cannot be named");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void AttrReadsTheFluentAttributeOfKeysDefsEntitiesTextsAndOptions()
        {
            var sloop = _world.Create("harbor:Sloop");
            Assert.That(Attr(new StrValue("harbor-accept"), "desc"), Is.EqualTo("take the deal"));
            Assert.That(Attr(new StrValue("harbor:Sloop"), "desc"), Is.EqualTo("a small boat"));
            Assert.That(Attr(new EntityValue(sloop), "desc", _world), Is.EqualTo("a small boat"));
            var offer = T("harbor-offer", new Dictionary<string, Value> { ["who"] = new StrValue("harbor:Brig") });
            Assert.That(Attr(offer, "desc"), Is.EqualTo("brig offers a deal"));

            var plain = new OptionValue(new StrValue("harbor-accept"), null, null, null, null);
            var dressed = new OptionValue(new StrValue("x"), new StrValue("harbor:Brig"), offer, null, null);
            Assert.That((Label(plain), Attr(plain, "desc")), Is.EqualTo(("Accept", "take the deal")));
            Assert.That((Label(dressed), Attr(dressed, "desc")), Is.EqualTo(("brig", "Offer from brig")));
        }

        [Test]
        public void MissingKeysAndArgumentsFallBackAndReportButNeverThrow()
        {
            Assert.That(Label(new StrValue("harbor-nope")), Is.EqualTo("harbor-nope"));
            Assert.That(Attr(new StrValue("harbor:Brig"), "desc"), Is.EqualTo("def-harbor-Brig.desc"));
            string s = Format(T("harbor-count", new Dictionary<string, Value>()));
            Assert.That(s, Does.Contain("ships"));
            Assert.That(_errors, Has.Count.EqualTo(3));
            Assert.That(_errors[0], Is.EqualTo("no translation for 'harbor-nope'"));
        }

        // Def 이름의 번역이 없으면 키(def-…)가 아니라 Def id (G275)
        [Test]
        public void AnUntranslatedDefShowsItsId()
        {
            var raft = _world.Create("harbor:Raft");
            Assert.That(Label(new StrValue("harbor:Raft")), Is.EqualTo("harbor:Raft"));
            Assert.That(Label(new EntityValue(raft), _world), Is.EqualTo("harbor:Raft"));
            string said = Format(T("harbor-said", new Dictionary<string, Value>
                { ["who"] = new StrValue("harbor:Raft"), ["what"] = new StrValue("hi") }));
            Assert.That(said, Is.EqualTo("harbor:Raft said: hi"));
            Assert.That(_errors.Distinct(), Is.EqualTo(new[] { "no translation for 'def-harbor-Raft'" }), "still reported");
            Assert.That(Attr(new StrValue("harbor:Raft"), "desc"), Is.EqualTo("def-harbor-Raft.desc"), "attributes keep the key");
        }

        [Test]
        public void TheHostCanFormatItsOwnKeys()
        {
            Assert.That(_tr.Text("harbor-count", ("n", new BridgeInt(1))), Is.EqualTo("one ship"));
            Assert.That(Translator.DefKey("harbor:rigs/Brig"), Is.EqualTo("def-harbor-rigs-Brig"));
        }
    }
}
