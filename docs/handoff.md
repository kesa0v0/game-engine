# 인계 메모: 로딩 파이프라인, CLI, 런타임 World, S6 커맨드(진행 중)

마지막 갱신: 2026-10-15. 브랜치 `spec-v0.5-scaffold`, 테스트 1057개 통과(커널 1029, CLI 28). 작업 규칙은 저장소 루트의 `CLAUDE.md`.

## 한 줄 요약

spine-spec S1의 6단계 콘텐츠 로딩 파이프라인(①~⑥)과 CLI(`engine check`·`dump`·`stubs`), 런타임 데이터 층 `World`의 첫 슬라이스가 구현되었다. 결정은 `docs/spec-gaps.md`의 G34~G108에 있고, spec 본문(S1·S2·S4·S9, schema-design, use-cases U16)에도 반영했다. 순수 함수 실행기(G95~G104)를 `World`에 연결했다: `derived` 읽기와 형제 접근(G103), 재진입 감지·깊이 상한(G100), 기여 행 API와 `contribs`(G98), 읽은 값 근거 `Explain`(G105), 함수 기본값과 `derived` 원천 `default_from`(G106). `check`는 함수 본문의 이름 오타도 잡는다(G108). 그 뒤 실행기를 검토해 파이썬과 결과가 다르거나 spec과 어긋난 곳 6가지를 재현하고 고쳤다(아래 "실행기 검토"). 이어서 S6 커맨드 첫 슬라이스를 시작해 선언·사전 검사·인자 검사·쓰기 판정까지 왔다(G109~G118, 아래 "S6 커맨드 진행 상황"). 본문 실행은 아직 없다.

## 구현된 것 (`src/Kesa.Engine.Kernel/`)

| 단계 | 위치 | 결정 |
|---|---|---|
| ① 모드 발견·정렬 | `Loading/ModDiscovery`, `ModSorter` | |
| ②a 스키마 / ②b Def 등록 | `Schema/*`, `Loading/DefBuiltins`, `ContentLoader` | G9~G33 |
| ③ 패치 검사 | `PatchResolver`, `PatchOperationCheck`, `PatchConflicts`, `PatchLog`, 가드 `if mod_loaded` | G34~G51 |
| ④ 상속·패치 적용 | `InheritanceExpander`, `DeferredEvaluator`(`inherit` 식), `PatchApplier`, `ResolvedDef` | G52~G62 |
| ⑤ 링크 | `Linker` (DefId 값·줄 키 검사, 값은 안 바꿈) | G63~G66 |
| ⑥ 검증 | `Validator` (VAL001~003), `ValueBlame` | G67·G68·G73·G74·G77 |
| ⑥ 동결 | `DefSet`, `FrozenDef` (`ContentLoadResult.Frozen`) | G69·G70·G77 |
| ⑥ 작성용 `.pyi` | `Stubs/StubGenerator.Generate(modId, result)` | G71·G72 |
| `check` JSON | `Reporting/CheckReport.ToJson` | G75 |
| 최종 Def 덤프 | `Reporting/DefDump.ToJson` | G76 |
| `check` 사람용 텍스트 | `Reporting/CheckReport.ToText` | G79 |
| 스키마 스텁, 비활성 모드 스텁 | `StubGenerator.GenerateSchema`, `GenerateManifestOnly`, `ContentLoadResult.FirstRound` | G82·G83 |
| 함수 본문 파싱, 순수 함수 실행기 | `Script/Parser`(본문, `for`·`return`·조건식), `Script/Execution/*`(`PureEvaluator`, `PureOps`, `PureBuiltins`, `FunctionChecker`), `Numerics/FixedMath`(`fixed` 산술) | G95~G104 |
| 함수 본문 이름 검사 (②b 끝, 모드마다) | `Script/Execution/FunctionNameChecker`(`ModFunctions`에 인터프리터가 함수와 최상위 이름을 모음), `PureBuiltins.RuntimeNames` | G108 |
| 런타임 월드 | `Runtime/World`(+ `World.Derived`·`World.Contributions`·`World.Explain`·`World.Commands` 부분 클래스), `RuntimeTypes`(`EntityId`, `EntityValue`, `MemberHandle`, `Init`, `WorldException`) | G86~G93, G98, G100, G103, G105, G106 |
| 커맨드 선언·사전 검사·인자 검사 | `Script/CommandDecl`(`CommandParameter`), `Interpreter.DeclareCommand`, `FunctionChecker`(`with preconditions:`), `Loading/CommandSignatures`(어노테이션·기본값), `PureEvaluator.RunPreconditions`, `World.CheckPreconditions`(`PreconditionReport`, `ArgumentRejection`) | G109~G116 |
| 함수 효과(쓰기 함수) 판정 (②b 끝, 모드마다) | `Script/Execution/FunctionEffects`, `FunctionValue.WriteWitness`·`IsCommand`, `PatchOperationCheck.Mismatch`(Cond 자리) | G117·G118 |

