#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kesa.Engine.Unity.Editor
{
    /// <summary>
    /// 창 없이 돌리는 확인 (batch 모드): <c>-executeMethod Kesa.Engine.Unity.Editor.SmokeTest.Run -kesaMods &lt;skirmish 폴더&gt;</c>.
    /// 진짜 커널 위에서 skirmish 화면(UXML)을 올려 Unity의 <c>VisualElement</c> 트리가 맞게 만들어지는지 본다. 틀리면 종료 코드 1.
    /// </summary>
    public static class SmokeTest
    {
        public static void Run()
        {
            try
            {
                Check().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Fail(e);
                return;
            }
            // 모델은 glTFast가 에디터 프레임을 타고 읽으므로 막고 기다리지 않고 프레임마다 본다
            var models = CheckModels();
            var until = DateTime.UtcNow.AddSeconds(60);
            void Poll()
            {
                if (!models.IsCompleted && DateTime.UtcNow < until) return;
                EditorApplication.update -= Poll;
                if (!models.IsCompleted) Fail(new TimeoutException("the model check did not finish in 60 s"));
                else if (models.IsFaulted) Fail(models.Exception!.GetBaseException());
                else
                {
                    Debug.Log("KESA SMOKE TEST: OK");
                    EditorApplication.Exit(0);
                }
            }
            EditorApplication.update += Poll;
        }

        private static void Fail(Exception e)
        {
            Debug.LogError("KESA SMOKE TEST FAILED: " + e);
            EditorApplication.Exit(1);
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            if (i < 0 || i + 1 >= args.Length) throw new InvalidOperationException("missing " + name + " <value>");
            return args[i + 1];
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("expected: " + what);
        }

        private static async Task Check()
        {
            string root = Arg("-kesaMods");
            // skirmish의 2D 판을 본다: 그림을 바꾸는 fox는 켜지 않는다 (G439)
            var mods = LoadedMods.Load(root, new[] { "ko", "en" }, enabled: new[] { "skirmish" });
            Require(!mods.HasErrors, "mods load without errors: " + string.Join("; ", mods.Diagnostics));
            var game = mods.NewMatch(1);
            var world = game.Host.World;
            Require(world.Submit("host", "skirmish.Start", new Dictionary<string, Value>()).Succeeded, "skirmish.Start");
            var red = world.All("Party").Single(p => world.DefOf(p) == "skirmish:Red");
            await game.Host.Seat(red.Value, "alice");
            var session = await game.Host.Open("alice");

            var host = new VisualElement();
            var renderer = new UiToolkitRenderer(host, mods.Assets);
            var ui = new UiInterpreter(session, renderer, mods.Format, mods.Assets.LoadText);
            var read = UxmlReader.Read("screen.uxml", File.ReadAllText(Path.Combine(root, "core", "ui", "screen.uxml")));
            Require(read.Succeeded, "screen.uxml reads: " + string.Join("; ", read.Problems));
            var document = read.Document!;
            var mounted = await ui.MountAsync(document,
                new Dictionary<string, BridgeValue> { [document.SeatParameter!] = new BridgeEntity(red.Value, "skirmish:Red") });
            Require(mounted.Problems.Count == 0, "mount without problems: " + string.Join("; ", mounted.Problems));

            var dump = new StringBuilder();
            Dump(host, 0, dump);
            Debug.Log("KESA SCREEN\n" + dump);

            var turn = host.Q<Label>("turn");
            Require(turn != null && turn.text == "1", "the turn label shows 1, got " + turn?.text);
            var cards = host.Q("units").Children().ToList();
            Require(cards.Count == 6, "six unit cards, got " + cards.Count);
            Require(cards.Count(c => c.ClassListContains("mine")) == 3, "three cards are mine");
            var endTurn = host.Q<Button>("endturn");
            Require(endTurn != null && endTurn.enabledSelf, "red may end the turn");
            var firstHp = cards[0].Q<Label>("hp");
            Require(firstHp != null && (firstHp.text == "10" || firstHp.text == "6"), "a card shows hp, got " + firstHp?.text);
            var kind = cards[0].Q<Label>("kind");
            Require(kind != null && (kind.text.Contains("병사") || kind.text.Contains("궁수")), "the unit kind is translated, got " + kind?.text);
            Require(ui.Errors.Count == 0, "no binding or style sheet errors: " + string.Join("; ", ui.Errors));
            Require(host.Q("screen").styleSheets.count == 1, "the mod's ui/screen.uss is on the screen element");
            CheckWindows(mods.Assets);
            await CheckBoard(game, session);
            await CheckPreload(game, session);
            CheckFx(game, session);
            await CheckInputs(game.Mods, session);
            UssCheck.Run();
        }

        /// <summary>편집 모드에서는 프레임이 돌지 않으므로 다 읽힌 그림을 손으로 건다 (G392).</summary>
        private static void WaitPictures(GameObjectLookRenderer renderer)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (renderer.LoadingPictures)
            {
                Require(DateTime.UtcNow < until, "pictures finish loading");
                System.Threading.Thread.Sleep(5);
                renderer.FinishPictures();
            }
        }

        /// <summary>효과 (G425): skirmish의 hit-flash가 투명하게 떠서 시간이 흐르면 보였다가 수명이 끝나면 지워진다.</summary>
        private static void CheckFx(Match game, IHostSession session)
        {
            Require(game.Mods.Presentation == BridgePresentation.Staged, "skirmish asks for staged presentation, got " + game.Mods.Presentation);
            var boardObject = new GameObject("kesa-fx");
            try
            {
                var renderer = new GameObjectLookRenderer(boardObject.transform, game.Mods.Assets);
                var stage = game.Board(session, renderer).Stage;
                var where = BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(1), new BridgeInt(1) }));
                stage.Play(new BridgeWorldFx(2, false, new[]
                {
                    new BridgeFxBatch(1, new[] { new BridgeFx("skirmish.hit_flash", new BridgeLook("hit-flash", new KeyValuePair<string, BridgeValue>[0]), where, null) }),
                }));
                WaitPictures(renderer);
                var sprite = boardObject.GetComponentInChildren<SpriteRenderer>();
                Require(sprite != null && sprite.color.a == 0f, "the flash starts see-through, got " + (sprite != null ? sprite.color.ToString() : "none"));
                stage.Advance(100);
                Require(Mathf.Approximately(sprite!.color.a, 1f) && sprite.color.r == 1f, "the flash is fully shown and keeps its tint, got " + sprite.color);
                stage.Advance(600);
                Require(boardObject.transform.childCount == 0, "the flash is gone when its life ends");
                stage.Play(new BridgeWorldFx(3, false, new[]
                {
                    new BridgeFxBatch(2, new[]
                    {
                        new BridgeFx("skirmish.hit_flash", new BridgeLook("damage-number", new[] { new KeyValuePair<string, BridgeValue>("amount", new BridgeInt(-3)) }), where, null),
                    }),
                }));
                var text = boardObject.GetComponentInChildren<TextMesh>();
                Require(text != null && text.text == "-3" && text.color.a == 0f, "the damage number starts see-through, got " + (text != null ? text.text + " " + text.color : "none"));
                stage.Advance(100);
                Require(Mathf.Approximately(text!.color.a, 1f) && text.GetComponent<MeshRenderer>().sortingOrder > 0, "the damage number is shown above the board, got " + text.color);
                stage.Advance(1000);
                Require(boardObject.transform.childCount == 0, "the damage number is gone when its life ends");
                Require(stage.Problems.Count == 0, "the effects play cleanly: " + string.Join("; ", stage.Problems));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardObject);
            }
        }

        /// <summary>
        /// 모델 (G391·G433): 코드로 만든 삼각형 하나짜리 <c>.glb</c>를 임시 모드 폴더에 두고 <c>Model</c>로 그리면 그 메시가 물체 아래에 붙는다.
        /// 없는 파일은 보라색 상자다.
        /// </summary>
        private static async Task CheckModels()
        {
            string folder = Path.Combine(Path.GetTempPath(), "kesa-model-check");
            Directory.CreateDirectory(Path.Combine(folder, "models"));
            File.WriteAllBytes(Path.Combine(folder, "models", "tri.glb"), Triangle(false));
            File.WriteAllBytes(Path.Combine(folder, "models", "moving.glb"), Triangle(true));
            File.WriteAllBytes(Path.Combine(folder, "models", "dot.png"), new Texture2D(1, 1).EncodeToPNG());
            var boardObject = new GameObject("kesa-models-check");
            try
            {
                var kinds = GameObjectLookRenderer.StandardKinds();
                var assets = new ModFolderAssets(new[] { folder });
                var library = LookLibrary.Load(new[]
                {
                    new LookSource("check", "check/models.look.xml",
                        "<Looks><Look name=\"tri\"><Model src=\"models/tri.glb\" /></Look><Look name=\"gone\"><Model bind-src=\"file\" /></Look>" +
                        "<Look name=\"moving\"><Model src=\"models/moving.glb\" bind-state=\"pose\" fade=\"0.5\">" +
                        "<State name=\"a\" clip=\"slide\" /><State name=\"b\" clip=\"lift\" />" +
                        "<State name=\"hop\" clip=\"lift\" once=\"true\" next=\"a\" fade=\"0\" />" +
                        "<State name=\"mix\" blend=\"0.5\"><Clip clip=\"slide\" at=\"0\" /><Clip clip=\"lift\" at=\"1\" /></State>" +
                        "<Attach bone=\"hand\"><Model src=\"models/tri.glb\" /></Attach><Attach bone=\"tail\" />" +
                        "</Model></Look>" +
                        "<Look name=\"tinted\"><Model src=\"models/tri.glb\" tint=\"#808080\" /></Look>" +
                        "<Look name=\"painted\"><Model src=\"models/tri.glb\" tint=\"#808080\"><Paint material=\"Cloth\" bind-color=\"team\" />" +
                        "<Paint material=\"Skin\" color=\"#000000\" /></Model></Look>" +
                        "<Look name=\"half\"><Model src=\"models/tri.glb\" scale=\"0.5\" /></Look>" +
                        "<Look name=\"facing\"><Model src=\"models/tri.glb\" bind-turn=\"turn\" /><Sprite src=\"models/dot.png\" offset=\"0,0.5\" />" +
                        "<Text text=\"x\" face=\"camera\" /></Look></Looks>"),
                    new LookSource("check", "check/board.space.xml",
                        "<Spaces><Space name=\"square\" kind=\"grid\" /><Space name=\"floor\" kind=\"grid\" plane=\"xz\" /></Spaces>"),
                }, kinds, path => assets.Find(path) != null);
                Require(library.Problems.Count == 0, "the model look reads cleanly: " + string.Join("; ", library.Problems));
                var renderer = new GameObjectLookRenderer(boardObject.transform, assets);
                var stage = new LookStage(library, renderer);
                var at = BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(2), new BridgeInt(0) }));
                stage.Show("a", new BridgeLook("tri", new KeyValuePair<string, BridgeValue>[0]), at);
                stage.Show("b", new BridgeLook("gone", new[] { new KeyValuePair<string, BridgeValue>("file", new BridgeStr("models/none.glb")) }), at);
                Require(renderer.LoadingPictures, "the models are loading");
                while (renderer.LoadingPictures) await Task.Yield();
                var mesh = boardObject.transform.Find("a").GetComponentInChildren<MeshFilter>();
                Require(mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.vertexCount == 3, "the glTF triangle is under the object");
                Require(mesh!.gameObject.activeInHierarchy && mesh.gameObject.name == "tri", "the clone is shown with its node name, got " + mesh.gameObject.name);
                var missing = boardObject.transform.Find("b/Model/missing-model");
                Require(missing != null, "a model that cannot be read shows the missing box");
                Require(stage.Problems.Count == 0, "the models show cleanly: " + string.Join("; ", stage.Problems));
                // 크기 (G438): 모델 뷰에 곱하고 물체 노드의 크기는 그대로
                stage.Show("h", new BridgeLook("half", new KeyValuePair<string, BridgeValue>[0]), at);
                var half = boardObject.transform.Find("h");
                Require(half.Find("Model").localScale == Vector3.one * 0.5f && half.localScale == Vector3.one,
                    "scale=\"0.5\" halves the model, got " + half.Find("Model").localScale);
                await CheckFrames(stage, renderer, boardObject.transform);
                await CheckAnimation(stage, renderer, boardObject.transform, at);
                CheckColors(stage, boardObject.transform, at);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardObject);
                Directory.Delete(folder, true);
            }
        }

        /// <summary>
        /// 방향 틀 (G443·G444): xy 판은 위가 -Z(보는 쪽)·북이 +Y, xz 판은 위가 +Y·북이 +Z. 모델의 위·앞(glTF +Y·+Z)이 틀의 위·북이고 turn 90이면 동(+X)을 본다.
        /// 스프라이트는 바닥에 누워 그림의 위가 북이고 offset의 둘째 수는 북이다. face="camera"는 카메라의 방향을 따른다.
        /// </summary>
        private static async Task CheckFrames(LookStage stage, GameObjectLookRenderer renderer, Transform board)
        {
            bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 0.01f;
            BridgeWhere Place(string space) => BridgeWhere.At(space, new BridgeList(new BridgeValue[] { new BridgeInt(0), new BridgeInt(0) }));
            BridgeLook Facing(long turn) => new BridgeLook("facing", new[] { new KeyValuePair<string, BridgeValue>("turn", new BridgeInt(turn)) });
            var cases = new (string Key, string Space, long Turn, Vector3 Up, Vector3 Front, Vector3 North)[]
            {
                ("f1", "square", 0, Vector3.back, Vector3.up, Vector3.up),
                ("f2", "square", 90, Vector3.back, Vector3.right, Vector3.up),
                ("f3", "floor", 0, Vector3.up, Vector3.forward, Vector3.forward),
                ("f4", "floor", 90, Vector3.up, Vector3.right, Vector3.forward),
            };
            foreach (var c in cases) stage.Show(c.Key, Facing(c.Turn), Place(c.Space));
            while (renderer.LoadingPictures)
            {
                renderer.FinishPictures();
                await Task.Yield();
            }
            foreach (var c in cases)
            {
                var obj = board.Find(c.Key);
                var tri = obj.GetComponentsInChildren<Transform>().First(t => t.name == "tri");
                Require(Near(tri.up, c.Up) && Near(tri.forward, c.Front),
                    c.Space + " turn " + c.Turn + ": the model's up and front should be " + c.Up + " " + c.Front + ", got " + tri.up + " " + tri.forward);
                var sprite = obj.GetComponentInChildren<SpriteRenderer>().transform;
                Require(Near(sprite.up, c.North) && Near(-sprite.forward, c.Up),
                    c.Space + ": a sprite lies on the floor with its top to the north, got up " + sprite.up + " facing " + -sprite.forward);
                Require(Near(sprite.position - obj.position, c.North * 0.5f), c.Space + ": offset \"0,0.5\" is half a cell north, got " + (sprite.position - obj.position));
            }
            var eye = new GameObject("kesa-check-camera").AddComponent<Camera>();
            try
            {
                eye.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
                renderer.Camera = eye;
                renderer.FaceCamera();
                var text = board.Find("f3").GetComponentInChildren<TextMesh>().transform;
                Require(Quaternion.Angle(text.rotation, eye.transform.rotation) < 0.5f, "face=\"camera\" turns the text to the camera");
            }
            finally
            {
                renderer.Camera = null;
                UnityEngine.Object.DestroyImmediate(eye.gameObject);
            }
            foreach (var c in cases) stage.Hide(c.Key);
            Require(stage.Problems.Count == 0, "the turned looks show cleanly: " + string.Join("; ", stage.Problems));
        }

        /// <summary>
        /// 모델 애니메이션 (G434): <c>slide</c>는 1초에 x 0→1, <c>lift</c>는 y 0→1. 상태가 바뀌면 <c>fade</c> 동안 섞고, <c>once</c>는 끝나면
        /// <c>next</c>로, 블렌드 상태는 <c>at</c>이 가까운 두 클립을 섞는다. glTF의 x는 Unity에서 뒤집히므로 크기로 잰다.
        /// </summary>
        private static async Task CheckAnimation(LookStage stage, GameObjectLookRenderer renderer, Transform board, BridgeWhere at)
        {
            void Pose(string state) =>
                stage.Show("c", new BridgeLook("moving", new[] { new KeyValuePair<string, BridgeValue>("pose", new BridgeStr(state)) }), at);
            void Expect(float x, float y, string what)
            {
                var p = board.Find("c").GetComponentsInChildren<Transform>().First(t => t.name == "tri").localPosition;
                Require(Mathf.Abs(Mathf.Abs(p.x) - x) < 0.02f && Mathf.Abs(p.y - y) < 0.02f, what + ": expected (" + x + ", " + y + "), got " + p);
            }
            Pose("a");
            while (renderer.LoadingPictures) await Task.Yield();
            Expect(0f, 0f, "a state starts at its first pose");
            stage.Advance(250);
            Expect(0.25f, 0f, "a looping state plays with the time given");
            Pose("b");
            stage.Advance(250);
            Expect(0.25f, 0.125f, "halfway through the fade both states are mixed");
            stage.Advance(250);
            Expect(0f, 0.5f, "after the fade only the new state shows");
            Pose("b");
            stage.Advance(250);
            Expect(0f, 0.75f, "setting the same state again does not restart it");
            Pose("hop");
            stage.Advance(500);
            Expect(0f, 0.5f, "fade=0 on the state switches at once");
            stage.Advance(1000);
            stage.Advance(500);
            Expect(0.5f, 0f, "a once state goes to its next when it ends");
            Pose("mix");
            stage.Advance(500);
            Expect(0.25f, 0.25f, "blend=0.5 mixes the two clips around it evenly");

            // 뼈에 붙이기 (G435): hand는 tri의 자식 노드라 애니메이션을 따라간다. 없는 뼈는 모델 원점
            var c = board.Find("c");
            var attaches = c.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Attach").ToList();
            Require(attaches.Count == 2, "both <Attach>es are drawn");
            Require(attaches.Any(t => t.parent.name == "hand"), "an <Attach bone=\"hand\"> hangs under the hand part");
            Require(attaches.Any(t => t.parent.name == "Model"), "an unknown bone attaches at the model's origin");
            var hand = attaches.First(t => t.parent.name == "hand");
            Require(hand.GetComponentInChildren<MeshFilter>() != null, "the attached model is under the hand");
            var tri = c.GetComponentsInChildren<Transform>().First(t => t.name == "tri" && t.Find("hand") != null);
            var before = hand.position;
            stage.Advance(250);
            Require(Vector3.Distance(hand.position, before) > 0.05f, "the hand follows the animated part");

            // 모델 누르기: 메시의 경계 상자로 잰다. 판에 누운 모델이라 삼각형의 면 쪽에서 쏜다
            var aim = tri.TransformPoint(-0.2f, 0.2f, 0f);
            var keys = renderer.KeysAt(new Ray(aim - tri.forward * 10f, tri.forward));
            Require(keys.Contains("c"), "a model can be pressed, got " + string.Join(", ", keys));
            Require(stage.Problems.Count == 0, "the animated model shows cleanly: " + string.Join("; ", stage.Problems));
        }

        /// <summary>모델 색 (G436): <c>tint</c>는 모든 재질에, <c>Paint</c>는 그 이름 재질에 tint 대신 곱한다. 원본 재질은 그대로다.</summary>
        private static void CheckColors(LookStage stage, Transform board, BridgeWhere at)
        {
            Color Shown(string key)
            {
                var material = board.Find(key).GetComponentInChildren<MeshRenderer>().sharedMaterial;
                foreach (var name in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                    if (material.HasProperty(name)) return material.GetColor(name);
                throw new InvalidOperationException("the model's material has no base color");
            }
            bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
            var plain = Shown("a");
            stage.Show("t", new BridgeLook("tinted", new KeyValuePair<string, BridgeValue>[0]), at);
            stage.Show("p", new BridgeLook("painted", new[] { new KeyValuePair<string, BridgeValue>("team", new BridgeStr("#ff0000")) }), at);
            var half = new Color32(128, 128, 128, 255);
            Require(Near(Shown("t"), plain * (Color)half), "tint multiplies the model's color, got " + Shown("t"));
            Require(Near(Shown("p"), plain * Color.red), "a <Paint> colors its material instead of the tint, got " + Shown("p"));
            Require(Near(Shown("a"), plain), "the loaded original keeps its color");
            stage.Show("p", new BridgeLook("painted", new[] { new KeyValuePair<string, BridgeValue>("team", new BridgeStr("#0000ff")) }), at);
            Require(Near(Shown("p"), plain * Color.blue), "a bound color repaints, got " + Shown("p"));
        }

        // glTF 2.0 바이너리: 위치 세 개짜리 삼각형 하나, 노드 이름 "tri". 움직이면 노드 위치를 바꾸는 1초짜리 slide(x)·lift(y)
        private static byte[] Triangle(bool moving)
        {
            var floats = new List<float> { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
            if (moving) floats.AddRange(new float[] { 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0 });
            var bin = new byte[floats.Count * 4];
            Buffer.BlockCopy(floats.ToArray(), 0, bin, 0, bin.Length);
            string animation = !moving ? "" :
                ",\"animations\":[" +
                "{\"name\":\"slide\",\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}],\"samplers\":[{\"input\":1,\"output\":2}]}," +
                "{\"name\":\"lift\",\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}],\"samplers\":[{\"input\":1,\"output\":3}]}]";
            string views = !moving ? "" :
                ",{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":44,\"byteLength\":24},{\"buffer\":0,\"byteOffset\":68,\"byteLength\":24}";
            string accessors = !moving ? "" :
                ",{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]}" +
                ",{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"},{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}";
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0,\"name\":\"tri\"" + (moving ? ",\"children\":[1]},{\"name\":\"hand\",\"translation\":[0,0,1]}" : "}") + "]," +
                          "\"materials\":[{\"name\":\"Cloth\"}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"material\":0}]}],\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
                          "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36}" + views + "]," +
                          "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]}" + accessors + "]" +
                          animation + "}";
            while (json.Length % 4 != 0) json += " ";
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(0x46546C67u);
            w.Write(2u);
            w.Write((uint)(12 + 8 + jsonBytes.Length + 8 + bin.Length));
            w.Write((uint)jsonBytes.Length);
            w.Write(0x4E4F534Au);
            w.Write(jsonBytes);
            w.Write((uint)bin.Length);
            w.Write(0x004E4942u);
            w.Write(bin);
            w.Flush();
            return stream.ToArray();
        }

        /// <summary>미리 읽기 (G392): 끝나면 그 그림은 그릴 때 바로 걸린다.</summary>
        private static async Task CheckPreload(Match game, IHostSession session)
        {
            var boardObject = new GameObject("kesa-preload");
            try
            {
                var renderer = new GameObjectLookRenderer(boardObject.transform, game.Mods.Assets);
                var preload = renderer.Preload(game.Mods.Looks, new[] { "retreat-spot" });
                Require(!preload.IsCompleted, "a preload waits for the pictures");
                WaitPictures(renderer);
                Require(preload.IsCompleted, "the preload finishes once the pictures are in");
                var board = game.Board(session, renderer);
                board.Stage.Show("test", new BridgeLook("retreat-spot", new KeyValuePair<string, BridgeValue>[0]),
                    BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(0), new BridgeInt(0) })));
                Require(!renderer.LoadingPictures && boardObject.GetComponentInChildren<SpriteRenderer>().sprite != null, "a preloaded picture shows at once");
                await Task.CompletedTask;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardObject);
            }
        }

        /// <summary>
        /// 입력 선언 (G417·G423): skirmish의 <c>keys.input.xml</c>을 읽고, 모든 <c>Key</c> 이름이 키보드 컨트롤로 이어지며, 눌린 키가 입력 이름이 된다.
        /// </summary>
        private static async Task CheckInputs(LoadedMods mods, IHostSession session)
        {
            Require(mods.Inputs.Problems.Count == 0, "input files read cleanly: " + string.Join("; ", mods.Inputs.Problems));
            var handlers = await session.WorldInputs();
            var unknown = mods.Inputs.CheckHandlers(handlers);
            Require(handlers.Any(h => h.Input == "inspect") && unknown.Count == 0, "every skirmish handler has a key: " + string.Join("; ", unknown));
            Require(mods.Inputs.Inputs.TryGetValue("inspect", out var inspect) && inspect.Key.ToString() == "I", "skirmish declares inspect on I");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var lost = ModInputs.KeyNames.Where(k => InputControlPath.TryFindControl(keyboard, ModInputs.PathOf(k)) == null).ToList();
                Require(lost.Count == 0, "every Key name is a keyboard control, not: " + string.Join(", ", lost));
                Require(InputControlPath.TryFindControl(mouse, ModInputs.PathOf(LookBoard.Secondary)) != null, "secondary is the right button");

                var library = InputLibrary.Load(new[]
                {
                    new LookSource("t", "t/k.input.xml", "<Inputs><Input name=\"spin\" key=\"R\" /><Input name=\"spin-back\" key=\"SHIFT + R\" />" +
                                                       "<Input name=\"save\" key=\"CTRL + Digit1\" /></Inputs>"),
                }, ModInputs.KeyNames);
                Require(library.Problems.Count == 0, "test inputs read: " + string.Join("; ", library.Problems));
                // 편집 모드에서는 액션이 갱신되지 않아 누름은 흉내 내지 않고, 바인딩이 키로 풀리는지만 본다
                using var inputs = new ModInputs(library);
                string Controls(string action) => string.Join(",", inputs.Actions[action].controls.Select(c => c.name));
                Require(Controls("spin") == "r", "spin is bound to R, got " + Controls("spin"));
                Require(Controls("spin-back") == "shift,r", "spin-back is bound to SHIFT and R, got " + Controls("spin-back"));
                Require(Controls("save") == "ctrl,1", "save is bound to CTRL and 1, got " + Controls("save"));
                Require(Controls(LookBoard.Secondary) == "rightButton", "secondary is the right button, got " + Controls(LookBoard.Secondary));
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
            }
        }

        /// <summary>판 위 그림 (G412~G415): skirmish의 <c>looks/</c>로 표시가 SpriteRenderer로 칸 자리에 그려지고, 모르는 그림은 대체 그림이다.</summary>
        private static async Task CheckBoard(Match game, IHostSession session)
        {
            Require(game.Mods.Looks.Problems.Count == 0, "look files read cleanly: " + string.Join("; ", game.Mods.Looks.Problems));
            var boardObject = new GameObject("kesa-board");
            try
            {
                var renderer = new GameObjectLookRenderer(boardObject.transform, game.Mods.Assets);
                var board = game.Board(session, renderer);
                await board.RefreshAsync();
                Require(renderer.LoadingPictures, "unit pictures are read off the main thread, not while drawing");
                WaitPictures(renderer);
                Require(board.Stage.Keys.Count(k => k.StartsWith("entity:")) == 6 && !board.Stage.Keys.Any(k => k.StartsWith("mark:")),
                    "six units and no marks before a hit: " + string.Join(", ", board.Stage.Keys));
                Require(board.Stage.Problems.Count == 0, "the units draw cleanly: " + string.Join("; ", board.Stage.Problems));
                for (int i = boardObject.transform.childCount - 1; i >= 0; i--)
                    if (boardObject.transform.GetChild(i).name.StartsWith("entity:")) board.Stage.Hide(boardObject.transform.GetChild(i).name);

                var spot = new BridgeLook("retreat-spot", new KeyValuePair<string, BridgeValue>[0]);
                board.Stage.Show("test", spot, BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(2), new BridgeInt(3) })));
                WaitPictures(renderer);
                Require(boardObject.transform.childCount == 1, "one object on the board");
                var obj = boardObject.transform.GetChild(0);
                Require(obj.localPosition == new Vector3(2, -3, 0) && obj.localScale == Vector3.one, "placed on square (2, 3) with y down, got " + obj.localPosition);
                var sprite = obj.GetComponentInChildren<SpriteRenderer>();
                var picture = sprite != null ? sprite.sprite : null;
                Require(picture != null && picture.texture.width == 1, "the white picture is loaded");
                Require(picture!.bounds.size.x == 1f, "the long side is one cell, got " + picture.bounds.size);
                Require(sprite!.color == (Color)new Color32(0x40, 0x80, 0xff, 0x80), "tinted, got " + sprite.color);
                var keys = renderer.KeysAt(new Ray(new Vector3(2.2f, -3.1f, -10f), Vector3.forward));
                Require(keys.Count == 1 && keys[0] == "test", "a ray through the square finds the mark, got " + string.Join(", ", keys));
                Require(renderer.KeysAt(new Ray(new Vector3(0f, 0f, -10f), Vector3.forward)).Count == 0, "an empty square finds nothing");
                boardObject.transform.position = new Vector3(10f, 0f, 0f);
                var place = renderer.PlaceAt(board.Stage, new Ray(new Vector3(12.2f, -3.1f, -10f), Vector3.forward));
                Require(place != null && place.Name == "square" && place.Target!.ToString() == "[2,3]",
                    "a ray turns back into square (2, 3) in the board's own frame, got " + place);
                boardObject.transform.position = Vector3.zero;

                board.Stage.Show("test", new BridgeLook("no-such-look", new KeyValuePair<string, BridgeValue>[0]),
                    BridgeWhere.At("square", new BridgeList(new BridgeValue[] { new BridgeInt(0), new BridgeInt(0) })));
                var missing = boardObject.transform.GetChild(0).GetComponentInChildren<SpriteRenderer>();
                Require(missing != null && missing.sprite != null && missing.sprite.texture.width == 8, "an unknown look shows the missing picture");
                Require(board.Stage.Problems.Count == 1, "the unknown look is reported once: " + string.Join("; ", board.Stage.Problems));
                board.Stage.Hide("test");
                Require(boardObject.transform.childCount == 0, "hiding removes the object");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boardObject);
            }
        }

        /// <summary>창 배치 (G354): 창은 겹침 층으로 가고 원래 자리에는 자리표가 남아 형제 순서가 그대로이며, 모달 막이는 정한 자리에 선다.</summary>
        private static void CheckWindows(IUiAssets assets)
        {
            var host = new VisualElement();
            var renderer = new UiToolkitRenderer(host, assets);
            var root = renderer.Create("VisualElement", null, 0, new KeyValuePair<string, string>[0]);
            var first = renderer.Create("Label", root, 0, new[] { new KeyValuePair<string, string>("name", "first") });
            var info = renderer.Create("VisualElement", root, 1, new[] { new KeyValuePair<string, string>("name", "info") });
            var confirm = renderer.Create("VisualElement", root, 2, new[] { new KeyValuePair<string, string>("name", "confirm") });
            var inner = renderer.Create("Label", info, 0, new[] { new KeyValuePair<string, string>("name", "inner") });
            renderer.MakeWindow(info);
            renderer.MakeWindow(confirm);
            var last = renderer.Create("Label", root, 3, new[] { new KeyValuePair<string, string>("name", "last") });

            var rootElement = renderer.Element(root);
            Require(rootElement.childCount == 4 && rootElement[0].name == "first" && rootElement[3].name == "last",
                "the windows left place holders so sibling order holds");
            var layer = host.Q("kesa-windows");
            Require(renderer.Element(info).parent == layer && renderer.Element(inner).parent == renderer.Element(info), "the window moved to the window layer with its children");
            Require(renderer.Element(info).resolvedStyle.display == DisplayStyle.None || renderer.Element(info).style.display == DisplayStyle.None,
                "a window is hidden until arranged");

            renderer.ArrangeWindows(new[] { info, confirm }, 1);
            var order = layer.Children().Select(e => e.name).ToList();
            Require(string.Join(",", order) == "info,kesa-modal-blocker,confirm", "arranged with the blocker under the modal, got " + string.Join(",", order));
            Require(renderer.Element(info).style.display == DisplayStyle.Flex, "a shown window is displayed");

            renderer.ArrangeWindows(new[] { confirm, info }, -1);
            order = layer.Children().Select(e => e.name).ToList();
            Require(string.Join(",", order) == "confirm,info", "reordered without a blocker, got " + string.Join(",", order));
            renderer.ArrangeWindows(new[] { info }, -1);
            order = layer.Children().Select(e => e.name).ToList();
            Require(string.Join(",", order) == "confirm,info", "already in order, so nothing moved, got " + string.Join(",", order));
            Require(renderer.Element(confirm).style.display == DisplayStyle.None, "a window not in the list is hidden");

            renderer.Move(info, root, 3);
            Require(rootElement.childCount == 4 && rootElement[3].name == "kesa-window-place", "moving a window moves its place holder");
            renderer.Remove(root);
            Require(layer.childCount == 0, "removing the panel removes its windows from the layer, left " + layer.childCount);
        }

        private static void Dump(VisualElement element, int depth, StringBuilder text)
        {
            text.Append(' ', depth * 2).Append(element.GetType().Name);
            if (!string.IsNullOrEmpty(element.name)) text.Append(" #").Append(element.name);
            if (element is TextElement t && !string.IsNullOrEmpty(t.text)) text.Append(" \"").Append(t.text).Append('"');
            var classes = element.GetClasses().ToList();
            if (classes.Count > 0) text.Append(" .").Append(string.Join(".", classes));
            if (!element.enabledSelf) text.Append(" (disabled)");
            text.AppendLine();
            foreach (var child in element.Children()) Dump(child, depth + 1, text);
        }
    }
}
