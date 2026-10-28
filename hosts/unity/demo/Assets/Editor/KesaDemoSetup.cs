#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    /// <summary>batch 모드용: 장면이 열리고(없는 스크립트 없이) 데모가 skirmish 화면을 올리는지 본다. 틀리면 종료 코드 1.</summary>
    public static void Check()
    {
        int code = 0;
        try
        {
            Verify().GetAwaiter().GetResult();
            Debug.Log("KESA DEMO CHECK: OK");
        }
        catch (Exception e)
        {
            Debug.LogError("KESA DEMO CHECK FAILED: " + e);
            code = 1;
        }
        EditorApplication.Exit(code);
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
