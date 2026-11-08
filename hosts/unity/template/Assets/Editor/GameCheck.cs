#nullable enable
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>창 없는 확인: <c>-batchmode -nographics -executeMethod GameCheck.Run</c>(<c>-quit</c>은 붙이지 않는다). 성공하면 로그에 <c>GAME CHECK: OK</c>, 틀리면 종료 코드 1.</summary>
public static class GameCheck
{
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            var host = UnityEngine.Object.FindFirstObjectByType<GameHost>();
            if (host == null) throw new InvalidOperationException("the scene has no GameHost (a missing script?)");
            host.Begin();
            HostDeclarations.Verify();
            Debug.Log("GAME CHECK: OK");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("GAME CHECK FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }
}
