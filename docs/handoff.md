# 인계 메모

마지막 갱신: 2026-11-15. 브랜치 `spec-v0.5-scaffold`, 테스트 3164개 통과(커널 2604, CLI 90, 콘솔 호스트 114, 번역 65, 경계 94, UI 190, 결정 도구 7). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)·봇(`@bot`)이 끝났다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 10장 "아직 없는 것"도 고친다.

**봇의 시험 두기(G453~G466)가 끝났다.** `with imagine():`이 봇이 본 판을 통째 복사한 시험 판을 열고(`World.Imagine.cs`), 블록 안의 읽기는 모두 시험 판이며 `play`로 수를 두고 `advance(n)`으로 시간을 굴리고 가정을 쓴다. 스크립트 값(`EntityView` 등)과 판을 읽는 내장은 읽을 때마다 `Current`(맨 위 시험 판)로 간다. 숨긴 컴포넌트는 가림 표시(`_veiled`)로 붙지 않은 것처럼 보이고 `attach`로 채운다. 봇이 못 보던 질문은 뼈대만 읽힌다(G466). 시험 id는 2^62부터 봇 기록에 이어 센다. Flow 지역 변수의 비밀은 로드 경고 `CMD009`(`SecretLocals.cs`). 모더 문서는 9장 "수를 해 보기". 복사는 지금 Save+Load라 유닛 하나에 약 7µs(벤치 `--copy`).

첫 실제 호스트는 Unity다(G323).

