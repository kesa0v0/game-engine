# 인계 메모

마지막 갱신: 2026-10-30. 브랜치 `spec-v0.5-scaffold`, 테스트 2876개 통과(커널 2439, CLI 42, 콘솔 호스트 101, 번역 65, 경계 73, UI 149, 결정 도구 7). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 10장 "아직 없는 것"도 고친다. 봇은 9장이다.

첫 실제 호스트는 Unity다(G323). 만든 것:

- 경계 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`(`LocalHost`)(G324~G329, G342, G346). 콘솔 호스트도 이 경계만 쓴다.
- UXML 해석 `src/Kesa.Engine.Ui`: 식 파서, UXML 읽기, 해석기, `IUiRenderer`. 경계만 참조한다(G330~G345). 시험은 `tests/Kesa.Engine.Ui.Tests`(시험용 메모리 렌더러, 진짜 `LocalHost`, skirmish 화면).
- Unity 패키지 `hosts/unity/uk.kesa.engine.unity`(`UiToolkitRenderer`, `ModFolderAssets`, 입구 `LoadedMods.Load`와 `mods.NewMatch`, G353)와 데모 프로젝트 `hosts/unity/demo`(씬 `Assets/Scenes/Demo.unity`, `KesaDemo`). 루트 `dotnet build`가 DLL을 패키지의 `Plugins/`로 복사한다(G347, 복사본은 git에 없음). 모드 오류는 판을 막지 않고 `Diagnostics`·`Disabled`로 온다. API 타입 이름에 `Kesa` 접두사를 붙이지 않는다.
- 설치된 Unity 6000.3.8f1(이 컴퓨터: `C:\Program Files\Unity\Hub\Editor\6000.3.8f1`)에서 컴파일과 창 없는 확인이 통과했다(`SmokeTest`, `KesaDemoSetup.Check`, 패키지 README). skirmish 화면이 실제 `VisualElement` 트리로 올라와 카드 6장, 번역된 유닛 이름, 회색 버튼까지 맞다.
- 사용자가 에디터에서 데모를 재생해 눈으로 확인했다(2026-10-29): 카드 배치, 선택 표시, 정보창 끌기·ESC, 확인 모달(뒤를 막음, 턴이 넘어가면 닫힘). 그 뒤 화면 모양을 모드 USS(`samples/skirmish/core/ui/screen.uss`, 카드에 `:hover` 더함)로 옮겼고 데모 게임의 `Assets/Resources/KesaDemo.uss`에는 `Label` 글자색만 남았다(기본 런타임 테마는 글자가 짙어 어두운 배경에서 안 보인다). 옮긴 뒤의 모양은 아직 눈으로 보지 않았다.

## 다음 할 일

**질문을 화면에 보이고 답받기(G380~G385, 구현함):** 데모에서 사람 빨강이 맞음 질문에 답할 길이 없어 판이 멈췄다. 층을 나눴다: 게임 규칙(`ask`) → 판단(모드 스크립트) → 그림(UXML·나중의 월드 표시는 판단 함수에 묶이기만) → 엔진 기본(아무도 안 그린 질문만).
- 엔진 기본 창(G386): 화면에 붙은 바인딩이 `questions("이름")`으로 읽지도, `on-click`이 응답 커맨드를 가리키지도 않은 열린 질문은 해석기(`UiInterpreter.Fallback.cs`)가 제목과 답 버튼 창으로 보이고 경고를 남긴다. 영수증이 읽은 이름을 들고(`CallResult.ReadsQuestion`) 경계 `UndrawnRequests()`가 거른다. 시험 `QuestionFallbackTests`. Unity에서 이 창의 모양(`kesa-question` 클래스)은 아직 눈으로 보지 않았다.
- `ask(..., name="종류")`는 필수(G382). 경계에서는 `BridgeRequest.Name`.
- `questions()`/`questions("이름")`은 이 연결이 답할 열린 질문 목록(G383). 화면 함수·`@ui_action`·`@bot`에서만 쓰고 게임 쪽 코드는 로드 오류(`ReadsQuestions` 표시가 퍼짐). 화면 함수 영수증이 질문 변화(`RequestsVersion`)를 본다.
- `respond(q, 커맨드, 인자…)`는 고른 질문 하나에 답한다(G384, `UiSubmission.Request`). `submit`은 그대로 엔진이 짝을 찾는다.
- 질문이 열리고 닫히면 전역 시계가 오른다(G385).
- skirmish: 카드가 `is_hit`에 `hit` 클래스와 hold·retreat 버튼 보임을 묶고 버튼이 `respond`한다. 봇은 `questions()`로 판단해 차례가 아닐 때 아무것도 적지 않는다. 시험 `SkirmishScreenTests.AHitUnitsCard…`, `QuestionsTests`.

**바로 다음(순서대로):**

1. 사용자가 에디터에서 데모를 재생해 본다: 빨강 유닛이 맞으면 카드가 붉어지고 hold·retreat가 뜨는지, 파랑 봇이 두는지.
2. **월드 표시 구현 중.** 순서: ① 커널·경계(`dotnet test`) ② 호스트 쪽 해석(Unity 없는 부분) ③ Unity 2D 스프라이트와 네모 칸으로 skirmish 판과 물러날 칸 클릭 ④ glTFast·`@world_fx`·붙잡고 재생. ①에서 한 것:
   - Def 함수 칸 `Look["T"]`·`Where["T"]`(G388·G400, `Place`는 모더의 흔한 타입 이름이라 버림). 화면 함수 등급으로 검사하고, `Look`은 글자만 적어도 된다. 게임 코드가 `x.Unit.look`처럼 그 칸을 읽으면 로드 오류(선언한 타입·컴포넌트를 거친 읽기만 셈).
   - 값 `look(이름, 키=값)`·`at([공간,] 값)`·`onto(엔티티[, 붙는 곳])`·`between(자리, 자리)`(G389·G394·G406, `on`은 데코레이터·`attach`는 컴포넌트 내장이라 `onto`). `Runtime/World.Look.cs`. 만드는 것은 화면 쪽 코드만이다(멈춘 흐름에 남으면 저장 못 함): 게임 코드·Cond·Action·wrap에서 쓰면 로드 오류(`FunctionValue.MakesLooks`). 화면에서 읽은 가려진 엔티티는 그대로 싣는다.
   - 경계 값 `BridgeLook`·`BridgeWhere`(At·Onto·Between)와 JSON(G402).
   - 지도에 올리는 칸은 이름이 정확히 `look`·`where`(G403). 둘 중 하나가 `None`이면 지금은 안 올림(G405).
   - 경계 `WorldObjects(since)`(전부 또는 도장 뒤 바뀐 것·빠진 것)와 `WorldObject(key, entity)`(G401·G407). 칸 함수 오류는 `BridgeWorldObject.Error`로 실림(호스트가 대체 그림, G392). 커널은 `WorldView.WorldEntities()`·`WorldObject(ui, id)`.
   - ①에서 skirmish가 쓰는 만큼은 끝났다. `@world_click("타입")`·`@world_click_space`(G393)는 ③에서 필요하면 더한다. 입력 상태 읽기 내장(G397, 이름 정해야 함)은 쓸 곳이 생길 때. `@world_fx`(G398)·`presentation=`(G399)은 ④. ②의 그리지 않는 해석은 `Kesa.Engine.Ui`의 `LookLibrary`(읽기)·`LookStage`(펼치기, `ILookRenderer`)·`LookKinds`(등록)에 있고 skirmish의 `looks/`가 시험에 쓰인다. Unity는 `LoadedMods.Looks`·`GameObjectLookRenderer`(SpriteRenderer, 물체를 공간 단위로 키움)·`Match.Board`(`LookBoard`)가 그리고 데모 `KesaDemo`가 `Board` 아래에 그린다. skirmish 유닛은 `look`·`where` 칸(`ui.py`의 `unit_look`, 진영 색은 `Party`의 `color` 상수)과 `looks/units.look.xml`로 판에 보이고, 표시를 누르면 `LookBoard.PressAsync`가 그 `click`을 `Act`로 낸다(Unity는 `KeysAt`). 데모 카메라 그림은 `KesaDemoSetup.Shot`으로 확인했다. 플레이 모드에서 사용자가 손으로 확인한 것: 왼쪽 단추로 고르기, 오른쪽 단추로 정보 창, 빈 칸을 눌러 앞·옆으로 이동(`I` 키는 아직). 데모 확인(`KesaDemoSetup.Check`)은 판 GameObject를 화면 좌표로 눌러 고르기·창·`inspect`·이동을 본다(키 장치만 뺌). 판의 칸 바닥 그림은 없다. **판 위 입력(G416~G420):** 클릭만이 아니라 이름 붙은 입력(내장 `primary`·`secondary`, 모드 입력은 호스트 파일 `.input.xml`에 이름과 기본 키, G417). 핸들러는 `@ui_action` 위에 쌓는 `@world_input("입력", "Def 타입")`이고 매개변수는 그 엔티티 하나. 경계 `WorldInput(입력, 커서 아래 엔티티들)`에서 커널(`World.Look.cs`의 `WorldInputAs`)이 나중 모드부터 `ActAs`를 해 보아 사전 조건을 통과한 첫 것에서 멈추고, 모두 막히면 아래 엔티티로, 끝내 없으면 처음 막힌 결과(G420). 호스트 쪽은 `LookBoard.InputAsync(입력, 열쇠들)`(표시의 `click`이 먼저, 주 누름만). 맡은 자리는 내장 `seats()`·`seats(타입)`(G421, `World.Seats.cs`, `questions()`와 같은 화면 쪽 규칙, 영수증이 배정 표를 봄). skirmish는 `inspect`를 `secondary`에, `press_unit`(내 유닛이면 고르기, 사거리 안 적이면 공격)을 `primary`에 묶었고 데모는 왼쪽·오른쪽 단추를 보낸다. 빈 자리(G422): 대상이 Def 타입이 아니면 공간 이름이고 핸들러는 자리 값만 받는다(`press_square(cell: list[int])`, 고른 유닛을 옆 칸으로 `Move`). 경계 `WorldInput(입력, 엔티티들, 자리)`, 호스트는 `LookStage.PlaceOnRay`(`SpaceLayout.TryPlaceOnRay`, `grid`의 역계산을 고정소수점으로)와 Unity `GameObjectLookRenderer.PlaceAt`(판 기준 좌표로 바꿈). 표시의 `inputs=`(G418): `MarkValue.Inputs`·`BridgeMark.Inputs`, 호스트는 `LookStage.ActionOf(열쇠, 입력)`(주 누름은 `click=`이 먼저, 둘 다면 `Problems`에 경고). 모드 입력의 키(G423): `.input.xml`의 `key="SHIFT + R"`(수정 키 둘까지, 키는 Input System `Key` 이름이나 `primary`·`secondary`)를 `Kesa.Engine.Ui`의 `InputLibrary`가 읽고(키 이름 목록은 호스트가 줌), Unity 패키지는 `com.unity.inputsystem` 1.18.0에 기대어 `ModInputs`가 액션으로 만든다(`PressedThisFrame`, 수정 키가 더 붙은 쪽만 냄은 `InputChord.IsCoveredBy`). 데모는 Active Input Handling을 Both로 바꾸고 눌린 이름을 커서 자리와 함께 보낸다. skirmish는 `I`로 `inspect`. 창 없는 확인은 바인딩이 키로 풀리는지까지만 본다(편집 모드에서 액션이 갱신되지 않음). 선언에 없는 입력 이름(G424): 경계 `WorldInputs()`(`BridgeWorldInput{Input, Target, Action, Mod}`)를 `InputLibrary.CheckHandlers`가 보고, 표시의 `inputs=`는 `LookStage`(입력 선언을 받았을 때)가 함수마다 한 번 경고. 그림 읽기(G392): Unity `LookPictures`가 파일을 다른 스레드에서 읽고 `LookPicturePump`(판 GameObject)가 프레임마다 텍스처를 건다. 읽는 동안 요소는 비어 있다. 미리 읽기는 `GameObjectLookRenderer.Preload(library, 그림 이름들)`. ③은 skirmish가 쓰는 만큼 끝났다. ④ 효과(G398·G425·G426) 커널·경계는 됨: `@world_fx(신호)`(`WorldFxDecl`, 매개변수는 신호 값 이름), `fx(그림, at=)`(`FxValue`), `World.Fx.cs`가 효과 함수가 받는 신호만 묶음 번호와 함께 최근 256개 기록(`RecordFx`, 신호 처리 뒤), 경계 `WorldFx(since)`(처음 0은 도장만, 도장은 1부터, 늦으면 `Missed`). 시험 `LocalWorldFxTests`. 효과 재생: `LookDefinition.Timing`·`TimingBindings`(읽기는 `LookReader.ReadLooks`), `LookStage.Play`(열쇠 `fx:순번`)·`Advance(밀리초)`, `ILookRenderer.SetOpacity`, `LookBoard.RefreshAsync`가 `WorldFx`도 가져옴. Unity는 `ILookElementView.SetOpacity`(기본 메서드)로 색에 곱하고 데모가 `Time.deltaTime`을 넘긴다. skirmish는 `Hit(x, y, damage)` 신호와 `defs/fx.py`·`looks/fx.look.xml`의 붉은 고리와 떠오르는 피해 숫자. 그림 요소 `Text`(G427): 속성 종류 `LookAttributeType.Text`, 묶인 값은 `LookStage`의 `format`(Unity는 `Mods.Format`)으로 글이 되고, Unity는 내장 TextMesh(`GameObjectLookRenderer.TextView`). 시험 `LookFxTests`, `SkirmishScreenTests`, Unity `SmokeTest.CheckFx`. 플레이 모드에서 눈으로 본 적은 없다. 효과의 방향·크기(G428): `drift`·`grow`, `LookDefinition.Timing`은 칸마다 `LookAttributeValue`, 크기는 `PlaceObject`의 단위에 곱함. 연출 방식 선언(G429): `Mod(presentation="instant"|"staged")`, 커널 `ModManifest.Presentation`(값 검사만), 경계 `BridgeMod.Presentation`·`BridgeContent.Presentation`(뒤 모드가 이김), Unity `LoadedMods.Presentation`. `staged` 재생(G430): `LookBoard`가 `IPresentationHold`(`HoldAsync`가 `WorldFx`를 먼저 당겨 새 효과면 물체·표시 도장을 멈춤, `Advance`가 신호마다 단계, `Skip`), `UiInterpreter.Hold`(붙잡는 동안 갱신 멈춤, 누름은 건너뛰기), 경계 `BridgeFx.Signal`(커널 신호 도장), `LookStage.Play(효과들)`이 흐려지기 시작하는 때를 돌려줌·`StopEffects`. skirmish는 `presentation="staged"`. 플레이 모드에서 눈으로 본 적은 없다(공격해 맞히고 물러날 칸을 누르면 고리·숫자 뒤에 유닛이 물러나야 함). 퇴장(G431): `<Look exit="그림">`, `LookStage.Leave`(경계 `Gone`일 때만)가 마지막 자리·크기에 그림 값을 넘겨 효과로 띄움(`ShowAt`·`StartFx`), 없는 이름은 `LookLibrary` 로드 문제. skirmish `unit-down`(줄어들며 흐려짐). 붙잡은 판은 물러난 뒤 죽은 유닛의 물러난 자리를 못 보므로 퇴장은 맞은 자리에서, 고리는 물러난 자리에서 난다(보간이 풀 문제). 옮기기(G432): `move`는 시간 칸 목록에 있지만 효과에는 안 쓰임, `ShownObject.MoveFrom`·`Drawn`, `LookStage._moving`을 `Advance`가 옮김, 퇴장은 보이던 자리(`Drawn`)에서. skirmish 유닛 `move="0.25"`. 데모 확인은 `KesaDemo.Pass(밀리초)`로 시간을 넘겨 중간·도착 자리를 본다. 모델(G391·G433): Ui `Model` 요소(`src`만, `LookElementKind.Check`로 `LookKinds.CheckModel`이 확장자 검사, `AddModelFormat`), Unity `IModelLoader`·`GltfModelLoader`(glTFast 6.20.0, 편집 모드는 `UninterruptedDeferAgent`)·`LookModels`(경로마다 원본 하나)·`ModelView`(복제, 못 읽으면 보라색 상자). 스모크는 코드로 만든 삼각형 `.glb`로 보고 `EditorApplication.update`로 끝을 기다린다. 데모의 `packages-lock.json`은 에디터로 열 때 glTFast가 더해진다. 모델 애니메이션(G434): Ui `Model`의 `state`·`fade`와 자식 `State`(`name`·`clip`·`once`·`next`·`fade`·`blend`)·`Clip`(`clip`·`at`), `LookElementKind`의 `Parents`·`Validate`, 속성 종류 `Name`, `ILookRenderer.Advance`(기본 메서드). Unity `ModelViews.cs`(`ModelView`·`StateView`·`ClipView`, 렌더러가 `ILookParentView`로 자식 뷰를 이음), glTFast는 legacy 클립으로 읽고 `Animation.Sample()`. 스모크 `CheckAnimation`이 코드로 만든 움직이는 `.glb`로 섞기·한 번 틀기·블렌드를 잰다. 플레이 모드와 진짜 캐릭터 모델로는 본 적 없다. 뼈에 붙이기(G435): Ui `Attach`(`bone`, `Model` 바로 안, `Model` 안에는 `State`·`Attach`만), Unity `AttachView`·`ModelView.Place`. 모델 누르기는 `KeysAt`이 메시 경계 상자까지 봄. `onto(엔티티, 붙는 곳)` 그리기는 정할 때 같은 뼈 찾기를 쓴다. 모델 색(G436): `Model`의 `tint`와 `Paint`(`material`·`color`), Unity `PaintView`·`ModelView.Recolor`(칠할 때만 재질 복사). staged는 화면이 세계를 따라잡을 때까지(G437): `LookStage`의 재생 목록 하나(`IPlayback`: 효과·옮기기, `Busy`·`Settle`), `LookBoard.Holding`은 남은 단계나 `Busy`, 단계가 다 뜨면 판을 맞추고(`CatchUp`), 건너뛰기 뒤 처음 맞출 때 생긴 옮기기도 끝 상태로. skirmish는 빗나감도 `Miss` 신호와 글로 보인다. 데모 확인은 `KesaDemo.Finish()`로 따라잡게 한다. **플레이 모드에서 사용자가 확인함:** 옮기기, 맞는 효과(고리·피해 숫자), 빗나감 글, staged(봇이 기다림, 미끄러지는 중 클릭은 건너뛰기), 퇴장. 데모 `playbackSpeed`로 느리게 볼 수 있다. 모델은 skirmish에 없어 플레이 모드로 본 적 없다. 모델은 샘플 glTF로 볼지 정하지 않았다. **그 뒤:** `onto`·`between` 그리기나 기본 공간(계약, 쓸 곳이 생길 때). 쓸 곳이 생길 때 물을 계약: 기본 공간(`at(값)`)을 선언하는 법, 그림 파일 안의 `Include` 모양, `onto`·`between` 그리기, 2D에서 z(지금은 경고하고 안 그림). 모더 문서는 지금까지 만든 것만큼 고쳤다(8장 "월드 표시: 그림과 자리", 10장 표): 새로 만드는 것은 그 절과 10장 행에 더한다. 호스트 함수(C#)는 그림·자리를 돌려줄 수 없어 `Look`·`Where` 칸에 넣으면 로드 오류다. 별칭으로 칸을 부르는 게임 코드는 실행 중에 막힌다(`questions()`와 같은 안전망).
   - 설계 요약: 뜻은 호스트가 읽는 XML 그래픽 정의(UXML 장치 재사용, 요소 종류는 호스트가 등록, G390), 모델은 확장자로 로더를 고르고 첫 로더는 glTFast(G391), 로드 때 검사만 하고 처음 보일 때 읽음(G392). 공간 종류는 호스트(G394). 표시는 그림 이름+놓을 곳(또는 `key=`)으로 알아봄(G396). 연출 방식(바로 그리기·붙잡고 재생)은 모드 선언이 고르고 커널은 모름(G399).
3. 그 밖: UXML 컴포넌트 재사용, `hint`·선택지 `style`을 모양에 잇기, 데모 로그(봇 거절·열린 질문).
4. 봇 점수 도우미: 함수 칸 `Score["T"]`·`BotAction["T"]`(G374)와 `std.ai` 모드(Wesnoth RCA식). 먼저 skirmish 봇을 이 꼴로 다시 써 보며 모양을 정한다.

데모를 돌리는 법: 루트에서 `dotnet build`(DLL 복사) 후 Unity 6000.3으로 `hosts/unity/demo`를 열고 `Assets/Scenes/Demo.unity`를 재생한다. 에디터가 프로젝트를 열고 있으면 창 없는 확인(`-batchmode`)은 잠금 때문에 바로 끝난다.

**창(구현함):** 창은 제어되는 패널(G348, G337 일부바뀜): 열림은 UI 기록, 패널이 층·ESC·끌기를 선언, 호스트의 얇은 창 층이 처리하고 닫을 때 패널의 닫기 동작을 부름. 표기는 `window="층"`·`on-close`·`close-on-escape`·`window-drag`(G349), 종류는 `normal`·`modal`뿐이고 층은 없다(G350), 누르면 맨 위로 올리고 `window-raise="false"`로 끔(G351), 위치는 게임을 켜 둔 동안만 남음(G352), 판단은 해석기들이 함께 쓰는 `UiWindows`가 하고 ESC는 호스트가 `Windows.Escape()`로 보내며 바깥 클릭은 `close-on-outside="true"`일 때만(G354), 창 안에 창은 오류(G355). Unity 렌더러는 창을 겹침 층으로 옮기고 원래 자리에 자리표를 둔다. 시험은 `WindowTests`와 `SmokeTest`의 창 배치이고, Unity 데모에서 눈으로도 확인했다.

**잠정 결정:** handoff가 기다리던 G337·G345·G347은 모두 정리했다. 잠정 G2~G30(로딩·로직 언어 규칙)은 그 영역을 다시 건드리거나 실제 모드가 부딪힐 때 확인한다.

**정한 UXML 규칙 요약**(전문은 결정 기록):

- 변수는 윗 요소가 이름을 정의하는 이름 범위, 반복은 UXML의 선언형, 커맨드 조립은 스크립트(G330). 반복은 `for="변수 in 모드.함수(인자)"`이고 자식이 템플릿, 항목 값 자체가 키(G338·G339).
- 속성: `bind-<요소 속성>`, `bind-class-<이름>`(참거짓), `bind-class`(함수가 클래스 이름을 돌려줌), `bind-image`(모드가 공유하는 가상 경로, 나중에 로드된 모드가 이김)(G331·G333·G335). 클릭은 `on-click="모드.이름(인자)"`: 커맨드는 `Deliver`(자유 커맨드는 `Submit`, 응답 커맨드는 열린 질문에 답함), 화면 동작은 `Act`(G336·G346).
- 식은 모드 함수 호출 하나나 변수 하나, 연산자는 사례가 생기면 더한다(G332). 변수에서 사전 키를 점으로 읽는다(G344). 참거짓은 모드 언어의 판정, 숫자는 엄격, 맞지 않으면 그 바인딩만 실패하고 직전 값 유지(G341).
- 파일이 받는 이름은 `params`로 선언하고 `Include`로 인자를 넘기며 최상위는 `seat`로 앉은 엔티티를 받는다(G340).
- 회색 버튼: 세션의 `Check`(인자 → 입구 `acts_for`와 응답 커맨드의 열린 질문 → 사전 조건)와 `Version()`(G342·G346).

**남은 일:**

1. **런타임 USS (구현함, G359~G365):** `<Style src>`·`Include src`·`url()`에 UI Builder의 `project://…`, 절대 경로, `..`가 오면 찾지 않고 모드 경로로 고치라고 알린다(G365, `UiPaths`). 모드도 USS를 주고 문법은 Unity USS 그대로다. `Kesa.Engine.Ui`의 `UssReader`가 읽고(값은 글자 나무, 숫자 변환은 호스트) 진단하며(`UssProperties` 속성 99개, `@import`·`resource()`·모르는 의사 클래스), 해석기가 `files`로 `<Style src>`를 모드 폴더에서 읽어 `IUiRenderer.AddStyleSheet(요소, UssSheet)`로 넘긴다. Unity 패키지의 `UssStyleSheets`·`UssBuilder`(OneJS MIT, 패키지의 `THIRD_PARTY_NOTICES.md`)가 내부 `StyleSheetBuilder`로 진짜 `StyleSheet`을 만든다. 문제는 화면을 막지 않고 `Errors`로 간다(G364). 게임 시트는 게임 코드가 루트에 붙이고(G362) 명시도가 같으면 가까운 모드 시트가 이긴다(Unity 규칙). `SmokeTest`(`UssCheck`)가 속성 목록, Unity 임포터와 같은 모양(관계는 오른쪽 조각에 붙고 색 이름은 낱말 그대로), 런타임 패널 적용(`:hover` 자손, 자식, `var()`, `url()` 그림)을 확인한다. `ModFolderAssets.LoadUxml`은 `LoadText`로 바뀌었다. 확인하지 못한 것: IL2CPP 빌드(`Runtime/link.xml`을 `Editor/UssLinker`가 링커에 넘기고, `UssCheck`는 그 파일과 적힌 타입이 있는지만 봄). 에디터가 데모를 열고 있으면 데모를 복사하고 `manifest.json`의 패키지 경로를 절대 경로로 바꿔 창 없이 돌린다. 복사본을 `hosts/unity/` 아래 같은 깊이(예: `hosts/unity/demo-check-tmp`, 끝나면 치움)에 두면 데모의 상대 경로 `../../../samples/skirmish`가 맞아 `KesaDemoSetup.Check`도 돈다. 사용자가 에디터에서 데모를 보았고 모양과 카드 `:hover`가 맞다.
2. **AI 자리와 모드 봇 (구현함, G366~G379):** 경계의 `IBotPolicy.Act(세션)`과 `BotSeat.Run()`(세계 도장이 그대로면 부르지 않음)에 C# 봇(`PreferredResponses`)과 모드 봇이 같이 끼워진다.
   - 모드 봇: `@bot def f(me: Ref[X], 설정=기본값)`. 기억은 `bot_state`(칸 규칙은 `ui_state`와 같음, `bot.기록.칸`). 본문은 그 연결의 눈으로 읽고 `bot.…`·`submit`·`random`만 쓰며, "봇을 건드리는 함수" 표시가 퍼져 게임 쪽·화면 쪽은 `bot.…`을 못 쓴다.
   - 커널: `World.NewBot`/`WorldView.Think`/`BotState.Save`/`World.LoadBot`(`Runtime/World.Bot.cs`). 성공했을 때만 기억과 봇 난수 카운터가 들어가고, 예산을 넘으면 그 부름만 버린다. 봇 난수열은 봇 시드에서(기본은 월드 시드·엔티티·봇 id).
   - 경계: `session.Bots()`·`Bot(...)`·`LoadBot(바이트)`, `IScriptBot.Act`는 적은 수를 그 연결로 내고 하나라도 받아들여지면 참(G377), 거절 이유는 `LastProblems`(G378).
   - 선택지 질문에는 `submit("engine.pick", choice=n, by=e)`로 답한다(G379, `@ui_action`도 같음).
   - skirmish 파랑은 모드 봇 `skirmish.skirmisher`(`samples/skirmish/core/defs/ai.py`)이고 데모도 그것을 쓴다. `KesaDemoSetup.Check` 통과. 봇 둘이 끝까지 두는 시험(`SkirmishScreenTests`)이 맞음 질문 답 경로를 지난다. 에디터에서 눈으로는 아직 안 봄.
   - 다음 후보: 함수 칸 `Score["T"]`·`BotAction["T"]`(G374)와 `std.ai` 모드(Wesnoth RCA식 후보 행동). 봇은 `questions()`로 열린 질문을 읽는다(G383). 합법 수를 스크립트로 읽는 길은 아직 없다.
   - 남은 물음: 큰 게임에서 판을 읽는 속도(spec 9절), 몇 수 앞을 시험하는 판 복사, 원격 멀티의 전송. 이전 엔진(WG·QU·TR)은 문법이 달라 AI 설계에 참고하지 않는다.

