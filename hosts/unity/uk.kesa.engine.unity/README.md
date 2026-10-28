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
   ```

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
