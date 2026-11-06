# 인계 메모

마지막 갱신: 2026-11-06. 브랜치 `spec-v0.5-scaffold`, 테스트 3074개 통과(커널 2596, CLI 42, 콘솔 호스트 102, 번역 65, 경계 87, UI 175, 결정 도구 7). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)·봇(`@bot`)이 끝났다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 10장 "아직 없는 것"도 고친다.

**봇의 시험 두기(G453~G466)가 끝났다.** `with imagine():`이 봇이 본 판을 통째 복사한 시험 판을 열고(`World.Imagine.cs`), 블록 안의 읽기는 모두 시험 판이며 `play`로 수를 두고 `advance(n)`으로 시간을 굴리고 가정을 쓴다. 스크립트 값(`EntityView` 등)과 판을 읽는 내장은 읽을 때마다 `Current`(맨 위 시험 판)로 간다. 숨긴 컴포넌트는 가림 표시(`_veiled`)로 붙지 않은 것처럼 보이고 `attach`로 채운다. 봇이 못 보던 질문은 뼈대만 읽힌다(G466). 시험 id는 2^62부터 봇 기록에 이어 센다. Flow 지역 변수의 비밀은 로드 경고 `CMD009`(`SecretLocals.cs`). 모더 문서는 9장 "수를 해 보기". 복사는 지금 Save+Load라 유닛 하나에 약 7µs(벤치 `--copy`).

첫 실제 호스트는 Unity다(G323).

