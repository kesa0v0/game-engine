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
            int code = 0;
            try
            {
                Check().GetAwaiter().GetResult();
                Debug.Log("KESA SMOKE TEST: OK");
            }
            catch (Exception e)
            {
                Debug.LogError("KESA SMOKE TEST FAILED: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
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
            var mods = LoadedMods.Load(root, new[] { "ko", "en" });
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
