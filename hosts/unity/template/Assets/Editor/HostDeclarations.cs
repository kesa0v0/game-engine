#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 호스트 함수 선언 파일 <c>host/game.host.json</c>(<c>engine check --host</c>용)을 <see cref="GameHost.NewHost"/>의 등록에서 만든다.
/// 메뉴 <c>Game/Write Host Declarations</c> 또는 <c>-batchmode -nographics -executeMethod HostDeclarations.Write</c>.
/// </summary>
public static class HostDeclarations
{
    private static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "host", "game.host.json"));

    private static string Expected() => GameHost.NewHost().ExportDeclarations();

    [MenuItem("Game/Write Host Declarations")]
    public static void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Expected() + "\n");
        Debug.Log("Host declarations written: " + FilePath);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// <summary>파일이 등록과 같은지 본다(파일이 없으면 건너뛴다).</summary>
    internal static void Verify()
    {
        if (!File.Exists(FilePath)) return;
        string Norm(string text) => text.Replace("\r\n", "\n").Trim();
        if (Norm(File.ReadAllText(FilePath)) != Norm(Expected()))
            throw new InvalidOperationException("host/game.host.json differs from the registered host functions; run the menu Game/Write Host Declarations. Expected: " + Expected());
    }
}
