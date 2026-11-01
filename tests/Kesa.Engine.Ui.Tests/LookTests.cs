using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>그림 정의·공간 선언 읽기와 판 위 그림 펼치기 (G409~G414).</summary>
    public class LookTests
    {
        private const string Board = "<Spaces>\n  <Space name=\"square\" kind=\"grid\" />\n</Spaces>\n";

        private MemoryLookRenderer _renderer = null!;

        private static LookLibrary Load(params (string mod, string path, string text)[] files) =>
            LookLibrary.Load(files.Select(f => new LookSource(f.mod, f.path, f.text)), LookKinds.Standard());

        private static LookLibrary Looks(string looks, string spaces = Board)
        {
            var library = Load(("core", "looks/units.look.xml", looks), ("core", "looks/board.space.xml", spaces));
            Assert.That(library.Problems, Is.Empty, string.Join("\n", library.Problems));
            return library;
        }

        private static string[] Problems(string looks, string spaces = Board) =>
            Load(("core", "looks/units.look.xml", looks), ("core", "looks/board.space.xml", spaces)).Problems.Select(p => p.ToString()).ToArray();

        private LookStage Stage(LookLibrary library)
        {
            _renderer = new MemoryLookRenderer();
            return new LookStage(library, _renderer);
        }

        private static BridgeList Cell(params long[] xyz) => new BridgeList(xyz.Select(v => (BridgeValue)new BridgeInt(v)).ToList());

        private static BridgeWhere At(params long[] xyz) => BridgeWhere.At("square", Cell(xyz));

        private static BridgeLook Look(string name, params (string key, BridgeValue value)[] keys) =>
            new BridgeLook(name, keys.Select(k => new KeyValuePair<string, BridgeValue>(k.key, k.value)).ToList());

        private static BridgeStr Str(string s) => new BridgeStr(s);

        // ---- 읽기 ----

        [Test]
        public void ALookIsDrawnFromItsDefinitionAtTheGridPlace()
        {
            var stage = Stage(Looks("<Looks>\n  <Look name=\"retreat-spot\">\n    <Sprite src=\"art/retreat.png\" tint=\"#ffffff80\" layer=\"2\" />\n  </Look>\n</Looks>"));
            stage.Show("m", Look("retreat-spot"), At(2, 3));
            Assert.That(_renderer.Dump("m"), Is.EqualTo("@(2.0, 3.0, 0.0) Sprite[layer=2,src=art/retreat.png,tint=#ffffff80]"));
        }

        [Test]
        public void GridSettingsMoveScaleAndTurnThePlace()
        {
            const string look = "<Looks><Look name=\"dot\"><Sprite src=\"a.png\" /></Look></Looks>";
            var cases = new (string space, long[] cell, string at)[]
            {
                ("cell=\"2\" origin=\"1,0.5\"", new long[] { 2, 3 }, "@(5.0, 6.5, 0.0)"),
                ("flip-y=\"true\"", new long[] { 2, 3 }, "@(2.0, -3.0, 0.0)"),
                ("plane=\"xz\"", new long[] { 2, 3 }, "@(2.0, 0.0, 3.0)"),
                ("plane=\"xz\" cell=\"0.5\"", new long[] { 2, 3, 4 }, "@(1.0, 2.0, 1.5)"),
            };
            foreach (var c in cases)
            {
                var stage = Stage(Looks(look, "<Spaces><Space name=\"square\" kind=\"grid\" " + c.space + " /></Spaces>"));
                stage.Show("m", Look("dot"), At(c.cell));
                Assert.That(_renderer.Dump("m"), Does.StartWith(c.at + " "), c.space);
            }
        }

        [Test]
        public void SizesAndOffsetsStayInCellsBecauseTheObjectIsScaledByTheCell()
        {
            var stage = Stage(Looks("<Looks><Look name=\"dot\"><Sprite src=\"a.png\" offset=\"0,0.5\" /></Look></Looks>",
                "<Spaces><Space name=\"square\" kind=\"grid\" cell=\"2\" /></Spaces>"));
            stage.Show("m", Look("dot"), At(2, 3));
            Assert.That(_renderer.Dump("m"), Is.EqualTo("@(4.0, 6.0, 0.0) x2.0 Sprite[offset=0.0,0.5,src=a.png]"));
        }

        [Test]
        public void BadDefinitionsAreFoundWhenLoading()
        {
            Assert.That(Problems("<Looks><Look name=\"a\"><Sprit src=\"a.png\" /></Look></Looks>"),
                Has.Some.Contains("<Sprit>: no look element is registered with this name (did you mean 'Sprite'?)"));
            Assert.That(Problems("<Looks><Look name=\"a\"><Sprite sze=\"1,1\" /></Look></Looks>"),
                Has.Some.Contains("sze: <Sprite> has no attribute 'sze' (did you mean 'size'?)"));
            Assert.That(Problems("<Looks><Look name=\"a\"><Sprite tint=\"red\" /></Look></Looks>"),
                Has.Some.Contains("'red' is not a color"));
            Assert.That(Problems("<Looks><Look name=\"a\"><Sprite bind-src=\"core.art(team)\" /></Look></Looks>"),
                Has.Some.Contains("it cannot call mod functions"));
            Assert.That(Problems("<Looks><Look name=\"a\"><Sprite src=\"a.png\"><Sprite src=\"b.png\" /></Sprite></Look></Looks>"),
                Has.Some.Contains("<Sprite> has no children"));
            Assert.That(Problems("<Looks><Look name=\"a\"><Sprite src=\"../a.png\" /></Look></Looks>"),
                Has.Some.Contains("climbs out with '..'"));
            Assert.That(Problems("<Looks><Space name=\"s\" kind=\"grid\" /></Looks>"),
                Has.Some.Contains("(spaces go in a .space.xml file)"));
            Assert.That(Problems("<Looks />", "<Spaces><Space name=\"s\" kind=\"grod\" /></Spaces>"),
                Has.Some.Contains("kind=\"grod\": no space kind is registered with this name (did you mean 'grid'?)"));
            Assert.That(Problems("<Looks />", "<Spaces><Space name=\"s\" kind=\"grid\" cel=\"2\" /></Spaces>"),
                Has.Some.Contains("cel: the space kind 'grid' has no such setting (did you mean 'cell'?)"));
            Assert.That(Problems("<Looks />", "<Spaces><Space name=\"s\" kind=\"grid\" plane=\"yz\" /></Spaces>"),
                Has.Some.Contains("plane=\"yz\": a plane is xy or xz"));
            Assert.That(Problems("<Looks><Look name=\"a\"><Include src=\"x\" /></Look></Looks>"),
                Has.Some.Contains("Include in a look file is not supported yet"));
        }

        [Test]
        public void ALaterModWinsTheWholeDefinitionButOneModCannotDefineANameTwice()
        {
            var library = Load(
                ("core", "looks/a.look.xml", "<Looks><Look name=\"dot\"><Sprite src=\"core.png\" /><Sprite src=\"core2.png\" /></Look></Looks>"),
                ("extra", "looks/a.look.xml", "<Looks><Look name=\"dot\"><Sprite src=\"extra.png\" /></Look></Looks>"),
                ("core", "looks/board.space.xml", Board));
            Assert.That(library.Problems, Is.Empty);
            var stage = Stage(library);
            stage.Show("m", Look("dot"), At(0, 0));
            Assert.That(_renderer.Dump("m"), Is.EqualTo("@(0.0, 0.0, 0.0) Sprite[src=extra.png]"));

            var twice = Load(("core", "looks/a.look.xml", "<Looks><Look name=\"dot\"><Sprite /></Look></Looks>"),
                ("core", "looks/b.look.xml", "<Looks><Look name=\"dot\"><Sprite /></Look></Looks>"));
            Assert.That(twice.Problems.Select(p => p.ToString()),
                Has.Some.EqualTo("looks/b.look.xml:1: the look 'dot' is already defined at looks/a.look.xml:1"));
        }

        [Test]
        public void AWrittenPathThatIsNotInTheModsIsFoundWhenLoading()
        {
            var library = LookLibrary.Load(new[] { new LookSource("core", "looks/a.look.xml", "<Looks><Look name=\"a\"><Sprite src=\"art/gone.png\" /></Look></Looks>") },
                LookKinds.Standard(), path => path == "art/here.png");
            Assert.That(library.Problems.Select(p => p.ToString()),
                Is.EqualTo(new[] { "looks/a.look.xml:1: src=\"art/gone.png\": no such file in the mods" }));
        }

        [Test]
        public void AKeyTheLookValueLeavesOutReadsAsNoneSoOnlyTheStatesThatAreOnArePassed()
        {
            var stage = Stage(Looks("<Looks><Look name=\"soldier\"><Sprite src=\"a.png\" /><Sprite src=\"hurt.png\" bind-display=\"hurt\" /></Look></Looks>"));
            stage.Show("u", Look("soldier"), At(0, 0));
            Assert.That(_renderer.Dump("u"), Is.EqualTo("@(0.0, 0.0, 0.0) Sprite[src=a.png] -Sprite[src=hurt.png]"));
            stage.Show("u", Look("soldier", ("hurt", BridgeBool.True)), At(0, 0));
            Assert.That(_renderer.Dump("u"), Is.EqualTo("@(0.0, 0.0, 0.0) Sprite[src=a.png] Sprite[src=hurt.png]"));
            Assert.That(stage.Problems, Is.Empty);
        }

        // ---- 펼치기 ----

        [Test]
        public void KeysOfTheLookValueFillAttributesAndChooseWhatShows()
        {
            var stage = Stage(Looks(
                "<Looks><Look name=\"soldier\">\n" +
                "  <Sprite bind-src=\"art\" bind-tint=\"team\" />\n" +
                "  <Sprite src=\"hurt.png\" bind-display=\"hurt\" layer=\"1\" />\n" +
                "</Look></Looks>"));
            stage.Show("u", Look("soldier", ("art", Str("red.png")), ("team", Str("#cc3333")), ("hurt", BridgeBool.False)), At(1, 1));
            Assert.That(_renderer.Dump("u"), Is.EqualTo("@(1.0, 1.0, 0.0) Sprite[src=red.png,tint=#cc3333ff] -Sprite[layer=1,src=hurt.png]"));

            int before = _renderer.Sets;
            stage.Show("u", Look("soldier", ("art", Str("red.png")), ("team", Str("#cc3333")), ("hurt", BridgeBool.True)), At(1, 2));
            Assert.That(_renderer.Dump("u"), Is.EqualTo("@(1.0, 2.0, 0.0) Sprite[src=red.png,tint=#cc3333ff] Sprite[layer=1,src=hurt.png]"));
            Assert.That(_renderer.Sets, Is.EqualTo(before), "unchanged attributes are not set again");
        }

        [Test]
        public void ForRepeatsChildrenAndKeepsThemByItem()
        {
            var stage = Stage(Looks(
                "<Looks><Look name=\"stack\">\n" +
                "  <Group for=\"flag in flags\"><Sprite bind-src=\"flag\" /></Group>\n" +
                "</Look></Looks>"));
            BridgeLook Stack(params string[] flags) => Look("stack", ("flags", new BridgeList(flags.Select(f => (BridgeValue)Str(f)).ToList())));

            stage.Show("s", Stack("a.png", "b.png"), At(0, 0));
            Assert.That(_renderer.Dump("s"), Is.EqualTo("@(0.0, 0.0, 0.0) Group{Sprite[src=a.png] Sprite[src=b.png]}"));
            long kept = _renderer.Nodes.Single(n => n.Value.Attributes.TryGetValue("src", out var s) && s == "b.png").Key;

            stage.Show("s", Stack("b.png", "c.png"), At(0, 0));
            Assert.That(_renderer.Dump("s"), Is.EqualTo("@(0.0, 0.0, 0.0) Group{Sprite[src=b.png] Sprite[src=c.png]}"));
            Assert.That(_renderer.Nodes[kept].Attributes["src"], Is.EqualTo("b.png"), "the b item kept its element");
        }

        [Test]
        public void AnotherLookNameRebuildsAndHideRemoves()
        {
            var stage = Stage(Looks("<Looks><Look name=\"a\"><Sprite src=\"a.png\" /></Look><Look name=\"b\"><Group /></Look></Looks>"));
            stage.Show("m", Look("a"), At(0, 0));
            stage.Show("m", Look("b"), At(0, 0));
            Assert.That(_renderer.Dump("m"), Is.EqualTo("@(0.0, 0.0, 0.0) Group"));
            stage.Hide("m");
            Assert.That(_renderer.Objects, Is.Empty);
            Assert.That(_renderer.Nodes, Is.Empty);
        }

        [Test]
        public void ProblemsWhileDrawingAreReportedOnceAndDoNotStopTheBoard()
        {
            var stage = Stage(Looks("<Looks><Look name=\"dot\"><Sprite bind-src=\"art\" /></Look></Looks>"));
            stage.Show("missing", Look("dor"), At(0, 0));
            Assert.That(_renderer.Dump("missing"), Is.EqualTo("@(0.0, 0.0, 0.0) MISSING"));

            stage.Show("nokey", Look("dot"), At(0, 0));
            stage.Show("nokey", Look("dot", ("other", Str("x"))), At(0, 0));
            stage.Show("space", Look("dot", ("art", Str("a.png"))), BridgeWhere.At("sqare", Cell(0, 0)));
            stage.Show("bad", Look("dot", ("art", Str("a.png"))), BridgeWhere.At("square", Str("e4")));
            stage.Show("high", Look("dot", ("art", Str("a.png"))), At(0, 0, 1));
            stage.Show("onto", Look("dot", ("art", Str("a.png"))), BridgeWhere.Onto(new BridgeInt(1), null));
            Assert.That(stage.Problems.Select(p => p.ToString()), Is.EqualTo(new[]
            {
                "missing: no look is named 'dor' (did you mean 'dot'?)",
                "looks/units.look.xml:1: look 'dot', bind-src=\"art\": a path is text, got a none",
                "space: no space is named 'sqare' (did you mean 'square'?)",
                "bad: space 'square': a grid place is a list of 2 or 3 whole numbers like [2, 3], got \"e4\"",
                "high: space 'square': a height (z) on plane=\"xy\" is not supported yet, got [0,0,1]",
                "onto: onto(…) is not drawn yet",
            }));
            Assert.That(stage.Keys, Is.EqualTo(new[] { "missing", "nokey" }));
        }

        [Test]
        public void MarksAndObjectsFromTheBoundaryAreShownAndRemoved()
        {
            var stage = Stage(Looks("<Looks><Look name=\"dot\"><Sprite src=\"a.png\" /></Look></Looks>"));
            stage.Apply(new BridgeWorldMarks(1, new[]
            {
                new BridgeMark("f/dot@1", Look("dot"), At(1, 0), null, null),
                new BridgeMark("g", null, null, null, "'g' failed"),
            }, new string[0]));
            Assert.That(stage.Keys, Is.EqualTo(new[] { "mark:f/dot@1" }));
            Assert.That(stage.Problems.Single().ToString(), Is.EqualTo("g: 'g' failed"));

            stage.Apply(new BridgeWorldMarks(2, new BridgeMark[0], new[] { "f/dot@1" }));
            Assert.That(stage.Keys, Is.Empty);
        }

        [Test]
        public void AMarkAnswersEachInputAndClickWinsThePrimaryPress()
        {
            var stage = Stage(Looks("<Looks><Look name=\"dot\"><Sprite src=\"art/dot.png\" /></Look></Looks>"));
            BridgeMarkClick Act(string action) => new BridgeMarkClick(action, new Dictionary<string, BridgeValue>());
            var inputs = new Dictionary<string, BridgeMarkClick> { ["primary"] = Act("core.also"), ["secondary"] = Act("core.move") };
            stage.Apply(new BridgeWorldMarks(1, new[]
            {
                new BridgeMark("f/dot@1", Look("dot"), At(1, 0), Act("core.go"), null, inputs),
                new BridgeMark("f/dot@2", Look("dot"), At(2, 0), null, null, new Dictionary<string, BridgeMarkClick> { ["primary"] = Act("core.only") }),
            }, new string[0]));
            Assert.That(stage.ActionOf("mark:f/dot@1", LookBoard.Primary)!.Action, Is.EqualTo("core.go"));
            Assert.That(stage.ActionOf("mark:f/dot@1", LookBoard.Secondary)!.Action, Is.EqualTo("core.move"));
            Assert.That(stage.ActionOf("mark:f/dot@1", "rotate"), Is.Null);
            Assert.That(stage.ActionOf("mark:f/dot@2", LookBoard.Primary)!.Action, Is.EqualTo("core.only"));
            Assert.That(stage.Problems.Select(p => p.ToString()).Single(), Does.Contain("click= and inputs[\"primary\"] both answer the primary press"));
        }

        [Test]
        public void AMarkInputNoFileDeclaresIsReportedOncePerFunction()
        {
            var declared = InputLibrary.Load(new[] { new LookSource("core", "core/k.input.xml", "<Inputs><Input name=\"rotate\" key=\"R\" /></Inputs>") },
                new[] { "R" });
            var stage = new LookStage(Looks("<Looks><Look name=\"dot\"><Sprite src=\"art/dot.png\" /></Look></Looks>"), new MemoryLookRenderer(), declared);
            BridgeMarkClick Act(string action) => new BridgeMarkClick(action, new Dictionary<string, BridgeValue>());
            var inputs = new Dictionary<string, BridgeMarkClick> { ["rotate"] = Act("core.turn"), ["rotat"] = Act("core.turn"), ["secondary"] = Act("core.move") };
            stage.Apply(new BridgeWorldMarks(1, new[]
            {
                new BridgeMark("core.f/dot@1", Look("dot"), At(1, 0), null, null, inputs),
                new BridgeMark("core.f/dot@2", Look("dot"), At(2, 0), null, null, inputs),
            }, new string[0]));
            Assert.That(stage.Problems.Select(p => p.ToString()), Is.EqualTo(new[]
            {
                "core.f: no .input.xml declares the input 'rotat' (did you mean 'rotate'?), so no key reaches it (inputs= of a mark)",
            }));
        }

        private static LookPosition P(double x, double y, double z) =>
            new LookPosition((long)(x * BridgeFixed.Scale), (long)(y * BridgeFixed.Scale), (long)(z * BridgeFixed.Scale));

        [TestCase("<Space name=\"square\" kind=\"grid\" />", 2.0, 3.4, -10.0, 0, 0, 1, "[2,3]")]
        [TestCase("<Space name=\"square\" kind=\"grid\" />", -1.6, -0.4, -10.0, 0, 0, 1, "[-2,0]")]
        [TestCase("<Space name=\"square\" kind=\"grid\" flip-y=\"true\" />", 2.0, -3.0, -10.0, 0, 0, 1, "[2,3]")]
        [TestCase("<Space name=\"square\" kind=\"grid\" cell=\"2\" origin=\"1,1\" />", 4.9, 1.0, 5.0, 0, 0, -1, "[2,0]")]
        // 기울어진 반직선: (0, 1, -2)에서 (1, 0, 1) 쪽으로 2만큼 가면 바닥 z=0의 (2, 1)
        [TestCase("<Space name=\"square\" kind=\"grid\" />", 0.0, 1.0, -2.0, 1, 0, 1, "[2,1]")]
        // 3D 바닥: 위에서 내려다보면 높이 Y가 0이 되는 곳, 모드 y는 Z
        [TestCase("<Space name=\"square\" kind=\"grid\" plane=\"xz\" />", 3.0, 10.0, 4.2, 0, -1, 0, "[3,4]")]
        public void APressedRayTurnsBackIntoAPlace(string space, double x, double y, double z, int dx, int dy, int dz, string expected)
        {
            var stage = Stage(Looks("<Looks />", "<Spaces>" + space + "</Spaces>"));
            var place = stage.PlaceOnRay(P(x, y, z), P(dx, dy, dz));
            Assert.That(place!.Name, Is.EqualTo("square"));
            Assert.That(place.Target!.ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void ARayThatMissesTheFloorGivesNoPlace()
        {
            var stage = Stage(Looks("<Looks />"));
            Assert.That(stage.PlaceOnRay(P(0, 0, -10), P(1, 0, 0)), Is.Null, "parallel to the floor");
            Assert.That(stage.PlaceOnRay(P(0, 0, -10), P(0, 0, -1)), Is.Null, "pointing away from it");
        }

        /// <summary>모델 애니메이션 (G434): 상태와 클립의 자리·이름·next를 로드 때 본다. 클립 이름은 모델을 읽어야 알므로 보지 않는다.</summary>
        [Test]
        public void AModelsStatesAreCheckedWhenLoaded()
        {
            var kinds = LookKinds.Standard();
            kinds.AddModelFormat(".glb");
            List<string> Problems(string body) => LookLibrary.Load(new[]
            {
                new LookSource("core", "core/units.look.xml", "<Looks><Look name=\"a\">" + body + "</Look></Looks>"),
            }, kinds).Problems.Select(p => p.ToString().Substring("core/units.look.xml:1: ".Length)).ToList();

            Assert.That(Problems(
                "<Model src=\"s.glb\" bind-state=\"pose\" fade=\"0.2\" scale=\"0.006\">" +
                "<State name=\"idle\" clip=\"idle\" />" +
                "<State name=\"move\" bind-blend=\"speed\"><Clip clip=\"walk\" at=\"0\" /><Clip clip=\"run\" at=\"1\" /></State>" +
                "<State name=\"attack\" clip=\"attack\" once=\"true\" next=\"idle\" fade=\"0.1\" />" +
                "<Attach bone=\"hand_R\"><Model bind-src=\"weapon\" /><Sprite src=\"x.png\" /></Attach>" +
                "<Attach bind-bone=\"flag_at\"><Group /></Attach>" +
                "<Paint material=\"Cloth\" bind-color=\"team\" /><Paint material=\"Metal\" color=\"#888888\" />" +
                "</Model>"), Is.Empty);
            Assert.That(Problems(
                "<Model src=\"s.glb\" state=\"idlee\" fade=\"-1\" scale=\"0\">" +
                "<State name=\"idle\" clip=\"idle\" />" +
                "<State name=\"idle\" clip=\"walk\"><Clip clip=\"run\" at=\"1\" /></State>" +
                "<State clip=\"attack\" next=\"idlee\" />" +
                "<Clip clip=\"x\" />" +
                "<Sprite src=\"x.png\" /><Attach />" +
                "<Paint material=\"Cloth\" color=\"#ff0000\" /><Paint material=\"Cloth\" color=\"#00ff00\" /><Paint color=\"#000000\" />" +
                "</Model><State name=\"loose\" clip=\"x\" /><Attach bone=\"hand\" />"), Is.EquivalentTo(new[]
            {
                "fade=\"-1\": a time is not negative",
                "scale=\"0\": a scale is more than 0",
                "a <State> plays clip= or blends the <Clip>s inside, not both",
                "a <State> needs name=\"…\" written (spec-gaps G434)",
                "next= is where a once=\"true\" state goes when it ends",
                "<Clip> goes directly inside <State>, not inside <Model>",
                "a <Clip> needs at=\"…\", its place on the blend",
                "two <State>s are named 'idle'",
                "next=\"idlee\": no <State> in this <Model> is named 'idlee' (did you mean 'idle'?)",
                "state=\"idlee\": no <State> in this <Model> is named 'idlee' (did you mean 'idle'?)",
                "<State> goes directly inside <Model>, not at the top of a look",
                "two <Paint>s color the material 'Cloth' (spec-gaps G436)",
                "a <Paint> needs material=\"…\", the name of the model's material it colors",
                "inside a <Model> go only <State>s, <Attach>es and <Paint>s; put <Sprite> in an <Attach bone=\"…\"> or beside the <Model>",
                "an <Attach> needs bone=\"…\", the name of the part it follows (spec-gaps G435)",
                "<Attach> goes directly inside <Model>, not at the top of a look",
            }));
        }

        /// <summary>모델 (G391): 확장자로 로더를 고르고, 맡을 로더가 없으면 로드 때 등록된 확장자와 함께 알린다.</summary>
        [Test]
        public void AModelsFileNeedsARegisteredLoader()
        {
            List<string> Problems(LookKinds kinds) => LookLibrary.Load(new[]
            {
                new LookSource("core", "core/units.look.xml",
                    "<Looks><Look name=\"a\"><Model src=\"m/tank.GLB\" /></Look><Look name=\"b\"><Model src=\"m/tank.fbx\" /></Look></Looks>"),
            }, kinds).Problems.Select(p => p.ToString()).ToList();

            var kinds = LookKinds.Standard();
            Assert.That(Problems(kinds), Is.EqualTo(new[]
            {
                "core/units.look.xml:1: src=\"m/tank.GLB\": no model loader takes '.glb' (this host registers none) (spec-gaps G391)",
                "core/units.look.xml:1: src=\"m/tank.fbx\": no model loader takes '.fbx' (this host registers none) (spec-gaps G391)",
            }));
            kinds.AddModelFormat(".glb");
            kinds.AddModelFormat(".gltf");
            Assert.That(Problems(kinds), Is.EqualTo(new[]
            {
                "core/units.look.xml:1: src=\"m/tank.fbx\": no model loader takes '.fbx' (can be: .glb, .gltf) (spec-gaps G391)",
            }));
        }
    }
}