CLI(`src/Kesa.Engine.Cli/Commands.cs`, 테스트 `tests/Kesa.Engine.Cli.Tests`):

| 명령 | 하는 일 | 결정 |
|---|---|---|
| `engine check <mods> [--json] [--warnings-as-errors]` | 진단을 텍스트(기본)나 JSON으로 stdout에 | G78·G79·G84·G85 |
| `engine dump <mods>` | 덤프 JSON은 stdout, 진단 텍스트는 stderr | G80 |
| `engine stubs <mods>` | 모드마다 `__builtins__.pyi`, `schema/__builtins__.pyi`, 없을 때만 `pyrightconfig.json` | G81~G83 |

종료 코드는 0(오류 없음)·1(콘텐츠 오류)·2(사용법·환경)·3(엔진 예외)이다(G78). 스텁은 pyright 1.1.389로 실제로 확인했다: pyright는 파일에서 가장 가까운 상위 폴더의 `__builtins__.pyi`를 내장 이름으로 쓴다. 확인하려면 `npx -y pyright@1.1.389`(node는 `C:/Users/kesa0/AppData/Roaming/JetBrains/WebStorm2024.3/node/versions/22.14.0`에 있고 PATH에는 없다).

흐름: `ContentLoader.Load`가 회차 루프를 돈다. ③ 이후에 모드가 비활성화되면 그 모드와 의존 모드를 빼고 ②a부터 다시 돈다(G49). 한 회차의 ③~⑥ 검사는 `ExcludeFailedAtPatchStage`에서 같은 `StageDiagnostics`로 모은다.

### 런타임 World (G86~G93)

호스트용 C# API다(테스트 `tests/Kesa.Engine.Kernel.Tests/Runtime/WorldTests.cs`). 스크립트가 쓰는 길(커맨드)은 S6에서 이 위에 얹는다. ECS의 데이터 층에 해당하고 System 자리는 S5의 단계별 신호 핸들러다.

