# 인계 메모: 로딩 파이프라인, CLI, 런타임 World, S6 커맨드(진행 중)

마지막 갱신: 2026-10-16. 브랜치 `spec-v0.5-scaffold`, 테스트 1243개 통과(커널 1215, CLI 28). 작업 규칙은 저장소 루트의 `CLAUDE.md`.

## 한 줄 요약

spine-spec S1의 6단계 콘텐츠 로딩 파이프라인(①~⑥)과 CLI(`engine check`·`dump`·`stubs`), 런타임 데이터 층 `World`의 첫 슬라이스가 구현되었다. 결정은 `docs/spec-gaps.md`의 G34~G108에 있고, spec 본문(S1·S2·S4·S9, schema-design, use-cases U16)에도 반영했다. 순수 함수 실행기(G95~G104)를 `World`에 연결했다: `derived` 읽기와 형제 접근(G103), 재진입 감지·깊이 상한(G100), 기여 행 API와 `contribs`(G98), 읽은 값 근거 `Explain`(G105), 함수 기본값과 `derived` 원천 `default_from`(G106). `check`는 함수 본문의 이름 오타도 잡는다(G108). 그 뒤 실행기를 검토해 파이썬과 결과가 다르거나 spec과 어긋난 곳 6가지를 재현하고 고쳤다(아래 "실행기 검토"). 이어서 S6 커맨드 첫 슬라이스를 시작해 선언·사전 검사·인자 검사·쓰기 판정까지 왔다(G109~G118, 아래 "S6 커맨드 진행 상황"). 그 뒤 옛 컨텍스트 바구니(`^Self` 등)가 새 spec에서 어디로 갔는지 대조해 제출자·행위자·입구 정책·`ask` 대상·UI의 나·상태 안 값 목록을 정했다(G119~G129). 마지막으로 첫 슬라이스를 마쳤다: 입구 선언(G130), 제출 API `World.Submit`(G131), 본문 실행(G132), 런타임 안전망(G117 보강), 커맨드 단위 롤백(G133). 이제 자유 커맨드가 게임 상태를 바꿀 수 있다. 이어서 스크립트의 구조 변경(`spawn`·`destroy`·`attach`·`detach`·`add_row`·`remove_row`, G134), 쓰기 함수의 결과 받기(G135), 구조 변경의 롤백, `int` 키 컴포넌트와 `replace_rows`·`append_row`(G129 구현)를 했다(아래 "구조 변경 슬라이스").

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
| 함수 효과(쓰기 함수) 판정 (②b 끝, 모드마다) | `Script/Execution/FunctionEffects`, `FunctionValue.WriteWitness`·`IsCommand`, `PatchOperationCheck.Mismatch`(Cond 자리) | G117·G118·G135 |
| 스크립트의 구조 변경, 컴포넌트 값 생성자, `int` 키 줄 헬퍼 | `Runtime/World.Structure`(내장 8개, `ComponentInitValue`), `PureBuiltins.WritingNames`, `PureBuiltin.Writes`, `spawn` 리터럴 링크는 `FunctionNameChecker`가 모아 `Linker`가 봄 | G129·G134·G135 |

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

### 제출자·행위자 정리와 컨텍스트 바구니 해체 (G119~G129, 2026-10-15, 문서만 — 코드 미반영)