- 경계 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`. 콘솔 호스트도 이 경계만 쓴다.
- 그리지 않는 해석 `src/Kesa.Engine.Ui`(경계만 참조): UXML 화면(`UiInterpreter`, 창 G348~G355, 런타임 USS G359~G365, 질문 창 G380~G386)과 판 위 그림(`LookLibrary`·`LookStage`·`LookBoard`·`LookKinds`, 입력 `InputLibrary`). 시험은 `tests/Kesa.Engine.Ui.Tests`(메모리 렌더러, 진짜 `LocalHost`, skirmish).
- Unity 패키지 `hosts/unity/uk.kesa.engine.unity`(입구 `LoadedMods.Load`·`mods.NewMatch`, `UiToolkitRenderer`, `GameObjectLookRenderer`, `ModInputs`)와 데모 `hosts/unity/demo`(`KesaDemo`). 패키지는 Input System 1.18.0, glTFast 6.20.0, 내장 animation 모듈에 기댄다. 호스트 개발자용 설명은 패키지 README.

**월드 표시(G388~G437)는 skirmish가 쓰는 만큼 끝났다.** 모드는 `look`·`where` 칸과 `look()`·`at()`·`mark()`·`fx()`로 그림 이름과 자리만 말하고, 호스트가 그림 정의 `.look.xml`과 공간 `.space.xml`로 그린다(설계 G390·G391·G392·G394·G396·G399). 입력은 `@world_input`과 `.input.xml`(G416~G424). 효과 `@world_fx`(G425~G428), 연출 방식 `Mod(presentation="instant"|"staged")`(G429), staged는 화면이 세계를 따라잡을 때까지 연결마다 붙잡는다(G430·G437: `LookStage`의 재생 목록 하나, 판은 종류를 묻지 않음, 누름은 건너뛰기). 퇴장 `exit`(G431), 옮기기 `move`(G432). 그림 요소는 `Sprite`·`Text`·`Group`·`Model`이고 모델은 애니메이션 상태 `State`·`Clip`(G434), 뼈에 붙이기 `Attach`(G435), 색 `tint`·`Paint`(G436). Unity는 glTFast legacy 클립을 손으로 샘플한다(Mecanim 클립은 빌드에서 만들어지지 않음). 모델 크기 `scale`(G438). 모더 문서는 8장 "월드 표시: 그림과 자리"와 10장 표.

**방향과 카메라(G440~G447):** 공간 종류가 자리마다 틀(위·북·동·떠오름)을 주고 `offset`·`drift`는 동·북·위, `turn`은 위에서 본 시계 방향, `Sprite`·`Text`는 바닥에 눕고 `face="camera"`면 빌보드, `rise`는 공간이 푸는 지름길. 카메라는 `.view.xml`의 이름 있는 `<View rig>`이고(커널은 모름) 리그 속성 규칙은 `LookKinds`, 움직이는 것은 Unity `BoardCamera`(`top`·`tilted`, 가운데 단추 끌기·휠). 켤 모드 목록 `ModSorter.Sort(discovery, enabled, bag)`(G439). `samples/skirmish/fox`는 skirmish 위의 덧붙임 모드다(Khronos Fox 모델, 판을 3D 바닥 `xz`와 기울인 카메라로 덮음). 데모는 `enabledMods`가 비면 다 켜므로 여우가 보이고, `skirmish`만 켜면 전과 같은 2D다(바이트까지 같음을 Shot으로 확인).

**사용자가 플레이 모드에서 확인함:** 화면 카드·창·모달·USS, 고르기·정보창·이동·공격, 옮기기, 맞는 효과(고리·피해 숫자), 빗나감 글, staged(봇이 기다림, 미끄러지는 중 클릭은 건너뛰기), 퇴장, 여우(3D 바닥·기울인 카메라·끌기·휠·피해 숫자). 데모 `KesaDemo`의 Playback Speed로 연출을 느리게 볼 수 있다. **본 적 없는 것:** 질문 기본 창의 모양(`kesa-question`), IL2CPP 빌드.

## 다음 할 일

**백본 전환의 세 단계를 마쳤다(G475~G478).** 값은 `World.Store.cs`의 Def 표(`DefTable`)와 곁표(`SideTable`)에 칼럼(`Column`)으로 있고, int·fixed·bool·Ref 칸은 `long` 배열이다. `Value` 클래스는 스크립트 쪽 표현으로 그대로 둔다. 호스트는 `World.Columns.cs`의 `Parts`와 `WriteColumn`으로 묶어 읽고 쓴다. Mono 플레이어와 Burst에서 도는 것은 `hosts/unity/bulk-check`로 확인했다(G478). 안 한 것: IL2CPP 실행(모듈 없음, 있으면 그 README대로 `-kesaIl2cpp`), Commander가 실제로 `Parts`로 옮기기(`ReadUnits`만 바꿔도 틱 11~14% 감소), `WriteColumn`의 비용 줄이기(Mono에서 유닛당 약 90ns), 키 컴포넌트의 줄은 아직 사전(`KeyedRows`의 모양은 밖에 보인다), 시험 판 복사는 아직 저장 나무를 거쳐 칼럼 복사의 이득이 없다. 저장 구조를 바꿀 때는 전체 테스트의 저장 해시를 전후로 비교한다(`SaveBytes`에 임시 훅, 커밋하지 않음).
`docs/engine-improvements.md`(Commander에서 나온 후보)에서 1·2·4·5·6·8·11·12·13번을 마쳤다: 판 갱신 비용, 창구 읽기의 할당과 호스트 함수 호출 비용, 문제 통로 `IHostOperator.Problems`(G470), `heavy_warning_steps`(G471), 수학 함수와 `**`(G472), 걸음 합계(G473), `e.id`(G474). 남은 것: Commander의 측정 도구로 1·2번 효과 다시 재기, 틱을 프레임에 나눌 때의 엔티티별 변경 추적(2번의 나머지), 3·7·9·10번, 근거를 기다리는 14~16번. `KesaDemo`의 `Problems` 줄은 Unity에서 컴파일해 보지 않았다.

봇 틀의 보완이 끝났다: 시험 두기, 로직 언어 속도의 잔손질, 큰 판 읽기. 점수 도우미는 공식으로 만들지 않고 모양만 `docs/std-ideas.md`에 뒀다(G469, 모드가 Def 가중치·`defs()`·`@wrap`으로 짬). 큰 판 읽기에서 한 것: 보기 규칙이 없으면 읽기마다 찾지 않고 있으면 한 번 동안 답을 기억, 시험 판 복사는 저장 글 없이 나무를 바로 읽음(1만 판 115 → 약 80ms), 크기를 따르는 일은 그 양만큼 단계를 셈(G467: 복사는 엔티티·값 하나에 300걸음, 묶음·목록은 원소마다 1), 긴 봇 생각이 화면을 막는 것은 호스트가 예산과 부르는 때로 다룸(G468, Unity README "봇이 생각하는 시간"). 남은 것은 아래 1뿐이고 미뤄 둔 것이라, 다음 큰 주제를 사용자와 정한다.

1. 로직 언어 실행 방식(되돌리기 비싼 내부 결정, 사용자에게 물을 것): 잔손질로 minimax가 2686 → 약 1910ns/판(C# 약 76ns, 25배)이 됐다. 벤치 `--parts`(부품마다 ns와 바이트), `--part 이름`(프로파일러용, `dotnet-trace`는 스크래치에 깔아 `report topN`으로 봄). 남은 시간은 문장 고리(`Session.Step`)와 식 노드에 고르게 퍼져 있어, 더 줄이려면 실행 방식을 바꿔야 한다. 잔손질은 다 했다. 선택지: 두 갈래(멈출 수 없는 함수(ask·wait 없음)는 C# 재귀로 바로 돌림, 2~3배 추정, 대신 깊이 천장이 C# 스택에 묶임), 바이트코드 VM(빠르고 천장도 올림, 아무 걸음에서나 멈출 수 있어 봇 생각을 프레임에 나눌 수도 있음(G468), 큰 공사, 멈춘 실행의 저장 형식은 지켜야 함). 사용자는 "실제 필요가 보일 때"로 미뤘다. 원래 같이 하려던 "식 안의 호출도 C# 스택 없이 + `max_call_depth` 천장(지금 300, G452) 올리기"는 두 갈래와 부딪히므로 실행 방식을 정할 때 함께 정한다.
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
- 따옴표 없는 Def 참조(`Resource.Wood`), 비트 연산(G96), S8 `migrate`.
- 커널을 작업 스레드에 두는 호스트(G468): 묶은 읽기나 밀어 주는 화면 사본, 호스트 함수의 Unity API 금지를 그때 정한다. 시험 판 복사를 저장 나무 없이 바로 복사하기(1만 판 약 80ms, G453 내부).

## 새 세션에서 시작하는 법

1. `git pull` 후 루트에서 `dotnet build`와 `dotnet test`로 3032개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다. Unity 쪽 코드는 `dotnet test`에 안 들어간다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 판 위 그림은 `src/Kesa.Engine.Ui/Look*.cs`, UI 해석은 `UiInterpreter*.cs`, 커널 쪽 월드 표시는 `Runtime/World.Look.cs`·`World.Fx.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다. 작업 방식(한국어, 허락 질문으로 멈추지 않기, push는 사용자가 말할 때만 등)은 CLAUDE.md에 있다.