- `new World(loadResult)`, `Member("Health", "current")`로 핸들을 한 번 풀고(오타면 RUN001 + "혹시 …?"), `Create(defId, Init...)`·`Destroy`·`IsAlive`·`DefOf`, `Get`/`Set`, `Add`/`Remove`/`Has`(런타임 컴포넌트), `GetRow`/`SetRow`/`AddRow`/`RemoveRow`/`HasRow`/`RowKeys`/`RowEntities`(키 줄), `All(type)`, `Inverse(target, refHandle)`.
- id는 재사용하지 않는다(G87). 구조 변경은 즉시 보이고 순회는 시작 시점의 목록이다(G88, S4의 "지연"을 바꿈).
- 읽기는 엔티티 값 → Def 값 → 스키마 기본값. 생성 순간에 `default_from`만 계산해 저장한다(G89). 생성 순간에 구멍이 있으면 실패한다: 필수 Ref, 기본값 없는 `field`는 `Init`으로 준다(G91·G93). 쓰기는 타입·범위 검사(G92).
- 오류는 `WorldException`(코드 `RUN001`~`RUN011`). 실패한 호출은 월드를 바꾸지 않는다.
- `derived`는 읽을 때마다 함수를 돌려 계산한다(캐시 없음). 함수는 컴포넌트(키 컴포넌트면 줄)를 받아 자기 멤버, 형제 컴포넌트(`h.Armor.bonus`, 없으면 None), `h.entity`, `h.key`, Ref 너머를 읽는다(G103). `contribs(h, "max")`는 기여 행을 준다. 기여 행 API는 `AddContribution`·`RemoveContribution`·`RemoveBySource`이고 엔티티가 죽으면 지워진다(G98). `Explain(id, member)`는 읽은 값의 트리를 준다(G105). 함수 기본값과 `derived` 원천 `default_from`은 엔티티를 만든 뒤 선언 순서로 계산하고 실패하면 되돌린다(G106). 스키마는 `entity`·`key`라는 멤버와 컴포넌트와 같은 이름의 멤버를 거부한다(G103).

### 실행기 검토 (2026-10-15)

임시 테스트로 재현한 뒤 고쳤다. 결정은 G97·G101·G104·G105의 보강 문장에 있다.

- 대입 전 지역 변수는 같은 이름의 전역·내장으로 넘어가지 않고 오류(파이썬 규칙, `FunctionLocals`를 G108 검사와 공유).
- 순수 함수의 값은 불변(불변식): 리스트 `+=`는 오류.
- 단계 예산은 최상위 읽기 하나에 하나(`StepMeter`), 중첩 `derived`는 남은 단계를 이어 씀.
- `in range`의 `long` 끝 값: .NET 예외가 새거나 틀린 답을 내던 것을 큰 정수로.
- 중첩 `derived` 오류의 흔적: 안쪽 함수, `reading 컴포넌트.값`, 바깥 함수(읽은 위치)로 이어짐.
- `sum(묶음, 시작값)`은 파이썬 시그니처. 키 컴포넌트 줄 묶음은 `dict`처럼 `len`·`in`·비면 거짓, 부착 여부는 `is not None`.
- **아직 안 되는 것:** 키 줄 안의 Ref는 역방향 색인이 없다. 저장(S8)·트랜잭션·벤치는 아직 없다. 내부 저장은 엔티티마다 바뀐 값만 맵으로 둔 단순한 모양이다(저장 방식은 공개 약속이 아님).
- G93은 로더 규칙도 바꿨다: `keys="all"` 줄의 `field`는 고정 기본값이 있어야 하고(`default_from`·함수 기본값 금지), Ref는 `optional`이어야 한다(SCH004).

## S6 커맨드 진행 상황 (2026-10-15)

결정은 모두 사용자와 정했다(spec-gaps 상태 `확인`). 테스트는 `Loading/CommandDeclarationTests`, `Loading/BuiltinShadowingTests`, `Loading/FunctionEffectTests`, `Runtime/WorldPreconditionTests`.