옛 엔진의 `ScopeContext`(키 → 값 바구니)가 하던 일이 새 spec에서 어디로 흩어졌는지 대조하다가, `^Self` 하나가 맡던 다섯 역할(보낸 연결 · 행동하는 진영 · 비용 지불자 · 질문받는 쪽 · UI의 나)이 spec에서 `me` 하나로 뭉쳐 ❓로 남은 것을 정리했다(G119~G127). 이어서 바구니가 통째 복사로 넘기던 값(스택 노드, 주사위 눈)의 자리를 정했다(G128·G129). spine-spec S2·S4·S6·S10·X1·X2, schema-design 3.3.2, use-cases U1·U2·U5·U6을 고쳤다.

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
| G127 | UI의 "나" = 클라이언트가 보고 있는 엔티티, UI 로컬(시뮬레이션 상태 아님). 호스트·UI가 설정, 후보는 정책 함수로. 근거: 옛 `"Player"` 세션 별칭, OpenTTD `_local_company` |
| G128 | `std.stack`의 대기 행동 표현은 모드 몫(방향: 노드 엔티티에 행동 Def id + 인자, 해소 때 Def의 `Action` 함수 호출). 커널 몫은 이걸 커널 기능만으로 만들 수 있게 하는 것. Flow 사이 대기 기능은 지금 안 넣음 |
| G129 | 키 컴포넌트에 `int` 키 허용(항상 `listed`), 커널 헬퍼 `replace_rows`·`append_row`, 번호 다시 매기기 = 지우고 새로 만들기(그 줄의 기여 행도 지움). 리스트 칼럼·상태 안 맵은 안 엶 |
| G130 | `acts_for` 람다는 커맨드 매개변수를 이름으로 골라 받음(`lambda unit: unit.owner`), 자유 커맨드에만. 정책은 `@submit_policy`(교체 `override=True`), `(issuer, target)`. **코드 반영함** |
| G131 | `Submit`의 결과는 모두 값 `SubmitResult`(성공·Request 필요·인자 거절·입구 거부·전제 조건 실패·스크립트 오류), 예외는 없는 커맨드 id뿐. 채운 전체 인자를 담음. 정책 필수는 호스트 옵션. **코드 반영함** |
| G132 | 본문의 속성 대입 = `World.Set`과 같은 검사의 상태 쓰기, 키 컴포넌트는 있는 줄만. 사전 조건과 본문은 한 실행(지역 변수 이어짐). 리스트 `+=`는 상태에 리스트가 없어 생기지 않음. **코드 반영함** |
| G133 | 롤백은 되돌리기 기록(undo 로그): 커맨드 실행 중 값 칸·`keys="all"` 줄 기록·역방향 색인 변경을 기록, 예외면 되감음. **코드 반영함** |

**로더 쪽 구현 (G130, 2026-10-15):** `acts_for` 람다는 커맨드 매개변수를 이름으로 골라 받는다(`acts_for=lambda unit: unit.owner`). 자유 커맨드에 `acts_for`가 없거나, 자유 커맨드가 아닌데 있거나, 람다가 커맨드에 없는 이름을 받으면 `CMD001`이다(`CommandDecl.ActsFor`, `CommandSignatures.CheckActsFor`). 정책 함수는 `@submit_policy`/`@submit_policy(override=True)`이고 `(issuer, target)` 두 매개변수를 받는다. 개수·교체는 Def 교체와 같은 규칙으로 `BuildDefs` 고정점 안에서 판정한다(`ContentLoader.RegisterPolicy`, `CMD005`). 결과는 `ContentLoadResult.SubmitPolicy`, 쓰기 함수면 `CMD004`다. 테스트는 `Loading/SubmitGateDeclarationTests`.

**제출 API (G131, 2026-10-15):** `World.Submit(issuer, command, args)` → `SubmitResult`(`Runtime/World.Submit.cs`). 순서는 자유 커맨드 확인 → 인자 검사 → `acts_for` → `may_act` → 전제 조건이다. `Outcome`은 `Succeeded`/`NeedsRequest`/`ArgumentsRejected`/`GateDenied`/`PreconditionsFailed`/`ScriptError`이고, 결과에 채운 전체 인자(`Arguments`), 대상(`Target`), `Preconditions`, `Error`를 담는다. 예외는 없는 커맨드 id뿐이다. `new World(loaded, requireSubmitPolicy: true)`에 정책이 없으면 `RUN016`이다. 테스트는 `Runtime/WorldSubmitTests`. 전제 조건을 통과하면 본문까지 실행한다(G132), 실패하면 되감는다(G133).

**아직 안 정한 것:**

