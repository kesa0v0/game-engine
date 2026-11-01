#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// 데모 장면을 만들고 확인한다. 메뉴 <c>Kesa/Create demo scene</c>은 장면과 패널 설정을 만들고, <c>Check</c>는 창 없이(batch 모드)
/// 장면이 열리고 데모가 화면을 올리는지 본다: <c>-executeMethod KesaDemoSetup.Check</c>.
/// </summary>
public static class KesaDemoSetup
{
    private const string ScenePath = "Assets/Scenes/Demo.unity";
    private const string SettingsPath = "Assets/Settings/DemoPanelSettings.asset";
    private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

    [MenuItem("Kesa/Create demo scene")]
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Settings");
        AssetDatabase.ImportAsset(ThemePath);

        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(SettingsPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        EditorUtility.SetDirty(settings);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        camera.GetComponent<Camera>().backgroundColor = new Color(0.12f, 0.12f, 0.14f);
        var demo = new GameObject("Kesa Demo");
        demo.AddComponent<UIDocument>().panelSettings = settings;
        demo.AddComponent<KesaDemo>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Kesa: the demo scene was created at " + ScenePath);
    }

    /// <summary>batch 모드용: 장면을 만들고 끝낸다.</summary>
    public static void CreateAndExit()
    {
        Create();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// batch 모드용(그래픽 있이, <c>-nographics</c> 없이): 데모를 열고 카메라가 비춘 판을 <c>-kesaShot</c> 경로의 PNG로 남긴다.
    /// 패널(UI Toolkit)은 카메라 그림에 들어가지 않는다.
    /// </summary>
    public static void Shot() => RunInFrames("KESA DEMO SHOT", async () =>
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-kesaShot");
        if (at < 0 || at + 1 >= args.Length) throw new InvalidOperationException("missing -kesaShot <png path>");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var demo = UnityEngine.Object.FindFirstObjectByType<KesaDemo>()!;
        await demo.Begin(new VisualElement());
        await demo.WaitPictures();
        var camera = Camera.main!;
        var target = new RenderTexture(960, 540, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        image.Apply();
        File.WriteAllBytes(args[at + 1], image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        Debug.Log("KESA DEMO SHOT: " + args[at + 1]);
    });

    /// <summary>batch 모드용: 장면이 열리고(없는 스크립트 없이) 데모가 skirmish 화면을 올리는지 본다. 틀리면 종료 코드 1.</summary>
    public static void Check() => RunInFrames("KESA DEMO CHECK", async () =>
    {
        await Verify();
        Debug.Log("KESA DEMO CHECK: OK");
    });

    // 모델(glTFast)은 에디터 프레임을 타고 읽으므로 막고 기다리지 않고 프레임마다 본다 (SmokeTest와 같음)
    private static void RunInFrames(string name, Func<Task> work)
    {
        var task = work();
        var until = DateTime.UtcNow.AddSeconds(120);
        void Poll()
        {
            if (!task.IsCompleted && DateTime.UtcNow < until) return;
            EditorApplication.update -= Poll;
            if (task.IsCompleted && !task.IsFaulted)
            {
                EditorApplication.Exit(0);
                return;
            }
            Debug.LogError(name + " FAILED: " + (task.IsCompleted ? task.Exception!.GetBaseException().ToString() : "did not finish in 120 s"));
            EditorApplication.Exit(1);
        }
        EditorApplication.update += Poll;
    }

    private static async Task Verify()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var demo = UnityEngine.Object.FindFirstObjectByType<KesaDemo>();
        if (demo == null) throw new InvalidOperationException("the scene has no KesaDemo (a missing script?)");
        var document = demo.GetComponent<UIDocument>();
        if (document == null || document.panelSettings == null) throw new InvalidOperationException("the scene's UIDocument has no panel settings");
        if (document.panelSettings.themeStyleSheet == null) throw new InvalidOperationException("the panel settings have no theme");

        var root = new VisualElement();
        await demo.Begin(root);
        var dump = new StringBuilder();
        Dump(root, 0, dump);
        Debug.Log("KESA DEMO SCREEN\n" + dump);
        var turn = root.Q<Label>("turn");
        if (turn == null || turn.text != "1") throw new InvalidOperationException("the turn label should show 1, got " + turn?.text);
        int cards = root.Q("units").childCount;
        if (cards != 6) throw new InvalidOperationException("six unit cards expected, got " + cards);
        if (root.styleSheets.count == 0) throw new InvalidOperationException("the demo style sheet (Resources/KesaDemo.uss) was not loaded");
        if (scene.rootCount < 2) throw new InvalidOperationException("the scene lost its objects");

        var me = (await demo.Session!.Seats()).Single();
        var ended = await demo.Session.Deliver("skirmish.EndTurn", new Dictionary<string, BridgeValue> { ["party"] = me });
        if (!ended.Succeeded) throw new InvalidOperationException("red could not end the turn: " + ended);
        await demo.Step();
        await demo.Finish();
        turn = root.Q<Label>("turn");
        if (turn == null || turn.text != "3") throw new InvalidOperationException("the bot should play blue's turn so it is turn 3, got " + turn?.text);
        await VerifyBoard(demo);
    }

    /// <summary>
    /// 판의 GameObject를 화면 좌표로 눌러 본다: 카메라 광선 → 물체 → 경계 → 커널. 실제 키 장치만 빠진다(입력 이름을 바로 넘김).
    /// </summary>
    private static async Task VerifyBoard(KesaDemo demo)
    {
        await demo.WaitPictures();
        var board = demo.transform.Find("Board") ?? throw new InvalidOperationException("the demo has no Board object");
        var units = Enumerable.Range(0, board.childCount).Select(board.GetChild).Where(t => t.name.StartsWith("entity:")).ToList();
        if (units.Count != 6) throw new InvalidOperationException("six unit objects expected, got " + string.Join(", ", units.Select(u => u.name)));
        var camera = Camera.main ?? throw new InvalidOperationException("no main camera");
        Vector3 ScreenOf(Vector3 local) => camera.WorldToScreenPoint(board.TransformPoint(local));
        long IdOf(Transform unit) => long.Parse(unit.name.Substring("entity:".Length));
        async Task<long?> Inspected() => ((await demo.Session!.Call("inspected", "skirmish.inspected", new BridgeValue[0])).Value as BridgeEntity)?.Id;

        // 빨강은 아래(y=4) 줄에서 시작한다 (skirmish board.space.xml의 flip-y)
        var red = units.Where(u => Mathf.RoundToInt(u.localPosition.y) == -4).OrderBy(u => u.localPosition.x).ToList();
        if (red.Count < 2) throw new InvalidOperationException("two red units on the bottom row expected");
        await demo.PressBoard("secondary", ScreenOf(red[0].localPosition));
        if (await Inspected() != IdOf(red[0])) throw new InvalidOperationException("the right press on a unit object should open its window");
        await demo.PressBoard("inspect", ScreenOf(red[1].localPosition));
        if (await Inspected() != IdOf(red[1])) throw new InvalidOperationException("the inspect key (keys.input.xml) on a unit object should open its window");

        // 고르고 바로 위 빈 칸을 누르면 옮겨 가고 물체도 따라간다
        var mover = red.First(u => !units.Any(o => o != u && Vector3.Distance(o.localPosition, u.localPosition + Vector3.up) < 0.1f));
        var from = mover.localPosition;
        await demo.PressBoard("primary", ScreenOf(from));
        await demo.PressBoard("primary", ScreenOf(from + Vector3.up));
        await demo.Step();
        await demo.WaitPictures();
        // skirmish 유닛은 move="0.25"로 미끄러져 간다 (G432)
        demo.Pass(125);
        if (Vector3.Distance(mover.localPosition, from + Vector3.up * 0.5f) > 0.01f)
            throw new InvalidOperationException("the unit object should be halfway up from " + from + " after 0.125 s, is at " + mover.localPosition);
        demo.Pass(200);
        if (Vector3.Distance(mover.localPosition, from + Vector3.up) > 0.01f)
            throw new InvalidOperationException("the unit object should step up one square from " + from + ", is at " + mover.localPosition);
    }

    private static void Dump(VisualElement element, int depth, StringBuilder text)
    {
        text.Append(' ', depth * 2).Append(element.GetType().Name);
        if (!string.IsNullOrEmpty(element.name)) text.Append(" #").Append(element.name);
        if (element is TextElement t && !string.IsNullOrEmpty(t.text)) text.Append(" \"").Append(t.text).Append('"');
        if (!element.enabledSelf) text.Append(" (disabled)");
        text.AppendLine();
        foreach (var child in element.Children()) Dump(child, depth + 1, text);
    }
}
