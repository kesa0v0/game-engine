using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>효과 띄우기 (G425·G426): 그림 정의의 시간 칸대로 흐려지고 떠오르다 수명이 끝나면 지워진다. 그림 값이 시간 칸을 덮는다.</summary>
    public class LookFxTests
    {
        private const string Board = "<Spaces><Space name=\"square\" kind=\"grid\" /></Spaces>";

        private const string Number =
            "<Looks>\n" +
            "  <Look name=\"number\" fade-in=\"0.1\" solid=\"0.4\" fade-out=\"0.5\" rise=\"2\" bind-solid=\"hold\">\n" +
            "    <Sprite src=\"art/digit.png\" />\n" +
            "  </Look>\n" +
            "  <Look name=\"dot\"><Sprite src=\"art/dot.png\" /></Look>\n" +
            "</Looks>";

        private MemoryLookRenderer _renderer = null!;

        private LookStage Stage(string looks)
        {
            var library = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml", looks), new LookSource("core", "core/board.space.xml", Board),
            }, LookKinds.Standard());
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            _renderer = new MemoryLookRenderer();
            return new LookStage(library, _renderer);
        }

        private static BridgeFx Fx(string look, params (string key, BridgeValue value)[] keys) =>
            new BridgeFx("core.hit", new BridgeLook(look, keys.Select(k => new KeyValuePair<string, BridgeValue>(k.key, k.value)).ToList()),
                BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(1), new BridgeInt(2) })), null);

        private static BridgeWorldFx Batch(params BridgeFx[] effects) => new BridgeWorldFx(2, false, new[] { new BridgeFxBatch(1, effects) });

        [Test]
        public void AnEffectFadesInHoldsRisesFadesOutAndIsGone()
        {
            var stage = Stage(Number);
            stage.Play(Batch(Fx("number")));
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 2.0, 0.0) a0.0 Sprite[src=art/digit.png]"));
            stage.Advance(50);
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 2.1, 0.0) a0.5 Sprite[src=art/digit.png]"), "half faded in, risen 2 × 0.05");
            stage.Advance(250);
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 2.6, 0.0) Sprite[src=art/digit.png]"));
            stage.Advance(450);
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 3.5, 0.0) a0.5 Sprite[src=art/digit.png]"), "half faded out");
            stage.Advance(250);
            Assert.That((_renderer.Objects.Count, stage.PlayingCount), Is.EqualTo((0, 0)), "gone after 1 second");
        }

        [Test]
        public void TheLookValueOverridesATimeAndNoneKeepsTheWrittenOne()
        {
            var stage = Stage(Number);
            stage.Play(Batch(Fx("number", ("hold", new BridgeFixed(1_400_000))), Fx("number", ("hold", BridgeNone.Instance))));
            stage.Advance(1100);
            Assert.That(_renderer.Objects.Keys, Is.EqualTo(new[] { "fx:1" }), "held 1.4 s instead of 0.4 s; the other ended at 1 s");
            stage.Advance(900);
            Assert.That(_renderer.Objects, Is.Empty);
        }

        [Test]
        public void ALookWithoutTimesEndsAtOnceAndAFailedFunctionIsReported()
        {
            var stage = Stage(Number);
            stage.Play(Batch(Fx("dot"), new BridgeFx("core.broken", null, null, "'core.broken' failed: division by zero")));
            Assert.That(stage.Problems.Select(p => p.ToString()), Is.EquivalentTo(new[]
            {
                "core/fx.look.xml:5: look 'dot' is played as an effect but has no fade-in, solid or fade-out, so it ends at once (spec-gaps G425)",
                "core.broken: 'core.broken' failed: division by zero",
            }));
            stage.Advance(1);
            Assert.That(_renderer.Objects, Is.Empty);
        }

        [Test]
        public void TextShowsAnyValueThroughTheHostFormatter()
        {
            var library = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml",
                    "<Looks><Look name=\"n\" solid=\"1\"><Text bind-text=\"amount\" size=\"0.5\" /><Text text=\"plain\" /></Look></Looks>"),
                new LookSource("core", "core/board.space.xml", Board),
            }, LookKinds.Standard());
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            _renderer = new MemoryLookRenderer();
            var stage = new LookStage(library, _renderer, format: v => v is BridgeText t ? "[" + t.Key + "]" : LookAttributesPlain(v));
            stage.Play(Batch(Fx("n", ("amount", new BridgeInt(-4))), Fx("n", ("amount", new BridgeText("core-miss", new KeyValuePair<string, BridgeValue>[0]))), Fx("n")));
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 2.0, 0.0) Text[size=0.5,text=-4] Text[text=plain]"));
            Assert.That(_renderer.Dump("fx:2"), Is.EqualTo("@(1.0, 2.0, 0.0) Text[size=0.5,text=[core-miss]] Text[text=plain]"));
            Assert.That(_renderer.Dump("fx:3"), Is.EqualTo("@(1.0, 2.0, 0.0) Text[size=0.5,text=] Text[text=plain]"), "None is no text");
            Assert.That(stage.Problems, Is.Empty);
        }

        private static string LookAttributesPlain(BridgeValue v) => v is BridgeInt i ? i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";

        [Test]
        public void WrongTimesAreLoadProblems()
        {
            var problems = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml",
                    "<Looks><Look name=\"a\" solid=\"-1\" fade=\"1\" fade-out=\"x\"><Sprite src=\"s.png\" /></Look></Looks>"),
            }, LookKinds.Standard()).Problems.Select(p => p.ToString()).ToList();
            Assert.That(problems, Has.Some.Contain("solid=\"-1\": a time is not negative"));
            Assert.That(problems, Has.Some.Contain("<Look> takes name and the effect times fade-in, solid, fade-out, rise"));
            Assert.That(problems, Has.Some.Contain("fade-out=\"x\""));
        }
    }
}