- ~~**Def·Flow 경계를 넘는 값 넘기기**~~ → 정리됨. 같은 실행 안(TR `PaidExtraOP`)은 `Action` 함수 인자, `ask`를 사이에 둔 값은 Flow 지역 변수(S2), 다른 Flow·참가자가 보고 고치는 값(스택 노드, 주사위 눈)은 상태: 노드 표현은 `std.stack` 몫(G128), 인자 묶음은 런타임 컴포넌트, 값 목록은 `int` 키 컴포넌트(G129). 스택의 취소·환불은 `std.stack`을 만들 때 정한다(모드 몫).
- ~~**G129 구현**~~: 구현함(아래 "구조 변경 슬라이스").
- 표기: `ask` 반환값의 필드 이름. (`acts_for`·`submit_policy`는 G130으로 정함)

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다. 런타임도 생성 순간에 구멍을 막는다(G93).
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## S6 첫 슬라이스 마무리 (끝남, 2026-10-15)

1. ~~**제출 API의 모양**~~: G130·G131로 정하고 구현함(`World.Submit`, 위 "제출 API").
2. ~~**본문 실행**~~: G132로 정하고 구현함. `PureEvaluator.RunCommand`가 사전 조건 → 본문을 한 실행으로 돌리고, 본문의 속성 대입은 `IRuntimeAssignable`(컴포넌트·줄 뷰) → `World.WriteMember` → `Set`/`SetRow`. 테스트 `Runtime/WorldCommandBodyTests`.
3. ~~**런타임 안전망**~~: 구현함(G117 보강). `Frame.Writable`: 커맨드 본문, 쓰기 모드 프레임이 문장으로 부른 쓰기 함수만 쓰기 모드. 순수 함수 `apply(f, u)`를 거쳐 쓰기 함수 값이 쓰던 구멍을 재현하고 막음(`RUN003`). 최상위 대입으로 숨긴 커맨드(`x = Heal`)는 재현해 보니 이미 로드 때 `CMD004`로 잡힘. 함수 값으로 커맨드를 부르는 런타임 방어선은 넣었지만 그 길을 만들 방법을 찾지 못해 테스트가 없다.
4. ~~**커맨드 단위 롤백(C8)**~~: G133으로 정하고 구현함(`Runtime/World.Undo.cs`의 `Atomically`·`Assign`·`Journal`). 테스트는 롤백을 끈 채로 실패하는 것까지 확인함.
5. ~~**`me`의 정체(S2 ❓)**~~: G119~G127로 정함(`me` 없음). 위 "제출자·행위자 정리".

## 구조 변경 슬라이스 (끝남, 2026-10-16)

결정은 사용자와 정했다(G134·G135 `확인`). 테스트는 `Runtime/WorldStructureTests`, `Runtime/WorldStructureRollbackTests`, `Runtime/WorldIntKeyTests`.

1. **표기 (G134):** 이름 붙은 커널 내장 `u = spawn("Def", Health=Health(current=3))`, `destroy(u)`, `attach(u, Wounded(turns=3))`, `detach(u.Wounded)`, `add_row(u.Skill, key, Skill(...))`, `remove_row(u.Skill, key)`. 컴포넌트 값 생성자는 로직에서도 쓰는 이름(그 모드에 보이는 것만, 아니면 SCH003). `spawn`의 리터럴 Def id는 ⑤가 검사(없음·데이터 전용·abstract·안 보임). 키 줄은 `spawn`에 넣지 않고 `add_row`로.
2. **쓰기 판정 (G135):** 쓰기 함수는 한 줄 전체나 이름 대입의 오른쪽 전체(`u = make_scout(c)`)로 부른다. 쓰는 내장을 부르면 쓰기 함수로 판정(로드 시점 CMD004). 함수 값으로 우회하면 실행 중 안전망(RUN003). `pay(...)`는 `paid = pay(...)` 뒤 `if paid:`.
3. **롤백:** 6가지 구조 변경이 모두 흔적을 남기던 것을 재현하고 고쳤다. 생성(엔티티·타입 목록·id 카운터: 실패한 커맨드는 id를 쓰지 않음), 삭제(기여 행·남의 엔티티 키 줄), 부착·분리(값·줄·역방향 색인·기여 행), 줄(값·기여 행·키 보유 색인), 기여 행 추가를 되돌리기 기록에 넣었다. 연산이 스스로 실패해 정리한 뒤 되감기가 다시 지워도 같은 상태(테스트 있음).
4. **G129 구현:** `component("Die", key=int, ...)`는 항상 listed(`keys="all"`은 SCH004), Def 리터럴·패치에 줄을 적을 수 없음(런타임 목록). 스크립트는 `node.Die[i]`, 키 순 순회, `row.key`는 int, 없는 칸은 None. `replace_rows(node.Die, [Die(...), ...])`는 원소를 모두 검사한 뒤 줄을 지우고 0부터 다시 만들고(그 줄의 기여 행도 지워짐), `append_row(node.Die[, Die(...)])`는 가장 큰 키 + 1. 리스트 원소는 컴포넌트 값 모양(G134 표기를 따름, 사용자 동의). 호스트 API는 `long` 키 오버로드와 `RowInts`.

