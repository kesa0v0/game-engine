# 인계 메모: 현재 상태와 다음 할 일

마지막 갱신: 2026-10-26. 브랜치 `spec-v0.5-scaffold`, 테스트 2560개 통과(커널 2329, CLI 42, 콘솔 호스트 100, 번역 55, 경계 34). 모더용 안내서는 `docs/modding/`(G252, 예시 모드 `samples/guide`). 작업 규칙은 저장소 루트의 `CLAUDE.md`. 슬라이스마다의 자세한 기록(무엇을 어떻게 했고 무엇을 확인했나)은 `docs/history.md`에 날짜순으로 있다. 이 문서는 지금 상태와 다음 할 일만 둔다.

## 한 줄 요약

**커널은 혼자서 게임 한 판을 돌릴 수 있다. 번역(X4)과 UI의 행동(`@ui_action`)은 끝났고, 첫 실제 호스트는 Unity로 정했다(G323, 경계 어셈블리 ①~③까지 끝).** 엔진 무관한 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, 실행 파일 `kesa-play`)와 예시 게임 `samples/skirmish`를 두었다.

| 영역 | 상태 |
|---|---|
| S1 콘텐츠 로딩 ①~⑥, CLI(`check`·`dump`·`stubs`), S9 진단(일부) | 끝남 |
| S3·S4 데이터(엔티티·컴포넌트·키 줄·Ref·역방향), `derived`·기여 행, `Explain` | 끝남 |
| S2 로직 언어(순수 함수, `for`·`while`, 컴프리헨션, `fixed`, 시드 난수, `query`) | 끝남 |
| S6 커맨드(입구 정책, 사전 조건, `cost`·`pay`·`choose`, 롤백, `LegalMoves`) | 끝남 |
| Flow(`ask`·응답 커맨드·`wait`·기한·대상 소멸 취소), S5 반응(`@on`·`@when`), X2 시간 | 끝남 |
| 숨긴 정보(보기 규칙 `@view_rule`, `ViewAs`의 거른 읽기·결과·합법 수·`Explain`) | 끝남 (G196~G206, 여러 모드의 규칙 겹치기 G315, 몰래 보기는 엔진 기능 없이 상태 + 보기 규칙 G322) |
| S8 저장(C1·C4·C5, 함수 본문 해시, 지운 것 버리기, Def 삭제, 내용 해시, Flow 취소 후 로드) | 끝남, `migrate`만 미룸(실제 모드 업데이트 사례가 생기면 정함) |
| 로직용 `.pyi`(모드별 스텁, pyright 확인) | 끝남 (G207~G209) |
| 적합성 C1~C10, 검증 시나리오 ①~④ | 모두 통과 |
| **S7 호스트 함수** | 결정 G215~G222, ①~⑦ 구현 끝(남긴 것은 아래 "다음 할 일") |
| 규모 벤치(spec 9절, G223) | 첫 판 측정함 (history의 "규모 벤치 첫 판") |
| 스크립트 속도(실행 예산 G224·G225, 실행기 구조 G226) | 끝남 (커밋 `cea92f6`, history의 "G226 실행기 구조 6단계"). 값 구조체화는 백본 ECS화와 함께 맨 끝 |
| 텍스트 콘솔 호스트(핫시트, 게임 지식 없이 공개 API만) + 예시 게임 `samples/skirmish` | 첫 판 돎. 드러난 API 구멍은 아래 "콘솔 호스트에서 드러난 구멍" |
| S10 UI가 보여 줄 값 읽기(`Call`·`ViewAs(…).Call`·영수증, G244~G248) | 끝남. UI 정의 파일은 호스트마다 제 형식(G249), 콘솔은 `ui/console.txt`(G250), 도장은 저장된 값의 것(G235·G251) |
| X4 번역 파일·포매터(`src/Kesa.Engine.Text`, `check`의 `LOC`, 콘솔 `--lang`) | 끝남 (G254~G275). 커맨드 이름은 엔진이 번역하지 않음(버튼 글은 호스트 UI 파일, G287) |
| S10 UI의 행동(`@ui_action`·`ui_state`·`submit`, 콘솔의 `[화면]` 메뉴) | 끝남 (G276~G286). 덮어쓰기·패치는 미룸 |

결정은 `docs/spec-gaps.md`의 G1~G328. 기능을 더하거나 바꾸면 `docs/modding/`의 해당 장과 9장 "아직 없는 것"도 고친다(예시는 `samples/`에서 가져오고 `GuideSampleTests`·`SkirmishTests`가 돌린다).

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). id는 `모드id:경로`이고 `:` 없이 쓰면 쓴 코드의 모드 기준, 실행 중 값은 언제나 전체 이름(G258~G266). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다. 런타임도 생성 순간에 구멍을 막는다(G93).
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## 다음 할 일 (사용자와 고를 것)

