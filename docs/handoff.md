# 인계 메모

마지막 갱신: 2026-10-29. 브랜치 `spec-v0.5-scaffold`, 테스트 2716개 통과(커널 2354, CLI 42, 콘솔 호스트 101, 번역 65, 경계 46, UI 101, 결정 도구 7). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 10장 "아직 없는 것"도 고친다. 봇은 9장이다.

첫 실제 호스트는 Unity다(G323). 만든 것:

- 경계 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`(`LocalHost`)(G324~G329, G342, G346). 콘솔 호스트도 이 경계만 쓴다.
- UXML 해석 `src/Kesa.Engine.Ui`: 식 파서, UXML 읽기, 해석기, `IUiRenderer`. 경계만 참조한다(G330~G345). 시험은 `tests/Kesa.Engine.Ui.Tests`(시험용 메모리 렌더러, 진짜 `LocalHost`, skirmish 화면).
- Unity 패키지 `hosts/unity/uk.kesa.engine.unity`(`UiToolkitRenderer`, `ModFolderAssets`, 입구 `LoadedMods.Load`와 `mods.NewMatch`, G353)와 데모 프로젝트 `hosts/unity/demo`(씬 `Assets/Scenes/Demo.unity`, `KesaDemo`). 루트 `dotnet build`가 DLL을 패키지의 `Plugins/`로 복사한다(G347, 복사본은 git에 없음). 모드 오류는 판을 막지 않고 `Diagnostics`·`Disabled`로 온다. API 타입 이름에 `Kesa` 접두사를 붙이지 않는다.
- 설치된 Unity 6000.3.8f1(이 컴퓨터: `C:\Program Files\Unity\Hub\Editor\6000.3.8f1`)에서 컴파일과 창 없는 확인이 통과했다(`SmokeTest`, `KesaDemoSetup.Check`, 패키지 README). skirmish 화면이 실제 `VisualElement` 트리로 올라와 카드 6장, 번역된 유닛 이름, 회색 버튼까지 맞다.
- 사용자가 에디터에서 데모를 재생해 눈으로 확인했다(2026-10-29): 카드 배치, 선택 표시, 정보창 끌기·ESC, 확인 모달(뒤를 막음, 턴이 넘어가면 닫힘). 그 뒤 화면 모양을 모드 USS(`samples/skirmish/core/ui/screen.uss`, 카드에 `:hover` 더함)로 옮겼고 데모 게임의 `Assets/Resources/KesaDemo.uss`에는 `Label` 글자색만 남았다(기본 런타임 테마는 글자가 짙어 어두운 배경에서 안 보인다). 옮긴 뒤의 모양은 아직 눈으로 보지 않았다.

## 다음 할 일

**바로 다음(순서대로):**

1. 사용자가 에디터에서 데모를 재생해 파랑 모드 봇이 두는 것을 눈으로 본다(이동·공격·차례 넘김, 맞음 질문 답). 빨강 차례에도 봇이 수를 적어 거절되므로 `LastProblems`가 시끄러운 것은 알려진 일이다.
2. 봇 점수 도우미: 함수 칸 `Score["T"]`·`BotAction["T"]`(G374)와 `std.ai` 모드(후보 행동마다 점수를 매기고 가장 높은 것을 하는 Wesnoth RCA식). 먼저 skirmish 봇을 이 꼴로 다시 써 보며 모양을 정한다.
3. 봇이 열린 질문·합법 수를 스크립트로 읽는 길(필요해지면). 지금은 거절에 기댄다. 정하면 모딩 안내서 9장과 10장의 표를 고친다.

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
   - 다음 후보: 함수 칸 `Score["T"]`·`BotAction["T"]`(G374)와 `std.ai` 모드(Wesnoth RCA식 후보 행동). 봇이 열린 질문을 스크립트로 읽는 길은 없다(지금 skirmish 봇은 차례가 아닐 때 적은 수가 거절되는 것에 기댐, 필요해지면 정함).
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

1. `git pull` 후 루트에서 `dotnet build`(Unity 패키지의 `Plugins/`로 DLL 복사)와 `dotnet test`로 2716개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다. Unity 쪽 코드는 `dotnet test`에 안 들어간다(창 없는 확인은 패키지 README).
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`, UI 해석은 `src/Kesa.Engine.Ui/UiInterpreter*.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다. 작업 방식(한국어, 허락 질문으로 멈추지 않기, push는 사용자가 말할 때만 등)은 CLAUDE.md에 있다.
