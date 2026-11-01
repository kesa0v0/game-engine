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
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// 데모: 저장소의 <c>samples/skirmish</c>를 같은 프로세스에서 돌리고 그 UXML 화면을 UIDocument에 올린다 (spec-gaps G323·G347).
/// 프레임마다 영수증이 바뀐 곳만 다시 그린다. 판 위 표시(<c>looks/</c>)는 이 GameObject 아래 <c>Board</c>에 그린다 (G412).
/// </summary>
public sealed class KesaDemo : MonoBehaviour
{
    [Tooltip("모드 폴더. 프로젝트 폴더 기준 상대 경로")]
    [SerializeField] private string modsFolder = "../../../samples/skirmish";

    [Tooltip("켤 모드 id. 목록 순이 로드 순서다. 비우면 모드 폴더의 모드를 다 켠다 (2D만 보려면 skirmish 하나)")]
    [SerializeField] private string[] enabledMods = new string[0];

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

    [Tooltip("판 연출(효과·옮기기·모델 애니메이션)의 재생 속도. 0.25면 4배 느리게 (눈으로 확인할 때)")]
    [SerializeField, Range(0.05f, 2f)] private float playbackSpeed = 1f;

    private float _playbackCarry;

    [SerializeField] private string[] languages = { "ko", "en" };

    [Tooltip("판을 볼 View 이름 (모드의 .view.xml)")]
    [SerializeField] private string view = "main";