- 경계 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`. 콘솔 호스트도 이 경계만 쓴다.
- 그리지 않는 해석 `src/Kesa.Engine.Ui`(경계만 참조): UXML 화면(`UiInterpreter`, 창 G348~G355, 런타임 USS G359~G365, 질문 창 G380~G386)과 판 위 그림(`LookLibrary`·`LookStage`·`LookBoard`·`LookKinds`, 입력 `InputLibrary`). 시험은 `tests/Kesa.Engine.Ui.Tests`(메모리 렌더러, 진짜 `LocalHost`, skirmish).
- Unity 패키지 `hosts/unity/uk.kesa.engine.unity`(입구 `LoadedMods.Load`·`mods.NewMatch`, `UiToolkitRenderer`, `GameObjectLookRenderer`, `ModInputs`)와 데모 `hosts/unity/demo`(`KesaDemo`). 패키지는 Input System 1.18.0, glTFast 6.20.0, 내장 animation 모듈에 기댄다. 호스트 개발자용 설명은 패키지 README.
- 새 게임 템플릿 `hosts/unity/template`(빈 판, `GameHost`·`GameCheck`)과 `engine new <이름> [--engine copy|link]`(`src/Kesa.Engine.Cli/NewGame.cs`). copy(기본)는 패키지·안내서·CLI를 게임 안에 넣고, 넣은 CLI는 패키지 `Plugins/`의 DLL을 쓴다. copy로 만든 게임을 Unity 배치 모드에서 열어 `GAME CHECK: OK`를 확인했다. copy 게임에 엔진을 다시 넣는 명령은 없다(README에 "다시 복사"). 기존 게임(Commander·Weimar·Coup)은 옮기지 않았다.

**월드 표시(G388~G437)는 skirmish가 쓰는 만큼 끝났다.** 모드는 `look`·`where` 칸과 `look()`·`at()`·`mark()`·`fx()`로 그림 이름과 자리만 말하고, 호스트가 그림 정의 `.look.xml`과 공간 `.space.xml`로 그린다(설계 G390·G391·G392·G394·G396·G399). 입력은 `@world_input`과 `.input.xml`(G416~G424). 효과 `@world_fx`(G425~G428), 연출 방식 `Mod(presentation="instant"|"staged")`(G429), staged는 화면이 세계를 따라잡을 때까지 연결마다 붙잡는다(G430·G437: `LookStage`의 재생 목록 하나, 판은 종류를 묻지 않음, 누름은 건너뛰기). 퇴장 `exit`(G431), 옮기기 `move`(G432). 그림 요소는 `Sprite`·`Text`·`Group`·`Model`이고 모델은 애니메이션 상태 `State`·`Clip`(G434), 뼈에 붙이기 `Attach`(G435), 색 `tint`·`Paint`(G436). Unity는 glTFast legacy 클립을 손으로 샘플한다(Mecanim 클립은 빌드에서 만들어지지 않음). 모델 크기 `scale`(G438). 모더 문서는 8장 "월드 표시: 그림과 자리"와 10장 표.

**방향과 카메라(G440~G447):** 공간 종류가 자리마다 틀(위·북·동·떠오름)을 주고 `offset`·`drift`는 동·북·위, `turn`은 위에서 본 시계 방향, `Sprite`·`Text`는 바닥에 눕고 `face="camera"`면 빌보드, `rise`는 공간이 푸는 지름길. 카메라는 `.view.xml`의 이름 있는 `<View rig>`이고(커널은 모름) 리그 속성 규칙은 `LookKinds`, 움직이는 것은 Unity `BoardCamera`(`top`·`tilted`, 가운데 단추 끌기·휠). 켤 모드 목록 `ModSorter.Sort(discovery, enabled, bag)`(G439). `samples/skirmish/fox`는 skirmish 위의 덧붙임 모드다(Khronos Fox 모델, 판을 3D 바닥 `xz`와 기울인 카메라로 덮음). 데모는 `enabledMods`가 비면 다 켜므로 여우가 보이고, `skirmish`만 켜면 전과 같은 2D다(바이트까지 같음을 Shot으로 확인).

**판 위 끌기와 커서(G482~G490, Coup에서 나옴):** `.input.xml`의 `kind="path"|"lasso"`(모양 종류는 `LookKinds`에 공간 종류처럼 등록, `PathGesture`·`LassoGesture`), 핸들러는 공간 대상만 받고 모양의 값 하나를 받는다. 포인터 단추의 누름은 뗄 때, 문턱을 넘으면 끌기(문턱은 호스트, Unity 데모 `dragThreshold`). 수정 키는 누를 때 정한다. 읽기 내장 `drag("입력")`·`cursor("공간")`은 화면 쪽만이고, 경계 `IHostSession.Cursor`·`Drag`가 키마다 바뀐 때를 `UiState`에 두어 읽은 함수만 다시 부른다. 판 쪽은 `LookBoard`의 `PointAsync`·`PressedAsync`·`DragAsync`·`ReleaseAsync`. 모더 문서는 8장 "끌어 그리기와 커서". 30×30 잰 값은 G482. **Unity 플레이 모드에서 진짜 마우스로 끌어 본 적은 없다**(창 없는 `SmokeTest`·`KesaDemoSetup.Check`만 통과). skirmish에는 끌기 입력이 없다.

**사용자가 플레이 모드에서 확인함:** 화면 카드·창·모달·USS, 고르기·정보창·이동·공격, 옮기기, 맞는 효과(고리·피해 숫자), 빗나감 글, staged(봇이 기다림, 미끄러지는 중 클릭은 건너뛰기), 퇴장, 여우(3D 바닥·기울인 카메라·끌기·휠·피해 숫자). 데모 `KesaDemo`의 Playback Speed로 연출을 느리게 볼 수 있다. **본 적 없는 것:** 질문 기본 창의 모양(`kesa-question`), IL2CPP 빌드.

## 다음 할 일

**백본 전환의 세 단계를 마쳤다(G475~G478).** 값은 `World.Store.cs`의 Def 표(`DefTable`)와 곁표(`SideTable`)에 칼럼(`Column`)으로 있고, int·fixed·bool·Ref 칸은 `long` 배열이다. `Value` 클래스는 스크립트 쪽 표현으로 그대로 둔다. 호스트는 `World.Columns.cs`의 `Parts`와 `WriteColumn`으로 묶어 읽고 쓴다. Mono 플레이어와 Burst에서 도는 것은 `hosts/unity/bulk-check`로 확인했다(G478). 키 컴포넌트의 줄은 아직 사전이다(`KeyedRows`의 모양은 밖에 보인다). 저장 구조를 바꿀 때는 전체 테스트의 저장 해시를 전후로 비교한다(`SaveBytes`에 임시 훅, 커밋하지 않음).
**엔진 작업은 사용자 말로 여기서 멈췄다(2026-11-07). 아래는 정한 일이 아니라 후보이고, 다시 시작할 때 사용자와 고른다.** Commander에서 나온 개선 후보는 대부분 끝났고(판 갱신 비용, 창구 읽기, `IHostOperator.Problems` G470, `heavy_warning_steps` G471, 수학 함수 G472, 걸음 합계 G473, `e.id` G474, 묶음 API G478, `engine check` 넓히기 G480·G481), 새로 만나는 것은 비워 둔 `docs/engine-improvements.md`에 적는다.

봇 틀의 보완도 끝났다: 시험 두기, 로직 언어 속도의 잔손질, 큰 판 읽기. 점수 도우미는 공식으로 만들지 않고 모양만 `docs/std-ideas.md`에 뒀다(G469). 크기를 따르는 일은 그 양만큼 단계를 센다(G467). 긴 봇 생각이 화면을 막는 것은 호스트가 예산과 부르는 때로 다룬다(G468, Unity README "봇이 생각하는 시간").

### 확인이 남은 것 (작음)

- 끌기를 Unity 플레이 모드에서 진짜 마우스로: Coup처럼 `kind=` 입력이 있는 모드로 누름(뗄 때)·끌기·Shift/Alt·ESC 취소·미리 보기를 본다.
- G482의 "구현하며 정한 것 (사용자 확인 전)" 항목.
- IL2CPP 실행: 이 컴퓨터에 모듈이 없다. 있으면 `hosts/unity/bulk-check`의 README대로 `-kesaIl2cpp`.
- G480·G481의 "사용자 확인 전" 항목: `LOK002`를 따로 둔 것, CLI에 적은 `.glb`·`.gltf`, USS 문제를 오류로 낸 것.

### 성능 후보

- Commander 다시 재기: `Assets/Editor/CommanderMeasure.cs`(`-executeMethod CommanderMeasure.Run -releaseCodeOptimization`)로 판 갱신 개선의 효과를 처음과 같은 조건에서 확인한다. 고치기 전 값(틱, 틱 뒤 판 갱신, 변화 없는 판 갱신, ms): 1000유닛 13.95, 11.27, 0.78. 2000유닛 28.74, 27.52, 1.72. 5000유닛 76.98, 108.56, 6.60. 틱 중 스크립트의 몫은 아직 재지 않았다.
- 엔티티별 변경 추적: 세계가 그대로인 프레임은 건너뛰지만, 틱을 프레임에 나눠 매 프레임 무언가 바뀌면 `LocalSession.WorldObjects`가 유닛당 약 1.0~1.5µs로 모든 영수증을 다시 본다. 판 갱신 구조를 건드리는 큰 일이다.
- `where`·`look`의 스크립트 비용: 유닛당 약 5µs다. 위치 칸을 읽어 바로 넘기는 흔한 꼴을 스크립트 없이 선언하는 길이 있으면 줄어든다(계약 결정).
- 묶음 API 쪽: Commander가 실제로 `Parts`로 옮기기(`ReadUnits`만 바꿔도 틱 11~14% 감소, Commander 쪽 결정), `WriteColumn` 비용(Mono에서 유닛당 약 90ns), 키 컴포넌트 줄의 칼럼화, 시험 판 복사의 칼럼화(지금 저장 나무를 거쳐 1만 판 약 80ms, 복사 한 번이 한 프레임을 넘는다). 여러 프레임에 걸친 Burst 작업에는 복사가 필요하다(조각은 스택 전용).
- 로직 언어 실행 방식(되돌리기 비싼 내부 결정, 사용자가 "실제 필요가 보일 때"로 미룸): minimax는 이 컴퓨터에서 스크립트 3346ns/칸, C# 90ns로 약 37배다(4초 예열. 전에 적은 25배는 다른 측정). 문장 고리와 식 노드에 시간이 고르게 퍼져 잔손질은 끝났다. 선택지는 두 갈래(멈출 수 없는 함수를 C# 재귀로, 2~3배 추정, 깊이 천장이 C# 스택에 묶임), 바이트코드 VM, 값 구조체다. G226 시제품에서는 VM만으로는 지금 방식과 속도가 비슷했고(3300 대 3560) VM에 값 구조체를 더해야 1550이었다. "무엇을 위해 하는가"가 아직 없다. `max_call_depth`(300, G452)를 올리는 일은 이때 함께 정한다. 벤치 `--parts`, `--part 이름`.

### 기능 후보

- 봇 생각을 여러 프레임에 나누기(사용자가 미룸, 정한 것 없음): 실시간 게임에서는 생각 중 판 잠금을 못 쓴다. 후보는 ① 엔진을 고치지 않고 봇이 기억에 자리를 남겨 틱마다 조금씩 생각(유닛 훑기에 맞고 깊은 탐색에는 안 맞음, 돌려 보지 않음) ② 사본 위에서 나누기(판 복사 비용과 식 안의 C# 재귀 호출에서 못 멈추는 것을 먼저 풀어야 함, 낡은 판 문제가 남음). 바이트코드 VM이 꼭 필요한지는 확인하지 않았다. 지금 Commander에는 `@bot`이 없다.
- `engine check`가 아직 못 보는 것: `.input.xml`(키 이름이 Unity에 있음), UXML 요소·속성 이름(렌더러가 앎), USS `url()`의 파일, 게임이 등록한 종류의 내용. 게임이 종류 선언 파일을 내보내면 오타를 오류로 잡고 속성까지 볼 수 있다(G221과 같은 방식, G480에서 미룸).
- 게임의 공간 종류가 쓸 파서: `LookAttributes.FromText`, `UiSuggest.Hint`, `UiExprParser.ParseNumber`가 `internal`이라 게임의 `SpaceKind`는 "혹시 …?"를 못 낸다. 연속 공간이 엔진 표준이 되면 함께 사라진다.
- Unity 6.3과의 이름 충돌: `UnityEngine.EntityId`와 `Kesa.Engine.Runtime.EntityId`가 부딪혀 두 namespace를 함께 쓰는 파일은 별칭이 필요하다.

### 근거를 기다리는 구상

- 내장 배치 함수와 공간 색인: 이웃 찾기처럼 많은 엔티티에 같은 계산을 엔진이 한 번에 하고 결과를 컴포넌트에 적는다(`near` 등). 호스트 함수가 스크립트 함수를 못 부르므로(G217) 판단은 배치 안에 못 넣는다. Commander에서 호스트 함수로 시험해 쓸모가 확인된 부분만 올린다. 효과는 재지 않았다.
- 스크립트의 묶음 처리(`@system(Unit, reads=[…], writes=[…])`, 문법은 예시): 1단계 같은 시점 읽기와 모아 쓰기(거의 안 빨라지고 토대가 됨), 2단계 병렬 실행(값 구조체 필요), 3단계 연산 하나를 칼럼 전체에 적용(효과가 가장 크고 언어 제한도 가장 큼). 정할 계약: 값이 보이는 시점, 구조 변경(G88과 충돌), 유닛별 난수열, 다른 유닛에 쓰기의 충돌 규칙, 시스템 안의 `ask`·`wait` 금지. 사례는 일반 지식: 언리얼 Niagara의 벡터 VM. 효과 크기는 재지 않았다.

### 쓸 곳이 생길 때 물을 것

**쓸 곳이 생길 때 물을 계약(월드 표시):** `onto`·`between` 그리기(`onto`의 붙는 곳은 G435의 뼈 찾기를 다시 씀), 기본 공간(`at(값)`) 선언, 그림 파일 안의 `Include`, 다른 공간 종류(육각·연속·구역), 2D에서 z(지금은 경고하고 안 그림), 모델 `once` 애니메이션을 끝이 있는 재생으로 기다리기, 모델 색 마스크 텍스처, 모더가 고르는 글꼴, 커서 아래 엔티티와 눌린 키 읽기(G397의 남은 것), 호스트가 그리는 기본 끌기 미리 보기(G487의 D), 멀티플레이에서 staged 단계가 쌓일 때 빨리 감기, 플레이어가 덮는 연출 설정(빠른 이동 등).
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
- 따옴표 없는 Def 참조(`Resource.Wood`), 비트 연산(G96), S8 `migrate`.
- 커널을 작업 스레드에 두는 호스트(G468): 묶은 읽기나 밀어 주는 화면 사본, 호스트 함수의 Unity API 금지를 그때 정한다. 시험 판 복사를 저장 나무 없이 바로 복사하기(1만 판 약 80ms, G453 내부).

## 새 세션에서 시작하는 법

1. `git pull` 후 루트에서 `dotnet build`와 `dotnet test`로 3164개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다. Unity 쪽 코드는 `dotnet test`에 안 들어간다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 판 위 그림은 `src/Kesa.Engine.Ui/Look*.cs`, UI 해석은 `UiInterpreter*.cs`, 커널 쪽 월드 표시는 `Runtime/World.Look.cs`·`World.Fx.cs`다.
3. 위 "다음 할 일"의 후보와 `docs/engine-improvements.md`에 새로 적힌 것을 보고 사용자와 정한다. 작업 방식(한국어, 허락 질문으로 멈추지 않기, push는 사용자가 말할 때만 등)은 CLAUDE.md에 있다.