| G | 정한 것 |
|---|---|
| G109 | `@command` / `@command(free=True)`, 커맨드 id는 `모드id.함수이름` (`ContentLoadResult.Commands`) |
| G110·G113 | `require(조건, text("키", **인자))`는 모두 평가, 앞 실패에 기댄 오류는 “평가 안 됨”, 예산·순환 오류는 늘 오류. 메시지는 조건이 거짓일 때만 평가(assert처럼) |
| G111 | `with preconditions:`는 커맨드의 첫 문장, 안에는 `require` 호출과 `이름 = 식`만. `cost`·`choose`는 아직 “미지원” |
| G112 | 엔진 이름(`require`, `text`, `command`, `int` 등)은 덮어쓸 수 있는 내장(파이썬 LEGB). 덮어쓴 모드에서는 그 자리에 못 씀 |
| G114 | 커맨드 매개변수는 어노테이션 필수: `int`·`fixed`·`bool`·`str`·`Ref[X]`·`DefId[X]`·`list[T]`·`dict[K, V]`·“T 또는 None” (CMD003) |
| G115 | 제출 인자가 틀리면 예외가 아니라 `PreconditionReport.Arguments`의 구조화된 거절(빠짐·남음·타입·죽은 엔티티·엔티티 타입·없는 Def·Def 타입). 숫자는 G95(`fixed` 자리의 int는 올림) |
| G116 | 기본값은 커맨드에만, 리터럴만. 로드 시 타입 대조, DefId는 ⑤ 링크, 제출 시 채움(로그에는 채운 뒤 전체 인자) |
| G117 | 쓰기 = 엔티티 상태 경로의 속성 대입. 쓰기 함수는 전이적으로 자동 판정, 문장으로만 부름, 순수 자리(`derived`, 함수 기본값, `Cond`)에 못 둠 (CMD004). 이름으로 알 수 없는 호출은 런타임 안전망(아래 다음 할 일) |
| G118 | 로직에서 커맨드 이름을 부르거나 값으로 쓰면 로드 오류. 공용 효과는 일반 쓰기 함수로 |

진단 코드: CMD001 데코레이터, CMD002 사전 조건 블록, CMD003 매개변수 타입·기본값, CMD004 함수 효과.

### 제출자·행위자 정리 (G119~G126, 2026-10-15, 문서만 — 코드 미반영)

옛 엔진의 `ScopeContext`(키 → 값 바구니)가 하던 일이 새 spec에서 어디로 흩어졌는지 대조하다가, `^Self` 하나가 맡던 다섯 역할(보낸 연결 · 행동하는 진영 · 비용 지불자 · 질문받는 쪽 · UI의 나)이 spec에서 `me` 하나로 뭉쳐 ❓로 남은 것을 정리했다. spine-spec S2·S6·X1·X2와 use-cases U1·U2·U6을 고쳤다.

| G | 정한 것 |
|---|---|
| G119 | issuer는 "어느 연결에서 왔나"이고 행동하는 진영이 아니다. `me.Points` 같은 표기는 틀림 |
| G120 | 엔진은 행위자를 넣어 주지 않는다. 행동하는 진영·비용 지불자는 인자(`party`)나 인자에서 끌어낸 값(`unit.owner`), 고르는 것은 모더 |
| G121 | "연결 A가 진영 P를 맡는다"는 게임 상태 안의 **모드 데이터**(담당 칼럼·상속 규칙·정책은 std나 본편 모드). 커널은 입구에서 모드의 검사를 부를 자리만 연다 |
| G122 | 자리의 모양: 커맨드가 `acts_for`(인자 → 엔티티)를 선언, 모드 하나가 정책 함수 `may_act(issuer, 엔티티)`를 선언. 소유·차례 검사는 전제 조건. 근거 OpenTTD(`client_playas` 일치 검사 + 커맨드마다 `CheckOwnership`), Factorio(권한 그룹은 동작 종류만) |
| G123 | 자유 커맨드는 `acts_for` 필수, 누구로서도 아니면 `acts_for=None`(정책은 `None`으로 불림). 빠지면 로드 오류 |
| G124 | 정책 함수가 없으면 모두 허용(호스트가 "정책 필수"를 켜면 시작 거부). 최대 하나, 교체는 `override=True` |
| G125 | `ask`는 엔티티에게 묻는다. `accepts`는 엔티티 목록, 담당 확인은 응답이 올 때. `ask`는 답한 엔티티를 돌려줌 |
| G126 | 응답은 어느 엔티티로서인지 늘 명시. 시간 초과 주입은 엔티티 `None` |

**코드와 어긋나는 점:** 지금 로더는 `@command(free=True)`를 `acts_for` 없이 받는다(G109 구현). G123대로라면 로드 오류가 되어야 하고, 기존 테스트의 자유 커맨드에도 `acts_for`를 붙여야 한다. 아래 다음 할 일 1번에서 함께 한다.

