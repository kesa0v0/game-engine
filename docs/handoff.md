# 인계 메모

마지막 갱신: 2026-10-27. 브랜치 `spec-v0.5-scaffold`, 테스트 2569개 통과(커널 2329, CLI 42, 콘솔 호스트 101, 번역 55, 경계 42). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났고, 미룬 것은 아래 "필요해지면"뿐이다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 9장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity(G323). 경계 어셈블리 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`의 ①값 타입·JSON ②세션·운영 인터페이스와 `LocalHost` ③번역기가 경계 값을 받음 ④콘솔 호스트가 경계만 씀(G329: 세션의 `Entities()`·`Read()`, 화면 파일이 없으면 자리마다 그 연결이 보는 엔티티를 늘어놓음)이 끝났다(G324~G329).

## 다음 할 일

**UXML 해석기의 규칙은 정했고(G330~G344) 아직 코드가 없다.** 정한 것:

- 변수는 윗 요소가 이름을 정의하는 이름 범위, 반복은 UXML의 선언형, 커맨드 조립은 스크립트(G330).
- 속성: `bind-<요소 속성>`, 클래스 토글은 `bind-class-<이름>`(G331), 함수가 클래스 이름(들)을 돌려주는 `bind-class`(G333), 이미지는 `bind-image`와 모드가 공유하는 가상 경로로 나중에 로드된 모드가 이김(G335), 클릭은 `on-click="모드.이름(인자)"`로 이름이 커맨드면 `Submit`, 화면 동작이면 `Act`(G336).
- 식은 모드 함수 호출 하나나 변수 하나이고 연산자는 사례가 생기면 더한다(G332). 변수에서 사전 키를 점으로 읽는다(G344). 타입 변환은 참거짓이 모드 언어의 판정, 숫자는 엄격, 맞지 않으면 그 바인딩만 실패하고 직전 값을 유지(G341).
- 반복은 `for="변수 in 모드.함수(인자)"`이고 자식이 템플릿(G338), 항목 값 자체가 키(G339). 파일이 받는 이름은 `params`로 선언하고 `Include`로 이름 붙은 인자를 넘기며 최상위는 `seat`로 앉은 엔티티를 받는다(G340).
- 회색 버튼은 세션에 `Check(이름, 인자)`와 `Version()`을 더하고 도장이 바뀌면 다시 확인한다(G342, **경계에 아직 없음**).
- 창은 별도 개념 없이 UI 기록에 묶은 패널이다(G337, 잠정).
- 해석의 그리지 않는 부분은 엔진 무관한 어셈블리, Unity는 렌더러 구현, 시험용 메모리 렌더러로 `dotnet test`(G343).
- 숫자 서식은 번역문의 `NUMBER`(G334). 아직 구현하지 않아 지금은 숫자가 전체 자릿수 그대로 나온다(임시). 옵션 범위·반올림·언어별 기호는 구현할 때 정한다.

**남은 일, 순서대로:**

1. (끝) 경계에 `Check`·`Version`(G342)을 더했다. 커맨드용 `WorldView.CheckPreconditions`도 더했다.
2. UXML 해석 어셈블리를 만든다(G343): 이름과 렌더러 인터페이스 모양을 정하고 시험용 렌더러로 규칙을 시험한다. **첫 화면은 `samples/skirmish`**(엔진 시험용 데모, 유닛 목록·선택 → 공격)를 UXML로 옮긴 것이다. 지금 콘솔의 `ui/console.txt` 패널이 기준이다.
3. ⑤ UPM 패키지와 빌드 복사(G328, 새 DLL이 더해짐)와 Unity 렌더러.
4. NUMBER 구현(G334).

**정하지 않은 세부(필요해지면):** 창 끌기·맨 위로 올리기 속성, 닫았다 열 때 위치 기억, ESC·모달·레이어(G337) · 회색 버튼의 표시와 이유 보이기(툴팁과 함께), 화면 동작이 읽는 UI 기록 변화로도 다시 확인할지 · `Include`의 `src`를 찾는 법과 `params` 기본값·타입 · 한 연결이 여러 곳에 앉았을 때 패널을 곳마다 만들지 · 숨김 값을 참거짓으로 볼지, 오류를 화면에 보일지 · 리스트 번호 읽기, 호출 결과 공유 · 반복이 빌 때의 보임과 이전 엔진의 `filter-path` · 이미지가 없을 때의 보임과 스프라이트 시트 · 고정소수점·`None`·숨김·선택지 값의 반복 키.

이전 엔진 UI는 `E:\Project\Game\SharedPackages\uk.kesa.engine\Scripts\View\UI\Core`(이 컴퓨터에서는 `Projects\Unity\SharedPackages`)에 있고, 이전 게임 UXML 75개는 `Quarantined`·`TurbulentRepublic`·`wargame`의 `*.uxml`이다. `Translator`의 커널 값 오버로드(`Label(Value, World)` 등)는 이제 번역 테스트만 쓰므로 거둘지 본다(`ConsoleHost`는 `Bridge.Local`을 `LocalHost` 때문에 계속 참조한다). Unity 쪽 안내서에는 "화면 주는 방식"(모드가 화면 함수를 두고 호스트가 읽어 그림, 커널은 `Changed()`로 바뀐 키만 알림, 영수증은 경계 너머라서 둔 최적화)을 짧게 넣는다.

**남은 정리:** 조회 도구(`tools/Kesa.Tools.Decisions`)의 테스트, 자동으로 붙인 결정 태그 보정.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2569개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다.
