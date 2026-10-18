# 인계 메모: 현재 상태와 다음 할 일

마지막 갱신: 2026-10-18. 브랜치 `spec-v0.5-scaffold`, 테스트 1919개 통과(커널 1883, CLI 36). 작업 규칙은 저장소 루트의 `CLAUDE.md`. 슬라이스마다의 자세한 기록(무엇을 어떻게 했고 무엇을 확인했나)은 `docs/history.md`에 날짜순으로 있다. 이 문서는 지금 상태와 다음 할 일만 둔다.

## 한 줄 요약

**커널은 혼자서 게임 한 판을 돌릴 수 있다. 바깥(호스트 C# 함수, UI, 번역)과 잇는 부분은 아직 없다.** 테스트 코드가 호스트 역할을 대신한다.

| 영역 | 상태 |
|---|---|
| S1 콘텐츠 로딩 ①~⑥, CLI(`check`·`dump`·`stubs`), S9 진단(일부) | 끝남 |
| S3·S4 데이터(엔티티·컴포넌트·키 줄·Ref·역방향), `derived`·기여 행, `Explain` | 끝남 |
| S2 로직 언어(순수 함수, `for`·`while`, 컴프리헨션, `fixed`, 시드 난수, `query`) | 끝남 |
| S6 커맨드(입구 정책, 사전 조건, `cost`·`pay`·`choose`, 롤백, `LegalMoves`) | 끝남 |
| Flow(`ask`·응답 커맨드·`wait`·기한·대상 소멸 취소), S5 반응(`@on`·`@when`), X2 시간 | 끝남 |
| 숨긴 정보(보기 정책 `may_see`, `ViewAs`의 거른 읽기·결과·합법 수·`Explain`) | 끝남 (G196~G206) |
| S8 저장(C1·C4·C5, 함수 본문 해시, 지운 것 버리기, Def 삭제, 내용 해시, Flow 취소 후 로드) | 끝남, `migrate`만 미룸(실제 모드 업데이트 사례가 생기면 정함) |
| 로직용 `.pyi`(모드별 스텁, pyright 확인) | 끝남 (G207~G209) |
| 적합성 C1~C10, 검증 시나리오 ①~④ | 모두 통과 |
| **S7 호스트 함수** | 결정 G215~G222, ①~⑦ 구현 끝(남긴 것은 아래 "다음 할 일") |
| 규모 벤치(spec 9절, G223) | 첫 판 측정함 (history의 "규모 벤치 첫 판") |
| 스크립트 속도(실행 예산 G224·G225, 실행기 구조 G226) | 끝남 (커밋 `cea92f6`, history의 "G226 실행기 구조 6단계"). 값 구조체화는 백본 ECS화와 함께 맨 끝 |
| X4 번역 파일·포매터, S10 UI 구독 API(호스트 엔진 미정) | 없음 |

결정은 `docs/spec-gaps.md`의 G1~G227.

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다. 런타임도 생성 순간에 구멍을 막는다(G93).
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## 다음 할 일 (사용자와 고를 것)

**방금 끝낸 것:** S7 호스트 함수(G215~G222)와 스크립트 속도(G223 벤치, G224·G225 실행 예산, G226 실행기 구조, G227 P6 바인딩 시점, 커밋 `cea92f6`·`57fd305`). 속도는 여기서 멈추기로 했다(남은 큰 몫은 값 구조체화, 백본 ECS화와 함께 맨 끝). 자세한 것은 history의 "S7 호스트 함수와 스크립트 속도".

**지금 하는 것:** 남은 일 16번(문서와 큰 파일 정리). 첫 단계로 handoff를 이 문서와 `docs/history.md`로 나눴다(2026-10-18).

후보:

- 남은 일 15번: 로드 때 타입 추론으로 속성 오타 잡기(재현됨). 10번(G100 순환 정적 검사)과 같은 타입 정보.
- 호스트 함수에서 남긴 것: 인자 8개 초과용 이름으로 꺼내기(`call.Int`), 콘텐츠 파일 최상위에서 호스트 함수를 값으로 참조(G221, 지금은 "이름 없음" 오류. Def의 `Cond`·`Action` 칸은 스크립트 함수만 받으므로 따로 정해야 함), 호스트 창구의 `choices`.
- 속도의 작은 후보(이득은 작을 것으로 추정): 람다 없는 함수의 지역 변수 표 다시 쓰기(부를 때마다 약 64B), 문장 수준 호출(`x = f()`)의 결과 칸 번호, 컴프리헨션 이름 공간 프레임 재사용, `contribs()` 빈 목록.
- X4 로컬라이제이션(번역 파일 형식·폴백·포매터, spec은 "v1 직전 재확인").
- S10 UI 구독 API(호스트 엔진 결정과 묶임).
- S8 `migrate`(실제 모드 업데이트 사례가 생기면).
- 아래 "남은 일"의 작은 것들.

## 남은 일 (사용자와 우선순위를 정할 것)

1. **실행 중 이름 오류의 "혹시 …?" 후보:** `PureEvaluator.LookupName`은 후보에 지역 변수와 내장만 넣고 모드 전역을 빼먹는다(코드로 본 추측, 재현 안 함). G108 뒤로는 로드 시점에 대부분 잡히므로 우선순위는 낮다.
2. **로직용 `.pyi`** (G207·G208, 대부분 했음): 모드 스텁(`StubGenerator.Write`)에 로직 이름(`PureNames`·`LogicNames`: 데코레이터, `preconditions`·`require`·`cost`·`choose`, 쓰는 내장, `ask`의 `Answer`, `random`, `type Ref[T] = T`, `type DefId[T] = str`)과 엔티티 속성(컴포넌트·Def 타입 클래스, 키 컴포넌트는 `_X_rows`, Def 상수는 `_T_consts`), Def 타입별 `query`·`spawn` 오버로드. 스키마 스텁에 순수 내장. `pyrightconfig.json`은 None 계열을 경고로(G208). **확인 방법:** 시나리오 ①~④와 `WorldViewTests`의 스크립트(6개)를 모드로 내보내 `engine stubs` 뒤 `npx -y pyright@1.1.389`로 오류 0(경고 3, 시나리오 ①의 `optional` Ref)이었고 일부러 넣은 오타 7개는 모두 오류였다. 내보낸 방법: 임시 NUnit 테스트가 각 테스트 클래스의 `Schema`·`Defs` 상수를 리플렉션으로 읽어 파일로 씀(지웠음, 다시 필요하면 같은 방식). `EveryNameScriptsCanUseIsDeclared`가 엔진 이름 목록과 스텁을 대조한다. 스키마 스텁도 모드마다 그 모드가 보는 클래스를 담는다(G209, `GenerateSchema(모드 id, 결과)`): `def hmax(h: Health)`가 통과하고 `h.base_maxx` 같은 오타는 오류다. **남은 것(작은 것):** 스키마 파일에서 `Health(...)`를 값으로 부르는 잘못은 스텁이 막지 않는다(엔진이 로드 때 잡음).
3. **스텁의 이름 충돌**: 멤버 이름이 `id`/`self` 같은 예약 이름이면 중복 인자가 생기고, Def 타입·컴포넌트 이름이 `Mod`·`use`·`use_all`·`patch`·`mod_loaded`·`inherit`이면 스텁 안에서 겹친다(G82 알려진 한계). 스키마 단계에서 막는 것이 유력하다.
4. S9의 나머지 "왜?" 도구 4가지(파생값 추적, 전제 조건 보고서, 반응 추적, 단계 순서 덤프). 각자 `engine` 명령이 될 자리다(G80).
5. **G32 규칙**(`Ref`=엔티티, `DefId`=Def)을 spec 본문으로 옮기는 일. 예시만 고쳐 둔 상태다.
6. 덤프의 `patches`가 부모 체인 전체를 훑어, 자식이 같은 값을 직접 적어 영향이 없었던 부모 패치도 나온다(G76 알려진 한계). `dump`에서 특정 Def만 고르는 옵션도 미뤘다(G80).
7. 속성 접근 표기(`Resource.Wood`, 따옴표 없는 Def 참조)는 후속 과제다(G63).
8. 진단 코드 체계(S9 열린 질문, G6 잠정).
9. 종료 코드 3(엔진 내부 예외) 경로는 테스트가 없다.
10. **`derived` 순환의 로드 시점 정적 검사(G100):** 지금은 실행 중 재진입 감지만 계획했다. `check`가 순환을 잡으려면 로직용 `.pyi`(2번)와 같은 타입 정보로 한다.
11. **컴프리헨션과 `derived` 안의 `all(Type)`:** G102가 컴프리헨션을 미뤘으므로 `derived`에서 전역 질의(`all(Type)`)를 아직 못 쓴다. 컴프리헨션을 열 때 제너레이터 식 미결(schema-design 3.3.1)과 `all(Type)`의 read-set(타입 전체를 읽음으로 기록)을 함께 정한다. 비용은 벤치(spec 9절)로 닫는다.
12. **로직 연산자 확장 후보(G96):** 지금은 `**`, 비트 연산, `@`를 거부한다. 복리·레벨 곡선이 필요해지면 `int ** 음이 아닌 int`부터, 플래그가 필요하면 `int` 비트 연산을 연다.
13. ~~**`require` 뒤의 타입 좁히기(G110·G113)**~~ → G208: "None일 수 있음"은 경고로 낮춘다(좁히지는 않는다).
14. ~~**커맨드를 함수로 부르기**~~: G118로 정함. 로직에서 커맨드 이름을 부르거나 값으로 쓰면 로드 오류(`CMD004`)다. 최상위 대입(`x = Heal`)처럼 이름으로 알 수 없는 경로는 G117 런타임 안전망(본문 실행 단계)에서 막는다.
15. **로드 때 타입 추론으로 `check`가 속성 오타 잡기 (10번 G100과 같은 타입 정보).** 재현(2026-10-18): `for u in query("Unit"): u.Fod.amount += 1`(`Food` 오타)을 `engine check`가 오류 0개로 통과시킨다. `u`의 타입을 로드 때 몰라서 실행해야 오류가 된다. `.pyi` 스텁과 pyright로는 잡힐 것으로 보이나(이 예로는 확인 안 함) pyright를 안 쓰는 모더는 놓친다. 속도 목적은 아니다(P6은 첫 실행 때 자리마다 푸는 것으로 정함, G227). 언제 할지는 다른 후보와 함께 고른다.
16. **문서와 큰 파일 정리.** 문서가 너무 길어졌다(2026-10-18 기준 `spec-gaps.md` 300KB·234줄로 표 한 행이 수천 자, `handoff.md` 120KB, `spine-spec.md` 71KB, `schema-design.md` 43KB). handoff는 날짜순 슬라이스 기록이 쌓여 "지금 상태와 다음 할 일"을 찾기 어렵다. 큰 소스 파일: `Script/Execution/PureEvaluator.cs` 1506줄, `Script/Parser.cs` 1214줄, `Runtime/World.cs` 1199줄, `Runtime/World.SaveFlows.cs` 1067줄, `Script/Interpreter.cs` 985줄, `Runtime/World.Flows.cs` 937줄, `Loading/ContentLoader.cs` 921줄, 테스트는 `Runtime/TimeTests.cs` 1016줄. 정리 방식(예: handoff를 현재 상태 문서와 지난 기록으로 나누기, spec-gaps를 영역별 파일로 나누거나 반영된 행을 spec으로 옮기고 줄이기, 큰 클래스를 partial 파일이나 역할별 클래스로 나누기)은 사용자와 정한다. 동작은 바꾸지 않으므로 테스트 전체 통과로 확인한다. **진행:** handoff를 이 문서(현재 상태)와 `docs/history.md`(지난 기록)로 나눴다(2026-10-18, handoff 120KB → 약 12KB). spec-gaps는 색인(`docs/spec-gaps.md`, 약 25KB)과 영역 파일(`docs/gaps/`, 내용 그대로 옮김, 칸마다 대조함)로 나눴다. 색인 영역 표에 "무엇을 정했나" 요약 칸을 226행 모두 채웠다(요약은 "어떻게 정했나"를 줄이기만, 색인 약 69KB). 큰 소스 파일은 두 단계로 하기로 했다: 1단계 partial 파일로 크게 나누기(코드는 옮기기만, 줄 대조와 테스트로 확인), 2단계 나눈 파일 안의 논리적 덩어리를 제자리 타입으로 구조화(파일마다 제안하고 하나씩, 저장 구조를 인터페이스 뒤로 옮기는 일은 G226대로 맨 끝이라 앞당기지 않음). 1단계 끝(2026-10-18): World.cs → +Rows·Values·Records, PureEvaluator.cs의 `Session` → +Calls·Preconditions·Stepping, Parser.cs → +Compound·Expressions, World.SaveFlows.cs → +LoadFlows, Interpreter.cs → +Declarations, World.Flows.cs → +Ask, ContentLoader.cs → +Mods·Build, 테스트 TimeTests → +Stale, ContentLoaderTests → +Registration. 바뀐 코드는 `partial` 수식어뿐이고 이제 가장 큰 파일은 `World.Save.cs` 900줄. 다음은 2단계.

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 1919개 통과(커널 1883, CLI 36)를 확인한다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(색인, 고를 항목의 영역은 `docs/gaps/`의 그 파일), 고를 항목에 맞는 `docs/spine-spec.md` 절을 읽는다. 실행기 구조는 `Script/Execution/PureEvaluator.cs`(세션·프레임·블록)·`PureEvaluator.Nodes.cs`(식 노드)·`PureEvaluator.Statements.cs`(문장 노드)·`FrameLayout.cs`·`LocalTable.cs`다. 고른 영역의 지난 기록이 필요하면 `docs/history.md`에서 그 절만 찾아 읽는다(전부 읽을 필요 없음). 옛 커맨드 첫 슬라이스의 읽을 곳: 커맨드 코드는 `Runtime/World.Submit.cs`(제출), `Runtime/World.Commands.cs`(인자·사전 검사), `Runtime/World.Undo.cs`(롤백), `Runtime/World.Structure.cs`(구조 변경 내장), `Script/Execution/PureEvaluator.cs`(`RunCommand`, `Frame.Writable`), `Script/Execution/FunctionEffects.cs`, `Loading/CommandSignatures.cs`다.
3. 위 "다음 할 일 (사용자와 고를 것)"에서 사용자와 다음 항목을 고른다.