**아직 안 정한 것:**

- **UI의 "나"**(다섯 번째 역할): 제안은 "UI 로컬의 보고 있는 엔티티, 후보는 정책 함수로 구함, 시뮬레이션 상태에 두지 않음"(핫시트에서 같은 연결의 "나"가 바뀌므로). 사용자 답을 받기 전에 멈췄다.
- **Def·Flow 경계를 넘는 값 넘기기:** 옛 바구니는 통째 복사(`CopyTo`)로 값이 따라갔다(TR `PaidExtraOP`를 커맨드에서 ActionDef로, 주사위 `DiceResults`를 스택 노드에 두고 상대의 재굴림이 `PeekUnder`로 고침). 새 spec은 통째 복사를 없앴고, 이런 값을 Action 함수 인자로 넘길지 스택 노드를 상태로 둘지는 비어 있다(S2 리스트·맵 표의 "키 컴포넌트 줄의 맵" ❓, U5).
- 표기: `acts_for`·`submit_policy` 이름, `ask` 반환값의 필드 이름.

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다. 런타임도 생성 순간에 구멍을 막는다(G93).
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## 다음 할 일: G117 2단계 — 커맨드 본문 실행

S6 첫 슬라이스에서 남은 것, 이 순서로 하나씩 짚는다(spec 빈칸은 선택지와 추천을 내고 사용자가 고른다):

0. **먼저 남은 설계 하나:** 위 "아직 안 정한 것"의 UI의 "나"를 사용자와 정한다(제안이 이미 나가 있음). Def·Flow 경계 값 넘기기는 `ask`/스택을 만들 때 정해도 된다.
1. **제출 API의 모양:** `World`에 커맨드를 제출해 입구(G122: `acts_for` + 정책 함수) → 사전 검사 → 본문 실행까지 하는 입구(이름 후보 `Submit`). issuer는 불투명한 문자열 인자(G119). 돌려주는 결과(입구 거부 / 전제 조건 거절 / 성공 / 스크립트 오류), 자유 커맨드(`free`)만 받을지, 채운 전체 인자(G116)를 결과에 담을지. `acts_for` 필수(G123)와 정책 함수 선언·개수 규칙(G124)의 로더 쪽 구현도 여기서.
2. **본문 실행:** 실행기에 속성 대입 → `World.Set` 연결(복합 대입 포함). 쓸 수 있는 칸은 `field`·Ref(`const`·`derived`는 `World`가 이미 거부). 리스트 칼럼의 `+=`, 키 줄 쓰기(`h.Skill[k].level = …`)를 이번에 열지 정한다.
3. **런타임 안전망(G117에 규칙이 적혀 있음):** 실행 프레임마다 순수/쓰기 모드. `derived` 읽기·사전 조건·`Cond` 호출·순수 함수에서 시작한 호출은 커맨드 실행 중이라도 쓰기를 거부. 최상위 대입으로 숨긴 커맨드(`x = Heal`) 호출도 여기서 막는다(G118).
4. **커맨드 단위 롤백(C8):** 스크립트 오류가 나면 효과를 남기지 않는다. 방식(쓰기 로그 되감기 vs 스냅샷)을 정한다. spec 9절 벤치 항목과 관련.
5. ~~**`me`의 정체(S2 ❓)**~~: G119~G126으로 정함(`me` 없음). 위 "제출자·행위자 정리".
6. 미룸: `ask`/`wait`(Flow 중단·재개), `cost`·`choose`, 반응(S5), 저장(S8), 엔티티 생성·삭제·컴포넌트 부착을 스크립트에서 하는 길.

### (참고) 첫 슬라이스를 시작할 때의 합의 (2026-10-15)

spec 척추 기준으로 어디까지 왔나:

