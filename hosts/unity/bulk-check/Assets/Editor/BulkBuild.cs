using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>사용법은 README. 플레이어는 <c>-kesaIl2cpp</c>를 주면 IL2CPP, 아니면 Mono로 빌드한다.</summary>
public static class BulkBuild
{
    public static void RunEditor()
    {
        try
        {
            BulkCheck.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("KESA BULK FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static void BuildPlayer()
    {
        bool il2cpp = Array.IndexOf(Environment.GetCommandLineArgs(), "-kesaIl2cpp") >= 0;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        EditorSceneManager.SaveScene(scene, "Assets/Empty.unity");
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Empty.unity" }, "Build/bulk.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("KESA BULK BUILD: " + report.summary.result + ", errors " + report.summary.totalErrors);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
