# uk.kesa.engine.unity

Kesa 엔진의 Unity 호스트 (spec-gaps G323·G328·G347). 모드의 UXML 화면을 UI Toolkit으로 그린다.

## 쓰는 법

1. 저장소 루트에서 `dotnet build`를 한다. `hosts/unity/Kesa.Engine.Unity.Plugins.csproj`가 커널·경계·번역·UI 해석과 Linguini의 DLL을 이 패키지의 `Plugins/`로 복사한다. 복사본은 git에 올라가지 않는다.
2. 게임 프로젝트의 `Packages/manifest.json`에 적는다:
   ```json
   "uk.kesa.engine.unity": "file:<저장소>/hosts/unity/uk.kesa.engine.unity"
   ```
3. 코드에서:
   ```csharp
   var mods = LoadedMods.Load(modsRoot, languages: new[] { "ko", "en" });
   // mods.Diagnostics·mods.Disabled: 오류가 난 모드는 빠진 채로 온다. 보일지, 멈출지는 게임이 정한다
   var match = mods.NewMatch(seed: 1);   // 새 게임마다 이것만 다시
   var session = await match.Host.Open("me");
   var renderer = new UiToolkitRenderer(uiDocument.rootVisualElement, mods.Assets);
   var ui = new UiInterpreter(session, renderer, mods.Format, mods.Assets.LoadText);
   var read = UxmlReader.Read("screen.uxml", File.ReadAllText(path));
   await ui.MountAsync(read.Document!, new Dictionary<string, BridgeValue> { [read.Document!.SeatParameter!] = seatEntity });
   // 프레임마다: await ui.RefreshAsync();
   // ESC: if (!await ui.Windows.Escape()) 게임의 메뉴를 연다
   // 화면을 여러 해석기로 나눠도 창을 함께 겹칠 것은 렌더러 하나에 올린다(그 렌더러의 해석기들은 창 목록 하나를 쓴다, G354)
   ```

## 화면이 갱신되는 방식

호스트는 게임 규칙을 모른다. 화면에 보일 값은 모드의 순수 함수(화면 함수, 모더 안내서 8장)가 만들고, 호스트는 UXML이 묶은 대로 그 함수를 불러 결과를 그린다. 이 일은 `UiInterpreter`가 하며, 게임은 프레임마다 `RefreshAsync()`만 부른다.

1. **올릴 때(`MountAsync`):** 해석기가 바인딩마다 `session.Call(키, 함수, 인자)`를 부르고 결과를 요소에 적는다. 이때 커널은 그 호출이 무엇을 읽었는지(엔티티, `query` 목록, 틱)를 그 키의 **영수증**으로 둔다(G244·G246).
2. **프레임마다(`RefreshAsync`):**
   - `session.Changed()`에 "읽은 곳이 바뀐 키"를 묻는다. 그 키의 바인딩만 다시 부르고, 나머지는 그대로 둔다.
   - `session.Version()`(세계 도장)이나 `session.UiVersion()`(이 연결의 UI 기록 도장)이 움직였으면 보이는 버튼의 `Check`를 다시 해서 회색 여부를 맞춘다(G342·G345).
   - 창의 보임과 겹침 순서를 맞춘다(G354).
3. **누를 때:** `on-click`의 커맨드는 `Deliver`, 화면 동작은 `Act`로 간다. 결과는 `ActionResult` 이벤트로 오고, 해석기가 바로 한 번 `RefreshAsync`를 해서 바뀐 값을 그린다. 다른 연결이 바꾼 것은 그 연결의 다음 프레임 갱신이 반영한다.

**왜 영수증인가:** 경계(`IHostSession`) 너머는 지금은 같은 프로세스이지만, 별도 프로세스나 다른 엔진이 될 수 있다(G324). 프레임마다 모든 화면 함수를 경계 너머로 부르면 비싸다. 그래서 "무엇을 다시 불러야 하나"를 한 번에 묻는다. 세계가 그대로면 `Changed()`는 비어 있고, 그 프레임에 하는 일은 묻는 것 세 번뿐이다. 영수증 안이 어떻게 생겼는지 호스트는 모른다. 커널이 나중에 더 잘게 나눠도 호스트 코드는 그대로다.

**호스트가 신경 쓸 것:**

