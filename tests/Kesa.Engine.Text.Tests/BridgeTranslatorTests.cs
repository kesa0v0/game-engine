using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary>
    /// 호스트 경계 값의 번역 (spec-gaps G269·G270·G326): 번역기는 월드 없이 경계 값만 받는다(엔티티는 제 Def를 듦).
    /// 번역 파일은 운영이 준 모드 목록으로 읽는다.
    /// </summary>
    public class BridgeTranslatorTests : TextTestBase
    {
        private const string Schema =
            "component(\"Hull\", hp=field(int, default=1))\n" +
            "deftype(\"Ship\", components=[\"Hull\"])\n";

        private const string Defs =
            "Ship(\"Sloop\")\nShip(\"Brig\")\n" +
            "def flagship():\n    for s in query(Ship):\n        return s\n" +
            "def bought(s):\n    return text(\"harbor-bought\", ship=s, n=5, f=1.5)\n";

        private const string Texts =
            "def-harbor-Sloop = sloop\n" +
            "    .desc = a small boat\n" +
            "def-harbor-Brig = brig\n" +
            "harbor-bought = bought { $ship } for { $n } gold ({ $f })\n" +
            "harbor-accept = Accept\n" +
            "    .desc = take the deal\n";

        private List<string> _errors = null!;
        private Translator _tr = null!;
        private LocalHost _host = null!;

        [SetUp]
        public async Task NewTranslator()
        {
            Mod("harbor", schema: Schema, defs: Defs, extra: ", defaultLanguage=\"en\"");
            Ftl("harbor", "en", Texts);
            var content = LoadContent();
            var world = new World(content);
            world.Create("harbor:Sloop");
            _host = new LocalHost(content, world);
            var bag = new DiagnosticBag();
            var texts = Translations.Load(await _host.Content(), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _errors = new List<string>();
            _tr = texts.For(new[] { "en" }, _errors.Add);
        }

        [Test]
        public async Task TheContentListsModsInLoadOrderAndDefs()
        {
            var content = await _host.Content();
            Assert.That(content.Mods[0].Id, Is.EqualTo("harbor"));
            Assert.That(content.Mods[0].DefaultLanguage, Is.EqualTo("en"));
            Assert.That(content.Defs, Is.EqualTo(new[] { "harbor:Brig", "harbor:Sloop" }));
        }

        [Test]
        public void EntitiesShowTheDefTheyCarry()
        {
            Assert.That(_tr.Value(new BridgeEntity(7, "harbor:Brig")), Is.EqualTo("brig"));
            Assert.That(_tr.Attr(new BridgeEntity(7, "harbor:Sloop"), "desc"), Is.EqualTo("a small boat"));
            Assert.That(_tr.Value(new BridgeEntity(7, null)), Is.EqualTo(_tr.Value(BridgeHidden.Instance)), "an unseen entity reads as hidden");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void StringsDefIdsAndOptions()
        {
            Assert.That(_tr.Value(new BridgeStr("harbor:Sloop")), Is.EqualTo("sloop"));
            Assert.That(_tr.Value(new BridgeStr("plain")), Is.EqualTo("plain"));
            Assert.That(_tr.Label(new BridgeStr("harbor-accept")), Is.EqualTo("Accept"));
            var option = new BridgeOption(new BridgeInt(1), label: new BridgeStr("harbor-accept"));
            Assert.That(_tr.Label(option), Is.EqualTo("Accept"));
            Assert.That(_tr.Attr(option, "desc"), Is.EqualTo(""), "the option's value has no .desc");
            Assert.That(_tr.Attr(new BridgeStr("harbor-accept"), "desc"), Is.EqualTo("take the deal"));
            Assert.That(_tr.Value(new BridgeFixed(2_500_000)), Is.EqualTo("2.5"));
            Assert.That(_tr.Value(BridgeBool.True), Is.EqualTo("true"));
        }

        [Test]
        public async Task AScreenValueFromTheSessionTranslatesWithoutTheWorld()
        {
            var session = await _host.Open("alice");
            var called = await session.Call("ship", "harbor.flagship", new BridgeValue[0]);
            Assert.That(called.Succeeded, Is.True, called.Error);
            var ship = called.Value!;
            Assert.That(_tr.Value(ship), Is.EqualTo("sloop"));
            var boughtCall = await session.Call("bought", "harbor.bought", new[] { ship });
            Assert.That(boughtCall.Succeeded, Is.True, boughtCall.Error);
            var text = boughtCall.Value!;
            Assert.That(_tr.Value(text), Is.EqualTo("bought sloop for 5 gold (1.5)"));
            Assert.That(_tr.Format(text.AsText), Is.EqualTo("bought sloop for 5 gold (1.5)"));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void HostKeysTakeBridgeArguments()
        {
            Assert.That(_tr.Text("harbor-bought", ("ship", new BridgeEntity(1, "harbor:Brig")), ("n", new BridgeInt(2)),
                ("f", new BridgeFixed(-250_000))), Is.EqualTo("bought brig for 2 gold (-0.25)"));
        }

        [Test]
        public void ContainersCannotBeShownAsOneValue()
        {
            Assert.That(_tr.Value(new BridgeList(new BridgeValue[0])), Is.EqualTo(""));
            Assert.That(_errors, Is.EqualTo(new[] { "cannot show list" }));
        }
    }
}