**바로 이어서 할 것 (2026-10-26): Unity 호스트(S10).** 첫 실제 호스트를 Unity로 정함(G323). 호스트는 엔진 무관한 좁은 경계로만 커널을 부른다(G324): 연결 세션과 운영 둘(G325), 경계 너머 값은 작은 어셈블리의 닫힌 값 목록이고 엔티티는 id와 Def id(G326), 호출은 비동기 `ValueTask`이고 같은 프로세스 구현은 이미 끝난 결과(G327), Unity에는 빌드한 DLL을 담은 저장소 안의 로컬 UPM 패키지(G328). 이미 정한 것을 경계에 맞게 구현할 것(묻지 않음): 닫힌 영수증(G246)은 세션이 들고 해석기가 붙인 내부 키로 "바뀐 키 목록"을 받음, 번역은 호스트 쪽(G269·G270)이고 번역기가 `World` 대신 경계 값을 받음(엔티티의 Def가 값에 있음), `Translations.Load`는 운영이 주는 모드 목록으로. UI 파일은 G249대로 UXML/USS를 실행 중 모드 폴더에서 읽는 해석기(이전 엔진 `UIInterpreter`·`RuntimeStyleSheet`처럼, 경로 대신 화면 함수 이름). **다음:** 구현을 조각으로(① 값 타입·JSON, ② 세션·운영 인터페이스(`IHostSession`·`IHostOperator`)와 같은 프로세스 구현(`LocalHost`), ③ 번역기를 경계 값으로(`Translator`가 `BridgeValue`를 받음, `Translations.Load(BridgeContent)`, 운영의 `Content()`) 끝남. 남은 것: ④ 콘솔 호스트를 경계로 옮김 ⑤ UPM 패키지와 빌드 복사), 그 뒤 UXML 해석기의 속성 모양(호스트가 정할 몫)과 첫 화면을 사용자와 정함. 스키마를 뒤지는 콘솔의 엔티티 늘어놓기는 경계 밖이라 디버그 도구로 둘지 따로 물을 것.

**이어 할 곳 (2026-10-26, ③ 뒤):** ④ 콘솔 호스트를 경계로 옮긴다. `src/Kesa.Engine.ConsoleHost/ConsoleGame.cs`가 지금 `World`·`WorldView`를 바로 부른다. 이것을 `IHostOperator`·`IHostSession`(`Bridge.Local`의 `LocalHost`)과 경계 값 번역(`Translator.Value(BridgeValue)` 등)으로 바꾼다. 패널은 `session.Call(키, …)`과 `Changed()`로 다시 그린다. 경계에 없는 것: `ConsoleGame`의 스키마를 뒤지는 엔티티 늘어놓기(`Version`·`All`·`Has`·`Get`·`Member`)는 경계 밖이다. 이것을 디버그 도구로 둘지, 경계에 넣을지, 뺄지 먼저 사용자에게 묻는다(G325에서 미룬 몫). 2026-10-26에 이 기능이 무엇인지 설명은 마쳤다. 모드가 `ui/`를 주지 않으면 세계 전체를 날것으로 찍고, `ViewAs`를 거치지 않아 숨긴 정보도 보인다. 선택지(판 주인 전용 디버그로 남김 / 보기 규칙을 지키는 읽기를 경계에 넣음 / 빼고 "화면 파일 없음"만 알림)와 추천은 아직 내지 않았다. 다음 세션은 이 셋을 장단점·사례와 함께 내고 고르게 한다. `--seat 연결=Def`가 쓰는 `World.All`은 `SeatChoices()`로 바꾸면 되므로 묻지 않는다. 사용자가 "화면을 어떻게 주나"를 직관적이지 않다고 했다. 설명한 내용: 모드는 화면 파일과 화면 함수를 두고, 호스트가 파일을 읽어 함수를 불러 값을 받는다. 커널은 먼저 보내지 않고 `Changed()`로 바뀐 키만 알린다. 사례는 패러독스 `.gui`의 데이터 함수, RimWorld의 매 프레임 그리기, 이전 엔진 `UIInterpreter`. 영수증은 경계 너머라서 둔 최적화다. Unity 쪽 안내서를 쓸 때 이 설명을 짧게 넣을 것. 다 옮기면 `Translator`의 커널 값 오버로드와 `Bridge.Local` 참조를 거둘지도 본다.

