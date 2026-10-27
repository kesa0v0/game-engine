# 인계 메모

마지막 갱신: 2026-10-27. 브랜치 `spec-v0.5-scaffold`, 테스트 2566개 통과(커널 2329, CLI 42, 콘솔 호스트 101, 번역 55, 경계 39). 이 문서는 지금 상태와 다음 할 일만 둔다. 지난 기록은 커밋 메시지, 결정은 `docs/decisions/`(조회법은 CLAUDE.md "결정 기록")다.

## 상태

커널은 혼자서 게임 한 판을 돌린다. 로딩·데이터·로직 언어·커맨드·Flow·반응·시간·숨긴 정보·저장·호스트 함수·번역·UI 행동(`@ui_action`)이 모두 끝났고, 미룬 것은 아래 "필요해지면"뿐이다. 엔진 무관 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, `kesa-play`)와 예시 게임 `samples/skirmish`가 있다. 모더용 안내서는 `docs/modding/`(예시 `samples/guide`). 기능을 바꾸면 해당 장과 9장 "아직 없는 것"도 고친다.

첫 실제 호스트는 Unity(G323). 경계 어셈블리 `src/Kesa.Engine.Bridge`(커널을 모름)와 같은 프로세스 구현 `Bridge.Local`의 ①값 타입·JSON ②세션·운영 인터페이스와 `LocalHost` ③번역기가 경계 값을 받음이 끝났다(G324~G328).

## 다음 할 일

④ 콘솔 호스트를 경계로 옮기는 일은 끝났다(G329: 세션의 `Entities()`·`Read()`). `ConsoleGame`은 `IHostOperator`·`IHostSession`만 부르고 `Play`가 `LocalHost`를 만든다. 화면 파일이 없을 때는 자리(연결)마다 그 연결이 보는 엔티티를 `[연결]` 머리 아래에 늘어놓는다.

**다음:** ⑤ UPM 패키지와 빌드 복사, UXML 해석기의 속성 모양과 첫 화면을 사용자와 정한다. `Translator`의 커널 값 오버로드(`Label(Value, World)` 등)는 이제 번역 테스트만 쓰므로 거둘지 본다(`ConsoleHost`는 `Bridge.Local`을 `LocalHost` 때문에 계속 참조한다). Unity 쪽 안내서에는 "화면 주는 방식"(모드가 화면 함수를 두고 호스트가 읽어 그림, 커널은 `Changed()`로 바뀐 키만 알림, 영수증은 경계 너머라서 둔 최적화)을 짧게 넣는다.

**남은 정리:** 조회 도구(`tools/Kesa.Tools.Decisions`)의 테스트, 자동으로 붙인 결정 태그 보정.

## 필요해지면 (실제 모드 사례가 생기면)

- 호스트 함수 인자 8개 초과(`HostOverloads.cs`를 16개까지), 호스트 창구의 `choices`.
- 따옴표 없는 Def 참조(`Resource.Wood`), 로직 연산자 확장(`**`·비트 연산, G96), S8 `migrate`.
- 속도의 작은 후보(지역 변수 표, 문장 호출 결과 칸, 컴프리헨션 프레임 재사용), 값 구조체화·백본 ECS화(맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2560개 통과를 확인한다. 클라우드 세션에서 .NET 9 SDK가 없으면 `apt-get install -y dotnet-sdk-10.0`으로 깔고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`와 이 문서를 읽는다. 필요한 `docs/spine-spec.md` 절과 결정(`list --tag …`·`show 번호`)만 본다. 실행기는 `Script/Execution/PureEvaluator*.cs`·`Session*.cs`·`Frame.cs`·`Block.cs`, 커맨드는 `Runtime/World.Submit.cs`·`World.Commands.cs`·`World.Undo.cs`다.
3. 위 "다음 할 일"을 사용자와 정한다.