**알려진 한계:** `RemoveRow`(엔티티 키)는 키 보유 색인(`_keyHolders`)에서 지우지 않는다(삭제 때 건너뛰므로 동작은 맞음). 키 줄 안의 Ref 역방향 색인은 여전히 없다.

## Flow 슬라이스 (진행 중, 2026-10-16)

사용자가 `ask`/`wait`를 다음 슬라이스로 골랐다(실행기를 멈출 수 있게 바꾸는 것이 반응·저장·`choose`의 전제라서).

1. **G136(확인):** `ask`/`await`/`wait`는 쓰기 함수와 같은 규칙. 포함한 함수(또는 그런 함수를 문장으로 부르는 함수)는 멈추는 함수이고 한 줄 전체나 `x = f()`로만 부른다. 식 안·순수 자리는 로드 오류(아직 미구현, `ask`와 함께).
2. **실행기 리팩터링(끝남, 동작 불변):** `PureEvaluator`의 문장 실행을 데이터로 옮겼다. `Frame.Blocks`(블록마다 다음 문장 번호, `for`면 굳힌 항목·위치), `Frame.Result`(버림·지역 변수·호스트)와 실행 고리 `RunUntil`/`Step`. 문장 수준 `def` 호출은 프레임만 쌓고, 식 안의 호출은 끝까지 실행한다. 단계 수를 고정한 테스트(`TheStepCountOfAProgramIsPinned`, 667)가 리팩터링 전후로 같다.
3. **G137(확인):** 선택 방법(창·지도 강조 클릭)은 스크립트가 `hint=`로 제안만 하고 UI 모듈이 정한다. 커널은 해석하지 않는다. 질문에 자유 데이터 `data=`. 근거는 TR의 고정 클래스(`PendingChoiceData`) 문제.
4. **G138(확인):** `ask(who, title, choices=[...], responses=[...], hint=, data=)`, 돌려받는 `r.response`·`r.index`·`r.choice`·`r.by`.
5. **`ask`(choices만) 구현(끝남):** `Runtime/World.Flows.cs`.
   - `ask`는 `PureBuiltin { Writes, Suspends }`라 로드 판정은 쓰는 내장과 같다. 메시지만 "waits for an answer".
   - 실행기는 질문을 들고 멈추고(`PureEvaluator.Suspended` = 프레임 스택 + 답을 받을 변수), 이어서 실행할 때는 사본에서 돈다(`Resume`).
   - `World`가 Flow 표(`_flows`, 번호 카운터 `_nextFlow`·`_nextRequest`, 모두 되돌리기 기록)를 들고 있다.
   - `Submit` 결과의 `OpenedRequest`, `World.OpenRequests`.
   - `World.Answer(issuer, request, by, choice)` → `AnswerResult`(`UnknownRequest`·`NotListed`·`GateDenied`·`BadChoice`·`ScriptError`). 이어서 실행한 구간이 실패하면 효과와 Flow가 되감기고 질문은 그대로 열려 있다.
   - 테스트는 `Runtime/WorldAskTests`(C2·C3·C8·C9). Flow 되감기 기록을 빼면 실패하는 것까지 확인했다.
