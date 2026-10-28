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
            var ui = new UiInterpreter(session, renderer, mods.Format, mods.Assets.LoadUxml);
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
            Require(ui.Errors.Count == 0, "no binding errors: " + string.Join("; ", ui.Errors));
            CheckWindows(mods.Assets);
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

            renderer.ArrangeWindows(new[] { info }, -1);
            order = layer.Children().Select(e => e.name).ToList();
            Require(string.Join(",", order) == "confirm,info", "hidden windows go below and there is no blocker, got " + string.Join(",", order));
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