- 게임 상태는 커맨드(`Deliver`·`Submit`)로만 바뀐다. 화면 요소에 값을 직접 쓰면 다음 갱신 때 덮인다.
- 화면 동작이 낸 `submit`이 거절돼도 `BridgeResult.Succeeded`는 참이다. 실패는 `Deliveries`에만 있으니 `ActionResult`에서 본다(G283).
- 부르다 실패한 바인딩은 직전 값을 유지하고 `UiInterpreter.Errors`에 쌓인다(G341). 비우는 것은 `ClearErrors`뿐이다.

## 판 위 그림

모드의 `*.look.xml`(그림 정의)과 `*.space.xml`(공간)은 `LoadedMods.Load`가 함께 읽는다(`mods.Looks`, 읽다 난 문제는 `mods.Looks.Problems`). 그리는 것은 `GameObjectLookRenderer`다.

```csharp
var renderer = new GameObjectLookRenderer(boardTransform, mods.Assets);
var board = match.Board(session, renderer);
// 프레임마다: await board.RefreshAsync();   그리다 난 문제는 board.Stage.Problems
// 누를 때(패널이 받지 않았으면):
//   var ray = camera.ScreenPointToRay(mouse);
//   await board.InputAsync(LookBoard.Primary, renderer.KeysAt(ray), renderer.PlaceAt(board.Stage, ray));
```