6. **G139(확인)와 응답 커맨드 구현(끝남):**
   - `acts_for`는 모든 커맨드에 필수가 되었다. 응답 커맨드는 `None`을 쓸 수 없다. 기존 테스트의 맨 `@command`는 `@command(free=True, acts_for=None)`으로 바꿨다.
   - `ask(..., responses=["Pass", "Bid"])`: 이름은 부른 함수의 모드에서 찾고(`PureBuiltin.CallerBody`가 부른 쪽 모드를 받음), 다른 모드 것은 `"모드.이름"`. 없는 커맨드나 자유 커맨드는 실행 중 오류다(로드 시점 리터럴 검사는 아직 없음).
   - `World.Respond(issuer, request, command, args)` 순서: 질문 → 받는 커맨드인가(`NotAccepted`) → 인자 → `acts_for` → 목록 → 정책 → 사전 조건 → 본문 → Flow 재개, 모두 한 트랜잭션. `r.response`는 `ask`에 적은 이름.
   - 응답 커맨드 본문이 `ask`하면 아직 오류다(spec은 "기다리던 Flow의 스택 위에서 실행"이라 중첩 멈춤이 가능해야 함). 단계 예산은 응답 본문과 재개 구간이 따로다.
7. **남은 것:** 시간(`blocks_time`·`deadline`·`on_timeout`·`wait`·`advance`), 고를 수 없는 선택지 표시, 동시 고르기(시나리오 ②), 엔티티가 죽은 뒤의 Flow(`on_stale`). 저장은 아래 저장 슬라이스.
8. 재개 뒤 람다가 재개 전 사본의 지역 변수를 보던 버그를 재현해 고쳤다(`CopyStack`이 클로저를 사본 프레임으로 다시 이음, 테스트 `ALambdaSeesLocalsAssignedAfterResume`).

## 저장 슬라이스 (진행 중, 2026-10-16)

G140(정규형 JSON을 엔진이 직접 씀)·G141(멈춘 Flow의 런타임 값도 전부 저장)을 사용자가 골랐다. 네 항목으로 나눠 하나씩 한다: ① 월드 상태(C4) ② 람다를 뺀 Flow(C1) ③ 람다 ④ 함수 본문 해시와 C5 거부.

1. **① 월드 상태(끝남, 커밋 전):** `Runtime/CanonicalJson.cs`(손으로 쓴 쓰기·읽기, 수는 `long`만, 들여쓰기 2칸 고정)와 `Runtime/World.Save.cs`.
   - `World.Save()` → `byte[]`. 커맨드·함수 실행 중이면 거부하고, 멈춘 Flow가 있으면 아직 거부한다(②에서 채움).
   - 형식: `format`·`mods`(id, version)·`counters`(entity·contribution·flow·request)·`entities`(id 순, `def`·`runtime`·`values`(컴포넌트·멤버 이름 순, 쓴 값만)·`rows`(`key`·`attached`·`values`))·`contributions`. 값 태그는 null·bool·수(int)·문자열·배열(리스트)·`{"fixed": raw}`·`{"entity": id}`·`{"dict": [[k, v]]}`·`{"text", "args"}`.
   - `World.Load(loaded, bytes)` → `WorldLoadResult`(예외 대신 진단). 원본 기록을 채운 뒤 색인(`_byType`·`_inverse`·`_keyHolders`·기여 색인)을 훑어 다시 만든다. `SAV001` 깨진 저장, `SAV002` 지금 내용과 어긋남(없어진 Def·컴포넌트·멤버, 타입이 바뀐 값, 없어진 키 Def, 새로 생겼는데 정적 기본값이 없는 멤버. 모두 모아 한 번에), `SAV003` 모드 목록 차이(경고만, S8 "Def 값 변경은 항상 허용").
   - 테스트 `Runtime/WorldSaveTests`: 고약한 월드로 C4 바이트 동일, 불러온 월드가 같게 읽힘, 카운터 유지, 색인 재구성, 골든 JSON, 어긋남 목록, 2^53+1 보존.
   - 범위(`min`·`max`)도 불러올 때 다시 검사한다(G92 보강, 사용자가 G92를 짚어 줌).
