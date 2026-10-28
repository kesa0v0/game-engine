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
   var ui = new UiInterpreter(session, renderer, mods.Format, mods.Assets.LoadUxml);
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

## 확인

창 없이 돌려 보는 방법 (Unity 6000.3):

```
Unity.exe -batchmode -nographics -projectPath <패키지를 manifest에 적은 프로젝트> \
  -executeMethod Kesa.Engine.Unity.Editor.SmokeTest.Run -kesaMods <저장소>/samples/skirmish -logFile -
```

로그에 `KESA SMOKE TEST: OK`가 나오면 skirmish 화면이 Unity의 요소 트리로 맞게 만들어진 것이다.

## 데모 프로젝트

`hosts/unity/demo`는 이 패키지를 `file:`로 가져다 쓰는 Unity 프로젝트다(`.gitignore`는 Unity 프로젝트용 템플릿). 먼저 저장소 루트에서 `dotnet build`를 한 뒤 Unity 6000.3으로 열고 `Assets/Scenes/Demo.unity`를 재생하면 `samples/skirmish`가 같은 프로세스에서 돌며 UXML 화면이 뜬다(`KesaDemo`). 창 없이 확인: `-executeMethod KesaDemoSetup.Check`.

## 아직 없는 것

- 스타일시트(`<Style src>`): Unity에 런타임에 USS 글을 읽는 공개 API가 없어 게임이 `ModFolderAssets`에 읽기를 꽂아야 한다.
- 인라인 `style` 속성, `Include` 밖의 여러 이벤트, 키보드 입력.