- 엔티티 물체(`WorldObjects`)와 표시(`WorldMarks`)를 받아 물체마다 GameObject 하나를 `boardTransform` 아래에 만든다. 물체는 공간 단위(`grid`의 `cell`)만큼 키우므로 그 안의 `size`·`offset`은 칸 단위다.
- `Sprite`는 SpriteRenderer이고 그림의 긴 변이 1이다. `layer`는 조상 것을 더해 `sortingOrder`가 된다. 그림을 못 읽거나 그림 정의가 없으면 보라·검정 체크무늬다.
- `Model`은 glTF(`.glb`·`.gltf`)를 glTFast 6.20.0으로 읽어(G391·G433) 경로마다 한 번 만든 원본을 복제한다. 원본은 판 아래 꺼진 `kesa-models`에 있다. 다 읽을 때까지 비어 있고 못 읽으면 보라색 상자다. 다른 형식은 `IModelLoader`를 `AddModelLoader`로, 그 확장자를 `LookKinds.AddModelFormat`으로 더한다(`GameObjectLookRenderer.StandardKinds()`가 glTF를 등록한 바탕). 모델은 미리 읽기에 들지 않는다. `KeysAt`은 스프라이트와 메시(글 빼고)의 경계 상자로 재므로 모델도 눌린다. `Attach`(G435)는 복제본 안에서 이름이 같은 첫 물체(넓이 우선) 아래로 옮겨지고, 원본을 바꾸거나 지울 때는 먼저 `Model` 아래로 내려 함께 지워지지 않게 한다. 색(G436)은 `tint`나 `Paint`가 있을 때만 복제본의 재질을 복사해 기본 색(`baseColorFactor`·`_BaseColor`·`_Color` 중 있는 것)에 곱한다. 원본 재질은 그대로다. 애니메이션(G434)은 glTFast가 옛 `Animation`용으로 가져온 클립을 `State`마다 따로 단 이름으로 걸고, 시간·가중치를 손으로 넣어 `Sample()`한다(Mecanim 클립은 빌드에서 만들어지지 않음). 시간은 `board.Stage.Advance(밀리초)`가 렌더러의 `Advance`로 넘긴다. 패키지는 내장 모듈 `com.unity.modules.animation`에 기댄다.
- `Text`는 내장 글꼴(`LegacyRuntime.ttf`)의 TextMesh이고 가운데 맞춤이다(G427). 묶인 값은 `Mods.Format`(화면 UXML과 같은 번역기)으로 글이 된다. 눌리지 않는다.
- 게임의 요소 종류: `LookKinds.Standard()`에 `LookElementKind`를 더해 `LoadedMods.Load(…, lookKinds:)`로 넘기고, 같은 이름으로 `renderer.AddKind(이름, assets => new 내View())`(`ILookElementView`)를 더한다. 공간 종류는 `SpaceKind`를 `LookKinds`에 더한다.
- 누름은 입력 이름(`LookBoard.Primary`·`Secondary`, G416)과 함께 넘긴다. 광선이 지나는 물체를 위에 그린 것부터 찾고(`KeysAt`), 광선이 닿는 공간의 자리를 판 기준으로 되돌린다(`PlaceAt`, `grid`의 역계산). `InputAsync`는 `click`이 있는 표시를 먼저 보고(주 누름만), 없으면 엔티티들과 자리를 경계의 `WorldInput`에 넘겨 모드의 `@world_input`이 받게 한다(G419·G422). 키·단추를 입력 이름으로 바꾸는 것은 `ModInputs`다(G417·G423): `new ModInputs(mods.Inputs)`가 `primary`·`secondary`와 모드의 `.input.xml` 선언마다 Input System 액션을 만들고, 프레임마다 `PressedThisFrame()`이 눌린 이름을 준다(데모는 이것을 커서 자리와 함께 `InputAsync`에 넘김). 패키지는 `com.unity.inputsystem` 1.18.0에 기대므로 프로젝트의 Active Input Handling이 Input System Package나 Both여야 한다. 핸들러가 선언에 없는 입력을 받으면 `mods.Inputs.CheckHandlers(await session.WorldInputs())`가 문제로 주고(데모는 경고로 찍음, G424), `Match.Board`는 표시의 `inputs=`를 같은 기준으로 `Stage.Problems`에 알린다. 플레이어 키 바꾸기는 `ModInputs.Actions`에 Input System의 다시 묶기와 `SaveBindingOverridesAsJson`을 쓴다. 키 누름 자체는 창 없는 확인에서 흉내 내지 못해(편집 모드에서 액션이 갱신되지 않음) `SmokeTest`는 바인딩이 키로 풀리는지만 본다.
- 카메라(G440~G442·G445): `mods.Looks.Views[이름]`을 `BoardCameras.Standard().Start(camera, 판 Transform, view)`에 넘기면 리그(`top`·`tilted`)가 카메라를 놓는다. 끌기는 `Pan(이전 화면 점, 지금 화면 점)`, 확대는 `Zoom(눈금)`이고 어느 단추·휠을 쓸지는 게임이 정한다(데모는 가운데 단추 끌기, 휠, 패널 위는 빼고). 게임의 리그: `LookKinds`에 `ViewRig`(속성 규칙)를, `BoardCameras.Add(이름, …)`에 `BoardCamera`를 함께 더한다.
- 연출(G429·G430·G437): `Match.Board`는 모드의 `presentation`을 따른다. `staged`면 효과 단계뿐 아니라 그 뒤의 옮기기·퇴장이 끝날 때까지 `board.Holding`이 참이다(`LookStage.Busy`). `staged`면 `ui.Hold = board`로 화면도 붙잡고, 붙잡는 동안(`board.Holding`) 봇은 쉬게 하고, 시간은 `board.Advance(밀리초)`로 넘긴다(단계를 넘긴다). 붙잡는 동안 판·UXML 누름은 건너뛰기가 된다.
- 효과(G425·G426): `board.RefreshAsync()`가 새 효과도 받아 띄운다. 흐려지고 떠오르고 옮겨 가게 하려면 프레임마다 `board.Advance(밀리초)`를 부른다(무대의 시간과 staged 단계를 함께 넘김, 데모는 `Time.deltaTime`에 Playback Speed를 곱함). 투명도는 렌더러가 요소의 색에 곱한다(`ILookElementView.SetOpacity`, 기본은 그대로 둠).
- 그림은 처음 보일 때 다른 스레드에서 읽고(G392), 다 읽을 때까지 그 요소는 비어 있다(보이지 않고 눌리지 않음). 텍스처는 판 GameObject에 붙은 `LookPicturePump`가 프레임마다 주 스레드에서 만든다. 같은 파일은 한 번 읽어 나눠 쓴다. 로딩 화면은 `renderer.Preload(mods.Looks, 그림 이름들)`을 기다리면 그 그림이 처음부터 보인다. 프레임이 돌지 않는 편집 모드 확인에서는 `LoadingPictures`가 거짓이 될 때까지 `FinishPictures()`를 부른다.

## 스타일시트 (USS)

