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

        private LookStage Stage(string looks, string board = Board)
        {
            var library = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml", looks), new LookSource("core", "core/board.space.xml", board),
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
        public void DriftMovesAndGrowScalesAndTheLookValueCanAimIt()
        {
            var stage = Stage("<Looks><Look name=\"spark\" solid=\"1\" drift=\"0.5,-1\" bind-drift=\"dir\" grow=\"-2\">" +
                              "<Sprite src=\"art/dot.png\" /></Look></Looks>");
            stage.Play(Batch(Fx("spark"), Fx("spark", ("dir", new BridgeList(new BridgeValue[] { new BridgeInt(-1), new BridgeInt(0) })))));
            stage.Advance(250);
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.125, 1.75, 0.0) x0.5 Sprite[src=art/dot.png]"), "moved 0.25 s × (0.5, -1), size 1 - 2 × 0.25");
            Assert.That(_renderer.Dump("fx:2"), Is.EqualTo("@(0.75, 2.0, 0.0) x0.5 Sprite[src=art/dot.png]"), "the look value aims it left");
            stage.Advance(500);
            Assert.That(_renderer.Dump("fx:1"), Does.Contain(" x0.0 "), "the size stops at 0");
            Assert.That(stage.Problems, Is.Empty);
        }

        /// <summary>drift는 틀의 동·북·위, rise는 공간이 정한 떠오름 쪽 (G447): xy 판은 북(화면 위), xz 바닥은 높이.</summary>
        [Test]
        public void DriftFollowsTheFrameAndRiseGoesWhereTheSpaceSaysIsUp()
        {
            const string looks = "<Looks><Look name=\"up\" solid=\"1\" rise=\"2\"><Sprite src=\"art/dot.png\" /></Look>" +
                                 "<Look name=\"spark\" solid=\"1\" drift=\"1,2,4\"><Sprite src=\"art/dot.png\" /></Look></Looks>";
            var stage = Stage(looks);
            stage.Play(Batch(Fx("up"), Fx("spark")));
            stage.Advance(500);
            Assert.That(_renderer.Dump("fx:1"), Does.StartWith("@(1.0, 3.0, 0.0)"), "xy: rise goes north, up the screen");
            Assert.That(_renderer.Dump("fx:2"), Does.StartWith("@(1.5, 3.0, -2.0)"), "xy: east, north, and up toward the viewer");

            stage = Stage(looks, "<Spaces><Space name=\"square\" kind=\"grid\" plane=\"xz\" /></Spaces>");
            stage.Play(Batch(Fx("up"), Fx("spark")));
            stage.Advance(500);
            Assert.That(_renderer.Dump("fx:1"), Does.StartWith("@(1.0, 1.0, 2.0)"), "xz: rise goes up into the air");
            Assert.That(_renderer.Dump("fx:2"), Does.StartWith("@(1.5, 2.0, 3.0)"), "xz: east, up, north");
        }

        [Test]
        public void RiseAndDriftTogetherIsALoadProblem()
        {
            var problems = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml",
                    "<Looks><Look name=\"a\" rise=\"1\" bind-drift=\"d\" grow=\"x\"><Sprite src=\"s.png\" /></Look></Looks>"),
            }, LookKinds.Standard()).Problems.Select(p => p.ToString()).ToList();
            Assert.That(problems, Has.Some.Contain("has both rise and drift"));
            Assert.That(problems, Has.Some.Contain("grow=\"x\""));
        }

        /// <summary>퇴장 (G431): 사라진 엔티티의 그림 값을 받아 그 자리에 exit 그림을 효과로 띄운다. 그림이 바뀌는 것은 사라짐이 아니다.</summary>
        [Test]
        public void AVanishedObjectPlaysItsExitLookWhereItWas()
        {
            var stage = Stage("<Looks>" +
                              "<Look name=\"soldier\" exit=\"down\"><Sprite bind-src=\"art\" /></Look>" +
                              "<Look name=\"down\" fade-out=\"0.5\" rise=\"-1\"><Sprite bind-src=\"art\" /></Look>" +
                              "<Look name=\"flag\"><Sprite src=\"art/flag.png\" /></Look>" +
                              "</Looks>");
            var here = BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(1), new BridgeInt(2) }));
            BridgeLook Soldier() => new BridgeLook("soldier", new[] { new KeyValuePair<string, BridgeValue>("art", new BridgeStr("art/red.png")) });
            stage.Apply(new BridgeWorldObjects(1, new[]
            {
                new BridgeWorldObject(new BridgeEntity(7, null), Soldier(), here, null),
                new BridgeWorldObject(new BridgeEntity(8, null), Soldier(), here, null),
            }, new long[0]));
            stage.Apply(new BridgeWorldObjects(2, new[]
            {
                new BridgeWorldObject(new BridgeEntity(8, null), new BridgeLook("flag", new KeyValuePair<string, BridgeValue>[0]), here, null),
            }, new long[] { 7 }));
            Assert.That(stage.Keys, Is.EquivalentTo(new[] { "entity:8", "fx:1" }), "7 left; 8 only changed its look");
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 2.0, 0.0) Sprite[src=art/red.png]"));
            stage.Advance(250);
            Assert.That(_renderer.Dump("fx:1"), Is.EqualTo("@(1.0, 1.75, 0.0) a0.5 Sprite[src=art/red.png]"), "fading and sinking");
            stage.Advance(250);
            Assert.That(stage.Keys, Is.EqualTo(new[] { "entity:8" }));
            Assert.That(stage.Problems, Is.Empty);
        }

        private static BridgeWorldObjects Objects(long stamp, long x, BridgeValue? slide = null, long[]? gone = null)
        {
            var keys = slide == null ? new KeyValuePair<string, BridgeValue>[0] : new[] { new KeyValuePair<string, BridgeValue>("slide", slide) };
            var changed = gone != null ? new BridgeWorldObject[0] : new[]
            {
                new BridgeWorldObject(new BridgeEntity(7, null), new BridgeLook("soldier", keys),
                    BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(x), new BridgeInt(0) })), null),
            };
            return new BridgeWorldObjects(stamp, changed, gone ?? new long[0]);
        }

        private const string Walker = "<Looks>" +
                                      "<Look name=\"soldier\" move=\"0.5\" bind-move=\"slide\" exit=\"down\"><Sprite src=\"s.png\" /></Look>" +
                                      "<Look name=\"down\" fade-out=\"1\"><Sprite src=\"s.png\" /></Look>" +
                                      "</Looks>";

        /// <summary>옮기기 (G432): 처음은 바로 놓고, 자리가 바뀌면 보이던 자리에서 0.5초 동안 직선으로, 중간에 바뀌면 거기서 다시.</summary>
        [Test]
        public void AMovedObjectSlidesFromWhereItIsShown()
        {
            var stage = Stage(Walker);
            stage.Apply(Objects(1, 0));
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(0.0, 0.0, 0.0) "), "appears at once");
            stage.Apply(Objects(2, 4));
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(0.0, 0.0, 0.0) "));
            stage.Advance(250);
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(2.0, 0.0, 0.0) "), "halfway, whatever the distance");
            stage.Apply(Objects(3, 0));
            stage.Advance(250);
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(1.0, 0.0, 0.0) "), "turned back from where it was shown");
            stage.Advance(250);
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(0.0, 0.0, 0.0) "));
            stage.Apply(Objects(4, 3, slide: new BridgeInt(0)));
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(3.0, 0.0, 0.0) "), "move 0 from the look value jumps");
            Assert.That(stage.Problems, Is.Empty);
        }

        /// <summary>순간이동으로 끝난 재생이 다음 옮기기에 되살아나면 물체 하나를 두 재생이 밀어 두 배로 빨라진다.</summary>
        [Test]
        public void AJumpThenAMoveInTheSameFramePlaysOnce()
        {
            var stage = Stage(Walker);
            stage.Apply(Objects(1, 0));
            stage.Apply(Objects(2, 4));
            stage.Advance(125);
            stage.Apply(Objects(3, 0, slide: new BridgeInt(0)));
            stage.Apply(Objects(4, 4));
            Assert.That(stage.PlayingCount, Is.EqualTo(1));
            stage.Advance(250);
            Assert.That(_renderer.Dump("entity:7"), Does.StartWith("@(2.0, 0.0, 0.0) "), "halfway, not arrived");
        }

        [Test]
        public void AnObjectThatLeavesWhileMovingExitsWhereItIsShown()
        {
            var stage = Stage(Walker);
            stage.Apply(Objects(1, 0));
            stage.Apply(Objects(2, 2));
            stage.Advance(125);
            stage.Apply(Objects(3, 0, gone: new long[] { 7 }));
            Assert.That(stage.Keys, Is.EqualTo(new[] { "fx:1" }));
            Assert.That(_renderer.Dump("fx:1"), Does.StartWith("@(0.5, 0.0, 0.0) "));
        }

        [Test]
        public void AnExitNamesAWrittenLook()
        {
            var problems = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml",
                    "<Looks><Look name=\"a\" exit=\"dwon\"><Sprite src=\"s.png\" /></Look><Look name=\"down\" bind-exit=\"x\"><Sprite src=\"s.png\" /></Look></Looks>"),
            }, LookKinds.Standard()).Problems.Select(p => p.ToString()).ToList();
            Assert.That(problems, Has.Some.Contain("exit=\"dwon\": no look is named 'dwon'"));
            Assert.That(problems, Has.Some.Contain("exit is written, not bound"));
        }

        [Test]
        public void WrongTimesAreLoadProblems()
        {
            var problems = LookLibrary.Load(new[]
            {
                new LookSource("core", "core/fx.look.xml",
                    "<Looks><Look name=\"a\" solid=\"-1\" fade=\"1\" fade-out=\"x\"><Sprite src=\"s.png\" /></Look></Looks>"),
            }, LookKinds.Standard()).Problems.Select(p => p.ToString()).ToList();
            Assert.That(problems, Has.Some.Contain("solid=\"-1\": a time is not negative"));
            Assert.That(problems, Has.Some.Contain("<Look> takes name, exit and the effect times fade-in, solid, fade-out, rise"));
            Assert.That(problems, Has.Some.Contain("fade-out=\"x\""));
        }
    }
}