| 영역 | 상태 |
|---|---|
| S1 로딩, S9 진단(일부), CLI | 끝남 |
| S4 데이터 층(`World`), `derived`·기여 행 | 끝남 |
| S2 순수 함수(읽기만) | 끝남, 검토 마침 |
| S6 커맨드 | 선언·사전 검사·인자 검사·쓰기 판정까지(G109~G118), 본문 실행·롤백 없음 |
| S2 Flow(쓰기 실행, `ask`, `wait`, 재개) · S5 반응 · S8 저장 | 없음 |

spine-spec 7절의 적합성 기준 C1~C10과 검증 시나리오 ①~④는 거의 모두 Flow·커맨드·저장을 요구하므로 아직 하나도 검증하지 못했다. 엔진은 콘텐츠를 읽고 값을 계산할 수 있지만 게임을 진행시킬 수는 없다. 로직용 `.pyi`(남은 일 2번)는 스크립트의 쓰기 API 모양이 정해진 뒤에 하는 편이 다시 고칠 일이 적다.

첫 슬라이스 범위(제안, 세부는 spec 빈칸을 하나씩 짚으며 정한다):

- `@command`와 타입 있는 매개변수, `with preconditions:` 안의 `require`(실패 사유를 하나씩 구조화해 돌려줌, C7).
- 본문의 상태 쓰기(`u.Health.current -= 3`). 순수 함수의 불변식(G104)과 어떻게 갈라지는지 정해야 한다.
- 커맨드 단위 롤백: 오류가 나면 효과를 남기지 않음(C8).
- 미룸: `ask`/`wait`(Flow 중단·재개), `cost`·`choose`, 반응(S5), 저장(S8).

읽을 곳: spine-spec S2(커맨드 머리부, 함수 효과 표, 평가 모델, 난수), S6 전체, 7절 C7·C8·C10, use-cases에서 커맨드가 나오는 사례(U1·U2 등). S2의 `me` 정체(❓)는 이 슬라이스나 다음 슬라이스에서 시나리오로 정한다.

## 남은 일 (사용자와 우선순위를 정할 것)

1. **실행 중 이름 오류의 "혹시 …?" 후보:** `PureEvaluator.LookupName`은 후보에 지역 변수와 내장만 넣고 모드 전역을 빼먹는다(코드로 본 추측, 재현 안 함). G108 뒤로는 로드 시점에 대부분 잡히므로 우선순위는 낮다.
2. **로직용 `.pyi`**: 엔티티 접근 클래스, `derived`, `ref` 칼럼, 함수 서명. 스키마 스텁의 `derived` 함수 매개변수 타입도 이때 좁힌다.
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
13. **`require` 뒤의 타입 좁히기(G110·G113):** pyright는 `assert c is not None` 뒤에서는 `c`를 좁히지만 `require(c is not None, ...)` 같은 함수 호출 뒤에서는 좁히지 않는다. 로직용 `.pyi`(2번)를 만들면 블록 안의 `c.walls`에 “None일 수 있음” 경고가 뜬다. 그때 함께 정한다.
14. ~~**커맨드를 함수로 부르기**~~: G118로 정함. 로직에서 커맨드 이름을 부르거나 값으로 쓰면 로드 오류(`CMD004`)다. 최상위 대입(`x = Heal`)처럼 이름으로 알 수 없는 경로는 G117 런타임 안전망(본문 실행 단계)에서 막는다.

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 1057개 통과(커널 1029, CLI 28)를 확인한다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(끝의 G109~G126), `docs/spine-spec.md` S2·S6·7절(C7·C8)을 읽는다. 커맨드 코드는 `Runtime/World.Commands.cs`, `Script/Execution/PureEvaluator.cs`(`RunPreconditions`), `Script/Execution/FunctionEffects.cs`, `Loading/CommandSignatures.cs`다.
3. 위 "다음 할 일"(G117 2단계)의 0번(UI의 "나")부터 시작한다.
   예: "handoff의 다음 할 일 0번, UI의 '나'부터 짚어 줘."