- 모드의 `<Style src>`는 해석기가 `files`(`mods.Assets.LoadText`)로 모드 폴더에서 읽고 해석해 렌더러에 넘긴다. 렌더러는 Unity 내부 `StyleSheetBuilder`를 리플렉션으로 불러 진짜 `StyleSheet`을 만들어 붙인다(G359, `Runtime/UssBuilder.cs`·`UssStyleSheets.cs`, OneJS에서 고쳐 옮김, `THIRD_PARTY_NOTICES.md`). 캐스케이드·`:hover`·선택자·`var()`·`transition`은 Unity가 한다.
- 게임 빌드 안의 모양(Unity 에셋 `StyleSheet`)은 경로로 찾지 않는다. 게임 코드가 화면 맨 바깥(`UIDocument.rootVisualElement`)에 붙인다(G362). 명시도가 같으면 모드 시트가 이긴다.
- UI Builder가 쓰는 `project://…`·`/Assets/…`·`..` 경로는 모드 경로가 아니라서 찾지 않고 진단만 낸다(G365). 모드 UXML·USS는 모드 폴더 안의 경로로 쓴다.
- USS의 잘못은 화면을 막지 않고 `UiInterpreter.Errors`에 담긴다(G364).
- 공개되지 않은 API라 Unity를 올리면 깨질 수 있다. `SmokeTest`가 엔진의 속성 목록과 Unity의 표, 같은 글을 Unity 임포터가 만든 시트와 우리 시트, 런타임 패널의 실제 적용을 비교하므로 Unity를 올리면 먼저 이것을 돌린다.
- IL2CPP: 리플렉션으로 부르는 타입은 `Runtime/link.xml`에 있고 `Editor/UssLinker`가 빌드 때 링커에 넘긴다(Unity는 패키지의 link.xml을 읽지 않는다). IL2CPP 빌드로는 아직 돌려 보지 않았다.

## 확인

창 없이 돌려 보는 방법 (Unity 6000.3):

```
Unity.exe -batchmode -nographics -projectPath <패키지를 manifest에 적은 프로젝트> \
  -executeMethod Kesa.Engine.Unity.Editor.SmokeTest.Run -kesaMods <저장소>/samples/skirmish -logFile -
```

로그에 `KESA SMOKE TEST: OK`가 나오면 skirmish 화면이 Unity의 요소 트리로 맞게 만들어졌고, 창 배치와 런타임 USS(`UssCheck`), 판 위 그림(skirmish `looks/`의 표시가 SpriteRenderer로 칸 자리에, 모르는 그림은 대체 그림)도 맞는 것이다. 에디터가 그 프로젝트를 열고 있으면 잠금 때문에 바로 끝나므로, 프로젝트를 다른 폴더로 복사하고 `manifest.json`의 패키지 경로를 절대 경로로 바꿔 돌린다(USS 확인은 그 프로젝트의 `Assets/KesaUssCheck`에 시트를 잠시 만들었다 지운다).

## 데모 프로젝트

`hosts/unity/demo`는 이 패키지를 `file:`로 가져다 쓰는 Unity 프로젝트다(`.gitignore`는 Unity 프로젝트용 템플릿). 먼저 저장소 루트에서 `dotnet build`를 한 뒤 Unity 6000.3으로 열고 `Assets/Scenes/Demo.unity`를 재생하면 `samples/skirmish`가 같은 프로세스에서 돌며 UXML 화면이 뜬다(`KesaDemo`). 창 없이 확인: `-executeMethod KesaDemoSetup.Check`(편집 모드는 프레임이 돌지 않아 `KesaDemo.Pass(밀리초)`로 시간을 넘기고 `Finish()`로 staged가 다 따라잡게 한다). 연출을 눈으로 따라가려면 재생 중 `KesaDemo`의 Playback Speed를 내린다(0.25면 4배 느림). 카메라가 비춘 판을 PNG로 보려면 `-nographics` 없이 `-executeMethod KesaDemoSetup.Shot -kesaShot <png 경로>`(패널은 들어가지 않는다).

## 아직 없는 것

- USS의 `@import`, `url()`로 그림이 아닌 파일(폰트 등) 읽기.
- 인라인 `style` 속성, `Include` 밖의 여러 이벤트, 키보드 입력.
