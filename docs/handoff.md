# 인계 메모

마지막 갱신: 2026-11-02. 브랜치 `spec-v0.5-scaffold`, 테스트 2919개 통과(커널 2455, CLI 42, 콘솔 호스트 101, 번역 65, 경계 75, UI 174, 결정 도구 7). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)·봇(`@bot`)이 끝났다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 10장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity다(G323).

- 경계 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`. 콘솔 호스트도 이 경계만 쓴다.
- 그리지 않는 해석 `src/Kesa.Engine.Ui`(경계만 참조): UXML 화면(`UiInterpreter`, 창 G348~G355, 런타임 USS G359~G365, 질문 창 G380~G386)과 판 위 그림(`LookLibrary`·`LookStage`·`LookBoard`·`LookKinds`, 입력 `InputLibrary`). 시험은 `tests/Kesa.Engine.Ui.Tests`(메모리 렌더러, 진짜 `LocalHost`, skirmish).
- Unity 패키지 `hosts/unity/uk.kesa.engine.unity`(입구 `LoadedMods.Load`·`mods.NewMatch`, `UiToolkitRenderer`, `GameObjectLookRenderer`, `ModInputs`)와 데모 `hosts/unity/demo`(`KesaDemo`). 패키지는 Input System 1.18.0, glTFast 6.20.0, 내장 animation 모듈에 기댄다. 호스트 개발자용 설명은 패키지 README.

**월드 표시(G388~G437)는 skirmish가 쓰는 만큼 끝났다.** 모드는 `look`·`where` 칸과 `look()`·`at()`·`mark()`·`fx()`로 그림 이름과 자리만 말하고, 호스트가 그림 정의 `.look.xml`과 공간 `.space.xml`로 그린다(설계 G390·G391·G392·G394·G396·G399). 입력은 `@world_input`과 `.input.xml`(G416~G424). 효과 `@world_fx`(G425~G428), 연출 방식 `Mod(presentation="instant"|"staged")`(G429), staged는 화면이 세계를 따라잡을 때까지 연결마다 붙잡는다(G430·G437: `LookStage`의 재생 목록 하나, 판은 종류를 묻지 않음, 누름은 건너뛰기). 퇴장 `exit`(G431), 옮기기 `move`(G432). 그림 요소는 `Sprite`·`Text`·`Group`·`Model`이고 모델은 애니메이션 상태 `State`·`Clip`(G434), 뼈에 붙이기 `Attach`(G435), 색 `tint`·`Paint`(G436). Unity는 glTFast legacy 클립을 손으로 샘플한다(Mecanim 클립은 빌드에서 만들어지지 않음). 모델 크기 `scale`(G438). 모더 문서는 8장 "월드 표시: 그림과 자리"와 10장 표.

**방향과 카메라(G440~G447):** 공간 종류가 자리마다 틀(위·북·동·떠오름)을 주고 `offset`·`drift`는 동·북·위, `turn`은 위에서 본 시계 방향, `Sprite`·`Text`는 바닥에 눕고 `face="camera"`면 빌보드, `rise`는 공간이 푸는 지름길. 카메라는 `.view.xml`의 이름 있는 `<View rig>`이고(커널은 모름) 리그 속성 규칙은 `LookKinds`, 움직이는 것은 Unity `BoardCamera`(`top`·`tilted`, 가운데 단추 끌기·휠). 켤 모드 목록 `ModSorter.Sort(discovery, enabled, bag)`(G439). `samples/skirmish/fox`는 skirmish 위의 덧붙임 모드다(Khronos Fox 모델, 판을 3D 바닥 `xz`와 기울인 카메라로 덮음). 데모는 `enabledMods`가 비면 다 켜므로 여우가 보이고, `skirmish`만 켜면 전과 같은 2D다(바이트까지 같음을 Shot으로 확인).

**사용자가 플레이 모드에서 확인함:** 화면 카드·창·모달·USS, 고르기·정보창·이동·공격, 옮기기, 맞는 효과(고리·피해 숫자), 빗나감 글, staged(봇이 기다림, 미끄러지는 중 클릭은 건너뛰기), 퇴장, 여우(3D 바닥·기울인 카메라·끌기·휠·피해 숫자). 데모 `KesaDemo`의 Playback Speed로 연출을 느리게 볼 수 있다. **본 적 없는 것:** 질문 기본 창의 모양(`kesa-question`), IL2CPP 빌드.

## 다음 할 일

봇 틀의 약한 곳을 보완하는 중이다(점수 도우미보다 먼저, 사용자 뜻). 아래 1~3 중 무엇부터 할지 사용자에게 고르게 한다(추천 1).

1. 판 복사·시험 두기: 봇이 "이 커맨드를 내면 판이 어떻게 되나"를 해 보고 버리는 길. 지금은 판(엔티티)으로는 못 하고 모더가 판을 리스트로 따로 베껴 규칙을 두 번 짜야 한다. 계약이라 선택지부터 설명한다.
2. 로직 언어 속도: 벤치 `--minimax`(틱택토 전체 55만 판)에서 3.7µs/판, C# 91ns/판(41배), 판 하나 약 80단계. 먼저 어디에 시간이 드는지 잰다. 같이 할 것: 식 안의 호출도 문장 호출처럼 C# 스택 없이 프레임만 쌓게 고치고(벤치 `--stack`으로 확인) `max_call_depth` 천장(지금 300, G452)을 크게(예: 1만) 올린다.
3. 큰 판 읽기: 유닛 수천 개의 판을 봇이 매 틱 훑는 비용(spec 9절), 봇 경로의 약 50% 덧비용 프로파일. 긴 봇 생각이 호스트 화면을 막는 것도 같이 본다.
4. 봇 점수 도우미: 함수 칸 `Score["T"]`·`BotAction["T"]`(G374)와 효용·RCA 모드. 편의 라이브러리라 미룸. 다시 시작할 때 먼저 물을 것: 첫 공식 모드의 자리와 id. 모드 id에 점이 안 되므로(G2) `std.ai`는 안 된다. 선택지: 모드 하나 id `std`(안에 ai·stats·stack, 사용자가 `std`를 쓰려 함), 모드 여럿 `std_ai`·`std_stats`, 엔진 어셈블리 내장.

**쓸 곳이 생길 때 물을 계약(월드 표시):** `onto`·`between` 그리기(`onto`의 붙는 곳은 G435의 뼈 찾기를 다시 씀), 기본 공간(`at(값)`) 선언, 그림 파일 안의 `Include`, 다른 공간 종류(육각·연속·구역), 2D에서 z(지금은 경고하고 안 그림), 모델 `once` 애니메이션을 끝이 있는 재생으로 기다리기, 모델 색 마스크 텍스처, 모더가 고르는 글꼴, 커서·끌기 같은 입력 상태 읽기(G397), 멀티플레이에서 staged 단계가 쌓일 때 빨리 감기, 플레이어가 덮는 연출 설정(빠른 이동 등).
카메라 쪽(G441에 적은 것): UXML 패널 안의 View(작은 지도·초상), View마다 다른 그림(지도 모드), 판과 따로인 무대, 모드가 카메라를 옮기거나 잠그라는 요청, View가 여럿일 때 빌보드가 어느 카메라를 볼지, 카메라 기울기 말고 롤, 키로 돌리기(생기면 `face="camera"` 피해 숫자가 돌려도 정면인지 플레이로 본다). 모든 여우가 북을 본다(진영마다 돌리려면 skirmish `unit_look`이 `turn` 값을 넘기고 그림이 `bind-turn`).

**알려진 작은 어긋남:** 한 커맨드에서 물러나며 죽으면 고리는 물러난 자리, 퇴장은 붙잡힌 판이 마지막에 본 자리에서 날 수 있다(`SkirmishScreenTests.AFallenUnitShrinksAwayAfterTheStagedHit`). 모델 누르기는 메시의 경계 상자로 잰다. 모델은 미리 읽기에 들지 않는다.

## 참고

- 데모 돌리기: 루트에서 `dotnet build`(DLL을 패키지 `Plugins/`로 복사) 후 Unity 6000.3으로 `hosts/unity/demo`를 열고 `Assets/Scenes/Demo.unity`를 재생한다. 처음 열 때 glTFast를 받아 `packages-lock.json`이 바뀐다.
- 창 없는 확인: `SmokeTest.Run`(`-kesaMods samples/skirmish`)과 `KesaDemoSetup.Check`. 에디터가 데모를 열고 있으면 잠금 때문에 바로 끝나므로, 데모를 `hosts/unity/demo-check-tmp`처럼 같은 깊이로 복사하고 `manifest.json`의 패키지 경로를 절대 경로로 바꿔 돌린다(끝나면 치움). 플러그인 DLL이 낡으면 이상한 로드 오류가 나므로 먼저 `dotnet build`. 이 컴퓨터의 Unity: `C:\Program Files\Unity\Hub\Editor\6000.3.8f1`.
- 잠정 결정 G2~G30(로딩·로직 언어 규칙)은 그 영역을 다시 건드리거나 실제 모드가 부딪힐 때 확인한다.
- 작은 것: 화면 동작이 낸 `submit`이 거절돼도 `BridgeResult.Succeeded`는 참이고 실패는 `Deliveries`에만 있다(G283). `UiInterpreter.Errors`는 `ClearErrors`로만 비운다.
- 정하지 않은 UI 세부(필요해지면): 껐다 켜도 창 위치 남기기(G352), 회색 버튼의 이유 보이기, `Include`의 `src` 찾기와 고정값 인자·`params` 기본값·타입, 한 연결이 여러 곳에 앉았을 때 패널, 숨김 값의 참거짓, 반복이 빌 때의 보임, 이미지가 없을 때의 보임·스프라이트 시트, 인라인 `style`, 고정소수점·`None`·숨김·선택지 값의 반복 키.
- 봇의 남은 물음(위 다음 할 일 밖): 원격 멀티의 전송, 합법 수를 스크립트로 읽는 길. 기성 BT·FSM·GOAP와 길찾기 함수는 없다(`@bot`·기억·`now()`·호스트 함수로 짤 수는 있음).
- 데모 `KesaDemo`는 화면 오류(`ui.Errors`), 봇이 거절당한 이유(`LastProblems`), 열리고 닫힌 질문(`logQuestions`)을 콘솔에 적는다.
- `Include`의 고정값 인자·`params` 기본값은 플래그로 갈리는 변형이 여러 화면에 생길 때 묻는다(지금은 파일을 쪼개 조립, 값이 없을 때는 `if="식"`, G449).
- 이전 엔진 UI는 `E:\Project\Game\SharedPackages\uk.kesa.engine\Scripts\View\UI\Core`(다른 컴퓨터에서는 `Projects\Unity\SharedPackages`), 이전 게임 UXML은 `Quarantined`·`TurbulentRepublic`·`wargame`의 `*.uxml`이다. 이전 엔진(WG·QU·TR)은 문법이 달라 AI 설계에 참고하지 않는다.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 루트에서 `dotnet build`와 `dotnet test`로 2904개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다. Unity 쪽 코드는 `dotnet test`에 안 들어간다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 판 위 그림은 `src/Kesa.Engine.Ui/Look*.cs`, UI 해석은 `UiInterpreter*.cs`, 커널 쪽 월드 표시는 `Runtime/World.Look.cs`·`World.Fx.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다. 작업 방식(한국어, 허락 질문으로 멈추지 않기, push는 사용자가 말할 때만 등)은 CLAUDE.md에 있다.