2. **G142(확인):** `==`는 뷰·답·`range`·컴포넌트 값·기여 행을 가리키는 대상이나 내용으로 비교한다(전에는 객체 비교라 `u.Health == u.Health`가 거짓이었고, 저장 뒤 `x == y`가 바뀌어 C1이 깨질 뻔했다). 테스트 `Runtime/WorldEqualityTests`.
3. **② 람다를 뺀 Flow(끝남, 커밋 전):** `Runtime/World.SaveFlows.cs`. 저장에 `flows`가 생겼다(골든도 갱신).
   - Flow마다 `id`·`command`·`request`(질문 전체)·`frames`. 프레임은 `mod`·`function`(그 이름이 그 함수에 묶여 있는지 저장 때 확인)·`locals`·`blocks`(`next`, `if` 갈래 번호 `branch`, `for`면 `index`와 `items`나 `range`).
   - 부른 위치·결과 자리(`x = f()`의 x)·쓰기 모드·답 변수는 부른 문장에서 다시 계산한다(`Relink`). 저장할 때 계산이 실제 프레임과 다르면 엔진 결함으로 예외.
   - 지역 변수 값 태그: `entity_view`(상태의 `entity`와 구별), `component`(엔티티·이름·줄 키), `rows`, `def_view`, `answer`, `component_value`, `contribution`, `range`, `function`(모드·이름), `builtin`. 람다(`ClosureValue`)는 저장이 예외(③).
   - 불러올 때 함수·커맨드·응답 커맨드·컴포넌트가 없거나 멈춘 자리가 본문에 없으면 `SAV002`.
   - 테스트 `Runtime/WorldSaveFlowTests`: 시나리오를 저장 없이 한 번, 대기 지점마다 저장→새 내용으로 불러오기→이어서 한 번씩 돌려 걸음별 결과(오류 문구 포함)·열린 질문·최종 저장 바이트가 같은지(C1)와 저장 순간의 C4. 일부러 고장(쓰기 모드, 엔티티 뷰)을 넣어 실패하는 것도 확인했다.
   - 한계: 모드 최상위 이름이 아닌 로드 시점 값(Def 리터럴 등)이 지역 변수에 있으면 저장이 예외다.
   - 사용자에게 아직 묻지 않은 것: 칼럼 삭제(지금 거부, S8 최종은 고아 영역), Def가 더 이상 적지 않는 Def 줄 기록(보이지 않게 남음), 새 멤버의 함수 기본값·`default_from`(지금 거부), 모드 내용 해시(④와 함께?).

## 다음 할 일 (사용자와 고를 것)

자유 커맨드가 값과 구조를 바꿀 수 있게 됐다. 다음 후보(미룬 것):

- **`ask`/`wait`(Flow 중단·재개)**와 응답 커맨드(G125·G126, `NeedsRequest`가 지금 받는 자리): C1~C3과 시나리오 ①②④의 전제. → 위 Flow 슬라이스로 진행 중.
- `cost`·`choose`(사전 조건 블록 안, 지금 “미지원”).
- 반응(S5), 저장(S8).
- 로직용 `.pyi`(아래 남은 일 2): 쓰기 API 모양이 정해졌으므로 이제 해도 다시 고칠 일이 적다.

### (참고) 첫 슬라이스를 시작할 때의 합의 (2026-10-15)

spec 척추 기준으로 어디까지 왔나:

| 영역 | 상태 |
|---|---|
| S1 로딩, S9 진단(일부), CLI | 끝남 |
| S4 데이터 층(`World`), `derived`·기여 행 | 끝남 |
| S2 순수 함수(읽기만) | 끝남, 검토 마침 |
| S6 커맨드 | 자유 커맨드의 선언·입구·사전 검사·본문 실행·롤백, 스크립트의 구조 변경까지 끝남(G109~G135). 응답 커맨드, `cost`·`choose` 없음 |
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

1. `git pull` 후 `dotnet test`로 1205개 통과(커널 1177, CLI 28)를 확인한다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(끝의 G109~G135), `docs/spine-spec.md` S2·S4·S6·7절(C7·C8)을 읽는다. 커맨드 코드는 `Runtime/World.Submit.cs`(제출), `Runtime/World.Commands.cs`(인자·사전 검사), `Runtime/World.Undo.cs`(롤백), `Runtime/World.Structure.cs`(구조 변경 내장), `Script/Execution/PureEvaluator.cs`(`RunCommand`, `Frame.Writable`), `Script/Execution/FunctionEffects.cs`, `Loading/CommandSignatures.cs`다.
3. 위 "다음 할 일 (사용자와 고를 것)"에서 사용자와 다음 항목을 고른다.
