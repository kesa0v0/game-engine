#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Ui;
using Kesa.Engine.Unity;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 데모: 저장소의 <c>samples/skirmish</c>를 같은 프로세스에서 돌리고 그 UXML 화면을 UIDocument에 올린다 (spec-gaps G323·G347).
/// 프레임마다 영수증이 바뀐 곳만 다시 그린다.
/// </summary>
public sealed class KesaDemo : MonoBehaviour
{
    [Tooltip("모드 폴더. 프로젝트 폴더 기준 상대 경로")]
    [SerializeField] private string modsFolder = "../../../samples/skirmish";

    [Tooltip("판을 시작하는 커맨드 (없으면 비움)")]
    [SerializeField] private string startCommand = "skirmish.Start";

    [Tooltip("화면 UXML. 모드 폴더 안의 경로 (나중에 로드된 모드가 이김)")]
    [SerializeField] private string screen = "ui/screen.uxml";

    [Tooltip("이 연결이 앉을 진영의 Def")]
    [SerializeField] private string seatDef = "skirmish:Red";

    [SerializeField] private string connection = "me";

    [Tooltip("AI 봇이 앉을 진영의 Def (없으면 비움)")]
    [SerializeField] private string botSeatDef = "skirmish:Blue";

    [Tooltip("그 진영을 맡을 모드의 @bot")]
    [SerializeField] private string botName = "skirmish.skirmisher";

    [Tooltip("봇이 한 수를 두고 다음 수까지 기다리는 초 (수를 눈으로 따라가게)")]
    [SerializeField] private float botDelay = 0.6f;

    [SerializeField] private string[] languages = { "ko", "en" };

    private UiInterpreter? _ui;
    private BotSeat? _bot;
    private float _botNext;
    private bool _refreshing;

    /// <summary>이 연결의 세션 (확인용).</summary>
    public IHostSession? Session { get; private set; }

    private async void Start()
    {
        var document = GetComponent<UIDocument>();
        try
        {
            await Begin(document.rootVisualElement);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>판을 열고 화면을 <paramref name="root"/>에 올린다.</summary>
    public async Task Begin(VisualElement root)
    {
        var mods = LoadedMods.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", modsFolder)), languages);
        foreach (var d in mods.Diagnostics)
            if (d.Severity != Severity.Info) Debug.LogWarning("Kesa mods: " + d);
        if (mods.Disabled.Count > 0) Debug.LogWarning("Kesa mods disabled by errors: " + string.Join(", ", mods.Disabled));
        var game = mods.NewMatch(1);
        var world = game.Host.World;
        if (startCommand.Length > 0 && !world.Submit("host", startCommand, new Dictionary<string, Value>()).Succeeded)
            throw new InvalidOperationException("the start command '" + startCommand + "' was refused");

        var seat = world.SeatChoices().FirstOrDefault(id => world.DefOf(id) == seatDef);
        if (seat.Value == 0) throw new InvalidOperationException("no seat with the Def '" + seatDef + "'");
        await game.Host.Seat(seat.Value, connection);
        var session = await game.Host.Open(connection);
        Session = session;

        // 빈자리는 질문에 아무도 답하지 않아 판이 멈추므로 봇을 앉힌다 (G366)
        if (botSeatDef.Length > 0)
        {
            var botSeat = world.SeatChoices().FirstOrDefault(id => world.DefOf(id) == botSeatDef);
            if (botSeat.Value == 0) throw new InvalidOperationException("no seat with the Def '" + botSeatDef + "' for the bot");
            await game.Host.Seat(botSeat.Value, "bot", ai: true);
            var botSession = await game.Host.Open("bot");
            _bot = new BotSeat(botSession, await botSession.Bot(botName, botSeat.Value), maxActs: 1);
        }

        // 게임이 주는 기본 테마(Assets/Resources/KesaDemo.uss)는 맨 바깥에, 화면의 모양은 모드의 USS가 준다 (G362)
        var style = Resources.Load<StyleSheet>("KesaDemo");
        if (style != null) root.styleSheets.Add(style);
        var renderer = new UiToolkitRenderer(root, mods.Assets);
        var ui = new UiInterpreter(session, renderer, mods.Format, mods.Assets.LoadText);
        string? uxml = mods.Assets.LoadText(screen);
        if (uxml == null) throw new FileNotFoundException("the screen '" + screen + "' was not found in the mods");
        var read = UxmlReader.Read(screen, uxml);
        if (!read.Succeeded) throw new InvalidOperationException(string.Join("\n", read.Problems));
        var document = read.Document!;
        var arguments = new Dictionary<string, BridgeValue>();
        if (document.SeatParameter != null) arguments[document.SeatParameter] = new BridgeEntity(seat.Value, world.DefOf(seat));
        var mounted = await ui.MountAsync(document, arguments);
        if (mounted.Problems.Count > 0) throw new InvalidOperationException(string.Join("\n", mounted.Problems));
        ui.ActionResult += (name, result) =>
        {
            if (!result.Succeeded) Debug.LogWarning("Kesa: " + name + " -> " + result);
        };
        _ui = ui;
    }

    /// <summary>봇이 할 일을 기다리지 않고 모두 하고 화면을 다시 그린다 (확인용).</summary>
    public async Task Step()
    {
        if (_bot != null)
            while (await _bot.Run() > 0) { }
        if (_ui != null) await _ui.RefreshAsync();
    }

    private async void Update()
    {
        if (_ui == null || _refreshing) return;
        _refreshing = true;
        try
        {
            // 창이 ESC를 받지 않으면(거짓) 게임이 자기 메뉴를 열 자리다
            if (Input.GetKeyDown(KeyCode.Escape)) await _ui.Windows.Escape();
            if (_bot != null && Time.time >= _botNext && await _bot.Run() > 0) _botNext = Time.time + botDelay;
            await _ui.RefreshAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            _refreshing = false;
        }
    }
}