    private UiInterpreter? _ui;
    private LookBoard? _board;
    private GameObjectLookRenderer? _boardRenderer;
    private BoardCamera? _camera;
    private Vector2? _panFrom;
    private ModInputs? _inputs;
    private VisualElement? _root;
    private int _boardProblems;
    private BotSeat? _bot;
    private float _botNext;
    private bool _wasHolding;
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
        var mods = LoadedMods.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", modsFolder)), languages,
            enabled: enabledMods.Length == 0 ? null : enabledMods);
        foreach (var d in mods.Diagnostics)
            if (d.Severity != Severity.Info) Debug.LogWarning("Kesa mods: " + d);
        foreach (var p in mods.Looks.Problems) Debug.LogWarning("Kesa looks: " + p);
        foreach (var p in mods.Inputs.Problems) Debug.LogWarning("Kesa inputs: " + p);
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
        foreach (var p in mods.Inputs.CheckHandlers(await session.WorldInputs())) Debug.LogWarning("Kesa inputs: " + p);

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

        var board = new GameObject("Board");
        board.transform.SetParent(transform, false);
        _boardRenderer = new GameObjectLookRenderer(board.transform, mods.Assets);
        _board = game.Board(session, _boardRenderer);
        // staged면 효과를 보이는 동안 화면도 붙잡는다 (G430)
        ui.Hold = _board;
        _inputs = new ModInputs(mods.Inputs);
        _root = root;
        // 모델(PBR 재질)은 빛이 없으면 거의 검다
        if (FindFirstObjectByType<Light>() == null)
        {
            var sun = new GameObject("Kesa Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
        var camera = Camera.main;
        if (camera != null)
        {
            if (!mods.Looks.Views.TryGetValue(view, out var declared))
                Debug.LogWarning("Kesa: no .view.xml declares the view '" + view + "' (views: " + string.Join(", ", mods.Looks.Views.Keys) + ")");
            else if ((_camera = BoardCameras.Standard().Start(camera, board.transform, declared)) == null)
                Debug.LogWarning("Kesa: the view '" + view + "' uses the rig '" + declared.Rig + "', which this demo does not drive");
        }
        await RefreshBoard();
    }

    /// <summary>패널 요소가 받지 않은 누름을 판에 넘긴다 (G395·G419·G422: 표시가 먼저, 다음 엔티티, 다음 빈 자리). 공개는 확인용.</summary>
    public async Task PressBoard(string input, Vector3 screenPoint)
    {
        var camera = Camera.main;
        if (_board == null || _boardRenderer == null || camera == null) return;
        if (OverPanel(screenPoint)) return;
        var ray = camera.ScreenPointToRay(screenPoint);
        var result = await _board.InputAsync(input, _boardRenderer.KeysAt(ray), _boardRenderer.PlaceAt(_board.Stage, ray));
        if (result != null && !result.Succeeded) Debug.LogWarning("Kesa board press -> " + result);
        if (_ui != null) await _ui.RefreshAsync();
    }

    private async Task RefreshBoard()
    {
        if (_board == null) return;
        await _board.RefreshAsync();
        var problems = _board.Stage.Problems;
        for (; _boardProblems < problems.Count; _boardProblems++) Debug.LogWarning("Kesa board: " + problems[_boardProblems]);
    }

    /// <summary>판 그림을 다 읽을 때까지 기다린다 (확인용: 편집 모드에서는 프레임이 돌지 않음, G392).</summary>
    public async Task WaitPictures()
    {
        if (_boardRenderer == null) return;
        var until = DateTime.UtcNow.AddSeconds(30);
        // 모델(glTFast)은 에디터 프레임을 타고 읽으므로 막지 않고 양보한다
        while (_boardRenderer.LoadingPictures && DateTime.UtcNow < until)
        {
            _boardRenderer.FinishPictures();
            await Task.Yield();
        }
        _boardRenderer.FaceCamera();
    }

    /// <summary>판의 시간(효과·옮기기·연출 단계)을 넘긴다 (확인용: 편집 모드에서는 프레임이 돌지 않음).</summary>
    public void Pass(long milliseconds) => _board?.Advance(milliseconds);

    /// <summary>봇이 할 일을 기다리지 않고 모두 하고 화면을 다시 그린다 (확인용).</summary>
    public async Task Step()
    {
        if (_bot != null)
            while (await _bot.Run() > 0) { }
        if (_ui != null) await _ui.RefreshAsync();
        await RefreshBoard();
    }

    /// <summary>staged는 화면이 따라잡을 때까지 기다리므로(G437) 시간을 넘겨 다 따라잡게 한다 (확인용).</summary>
    public async Task Finish()
    {
        while (_board?.Holding == true)
        {
            _board.Advance(1000);
            if (_ui != null) await _ui.RefreshAsync();
            await RefreshBoard();
        }
    }

    // 가운데 버튼으로 끌어 옮기고 휠로 확대한다: 왼쪽은 고르기, 오른쪽은 살피기라 비켜 둔다
    private void MoveCamera()
    {
        var mouse = Mouse.current;
        if (_camera == null || mouse == null) return;
        var at = mouse.position.ReadValue();
        if (mouse.middleButton.wasPressedThisFrame && !OverPanel(at)) _panFrom = at;
        if (!mouse.middleButton.isPressed) _panFrom = null;
        if (_panFrom is Vector2 from && from != at)
        {
            _camera.Pan(from, at);
            _panFrom = at;
        }
        float wheel = mouse.scroll.ReadValue().y;
        // 휠 한 눈금의 값이 플랫폼·설정마다 달라(1 또는 120) 방향만 쓴다
        if (wheel != 0 && !OverPanel(at)) _camera.Zoom(Mathf.Sign(wheel));
    }

    private bool OverPanel(Vector2 screenPoint)
    {
        var panel = _root?.panel;
        if (panel == null) return false;
        var picked = panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPoint.x, Screen.height - screenPoint.y)));
        return picked != null && picked != _root && picked != panel.visualTree;
    }

    private void OnDestroy() => _inputs?.Dispose();

    private async void Update()
    {
        if (_ui == null || _refreshing) return;
        _refreshing = true;
        try
        {
            // 창이 ESC를 받지 않으면(거짓) 게임이 자기 메뉴를 열 자리다
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) await _ui.Windows.Escape();
            MoveCamera();
            // 키 입력도 커서 아래로 간다 (G416: 가리킨 유닛에 F)
            if (_inputs != null && Pointer.current != null)
                foreach (var input in _inputs.PressedThisFrame()) await PressBoard(input, Pointer.current.position.ReadValue());
            // 연출을 붙잡는 동안 봇은 쉬고, 놓은 뒤에도 한 박자 쉰다 (G399)
            bool holding = _board?.Holding == true;
            if (_wasHolding && !holding) _botNext = Time.time + botDelay;
            _wasHolding = holding;
            if (_bot != null && _board?.Holding != true && Time.time >= _botNext && await _bot.Run() > 0) _botNext = Time.time + botDelay;
            await _ui.RefreshAsync();
            await RefreshBoard();
            // 느리게 돌리면 한 프레임이 1밀리초 밑일 수 있어 남은 것을 이어 센다
            _playbackCarry += Time.deltaTime * 1000f * playbackSpeed;
            long step = (long)_playbackCarry;
            _playbackCarry -= step;
            _board?.Advance(step);
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
