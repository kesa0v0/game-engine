# 인계 메모

마지막 갱신: 2026-10-26. 브랜치 `spec-v0.5-scaffold`, 테스트 2560개 통과(커널 2329, CLI 42, 콘솔 호스트 100, 번역 55, 경계 34). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났고, 미룬 것은 아래 "필요해지면"뿐이다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 9장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity(G323). 경계 어셈블리 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`의 ①값 타입·JSON ②세션·운영 인터페이스와 `LocalHost` ③번역기가 경계 값을 받음이 끝났다(G324~G328).

## 다음 할 일

**④ 콘솔 호스트를 경계로 옮긴다.** `ConsoleGame.cs`가 지금 `World`·`WorldView`를 바로 부른다. 이것을 `IHostOperator`·`IHostSession`(`LocalHost`)과 경계 값 번역(`Translator.Value(BridgeValue)`)으로 바꾼다. 패널은 `session.Call(키, …)`과 `Changed()`로 다시 그린다. `--seat 연결=Def`가 쓰는 `World.All`은 `SeatChoices()`로 바꾼다(묻지 않음).

먼저 사용자에게 물을 것: 화면 파일이 없을 때 세계를 늘어놓는 기능(`ConsoleGame`의 `All`·`Has`·`Get`)을 어떻게 하나. 선택지는 셋이다.
- **B′ (추천):** 보기 규칙을 지키는 읽기를 경계에 넣는다. 커널의 `WorldView.All`·`Has`·`Get`·`DefOf`가 이미 거른 읽기라서, 세션에 `Entities()`·`Read(엔티티)` 같은 것을 더해 `LocalHost`가 감싸면 된다. 숨긴 정보가 새지 않고 원격에도 쓴다. 단점은 경계의 값 목록을 넓히는 것(G326).
- A: 판 주인 전용 디버그로 둔다(숨긴 정보까지 보임, 경계 밖).
- C: 빼고 "화면 파일 없음"만 알린다.

정하면 `docs/decisions/G329.md`로 적는다. 그 뒤 ⑤ UPM 패키지와 빌드 복사, UXML 해석기의 속성 모양과 첫 화면을 사용자와 정한다. 다 옮기면 `Translator`의 커널 값 오버로드와 `Bridge.Local` 참조를 거둘지 본다. Unity 쪽 안내서에는 "화면 주는 방식"(모드가 화면 함수를 두고 호스트가 읽어 그림, 커널은 `Changed()`로 바뀐 키만 알림, 영수증은 경계 너머라서 둔 최적화)을 짧게 넣는다.

**남은 정리:** 조회 도구(`tools/Kesa.Tools.Decisions`)의 테스트, 자동으로 붙인 결정 태그 보정.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2560개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다.
