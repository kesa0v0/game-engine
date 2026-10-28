# 인계 메모

마지막 갱신: 2026-10-27. 브랜치 `spec-v0.5-scaffold`, 테스트 2628개 통과(커널 2329, CLI 42, 콘솔 호스트 101, 번역 55, 경계 42, UI 59). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 9장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity다(G323). 만든 것:

- 경계 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`(`LocalHost`)(G324~G329, G342, G346). 콘솔 호스트도 이 경계만 쓴다.
- UXML 해석 `src/Kesa.Engine.Ui`: 식 파서, UXML 읽기, 해석기, `IUiRenderer`. 경계만 참조한다(G330~G345). 시험은 `tests/Kesa.Engine.Ui.Tests`(시험용 메모리 렌더러, 진짜 `LocalHost`, skirmish 화면).
- Unity 패키지 `hosts/unity/uk.kesa.engine.unity`(`UiToolkitRenderer`, `ModFolderAssets`, `KesaGame`)와 데모 프로젝트 `hosts/unity/demo`(씬 `Assets/Scenes/Demo.unity`, `KesaDemo`). 루트 `dotnet build`가 DLL을 패키지의 `Plugins/`로 복사한다(G347, 복사본은 git에 없음).
- 설치된 Unity 6000.3.8f1(이 컴퓨터: `C:\Program Files\Unity\Hub\Editor\6000.3.8f1`)에서 컴파일과 창 없는 확인이 통과했다(`KesaSmokeTest`, `KesaDemoSetup.Check`, 패키지 README). skirmish 화면이 실제 `VisualElement` 트리로 올라와 카드 6장, 번역된 유닛 이름, 회색 버튼까지 맞다.

## 다음 할 일

**먼저:** 루트에서 `dotnet build`(DLL 복사) 후 Unity 6000.3으로 `hosts/unity/demo`를 열고 `Assets/Scenes/Demo.unity`를 재생해 눌림(선택·공격·턴 끝내기)과 프레임마다 갱신을 눈으로 확인한다. 이것은 Unity에서 아직 돌려 보지 않았다(`dotnet test`에서만 시험함). 데모 화면은 스타일이 없어 기본 모양이다.

**정한 것, 구현할 것:** 창은 제어되는 패널(G348, G337 일부바뀜): 열림은 UI 기록, 패널이 층·ESC·끌기를 선언, 호스트의 얇은 창 층이 처리하고 닫을 때 패널의 닫기 동작을 부름. 표기는 `window="층"`·`on-close`·`close-on-escape`·`window-drag`(G349), 종류는 `normal`·`modal`뿐이고 층은 없다(G350), 누르면 맨 위로 올리고 `window-raise="false"`로 끔(G351), 위치는 게임을 켜 둔 동안만 남음(G352). 구현은 아직이다(UXML 읽기·해석기의 창 속성, 렌더러에 창 알림, Unity 렌더러의 창 층).

**사용자 확인을 기다리는 잠정 결정:** G345(렌더러 인터페이스: 어셈블리 이름 `Kesa.Engine.Ui`, 번역을 참조하지 않고 글자 만들기를 주입, 숫자를 고정소수점 raw로 렌더러에 줌, 세션당 해석기 하나, 호스트가 직접 낸 동작은 `ActAsync`/`InvalidateButtons`로 알림), G347(`KesaGame`·`ModFolderAssets`의 API 모양). 조회: `dotnet run --project tools/Kesa.Tools.Decisions -- show 345`. 영수증 키 모양과 `.meta`를 올리는 것은 내부 구현이라 묻지 않는다(CLAUDE.md의 묻는 기준). 잠정 G2~G30(로딩·로직 언어 규칙)은 그 영역을 다시 건드리거나 실제 모드가 부딪힐 때 확인한다.

**정한 UXML 규칙 요약**(전문은 결정 기록):

- 변수는 윗 요소가 이름을 정의하는 이름 범위, 반복은 UXML의 선언형, 커맨드 조립은 스크립트(G330). 반복은 `for="변수 in 모드.함수(인자)"`이고 자식이 템플릿, 항목 값 자체가 키(G338·G339).
- 속성: `bind-<요소 속성>`, `bind-class-<이름>`(참거짓), `bind-class`(함수가 클래스 이름을 돌려줌), `bind-image`(모드가 공유하는 가상 경로, 나중에 로드된 모드가 이김)(G331·G333·G335). 클릭은 `on-click="모드.이름(인자)"`: 커맨드는 `Deliver`(자유 커맨드는 `Submit`, 응답 커맨드는 열린 질문에 답함), 화면 동작은 `Act`(G336·G346).
- 식은 모드 함수 호출 하나나 변수 하나, 연산자는 사례가 생기면 더한다(G332). 변수에서 사전 키를 점으로 읽는다(G344). 참거짓은 모드 언어의 판정, 숫자는 엄격, 맞지 않으면 그 바인딩만 실패하고 직전 값 유지(G341).
- 파일이 받는 이름은 `params`로 선언하고 `Include`로 인자를 넘기며 최상위는 `seat`로 앉은 엔티티를 받는다(G340).
- 회색 버튼: 세션의 `Check`(인자 → 입구 `acts_for`와 응답 커맨드의 열린 질문 → 사전 조건)와 `Version()`(G342·G346).

**남은 일:**

1. NUMBER 구현(G334): 번역문의 `NUMBER` 함수를 번역기에 넣는다. 지금은 숫자가 전체 자릿수 그대로 나온다(임시). 옵션 범위·반올림·언어별 기호는 구현할 때 사용자와 정한다.
2. `Translator`의 커널 값 오버로드(`Label(Value, World)` 등)는 이제 번역 테스트만 쓰므로 거둘지 본다(`ConsoleHost`는 `Bridge.Local`을 `LocalHost` 때문에 계속 참조한다).
3. Unity 쪽 안내서에 "화면 주는 방식"을 짧게 넣는다(모드가 화면 함수를 두고 호스트가 읽어 그림, 커널은 `Changed()`로 바뀐 키만 알림, 영수증은 경계 너머라서 둔 최적화). 8장에 UXML 절은 있다.
4. 조회 도구(`tools/Kesa.Tools.Decisions`)의 테스트, 자동으로 붙인 결정 태그 보정.

**작은 것:** 화면 동작이 낸 `submit`이 거절돼도 `BridgeResult.Succeeded`는 참이고 실패는 `Deliveries`에만 있다(G283, 호스트가 `ActionResult`에서 봐야 함). `UiInterpreter.Errors`는 `ClearErrors`로만 비운다.

**정하지 않은 세부(필요해지면):** 껐다 켜도 창 위치 남기기(G352) · 회색 버튼의 표시와 이유 보이기(툴팁과 함께), 화면 동작이 읽는 UI 기록 변화로도 다시 확인할지 · `Include`의 `src`를 찾는 법과 `params` 기본값·타입 · 한 연결이 여러 곳에 앉았을 때 패널을 곳마다 만들지 · 숨김 값을 참거짓으로 볼지, 오류를 화면에 보일지 · 리스트 번호 읽기, 호출 결과 공유 · 반복이 빌 때의 보임과 이전 엔진의 `filter-path` · 이미지가 없을 때의 보임, 스프라이트 시트, 이미지 읽기 방식의 세부 · 인라인 `style` 속성 · 고정소수점·`None`·숨김·선택지 값의 반복 키.

이전 엔진 UI는 `E:\Project\Game\SharedPackages\uk.kesa.engine\Scripts\View\UI\Core`(이 컴퓨터에서는 `Projects\Unity\SharedPackages`)에 있고, 이전 게임 UXML 75개는 `Quarantined`·`TurbulentRepublic`·`wargame`의 `*.uxml`이다.

## 필요해지면 (실제 모드 사례가 생기면)

- **런타임 USS (백로그, 사용자가 미룸):** 모드가 모양(스타일)을 바꾸려면 USS 글을 런타임에 읽어야 한다. 지금은 이미지(G335)만 모드가 바꿀 수 있다. Unity에는 런타임에 USS 글을 `StyleSheet`로 만드는 공개 API가 없다. 이전 엔진은 `RuntimeStyleSheet`(72줄 정규식 파서: `.클래스`·`#id` 선택자와 `--변수`)로 읽고 `StyleApplicator`(377줄, 속성 59가지)가 요소에 인라인 스타일로 직접 적용했다(`SharedPackages/uk.kesa.engine/Scripts/Core/Data/Base/UI/RuntimeStyleSheet.cs`, `.../View/UI/Core/StyleApplicator.cs`). 선택지: A 하이브리드(게임이 기본 스타일시트를 `StyleSheet` 에셋으로 주고 모드 USS는 파서로 나중에 더함, 제 추천) / B 파서를 지금 만든다 / C 게임만 스타일을 준다(모드는 USS 불가). 현재는 `ModFolderAssets`에 스타일시트 읽기를 꽂을 수 있다.
- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 루트에서 `dotnet build`(Unity 패키지의 `Plugins/`로 DLL 복사)와 `dotnet test`로 2628개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다. Unity 쪽 코드는 `dotnet test`에 안 들어간다(창 없는 확인은 패키지 README).
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`, UI 해석은 `src/Kesa.Engine.Ui/UiInterpreter*.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다. 작업 방식(한국어, 허락 질문으로 멈추지 않기, push는 사용자가 말할 때만 등)은 CLAUDE.md에 있다.
