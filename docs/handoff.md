# 인계 메모

마지막 갱신: 2026-10-27. 브랜치 `spec-v0.5-scaffold`, 테스트 2624개 통과(커널 2329, CLI 42, 콘솔 호스트 101, 번역 55, 경계 42, UI 55). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났고, 미룬 것은 아래 "필요해지면"뿐이다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 9장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity(G323). 경계 어셈블리 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`의 ①값 타입·JSON ②세션·운영 인터페이스와 `LocalHost` ③번역기가 경계 값을 받음 ④콘솔 호스트가 경계만 씀(G329: 세션의 `Entities()`·`Read()`, 화면 파일이 없으면 자리마다 그 연결이 보는 엔티티를 늘어놓음)이 끝났다(G324~G329).

## 다음 할 일

**UXML 해석기의 규칙(G330~G345)과 그리지 않는 부분의 코드(`src/Kesa.Engine.Ui`: 식 파서, UXML 읽기, 해석기, 렌더러 인터페이스)와 시험(`tests/Kesa.Engine.Ui.Tests`: 시험용 메모리 렌더러와 진짜 `LocalHost`, skirmish 화면)이 끝났다. Unity 렌더러와 패키지는 아직 없다.** 정한 것:

- 변수는 윗 요소가 이름을 정의하는 이름 범위, 반복은 UXML의 선언형, 커맨드 조립은 스크립트(G330).
- 속성: `bind-<요소 속성>`, 클래스 토글은 `bind-class-<이름>`(G331), 함수가 클래스 이름(들)을 돌려주는 `bind-class`(G333), 이미지는 `bind-image`와 모드가 공유하는 가상 경로로 나중에 로드된 모드가 이김(G335), 클릭은 `on-click="모드.이름(인자)"`로 이름이 커맨드면 `Submit`, 화면 동작이면 `Act`(G336).
- 식은 모드 함수 호출 하나나 변수 하나이고 연산자는 사례가 생기면 더한다(G332). 변수에서 사전 키를 점으로 읽는다(G344). 타입 변환은 참거짓이 모드 언어의 판정, 숫자는 엄격, 맞지 않으면 그 바인딩만 실패하고 직전 값을 유지(G341).
- 반복은 `for="변수 in 모드.함수(인자)"`이고 자식이 템플릿(G338), 항목 값 자체가 키(G339). 파일이 받는 이름은 `params`로 선언하고 `Include`로 이름 붙은 인자를 넘기며 최상위는 `seat`로 앉은 엔티티를 받는다(G340).
- 회색 버튼은 세션에 `Check(이름, 인자)`와 `Version()`을 더하고 도장이 바뀌면 다시 확인한다(G342, 경계에 있음).
- 창은 별도 개념 없이 UI 기록에 묶은 패널이다(G337, 잠정).
- 해석의 그리지 않는 부분은 엔진 무관한 어셈블리 `Kesa.Engine.Ui`(경계만 참조, 글자 만들기는 주입), Unity는 렌더러 구현, 시험용 메모리 렌더러로 `dotnet test`(G343·G345).
- 숫자 서식은 번역문의 `NUMBER`(G334). 아직 구현하지 않아 지금은 숫자가 전체 자릿수 그대로 나온다(임시). 옵션 범위·반올림·언어별 기호는 구현할 때 정한다.

**남은 일, 순서대로:**

1. (끝) 경계에 `Check`·`Version`(G342)과 커맨드용 `WorldView.CheckPreconditions`.
2. (끝) `Kesa.Engine.Ui`와 시험. 첫 화면은 `samples/skirmish/core/ui/screen.uxml`(엔진 시험용 데모, 유닛 카드·고르기 → 공격)이고 화면 값 함수 `turn_number`·`mine`·`is_chosen`을 skirmish에 더했다(콘솔 쪽 함수는 그대로).
3. 아래 "풀어야 할 빈틈"을 사용자와 정한다.
4. ⑤ UPM 패키지와 빌드 복사(G328, 새 DLL이 더해짐)와 Unity 렌더러(`IUiRenderer` 구현: 요소 만들기, 고정 속성·`style`·`<Style src>` 처리, 고정소수점 raw를 실수로, 이미지 경로 찾기).
5. NUMBER 구현(G334).

**풀어야 할 빈틈 둘:** ① `on-click`이 가리키는 커맨드가 `free=True`가 아니면(skirmish의 `Move`·`Attack`·`EndTurn`은 응답 커맨드) `Submit`은 `NeedsRequest`로 돌려준다. 화면 동작의 `submit`은 G283이 열린 질문을 찾아 짝지어 주는데 경계에 그 짝짓기 호출이 없다. 선택지: 그 호출(`Deliver`에 해당)을 경계에 더한다 / 그런 버튼은 `@ui_action`을 거치게 한다. ② `Check`는 사전 조건만 보고 이 연결이 그 커맨드를 낼 자격이 있는지(`acts_for` 입구 검사)는 보지 않아, 남의 진영 버튼이 눌러도 되는 것처럼 보일 수 있다.

**구현하며 제가 정한 것, 사용자 확인 전(G345 잠정):** 어셈블리 이름 `Kesa.Engine.Ui`, 번역을 참조하지 않고 글자 만들기를 주입(G343 일부바뀜), 숫자를 고정소수점 raw로 렌더러에 줌, 영수증 키 모양과 세션당 해석기 하나, 호스트가 직접 낸 동작은 `ActAsync`/`InvalidateButtons`로 알림.

**작은 것:** 화면 동작이 낸 `submit`이 거절돼도 `BridgeResult.Succeeded`는 참이고 실패는 `Deliveries`에만 있다(G283, 호스트가 `ActionResult`에서 봐야 함). `Errors`는 `ClearErrors`로만 비운다.

**정하지 않은 세부(필요해지면):** 창 끌기·맨 위로 올리기 속성, 닫았다 열 때 위치 기억, ESC·모달·레이어(G337) · 회색 버튼의 표시와 이유 보이기(툴팁과 함께), 화면 동작이 읽는 UI 기록 변화로도 다시 확인할지 · `Include`의 `src`를 찾는 법과 `params` 기본값·타입 · 한 연결이 여러 곳에 앉았을 때 패널을 곳마다 만들지 · 숨김 값을 참거짓으로 볼지, 오류를 화면에 보일지 · 리스트 번호 읽기, 호출 결과 공유 · 반복이 빌 때의 보임과 이전 엔진의 `filter-path` · 이미지가 없을 때의 보임과 스프라이트 시트 · 고정소수점·`None`·숨김·선택지 값의 반복 키.

이전 엔진 UI는 `E:\Project\Game\SharedPackages\uk.kesa.engine\Scripts\View\UI\Core`(이 컴퓨터에서는 `Projects\Unity\SharedPackages`)에 있고, 이전 게임 UXML 75개는 `Quarantined`·`TurbulentRepublic`·`wargame`의 `*.uxml`이다. `Translator`의 커널 값 오버로드(`Label(Value, World)` 등)는 이제 번역 테스트만 쓰므로 거둘지 본다(`ConsoleHost`는 `Bridge.Local`을 `LocalHost` 때문에 계속 참조한다). Unity 쪽 안내서에는 "화면 주는 방식"(모드가 화면 함수를 두고 호스트가 읽어 그림, 커널은 `Changed()`로 바뀐 키만 알림, 영수증은 경계 너머라서 둔 최적화)을 짧게 넣는다.

**남은 정리:** 조회 도구(`tools/Kesa.Tools.Decisions`)의 테스트, 자동으로 붙인 결정 태그 보정.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2624개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다.
