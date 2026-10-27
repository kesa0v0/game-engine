# 인계 메모

마지막 갱신: 2026-10-27. 브랜치 `spec-v0.5-scaffold`, 테스트 2566개 통과(커널 2329, CLI 42, 콘솔 호스트 101, 번역 55, 경계 39). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났고, 미룬 것은 아래 "필요해지면"뿐이다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 9장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity(G323). 경계 어셈블리 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`의 ①값 타입·JSON ②세션·운영 인터페이스와 `LocalHost` ③번역기가 경계 값을 받음 ④콘솔 호스트가 경계만 씀이 끝났다(G324~G329).

## 다음 할 일

④ 콘솔 호스트를 경계로 옮기는 일은 끝났다(G329: 세션의 `Entities()`·`Read()`). `ConsoleGame`은 `IHostOperator`·`IHostSession`만 부르고 `Play`가 `LocalHost`를 만든다. 화면 파일이 없을 때는 자리(연결)마다 그 연결이 보는 엔티티를 `[연결]` 머리 아래에 늘어놓는다.

**다음:** ⑤ UPM 패키지와 빌드 복사, UXML 해석기를 사용자와 정한다. 정한 것은 G330(변수는 윗 요소가 이름을 정의하는 이름 범위, 반복은 UXML의 선언형, 커맨드 조립은 스크립트)과 G331(`bind-<요소 속성>`, 클래스는 `bind-class-<이름>`)과 G332(식은 호출 하나나 변수 하나, 연산자는 사례가 생기면)와 G333(`bind-class`가 클래스 이름을 돌려받음)와 G334(숫자 서식은 번역문의 `NUMBER`, 엔진이 번역기에 넣음, 아직 구현 안 함: 지금은 숫자가 전체 자릿수 그대로 나오는 임시 상태이고, 옵션 범위·반올림·언어별 기호를 구현할 때 정한다. UXML 결정이 끝난 뒤에 한다)과 G335(이미지는 모드가 공유하는 가상 경로, 나중에 로드된 모드가 이김)과 G336(`on-click="모드.이름(인자)"`, 이름으로 커맨드와 화면 동작을 구분)과 G337(창은 별도 개념 없이 UI 기록에 묶은 패널, 하나만 열기는 값 하나. 이동 가능한 여러 창은 repeater가 못 하고 호스트의 창 틀 구성요소가 필요한데 모양은 미정이라 필요해질 때 정한다. ESC·모달·레이어도 그때)이다. 남은 질문은 순서대로: 반복(repeater)의 속성 이름과 항목 대응 키(창·목록이 기댐), 회색 버튼(경계에 `CheckPreconditions`에 해당하는 호출이 없음), 값을 속성 타입으로 바꾸는 규칙, 식 파서를 둘 곳(엔진 무관한 어셈블리 제안), 사전 필드 접근, 패널 루트 변수를 선언하는 모양, 반복이 리스트가 바뀔 때 항목을 대응시키는 키, 첫 화면. 한 번에 하나씩 묻는다. 이전 엔진 UI는 `E:\Project\Game\SharedPackages\uk.kesa.engine\Scripts\View\UI\Core`(이 컴퓨터에서는 `Projects\Unity\SharedPackages`). `Translator`의 커널 값 오버로드(`Label(Value, World)` 등)는 이제 번역 테스트만 쓰므로 거둘지 본다(`ConsoleHost`는 `Bridge.Local`을 `LocalHost` 때문에 계속 참조한다). Unity 쪽 안내서에는 "화면 주는 방식"(모드가 화면 함수를 두고 호스트가 읽어 그림, 커널은 `Changed()`로 바뀐 키만 알림, 영수증은 경계 너머라서 둔 최적화)을 짧게 넣는다.

**남은 정리:** 조회 도구(`tools/Kesa.Tools.Decisions`)의 테스트, 자동으로 붙인 결정 태그 보정.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2560개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다.
