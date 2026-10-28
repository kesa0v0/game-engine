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