(2026-10-26 정리: 끝난 항목의 긴 단락, 콘솔 호스트 구멍 1~6번, S10 논의 메모, 점검한 남은 일은 `docs/history.md` 끝의 "인계 메모에서 옮긴 지난 기록"으로 옮겼다. 판데믹 brink 변종 카드는 모두 끝났고 다음 고를 후보는 아래 "큰 후보"다.)

큰 후보:

- 위 "콘솔 호스트에서 드러난 구멍"을 하나씩.
- 호스트 함수에서 남긴 것: 인자 8개 초과(2026-10-21 사용자: 사례가 생길 때까지 넘김. 필요하면 `HostOverloads.cs`의 오버로드를 16개까지 늘리면 되고, 이름으로 꺼내기 `call.Int`는 G215에서 접은 이유가 그대로라 하지 않는다), 호스트 함수를 `Cond`·`Action` 칸의 값으로 쓰는 것은 끝(2026-10-21 G291·G292, history의 "호스트 함수를 `Cond` 칸의 값으로"와 "`Action` 칸의 값으로"), 호스트 창구의 `choices`.
- 속도의 작은 후보(이득은 작을 것으로 추정): 람다 없는 함수의 지역 변수 표 다시 쓰기(부를 때마다 약 64B), 문장 수준 호출(`x = f()`)의 결과 칸 번호, 컴프리헨션 이름 공간 프레임 재사용, `contribs()` 빈 목록.
- ~~영수증을 (엔티티, 컴포넌트) 단위로.~~ **끝(2026-10-21, G290 확인):** `Record(id, 이름)`·`CallRead`, 쓸어 보기 테스트 `CallReceiptNarrowingTests`. `@when` 읽기 기록도 같은 단위로 바꿨다(`Touch(id, 이름)`, `_whenReaders`는 엔티티 → 칸 이름 → 조건, 빠뜨림은 `WhenTrackingTests`의 전체 훑기 견주기, 덜 평가하는지는 `WhenNarrowingTests`). history의 "`@when` 읽기 기록을 (엔티티, 컴포넌트) 단위로".
- S10 UI 연동(호스트 엔진 결정과 묶임). 아래 "S10 논의 메모" 참고.

**필요해지면 할 것** (실제 모드 사례가 생기면 정한다):

- 따옴표 없는 Def 참조와 속성 접근 표기(`Resource.Wood`, G63).
- 로직 연산자 확장(G96): 지금은 `**`, 비트 연산, `@`를 거부한다. 복리·레벨 곡선이 필요해지면 `int ** 음이 아닌 int`부터, 플래그가 필요하면 `int` 비트 연산.
- S8 `migrate`(실제 모드 업데이트 사례).
- 값 구조체화와 백본 ECS화(저장 구조를 인터페이스 뒤로, G226, 맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2560개 통과(커널 2329, CLI 42, 콘솔 호스트 100, 번역 55, 경계 34)를 확인한다. 클라우드 세션에서 .NET 9 SDK를 내려받지 못하면 `apt-get install -y dotnet-sdk-10.0`으로 깔고, 저장소의 `global.json`을 피하려고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(색인, 고를 항목의 영역은 `docs/gaps/`의 그 파일), 고를 항목에 맞는 `docs/spine-spec.md` 절을 읽는다. 실행기 구조는 `Script/Execution/PureEvaluator.cs`(입구)·`Session.cs`(+`.Calls`·`.Preconditions`·`.Stepping`)·`Frame.cs`·`Block.cs`·`PureEvaluator.Nodes.cs`(식 노드)·`PureEvaluator.Statements.cs`(문장 노드)·`FrameLayout.cs`·`LocalTable.cs`다. 고른 영역의 지난 기록이 필요하면 `docs/history.md`에서 그 절만 찾아 읽는다(전부 읽을 필요 없음). 옛 커맨드 첫 슬라이스의 읽을 곳: 커맨드 코드는 `Runtime/World.Submit.cs`(제출), `Runtime/World.Commands.cs`(인자·사전 검사), `Runtime/World.Undo.cs`(롤백), `Runtime/World.Structure.cs`(구조 변경 내장), `Script/Execution/PureEvaluator.cs`(`RunCommand`)·`Frame.cs`(`Writable`), `Script/Execution/FunctionEffects.cs`, `Loading/CommandSignatures.cs`다.
3. 위 "다음 할 일 (사용자와 고를 것)"에서 사용자와 다음 항목을 고른다.
