#nullable enable
using System;
using System.IO;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Unity;
using UnityEngine;

/// <summary>
/// 모드를 불러와 판을 연다. 게임 규칙은 모드(<c>Assets/StreamingAssets/mods/</c>)에 두고 C#에 넣지 않는다.
/// 자리에 앉히기, 화면(UXML), 판 위 그림, 카메라, 틱 넘기기를 붙이는 예는 엔진의 <c>hosts/unity/demo/Assets/Scripts/KesaDemo.cs</c>다.
/// </summary>
public sealed class GameHost : MonoBehaviour
{
    public const string ModId = "game";
    public const int Seed = 1;
    private const string ModsFolder = "mods";
    private static readonly string[] Languages = { "ko", "en" };

    public LoadedMods? Mods { get; private set; }
    public Match? Game { get; private set; }

    /// <summary>호스트 함수를 여기서 등록한다(<c>host.For(ModId).Reads(...)</c>). 바꾸면 <c>host/game.host.json</c>을 다시 만든다.</summary>
    public static HostFunctions NewHost() => new HostFunctions();

    public static LoadedMods LoadMods() =>
        LoadedMods.Load(Path.Combine(Application.streamingAssetsPath, ModsFolder), Languages, lookKinds: GameObjectLookRenderer.StandardKinds(), host: NewHost());

    private void Start()
    {
        try
        {
            Begin();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public void Begin()
    {
        var mods = LoadMods();
        Mods = mods;
        foreach (var d in mods.Diagnostics)
            if (d.Severity != Severity.Info) Debug.LogWarning("Kesa mods: " + d);
        foreach (var p in mods.Looks.Problems) Debug.LogWarning("Kesa looks: " + p);
        if (mods.Disabled.Count > 0) throw new InvalidOperationException("mods disabled by errors: " + string.Join(", ", mods.Disabled));
        Game = mods.NewMatch(Seed);
        Debug.Log("Kesa: the match is open");
    }
}