결정 조회 도구에 `check`(머리 규칙 검사: 상태와 `changedBy`, 바뀐 쪽이 뒤의 번호인지, `**바뀜:**` 줄, 참조가 있는지, 파일 이름, 빠진 키, 태그 없음)가 있고, `tests/Kesa.Tools.Decisions.Tests`가 실제 `docs/decisions`에 이것을 돌린다. 결정 파일을 고친 뒤에는 `dotnet test`가 규칙 위반을 잡는다. G61은 처음부터 없던 번호다.

번역문의 `NUMBER`는 구현했다(G334·G356~G358, `NumberFunction`, 검사 `LOC012`). 옵션을 더할 때는 `Intl.NumberFormat` 이름을 따른다. 호스트 개발자용 "화면이 갱신되는 방식"은 Unity 패키지 README에 있다. `Translator`는 경계 값만 받는다(커널 값 오버로드를 거둠, 번역 어셈블리는 `Bridge.Local`이 아니라 `Bridge`를 참조하고 커널은 번역 파일 읽기와 검사에만 씀).

**작은 것:** 화면 동작이 낸 `submit`이 거절돼도 `BridgeResult.Succeeded`는 참이고 실패는 `Deliveries`에만 있다(G283, 호스트가 `ActionResult`에서 봐야 함). `UiInterpreter.Errors`는 `ClearErrors`로만 비운다.

**정하지 않은 세부(필요해지면):** 껐다 켜도 창 위치 남기기(G352) · 회색 버튼의 표시와 이유 보이기(툴팁과 함께), 화면 동작이 읽는 UI 기록 변화로도 다시 확인할지 · `Include`의 `src`를 찾는 법과 `params` 기본값·타입 · 한 연결이 여러 곳에 앉았을 때 패널을 곳마다 만들지 · 숨김 값을 참거짓으로 볼지, 오류를 화면에 보일지 · 리스트 번호 읽기, 호출 결과 공유 · 반복이 빌 때의 보임과 이전 엔진의 `filter-path` · 이미지가 없을 때의 보임, 스프라이트 시트, 이미지 읽기 방식의 세부 · 인라인 `style` 속성 · 고정소수점·`None`·숨김·선택지 값의 반복 키.

이전 엔진 UI는 `E:\Project\Game\SharedPackages\uk.kesa.engine\Scripts\View\UI\Core`(이 컴퓨터에서는 `Projects\Unity\SharedPackages`)에 있고, 이전 게임 UXML 75개는 `Quarantined`·`TurbulentRepublic`·`wargame`의 `*.uxml`이다.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 루트에서 `dotnet build`(Unity 패키지의 `Plugins/`로 DLL 복사)와 `dotnet test`로 2782개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다. Unity 쪽 코드는 `dotnet test`에 안 들어간다(창 없는 확인은 패키지 README).
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`, UI 해석은 `src/Kesa.Engine.Ui/UiInterpreter*.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다. 작업 방식(한국어, 허락 질문으로 멈추지 않기, push는 사용자가 말할 때만 등)은 CLAUDE.md에 있다.
