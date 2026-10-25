# 지난 기록: 슬라이스별 구현 기록 (날짜순)

지금 상태와 다음 할 일은 `docs/handoff.md`에 있다. 이 문서는 슬라이스마다 무엇을 어떻게 했는지의 자세한 기록이다(2026-10-18에 handoff에서 옮김, 내용은 고치지 않음). 새 슬라이스를 마치면 끝에 덧붙인다.

## 처음 슬라이스들

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

## S6 커맨드 진행 상황 (끝남, 2026-10-15)

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

### 제출자·행위자 정리와 컨텍스트 바구니 해체 (G119~G129, 2026-10-15, 코드는 G130~G133에서 반영)

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

## Flow 슬라이스 (끝남, 2026-10-16)

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

## 저장 슬라이스 (끝남, 2026-10-16. ①② 커밋 6fdf11b, ③ 3b37a7c)

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
4. **③ 람다(끝남, 커밋 전), G143(확인, 파이썬처럼 객체 비교를 지키고 같은 객체임을 보존):**
   - 재개 버그 셋을 재현해 고쳤다(`CopyStack`): `f = lambda: 1; g = f` 뒤 재개하면 `f == g`가 거짓, 끝난 함수의 묶음이 잡은 람다가 재개 뒤 낡은 지역 변수를 읽음, 컴포넌트 값·`text` 안의 람다가 사본으로 바뀌지 않음. 원본 람다 하나에 사본 하나, 끝난 함수의 묶음도 옮긴 사본을 쓴다. 저장이 들어가는 곳(`SaveScriptValue`)과 사본이 들어가는 곳(`Remap`)은 같아야 한다(C1). 컴포넌트 값은 `IValueContainer`로 들어간다.
   - `text(...)`의 `==`가 객체 비교였다: G142를 따라 내용 비교로(G142 행 보강).
   - 저장: Flow마다 `closures`(`lambda`: [모드, 든 함수, 순서 번호], `env`: `{"frame": i}` 또는 `{"env": k}`)와 `envs`(끝난 함수의 묶음: `names`, `locals`). 값은 `{"closure": n}`. 처음 만난 순서로 번호를 매겨 같은 객체는 같은 번호, 자기를 잡은 묶음도 적힌다. 순서 번호는 `Script/Execution/LambdaOrdinals.cs`(구조 순서, 공백·주석과 무관).
   - 불러오기: 빈 프레임 → 빈 묶음 → 람다 → 값 채우기. 든 함수나 그 번호의 람다가 없으면 `SAV002`.
   - 테스트: C1 시나리오에 늦게 묶이는 람다, 끝난 함수가 잡은 람다, 자기를 잡은 묶음, 도우미 프레임의 람다, 컴포넌트 값·`text` 안의 람다, 람다 목록을 도는 `for` 중간 멈춤, 동일성 비교를 더했다. 응답 커맨드를 기다리는 Flow의 C1(`Respond`로 이어 가기, 답 `r.response`가 든 채 저장)과 응답 커맨드가 없어진 `SAV002`. 일부러 고장(람다마다 새 객체, 프레임 묶음 대신 복사본)을 넣어 실패하는 것도 확인했다.
   - **④에 넘긴 것(④에서 함):** 람다 순서 번호는 든 함수의 구조에 기대므로 본문 해시는 멈춘 프레임의 함수만이 아니라 저장된 람다가 든 함수(`closures`의 [모드, 함수])도 덮어야 한다.
5. **④ 함수 본문 해시와 C5 거부(끝남, 커밋 전), G144(확인, 위치를 뺀 구조로 해시):**
   - `Script/Execution/BodyHash.cs`: 매개변수·반환 표기·본문을 노드 종류와 길이를 앞에 붙여 이어 쓰고 SHA-256 앞 16바이트를 16진수로. 공백·주석·줄·괄호·함수 순서·데코레이터는 무관.
   - 저장: 프레임마다 `hash`(그 함수), `closures` 항목마다 `hash`(람다가 든 함수). 불러올 때 다르면 `SAV004`(새 코드 `SaveFunctionChanged`), 함수마다 한 번, 프레임에서 걸려도 람다가 든 함수까지 마저 보고한다.
   - 테스트: 주석·빈 줄·공백·괄호·함수 순서만 바꾼 내용으로 모든 걸음에서 불러와 이어도 같은 결과(오류 위치의 줄 번호만 새 파일을 가리킴), 멈춘 함수의 상수 변경 거부, 끝난 함수가 남긴 람다의 함수 변경 거부, 멈추지 않은 함수 변경은 새 본문으로 실행, 고정 골든 해시(C6).
   - 형식 버전은 1 그대로다. ③까지의 저장(프레임·람다에 `hash`가 없음)은 `SAV001`로 거부된다. 실제 저장이 아직 없어 그대로 두었다(사용자에게 보고함).
   - 독스트링(본문의 문자열 문장)은 문장이라 커서 번호에 들어가고, 글자를 고쳐도 해시가 바뀐다(사용자에게 보고함).
   - 칼럼 삭제는 G210으로 정했다: 기본은 거부, 호스트가 `World.Load(..., dropRemoved: true)`를 주면 지워진 컴포넌트·멤버·Def 목록에서 빠진 컴포넌트·없어진 Def 키 줄·거기 걸린 기여 행을 버리고 `SAV005` 경고(고아 영역은 만들지 않음). 테스트 `WorldSaveTests`의 버리기 셋(버린 뒤 다시 불러오면 경고 없음, 버린 목록은 거부 목록과 같음, 타입 변경은 그래도 거부).
   - Def 삭제는 G211로 정했다: `dropRemoved`면 Def가 없어진 엔티티를 불러올 때 파괴된 것처럼 다룬다(남의 Ref는 죽은 id, 키 줄·기여 행·대상 Flow는 버리고 `SAV005`, 반응 없음). 그것을 가리키는 필수 Ref는 그래도 `SAV002`(`CheckRequiredRefs`). 거부할 때 대상 Flow가 엉뚱한 `SAV001`을 내던 것도 고쳤다(`Involves`). 테스트 `WorldSaveTests`의 두 개, 필수 Ref 검사를 끊으면 실패하는 것을 확인했다.
   - 모드 내용 해시는 G212로 정했다: `Loading/ModContent.cs`(`ModContent`·`ModFile`·`ContentHash`), `ContentLoadResult.Contents`(모드 id → 지문). `ParseFiles`가 읽은 파일(`mod.py` 포함)마다 BOM 빼고 LF로 맞춘 바이트의 SHA-256 128비트, 모드 지문은 `kesa-mod-hash/1` + 경로 순 (경로, 종류, 지문)을 길이 붙여 이은 것. 저장 `mods`에 `hash`·`files`(필수, 형식 1 그대로), 다르면 `SAV003`에 `'banking' 1.0, content changed (changed …, added …, removed …)`. 골든 테스트는 지문을 엔진 코드 없이 다시 계산해 맞춘다. 내용을 바꾸고 불러오는 기존 테스트는 진단 목록을 비교할 때 `SAV003`을 뺀다. 내용이 바뀐 뒤 Save→Load→Save는 지문만 다르다(`WorldSaveFlowTests`의 C4 비교는 지문을 가림).
   - 파일 이름 유니코드 정규화는 G213: `ModDiscovery.Nfc`·`FindOnDisk`(internal, NFC로 같은 이름 → 대소문자만 다른 이름, NFC로 같은 이름이 둘이면 모호), `ContentLoader`의 `ModUnit.DiskPaths`(NFC 경로 → 디스크 실제 경로, 열 때만 씀). 정규화만 다른 두 이름을 만드는 테스트는 그런 이름을 하나로 보는 파일 시스템(macOS APFS)에서 건너뛴다.
   - Flow 취소 후 로드는 G214: `World.Load(..., cancelBrokenFlows: true)`. `RestoreFlows`가 Flow마다 `problems.EnterFlow()`·`LeaveFlow(where, request)`로 감싸고 몸통은 `RestoreFlow`(문제면 null). `LoadProblems.Report`가 Flow 안의 `SAV002`·`SAV004`를 모았다가 옵션이 켜졌으면 `SAV006` 경고 하나("flow 1 (…) was canceled with its request N: 이유; 이유"), 아니면 그대로 오류. Flow 밖의 어긋남은 이 옵션과 무관. 테스트 `WorldSaveFlowTests`의 두 개, 모으기를 끊으면 실패하는 것을 확인했다.
   - 사용자에게 아직 묻지 않은 것: Def가 더 이상 적지 않는 Def 줄 기록(보이지 않게 남음), 새 멤버의 함수 기본값·`default_from`(지금 거부), `migrate`(S8 다음 단계), 불러오며 취소한 Flow를 모드가 알아채는 길(지금 없음, G214).

## 반응 슬라이스 (끝남, 2026-10-16)

사용자가 반응(S5)을 골랐다. 셋으로 나눠 한다: ① `@on` + `emit` + `stage` ② `when` + 정착 ③ 시간(`advance`, `tick`, `wait`, 마감). ① 끝남, 다음은 ②나 ③.

1. **신호 선언과 핸들러 선언(끝남, 커밋 전), G145(확인):**
   - 스키마 `signal("Damaged", target=Ref["Unit"], amount=int)`: `Schema/SchemaModel`(`SignalDecl`, `SignalField`, `SignalType`, `SchemaSet.Signals`), `SchemaBuiltins.DeclareSignal`, `SchemaBuilder`(Def 타입·컴포넌트와 한 이름 공간, 필드 타입 이름 검사).
   - `@on(Damaged)`: `Interpreter.DeclareHandler` → `HandlerDecl`(`Script/CommandDecl.cs`), `ContentLoadResult.Handlers`(로드 순서 → 정의 순서). 매개변수는 신호 인자 중 고른 이름만, 기본값·타입 표기 불가. `stage=`는 "아직".
   - 핸들러는 커맨드가 아니다(`FunctionDef.IsHandler`, `IsCommand`에서 빠짐, 사전 조건 없음). 로직에서 부르거나 값으로 쓰는 것은 커맨드처럼 막았다(`FunctionValue.IsEntry`, 메시지 `@on handler`). 사용자가 확인함(G145 보강).
   - 신호 값 `Damaged(target=u, amount=3)`: `Runtime/World.Signals.cs`. 인자는 모두 필수(G145 A), 커맨드 인자 검사(`Fit`, G115)로 맞춤, 저장 가능한 값으로 들고 `d.amount`로 읽음, `==`는 내용(G142), 저장 태그 `signal{name, values}`(불러올 때 지금 선언과 다시 맞춰 어긋나면 `SAV002`). Def 파일 최상위에서 만들면 오류. `.pyi` 스텁에 신호 클래스.
   - 함수 본문의 컴포넌트·신호 이름 오타에 "혹시 …?" 후보가 없던 것을 재현해 고쳤다(`FunctionNameChecker`의 후보에 보이는 스키마 이름).
   - 테스트: `Runtime/SignalTests.cs`, C1 시나리오에 신호 값(`"signal"` 태그, 로그 `G`).
2. **`emit`의 자리(끝남, 커밋 전), G146(확인):** `emit`은 쓰는 내장(`PureBuiltins.EmitName`, `Writes`)이라 판정은 `spawn` 등과 같고 메시지만 "sends a signal". 인자는 신호 값 하나. `World._emitted` 대기열(발생 순서, 되돌리기 기록), `World.PendingSignalCount`. 테스트 `SignalTests`의 `emit` 부분(되감기 기록을 빼면 실패하는 것까지 확인).
3. **핸들러 실행(끝남, 커밋 전), G147(연쇄 세대 상한)·G148(결과 `Reactions`)·G149(핸들러에 `acts_for` 없음)·G150(상한은 호스트 API `maxSignalDepth`, 기본 64), 모두 확인:** `Runtime/World.Reactions.cs`.
   - `Submit`·`Answer`·`Respond`가 트랜잭션을 마친 뒤(성공이나 `ask`로 멈춤) `RunReactions`가 대기열을 앞에서부터 비운다. 신호마다 핸들러를 로드 순서 → 정의 순서로, 각자 새 Flow·각자 트랜잭션(`Atomically`)·각자 단계 예산으로. 핸들러가 낸 신호는 뒤에 붙고 세대 + 1. 답·응답이 이어서 실행한 구간이 낸 신호는 1세대. 대기열은 `finally`로 반드시 비운다.
   - 결과 `Reaction`(신호, 값, 핸들러 id, 세대, `Finished`·`Suspended`(질문)·`ScriptError`·`Dropped`(메시지에 고리 경로, 힌트에 `wait`·`tick`·`maxSignalDepth`)).
   - 핸들러가 `ask`로 멈추면 Flow가 남는다(`FlowRecord.Handler`, `Request.Command`는 핸들러 id). 저장은 `"handler": id`(커맨드 Flow는 그대로 `"command"`), 핸들러가 없어지면 `SAV002`.
   - `@on(..., acts_for=)`는 로드 오류.
   - 테스트 `Runtime/WorldReactionTests`(시점, 순서와 연쇄, 실패 격리, 실패한 커맨드, `ask`로 멈춘 커맨드, 핸들러의 `ask`와 답, 기본 상한 64, 호스트 상한, 상호 고리 경로, 넓은 연쇄는 안 셈, 저장·불러오기 C1·C4, 핸들러 삭제 `SAV002`).
   - 재현해 본 것: 같은 모드의 같은 이름 핸들러는 이미 로드 오류(EVL004).
4. **사라진 대상, G151(확인, 끝남, 커밋 전):** 핸들러가 받는 매개변수 중 사라진 엔티티가 있으면 돌리지 않고 `ReactionOutcome.Canceled`(오류 아님, 이유 메시지). 받지 않는 값은 보지 않는다. 테스트 `WorldReactionTests`의 "사라진 대상". `on_stale=` 옵션과 멈춘 Flow의 대상 소멸은 시간 슬라이스로.
5. **단계(stage), G152(문자열 id + 모드 접두사, 스키마 파일에서 선언)·G153(필수 참조는 의존 필요, 선택 참조는 불필요), 모두 확인, 끝남, 커밋 전:**
   - `Schema/SchemaModel.cs`의 `StageDecl`, `SchemaSet.StageOrder`·`TryGetStage`·`StageIndex`. `SchemaBuiltins.DeclareStage`(접두사 검사). `SchemaBuilder.OrderStages`(중복 SCH001, 없는 단계 SCH002 + 후보, 의존 없음 SCH003, 자기 참조·사이클 SCH004; Kahn 정렬, 동률은 id 순; 사이클은 앞 단계를 거슬러 찾아 가장 작은 id부터 보고).
   - 핸들러 `@on(Tick, stage="core.economy")`: `Interpreter.DeclareHandler`가 문자열만 받고, `Loading/HandlerStages.Check`가 ②b에서 있는지·의존을 본다. `World.SetHandlers`가 단계 순서 → 받은 순서(로드 → 정의), 단계 없는 핸들러는 맨 뒤로 정렬.
   - 스키마 스텁에 `def stage(...)`.
   - 테스트 `Runtime/StageTests`(정렬과 동률, 끼워 넣기, 접두사, 중복, 오타 후보, 자기 참조, 인자 오타, 의존, 선택 참조, 사이클 경로·관련 위치, 모드를 건넌 사이클, 핸들러 stage 검사, 실행 순서). 정렬을 끄면 순서 테스트가 실패하는 것까지 확인했다.
   - 반응 ①(`@on`·`emit`·`stage`)이 끝났다. 사용자가 ③(시간)을 먼저 골랐다(②의 정착 지점 하나가 틱 끝이라서).

## 시간 슬라이스 (반응 ③, 끝남, 2026-10-16)

나눠서 한다: ① 틱·`Advance`·`Tick`·`now()` ② `wait(ticks=n)`(자는 Flow, 저장) ③ `blocks_time`·`deadline`·`on_timeout` ④ 멈춘 Flow의 대상 소멸(`on_stale`), 스크립트의 `advance`.

1. **틱과 `Advance`(끝남, 커밋 전), G154(엔진이 `Tick`을 값 없이 선언, 지금 틱은 `now()`)·G155(`now()`는 어디서나, 읽으면 Explain에 `now() = n`)·G156(스크립트는 `Tick()`을 만들 수 없음), 모두 확인:**
   - `Schema/EngineSignals.cs`(이름 `Tick`, 선언 모드 자리는 `""`). `SchemaBuilder.RegisterNames`가 먼저 등록하고, 모드가 같은 이름(타입·컴포넌트·신호)을 쓰면 SCH001. `ModVisibility.CanSee`는 엔진 것을 모두에게 보인다.
   - `Tick(...)` 호출: 함수 본문에 적혀 있으면 `FunctionNameChecker`가 CMD004, 함수 값을 거치면 실행 오류. 스텁은 `class Tick: ...`(생성자 없음).
   - `Runtime/World.Time.cs`: `World.Tick`, `World.Advance(n)` → `AdvanceResult(Advanced, Tick, Reactions)`. 틱마다 +1 → `Tick` 1세대로 대기열 → `RunReactions`. 만료 스윕(표준 모드 몫)·깨우기·정착은 아직 자리만. 커맨드 실행 중이면 호출자 오류.
   - `now()`(`PureBuiltins.NowName`), `ExplainKind.Time`.
   - 저장에 최상위 `"tick"`(골든 갱신). 그 전 저장은 SAV001(실제 저장 없음).
   - 테스트 `Runtime/TimeTests`(11개).
2. **`wait(ticks=n)`(끝남, 커밋 전), G157(깨어난 Flow가 실패하면 그 구간을 되감고 Flow를 버리고 결과에 남김, 확인):**
   - 쓰는·멈추는 내장(`PureBuiltins.WaitName`, 문장 자리만, G136). `ticks=`만 받고 1 이상 int. 질문을 열지 않는다.
   - `FlowRecord.Request`가 null이고 `WakeAt`(멈춘 틱 + n)이 있는 Flow가 "자는 Flow". `OpenRequests`·답·응답은 자는 Flow를 보지 않는다.
   - `Advance`가 틱 +1 뒤 `WakeSleepers`: `WakeAt <= 틱`인 Flow를 Flow id 순으로 각자 트랜잭션으로 이어서 실행하고, Flow마다 그 뒤 `RunReactions`. 그다음 `Tick` 핸들러. 결과에는 `Reaction`(신호 자리 `Reaction.WakeSignal` = `"wait"`, 핸들러 자리에 Flow를 만든 커맨드·핸들러 id, `Reaction.Flow`에 Flow 번호).
   - 핸들러가 `wait`하면 `Suspended`(질문 없음). 오류 문구는 "(after waking)".
   - 저장: 자는 Flow는 `"request"` 대신 `"wake_at"`. 불러올 때 지금 틱 이하면 형식 오류.
   - 테스트 `TimeTests`에 13개 더(모두 24개). ask와 wait가 저장을 사이에 두고 번갈아 바뀌는 경우, 깨어난 뒤 신호가 1세대로 다시 시작하는 경우 포함.
   - G158(확인): 잠들었으면 결과에 깨울 틱 `SleepsUntil`. `SubmitResult`·`AnswerResult`(답·응답)·`Reaction`(잠든 핸들러, 다시 잠든 깨어난 Flow). 결과 종류는 그대로 `Succeeded`. 응답 커맨드 자체가 `wait`하면 아직 지원 안 함 오류(문구가 `wait()`를 가리킨다).
3. **마감(진행 중):**
   - G159(확인): `ask(..., blocks_time=True/False)`. 적지 않으면 마감 유무로 정한다(지금은 마감이 없어 늘 막음). `Request.BlocksTime`, 저장 `"blocks_time"`.
   - `Advance`는 매 틱 전에 막는 질문을 보고, 있으면 예외 없이 멈춘다. 결과의 `Advanced`가 실제로 간 틱 수, `BlockedBy`(질문 번호 순)·`Blocked`. spec X2의 "거부"를 이렇게 읽었고 사용자에게 알렸다.
   - G160(확인): `deadline=n`은 몇 틱 뒤(1 이상 int). 열릴 때 지금 틱 + n으로 굳어 `Request.Deadline`, 저장 `"deadline"`(없으면 null). 마감이 있으면 기본은 막지 않고, `blocks_time=True`와 함께 쓰면 실행 오류.
   - G161(확인): `on_timeout=`은 선택지 값(같은 값이 여럿이면 앞의 것)이나 응답 이름. `Request.TimeoutChoice`/`TimeoutResponse`(번호)·`TimesOut`, 저장 `"on_timeout": {"choice": i}` 또는 `{"response": i}`. 마감 없이 쓰거나, 어디에도 없거나, 둘 다에 맞으면 실행 오류.
   - 주입: `WakeSleepers`가 잠이 끝난 Flow와 기본 답 있는 마감이 지난 질문을 함께 Flow id 순으로. 반응 항목 신호 자리 `Reaction.TimeoutSignal` = `"timeout"`, 오류 문구 "(after its deadline)". 응답 이름이면 응답 커맨드 본문은 돌지 않는다. 실패하면 G157과 같이 되감고 Flow를 버린다(사용자에게 알림).
   - 불러오기 검사: 막으면서 마감 있음, 마감 없이 기본 답, 기본 답 있는 마감이 이미 지남은 형식 오류.
   - G162(확인): `deadline=`을 쓰면 `on_timeout=`도 적어야 한다(없으면 실행 오류, 불러오기에서는 형식 오류). 그래서 마감이 있는 질문은 늘 기본 답이 있다.
   - 테스트 `TimeTests`에 24개(모두 48개).
   - ③ 끝(커밋 `0e09f45`).
4. **멈춘 Flow의 대상 소멸(진행 중, 커밋 전):**
   - G163(확인): Flow의 대상 = 만들 때 받은 커맨드 인자·핸들러 매개변수 중 엔티티(`FlowRecord.Subjects`, 저장 `"subjects"`), 그리고 질문을 든 Flow면 답할 엔티티 전부. `StaleReason`.
   - G164(확인): `Destroy`가 `_destroyedSinceSweep`을 세우고, `RunReactions` 끝(모든 제출·답·응답·깨우기·틱 핸들러 뒤)과 `Advance`의 매 틱 전에 `SweepStale`이 대상이 사라진 Flow를 버린다. 결과에 신호 자리 `Reaction.StaleSignal` = `"stale"`, `Canceled`, 이유. 깨우기·마감 때도 같은 검사. 대상이 사라진 Flow가 든 저장은 형식 오류.
   - 결과의 `OpenedRequest`·`SleepsUntil`은 신호 처리와 취소 확인 뒤의 상태로 채운다(`StillOpen`).
   - 재현해서 고친 것: 답할 엔티티가 모두 사라진 막는 질문이 `Advance`를 0틱에 멈췄고, 파괴된 엔티티로서 한 답이 받아졌다.
   - G165(확인): `wait(ticks=n, about=...)`·`ask(..., about=...)`. 엔티티 하나나 목록, 적으면 그번 멈춤의 대상이 정확히 그것(`FlowRecord.About`, 저장 `"about"`, 없으면 null). 다음 멈춤은 그 멈춤이 정한다. 답할 엔티티 규칙은 그대로. 사라진 엔티티를 적으면 실행 오류. 재현: about 없이 간접 대상(마을의 군주)이 사라지면 깨어나 스크립트 오류였다.
   - 테스트 `TimeTests`에 19개(모두 67개). `about=`을 무시하게 바꾸면 4개가 실패하는 것을 확인했다.
   - G166(확인): `@command`·`@on`의 `on_stale=`은 이름만 예약(CMD 로드 오류, `Interpreter.OnStaleName`).
5. **스크립트의 `advance(n)`(끝남, 커밋 전), G167(확인):**
   - 멈추는 쓰기 내장(`PureBuiltins.AdvanceName`, 문장 자리만). Park가 `FlowRecord.AdvanceBy`로 두고 `_advancers`에 넣는다(저장되지 않음, 저장하려 하면 엔진 오류).
   - 제출·답·응답은 `RunReactions` 대신 `AfterRun`: 신호 처리 뒤 `_advancers`를 멈춘 순서대로 `AdvanceTicks`로 진행하고 `Resume`(깨우기·마감과 같은 이어 가기, 실패하면 G157)으로 실제 간 틱 수를 돌려준다. 결과 항목 신호 자리 `Reaction.AdvanceSignal` = `"advance"`.
   - `_advancing`(시간 진행 중) 또는 `_reacting`(신호 핸들러 첫 실행)이면 실행 오류. 답으로 이어진 핸들러 Flow에서는 부를 수 있다.
   - 결과의 `OpenedRequest`는 이제 "호출이 끝났을 때 그 Flow가 든 질문"(`HeldRequest`): `advance` 뒤 같은 호출에서 연 질문도 나온다.
   - 테스트 `TimeTests`에 12개(모두 81개 + 앞의 2개).
   - 호출당 상한(G168): 제출·답·응답 하나가 `advance`로 요청한 틱의 합이 `maxAdvancePerCall`(호스트 설정, 기본 10000, `World` 생성자·`Load` 인자)을 넘으면 그 Flow를 버리고 `"advance"` ScriptError 항목을 남긴다. 그때까지 간 틱과 효과는 남는다. 막혀서 덜 갔어도 요청한 만큼 센다. 테스트 4개 추가(모두 85개).
   - 시간 슬라이스 끝(커밋 `7b0d5c9`).

## 반응 ②: `when`과 정착 (끝남, 커밋 `e9a65d6`·`f44f472`, 2026-10-16)

1. **기본 `when`(끝남), G169(주어: 매개변수 없으면 전역, `Ref[Def타입]` 하나면 그 타입의 엔티티마다)·G170(거짓→참 기록은 저장 구역, 기록 없으면 거짓으로 봄)·G171(정착 상한 `maxSettleRounds`, 기본 64, 넘으면 `Dropped`, 버린 것은 기록 안 함)·G172(본문 하나가 끝날 때마다 모든 조건을 다시 봄, 회차 = 세대), 모두 확인:**
   - 선언: `Interpreter.DeclareWhen` → `WhenDecl`(`Script/CommandDecl.cs`, `Condition`·`Stage`·`Subject`). `FunctionDef.IsWhen`/`FunctionValue.IsWhen`(입구, 로직에서 못 부름, 사전 조건 없음). 조건이 쓰는 함수면 `FunctionEffects`가 CMD 오류. 매개변수 타입은 `CommandSignatures.ResolveWhens`, 단계는 `HandlerStages.Check`(핸들러와 같은 검사). `ContentLoadResult.Whens`.
   - 런타임 `Runtime/World.When.cs`: `Settle`이 `AfterRun` 끝(제출·답·응답 뒤)과 `AdvanceTicks`의 매 틱 끝에서 돈다. `Look`이 모든 조건을 보고 `Pending`(회차, 원인) 목록을 갱신, 가장 이른 회차 → 단계 순서 → 엔티티 순으로 하나씩 `RunWhen`(핸들러처럼 새 Flow·트랜잭션, 결과 신호 자리 `Reaction.WhenSignal` = `"when"`, 세대 0, 본문이 낸 신호는 1세대). 조건 오류는 정착마다 한 번 `ScriptError`. 본문이 실패해도 발동한 것으로 기록한다(매 정착마다 되풀이 안 함).
   - `when` 본문 첫 실행에서는 `advance()` 금지(`_settling`).
   - 저장 최상위 `"when": [{"reaction", "entity"(전역은 null)}]`(골든 갱신). 불러올 때 사라진 반응·엔티티, 전역/엔티티별이 바뀐 반응의 기록은 버린다.
   - 다시 평가는 아래 ⑤처럼 바뀌었을 수 있는 조건만 한다.
   - 테스트 `Runtime/WhenTests`(29개). 재현했던 것: 차례마다 한 번만 보면 부활/저주가 HP 0으로 조용히 끝났다(G172의 이유).
2. **`repeat=True`(끝남), G173(확인):** 틱 끝 정착에서만 보고 참이면 틱마다 한 번(`WhenDecl.Repeat`, `Settle(..., tickEnd)`). 기록 대신 그 정착에서 돌았는지(`repeated`)만 본다. 저장에 남지 않는다. 테스트 5개(모두 34개).
3. **`cooldown=N`(끝남), G174(확인):** 발동 뒤 N틱 동안 그 (반응, 대상)의 조건을 거짓으로 본다(`WhenDecl.Cooldown`, `_whenCooldown`, `Cooling`). 끝나는 정착에서 아직 참이면 발동. 저장 최상위 `"when_cooldown"`(골든 갱신). 테스트 6개(모두 40개).
4. **`once`(끝남), G175(확인):** `once=True`는 (반응, 엔티티)마다, `once="game"`은 반응 전체에 한 번(`WhenDecl.Once`·`WhenOnce`, `_whenDone`, `Done`). `repeat`·`cooldown`과 함께면 로드 오류. 저장 최상위 `"when_once"`(골든 갱신). 테스트 7개(모두 47개).
   - 여기까지 커밋 `e9a65d6`.
5. **S5 남은 열린 질문(커밋 전):**
   - G176(확인): `prevent_default`(남의 규칙을 미리 막는 훅)는 두지 않는다. G177(확인): `wait` 중 Flow를 취소하는 전용 기능은 두지 않고 상태로 표현한다. 둘 다 문서 결정이라 코드는 없다.
   - **"읽은 칸이 바뀔 때만 재평가"(S5, 결과가 같은 최적화라 G 번호 없음):** `World.When.cs`의 `Look`. 조건을 평가할 때 읽은 엔티티(`Record`, Ref 값을 읽으면 그 대상도)와 `now()`를 읽었는지를 `WhenCache`에 남기고 색인(`_whenReaders`, `_whenTickReaders`)에 건다. 쓰기는 `Touch(id)`로 표시한다: 값 대입(`Assign`의 owner), 생성·삭제(삭제된 엔티티를 줄 키로 가진 엔티티도), 컴포넌트 붙이기·떼기, 줄 넣기·빼기, 역방향 컬렉션(대상), 기여 행(대상). 정착 때 다시 평가하는 것은 표시된 엔티티를 읽은 조건, 아직 평가하지 않은 (반응, 엔티티), 틱이 바뀌었으면 `now()`를 읽은 조건, 지난번 오류 난 조건뿐이고, 새 월드·불러온 월드는 처음 한 번 모두 평가한다. 판정도 바뀌었을 수 있는 것만(다시 평가한 것, 쿨다운 중인 것, 틱 끝이면 참인 repeat, 상한에 걸려 버린 것 `_whenRetry`, 방금 once로 막힌 반응의 기다리는 것).
   - 견주기 테스트 `Runtime/WhenTrackingTests`: 같은 무작위 단계 400개(시드 4개)를 `WhenFullScan = true`(매번 모두 평가) 월드와 견주어 단계마다 결과 항목과 저장 바이트가 같은지 본다. 50단계마다 추적 쪽을 저장·불러오기로 바꾼다. `Touch` 12곳과 읽기 기록 3곳을 하나씩 빼면 모두 실패하는 것을 확인했다. 이 테스트로 찾은 차이: once 반응이 발동 뒤 참 기록을 남겨 저장이 달랐다 → once는 참 기록을 남기지 않는다.
   - 잰 것(임시 측정, 지움): 조용한 틱(엔티티 1000 × 반응 5, 100틱)이 틱당 약 6.7ms → 0ms. 엔티티 1000개가 한꺼번에 발동하는 정착이 1497ms → 48ms.
   - 여기까지 커밋 `f44f472`.
   - **`field`가 `derived` 상한을 넘는 경우(커밋 전):** 이미 G99(확인, 게임이 고름)로 정해져 있었고 spec·schema-design에 🟡로 남아 있던 것을 고쳤다. G99가 "S5가 생긴 뒤"로 미룬 패턴 B(정착 반응으로 저장값 자르기)가 되는지 테스트 `WhenTests.ASettleReactionClampsAFieldWhenItsDerivedCapGoesDown`로 확인했다: 커맨드 안에서 상한이 내려가면 그 제출의 정착에서 바로 자르고, 호스트가 커맨드 밖에서 내리면 다음 정착 지점까지 넘은 채로 읽힌다. `std.stats`의 편의 함수는 표준 모드 작업이다. spec S5의 열린 질문 줄도 G176·G177과 재평가 비용으로 닫았다.
   - S5의 열린 질문은 이것으로 끝났다. spec S5에 🟡로 남은 줄(단계 없는 핸들러 순서, `when` 정착 지점, `emit` 시점, 트랜잭션 단위)은 구현과 G 번호가 있는데 표시만 남은 것이다(정리 필요).
   - 여기까지 커밋 `d4703fe`.

## 적합성 점검과 난수 (커밋 `d908c91`, 2026-10-17)

spec 7절 C1~C10을 테스트와 맞춰 봤다: C2·C3·C9(`WorldAskTests`), C4·C5(`WorldSaveTests`·`WorldSaveFlowTests`), C7(`WorldPreconditionTests`), C8(롤백 테스트들)은 확인되어 있었다. 난수가 없어서 C10과 C1의 "난수까지", C6의 시드가 비어 있었다. 검증 시나리오 ①~④는 런타임에서 끝까지 돌린 테스트가 없다(③은 `choose`가 있어야 합법 수를 나열한다).

1. **난수(끝남), G178(부를 수 있는 곳: Flow 본문과 그 본문이 부른 함수, 식 안도 됨, 순수 자리는 로드 오류)·G179(파이썬과 같은 `random` 이름 공간)·G180(호스트가 주는 월드 시드)·G181(H = ChaCha20, RFC 8439)·G182(정의 id마다 실행 횟수, 실패하면 되감음), 모두 확인:**
   - `Numerics/ChaCha20.cs`(블록 함수). `Runtime/World.Random.cs`: `_seed`(생성자 `seed:`, `World.Seed`), `_randCounts`(정의 id → 실행 횟수), 지금 Flow의 `RandStream`(키, n). 본문이 시작될 때 `PureEvaluator.RunCommand(..., bodyStarts)` 콜백이 `StartStream(id)`로 횟수를 올린다(되돌리기 기록). 커맨드(`EvaluateRequires`)·핸들러·`when` 세 곳. 멈출 때 `FlowRecord.Rand`에 사본을 두고 `Continue`가 사본으로 잇는다(실패하면 멈춘 때의 n으로 남는다).
   - 스크립트 API: `RandomModule`(`randint`, `randrange`, `choice`, `choices`, `sample`, `random`). 범위는 버리고 다시 뽑기로 쏠림이 없다. 호출 한 번에 n이 1이고 안에서 여러 번 뽑는 것은 블록 안 순서다.
   - 효과 판정: `FunctionValue.RandomWitness`/`Random`(`FunctionEffects`의 고정점, `random` 이름을 쓰거나 난수 함수를 부르면). 오류 자리: 사전 조건 블록, `when` 조건, 입구 정책, `derived`·`field` 기본값·`Cond` 기본값(`CheckPureSlots`), Def·패치로 넣은 `Cond` 값(`PatchOperationCheck`). 실행 중 안전망: `PureBuiltin.Random`이면 스택에 본문 프레임(`Writable`)이 있어야 한다.
   - 저장: 최상위 `"seed"`, `"rand"`(정의 id 순 `{"id","count"}`, 사라진 정의는 불러올 때 버림), Flow마다 `"rand": {"count","calls"}`(골든 갱신).
   - 테스트 `Numerics/ChaCha20Tests`(RFC 8439 2.1.1·2.3.2 값), `Runtime/RandomTests`(골든 값 C6, 같은 시드 같은 값, 다른 커맨드가 밀지 않음, C10 거절·오류 뒤 같은 값, `ask`와 저장을 건너도 같은 난수열 C1, 실패한 답 뒤 같은 값, `when`의 난수열, API 뜻, 잘못된 인자, 로드 오류 자리 6개, 실행 중 안전망). 카운터 되감기와 이어 갈 때의 사본을 빼면 각각 테스트가 실패하는 것을 확인했다.
   - 로직용 `.pyi`가 아직 없어서 `random`의 스텁도 없다(.pyi 작업 때).
   - **다음:** 커밋했다(`d908c91`). 그다음 후보 중 `cost`·`choose`를 골랐다(아래).

## `cost`·`pay`·`choose` 슬라이스 (끝남, 커밋 `52b97e6`, 2026-10-17)

설계는 G183(`choose`는 후보를 정하고 엔진은 검사와 나열 양쪽에 씀)·G184(호스트 API `Candidates`·`LegalMoves`, 최대 개수)·G185(`cost`는 확인하고 본문 시작 때 뺌, 같은 칸은 합산)·G186(`cost`의 첫 자리는 칸 자체인 특수 동작)·G187(`pay(칸, 금액)`은 본문의 쓰기 문장, 돌려주기는 게임 상태로), 모두 확인. 구현은 네 항목으로 나눠 하나씩 한다.

1. **`cost`(끝남):**
   - 로드: `FunctionChecker.CheckCost`(위치 인자 둘, 키워드 없음, 첫 자리는 `AttributeExpr`; 깊이·이름은 보지 않아 `t = party.Treasury` 뒤 `cost(t.gold, n)`도 된다).
   - 실행: `PureEvaluator`의 `Cost`가 칸 식의 마지막 이름 앞까지만 계산해 `IRuntimeSlots`(월드의 `ComponentView`)를 얻고 `ReadSlot`으로 값을 읽는다(`int`·`fixed` `field`가 아니면 `RUN005`). 금액은 숫자, 음수면 `RUN006`, `int` 칸에 `fixed` 금액이면 `RUN005`. 같은 칸(`ValuesEqual` + 이름)의 앞 금액과 더해 확인한다. 본문이 시작될 때(`bodyStarts` 뒤) `PayCosts`가 블록에서 계산한 금액을 `SetAttribute`로 뺀다(보통 쓰기라 되돌리기·`when` 추적을 탄다). `CheckPreconditions`는 빼지 않는다.
   - 보고서: `RequireOutcome.Kind`(`Require`/`Cost`)와 `Slot`(`#1.Treasury.gold`, 줄이면 `#1.Stock['Wood'].amount`)·`Have`·`Need`(합한 양). 실패한 `cost`는 `Failures`에 든다.
   - 테스트 `Runtime/WorldCostTests`(빼기와 본문 첫 줄이 뺀 값을 봄, 전부 쓰기, 부족은 보고서 행, 사전 검사는 빼지 않음, 다른 이름으로도 같은 칸은 합산, 다른 엔티티는 따로, `fixed` 칸, `fixed` 금액 정확 비교, `require` 실패 뒤에도 보고, 키 컴포넌트 줄, 본문 실패면 되돌림, `@when` 깨움, 실행 오류 6개, 음수), `CommandDeclarationTests`의 모양 검사. 빼기와 합산을 각각 없애면 테스트가 실패하는 것을 확인했다.
2. **`pay`(끝남):**
   - `PureBuiltins.PayName`을 쓰는 내장 이름(`WritingNames`)에 넣어 자리 규칙(식 안·사전 조건 블록·순수 함수에서 로드 오류, 부르는 함수는 쓰기 함수)은 기존 판정을 그대로 탄다. 월드 표의 `pay` 몸체는 "값을 받았다" 오류만 낸다(`pay`를 값으로 넘겨 다른 내장이 부른 경우).
   - 실행: `PureEvaluator.StatementCall`이 부를 것이 내장 `pay`면(이름 그대로든 `f = pay`든) 인자를 계산하지 않고 `Pay`로 간다. `cost`와 같은 `SlotOwner`·`Amount`를 쓰고 모자라면 아무것도 쓰지 않는다.
   - 로드: `FunctionNameChecker.CheckPay`가 덮어쓰지 않은 `pay`의 모양을 본다(`EVL003`). 모드가 `def pay`를 두면 보통 함수다(G112).
   - 테스트 `WorldCostTests`의 `pay` 부분(있을 때·딱 맞을 때·모자랄 때, 한 줄 전체, 쓰기 함수 안, 본문 실패면 되돌림, `@when` 깨움, 함수 값으로 부름, 실행 오류, 음수)과 `CommandDeclarationTests`의 `pay` 부분(모양, 식 안·사전 조건 안·순수 함수, 모드의 `pay`). 모자랄 때도 빼게 바꾸면 테스트가 실패하는 것을 확인했다.
3. **`choose` 검사(끝남):**
   - 로드: `FunctionChecker.CheckChoose`(첫 자리는 커맨드 매개변수 이름, 키워드는 `where=`·`from_=` 중 하나만, 매개변수마다 한 번), `CommandSignatures.CheckWhereTypes`(`where=`의 매개변수는 `Ref[...]`). 모두 `CMD002`.
   - 실행: `PureEvaluator.Choose`. `where=`는 제출된 인자 하나에 함수를 부른다(그 타입의 살아 있는 엔티티라는 것은 인자 검사가 봤으므로 전체를 펼칠 필요가 없다, `None`이면 후보 아님). `from_=`는 목록(또는 키 컴포넌트 같은 런타임 묶음)에 같은 값이 있는가. 목록이 아니면 `RUN005`.
   - 보고서: `RequireOutcome.Kind == Choose`, `Parameter`. 후보가 아니면 `Failures`에 든다(C7).
   - 테스트 `Runtime/WorldChooseTests`(후보면 본문 실행, 남의 유닛·지금 도시는 거절, 사전 검사도 같은 행, 값 목록, 기본값 인자도 검사, 이름 있는 함수, 목록 아님 오류), `CommandDeclarationTests`의 `choose` 모양 9개. 늘 통과로 바꾸면 테스트가 실패하는 것을 확인했다.
   - 알게 된 것: 스크립트에는 "그 타입의 엔티티 전체"를 읽는 내장이 없고 컴프리헨션도 아직 파서가 받지 않는다(`PAR002`). 그래서 `from_=`의 목록은 지금 상태의 Ref나 함수로 만든다.
4. **`Candidates`·`LegalMoves`(끝남), G188(확인, 블록 순서가 고르는 순서, 후보 식이 자기나 아래쪽 `choose`의 매개변수를 읽으면 로드 오류):**
   - `Script/Execution/ChooseDependencies.cs`: `choose`마다 후보 식이 (블록 변수의 마지막 대입을 따라) 읽는 매개변수 `Needs`와 먼저 실행할 블록 대입 `Statements`. 람다 매개변수는 빼고 조건식 양쪽은 다 읽는 것으로 친다. `FunctionChecker.CheckChooseOrder`가 G188 로드 오류를 낸다.
   - `PureEvaluator.RunChooseOption`: 그 대입들과 후보 식만 실행해 `where=` 함수나 `from_=` 목록을 돌려준다(정하지 않은 매개변수는 `None`으로 묶지만 읽히지 않는다).
   - `Runtime/World.Choices.cs`: `Candidates`(→ `CandidateList`: `Values`·`Needs`·`Arguments`, `require`·`cost`는 보지 않음, 기본값은 정한 값으로 침), `LegalMoves`(→ `LegalMoveList`: `Moves`·`Truncated`·`Arguments`, 호스트가 준 값만 고정하고 기본값이 있어도 `choose`면 펼침, `choose` 없고 기본값 없는 매개변수가 빠지면 `RUN007`, 최대+1개까지 찾아 잘림을 판정). `where=` 후보는 `All(타입)`(id 순)을 함수로 거르고 `from_=`는 목록 순, 같은 값은 처음 것만. 후보 하나의 나열에 단계 예산 하나, `LegalMoves`는 조합마다 사전 검사 예산이 따로다. `CheckArguments(..., partial)`.
   - 테스트 `WorldChooseTests`의 나열 부분(id 순 후보, 먼저 정할 매개변수, 블록 변수를 따라감, `require` 안 봄, 같은 값 하나, 인자 거절, `choose` 없는 매개변수, 합법 수, 잘림, 기본값 펼침과 준 값 고정, 그대로 제출 가능, 빠진 매개변수 오류, 나열은 상태를 안 바꿈, 목록 아님 오류)과 `CommandDeclarationTests`의 순서 검사(오류 4, 허용 4). 합법 수에서 사전 검사를 빼거나 블록 변수의 매개변수를 따라가지 않게 하면 테스트가 실패하는 것을 확인했다.
   - spec S6의 열린 질문 "`choose` 후보가 큰 경우"를 닫았다.
   - **다음:** 커밋했다(`52b97e6`). 사용자가 시나리오 ③을 골랐다(아래 5).
5. **검증 시나리오 ③(끝남, 커밋 전):** `Runtime/ScenarioAiTickTests`. 도시 A-B-C 일렬(`Map.east/west` Ref), `Move` 커맨드(`choose` 둘·`require`·`cost`), 틱 끝 반응 `rest`(움직임 회복)·`regrow`(약탈물이 `random.randint`로 자람, `repeat=True`). AI는 호스트 C#이 `LegalMoves` 중 약탈물이 가장 많은 수를 고른다. 확인한 것: 나열은 남의 유닛·출구 밖을 빼고, AI가 다 움직이면 합법 수가 비었다가 틱 끝에 돌아오고, 금이 없으면 합법 수가 없고(`cost` 실패 행), 시드 7로 여섯 틱의 기록(낸 수·돈 반응·금)이 고정되며 같은 시드면 저장 바이트가 같고, 3틱에서 저장·불러오기 뒤 이어 해도 같다. 시드를 바꾸면 기록이 달라지는 것도 확인했다. 코어 수정은 필요 없었다. spec 7절 ③에 ✅.
   - 커밋 `9584650`. 다음으로 사용자가 컴프리헨션을 골랐다(아래 절).

## 컴프리헨션 (끝남, 2026-10-17)

G189(확인): 리스트·딕트 컴프리헨션과 제너레이터 식을 받는다. spec의 "제너레이터 금지"는 `yield` 함수와 제너레이터 객체를 값으로 들고 다니는 것으로 읽었다(이유는 G189: 식 안 멈춤, 저장, 소진되는 상태). 제너레이터 식은 호출 인자로만 오고, `any`·`all`(새로 더한 내장, G97 보강)은 하나씩 당겨 일찍 멈추고, 다른 내장과 사용자 함수에는 리스트로 넘긴다.

- AST `ComprehensionExpr`(종류·원소·딕트 값·절들)와 `ComprehensionClause`, 정적 검사기가 같은 규칙으로 돌도록 `Parts()`(하위 식과 그 식에서 보이는 반복 변수, 첫 절의 반복할 것은 바깥).
- 파서: `[...]`·`{k: v ...}`·`(...)`·호출 인자 뒤의 `for`. 대상 풀기와 집합 컴프리헨션은 `PAR002`, 괄호 없는 제너레이터 식이 유일한 인자가 아니면 `PAR001`.
- 검사기: `FunctionChecker`(제너레이터 식이 호출 인자가 아니면 `EVL002`), `FunctionNameChecker`·`FunctionEffects`·`ChooseDependencies`(반복 변수를 지역으로), `LambdaOrdinals`, `BodyHash`. 최상위 콘텐츠 파일에서는 `EVL002`(G102의 `for`·조건식과 같음).
- 실행: `PureEvaluator.Comprehend`가 이 식만의 이름 공간(지역 변수 사본 + 반복 변수)을 만든다. 안에서 만든 람다는 그 이름 공간을 들고 가며(파이썬처럼 마지막 값), 저장은 그것을 끝난 함수의 묶음으로 적는다. 딕트는 같은 키면 처음 자리에 나중 값. 바퀴마다 단계를 센다.
- 테스트 `Script/ComprehensionTests`(모양, 딕트 키, 변수가 새지 않음, 첫 반복할 것은 바깥, 람다 늦게 묶임, 제너레이터 식과 내장·사용자 함수, `any`·`all`, 일찍 멈춤, 예산, 오류), `Loading/ComprehensionLoadTests`(자리 오류 5, 받는 모양, 안의 쓰기 호출은 식 안 호출, `ask`를 건너 저장한 람다, 커맨드 본문의 난수). `ParserTests`의 미지원 목록을 바꿨다. 일찍 멈추는 경로를 끊으면 테스트가 실패하는 것을 확인했다.
- G190(확인): `all`은 파이썬 뜻, 엔티티 전역 순회는 만들 때 다른 이름으로 정한다(spec S3 문구 고침). G191(확인): 최상위 콘텐츠 파일의 컴프리헨션은 로드 오류로 둔다.
- 커밋 `fcf766d`. 다음으로 사용자가 엔티티 전체를 읽는 수단을 골랐다(아래 절).

## 전역 순회 `query` (끝남, 커밋 `f4a6902`, 2026-10-17)

사용자는 처음 낸 `entities(Type)` 대신 ECS·DB식 쿼리를 원했다. ECS들(Bevy·DOTS·Flecs·EnTT)의 쿼리 모양을 설명한 뒤 "Def 타입이 꼭 필요했나"도 이야기했고, 유지(schema-design Q1 B′ 그대로)하고 C안으로 가기로 했다.

- G192(확인): `query(이름, ...)`. 출처는 Def 타입 하나 그리고/또는 컴포넌트들, 모두 만족하는 엔티티(id 순, 처음엔 리스트였고 G194에서 엔티티 묶음으로 바뀜). 값 조건은 컴프리헨션. 리터럴 이름은 ⑤(`Linker.CheckQuery`, 이름 검사가 `ModFunctions.QueriedNames`에 모음)가 본다. 한 호출에 Def 타입 둘은 늘 빈 목록이라 오류로 정했다(내가 정한 세부, 사용자에게 보고함). 호스트 API `World.Query`.
- G193(확인): 어디서나 쓸 수 있고 읽으면 출처 목록을 읽은 것으로 기록. `World.When.cs`의 `WhenCache.Sources`·`_whenSourceReaders`·`_whenTouchedSources`, 표시는 `World.Query.cs`의 `TouchSources`(생성·삭제: 타입과 가진 컴포넌트)·`TouchSource`(부착·분리). `ExplainKind.Query`.
- 컴포넌트만인 쿼리는 지금 모든 타입의 엔티티를 훑는다(컴포넌트별 색인 없음). 벤치(spec 9절)에서 필요하면 더한다.
- 테스트 `Runtime/WorldQueryTests`(출처 셋, 컴프리헨션으로 거르기, 실행 중 이름 검사, `when`이 생성·삭제·부착·분리를 앎(전체 재평가와 같음), `derived`를 거쳐도, `Explain`, `ask`를 건너 저장, 로드 검사 6가지, 보이지 않는 모드, 덮어쓴 `query`). `WhenTrackingTests`에 쿼리 조건 둘 추가. 표시 네 곳을 하나씩 끄면 테스트가 실패하는 것을 확인했다.
- 커밋 `f4a6902`. 하다가 발견한 G88 반복 버그는 아래 절에서 고쳤다.

## 스크립트 순회의 G88 (끝남, 커밋 `0e924a6`, 2026-10-17)

스크립트의 `for`가 반복 중 죽은 엔티티와 뗀 줄을 건너뛰지 않았다(G88 구현 누락, 역방향 컬렉션·키 줄 둘 다 임시 테스트로 재현). 사용자와 이야기한 것: 파이썬은 참조가 있는 한 객체가 죽지 않아 문제가 없고, 우리 Ref는 약한 참조라 파이썬의 `weakref.WeakSet`이 맞는 비교다. 삭제를 미루는 안(Unity·Godot·Bevy)은 "지웠는데 아직 있음"이 조용히 틀려서 G88 즉시 반영을 유지했다. 멀티스레드는 읽기(조건·`derived`·나열) 쪽 병렬화가 후보라 즉시 반영과 부딪히지 않는다.

- G194(확인): 엔진이 준 묶음만 건너뛴다. 역방향 컬렉션과 `query()`는 `World.Query.cs`의 `EntityGroup`(받을 때의 id 목록, `IRuntimeLiveCollection`): `for`는 한 바퀴마다 `IsGone`으로 묻고(`PureEvaluator.NextIteration`, 블록의 `Live`), `len`·`in`·참/거짓은 살아 있는 것만, 인덱스와 리스트만 받는 자리(`+`, `min`, `random.choice`, `ask(choices=)`)는 오류(`PureOps.ListHint`가 컴프리헨션 사본을 안내), 묶음끼리 비교는 살아 있는 원소로. 묶음은 받을 때의 목록이라 옮겨 간 원소는 남는다. 키 줄 묶음 `RowsView`도 `IRuntimeLiveCollection`. 멈춘 반복은 저장의 블록에 `"live"`(묶음)를 적고, 묶음 값은 `"entity_group"` 태그.
- 테스트 `Runtime/WorldIterationTests`(역방향·`query`·키 줄·엔티티 키 줄에서 건너뜀, `len`·`in`, 사본은 파이썬 리스트, 인덱스 오류 둘, `ask`로 멈춘 반복을 저장·불러온 뒤에도 건너뜀). `WorldQueryTests`의 `query("Board")[0]`을 사본으로 바꿨다. 건너뛰기·저장된 `live` 읽기·살아 있는 것만 세는 `len`을 각각 끊으면 테스트가 실패하는 것을 확인했다.
- **구현 부채(멀티스레드 메모):** 평가 중 상태(`_whenReads`·`_whenReadsSources`, `Explain`의 `_trace`, 순환 검사 `_computing`, 예산 `_meter`)가 `World` 필드 하나씩이다. 읽기를 병렬화하려면 "평가 하나마다의 문맥"으로 옮겨야 한다.

## 검증 시나리오 ①과 `while` (끝남, 커밋 `68a4398`, 2026-10-17)

사용자가 ①을 "테스트 안의 작은 스택"(A)으로 골랐다. 진짜 `std.stack`(정책을 설치 인자로, std 모드를 저장소 어디에 둘지)은 나중에 따로 한다.

- 테스트 `Runtime/ScenarioStackTests`(3인, Bolt·Counter·Charm). 대기 효과는 `Spell` 엔티티(G128 데이터 방식), 맨 위는 `query("Spell")`의 마지막(id 순), 해소는 `s.Spell.effect(s)`(Def의 `Action`) 뒤 `destroy(s)`, 우선권 고리는 `Cast` 본문의 `while top() is not None:` + `ask(responses=["Pass", "Zap", "Negate"])`. 해소 중 Charm의 `ask`에서 여섯 시점마다 저장·불러와 이어 가도 마지막 저장 바이트가 같다(C1). 멈춘 자리가 `charm` 프레임인 것도 확인한다.
- 스택 정책은 테스트가 박은 것이다(낸 사람 다음 자리부터, 반응 뒤에도 다음 자리, 해소 뒤 처음 낸 사람부터, 참가자 수만큼 연속 통과면 해소). 결정 아님.
- **G195(확인):** 처음엔 `for i in range(100)`으로 썼고, 횟수를 다 쓰면 커맨드가 성공으로 끝나 스택이 남고 질문이 없는 채 멈추는 것을 임시 테스트로 재현했다. `while`을 파이썬 그대로 열고 상한은 단계 예산(구간마다 새로 셈, 조건 검사도 바퀴마다 한 단계)이 맡는다. spec S2의 "필수 `limit`" 문구를 고쳤다.
- `while` 구현: AST `WhileStmt`, `Parser.ParseWhile`(`else` 절은 `PAR002`), 검사기 6곳(`BodyHash`·`FunctionChecker`·`FunctionEffects`·`FunctionLocals`·`FunctionNameChecker`·`LambdaOrdinals`), 실행기 `Block.While`·`IsLoop`(`break`·`continue`가 찾는 반복), `NextIteration`이 조건을 평가, 저장은 블록 위치만(`World.SaveFlows`). 최상위 콘텐츠 파일에서는 `EVL002`(기본 경로).
- 테스트 `Script/WhileTests`(파이썬과 같은 결과, `break`·`continue`·`return`, 예산), `Runtime/WorldWhileTests`(바퀴마다 묻는 반복은 예산에 안 걸림, 혼자 도는 반복은 되감김, 저장·불러오기 C1·C4, 조건이 바뀌면 `SAV004`, 로드 검사 넷). `ParserTests`의 미지원 목록을 바꿨다. 불러오기에서 `While`을 빼면 저장 테스트가 실패하는 것을 확인했다.
- `std.stack`을 만들 때 볼 것(결정 아님): 스택 순서를 엔티티 id 순서에 기대지 말고 위치를 명시할지, 우선권 고리가 처음 낸 커맨드의 Flow 안에 있어 스택이 열린 동안 다른 `Cast`가 들어오면 고리가 둘이 되는 것.

## 검증 시나리오 ②와 보기 정책·거른 읽기 (끝남, 커밋 `427345c`~`d987345`, 2026-10-17)

②는 spec 9절 미결 2(공개/비공개)를 건드려 먼저 설계를 정했다. 다른 엔진 사례(락스텝 StarCraft·패러독스는 전체 상태가 클라이언트에 있어 숨김이 화면뿐, 서버 권위 boardgame.io·Unreal·BGA는 서버가 사람마다 거름)를 설명한 뒤 사용자가 "보는 사람 기준의 읽기 정책"을 골랐다. 결정은 G196~G203(모두 확인), 구현 전 advisor 검토를 받아 G201(문구 인자)·G203(입구 정책도 필수)·G197(Def는 엔티티 층)을 고쳤다.

- G196 방향, G197 `@view_policy def may_see(issuer, entity, component)`(issuer로 받음, `component=None`이면 존재 자체, Def·Def 상수는 엔티티 층), G198 숨긴 엔티티·컴포넌트는 없는 것처럼, 보이는 칸의 Ref가 숨긴 엔티티면 id 없는 `HiddenValue`, 목록에서는 빠짐, 숨긴 id는 죽은 id와 같은 응답. G199 `World.ViewAs(issuer)` → `WorldView`. G200 열린 질문은 `may_act`로 답할 수 있는 것만, 안의 값은 가림(선택지는 자리 유지). G201 결과는 `view.Show(result)`(문구 인자의 숫자는 모드가 쓴 대로, Ref만 가림, 반응 목록·오류 본문·인자 거부 본문은 뺌). G202 `LegalMoves`·`Candidates`는 숨긴 엔티티가 든 수를 빼고 상한은 거른 뒤에. G203 정책 없으면 모두 보임, 최대 하나, `requireViewPolicy`(입구 정책도 필수), 실패하면 안 보임 + 진단.
- **구현한 것(첫 조각):** `PolicyDecl`(옛 `SubmitPolicyDecl`, `PolicyKind` Submit·View, `Noun`)로 두 정책이 같은 등록·순수성 검사를 씀(`Interpreter.DeclarePolicy`, `ContentLoader.BuildDefs`·`RegisterPolicy`, `FunctionEffects`). `ContentLoadResult.ViewPolicy`, `World(..., requireViewPolicy:)`·`World.Load(..., requireViewPolicy:)`, 코드 `CMD006`·`RUN017`·`RUN018`. `Runtime/World.View.cs`: `HiddenValue`, `World.ViewAs`·`MaySee`(닫는 쪽)·`MayAnswer`, `WorldView`(`Sees`·`IsAlive`·`DefOf`·`Has`·`Get`·줄 읽기·`RowKeys`·`RowInts`·`RowEntities`·`All`·`Query`·`Inverse`·`OpenRequests`·`Diagnostics`).
- 줄 읽기의 대칭: 못 보는 키 컴포넌트(`keys="listed"`·`"all"` 둘 다)는 붙지 않은 컴포넌트와, 못 보는 엔티티 키는 죽은 키와 같은 오류 코드·문구를 낸다(월드에서 `HasRow`도 죽은 키면 오류이므로 거른 읽기도 그렇다). `HiddenRowsAnswerLikeUnattachedComponentsAndDeadKeys`가 지킨다.
- **`view.Show(result)`(G201, 했음):** `SubmitResult`·`AnswerResult`를 새로 만들어 돌려준다. 인자·대상·문구 인자 안의 못 보는 엔티티는 `HiddenValue`, 반응 목록 빠짐, 스크립트 오류는 코드와 "script error", 인자 거부는 문제 종류·매개변수 이름·타입 표기만(`Path`는 매개변수 이름), 답의 `Message` 빠짐, 연 질문은 `ForMe`(G200). `cost`의 칸(`#3.Hand.cards`)은 그 엔티티·컴포넌트를 못 보면 칸 null·가진 양 `HiddenValue`(필요한 양은 그대로, G204).
- **`view.LegalMoves`·`Candidates`(G202, 했음):** 월드의 나열에서 못 보는 엔티티가 든 수·후보(리스트·딕트 안까지, `AllSeen`)를 뺀다. `LegalMoves`는 월드의 internal 오버로드에 `keep`을 넘겨 사전 검사를 통과한 수 중 보이는 것만 세므로 상한·`Truncated`가 보이는 수만 센다. 인자 거절은 `Show`처럼 본문이 빠진다. 두 함수가 던지는 `WorldException`(스크립트 오류 등)은 호스트 호출 오류라 거르지 않는다.
- **`view.Explain`(G199·G205, 했음):** 월드의 `Explain`으로 근거를 다 모은 뒤(`_trace`가 원래대로 돌아온 뒤) 정책을 불러 거른다. 단계마다 못 보는 읽기·키·기여 출처를 빼고 끝에 `ExplainKind.Hidden` 하나, `query` 근거 목록은 `view.Query`와 같은 기준(`ExplainNode.Names`)으로 거름. 정책 호출은 근거 수집 바깥이라 `_trace`에 섞이지 않는다.
- **거른 읽기는 이로써 G196~G205가 다 구현됐다.** 남은 것: `Advance` 결과는 G206. 스크립트가 "보는가"를 물을 길은 필요해지면 정한다(G202).
- 테스트 `Runtime/WorldViewTests`(남의 손패는 붙지 않은 것처럼·`Query`로도 안 샘, 숨긴 엔티티는 죽은 id와 같은 메시지, 숨긴 Ref는 `HiddenValue`, `Inverse`·엔티티 키 줄에서 빠짐, 답할 수 있는 질문만·선택지 자리 유지·번호로 답함, `HiddenValue`는 인자로 못 냄, 보기는 저장 바이트를 안 바꿈, 정책 없으면 다 보임, 고장 난 정책은 닫힘 + 진단, 로드 검사 6, `requireViewPolicy`). `Runtime/ScenarioSimultaneousTests`(질문 셋이 동시에 열리고 각자 자기 것만 봄, **A가 바위든 보든 공개 전 B·C의 거른 읽기 전체가 같음**, 모두 고르면 공개·점수, 남의 질문에 못 답함, 일부가 고른 채 저장·불러와도 같음). `view.Query`의 컴포넌트 검사, `view.Has`·`Get`의 검사를 끊으면 테스트가 실패하는 것을 확인했다. 질문 여러 개 동시 열기는 구현 전에 임시 테스트로 확인했다.
- 알려진 한계: id가 차례로 붙어 숨긴 엔티티가 생긴 것을 번호 틈으로 짐작할 수 있다(G198). 정책을 읽을 때마다 부른다(캐시 없음, 벤치에서).


## S7 호스트 함수와 스크립트 속도 (2026-10-17~18)

**그 전에 끝낸 것: S7 호스트 함수 (G215~G222 결정).** 구현 순서는 ① 등록과 서명 검사 ② 스크립트에서 이름으로 부르기(소유 모드에 의존한 모드만, 모드 최상위 이름이 다른 모드 id와 같으면 오류) ③ 접근기와 실행 중 World 직접 쓰기 막기 ④ 오류 RUN019·RUN020과 실행 예산 ⑤ 함수 값 저장 `{"host": ...}`과 호스트 정보 ⑥ 서명 내보내기 파일과 CLI `--host`·스텁 ⑦ 분석기 안내(G220).
- **① 했음 (커밋 012ab57):** `Host/HostFunctions.cs`(`host.For("core").Pure(...)`/`Writes(...)`, 인자 0~8개 타입 있는 람다 오버로드는 `HostOverloads.cs`, 등록 순간 `def` 한 줄 문법·경로·같은 모드 안 중복·인자 개수·이름(`maxSteps`=`max_steps`)·C# 타입(G222)·반환 타입을 보고 `ArgumentException`), `Parser.ParseHostHeader`(점 경로 이름, 기본값 허용, 반환 타입 필수, 본문 없음), `Host/HostSignatures.cs`(②b에서 소유 모드의 스키마·가시성으로 타입과 기본값을 풀고 틀리면 소유 모드의 `HST001`로 소유 모드와 의존 모드가 빠짐, 소유 모드가 없으면 건너뜀), `ContentLoader.Load(..., host)`와 `ContentLoadResult.HostFunctions`. `CommandSignatures.Reader`·`Defaults`를 진단 코드와 빈 스코프를 받게 열었다. 인자가 8개를 넘는 함수의 이름으로 꺼내기(`call.Int("max_steps")`)는 ②에서 호출과 함께 한다. 테스트 `Loading/HostFunctionTests`(53개). 이름 대조와 로드 해석을 끊으면 실패하는 것을 확인했다.
- **② 했음 (커밋 d2f1120, ④도 함께):** 로드: `Script/HostNames.cs`(모드마다 보이는 호스트 이름, 소유 모드가 자신이거나 필수 의존), `FunctionNameChecker`가 `core.path.find` 줄을 끝까지 풀어 없는 이름은 `EVL` + "혹시 …?", 이름 공간을 값으로 쓰거나 함수에 속성을 붙이면 `HST002`, 의존하지 않은 소유 모드는 `SCH003` + 의존 추가 안내, 모드 최상위 이름이 이름 공간을 가리면 `HST002`, 지역 변수로 가리는 것은 그냥 지역 변수. `FunctionEffects`가 `Writes` 호스트 함수를 쓰는 내장처럼 판정(식 안이면 `CMD`, 부른 함수는 쓰기 함수). 등록: 한 경로가 함수이면서 이름 공간이면 등록 오류, 소유 모드 id가 런타임 내장 이름이면 `HST001`. 실행: `Script/HostCallable.cs`(실행기는 자리 규칙만), `Runtime/World.Host.cs`(소유 모드 id마다 이름 공간 값을 런타임 내장에 넣음, 인자는 스크립트 함수처럼 묶고 커맨드 인자 검사 `Fit`, C# 값으로 바꿔 `DynamicInvoke`, 예외는 `RUN019`이고 `InnerException`이 `WorldException`까지 이어짐, 반환은 서명대로 검사해 어긋나면 `RUN020`, 엔티티는 리스트 안까지 따라갈 수 있는 모양으로). `Writes` 함수는 아직 기능 없는 `HostAccess`를 받는다(③). 테스트 `Loading/HostFunctionTests`(69), `Runtime/HostCallTests`(15). 실행기의 쓰기 자리 검사와 로드 가시성 검사를 끊으면 실패하는 것을 확인했다.
- **③ 했음 (커밋 491452d):** `Host/HostAccess.cs`. 읽기 창구 `HostReader`(순수 함수는 람다의 첫 매개변수를 `HostReader`로 두면 받는다, 선택), 쓰기 창구 `HostAccess : HostReader`(`Writes`는 항상 받음, 순수 함수에 `HostAccess`를 두면 등록 오류). 창구는 월드의 공개 API를 그대로 불러 읽기 기록·롤백이 스크립트와 같고, 난수는 스크립트 `random`의 몸체를 그대로 쓴다(`RandInt`·`RandRange`·`Random`·`Choice`·`Sample`, `choices`는 없음). 호출이 끝나면 닫혀 붙잡아 두고 쓰면 `InvalidOperationException`. `World._hostDepth`: 호스트 C# 코드가 도는 동안 월드의 공개 쓰기(`Create`·`Destroy`·`Set`·`Add`·`Remove`·줄·기여 행)와 `Submit`·`Answer`·`Respond`·`Advance`를 직접 부르면 `InvalidOperationException`(호스트 버그라 `RUN019`가 된다), 창구를 거치는 동안은 0으로 풀린다. 창구가 낸 `WorldException`(죽은 엔티티 등)은 `RUN019`가 아니라 원래 코드 그대로 "in host function X: ..."로 낸다(스크립트의 같은 읽기와 같은 오류). 테스트 `Runtime/HostCallTests`(26, `when` 조건 안 호스트 읽기가 기록되어 반응이 남, 창구 쓰기의 롤백 등). 직접 쓰기 막기와 창구 닫기를 끊으면 실패하는 것을 확인했다.
- **⑤ 했음 (커밋 31edfa5):** 멈춘 Flow의 호스트 함수 값은 `{"host": "core.math.double"}`(이름뿐), 불러올 때 지금 등록에 없으면 그 값의 자리에서 `SAV002`(G214의 `cancelBrokenFlows`로 그 Flow만 취소 가능). 저장 최상위 `"host"`에 등록 목록(이름, 정규 서명 `HostSignature.Canonical`, `pure`/`writes`)을 이름 순으로 싣는다. 등록이 없으면 키 자체를 싣지 않아 호스트 함수를 안 쓰는 게임의 저장 바이트는 그대로다. 불러올 때 다르면 모드 경고와 따로 `SAV003` "the host functions differ from the save: changed X (옛 서명) -> (새 서명), added Y, removed Z". 정규 서명은 풀린 타입과 기본값으로 만들어 공백 등 적은 모양만 다르면 같다. 테스트 `Runtime/HostSaveTests`(7). 값 복원과 서명 비교를 끊으면 실패하는 것을 확인했다.
- **⑥ 했음 (커밋 f8ac01d):** `HostFunctions.ExportDeclarations()`가 등록 목록을 JSON(`"format": "kesa-engine-host-functions"`, `"version": 1`, 함수마다 전체 이름·등록한 `def` 한 줄·`pure`/`writes`·독스트링, 이름 순, 정규 JSON이라 같은 등록이면 같은 글)으로 내보내고, `HostFunctions.ImportDeclarations(text)`가 구현 없는 등록부를 만든다(서명은 등록과 같은 검사, 람다 대조만 뺌. 틀리면 `FormatException`, 모르는 키는 건너뜀). `HostFunction.IsDeclarationOnly`, 그런 함수가 있으면 `new World`가 `ArgumentException`. CLI는 `check`·`dump`·`stubs` 모두 `--host <file>`(못 읽거나 틀리면 종료 코드 2). 스텁: 그 모드가 볼 수 있는 호스트 함수(소유 모드가 자신이거나 의존)를 `class core:` 안의 `class path:` 안의 `@staticmethod def find(...)`로, `Ref[Tile]`은 `Tile`, 기본값은 `= ...`, 독스트링 포함, 로직 스텁과 스키마 스텁 양쪽(`WriteClasses`에서). `OptionalTypeRef`를 `T | None`으로 쓰게 함. 함께 고친 것: `check` 텍스트가 `<host core.x>` 위치 앞에도 모드 폴더 경로를 붙이던 것(재현 후 고침). 테스트 `Loading/HostDeclarationTests`(20), CLI `CommandsTests`(+8). 월드 거부·가시성 거르기·경로 접두사·CLI 전달을 끊으면 실패하는 것을 확인했다.
- **⑦ 했음 (커밋 28d3a19):** 금지 목록을 `src/Kesa.Engine.Analyzers/BannedSymbols.txt`로 옮기고 `src/Kesa.Engine.Analyzers/Determinism.props`(금지 목록 분석기, `DeterminismAnalyzer`, `RS0030`을 오류로)를 만들어 커널 `.csproj`도 그것을 `Import`한다. 호스트 함수 프로젝트는 같은 한 줄로 켠다. `KE0001` 문구와 금지 목록의 "커널 RNG/해시" 안내를 커널에 묶이지 않게 고침. 안내 문서 `docs/host-determinism.md`(켜는 법, 잡는 것, 못 잡는 것, Unity는 미확인). 저장소 밖 임시 호스트 프로젝트에서 `double`·`DateTime.Now`·`Dictionary` 순회가 `KE0001`·`RS0030`·`KE0002` 오류가 되고 깨끗한 코드는 경고 0으로 빌드되는 것, 커널에 같은 위반을 넣으면 여전히 막히는 것을 확인했다(자동 테스트는 아님).
- **②에서 남긴 것:** 인자 8개 초과용 이름으로 꺼내기(`call.Int`), 콘텐츠 파일 최상위에서 호스트 함수를 값으로 참조(G221, 지금은 "이름 없음" 오류. Def의 `Cond`·`Action` 칸은 스크립트 함수만 받으므로 따로 정해야 함).

**규모 벤치 첫 판 (G223, 커밋 전).** `bench/Kesa.Engine.Bench`(콘솔, `dotnet run -c Release --project bench/Kesa.Engine.Bench [-- 1000 10000]`, 단계별 JIT를 꺼서 처음부터 최적화된 코드로 잼, 실행 예산은 풀고 잼). 장면: 유닛 N개(`Health`의 `derived` max는 기여 행을 합침, `Food.amount` 칸). 두 번 돌려 서로 몇 % 안에서 같았다. 이 기계(Windows, 개발 PC) 기준 개체당 시간:

| 장면 | 1천 | 1만 | 10만 | 한 번 할당 |
|---|---:|---:|---:|---:|
| C# `Get` 칸 | 79ns | 69ns | 122ns | 47B |
| C# `Set` 칸 | 105ns | 87ns | 139ns | 100B |
| `Query("Food")`(컴포넌트만, 색인 없음) | 61ns | 51ns | 76ns | |
| `derived` 읽기, 기여 행 없음 | 2.6µs | 2.0µs | 2.0µs | 2.3KB |
| `derived` 읽기, 기여 행 2개 | 3.4µs | 3.0µs | 3.1µs | 2.7KB |
| 스크립트 `@on(Tick)`이 전부 갱신 | 540ns | 650ns | 1.6µs | 810B |
| 스크립트 커맨드가 전부 갱신 | 560ns | 680ns | 1.1µs | 810B |
| 같은 커맨드가 끝에 실패(롤백) | 630ns | 670ns | 1.5µs | 810B |
| `Writes` 호스트 함수가 전부 갱신 | 290ns | 280ns | 840ns | 590B |
| 생성 | 1.1µs | 1.5µs | 2.1µs | 1.7KB |

읽은 것:
1. **`derived` 읽기가 가장 비싸다.** 작은 함수 한 번에 2~3µs와 2.3KB로, 칸 읽기의 약 30배이고 스크립트가 개체 하나를 읽고 쓰는 것보다도 비싸다. 함수 본문이 작으므로 계산 자체보다 계산마다 드는 준비 비용으로 보인다(추측, 프로파일 전). 캐시(G105)보다 이것을 먼저 줄이는 것이 맞아 보인다.
2. **기본 실행 예산(100,000걸음)이면 전체 유닛을 도는 틱 핸들러가 1만~2만 개 사이에서 예산을 넘어 실패한다**(재현: 1만은 되고 2만부터 틱이 아무것도 바꾸지 못함). 예산을 개체 수에 맞게 어떻게 할지는 정해야 한다. → **G224로 해결 (커밋 3b99ecb):** 읽기 예산(`stepBudget`, 10만)과 행동 예산(`actionStepBudget`, 1천만)을 나눴다. 커맨드 본문·사전 검사·`@on`·`@when`·Flow 이어 하기가 행동 예산을 쓴다. 벤치를 기본 예산으로 돌려 10만 개 틱이 통과한다(벤치의 `int.MaxValue` 우회 없앰). 테스트 `Runtime/WorldActionBudgetTests`(4, 핸들러·커맨드를 읽기 예산으로 되돌리면 3개 실패 확인). 함께 고른 경고 문턱은 **G225로 했음 (커밋 전):** 행동 실행 하나가 읽기 예산을 넘으면 `RUN021` 경고를 `SubmitResult`·`AnswerResult`·`Reaction`의 `Warnings`에 넘을 때마다 붙인다. 구현은 `World.Derived.cs`의 `OpenWarnings`·`CloseWarnings`·`WithWarnings`(결과 단위마다 경고 목록을 열고 닫음, 안쪽 단위가 바깥 것을 섞지 않음)와 `CloseActionMeter`·`WarnHeavy`(무거울 때만 문구를 만듦). 테스트 `WorldActionBudgetTests`에 4개(핸들러 매번, 가벼우면 없음, 커맨드와 거른 결과, 답으로 이어 간 구간). 문턱을 끊으면 3개 실패 확인. `@when`의 경고는 같은 길이지만 직접 테스트는 없다.
3. 스크립트는 1만 개 틱이 6.5ms, 10만 개가 약 150ms다. 10만에서 개체당 시간이 늘어난 것은 개체당 810B 할당의 GC로 보인다(추측).
4. 호스트 함수는 스크립트의 2~2.5배 빠르지만 창구를 거친 읽기+쓰기(280ns)가 C# 직접 읽기+쓰기(약 160ns)보다 느리다(커맨드 안 쓰기 기록 포함).
5. 롤백은 성공과 거의 같은 비용이다. 쓰기 로그 방식으로 충분하다(spec 9절의 "전체 스냅샷 vs 쓰기 로그"는 쓰기 로그로 닫아도 됨).
6. 컴포넌트만인 쿼리는 10만에서 7.6ms로 색인이 급하지 않다.

**스크립트 속도 줄이기 (사용자와 정한 순서: A → B-1 → B-2).** 벤치의 `--profile-derived` 모드와 `dotnet-trace`(전역 설치 없이 `dotnet tool install dotnet-trace --tool-path <임시>`, `collect -- <exe 절대 경로> --profile-derived`, `report <파일> topN -n 40`)로 프로파일했다. `derived`가 비싼 것은 준비 비용이 아니라(추측이 틀림) 스크립트 식마다 약 100ns이기 때문이었고, 큰 몫은 문자열로 이름 찾기(지역 변수 `SortedDictionary`, 지역 이름 집합, 모드 전역, 내장을 차례로)와 오류용 문자열을 미리 만드는 것이었다. spec P6("로드 시점에 슬롯 핸들로 바인딩")이 이미 요구하는 방향이다.
- **A 했음 (커밋 전):** 오류에만 쓰는 문자열을 읽을 때마다 만들지 않음. `MemberDecl.Display`(`Health.current`)를 컴포넌트가 만들어질 때 한 번 정하고 `MemberHandle.Display`·칸 읽기·쓰기가 그것을 씀. `derived` 순환 검사 `_computing`을 글 목록에서 (엔티티, 컴포넌트, 줄, 멤버, 기본값인가) 목록으로 바꾸고 글은 순환·오류 때만 만듦. 위치 인자만인 함수 호출은 묶인 인자 집합을 만들지 않음. 1만 개 기준 `derived` 읽기 2.0→1.65µs(2.3→1.8KB), 스크립트 틱 650→565ns(810→685B), C# 칸 읽기 할당 47B→0. 이때 `WorldDerivedTests.ADerivedThatReadsItselfIsACycle`이 순환 검사를 끊어도 통과하는 것을 발견했다(깊이 상한 메시지에도 같은 단어가 들어감). 정확한 고리를 보게 고쳤고 끊으면 실패하는 것을 확인했다. 줄 읽기(`Display + "[" + key + "]"`)는 아직 읽을 때마다 만든다.
- **B-1 했음 (커밋 전):** 속성 접근의 인라인 캐시. `u`의 타입은 로드 때 알 수 없어서(`for u in query(...)`) "로드 때 풀기" 대신 자리(`AttributeExpr.Cache`)마다 처음 푼 것을 기억한다(CPython 3.11·JS 엔진의 속성 캐시와 같은 방식, 일반 지식). 엔티티의 `u.Food`는 (스키마, 컴포넌트), 컴포넌트의 `c.amount`는 (컴포넌트, 멤버, 쓰기 핸들)이고 쓸 때마다 스키마·컴포넌트 객체가 같은지 확인하므로 결과는 캐시 없을 때와 같다. 컴포넌트 이름이 아닌 것(Def 타입, 역방향, `entity`, `key`, 형제)은 전처럼 이름으로 찾는다. 실행기는 `ISiteAttributes`·`ISiteAssignable`을 먼저 묻는다. 이 길에서는 `Guard`의 람다 할당도 없앴다. 줄 읽기의 진단 글(`Skill.level['Cooking']`)도 오류 때만 만든다(`Shown`). 1만 개 스크립트 틱 565→450ns(할당 685→356B). 테스트 `Runtime/AttributeSiteTests`(4: 같은 자리가 다른 컴포넌트·엔티티와 형제·없는 멤버·`derived` 쓰기를 번갈아 받음), 캐시의 컴포넌트 확인을 끊으면 3개가 실패하는 것을 확인했다.
- **틱 프로파일에서 본 것:** 속성 이름 찾기는 틱의 약 10%였고, 쓰기 경로(`Set` → 되돌리기 기록 `Journal`의 람다 → `Touch`)가 약 30%로 더 크다. B-2 다음 후보.
- **B-2 했음:** 말 그대로의 번호 칸 대신 같은 효과를 더 작은 변경으로 냈다. 람다가 만든 함수의 지역 변수 표를 같은 객체로 붙잡고(늦게 묶임, G104), 저장이 그 객체가 같은지로 람다를 식별하고, 컴프리헨션이 표를 복사하므로, 이 셋과 저장 형식은 그대로 두고 그릇만 바꿨다. (1) `Script/Execution/LocalTable`: 지역 변수를 `SortedDictionary` 대신 이름·값 배열로 두고 앞에서부터 훑는다(지역 변수는 몇 개뿐). 돌 때는 이름 순(서수)이라 저장 바이트가 같다(골든 저장 통과). (2) `NameExpr.Cache`: 전역·내장으로 풀린 이름을 자리마다 기억한다. 같은 실행기(월드마다 내장이 다름)·같은 모드이고 그 이름이 이 프레임의 지역 이름이 아니면 그대로 쓴다(지역 이름 집합이 같은 객체면 확인도 생략). `IScriptGlobals`에 "실행기가 사는 동안 같은 값" 전제를 적었다. (3) 키워드 인자가 없는 호출은 빈 사전을 만들지 않는다. 테스트 `Runtime/NameSiteTests`(두 월드가 같은 함수를 번갈아 써도 각자의 `now()`, 내장 이름의 지역 변수는 여전히 "대입 전" 오류), `Script/LocalTableTests`(4). 실행기 확인을 끊으면 두 월드 테스트가 실패하는 것을 확인했다.
- **A~B-2 전체 효과 (1만 개, 개체당):** `derived` 읽기 2.0µs·2.3KB → 0.9µs·1.3KB, 스크립트 틱 650ns·810B → 340ns·356B. 10만 개 틱은 1.6µs → 0.57µs(틱 하나 160ms → 57ms).
- **`derived` 할당 줄이기 했음 (커밋 전):** 임시 테스트로 변형마다 읽기 한 번의 할당을 재 보니 `return 1`만 해도 760B였다(준비 비용). (1) `PureEvaluator.Run`이 세션을 다 쓰면 비워서(`Session.Clear`) 다시 쓴다. 안에서 다시 불린 `Run`(다른 `derived`)은 다른 세션을 꺼낸다. (2) 월드가 가장 바깥 `derived` 실행의 계량기를 비워서 다시 쓴다. (3) `floor`·`ceil`·`round`가 부를 때마다 메서드 그룹을 델리게이트로 만들던 것을(C# 9는 캐시하지 않음) 캡처 없는 람다로. (4) 호출 인자 목록을 인자 수만큼, 지역 변수 표를 함수의 지역 이름 수만큼 처음부터 잡는다. (5) 정수·고정소수·문자열 리터럴은 노드가 값 하나를 들고 그것을 돌려준다(값은 불변, G104, 값의 객체 동일성을 보는 곳 없음). `return 1` 760→472B, 벤치 함수 1352→952B. 테스트 `WorldDerivedTests`의 `EveryOutermostReadStartsWithTheWholeBudget`(계량기를 안 비우면 실패 확인), `AFailedReadLeavesNothingForTheNextRead`(세션의 스택 비우기를 빼도 통과한다: `CallFunction`의 `finally`가 이미 걷어 내므로 `Clear`의 스택 비우기는 방어선). 1만 개 `derived` 읽기 974ns·1.34KB → 약 780ns·0.95KB, 기여 2개 1.56µs·1.64KB → 1.31µs·1.25KB. 남은 고정 비용은 프레임·블록 목록·블록·지역 변수 표·컴포넌트 뷰·인자 배열(프레임 재사용은 멈춘 실행이 프레임을 그대로 들고 가고 람다가 지역 변수 표를 붙잡아 위험해서 하지 않았다).
- **컴포넌트를 번호로 찾기 했음 (커밋 전):** 틱 프로파일에서 사전 찾기(`FindValue`)가 약 18%였고, 그중 "이 Def가 이 컴포넌트를 가졌나"(`Has`·`Require`)와 `DefMembers`가 읽기·쓰기마다 컴포넌트 이름 문자열로 Def의 표를 찾고 있었다. `ComponentType.Index`(스키마 안의 번호, `SchemaSet.Add`가 줌)와 월드의 `DefLayout`(Def마다 한 번, 번호 → 그 Def의 컴포넌트 배열, 엔티티 기록이 듦)으로 배열 칸 하나를 본다. 다른 로드의 컴포넌트 타입은 번호가 겹칠 수 있어 칸의 타입이 같은 객체일 때만 쓰고 아니면 이름으로 찾는다(테스트 `Runtime/DefLayoutTests`, 객체 확인을 빼면 실패 확인). 1만 개 C# 칸 읽기 56→33ns, 쓰기 72→55ns, 스크립트 틱 약 320→300ns. `derived`는 그대로(흔들림 안). **프로파일러 주의:** `--profile-derived`를 `dotnet-trace`로 잡으면 스택이 `ComputeDerived`에서 끊겨 그 아래가 보이지 않는다(두 번 재현, 틱은 정상). 원인은 아직 모른다. G226 뒤 부르기만 모양에서도 같았다(`ComputeDerived` 아래가 전부 `UNMANAGED_CODE_TIME`, 단계별 JIT를 꺼도 같음). 그래서 이 경로는 조각을 빼 가며 시계로 쟀다(G226 절).
- **쓰기 경로 했음:** 되돌리기 기록(`World.Undo`)을 람다 목록에서 작은 구조체 `UndoEntry`의 목록으로 바꿨다. 가장 흔한 "칸 하나에 쓰기"는 (값 사전, 멤버, 옛 값, 없었으면 null)로 적어 할당하지 않고, 드문 구조 변경은 람다를 그대로 든다(`Journal(Action)`의 호출부는 그대로). 기록 목록은 커맨드마다 새로 만들지 않고 월드가 하나를 비워 다시 쓴다(커맨드는 겹쳐 돌지 않는다, 끝나면 비워 값을 놓는다). 칸 되감기를 빼 보면 롤백 테스트 여럿이 실패하는 것을 확인했다. 1만 개 스크립트 틱 340ns·356B → 316ns·206B, 10만 개 틱 0.57µs → 0.47µs. 틱에서 `World.Set`의 몫 29.5% → 20%.
- **(그때의) 남은 후보:** `derived` 한 번에 남은 고정 할당 약 470B(프레임 쪽, 위험해서 미룸), `contribs()`가 행이 없어도 만드는 목록, 람다 호출마다 만드는 지역 이름 집합. 예산 문제는 G224·G225로 해결. 프레임 쪽 할당과 지역 이름 집합은 G226에서 없어졌다(부르기만 `derived` 하나 176B).
- **실행기 몫 재기 (`--shapes`, 커밋함):** 모양마다 스크립트와 같은 일을 C# API로 한 것을 유닛 1만 개에서 비교한다. 결과(하나당): 부르기만(`return 1`) 260ns 대 5ns, 수식(값 둘 읽고 계산) 950 대 70, 조건(값 셋 읽고 if/elif) 870 대 110, 계산 반복 50바퀴 9800 대 42, 틱 읽고 쓰기 295 대 90. 실행기(부르기 준비와 스크립트에서 월드로 가는 다리 포함) 몫이 70~100%다. 앞서 틱 프로파일로 "월드 접근 64%"라고 한 것은 다리 비용(뷰 만들기, 속성 길 고르기, 오류 위치 감싸기)을 월드 쪽으로 센 착시였다. 식 칸 하나에 약 25~30ns, 부르기 한 번 약 260ns. 사용자는 "실행 방식을 바꿀 거면 지금이 가장 싸다"는 쪽이고, 바이트코드 VM(B)·로드 때 미리 풀어 둔 실행 객체(A)·값 구조체화를 작은 시제품으로 재 본 뒤 고르기로 했다. 시제품에서 A≈B였고 값 구조체는 계산 반복에서만 2배라 **"층을 나눈 A, 값 구조체화는 백본 ECS화와 함께 맨 끝"으로 정했다(G226).**
- **G226 실행기 구조 6단계 (끝남, 커밋 `cea92f6`).** 단계마다 테스트 전체 통과. 오류 위치·흔적 21가지와 여러 문법을 쓰는 프로그램의 결과·단계 수(443)를 바꾸기 전 실행기에서 재어 고정했다(`Script/EvaluatorPinTests`). 노드의 단계 세기를 빼거나 이항 노드의 위치 붙이기를 빼면 이 테스트가 실패하는 것을 확인했다.
  1. 칸 배치: `Script/Execution/FrameLayout`(이름을 서수 순으로 칸 번호, 람다·컴프리헨션은 바깥 배치의 자식 배치로 AST 자리마다 하나, 바깥 칸 → 자식 칸 표), `LocalTable`은 배치를 따르는 칸 배열(빈 칸 = 대입 전, 구조체로 감싸 공변 배열 검사를 피함). 람다는 `ClosureValue.Captured`(표 객체)만 든다. 저장 형식은 그대로(env의 이름 목록은 배치에서), 불러올 때 배치에 없는 이름은 형식 오류. `def`·맨 람다의 배치는 AST(`FunctionDef.Layout`·`LambdaExpr.Layout`)에 두어 여러 월드가 나눠 쓴다.
  2. 이름 풀기: 지역 칸 번호 또는 실행기·모드별 전역·내장(`NameSite`).
  3. 연산자 번호: `Operators`(`BinaryOp`·`UnaryOp`), AST가 만들 때 한 번 바꿈, `PureOps.Binary`·`Arithmetic`이 번호를 받음, `+=`의 `Substring` 없앰(오류 문구에서만 원래 글자).
  4. 식 노드: `PureEvaluator.Nodes.cs`(상수·지역·전역·리스트·딕트·속성·인덱스·단항·이항·and·or·조건·람다·호출·컴프리헨션). `Expr.Compiled`에 배치마다 하나. 노드 하나에 1단계(G101 그대로), 자기 연산이 실패할 수 없는 노드는 감싸지 않고 단계 초과·대입 전 오류는 그 자리에서 위치를 붙임.
  5. 호출 규약: 인자를 매개변수 칸에 곧장(`FrameLayout.SlotsOf`), 호출 인자는 맞는 크기의 배열, 끝난 프레임을 세션에서 다시 씀(람다는 프레임이 아니라 표를 잡고, 멈춘 실행은 끝나지 않은 프레임만 들어서 안전, `ALambdaKeepsTheLocalsOfACallWhoseFrameWasReused`).
  6. 문장 노드: `PureEvaluator.Statements.cs`. 블록이 AST 몸체(저장용)와 풀린 몸체(`Block.Code`)를 함께 들고, 불러온 저장의 블록은 처음 실행할 때 배치의 몸체 캐시에서 찾는다. 내린 블록은 다시 씀.
  - 결과(`--shapes`, 하나당, 앞 → 뒤): 부르기만 248→192ns, 수식 910→751, 조건 862→668, 계산 반복 9479→4873, 틱 289→247. 실행기만 재면(`PureEvaluator.Run`을 곧장, 5바퀴 중 최소) 부르기 118→72ns·392→96B, 수식 306→135, 두 함수 215→110, 바깥 부르기 353→180, 계산 반복 11278→3550. (한때 적은 "실행기만 부르기 150~170ns"는 한 번만 재고 예열이 모자란 잘못 잰 값이었다.) 월드의 `derived` 읽기 하나(부르기만, 1만 개, 140ns·176B)를 조각을 빼며 재면: `Get`의 엔티티·컴포넌트 찾기 20ns, `ComputeDerived`의 계산 중 표·뷰 만들기 약 20ns, 결과 타입 검사 약 3ns, 실행기 약 97ns(따로 잰 72ns와의 차이 약 25ns는 엔티티마다 다른 뷰라서 생기는 캐시 효과로 추정). 프로파일러(dotnet-trace)는 `ComputeDerived` 안을 UNMANAGED로만 잡아 쓸 수 없었다. 인자 배열(`new[] { self }`)을 다시 써도 할당만 32B 줄고 시간은 같았고, `IsEntry` 캐시와 프레임 자유 목록도 이득이 없어 되돌렸다. 남은 할당(실행기 약 64B)은 부를 때마다 새 지역 변수 표(`LocalTable`과 칸 배열)로 보인다(추정: 람다가 표를 붙잡으므로 람다 없는 함수만 표를 다시 쓸 수 있다). 계산 반복의 남은 할당(바퀴당 약 100B)은 값마다 새 `IntValue`라 값 구조체화(맨 끝) 몫이다.
  - 함께 바뀐 것: 불러온 저장의 멈춘 프레임·묶음에 함수 배치 밖의 지역 변수 이름이 있으면 이제 `SAV` 손상 오류다(전에는 그냥 넣었다, `ASavedLocalThatTheFunctionDoesNotHaveIsACorruptSave`). 공개 타입 `ClosureValue`의 생성자와 `Captured`가 internal이 되고 `CapturedLocals`가 없어졌다. AST가 모든 월드가 나눠 쓰는 캐시를 든다(`Expr.Compiled`·`Stmt.Compiled`·`FunctionDef.Layout`·`LambdaExpr.Layout`). 전역 이름 노드는 실행기 하나만 기억해서, 두 월드가 같은 콘텐츠를 번갈아 돌리면 맞게 돌지만 전역 이름을 매번 다시 찾는다.
  - 부르기 준비 줄이기(커밋 뒤 시도, 코드 변경 없음): 위 측정대로 실행기의 부르기 준비는 72ns로 이미 작고 월드 쪽 다리도 약 43ns(그중 `Get` 기본 20ns)라 몇 ns 넘게 짜낼 곳이 없었다. 시도한 `IsEntry` 캐시·프레임 자유 목록·인자 배열 재사용은 시간 이득이 없어 넣지 않았다.
  - 남은 작은 후보(사용자와 고를 것, 이득은 작을 것으로 추정): 람다 없는 함수의 지역 변수 표 다시 쓰기(부를 때마다 약 64B), 문장 수준 호출(`x = f()`)의 결과 칸 번호, 컴프리헨션 이름 공간 프레임 재사용, `contribs()` 빈 목록. 큰 이득은 값 구조체화(백본 ECS화와 함께 맨 끝)나 B 몫이다.

지금까지의 기록(날짜순): 반응 ①(G145~G153)이 끝났고 시간 슬라이스 ①(틱·`Advance`·`Tick`·`now()`, G154~G156)과 ② `wait`(G157·G158)를 했고 ③ 마감(G159~G162, 커밋함)과 ④ 대상 소멸(G163~G166)과 스크립트의 `advance`(G167)·호출당 상한(G168)을 했다(커밋 `7b0d5c9`). 반응 ② `when`과 정착(G169~G172), `repeat`·`cooldown`·`once`(G173~G175)를 했다(커밋 `e9a65d6`). S5 열린 질문 G176·G177과 "읽은 칸이 바뀔 때만 재평가"를 했다(커밋 `f44f472`). `field`가 `derived` 상한을 넘는 경우(G99의 패턴 B 확인, 문서 갱신)를 했다(커밋 `d4703fe`). S5 열린 질문은 끝났다. 적합성 점검 뒤 난수(G178~G182)를 했다(커밋 `d908c91`). `cost`·`pay`·`choose` 슬라이스(G183~G188)를 했다(커밋 `52b97e6`). 검증 시나리오 ③을 런타임 테스트로 했다(커밋 `9584650`). 컴프리헨션(G189~G191)을 했다(커밋 `fcf766d`). 전역 순회 `query`(G192·G193)를 했다(커밋 `f4a6902`). 스크립트 순회의 G88(G194)을 고쳤다(커밋 `0e924a6`). 검증 시나리오 ①과 `while`(G195)을 했다(커밋 `68a4398`). 검증 시나리오 ②와 보기 정책·거른 읽기의 첫 조각(G196~G203)을 했다(커밋 `427345c`). 거른 결과 `Show`(G201·G204)와 `LegalMoves`·`Candidates`(G202)를 했다(커밋 `12d1872`). 거른 `Explain`(G205)을 했다(커밋 `780e595`). 검증 시나리오 ④를 코어 수정 없이 런타임 테스트로 했다(`ScenarioRealtimeTests`). 이로써 시나리오 ①~④가 다 됐다. 거른 `Advance` 결과(`Show`, `BlockedBy`는 답할 수 있는 질문만, `Blocked`는 그대로)도 정해 구현했다(G206, 커밋 `d987345`). 로직용 `.pyi`(G207·G208, 커밋 `09da3e8`)와 모드별 스키마 스텁(G209, 커밋 `ad362ba`)을 했다(아래 남은 일 2). 적합성 기준 C1~C10을 점검했다(spine-spec 7절, C9의 `Respond` 테스트만 더함, 커밋 `eb04421`). S8 칼럼 삭제는 고아 영역 대신 "기본 거부, 호스트가 허락하면 버리고 경고"로 정해 구현했다(G210, 커밋 `4b5f955`). Def 삭제도 같은 옵션으로 "불러올 때 파괴"로 했다(G211, 커밋 `21ef858`). 모드 내용 해시(G212, 엔진은 파일별 지문이라는 사실만, 무시할지는 사용처)와 그 전제인 파일 이름 NFC 정규화(G213, 재현한 자소 분리 버그)를 했다. 이어 "Flow 취소 후 로드"(G214, 호스트 옵션 `cancelBrokenFlows`, `SAV006`)를 했다(G212~G214 커밋 `012f5f4`). S8에 남은 것은 `migrate`다.

## (참고) 첫 슬라이스를 시작할 때의 합의 (2026-10-15)

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

## 메모: 저장소 구조와 속도 (2026-10-16, 논의만, 결정 아님)

- 지금 월드 저장은 ECS식 struct 배열이 아니다. `World`가 `Dictionary<long, EntityRecord>`와 엔티티마다 `Dictionary<MemberDecl, Value>`(바뀐 값만), 줄은 `SortedDictionary`, 색인은 `SortedSet`으로 들고 있다(결정론 때문에 정렬 컬렉션). 값은 `Value` 객체다.
- 스크립트 `@on("tick")` 핸들러가 System 자리이고, 엔티티 순회는 본문이 직접 한다.
- 저장 접근이 `World.cs`·`World.Derived.cs`·`World.Save.cs`·`World.Commands.cs`·`World.Explain.cs` 다섯 파일에 있다. 저장소 인터페이스는 아직 없다(spec P6은 "인터페이스 뒤 격리"). 단순 맵 교체는 쉽지만 컴포넌트별 배열로 가려면 값 표현·undo·역방향 색인·저장 불러오기를 함께 건드려야 한다.
- 벤치가 아직 없어 병목이 저장인지 인터프리터인지 모른다. 저장을 바꾸기 전에 임시 벤치(엔티티 N개를 `tick`에서 갱신)로 먼저 재고, 저장이 병목이면 인터페이스 추출 → 교체 순으로 한다.
- (2026-10-18 덧붙임) 벤치(G223)가 생겼고 병목은 인터프리터였다(`--shapes`에서 70~100%). G226으로 줄인 뒤에도 남은 큰 몫은 실행기다. 저장(열 저장)은 값 구조체화와 함께 맨 끝에 하기로 했다(G226).

## 문서와 큰 파일 정리 (2026-10-18~19)

문서가 너무 길어졌다(2026-10-18 기준 `spec-gaps.md` 300KB·234줄로 표 한 행이 수천 자, `handoff.md` 120KB). 큰 소스 파일은 `Script/Execution/PureEvaluator.cs` 1506줄, `Script/Parser.cs` 1214줄, `Runtime/World.cs` 1199줄, `Runtime/World.SaveFlows.cs` 1067줄, `Script/Interpreter.cs` 985줄, `Runtime/World.Flows.cs` 937줄, `Loading/ContentLoader.cs` 921줄, 테스트 `Runtime/TimeTests.cs` 1016줄이었다. 동작은 바꾸지 않았으므로 테스트 전체 통과(1919개)로 확인했다.

- **문서:** handoff를 현재 상태(`handoff.md`, 약 12KB)와 지난 기록(이 문서)으로 나눴다. spec-gaps는 색인(`docs/spec-gaps.md`)과 영역 파일(`docs/gaps/`, 내용 그대로 옮김, 칸마다 대조함)로 나눴고, 색인 영역 표에 "무엇을 정했나" 요약 칸을 226행 모두 채웠다(요약은 "어떻게 정했나"를 줄이기만).
- **큰 소스 파일, 두 단계:** 1단계는 partial 파일로 크게 나누기(코드는 옮기기만, 줄 대조와 테스트로 확인), 2단계는 나눈 파일 안의 논리적 덩어리를 제자리 타입으로 구조화. 저장 구조를 인터페이스 뒤로 옮기는 일은 G226대로 맨 끝이라 앞당기지 않았다.
- **1단계(커밋 `d799448`):** World.cs → +Rows·Values·Records, PureEvaluator.cs의 `Session` → +Calls·Preconditions·Stepping, Parser.cs → +Compound·Expressions, World.SaveFlows.cs → +LoadFlows, Interpreter.cs → +Declarations, World.Flows.cs → +Ask, ContentLoader.cs → +Mods·Build, 테스트 TimeTests → +Stale, ContentLoaderTests → +Registration. 바뀐 코드는 `partial` 수식어뿐이다.
- **2단계 ①(커밋 `b080041`):** 동작 클래스 파일에 얹힌 공개 타입 21개(`Request`·`AnswerResult`·`SubmitResult`·`Reaction`·`ExplainNode`·`WorldView`·`ScriptError`·`ModScope`·`ContentLoadResult`·`ScopeGlobals` 등)를 제 파일로 옮겼다. 규칙은 CLAUDE.md "파일 배치".
- **2단계 ②(커밋 `7301b1b`):** PureEvaluator의 안쪽 `Session`(Session.cs·.Calls·.Preconditions·.Stepping)·`Frame`(`ResultTo` 포함)·`Block`·`NameSite`를 같은 이름 공간의 제 파일로 뺐다. 넓힌 것은 `PureEvaluator.Globals`(새 internal 속성)·`LayoutOf`·`BareLambdaLayout`·`NameSite`의 접근과 노드 창구 `NodeFor`·`CallParts`(노드 타입은 private 그대로). `CallNode`를 internal로 넓히는 길은 접근성 불일치가 노드 타입 전체로 번져 되돌렸다. `PureEvaluator.Suspended`는 이름이 뜻을 전하므로 안쪽에 남겼다. World의 기록 타입과 ContentLoader의 `ModUnit`은 private 구현이라 안쪽에 두기로 했다(이유는 World.Records.cs 머리 주석).
- **handoff "남은 일" 점검(2026-10-19):** 16개 항목을 코드와 문서에 대조해 끝난 것(로직용 `.pyi`, G32 spec 반영, `derived` 안의 `query`, `require` 뒤 좁히기, 커맨드를 함수로 부르기, 이 정리)을 지우고 번호를 새로 매겼다. 스텁 이름 충돌은 재현했다(멤버 `self`·`id`, 타입 이름 `Mod`가 겹쳐도 `engine check`는 오류 0개). 옛 번호를 가리키던 spec-gaps의 G207·G118·G117·G100 글도 고쳤다(이 문서의 옛 기록은 그대로 둠). 진단 코드 체계 G6은 코드 전체가 이미 따르므로 사용자 동의로 `확인`으로 올렸다.

## 속성 이름 오타의 로드 검사 1차 (G228, 2026-10-19)

handoff "남은 일" 1번. 재현(임시 테스트): `for u in query("Unit"): u.Fod.amount += 1`, `def Feed(u: Ref[Unit]): u.Food.amout += 1`, `derived(int, lambda h: h.Lfe.hp)` 모두 `check` 오류 0개였다. 엔진 스텁으로 pyright 1.1.414를 돌리면 앞의 둘과 반환값을 거친 `s = top(); s.Spel`은 잡고 `derived` 인자와 `Action["Spell"]` 칸의 `def bolt(s):`는 놓쳤다. 사용자와 정한 것: 엔진 자체 검사(가), 범위는 적힌 타입 + 문맥(A), 반환 타입 추론은 2차.

- **구현:** `Loading/AttributeChecker.cs`(문맥 모으기와 타입 표기)·`.Walker.cs`(본문 훑기와 이름 규칙)·`Loading/StaticType.cs`(엔티티·컴포넌트·키 줄 묶음·Def 상수·묶음, 모름, None, 합치기). ⑤ 링크에서 `Linker` 다음에 돈다(Def의 최종 값으로 `Cond`·`Action` 칸의 함수를 알아야 해서). 지역 변수는 대입을 합쳐 바뀌지 않을 때까지 되풀이하고(최대 16바퀴, 넘으면 모름) 마지막 바퀴에서만 오류를 낸다. 이름 규칙은 `World.EntityAttribute`·`ComponentAttribute`·`DefConst`와 같고, "혹시 …?" 후보는 그 값의 것, 없으면 스키마의 컴포넌트·Def 타입 이름 전체다. 오류 코드는 G108과 같은 `EVL001`.
- **기존 테스트:** 실행 중 오류를 시험하려 일부러 넣은 오타(`gld`·`lefft`·`nope`·`cosst`)가 로드 오류가 되어 105개가 실패했다. 실행 중 안전망은 타입을 모를 때 여전히 필요하므로 그 자리를 `[unit.Moves][0].nope`처럼 리스트로 감싸 타입을 모르게 했다(8곳, 주석에 G228). 진짜로 틀린 테스트 내용 두 개도 드러났다: 스키마에서 `Score.log`를 지우고도 로직이 쓰던 저장 테스트(감싸서 로드되게 함), 스키마에 없는 `g.Score`를 쓰던 효과 테스트(`Score` 컴포넌트 선언을 더함).
- **새 테스트:** `Loading/AttributeCheckerTests.cs` 15개(query 반복, 적힌 매개변수, `derived`, `Action` 칸, Def 상수, 쓰기 대상, 지역 변수 합치기와 None, 핸들러·`@when`·`acts_for`, `choose` 후보, `spawn`·컴프리헨션·역방향, 키 줄, 실행이 받는 이름은 통과, 모르는 타입은 넘어감, 적힌 타입이 문맥보다 앞섬, 책임 모드).
- **확인:** 테스트 1934개 통과(커널 1898, CLI 36). 다섯 예시 모드에 `engine check`를 돌리면 넷을 잡고 `s = top()`만 남는다(2차 몫). 이 컨테이너에는 .NET 9 SDK를 받을 길이 막혀 있어 Ubuntu의 .NET 10 SDK와 `DOTNET_ROLL_FORWARD=Major`로 돌렸다(저장소 루트의 `global.json` 때문에 다른 폴더에서 `dotnet test <경로>`).

## `derived` 순환의 로드 검사 (G229, 2026-10-19)

- **재현:** `a=derived(int, lambda h: h.b + 1), b=derived(int, lambda h: h.a)`가 로드를 오류 없이 통과하고 읽을 때에야 `RUN014`(임시 테스트로 확인 후 지움).
- **논의:** schema-design 3.3.1 5항은 다른 엔티티를 거친 것까지 넉넉하게 막고 쪼개게 했다. 하위 지역의 합처럼 같은 값을 다른 엔티티에서 읽는 계층 합산은 정적으로 `Region.total → Region.total`로 보이고 쪼갤 수 없어 5항이 너무 엄격했다고 정리했다. Ref에 `acyclic=True`를 선언하게 하는 안도 냈으나 넣지 않기로 했다.
- **구현:** `Loading/AttributeChecker.Cycles.cs`. `StaticType`의 `EntityType`·`ComponentStatic`에 `Self`(계산 중인 `derived`의 엔티티 자신)를 더하고 합칠 때는 둘 다 자기 것이어야 자기 것이다. 형제 컴포넌트와 `entity`는 `Self`를 물려받고, 멤버 값(Ref)·역방향·`query`·키 컴포넌트의 줄 묶음은 물려받지 않는다. `Walker`가 순환 검사 모드(`DerivedReads`를 받음)면 이름 오류를 내지 않고 마지막 바퀴에 자기 것의 `derived` 읽기를 모으며, 자기 것을 넘겨받은 모드 함수 호출은 그 본문을 따라 들어간다(읽기 자리는 바깥 호출). 그래프를 깊이 우선으로 돌아 순환 하나에 오류 하나(`SCH009`, 새 코드), 책임은 가장 늦게 로드되는 모드, 다른 읽기 자리는 관련 위치. 함수 기본값은 보지 않는다(G106).
- **기존 테스트:** 실행 중 재진입 감지를 보는 `WorldDerivedTests`의 `Loop`가 로드 오류가 되어 9개가 실패했다. `b`를 `[l][0].a`로 감싸 엔티티 자신인 줄 모르게 했다.
- **새 테스트:** `Loading/DerivedCycleTests.cs` 6개(둘이 서로, 자기 자신, 형제·`h.entity`·조건식의 다른 갈래, 도우미 함수 따라가기, 다른 엔티티·다른 줄을 거친 것은 통과, 필드·상수 읽기는 순환 아님). 재귀 도우미는 이미 `EVL002`라 시험하지 않았다.
- **확인:** 테스트 1940개 통과(커널 1904, CLI 36).

## 스키마 이름과 내장의 충돌 (G230, 2026-10-19)

- **재현:** 멤버 `self`, const `id`, 컴포넌트 `Mod`·`random`이 든 모드가 `engine check` 오류 0개인데 pyright는 스텁에서 13개를 냈다(중복 매개변수, `Mod`·`random`의 다시 선언). 엔진 쪽: 컴포넌트 이름이 `random`·`ask`·`wait`·`advance`·`emit`·호스트 루트면 첫 제출에서 `ArgumentException`(내장 표의 같은 키)으로 죽었고, `floor`·`text`처럼 먼저 들어가는 내장이면 생성자가 조용히 가려졌다.
- **논의:** 예약 이름으로 막는 안을 냈다가 사용자가 G112(엔진 이름은 덮어쓸 수 있는 내장)와 어긋난다고 짚어 거뒀다. 스키마 이름도 G112처럼 내장을 이기되 그것이 보이는 모드 안에서만으로 정했다(G230). 스텁은 같은 규칙에 맞추는 수정이라 따로 묻지 않았다.
- **구현:** 이름 층은 지역 → 모드 이름 → 보이는 스키마 이름 → 내장. `ModScope.SetSchemaNames`·`TryLookup`·`IsSchemaName`(로더가 `DefBuiltins.SchemaName`을 넣는다), `Interpreter`·`DeferredEvaluator`·`FunctionEffects`·`FunctionNameChecker`·`AttributeChecker.Globals`가 층을 본다. `@command`·`require`가 스키마 이름에 가려지면 `CMD001`·`CMD002`의 메시지가 "a component or signal this mod sees"라고 말한다. 런타임은 생성자를 내장 표에서 빼고 `World.Derived`의 `WorldGlobals`가 모드의 가시성으로 찾는다(`AddComponentConstructors`·`AddSignalConstructors`). 저장된 Flow의 생성자 값은 `schema` 태그(옛 `builtin` 태그도 읽음). 스텁: `__init__`의 위치 자리(`self`, Def의 `id`)를 위치 전용으로 적고 키워드 이름과 겹치면 앞에 `_`를 붙인다. 보이는 컴포넌트·신호와 같은 이름의 내장 선언은 뺀다(`WithoutNames`). 컴포넌트 `Mod`면 `mod.py`의 `Mod(...)`가 편집기에서 거짓 오류인 것은 G82의 한계로 적었다.
- **새 테스트:** `Loading/SchemaNameShadowingTests.cs` 5개(`random` 컴포넌트가 선언한 모드에서는 생성자·무관한 모드에서는 엔진 `random`, `floor` 생성자가 `derived`에서, `require`·`command`가 가려진 메시지, 무관한 모드의 `command` 컴포넌트는 데코레이터를 가리지 않음), `StubGeneratorTests` 3개. 기존 스텁 테스트의 `__init__` 문자열에 `/`가 붙었다. `WorldSaveFlowTests`는 멈춘 Flow가 생성자 값을 들고 저장되는 경우를 더했다.
- **확인:** 재현 모드에서 pyright 13개 → 2개. 남은 둘은 맞는 지적이다: `random.randint`(그 모드에서는 컴포넌트, 실행하면 실패, 엔진 `check`는 못 잡음, handoff 남은 일 2번)와 `mod.py`의 `Mod(id=...)`(G82 한계). 테스트 1948개 통과(커널 1912, CLI 36).

## 생성자에서 속성을 읽는 것의 로드 검사 (G228·G230 보강, 2026-10-19)

- **재현:** `random` 컴포넌트가 보이는 모드에서 `random.randint(1, 6)`이 `engine check` 오류 0개였고 실행하면 `'built-in function' has no attribute 'randint'`였다(G230 뒤로 `random`이 그 모드에서는 생성자라서. pyright는 잡았다).
- **구현:** 속성 검사(`AttributeChecker`)의 타입에 `ConstructorType`(컴포넌트·신호 생성자)을 더했다. 모드가 묶지 않은 보이는 스키마 이름(`ModNames.Constructors`, 지역 변수가 먼저)이 이 타입이고 속성이 없다. 같은 이름의 런타임 내장(`PureBuiltins.IsRuntimeName`)이 있으면 힌트가 "이 모드에서 내장이 가려졌으니 이름을 바꾸라(G230)", 아니면 "값을 먼저 만들라"다. 실행 쪽은 `PureBuiltin.Kind`를 더해 생성자의 오류 이름이 "component constructor random"·"signal constructor Hit"다. 새 결정이 아니라 G228의 규칙(타입을 아는 값에 없는 이름은 로드 오류)을 새 타입에 쓴 것이라 G 번호를 새로 두지 않고 G228·G230에 적었다.
- **새 테스트:** `SchemaNameShadowingTests` 4개(내장을 쓰려던 경우의 힌트, 컴포넌트·신호의 멤버를 생성자에서 읽은 경우, 같은 이름의 지역 변수·매개변수는 생성자가 아님, 값을 거쳐 실행 중에 닿은 경우의 메시지).
- **확인:** 재현 모드의 `engine check`가 pyright와 같은 자리(5:24)에서 오류를 낸다. 테스트 1952개 통과(커널 1916, CLI 36).

## 속성 오타 검사의 함수 반환 타입 (G231, 2026-10-19)

- **재현:** `a = first(); a.Fod`(본문에서 엔티티를 돌려줌), `annotated().Fod`(`-> Ref[Unit]`), `same(x).Fod`(`def same(u): return u`)가 `engine check` 오류 0개였고 pyright는 셋 다 잡았다.
- **논의:** 가(적은 반환 타입 + 본문의 return), 나(가 + 부른 자리의 인자로 다시 추론), 다(적은 반환 타입만) 중 나를 골랐다(G231).
- **구현:** `Loading/AttributeChecker.Returns.cs`의 `ReturnTypes`. 같은 모드의 함수 이름 호출이면 적은 반환 타입, 아니면 그 인자 타입으로 새 `Walker`(오류를 내지 않음)를 돌려 마지막 바퀴의 `return` 타입을 합친 것(`Walker.Returned`)이다. 함수와 인자 타입마다 결과를 기억하고, 훑는 중인 함수를 다시 부르면 모름. 적지 않은 매개변수의 인자 타입이 아직 없으면(대입을 모으는 바퀴) null이라 지역 변수 합치기와 맞물린다. `Walker.Reporting`은 `StageDiagnostics`가 없으면 거짓이다. 모드 이름(`ModNames`)은 `ReturnTypes`가 모드마다 한 번 만든다.
- **새 테스트:** `AttributeCheckerTests` 3개(셋의 재현과 이름 인자, 이름에 묶은 람다와 함수 안의 호출 따라가기, 적은 `int`·다른 타입의 반환·모르는 인자·반환 없음은 모름). 기존 테스트의 "함수의 결과는 모른다" 주석을 고쳤다.
- **확인:** 재현 모드의 `engine check`가 pyright와 같은 세 자리에서 오류를 낸다. 테스트 1955개 통과(커널 1919, CLI 36).

## 덤프의 `patches`를 값을 만든 패치만으로 (G76 구현 보강, 2026-10-19)

- **재현:** `Unit("Base", move=2)`, `Unit("Scout", parent="Base", move=3)`에 `patch.set("Base", "move", 5)`를 하면 Scout의 `move`(3, own) 칸 `patches`에 Base의 set이 나왔다. `DefDump`가 뿌리부터 이 Def까지 체인의 Def마다 겹치는 경로의 패치를 모두 훑었기 때문이다.
- **수정 방향:** G76이 정한 "그 칸에 영향을 준 패치"를 바르게 구현하는 것이라 묻지 않았다. 체인을 훑지 않고 값을 만들 때 기여한 패치를 따라 적는다.
- **구현:** `ResolvedValue.Patches`(적용 순서). 물려받으면 그대로(`AsInherited`), 자식이 직접 적은 값은 빈 목록, `PatchApplier`의 통째 `set`은 그 패치 하나, `add`·`append`·`remove`·맵 항목 `set`은 그 전 값의 목록 뒤에, 패치로 붙인 컴포넌트·줄의 값은 그 패치. `InheritanceExpander`는 `merge="append"` 리스트면 부모 값의 목록을, `inherit` 식이면 `DeferredEvaluator.ReadPatches`(식이 읽은 부모 값들의 목록, 값마다 `ResetReads`)를 쓴다. `DefDump.ValueEntry`는 이 목록만 낸다. `PatchLog.ForField`(그 칸을 건드린 모든 패치)는 공개 API로 그대로 둔다.
- **새 테스트:** `DefDumpTests` 3개(자식이 직접 적거나 통째 set한 값에는 부모 패치가 없음, 물려받은 값·`inherit` 식 위의 `add`는 부모 쪽 먼저 적용 순서로, `merge="append"` 리스트는 부모의 패치를 이음).
- **확인:** 테스트 1958개 통과(커널 1922, CLI 36).

## 텍스트 콘솔 호스트와 예시 게임 (2026-10-19)

- **왜:** 남은 일보다 외부 연결이 급하다고 보고(handoff의 S10 논의 메모), 호스트 엔진이 미정이라 엔진 무관한 시험대부터 만들었다. 커널 API를 테스트 코드가 아니라 사람이 실제로 플레이하는 루프에서 써 보고 구멍을 찾는 것이 목적이다. 같은 레포에 둔다: 커널이 아직 배포되지 않고, 구멍을 고칠 때 커널과 호스트가 한 커밋으로 바뀐다. 실제 게임은 나중에 따로 레포를 둔다.
- **예시 게임 `samples/skirmish`(모드 id `skirmish`):** 5×5 판, 빨강·파랑, 진영마다 병사 둘(체력 10, 공격 4, 사거리 1)과 궁수 하나(6, 3, 2). 자유 커맨드 `Start`가 판을 깔고 `while` 루프로 지금 진영에 `ask(p, "turn", responses=["Move", "Attack", "EndTurn"])`를 계속 묻는다(차례는 모드의 것, X2·U2). `Attack`은 공격을 `Combat`에 기록만 하고, 루프가 주사위(4 이상 명중)를 굴려 맞은 쪽 진영에 `ask(..., "hit", choices=["hold", "retreat"])`로 묻는다(후퇴하면 피해 절반, 공격자 반대쪽으로 한 칸). 입구 정책은 `Seat.conn == issuer`. 한 진영이 다 없어지면 `Match.winner`가 정해지고 Flow가 끝난다.
- **콘솔 호스트 `src/Kesa.Engine.ConsoleHost`(실행 파일 `kesa-play`):** `kesa-play <mods-dir> --seat <conn>... [--seed N]`. 핫시트. 게임 지식 없이 공개 API만 쓴다: 상태는 스키마의 Def 타입·컴포넌트를 따라 모든 엔티티를 늘어놓고(`All`·`Has`·`Get`), 할 수 있는 것은 열린 질문의 선택지(`Answer`)와 응답 커맨드의 합법 수(`LegalMoves` → `Respond`), 자유 커맨드의 합법 수(`Submit`)를 번호로 보여 준다. 낼 수 있는 것이 없으면 끝난다. 종료 코드는 CLI처럼 0·1(콘텐츠 오류)·2(사용법). 월드는 `requireSubmitPolicy: true`.
- **드러난 구멍(우회함, 결정 아님):** 응답 커맨드의 합법 수를 질문에 대해 물을 수 없음(호스트가 `acts_for` 람다 AST를 엶), 엔티티로 행동할 연결을 물을 수 없음(자리마다 내 보고 `GateDenied`면 넘김), 변경 구독 없음, 번역 없음, 자리 배정을 모드가 박아 둠, 응답 커맨드 안의 `ask`는 G139 제한으로 피함. handoff의 "콘솔 호스트에서 드러난 구멍".
- **테스트:** `tests/Kesa.Engine.ConsoleHost.Tests`의 5개. 화면을 읽고 고르는 봇(맞으면 버팀, 공격 가능하면 공격, 아니면 가장 가까운 적 쪽으로 이동, 그다음 차례 넘김)이 한 판을 끝까지(시드 1: 21턴, 빨강 승) 돌림, 명중 질문을 파랑 자리가 답함, 같은 시드면 같은 화면·다른 시드면 다른 화면, 잘못된 번호는 다시 물음, 자리·폴더 없음은 사용법 오류.
- **확인:** `engine check samples/skirmish` 오류 없음. 테스트 1963개 통과(커널 1922, CLI 36, 콘솔 호스트 5).

## 질문 단위 응답 합법 수 `LegalResponses` (2026-10-19, G232)

- **재현:** 예시 게임에서 빨강에게 연 질문에 `LegalMoves("skirmish.Move", {})`는 "`party`를 호스트가 줘야 함"(G184) 오류, `party`에 파랑을 넣으면 파랑의 수 5개를 나열했고 그 수를 `Respond`하면 `NotListed`. 콘솔 호스트는 `acts_for` 람다 구문을 열어 우회하고 있었다.
- **논의:** 다른 사례(OpenSpiel `legal_actions(player)`, boardgame.io `activePlayers`+`enumerate`, CK3 이벤트 선택지·상호작용(기억), RimWorld Gizmo(기억), 이전 TR의 `ValidTargetIds`(U6))는 모두 "누구 기준인가"를 엔진이 알거나 API 인자로 받는다. 구조는 게임–엔진–UI(엔진이 가운데)이고, 이번 구멍은 UI가 엔진을 건너뛰어 모드를 읽는 변이었다. `Who`로 거르기는 `Respond`의 `NotListed` 검사를 미리 하는 것이라 엔진에 맞다(사용자 확인).
- **구현:** `World.LegalResponses(request, by, max)`(`World.Choices.cs`), 결과 `ResponseMoveList`·`ResponseMove`·`ResponseNeeds`(`ResponseMoveList.cs`). 답하는 매개변수는 `acts_for` 람다의 본문이 람다 매개변수이자 커맨드 매개변수인 이름이고 `choose`가 없을 때(`ActorParameter`). 그 밖에는 `LegalMoves`의 `keep`으로 `acts_for` 결과가 대상인 것만. 상한은 남은 몫으로 부르고 몫이 없으면 하나만 더 찾아 `Truncated`. `WorldView.LegalResponses`는 `MayAnswer`(G200)로 대상을 좁히고 `AllSeen`(G202)으로 수를 거르며, 답할 수 없는 질문은 `World.NoRequest`와 같은 오류. 콘솔 호스트는 새 API를 쓰고 람다 들여다보기(`ResponseArgs`)를 지웠다.
- **새 테스트:** `LegalResponsesTests` 9개(대상으로 채우기와 `acts_for`로 거르기·`Needs`, 나열한 수는 모두 `Respond`가 받음, `by`로 좁히기, 상한 4가지, `ViewAs`, 없는 질문).
- **덤으로 본 것:** `ViewAs(연결).OpenRequests`는 이미 그 연결이 답할 수 있는 질문만 준다(G200). 콘솔 호스트 구멍 2번(어느 연결이 답하나)은 `ViewAs`를 안 써서 생긴 우회일 수 있다.
- **확인:** 테스트 1972개 통과(커널 1931, CLI 36, 콘솔 호스트 5). `spec-gaps` 표 확인 `231 6`.

## 거른 합법 수에 입구 정책 (2026-10-19, G233)

- **재현:** `acts_for=lambda party: party`인 자유 커맨드에서 `ViewAs("a").LegalMoves`가 남의 진영(#2)으로 내는 수까지 나열했고, 그 수를 `a`로 `Submit`하면 `GateDenied`였다(임시 테스트, 지움). 질문 쪽은 G200·G232로 이미 걸러져 있었다.
- **논의:** 가(`ViewAs`의 `LegalMoves`가 `acts_for` → `may_act`도 거름), 나(엔티티로 행동할 수 있는 연결을 묻는 새 API), 다(호스트의 자리 차례로 내 보기를 그대로) 중 가를 골랐다. 나는 연결 목록을 엔진이 모른다.
- **구현:** `World.MayIssue(issuer, command, move, diagnostics)`(`World.View.cs`): 제출과 같은 `ActsFor` → `MayAct`, 실패하면 빼고 진단. `WorldView.LegalMoves`의 거름 람다 끝에서 부른다. 상한·`Truncated`는 거른 뒤. 월드의 `LegalMoves`와 `Candidates`는 그대로.
- **콘솔 호스트:** 자리마다 `ViewAs(자리)`의 `OpenRequests`·`LegalResponses`·`LegalMoves`로 할 수 있는 것을 모으고, 메뉴에 `[자리]`를 붙여 그 자리로 바로 낸다. 자리 차례로 내 보기(`Issue`)와 `Outcome.GateDenied`를 지웠다. 남은 작은 후보: 대상이 여럿인 질문이면 `Who`에 그 자리가 답할 수 없는 대상도 보인다(주석 "구멍 후보").
- **새·바뀐 테스트:** `WorldViewTests.LegalMovesListOnlyMovesTheConnectionMayIssue`(A·B·C 연결마다 자기 진영 수만, 상한은 거른 뒤, 나열한 수는 `Submit` 통과). 기존 테스트의 "A가 B 쪽 인자로 물으면 모두 보임" 기대는 빈 목록으로 바뀌었다.
- **확인:** 테스트 1973개 통과(커널 1932, CLI 36, 콘솔 호스트 5). `spec-gaps` 표 확인 `232 6`.

## 함께 물은 질문의 거른 대상 목록 (2026-10-19, G234)

- **재현:** `ask([a, b], "vote", ...)`(a는 A 연결, b는 B 연결의 진영)에서 `ViewAs("A").OpenRequests`의 `Who`가 `#1, #2`였고, A가 #2로서 답하면 `GateDenied`였다(임시 테스트, 지움). G233 때 콘솔 호스트에 "구멍 후보"로 적어 둔 것.
- **논의:** 가(거른 `Who`를 답할 수 있는 대상만으로), 나(`Who`는 그대로, 답할 수 있는 대상을 따로), 다(그대로) 중 가를 골랐다. G200이 남의 질문을 통째로 숨기니 "남도 물었다"도 숨기는 게 같은 기준이다.
- **구현:** `WorldView.ForMe`가 답할 수 있는 살아 있는 대상을 모아 `Filtered`에 넘기고, `Filtered`는 그중 보이는 것만 `Who`에 둔다. `OpenRequests`도 `ForMe`를 쓴다. `Show`의 열린 질문과 `BlockedBy`도 같은 길이라 함께 바뀐다.
- **새 테스트:** `WorldViewTests.AQuestionToSeveralListsOnlyTheTargetsThisConnectionMayAnswerFor`(월드는 둘, A·B는 자기 것만, `Show` 결과도, C에게는 질문 없음).
- **확인:** 테스트 1974개 통과(커널 1933, CLI 36, 콘솔 호스트 5). `spec-gaps` 표 확인 `233 6`.

## 바뀜 도장 (2026-10-19, G235)

- **왜:** 콘솔 호스트 구멍 3번. 커널에 바뀐 것을 세는 장치가 없어 호스트는 입력마다 모든 엔티티의 모든 컴포넌트를 다시 읽었다. spec S10 4항은 "읽기 집합 + 칼럼 버전"의 방향만 정해 두었다.
- **논의:** 이전 엔진(`kesa0v0/unity-kesa-engine`, 이번에 받아 읽음)은 UXML `binding-path`를 `DataPathResolver`가 풀어 R3 `Observable`을 구독했다. 잘한 점(UI 파일은 경로만, 경로 언어 하나, 참조가 바뀌면 `Switch`로 다시 붙음)과 문제(필드마다 모더가 `TryResolveBinding`을 손으로 씀, 로드 때 경로 검사 없음, 쓰는 순간 동기 알림이라 커맨드 한가운데서 효과·UI가 끼어듦, 롤백 없음, `Observable.Never` 중간 조각이면 끝 값이 안 갱신, 구독 해제 누락, 숨긴 정보 거름 없음)를 코드로 확인했다. 다른 사례(Bevy `Changed<T>`, Unity ECS 청크 버전, Unity UI Toolkit 버전 숫자, WPF·Godot의 밀어주기, RimWorld·패러독스의 다시 그리기, Salsa 리비전; 일반 지식)를 보고 도장 방식, 엔티티와 컴포넌트 단위, 지금 넣기를 골랐다. R3 같은 밀어주기는 필요하면 호스트 어댑터가 도장 위에 얹는다(지금은 필요 없음).
- **구현:** `World.Stamps.cs`. 전역 시계 `_clock`, 엔티티마다 `Stamps`(만든 때, 아무 칸, 칸 이름별). `Assign`이 컴포넌트를 받아 찍고, 붙이기·떼기·줄 붙이기·떼기·키 엔티티 삭제로 지워진 줄·기여 행·역방향 컬렉션(`AddInverse`·`RemoveInverse`)·생성·삭제·불러오기가 찍는다. `Atomically`가 시작 시계를 `_stampFloor`로 두고, 칸마다 처음 찍을 때만 옛 도장을 되돌리기 기록에 넣으며, 실패하면 되감은 뒤 시계를 돌려놓는다. 되감는 동안(`_rewinding`)은 기본 연산이 도장을 찍지 않는다.
- **콘솔 호스트:** 엔티티마다 그린 줄과 도장을 기억해 도장이 그대로면 다시 읽지 않는다. 테스트용 `AlwaysRedraw`로 다시 그린 화면과 같은지 견준다.
- **새 테스트:** `WorldStampTests` 6개(쓴 칸만 바뀜, 역방향 컬렉션과 기여 행, 붙이기·줄·삭제와 키 엔티티 삭제로 지워진 줄, 성공한 커맨드는 찍고 실패한 커맨드는 쓰기·만들기·붙이기·삭제를 모두 되감음, 없는 이름은 오류, 불러온 월드). `SkirmishTests`에 도장으로 건너뛴 화면과 매번 다시 그린 화면이 같은지 1개.
- **남은 몫:** `ViewAs`의 도장(숨긴 정보), 남의 엔티티를 읽는 `derived`(읽기 집합), 호스트용 경로 읽기(handoff 구멍 3의 다음 후보).
- **확인:** 테스트 1981개 통과(커널 1939, CLI 36, 콘솔 호스트 6). `spec-gaps` 표 확인 `234 6`.

## 배정 표와 입구·보기 정책 다시 짜기 (2026-10-19, G236·G237·G238)

- **왜:** 콘솔 호스트 구멍 5번. 예시 모드의 `Start`가 `Seat(conn="red")`를 박아 넣었다. G121(담당은 모드 데이터)·G122(입구 검사는 모드의 `may_act`) 때문에 로비 배정이 게임에 들어갈 길이 없었다.
- **논의:** "로비는 호스트 몫"과 "모드가 자리를 내놓고 호스트가 채움"은 사용자가 아니라고 해서 레퍼런스를 소스로 확인했다(Wesnoth `connect_engine.cpp`·`[side]`, 0 A.D. `NetServer.cpp` `m_PlayerAssignments`, OpenTTD `client_playas`, EU4 세이브의 `players_countries`·`was_player`(pdx-tools)). 모두 배정과 입구 검사를 엔진이 한다. 이전 엔진은 G121의 근거와 달리 빈자리였다(`EngineCommandRouter`가 클라이언트가 적은 진영 id를 믿음, TR·WG가 `SessionCommandRouter`로 메움). 사용자가 G121·G122를 다시 열고 G236 가(게임 상태 안의 배정 표), G237 ㄱ(`may_act` 없앰), G238 ㄱ(`may_see(viewer, entity, component)`)을 골랐다. 앞 결정을 바꾼 G에는 "바뀜" 상태와 `**바뀜:**` 줄을 붙이는 규칙도 정했다(A, spec-gaps 머리말과 CLAUDE.md).
- **구현:** `World.Seats.cs`: `SortedDictionary<long, string>` 배정 표, `Seat`·`Unseat`(호스트 쓰기, 커맨드 안에서는 금지, 빈 꼬리표와 죽은 엔티티는 `RUN022`, 도장을 찍음)·`SeatOf`·`SeatsOf`(id 순). 입구 `MayAct(issuer, target)`는 `None`이면 통과, 엔티티면 표에 그 연결이 앉아 있어야 한다. 엔티티가 죽으면 배정도 지우고 되감기를 기록한다. 저장에 `"seats"`(엔티티 id 순, 필수 필드, 형식 번호는 1 그대로), 불러올 때 버린 엔티티의 배정은 `SAV005` 경고, 없는 엔티티·중복·빈 꼬리표는 손상. 보기 정책은 연결이 앉은 엔티티마다 `may_see(viewer, ...)`를 부르고 하나라도 참이면 보임, 앉은 데가 없으면 `viewer=None`으로 한 번. `@submit_policy`는 `CMD005` "was removed" 오류, `requireSubmitPolicy`·`ContentLoadResult.SubmitPolicy`·`PolicyKind`·`IsSubmitPolicy`·`RUN016`(번호는 다시 쓰지 않음)을 지웠다.
- **예시 게임·콘솔 호스트:** `Seat` 컴포넌트 대신 차례 순서만 `Order(next=)`로. `kesa-play samples/skirmish --seat red=Red --seat blue=Blue`: 그 Def의 엔티티가 생기면 호스트가 앉힌다("[red] #1 Red에 앉음"). 로비 목록은 아직 없어 Def 이름으로 고른다.
- **테스트:** 새 `WorldSeatTests` 8개(아무도 앉지 않으면 거절, `None`은 자리 없이, 다시 앉히면 넘겨받음, 한 연결이 여러 엔티티, 입력 검사, 도장, 죽음과 되감기, 저장·불러오기), `WorldSaveTests.ABrokenSeatIsCorrupt` 3개와 버린 엔티티의 배정 경고, `WorldViewTests`의 관전자(`viewer=None`)·핫시트 합·진단의 "seen by a spectator", `SubmitGateDeclarationTests`의 정책 교체 규칙 테스트를 `@view_policy`로 옮김. 기존 테스트는 `may_act`·`Seat.conn` 대신 `World.Seat`로 앉히고, 질문에 `host`로 답하던 테스트는 답하는 엔티티에 `host`를 앉혔다(아무도 앉지 않은 엔티티로는 답할 수 없으므로).
- **문서:** spine-spec S6 권한 3층·배정 표·두 종류의 제출·네트워크, S10 5항, X1 `accepts`, 공개/비공개 정보. 바뀜 표시: G17·G121~G124·G125·G126·G127·G130·G131·G139·G197·G200·G203·G233·G234.
- **확인:** 테스트 1979개 통과(커널 1937, CLI 36, 콘솔 호스트 6). `spec-gaps` 표 확인 `237 6`.

## 2026-10-19: 로비 후보 `Seatable` (G239·G240)

- **정한 것:** G236에서 이어서 정할 것의 첫째. 로비가 보일 앉을 후보는 엔진 기본 제공 표시 컴포넌트 `Seatable`이 붙은 살아 있는 엔티티(G239). 엔진 제공 컴포넌트는 커널·호스트 API가 이름을 알아야 할 때만, 엔진 필드는 커널·호스트가 읽는 것만, 게임 데이터는 모드 컴포넌트, 그래픽·UI는 공식 모듈 몫(G240).
- **구현:** `Schema/EngineComponents.cs`(`Seatable`, 주인 자리 `""`). `SchemaBuilder.RegisterNames`가 엔진 신호처럼 먼저 등록하고, 모드가 같은 이름을 쓰면 SCH001 "'Seatable' is a component declared by the engine". 모든 모드에 보임(`ModVisibility`의 주인 `""`). `World.SeatChoices()` = `Query("Seatable")`, id 순. 스텁에는 `class Seatable`과 모든 타입의 `Seatable: Seatable | None`이 나온다. 예시 게임 `deftype("Party", components=["Order", "Seatable"])`. 콘솔 호스트는 아직 `--seat red=Red`(Def 이름)로 고른다.
- **구현하며 바로잡은 것:** G239 초안의 "모드는 `extend`로 필드를 더한다"는 틀렸다. `extend`는 타입에 컴포넌트·`const`를 더할 뿐 남의 컴포넌트에 칼럼을 더하지 못한다(S1). 게임 데이터는 같은 엔티티에 모드 컴포넌트를 붙인다로 고침. 또 Def에 적은 컴포넌트는 뗄 수 없으므로(S4) 후보에서 빠질 수 있는 엔티티는 Def에 적지 않고 붙인다를 G239에 적음.
- **테스트:** 새 `SeatChoicesTests` 9개(id 순 목록, 규칙의 `attach`·`detach`, 죽으면 빠짐, 호스트 `Add`·`Remove`, Def 것은 못 뗌, `Seat`를 막지 않음, 저장, 의존 없이 보임, 같은 이름 선언은 오류). `StubGeneratorTests` 기대값에 `Seatable`.
- **확인:** 테스트 1988개 통과(커널 1946, CLI 36, 콘솔 호스트 6). `spec-gaps` 표 확인 `239 6`.

## 2026-10-19: 콘솔 호스트의 작은 로비

- **정한 것:** 사용자가 고름(세 안 중 나). 콘솔 호스트가 `World.SeatChoices()`(G239)를 쓰는 첫 사례. 엔진 결정이 아니라 예시 호스트의 사용법이라 G 번호는 붙이지 않음.
- **구현:** `--seat red`(`=Def` 없음)는 빈 앉을 후보가 생기면 "앉을 자리: 1) #1 Red  2) #2 Blue / red는 어디에? >"로 묻고 번호로 앉힌다(`ConsoleGame.AskSeats`). 남이 앉은 후보는 빠지고, 잘못된 번호는 다시 묻고, `:q`나 입력 끝이면 끝난다. 연결마다 한 번만 묻는다(앉은 진영이 죽어도 다시 묻지 않음). `--seat red=Red`는 그대로 묻지 않고 그 Def의 엔티티에 앉힌다(테스트·자동 실행용). 둘을 섞으면 Def로 정한 연결이 먼저 앉는다.
- **테스트:** `SkirmishTests.ASeatWithoutADefIsPickedFromTheChoices`(red가 Blue를 고르면 Red 차례의 수는 `[blue]`로 감). 사용법 오류 테스트의 `--seat red`는 이제 맞는 입력이라 `--seat`(값 없음)과 `--seat =A`로 바꿈.
- **확인:** 테스트 1989개 통과(커널 1946, CLI 36, 콘솔 호스트 7). 손으로 `printf '1\n9\n2\n1\n:q\n' | kesa-play samples/skirmish --seat red --seat blue`를 돌려 화면 확인.

## 2026-10-19: 스크립트가 배정을 읽는 길 `controller(e)` (G241)

- **정한 것:** G236에서 이어서 정할 것의 둘째. 가(읽지 않음)·나(`seated`만)·다(종류) 중 다. 꼬리표는 계속 숨기고 사람·AI·빈자리만 읽는다.
- **구현:** 새 `Runtime/SeatKind.cs`(`Human`·`Ai`). 배정 표는 엔티티 → (꼬리표, 종류). `World.Seat(e, issuer, kind = Human)`, `SeatKindOf(e)`. 내장 `controller(e)`(`World.Seats.cs`의 `ControllerBuiltin`, `PureBuiltins.ControllerName`, 런타임 이름 목록): `"human"`·`"ai"`·`None`, 엔티티가 아니면 RUN005(타입 오류). when 조건이 읽으면 그 엔티티를 읽은 것으로 기록하고, `Seat`·`Unseat`가 `Touch`해 다음 정착에서 다시 본다. 저장의 `seats` 줄에 `"kind"`(필수, `human`·`ai` 밖이면 형식 오류). 저장 형식 버전은 그대로 1(G236 때처럼 출시 전). 스텁에 `def controller(entity: Any, /) -> Literal["human", "ai"] | None`.
- **테스트:** 새 `ControllerTests` 4개(사람·AI·빈자리 읽기, AI가 되면 when이 발동하고 사람으로 돌아갔다 다시 AI면 또 발동, 종류 저장, 엔티티가 아닌 인자). `WorldSaveTests.ABrokenSeatIsCorrupt`에 `kind` 칸과 잘못된 종류 사례. `Seat`의 `Touch`를 지우면 when 테스트가 실패하는 것을 임시로 확인하고 되돌림.
- **확인:** 테스트 1994개 통과(커널 1951, CLI 36, 콘솔 호스트 7). `spec-gaps` 표 확인 `240 6`.

## 2026-10-19: G236의 남은 것 정리 (공동 조종 미룸, API 이름 G242)

- **공동 조종(한 엔티티에 연결 여럿):** 미룸. 지금은 호스트가 한 꼬리표를 여러 사람에게 나눠 주면 된다(규칙이 그중 누가 냈는지 가르는 것만 안 됨). 사례가 생기면 `Seat`가 바꾸나 더하나, `SeatOf`의 목록화, 섞인 종류의 `controller()`, 저장 형식을 함께 정한다. spine-spec S6과 handoff에 적음.
- **API 이름(G242):** 가(그대로)·나(seat 통일)·다(controller 통일) 중 가. 코드 변경 없음.
- **확인:** `spec-gaps` 표 확인 `241 6`. 테스트는 코드 변경이 없어 1994개 그대로.

## 2026-10-19: 멈추는 응답 커맨드 (G243, 콘솔 호스트 구멍 6번)

- **정한 것:** 가(쌓는다)·나(나란히) 중 가. 응답 본문이 `ask`·`wait`·`advance`에서 멈추면 새 Flow가 되고, 바깥은 질문을 닫고 기다리다 그 Flow가 끝나면 같은 트랜잭션에서 `r.response`·`r.by`로 이어 간다. 이어서 "안쪽이 버려지면 바깥은?"을 물어 (가) 함께 버림·(나) 이어 감·(나+) 이어 가며 끊김 표시·(다) 질문 다시 열기 중 나+. 불러오며 취소되면 스크립트를 돌릴 수 없어 바깥도 함께 취소.
- **구현:** `FlowRecord`에 `Parent`(`ParentLink`: 바깥 Flow·응답 이름·응답한 엔티티)와 `AwaitsChild`. `World.Flows.cs`의 `AwaitResponse`(바깥을 질문 없는 기다림으로 바꾸고 본문을 `OpenFlow`), `Continue`가 본문이 끝나면 바깥을 재귀로 이어 감, `Lineage`·`Leaf`로 답·응답·깨우기 결과의 `OpenedRequest`·`SleepsUntil`을 실행이 끝난 뒤 멈춘 곳(맨 안쪽)으로. `ResumeAfterInterrupted`: 대상 소멸 취소(`SweepStale`·`WakeSleepers`), 깨운 뒤 오류(`Resume`의 catch, G157), `advance` 한도(G168)에서 버린 Flow의 바깥을 `r.interrupted=True` 답으로 이어 가고 결과 목록에 신호 `interrupted`(`Reaction.InterruptedSignal`). `SweepStale`은 함께 사라진 바깥을 잇지 않게 모두 버린 뒤에 잇는다. 답 값에 `interrupted`(스텁 `interrupted: bool`, 끊겼을 때만 저장에 `"interrupted": true`). 저장은 바깥에 `awaits`, 안쪽에 `parent`. 불러오기는 `awaits`와 `wake_at`이 함께 있거나 순서가 어긋나면 형식 오류, 양쪽 짝 확인, 기다리던 Flow가 저장에 없으면 형식 오류, 불러오며 취소되었으면 안쪽부터 바깥도 취소하고 `SAV006` 경고(`LoadProblems.CanceledWith`).
- **재현:** 응답 본문의 `ask`가 "not supported yet" 오류인 것, 그리고 핵심 구현 뒤 깨운 뒤 오류로 안쪽이 버려지면 바깥이 열린 질문도 깰 틱도 없이 영원히 남는 것을 임시 테스트로 재현하고 지웠다.
- **테스트:** 새 `NestedResponseTests` 14개(바깥은 본문이 끝난 뒤 돌아옴, 세 겹, `wait`·`advance`하는 본문, 이은 뒤 오류면 둘 다 기다림, 깨운 뒤 오류·대상 소멸·`advance` 한도로 끊기면 `interrupted`로 이어 감과 그 답의 저장, 바깥이 먼저 사라지면 혼자 끝남, 끊기지 않으면 거짓, 불러오며 세 겹 함께 취소, 기다리던 Flow가 없는 저장은 깨짐, 걸음마다 저장·불러오기가 같은 결과와 바이트, 저장에 연결 양쪽).
- **문서:** G243(색인·X-flow-time), G139 `일부 바뀜(G237·G243)`과 바뀜 줄, spine-spec X1 응답 문단.
- **확인:** 테스트 2008개 통과(커널 1965, CLI 36, 콘솔 호스트 7). `spec-gaps` 표 확인 `242 6`.

## 2026-10-19: 예시 게임의 공격 안 질문 (G243 사용)

- **정한 것:** 사용자가 고름(가: 예시를 고친다). 예시의 사용법이라 G 번호는 붙이지 않음.
- **구현:** `samples/skirmish`의 `Attack` 본문이 `resolve(unit, target)`을 바로 불러 명중 때 맞은 쪽 진영에 버팀/후퇴를 묻는다. 차례 루프(`Start`)는 공격 응답의 답까지 끝난 뒤 돌아와 승패만 본다. 공격자·대상을 차례 루프에 넘기던 우회용 `Combat` 컴포넌트를 지웠다.
- **확인:** 테스트 2008개 통과(커널 1965, CLI 36, 콘솔 호스트 7). `SkirmishTests.TheHitQuestionGoesToTheDefendersSeat`가 그대로 통과(질문은 파랑에게, 파랑 자리가 답함). 임시 테스트로 봇 판의 화면을 찍어 Attack 응답 뒤 `hit` 질문이 열리고 답한 뒤 피해가 들어가는 것을 보고 지웠다.

## 2026-10-19: UI 값 함수 부르기 1단계 `Call`·`CheckCall` (G244~G247)

- **정한 것:** G244~G248로 정한 것의 구현 첫 단계. 영수증(`Changed`)과 `ViewAs`는 다음 단계.
- **구현:** 새 `Runtime/World.Call.cs`와 `Runtime/CallResult.cs`(`Function`·`Value`). `world.Call("모드.함수", 값들…)`은 `_scopes`에서 이름을 찾고(점은 마지막 것으로 나눔), 인자를 `ToScript`로 바꿔 `RunGateFunction`(걸음 예산 하나, 스크립트 오류는 `파일:줄`이 붙은 `WorldException`, 엔티티는 `EntityValue`로)으로 돌린다. 쓰기 함수·커맨드·핸들러·반응에 더해 난수 함수(G178)도 순수가 아니라 부르지 않는다. 없는 모드·함수는 `RUN001`과 "혹시 …?"(후보는 부를 수 있는 함수만), 부르지 않는 함수와 인자 수는 새 코드 `RUN023`(`RunNotCallable`). `CheckCall(이름, 인자 수)`는 같은 확인을 해 null이나 이유 문장. 호스트 함수 안에서는 부를 수 없다(G217, `NoHostWrites`).
- **테스트:** 새 `WorldCallTests` 11개(평범한 값으로 돌려줌, 부르기가 아무것도 바꾸지 않음(시계·저장), 없는 이름의 제안, 쓰기·난수·커맨드·핸들러·반응 거절, 인자 수, 스크립트 오류와 예산 뒤에도 다음 호출, `CheckCall`).
- **확인:** 테스트 2019개 통과(커널 1976, CLI 36, 콘솔 호스트 7).

## 2026-10-19: UI 값 함수 부르기 2단계 영수증 `CallResult.Changed` (G246)

- **구현:** `World.Call`이 부르는 동안 `@when` 조건의 기록 장치(`_whenReads`·`_whenReadsSources`·`_whenReadsTick`, 앞의 값은 끝나면 되돌림)로 읽은 엔티티·`query` 출처·틱을 모은다. 결과를 만들 때 엔티티마다 지금 `Version(id)`, 출처마다 출처 도장을 적고, 틱을 읽었으면 그 틱을 적는다. `CallResult.Changed(world)`는 다른 월드면 참, 아니면 셋 가운데 하나라도 달라졌나. 출처 도장은 새로(`World.Stamps.cs`의 `_sourceStamps`·`SourceVersion`·`StampSource`): `TouchSource`가 when이 없어도 찍고, 커맨드 안에서는 처음 찍을 때 옛 값을 기록해 실패하면 되감는다(엔티티 도장과 같은 규칙).
- **테스트:** `WorldCallTests`에 6개(읽은 엔티티만·죽음, Ref를 따라간 엔티티, 목록의 생성·삭제·붙이기·떼기, 틱은 읽었을 때만, 실패한 커맨드(값·생성·붙이기)는 영수증을 그대로 둠, 불러온 월드는 늘 참). 출처 도장의 두 되감기 갈래를 하나씩 빼 보면 실패하는 것을 확인.
- **확인:** 테스트 2025개 통과(커널 1982, CLI 36, 콘솔 호스트 7).

## 2026-10-19: UI 값 함수 부르기 3단계 `ViewAs(연결).Call` (G248)

- **구현:** `WorldView.Call`·`CheckCall`. `World.CallAs`가 부르는 동안 `_viewer`(그 `WorldView`)를 걸어 두고, 스크립트가 읽는 길마다 `WorldView`와 같은 거르기를 한다: `Record`(못 보는 엔티티는 죽은 id, RUN002), `SiblingOrNone`(못 보는 컴포넌트는 None), 멤버 값(`WorldView.Filter`를 internal로: Ref는 `HiddenValue`, Ref 목록에서는 빠짐), 역방향 컬렉션(가리키는 쪽과 그 컴포넌트), `query`(`ViewQueried`), 엔티티 키 줄(목록에서 빠지고 꼬집으면 죽은 키), `contribs`(못 보는 출처의 행 빠짐), `controller`(None). 보기 정책(`MaySee`)과 `derived` 계산(G199) 안에서는 `_viewer`를 잠시 풀어 실제 상태를 읽는다. 정책이 읽은 곳은 그대로 영수증에 들고, 보는 쪽 영수증은 배정 표의 횟수(`SeatsVersion`, `Seat`·`Unseat`·죽을 때 지움, 커맨드 안이면 되감음)도 본다.
- **테스트:** 새 `WorldViewCallTests` 11개(query, 못 보는 컴포넌트, 역방향 컬렉션과 키 줄, 숨긴 Ref, 못 보는 인자, 보이는 derived, controller, contribs, 정책이 읽은 것(안개가 걷힘)과 처음 읽는 자리에 앉기의 영수증, 보는 쪽으로 불러도 월드는 그대로). 거르기 갈래 10개를 하나씩 빼 보면 각각 테스트가 실패하는 것을 확인. 처음의 배정 표 테스트는 앉힌 엔티티를 함수가 이미 읽어 엔티티 도장만으로도 통과했으므로, 읽은 적 없는 자리에 앉는 테스트로 바꿈.
- **확인:** 테스트 2036개 통과(커널 1993, CLI 36, 콘솔 호스트 7).

## 2026-10-19: UI 정의 파일은 호스트의 것, 콘솔의 `ui/console.txt` (G249·G250)

- **정한 것:** G249(사용자가 고름: UI 정의 파일은 호스트마다 제 형식이고 엔진은 읽지 않음, 엔진 공통 패널 목록은 두지 않음. 처음 추천한 두 층 나누기는 "모더는 호스트 하나를 목표로 한다"는 지적으로 거둠). G250(가: 콘솔은 모드 폴더의 `ui/console.txt`, 한 줄에 `모드.함수`).
- **구현:** 새 `ConsoleHost/ConsoleUi.cs`(`Load`: 로드 순서대로 모드 폴더의 파일을 읽어 `#` 주석·빈 줄을 빼고 `모드폴더/ui/console.txt:줄`을 붙임, `Check`: 줄마다 `CheckCall(함수, 1)`). `Play`가 시작 때 틀린 줄을 stderr에 보이고 1로 끝남. `ConsoleGame.WritePanels`: 자리마다 앉은 엔티티마다 `ViewAs(연결).Call(함수, 앉은 엔티티)`, 문자열은 그대로·목록은 한 줄씩·그 밖은 `ValueText`, 지난 결과의 `Changed`가 거짓이면 다시 부르지 않음, 실패한 칸은 오류 줄. 파일이 없으면 지금의 덤프(`WriteEntities`). 스커미시에 `ui/console.txt`와 `defs/view.py`(`status`·`board`·`units`, 내 유닛 대문자 `S`·`A`, 상대 소문자).
- **테스트:** 새 `ConsoleUiTests` 4개(값과 목록이 자리 아래 보임, 틀린 이름·커맨드는 줄 번호와 함께 시작에서 멈춤, 실패한 칸은 오류 줄이고 나머지는 그려짐, 파일이 없으면 덤프). `SkirmishTests`의 봇은 `units` 패널을 읽게 바꾸고, 도장 비교 테스트는 영수증 비교로 이름을 바꿈. `Changed`를 늘 거짓으로 바꿔 보면 4개가 실패하는 것을 확인.
- **본 것:** 순수 함수에 `str()`이 없어 숫자를 글에 넣을 수 없다(handoff 구멍 3의 후보).
- **확인:** 테스트 2040개 통과(커널 1993, CLI 36, 콘솔 호스트 11). `spec-gaps` 표 확인 `249 6`.

## 2026-10-19: 도장은 저장된 값의 것, `derived`는 영수증이 맡음 (G251)

- **정한 것:** 사용자가 고름(가). G235의 "아직 안 담는 것" 가운데 남의 엔티티를 읽는 `derived`와 거른 읽기는 도장에 담지 않고 `Call`·`ViewAs(…).Call` 영수증이 맡는다. G235는 `일부 바뀜(G251)`.
- **재현:** 임시 테스트로 진영의 `Power.total`(소속 유닛 hp 합)이 4 → 9로 바뀌어도 `Version(진영)`·`Version(진영, "Power")`는 그대로이고, 같은 값을 `world.Call`로 읽은 영수증은 `Changed`가 참인 것을 확인하고 지웠다. 콘솔 덤프에서도 옛 값(`Worth(total=1)`)이 남는 것을 테스트로 재현.
- **구현:** `ConsoleGame.WriteEntities`가 `derived` 칸이 있는 엔티티(`HasDerived`)는 도장을 보지 않고 매번 다시 읽는다.
- **테스트:** `ConsoleUiTests`의 테스트 모드에 동전(`Coin`, 역방향 `coins`)과 금고의 `Worth.total`을 더하고 1개(동전을 닦으면 덤프의 `Worth`가 바뀜). `WorldViewCallTests`에 1개(남의 엔티티를 읽는 derived는 영수증에 들고 도장에는 없음).
- **확인:** 테스트 2042개 통과(커널 1994, CLI 36, 콘솔 호스트 12). `spec-gaps` 표 확인 `250 6`.

## 2026-10-19: 모더 안내서 `docs/modding/` (G252)

- **정한 것:** 사용자가 고름(가: 저장소 안 마크다운 안내서, 레퍼런스는 스텁, 예시는 테스트가 도는 모드에서). 한 번에 다 쓰기.
- **조사:** 세 갈래(모드 구조·스키마·Def·패치 / 로직·커맨드·흐름·반응 / 자리·숨긴 정보·저장·화면)로 실제 문법과 오류 코드를 소스와 테스트에서 확인. 드러난 것: CLI 이름은 `engine`, `mod.py` 필드는 camelCase(`loadAfter`), Def 삭제 패치 없음, `match`·`try`는 PAR002(spec 72줄은 `match`를 허용으로 적음), `ask`의 선택지는 `choices=`만(spec 73줄의 위치 인자 아님), 틱 핸들러는 `@on(Tick)`(spec의 `on("tick")` 아님), `has()` 내장 없음(G248 본문이 언급), `World.cs:40` 주석은 사전 조건을 읽기 예산으로 적었으나 실제로는 쓰기 예산(G224), `World.Save.cs:14` 주석은 멈춘 흐름을 저장하지 못한다고 적었으나 저장됨.
- **예시 모드:** 새 `samples/guide`(`harbor`: 키 줄, `derived`와 기여 행, 추상 Def·상속·`inherit`, 비용과 `choose`, 난수, 신호·`@on`·단계·`@on(Tick)`, `@when(once=True)`, 마감 있는 `ask`와 `wait`, 보기 정책, 화면 함수와 `ui/console.txt` / `lighthouse`: `dependencies`, `patch.add`·`set`·`append`, 새 Def, `mod_loaded` 조건). `engine check`는 정보 `PAT002` 하나(조건 모드 없음)만.
- **문서:** `docs/modding/README.md`와 1~9장(시작하기, 데이터, 로직, 커맨드, 흐름과 시간, 반응, 자리와 숨긴 정보, 화면에 보일 값, 모드를 고칠 때와 아직 없는 것). 예시 코드는 두 샘플 모드에서 옮김.
- **테스트:** 새 `GuideSampleTests` 10개(로드 진단은 PAT002뿐, 상속과 패치 결과, 비용 거절, 입구, 침몰 신호와 보험, 기여 행, 용선 질문과 2틱 뒤 생선, 마감 거절, `@when`과 보기 정책으로 남의 계획은 "?", 콘솔 패널).
- **그 밖:** `.gitignore`에 샘플에서 `engine stubs`로 생기는 파일.
- **확인:** 테스트 2052개 통과(커널 1994, CLI 36, 콘솔 호스트 22). `spec-gaps` 표 확인 `251 6`.

## 2026-10-20: `match`/`case` (G253, 안내서에서 드러난 어긋남 첫 항목)

- **정한 것:** 처음에는 spec을 구현에 맞추는 쪽(미룸)을 추천했으나, 사용자가 "지금 하면 안 되는 이유가 있나"를 물어 다시 따져 봄. 막는 것은 없고, 파서에서 `if`로 바꿔 넣으면 비용이 작아 추천을 바꿈. 사용자 동의. 패턴은 리터럴·세로줄·`_`·가드만, 이름 캡처는 파이썬의 함정이라 `PAR002`와 가드 안내.
- **구현:** `Parser.ParseMatch`·`ParsePattern`·`ParseLiteralPattern`(`Parser.Compound.cs`). `$m번호 = 대상` 대입과 `IfStmt`(세로줄은 `or`, 가드는 `and`, 가드 없는 `_`는 `else`, `_`만 있으면 `if True`). 숨은 변수 번호는 함수마다 0부터(`_matchCount`, 본문 해시가 다른 함수에 흔들리지 않게). 최상위는 `EVL002`. 오류 뒤에는 match 블록 끝까지 건너뜀. "혹시 …?" 후보에서 `$` 이름을 뺌(`FunctionNameChecker`, `Session.LookupFree`). 검사기·실행기·저장은 손대지 않음.
- **예시 게임:** 스커미시 차례 루프의 `if r.response == …/elif`를 `match`로(`case "Attack" if …:`), 안내서 5장도 같이. 3장에 `match` 절, 9장 표 고침, spine-spec 72줄에 받는 패턴을 적음.
- **테스트:** `ParserTests`(바꿔 넣은 모양, 함수마다 번호, 미지원 패턴 9가지는 `PAR002`, 구문 오류 4가지, 캡처 패턴 안내, 오류 뒤 이어 읽기, 최상위), 새 `MatchTests` 7개(리터럴·세로줄·음수·`fixed`·`True`·`None`·`_`, 타입이 다르면 안 맞음, 맞는 것 없음, 가드, `break`, 중첩), 새 `WorldMatchTests`(답으로 가지 고르기 4가지, `case` 안의 `ask`에서 저장·불러오기 C1·C4, 대상은 한 번만 평가, 최상위 로드 오류, 가드 안 이름 오타와 숨은 이름 안 보임).
- **확인:** 테스트 2084개 통과(커널 2026, CLI 36, 콘솔 호스트 22). `spec-gaps` 표 확인 `252 6`.

## 2026-10-20: 안내서에서 드러난 어긋남 고치기 (나머지)

이미 정한 결정에 문서와 주석을 맞춘 것이라 새 G 없이 진행(커밋 `aee9d62`·`3f795b9`·`a0d19b6`과 이 커밋).

- **spine-spec S2 73줄:** `ask(party, "Title_Key", ["a", "b"])` → `choices=["a", "b"]`(G138, 구현과 스텁은 `/, *`로 이름 인자만). 멈추는 호출 목록의 `await`(엔진에 없음)를 `ask`·`wait`·`advance`로.
- **spine-spec S5·X2:** `on("Name")`·`emit("Name", ...)`·`@on("tick", ...)`·`emit("turn_start")`·`emit("tick")` → `@on(Name)`·`emit(Name(...))`·`@on(Tick, ...)`·엔진 신호 `Tick`(G145·G152·G154·G156).
- **G248 본문:** 없는 내장 `has(u, "Life")` → `u.Life is None`. 결정이 바뀐 것이 아니라 표기 오류라 `바뀜`이 아닌 `**고침(2026-10-20):**` 줄을 붙임. 못 보는 컴포넌트가 `None`으로 읽히는 것은 `WorldViewCallTests`의 `p.Hand is None`으로 확인.
- **주석:** `World.cs`의 `DefaultStepBudget`·`DefaultActionStepBudget`(사전 조건은 행동 예산, `World.Commands.cs`의 `StepMeter(_actionStepBudget)`로 확인), `World.Save.cs` 머리(멈춘 Flow도 저장).
- **확인:** 테스트 2084개 통과.

## 2026-10-20: Def id 이름 공간 구현 (G258~G266)

결정(G258~G265)을 마친 뒤 사용자가 구현을 고름. 네 단계로 나눠 커밋(`dce42fb`·`5d82c14`·`c0d5d27`과 이 커밋). 이름 공간을 쓰는 결정 G266(`defid`라는 이름)은 2단계 앞에서 사용자와 정함(처음 추천은 `def_id`였으나 사용자가 `defid`를 물어 다시 따짐: 파이썬 내장도 붙여 쓰고 지역 변수 `def_id`와 부딪히지 않음).

- **1단계 로더·실행 중 풀이:** 새 `Loading/DefIds.cs`(전체 이름 풀이, 글자 검사 `DEF005`, 같은 경로를 먼저 권하는 힌트, 타입 자리 값 정규화). 새 Def는 자기 모드 것, `override=True`는 전체 이름만. 다른 모드와 id가 겹치는 경우가 없어져 등록(`ContentLoader.Build.Register`)의 "서로 의존하지 않는 두 모드의 같은 id" 분기를 지움. ②b 생성자·③ 패치(대상, 경로 안 Def 키, 값)·④ 상속·스키마/커맨드/호스트 기본값에서 `DefId` 값을 그 코드의 모드 기준 전체 이름으로. 실행 중은 `PureEvaluator.ScriptMod`(지금 도는 세션의 맨 위 함수의 모드)로 `spawn`·칸 쓰기·줄 키를 풀고, 호스트가 부르면 그대로라 전체 이름이어야 함(G264). 속성 검사의 `spawn` 리터럴도 모드 기준. 스텁 `Literal`은 내 모드 짧은 이름 먼저, 보이는 전체 이름 뒤(G265), 줄 속성(`u.Skill.Cooking`)은 내 모드의 식별자 이름만. 예시 lighthouse는 harbor의 Def를 전체 이름으로. 콘솔 호스트는 전체 이름을 보이고 받음(`#1 banking:Vault`, `--seat red=skirmish:Red`, 짧게 받는 것은 호스트가 고를 일). harbor의 `Buy(kind: str)`는 `DefId`가 아니라 짧은 이름 그대로 받음.
- **2단계 `defid`·`choose`·저장:** `defid("Sloop")`(로직은 `CallerBody`로 부른 모드 기준, 없는 Def는 실행 오류와 힌트, Def 파일 최상위에서도 씀). 리터럴은 `ModFunctions.DefIdLiterals`로 모아 ⑤ 링크(보이는 구체 Def, 타입 무관). 임시 테스트로 `DefId` 매개변수의 `choose(k, from_=["Sloop"])`가 풀리지 않아 맞는 값이 거부되는 것을 재현하고 고침(`PureEvaluator.ParameterType` 고리로 제출 검사, `World.Choices`의 후보 나열 모두). 저장 파일의 짧은 id는 풀지 않음을 테스트로 고정.
- **3단계 경고:** `LNK005`. 속성 검사의 정적 타입에 `DefIdStatic`·`DefIdMapType`(DefId 키 맵)·`GroupType(DefIdStatic)`(DefId 리스트)을 더함(필드·const·커맨드 매개변수·함수 어노테이션). `==`·`!=`·`in`·`not in`·맵의 `["X"]`·돈 원소의 비교에서 `:` 없는 리터럴이면 경고. `DefId` 매개변수의 `choose(from_=[...])` 리터럴 오타는 `LNK001`.
- **4단계 문서:** 안내서 2장에 "Def id의 이름 공간" 절, override·패치 예시·경로의 다른 모드 키(`Stock['harbor:Fish']`), 1장(LNK005, 콘솔 예시와 `patches.py` 줄 번호), 8장 콘솔 출력. spine-spec S1에 이름 공간 항목, 스텁 설명, 예시 `cost['core:Wood']`·`spawn("Warrior")`.
- **테스트:** 기존 테스트의 id 기대값을 전체 이름으로(약 90개 파일), 등록 테스트는 새 전제로(같은 이름 다른 모드는 다른 Def, override 없이 남의 전체 이름은 `DEF005`). 새 `DefIdBuiltinTests` 9개, `BareDefIdWarningTests` 4개, `StubGeneratorTests`(G265 모양), `WorldSaveTests` 1개.
- **확인:** 테스트 2099개 통과(커널 2041, CLI 36, 콘솔 호스트 22). `engine check samples/guide`·`samples/skirmish` 경고 없음. `spec-gaps` 표 확인 `265 6`.

## 2026-10-20: X4 번역 설계 마무리와 질문의 선택지 (G267~G273)

- **번역 인자 값 표(G267):** Linguini 0.11.0 임시 실험(저장소에 남기지 않음)으로 직접 만든 `IFluentType`이 자리 표시자에 `AsString()`으로 나오고 사용자 함수에 타입 그대로 가는 것, 그 값으로는 `select`가 안 되는 것(`Matches`가 불리지 않음), `NUMBER()`가 기본으로 없고 수가 불변 문화권 표기인 것을 확인. 실행 중 Def id가 문자열이라 "불린 Def의 전체 이름과 같은 문자열은 Def 이름"(사용자가 고름), 엔티티는 `text()` 때 Def를 적어 둠(`kesa-gone` 없음).
- **조사(G268):** 번역문의 `JOSA($x, "을")`, Def 속성으로 고르기 `DEFATTR`. 뒤처리 표시 `(을)를`은 안 씀.
- **호스트 API(G269):** `Translations.Load`(불변) → `For(언어 목록)`의 `Translator`, 다시 읽기는 `Load`를 다시 불러 갈아 끼움(사용자 질문 "핫 리로드가 되나?"에서).
- **선택지 이야기(G270~G273):** Def의 `.desc`를 어떻게 띄우나에서 사용자가 "호스트가 k.title·k.desc를 배치"를 내어 제목·선택지 자리의 `Label`·`Attr`로(G270, 잠정), 선택지의 보이는 모양("보라색으로 불탐")은 모드가 `option(…, style=)`으로(G271), "응답 커맨드(내용: 예)"는 호스트가 보는 목록과 내는 함수만 `kesa.pick(choice=번호)`로 합침(G272, 커널의 `Answer`는 그대로, 번호인 것은 숨김 값 때문). 모드 id `kesa` 예약은 확인 중 드러남(G273, 잠정). advisor 검토로 사용자가 답하지 않은 G270을 잠정으로 고침.
- **커밋:** `f8fba32`, `7c9eec1`, `297dc21`, `cea3c56`과 이 정리. 문서만 바뀜, 테스트 2099개 통과.

## 2026-10-20: X4 커널 쪽 구현 (G271~G273, G267의 인자)

번역 어셈블리 앞에 커널이 할 몫을 네 커밋으로 나눔(`5575259`·`e9489b0`·`13ef1af`와 이 커밋).

- **모드 id `engine` 예약(G273):** `ManifestReader.ReservedId`, 쓰면 `MOD005`.
- **`option`(G271):** 새 `OptionValue`. 질문을 열 때 맨 값도 감싸 `Request.Choices`는 늘 option 목록, 답은 안의 값. 저장은 꾸밈 없는 선택지는 값만(앞의 모양 그대로), 꾸밈은 `{"option","label","desc","style","data"}`. 거른 읽기가 칸 안의 엔티티까지 가림. `data=`에 option은 실행 오류. 스텁에 `option`과 `Option`. `Session.CopyStack`의 `Remap`에 `OptionValue`를 더함(C1: `SaveScriptValue`와 같은 종류를 다뤄야 함). 테스트 `WorldAskOptionTests`.
- **`engine.pick`(G272):** `LegalResponses`가 물은 엔티티마다 선택지 순서로 `engine.pick(by=, choice=번호)`를 먼저 내고 `max`에 셈. `Respond`가 `Answer`로 보냄. 번호인 까닭은 숨긴 엔티티가 커맨드 인자가 될 수 없어서. 콘솔 호스트는 이 한 길만 써 같은 선택지가 두 번 보이던 것을 임시 테스트로 재현하고 고침. 테스트 `PickMoveTests`, `SkirmishTests.EachChoiceIsListedOnceAsAPickMove`.
- **`text()` 인자(G267):** `PureBuiltins.MakeText`가 수·str·bool·None·text만 바로 받고 나머지는 월드의 `TextArgument`에 물어 엔티티면 부른 때의 Def를 `TextValue.EntityDefs`에 적음(같음에는 들지 않음). 그 밖(list·dict·함수)은 `RUN015`와 힌트. 저장은 적은 것이 있을 때만 셋째 칸 `"defs"`, 불러올 때 없는 인자를 가리키면 거부. 거른 읽기는 숨긴 엔티티의 Def를 뺌(Def가 정체를 알려 주므로). 람다를 `text()`에 넣던 기존 테스트 두 곳은 엔티티 인자와 `option(data=)` 안의 람다로 바꿈. 테스트 `TextArgumentTests`.
- **확인:** 테스트 2129개 통과(커널 2070, CLI 36, 콘솔 호스트 23).

## 2026-10-20: 번역 어셈블리 1단계 (G254~G257·G267·G269·G270 구현)

이미 정한 결정의 구현이라 새 G 없음. 두 단계로 나눈 앞쪽.

- **프로젝트:** `src/Kesa.Engine.Text`(netstandard2.1, C# 9, Unity 호스트도 쓰게 커널과 같게), `Linguini.Bundle` `[0.11.0]` 고정, `Determinism.props` 안 켬. 테스트 `tests/Kesa.Engine.Text.Tests`. 둘 다 `Kesa.Engine.slnx`에 넣음.
- **커널 쪽 배관:** `ContentLoadResult.Order`(G256의 "순서 없는 두 모드" 판정에 `LoadOrder.IsOrdered`가 필요한데 결과에 없었음), `ContentLoadResult.ModsRoot`(`ModManifest.Directory`가 모드 폴더 기준 상대 경로라서, 임시 테스트로 파일을 못 찾는 것을 보고 더함). `Loading/LanguageTag`(모양만 보는 BCP 47 검사, 5~8자 첫 조각도 규칙상 맞음), `defaultLanguage`가 태그가 아니면 `MOD002`(G256에 정했으나 구현이 없었음). 진단 `LOC001`(구문)·`LOC002`(한 곳 안의 같은 키)·`LOC003`(순서 없는 모드의 같은 키, 경고)·`LOC004`(폴더 이름)·`LOC005`(`defaultLanguage`·그 폴더 없음).
- **모으기:** 파일마다 `LinguiniParser`로 직접 읽어 Junk만 빼고, 언어 → 키 → 이긴 글 표를 우리가 만든 뒤 언어마다 한 `Resource`로 번들에 넣음(Linguini의 글 하나 덮어쓰기는 공개가 아님). 구문 오류의 줄은 `Row`(파서가 멈춘 다음 글의 줄)가 아니라 빠진 글 `Slice`의 시작 줄(임시 테스트로 확인). 키의 주인 = 처음 정의한 곳, 그 기본 언어가 폴백 두 번째. 엔진 글 `engine-hidden`(en·ko)이 맨 앞, 호스트 폴더가 다음. 번들은 `SetUseIsolating(false)`(자리 표시자에 U+2068·U+2069를 안 붙임).
- **번역기:** `Format`·`Text`·`Label`·`Attr`·`Value`(`WorldView`나 `World`를 받음), 늘 문자열, 오류는 `For(…, onError)`로. 빠진 인자 같은 오류에도 Fluent가 채운 글을 씀. 인자 표 G267: 불린 Def의 전체 이름 문자열·엔티티(적어 둔 Def)는 Def 이름, 숨김은 `engine-hidden`, 안쪽 `text()`는 먼저 번역, bool·None은 `true`·`false`·`none`. 수는 `FluentNumber`가 double을 거쳐 `0.000001`을 `1E-06.000000`, 큰 int를 지수 표기로 내는 것을 임시 테스트로 재현해 `ExactNumber`(FluentNumber를 상속한 record, `IFluentType`을 다시 구현해 커널의 십진 표기를 냄, 복수형 select는 그대로 됨)로 고침.
- **남은 것(2단계):** Def 이름 값을 문자열이 아닌 직접 만든 Fluent 값으로 넘겨 `JOSA`·`DEFATTR`가 Def를 알게 하기.
- **확인:** 테스트 2153개 통과(커널 2077, 번역 17, CLI 36, 콘솔 호스트 23).

## 2026-10-20: 번역 어셈블리 2단계 (G268 구현)

- **Def 이름 값:** 번역 인자 자리에서 불린 Def의 전체 이름 문자열과 엔티티(적어 둔 Def)는 `DefName`(직접 만든 `IFluentType`)으로 간다. 자리 표시자에는 번역된 이름이 나오고, 사용자 함수는 Def id와 그 Def의 속성을 같은 언어 목록으로 찾는 길을 안다.
- **`JOSA`:** `Josa.cs`. 짝 열 개, 짝 중 아무 쪽, ㄹ 받침 뒤 "로", 끝이 숫자면 읽는 소리(끝자리가 0이 아니면 그 숫자, 0으로 끝나면 십·백·천·만·억·조·경, 소수점 뒤는 숫자 하나씩), 받침을 알 수 없으면 `을(를)`, 모르는 조사는 적힌 그대로 붙임(`check` 오류는 다음 항목).
- **`DEFATTR`:** Def가 아니거나 속성이 없으면 빈 문자열이라 `select`가 기본 가지로 가고, 번역 오류로 알리지 않는다.
- 두 함수는 언어마다 번들에 넣는다(`TextFunctions`).
- **확인:** 테스트 2178개 통과(커널 2077, 번역 42, CLI 36, 콘솔 호스트 23).

## 2026-10-20: check의 번역 검사 (G257·G262·G268·G273 구현)

- **커널:** 함수 본문 이름 검사가 덮어쓰지 않은 `text("리터럴", 이름=…)`을 `TextUse`(키, 넘긴 이름, 키의 위치)로 모으고 `ContentLoadResult.TextUses`(모드 id → 호출)에 둔다. 키를 계산한 호출과 Def 파일 최상위의 호출은 모으지 않는다.
- **`TranslationCheck.Run`:** 모드의 글만 본다(엔진·호스트 글은 모더가 고칠 수 없음).
  - 오류: `JOSA`의 모르는 조사 `LOC006`(힌트에 아는 조사 목록), 불린 Def의 것이 아닌 `def-` 키와 엔진·호스트가 주지 않은 `engine-` 키 `LOC007`(엔진의 `engine-hidden`은 모드가 덮어써도 됨).
  - 경고: 어느 언어에도 없는 `text("키")` `LOC008`, 번역문이 쓰는 변수를 넘기지 않음 `LOC009`(언어마다, `키.속성`도), 주인의 기본 언어 글에 없는 변수를 다른 언어가 씀 `LOC010`.
  - 정보: 모드·언어마다 빠진 글 수 `LOC011`(엔진 글만 있는 언어는 셈하지 않음).
  - `Translations`가 모은 표(언어 → 키 → 글·정의한 곳·줄), 키의 주인 모드, 엔진·호스트가 준 키를 검사용으로 남긴다.
- **CLI:** `engine check`가 번역 파일을 읽고 검사한다(`dump`·`stubs`는 안 함). CLI가 `Kesa.Engine.Text`를 참조.
- **샘플:** 새 검사로 `LOC008` 경고 세 개가 나와 `harbor`·`skirmish`에 `defaultLanguage="en"`과 en·ko 번역 파일(`text()` 키만)을 더함. 선택지·제목 키 바꾸기는 콘솔 연결 때.
- **확인:** 테스트 2185개 통과(커널 2077, 번역 48, CLI 37, 콘솔 호스트 23). `engine check samples/guide`·`samples/skirmish` 경고 없음.

## 2026-10-20: 콘솔 호스트 번역 연결 (G274·G275)

- **결정(확인):** G274 콘솔은 `--lang ko,en`으로 언어를 받고 없으면 `CurrentUICulture.Name` 뒤에 `en`, 태그가 아니면 사용법 오류(2), 콘솔 자신의 글은 실행 파일 옆 `locale/`의 `engine-console-…`. G275 번역 없는 Def 이름은 키가 아니라 Def id(G257 일부 바뀜).
- **콘솔:** `ConsoleGame`이 `Translator`를 받아 모든 글을 `engine-console-…` 키로 찍고(ko는 옛 하드코딩 글과 같음), 질문 제목·선택지는 `Label`, `require` 실패는 `Message`, 엔티티는 `#번호 번역된 이름`(없어진 엔티티는 `(gone)` 글). 리스트·사전은 콘솔이 늘어놓고 맨 값만 `Value`. 읽기 진단은 정보를 빼고 stderr, 그리는 중의 번역 오류는 같은 것을 한 번만 `translation: …`.
- **예시 모드:** 키를 G255 관례로(`harbor-charter-accept`·`-decline`, `skirmish-hit`·`-hit-hold`·`-hit-retreat`·`skirmish-turn`, `harbor-good-fish`·`-salt`), en·ko 번역에 Def 이름(`def-harbor-Gull` 등, 배는 `.desc`)을 더함. `lighthouse`는 일부러 번역이 없다.
- **테스트:** 콘솔 테스트는 `--lang ko`로 돌리고 번역된 글을 기대함, `TheLanguageComesFromLang`(fr,en → 영어, 태그 오류는 2), `AnUntranslatedDefShowsItsId`.
- **안내서:** 8장 "번역" 절(파일 자리, 키, Def 이름, `engine-` 키, 고르는 순서, `JOSA`·`DEFATTR`, 검사), 1·2·3·5장의 "번역은 아직 없음"과 예시 키, 9장 표는 "커맨드 이름의 번역"으로 바꿈.
- **남은 것:** 수 목록의 커맨드 이름은 번역하지 않는다.
- **확인:** 테스트 2187개 통과(커널 2077, 번역 49, CLI 37, 콘솔 호스트 24).

## 2026-10-20~21: S10 UI의 행동 (G276~G286)

- **결정(모두 확인):**
  - G276: 화면만 바꾸는 동작은 모드 스크립트의 `@ui_action`이 쓴다.
  - G277: 저장소는 스키마의 `ui_state` 선언이고 연결마다 기록 하나.
  - G278: 본문은 보는 세계로 읽기만, 쓰기는 기록만, `submit`은 적어 두기만.
  - G279: 표기는 컴포넌트와 같고 모든 칸에 기본값, Ref는 optional, 호스트가 `UiState`를 든다.
  - G280: 사전 조건이 있고, 호스트 API는 커맨드의 것을 따른다(`Act`, `CheckCommand`).
  - G281: 이름 공간은 타입·컴포넌트·신호와 하나.
  - G282: UI를 쓰는 도우미는 문장으로만 부르고, "UI를 건드림"은 호출을 따라 퍼진다.
  - G283: 응답 커맨드의 `submit`은 엔진이 짝지을 질문을 찾는다.
  - G284: 기록의 리스트·사전은 읽을 때 죽은·숨긴 엔티티를 뺀다.
  - G285: 콘솔은 표기를 늘리지 않고 `[화면]` 메뉴에 섞어 보인다.
  - G286: 콘솔의 약속으로, `me` 매개변수에 앉은 엔티티를 넣는다.
- **1단계 로드:**
  - `UiStateDecl`·`SchemaSet.UiStates`. 리스트·사전 칸은 `ui_state`만 쓴다.
  - `CommandDecl.IsUiAction`과 `ContentLoadResult.UiActions`. 사전 조건은 쓰고 `cost`는 오류.
  - `FunctionEffects`가 `UiTouchWitness`·`UiWriteWitness`를 고정점으로 채운다(`CMD007`). `ui.기록.칸` 이름은 `UiNames`가 본다.
  - 리터럴 `submit`은 `Linker.CheckSubmit`(`CMD008`)이 본다.
  - 스텁에 `submit`과 늘 있는 `ui`를 둔다.
- **2단계 실행:**
  - `World.NewUiState()`, `WorldView.Act(ui, 이름, 인자)` → `ActResult`. 사본에 쓰고 성공해야 반영한다.
  - `World.Deliver(issuer, UiSubmission)` → `DeliveryResult`.
  - 프레임의 `UiWritable`은 G117의 `Writable`처럼 문장 호출을 따라간다.
  - UI 동작 밖의 `ui`·`submit`은 실행 오류다(`derived`·보기 정책은 `OutsideAct`).
  - 인자 검사도 보는 쪽으로 해서 숨긴 엔티티는 죽은 것처럼 거절한다. 이것은 advisor가 짚은 누출로, 임시 테스트로 재현한 뒤 고쳤다.
  - UI 칸의 리스트 `+=`는 조용히 무시되던 것을 재현해 오류로 바꿨다.
  - `Show(ActResult)`·`Show(DeliveryResult)`.
- **2b 나열:**
  - `WorldView.CheckPreconditions(ui, …)`·`Candidates(ui, …)`·`LegalMoves(ui, …)`: 버릴 사본으로 도는 `Peek`. 커맨드 쪽 `Candidates`·`LegalMoves`를 선언 단위로 나눠 함께 쓴다.
  - `CheckCommand`·`CheckUiAction`.
- **3단계 화면 함수:**
  - `WorldView.Call(ui, 이름, 인자…)`. 보통 `Call`은 `ui`를 읽는 함수를 이 꼴을 알려 주며 거절하고, 쓰는 함수는 늘 거절한다.
  - `CheckCall(…, withUi: true)`.
  - `ui`를 읽은 영수증은 `UiState.Version`을 보고, 기록이 가리키는 엔티티도 읽은 곳으로 든다.
- **4단계 콘솔·샘플:**
  - 콘솔은 자리마다 `UiState`를 든다. UI 동작의 수는 모든 자리의 커맨드 뒤에 둔다. 처음에 자리마다 붙였더니 봇 테스트의 번호가 밀려서 옮겼다.
  - 고르면 `Act` 뒤 `Deliver`한다.
  - 스커미시에 `Selection` 기록, `defs/ui.py`의 `select`·`strike`, 화면 함수 `selection`을 더했다.
- **안내서:** 8장 "버튼" 절을 "화면 동작"으로 다시 썼다(`ui_state`, `@ui_action`, `submit`, 도우미 규칙, 화면 함수, 콘솔). 9장 표도 고쳤다.
- **미룸:**
  - `@ui_action` 덮어쓰기·패치.
  - `submit(..., request=…)`.
  - `PatchOperationCheck`가 패치로 `ui`를 건드리게 된 함수를 알리는 것.
- **확인:** 테스트 2245개 통과(커널 2134, 번역 49, CLI 37, 콘솔 호스트 25). `engine check samples/skirmish` 경고 없음.

## 2026-10-21: 커맨드 이름 번역은 하지 않음(G287), 붙기 전의 컴포넌트 값 읽기(G288)

- **G287(결정만):** 사용자가 "커맨드는 버튼 뒤에 감춰져 번역이 필요 없지 않나"라고 짚어, 엔진은 커맨드·`@ui_action` 이름의 번역 키를 두지 않기로 했다. 버튼 글은 호스트 UI 파일의 일이다. 안내서 9장 표에서 그 줄을 빼고 8장 콘솔 절에 까닭을 적었다.
- **G288 재현:** 임시 테스트에서 `Moves(left=3).left`는 `'Moves(...)' has no attribute 'left'`(힌트 없음)였다. `Unit("Scout", Moves=Moves(max=8))`에 `Moves(left=3)`을 넣어 생성하면 left=3, max=8이었다. 안 준 칸을 스키마 기본값으로 읽게 하면 틀린 값이 나온다는 것을 이 결과로 보였다.
- **구현:** `World.Structure.cs`의 `ComponentInitValue`가 `IRuntimeAttributes`·`IRuntimeAssignable`을 구현한다. 값을 엔티티 보기로 돌리려고 월드를 들고 있다.
  - 준 칸은 그 값을 준다.
  - 없는 이름은 "혹시 …?" 오류, `derived` 칸은 엔티티에서 계산된다는 오류다.
  - 안 준 칸은 "was not given"과 G288 힌트를 붙인 오류다.
  - 쓰기는 "cannot be changed"와 새 값 만들기 힌트를 붙인 오류다.
  - 로드 때의 효과 검사는 `m.left = 4`를 여전히 게임 쓰기로 본다. 실행하면 오류가 나므로 그대로 두었다.
- **테스트·안내서:** 테스트 `ComponentValueReadTests`. 안내서 4장에 컴포넌트 값 문단을 더했다.
- **확인:** 테스트 2249개 통과(커널 2138, 번역 49, CLI 37, 콘솔 호스트 25).

## 2026-10-21: "혹시 …?" 힌트가 빠지는 두 경우 (남은 일 1번)

새 G 없음. spec S9 힌트를 바르게 구현하는 수정이다.

- **재현(임시 테스트):**
  - `Suggest.Hint("lfet", ["left", "max"])`가 null이었다. 로드 검사에서 `LIMTI`(전역 `LIMIT`)도 힌트가 없었다. Levenshtein으로는 맞바꾸기가 2인데 짧은 이름의 상한은 1이라서다.
  - 전역 오타 `helpr`는 로드 검사(G108)가 `helper` 힌트로 잡았다. 실행 중 길은 로더 없이 실행기를 직접 돌릴 때만 닿는다.
- **고침:**
  - `Suggest.Distance`를 제한된 Damerau(OSA) 거리로 바꿨다. 이웃한 두 글자 맞바꾸기가 한 번이다. 로드·실행·Def id 후보가 모두 이 함수를 쓴다.
  - `IScriptGlobals.Names(mod)`를 더했다(`ScopeGlobals`, `WorldGlobals`는 모드 이름과 보이는 컴포넌트·신호 생성자). `Session.LookupFree`가 이것을 후보에 넣는다.
- **테스트:** `SuggestTests`(거리 표, 맞바꾼 오타의 힌트, 실행 중 전역 후보). 기존 테스트는 바뀐 것 없이 통과했다.
- **확인:** 테스트 2257개 통과(커널 2146, 번역 49, CLI 37, 콘솔 호스트 25).

## 2026-10-21: CLI 종료 코드 3 경로 테스트 (남은 일 2번)

- 모드 내용으로는 엔진 예외에 닿으면 안 되므로(닿으면 그것이 버그), `Commands.LoadAndReport`를 internal로 열고 보고 콜백에서 예외를 던져 시험한다.
- 확인하는 것: 종료 코드 3, stderr 첫 줄 `engine check: internal error (this is an engine bug, please report it)`, 예외 내용, stdout은 비어 있음.
- **확인:** 테스트 2258개 통과(커널 2146, 번역 49, CLI 38, 콘솔 호스트 25).

## 2026-10-21: 파일 정리의 작은 나머지 (남은 일 4번)

동작은 바뀌지 않는다. 파일 배치 규칙(CLAUDE.md, 2026-10-18)을 마저 맞췄다.

- **제 파일로 옮긴 타입 아홉** (문서 주석과 함께 잘라 옮기는 스크립트로 했다):
  - `LoadOrder`(←`ModSorter.cs`), `DiscoveryResult`(←`ModDiscovery.cs`)
  - `ChooseInfo`(←`ChooseDependencies.cs`), `ModFunctions`(←`FunctionNameChecker.cs`)
  - `ModEnvironment`(←`DeferredEvaluator.cs`), `HostReader`(←`HostAccess.cs`), `HostSignature`(←`HostSignatures.cs`)
  - `GenericType`(←`SchemaBuiltins.cs`), `FrozenDef`(←`DefSet.cs`)
  - `Frame.cs`의 `ResultTo`는 `Frame`의 칸이 쓰는 작은 열거형이라 그대로 둔다. `Ast.cs`·`SchemaModel.cs`·`Values.cs` 같은 모델 묶음도 규칙대로 그대로 둔다.
- **쓰지 않는 using:**
  - `dotnet format style --diagnostics IDE0005`는 이 저장소에서 아무것도 찾지 못했다. 일부러 넣은 using으로 확인했다.
  - 이렇게 하면 찾는다: 저장소 루트에 임시 `.editorconfig`(`[*.cs]` `dotnet_diagnostic.IDE0005.severity = warning`)를 두고 `dotnet build Kesa.Engine.slnx --no-incremental -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true "-p:NoWarn=CS1591%3BCS1573%3BCS1572%3BCS1574%3BCS1587%3BRS2008"`로 빌드한다. 문서 주석 경고와 분석기 릴리스 추적(RS2008)은 이 검사에만 끈다. 끝나면 `.editorconfig`를 지운다.
  - IDE0005는 붙어 있는 쓰지 않는 using 묶음을 첫 줄 하나로 알린다. 그래서 줄을 지우고 다시 검사하기를 0이 될 때까지 되풀이했다.
  - 모두 `src/Kesa.Engine.Kernel`에 있었다. using이 모두 빠진 두 파일은 앞의 빈 줄도 지웠다.
- **확인:** 일반 빌드 경고·오류 0. 테스트 2258개 통과(커널 2146, 번역 49, CLI 38, 콘솔 호스트 25).

## 2026-10-21: 단계 순서 덤프 `engine stages` (남은 일 3번, G289)

- **명령:** `engine stages <mods-dir> [--host <file>]`. `check`와 같은 로드를 한다(`LoadAndReport`). 순서 텍스트는 stdout, 진단과 요약 줄은 stderr로 낸다. 종료 코드는 G78을 따른다.
- **출력:** `Reporting/StageReport.ToText`가 만든다. 세 절이다.
  - 단계 순서와 동률 규칙. 단계마다 그 단계를 뒤에 오게 한 간선을 위치와 함께 보인다. 제 `after`·`optional_after`(대상이 있을 때만)와 남의 `before`·`optional_before`(`(before= of X)`로 표시)다.
  - 신호별 핸들러. 신호는 이름 순이고, 핸들러는 단계 순서로 묶는다. 순서를 얻지 못한 단계는 `(not ordered)`, 단계 없는 것은 `(no stage)`로 맨 뒤다. 묶음 안에서는 로드·정의 순서다.
  - `@when` 반응. 같은 묶기를 쓴다.
- `CheckReport.Location`을 internal로 열어 위치 표기를 같게 했다.
- **안내서:** 1장의 `dump` 옆에 `stages` 한 줄을 넣었다. 6장 단계 절에 무엇을 보이는지와 순서 규칙을 적었다.
- **테스트:** `CommandsTests.StagesPrintsTheOrderTheHandlersRunIn`. 두 모드, 단계 셋(`after`, 남의 `before`, 없는 단계를 가리킨 `optional_after`)과 핸들러 넷, `@when` 하나로 stdout 전체를 맞춘다. `samples/guide`에서도 손으로 돌려 보았다(`harbor.market`, `Sunk`→`harbor.insurance`, `Tick`→`(no stage)`).
- **확인:** 테스트 2259개 통과(커널 2146, 번역 49, CLI 39, 콘솔 호스트 25).

## 2026-10-21: 영수증을 (엔티티, 컴포넌트) 단위로 (G290)

- **재현:** 임시 테스트에서 `u.Life.hp`만 읽은 영수증이 같은 유닛의 `Owner`를 바꾸면 `Changed`가 참이었다(엔티티 단위, G246).
- **바꾼 것:** 쓰기는 이미 (엔티티, 칸) 도장을 찍고 있어서 읽는 쪽만 고쳤다.
  - `World.Call.cs`에 `CallRead`(id, 칸 이름)와 `_callReads`를 두었다. 호스트가 부르는 동안만 모으고, 평소에는 null이라 비용이 없다.
  - `Record(id, 이름)`과 `Read(id, 이름)`은 when 쪽(`_whenReads`, 엔티티 단위)과 영수증 쪽에 함께 적는다.
  - `CallResult`는 칸마다 `ReadVersion(id, 이름)`을 견준다. 이름이 null이면 엔티티 전체, `""`이면 살았는지만(만들어진 때 도장), 그 밖에는 그 칸의 도장이다.
  - 이름을 붙인 길:
    - 멤버 읽기·형제 컴포넌트(`ComponentAttribute`, `EntityAttribute`)
    - 키 컴포넌트(`RowOrNone`·`HasRowKey`·`RowKeys`·`RowInts`·`RowEntities`·`GetRow`·`HasRow`)
    - `Get`, `Has`, `contribs()`
    - 역방향(`Inverse`가 그 inverse 이름으로)
  - 살았는지만 보는 길: `DefOf`, `DefConst`, Ref 대상, UI 기록의 `Gone`.
  - 배정은 `Seat`·`Unseat`가 `"@seat"` 칸에 도장을 찍고, `controller()`가 그것을 읽는다.
- **테스트:** `CallReceiptNarrowingTests`.
  - 읽지 않은 컴포넌트에는 거짓이다.
  - Ref 대상은 죽을 때만 참이다.
  - 아래 읽기는 여전히 참이 된다:
    - 따라간 엔티티
    - 붙이기·떼기
    - 줄 추가·쓰기
    - 역방향
    - 다른 엔티티를 읽는 `derived`
    - 기여 행
    - 배정
  - 쓸어 보기: 쓰기 열일곱 가지를 하나씩 한다. "안 바뀜"이라고 답한 영수증은 다시 불러도 값이 같아야 한다. 역방향 읽기를 일부러 "살았는지만"으로 바꾸면 이 테스트가 실패함을 확인하고 되돌렸다.
- **남은 것:** `@when` 읽기 기록은 아직 엔티티 단위다(handoff).
- **확인:** 테스트 2263개 통과(커널 2150, 번역 49, CLI 39, 콘솔 호스트 25).

## `@when` 읽기 기록을 (엔티티, 컴포넌트) 단위로 (2026-10-21, G290 확인)

G290에서 남긴 몫이다. 사용자가 G290을 `확인`으로 정한 뒤 이어서 했다.

- **무엇을 했나:**
  - 읽은 것을 모으는 장치를 하나로 합쳤다. `_whenReads`가 `SortedSet<CallRead>`가 되어 `@when` 평가와 `Call` 영수증이 같은 모음을 쓴다(`_callReads`를 지움). `Read(id, 이름)`이 한 곳에서 담는다.
  - 쓰기 표시는 `Touch(id, 이름)`이다. 이름 없는 쓰기(엔티티 생성·삭제)는 그 엔티티를 읽은 모든 조건을 깨운다. 값·줄·붙이기·떼기·기여 행·키 줄 정리는 컴포넌트 이름, 역방향 컬렉션은 inverse 이름, 배정은 `"@seat"`을 넘긴다.
  - 읽은 조건 색인 `_whenReaders`는 엔티티 → 칸 이름 → 조건이다. 엔티티 전체를 읽은 조건은 `"*"` 칸에 들어, 어느 이름의 쓰기에도 깨어난다. 살았는지만 읽은 조건(`""` 칸)은 삭제 때만 깨어난다. 순회 순서 규칙(KE0002) 때문에 안쪽 사전은 `SortedDictionary`다.
- **확인:**
  - 빠뜨림은 기존 `WhenTrackingTests`의 무작위 비교(전체 훑기와 결과·저장 견주기)가 그대로 통과해 막는다.
  - 덜 평가하는지는 새 `WhenNarrowingTests`가 센다(테스트용 `World.WhenEvaluations`). 다른 컴포넌트에 쓰거나, Owner를 바꾸거나, 앉히거나, 컴포넌트를 붙일 때 그 칸을 읽은 조건 하나만 다시 평가한다.
- **테스트:** 2267개 통과(커널 2154, 번역 49, CLI 39, 콘솔 호스트 25).

## 함수 자리에 UI를 건드리는 함수를 넣는 것 막기 (2026-10-21, G282 보강)

S10 UI의 행동에서 미룬 "`PatchOperationCheck`가 패치로 `ui`를 건드리게 된 함수를 알리는 것"이다.

- **재현:** `Cond["Goal"]` 자리에 `ui.Selection`을 읽는 함수를 Def 값(`Goal("A", check=peek)`)으로도, 패치(`patch.set("core:A", "check", peek)`)로도 넣어 봤다. 둘 다 오류 없이 로드됐다. 항목에 적힌 패치뿐 아니라 Def 값도 빠져 있었다. 두 경로가 같은 `PatchOperationCheck.Mismatch`를 거치므로 한 곳을 고쳤다.
- **고침:**
  - `Mismatch`의 함수 자리 검사에서 `fn.TouchesUi`이면 거절한다(읽기·쓰기·`submit` 모두, 증거는 처음 건드린 곳).
  - `Cond`와 `Action` 자리 모두에 적용한다. 둘 다 게임 쪽에서 돈다.
  - 코드는 기존 값 검사 코드를 그대로 쓴다(Def 값은 `VAL002`, 패치는 `PAT009`). 오류는 값을 처음 적은 모드의 몫이다(G64).
- **테스트:** `UiEffectTests`에 Def 값(`Cond`·`Action` 둘)과 패치 한 건을 더했다.
- **확인:** 테스트 2269개 통과(커널 2156, 번역 49, CLI 39, 콘솔 호스트 25).

## 호스트 함수를 `Cond` 칸의 값으로 (2026-10-21, G291)

G221에서 미룬 "콘텐츠 파일 최상위에서 호스트 함수를 값으로 참조"를 `Cond` 칸에 한해 끝냈다. 사용자는 "1번이 맞는 것 같다"(읽기 등급이면 허용)고 했고, `Action` 칸은 스크립트 함수만 받기로 했다.

- **정정:** 처음에는 "엔진이 C# 안을 못 봐서 등급을 믿어야 한다"고 설명했으나, 읽기 등급에는 쓰기 창구를 건네지 않으므로(G217) 틀린 설명이었다. G216은 이미 `Cond`에서도 읽기 등급을 부를 수 있다고 정했다.
- **구현:**
  - `HostReference`(`Host/HostReference.cs`): 이름과 서명만 든 값. `HostCallable`이라 쓰기 자리 규칙 등 기존 길을 그대로 탄다.
  - Def 파일 실행 중 보이는 호스트 이름(`core`, `core.goals`)이 `HostReferenceNamespace`로 풀린다. `DefBuiltins`가 `HostNames`와 서명 조회를 받는다.
  - 로더는 호스트 서명을 Def 파일 실행 전에 푼다(`u.Hosts`를 앞으로 옮김).
  - `PatchOperationCheck.Mismatch`가 `HostSlotMismatch`로 등급·서명·반환을 본다. Def 값과 패치가 같은 길이다.
  - `DefConst`가 읽을 때 `HostReference`를 그 월드의 호스트 함수 값으로 바꾼다. `DefDump`는 `{"host": …}`.
- **테스트:** `HostSlotTests` 8개(게임에서 돎, 쓰기 등급 거절, `Action` 칸 거절, 서명·반환 어긋남, 패치, 없는 이름, 로드 중 호출).
- **확인:** 테스트 2277개 통과(커널 2164, 번역 49, CLI 39, 콘솔 호스트 25).
- **남은 것:** `Action` 칸의 쓰기 등급 호스트 함수(사례가 생기면).
- **CLI 확인(같은 날):** `engine check --host`는 선언 파일이 있어야 통과하고(없으면 이름 오류), `dump --host`는 `{"host": …}`로 적고, `stubs --host`는 그대로 돈다. 테스트 3개를 `CommandsTests`에 더했다. 테스트 2280개 통과(커널 2164, 번역 49, CLI 42, 콘솔 호스트 25).

## 호스트 함수를 `Action` 칸의 값으로 (2026-10-21, G292)

G291에서 미룬 몫이다. 사용자가 선택지 A("허용한다, `Cond`와 같은 서명 검사")를 골랐다.

- **재검토:** G291에서 든 "호스트 함수는 `ask`·`wait`로 멈출 수 없다"는 이유는 멈추지 않는 스크립트 `Action`에도 똑같아서 걸림돌이 아니었다. 쓰기 호출(G216)·되감기(G217)·멈춘 Flow 안의 저장(G221)이 이미 있었다.
- **고침:**
  - `PatchOperationCheck.HostSlotMismatch`: `Action`도 서명(칸의 Def `Ref[X]` 하나로 부를 수 있나)만 본다. 읽기 등급과 `bool` 반환 조건은 `Cond`만.
  - `World.BindHostReferences`: 칸을 읽을 때 `list[Action[...]]` 안의 참조도 그 월드의 호스트 함수 값으로 바꾼다(맨 위 값만 바꾸던 것을 넓힘).
- **테스트:** `HostSlotTests`의 `Action` 칸 거절을 허용으로 바꾸고 둘을 더했다. 쓰기 호스트 함수를 커맨드가 `g.Goal.effect(g)`로 실행, 목록 안의 호스트 함수를 `for act in …: act(g)`로 실행, `Action` 칸의 서명 어긋남.
- **확인:** 테스트 2282개 통과(커널 2166, 번역 49, CLI 42, 콘솔 호스트 25).

## 예시 게임 키우기: Pandemic 1조각, 지도와 말 (2026-10-21)

예시 게임을 키우기로 하고(사용자: 새 게임보다 시험에 맞는 기존 게임을 따라 하자, 내 게임 TR·WG·QU는 미완성이라 완성된 공개 규칙 게임으로) Pandemic을 골랐다. 한 번에 만들지 않고 조각으로 자른다. 1조각은 사용자 제안대로 지도와 말만.

- **`samples/pandemic/core`:** 도시 12개(Def, `color`·`neighbors` const), 플레이어 둘(앨리스·밥, 말 겸용: `At`·`Actions`·`Order`·`Seatable`), 판(`Game`). `Start`가 도시를 엔티티로 깔고 `ask` 루프로 차례를 돌린다(`Move`·`EndTurn`, 행동 4개 다 쓰면 자동으로 넘김). 협력 게임이라 승패는 아직 없다.
- **쓰인 엔진 기능:** Def const의 `list[DefId]` 그래프, `ref(..., inverse="here")`(도시에 선 말들), `DefId` 매개변수의 `choose(from_=)`, 응답 커맨드와 `Seatable`, 번역(en·ko), 콘솔 `ui/console.txt`.
- **우회한 것(드러난 구멍 후보, 결정 아님):** 엔티티의 Def id를 묻는 식이 스크립트에 없어 도시 엔티티에 `Site.city`(DefId 필드)를 따로 적었다. 도시를 이름으로 찾으려면 `query`를 돌며 비교한다.
- **테스트:** `PandemicTests`(콘솔 호스트 테스트, 7개): 로드 무진단, 시작 위치와 행동 4, 연결된 도시로 이동, 연결 없는 도시 거절, 4행동 뒤 차례 넘김, 남의 차례 거절, 이웃 관계가 서로 같은지. 응답 커맨드라 `Submit`이 아니라 `Respond(열린 질문)`으로 낸다.
- **확인:** 테스트 2289개 통과(커널 2166, 번역 49, CLI 42, 콘솔 호스트 32).
- **다음 조각(B):** 감염 카드와 큐브(키 컴포넌트), 연쇄 폭발(`@on`/`@when`), 큐브 합계·위험한 도시·감염 속도 `derived`. 이어서 숨은 덱, 치료·역할(기여 행), 확장 모드.

## 엔티티의 `defid` 속성 (2026-10-21, G293)

Pandemic 1조각에서 도시 엔티티에 `Site.city`(DefId 필드)를 따로 적어 둔 우회를, 사용자가 "entity.defid로 접근하게 할 수 없나"로 짚었다. 그대로 열었다.

- **고침:** 실행 `World.EntityAttribute`(컴포넌트·Def 타입 이름 뒤에 `defid` → 그 엔티티 Def의 전체 이름 `StrValue`), 로드 검사 `AttributeChecker` 같은 자리(`DefIdStatic`이라 맨 리터럴 비교는 `LNK005`), 스텁 `StubGenerator`(Def 타입마다 읽기 속성 `defid`, 그 타입의 구체 Def id 리터럴).
- **예시 정리:** `samples/pandemic`의 `Site` 컴포넌트를 없애고 `c.defid`로 바꿨다. 도시는 컴포넌트가 하나는 있어야 엔티티 타입이라(`SCH004`) 연구소 표시 `Research`를 넣었다(뒤 조각에서 쓸 것).
- **테스트:** `EntityDefIdTests` 3개(읽기·비교, 맨 문자열 경고, 스텁), `StubGeneratorTests` 기대 문자열에 `defid`. 2292개 통과(커널 2169, 번역 49, CLI 42, 콘솔 호스트 32).

## 예시 게임 Pandemic 2조각: 감염과 연쇄 폭발 (2026-10-21)

- **추가:** 질병(`Disease` Def 둘), 도시마다 질병별 큐브(키 컴포넌트 `Cubes`, `max=3`), 감염 덱(`InfectionCard`, 뽑을 더미/버린 더미 필드, 뽑기는 `random.choice`), 시작 감염(3·3·3·2·2·2·1·1·1장), 턴 끝 감염 단계(`rate`장), 폭발 수 8이면 패배.
- **`derived`:** `Match.rate`(에픽 수로 2,2,2,3,3,4,4), `Match.lost`(폭발 수 ≥ 8), `Load.total`(도시의 큐브 합, 키 줄을 돎), `Load.danger`(어느 질병이든 3개).
- **연쇄 폭발은 작업 목록으로 푼다.** 처음에는 `@on(Outbreak)` 핸들러가 이웃에 큐브를 올리게 짰는데, 핸들러는 **현재 Flow가 멈추거나 끝난 뒤에** 돈다(spine-spec S5, 의도된 규칙. 임시 시험으로 확인: 감염 단계 바로 뒤에 폭발 수를 읽으면 0). 그래서 지는 순간에도 다음 질문이 열리고, 카드 한 장마다 따로여야 할 연쇄가 한 단계 전체로 묶였다. 엔진에 `resolve()` 같은 즉시 처리 문장을 더하자고 제안했으나 사용자가 "구현 문제"라고 짚어 철회했다. 재귀는 못 쓰지만 `while`과 리스트(`queue = queue + [x]`)로 한 함수가 연쇄를 끝까지 푼다(`spread`). 신호 `Outbreak`는 "터졌다"를 다른 모드에 알리는 용도로만 낸다(받는 핸들러는 아직 없음).
- **알게 된 규칙:** 쓰기 함수는 `if f(...)`·`f(...).속성`처럼 식 안에서 못 부른다(`CMD004`): `x = f(...)`로 받아서 쓴다.
- **쓰지 않은 것:** `@on`·`@when`은 이 조각에서 실제로 쓰이지 않는다(패배 판정도 `derived`로 바뀜). 반응이 필요한 규칙(역할 능력, 박멸 알림 등)은 뒤 조각에서.
- **테스트:** `PandemicTests` 14개(새 7개: 시작 감염 분포, 턴 끝 감염 수, `rate`의 `derived`, 위험한 도시, 연쇄가 도시마다 한 번만, 카드마다 새 연쇄, 폭발 한도 패배). 

## 예시 게임 Pandemic 3조각: 숨은 감염 덱 (2026-10-21)

- **모델:** 카드 한 장 = 엔티티 `InfectionCard`. 공개 컴포넌트 `Card`(`pile` 0 뽑을 더미/1 버린 더미, `slot` 자리, 작을수록 위)와 숨기는 컴포넌트 `Face`(어느 도시인지). `@view_policy`의 `may_see`가 `Face`를 `pile == 1`일 때만 보인다고 한다. 그래서 화면과 AI에게는 뽑을 더미의 카드가 "있다"는 것(장수)만 보이고, 뽑혀 버려지면 앞면이 된다. 규칙(커맨드·`derived`)은 실제 상태를 다 읽는다.
- **순서:** 맨 위는 `slot`이 가장 작은 카드. 시작할 때 `random.sample`로 섞은 순서로 카드를 만들어 슬롯을 준다. 엔티티 번호가 카드 순서와 같아지지만 어느 도시인지는 숨겨져 있어 새는 것이 없다(안내서 7장 "번호 틈" 한계를 피하려고 도시 순서가 아니라 섞은 순서로 만듦). 더미가 비면 전부 다시 섞는다(`reshuffle`, 간단화: 실제 규칙은 에픽 카드가 버린 더미만 맨 위에 올림, 에픽은 플레이어 덱과 함께 다음 조각).
- **화면:** `infection_deck(me)`는 `{draw: 장수, discard: [앞면 도시들]}`을 돌려준다. 거른 읽기에서 뒷면은 `c.Face is None`이다.
- **테스트:** `PandemicTests` 19개(새 5개: 뽑을 더미는 두 자리와 관전자 모두에게 뒷면, 버린 더미는 앞면, 뽑히면 앞면이 되고 실제 값은 그대로, 화면 함수가 앞면만 돌려줌, 맨 위부터 뽑음). 예시를 짜며 새로 드러난 엔진 구멍은 없다.

## 예시 게임 Pandemic 4a조각: 플레이어 덱, 손패, 행동 (2026-10-21)

- **카드:** `PlayerCard` 엔티티, Def는 모두 같은 뒷면 `Back`(도시 카드인지는 숨긴 `Face`에만, 에픽 카드가 들어올 4b를 위해 `Face.epidemic`도 둠). `Card`(`pile` 0 덱/1 버림/2 손, `slot`)는 감염 덱과 같은 컴포넌트를 쓰고, `Held.holder`(역방향 `p.hand`)가 누구 손인지. 보기 정책은 `pile != 0`이면 앞면이라 손패는 모두에게 공개, 덱은 뒷면.
- **`derived`:** `Hand.size`(`len(h.entity.hand)`, 역방향 컬렉션을 읽음), `Hand.over`(7장 초과), `Match.lost`에 `deck_out`을 더함.
- **턴:** 행동 4개 → 카드 2장 뽑기(못 뽑으면 `deck_out`으로 짐) → 손이 7장을 넘으면 `ask`로 `Discard`를 받아 한도까지 → 감염 단계. 처음 손패 4장씩.
- **행동:** `Move`, `DirectFlight`(목적지 도시 카드를 버림), `BuildStation`(지금 도시 카드를 버림), `Treat`(질병 큐브 하나), 질문 "pandemic-discard"의 답 `Discard`.
- **알게 된 것:** 12도시 판에서는 덱이 14장이 아니라 12장이고 처음 손패가 8장이라 둘째 차례 뒤에 덱이 바닥난다(질 수 있음). 도시를 48개로 늘리는 것은 데이터 일이라 뒤로 미룸. 엔진 구멍은 새로 안 드러남.
- **테스트:** `PandemicTests` 28개(새 9개: 처음 손패와 덱, 손패 공개·덱 뒷면, 턴 끝 두 장, 덱 고갈 패배, 손패 초과 때 버리는 질문, 직항, 직항 거절, 연구소 짓기, 치료).

## 예시 게임 Pandemic 4b조각: 에픽, 치료제, 박멸, 승패 (2026-10-21)

- **질병 엔티티:** `Disease` Def마다 엔티티를 깔고 `Status`(`cured`, `eradicated` 필드, `on_board`·`supply` `derived`)를 둔다. `on_board`는 `query("City")`를 돌며 모든 도시의 이 질병 큐브를 더하는 **도시들을 건너 읽는 `derived`**다. 큐브 공급은 24개 대신 16개로 줄였다(도시가 12개라서).
- **승패 `derived`:** `Match.lost`(폭발 8번, 덱 바닥, 어느 질병이든 `supply < 0`), `won`(모든 질병 치료제), `over`(둘 중 하나). 턴 루프는 `while not g.Match.over`.
- **에픽 카드:** 플레이어 덱에 둘. 처음 손패를 나눈 뒤 남은 덱을 에픽 수만큼 더미로 나누고 더미마다 하나를 섞어 넣는다(`seed_epidemics`; 처음 짠 것은 시작 값을 잘못 잡아 고쳤다. 앞 더미를 처리하며 바뀐 자리가 뒤 더미 계산에 섞이지 않게 뒤 더미부터 처리). 뽑으면 손에 들지 않고 곧 치른다: 속도 +1(`epidemics`가 늘어 `rate` `derived`가 바뀜), 감염 덱 맨 밑(`slot` 최대) 카드의 도시에 큐브를 3개로 채우고(이미 큐브가 있으면 채운 뒤 터짐, 규칙서 대조 필요), 버린 더미를 섞어 뽑을 더미 맨 위에 올림(`intensify`: 뽑을 더미는 버린 장수만큼 아래로 밀고 버린 카드는 0번부터 새 자리).
- **치료제:** `DiscoverCure`(연구소가 있는 도시, 같은 색 카드 5장; 더 가졌으면 `slot`이 작은 것부터 버림, 간단화). 치료제가 있는 질병은 `Treat` 한 번에 도시의 큐브를 모두 걷는다.
- **박멸 `@when`:** `@when(lambda d: d.Status.cured and d.Status.on_board == 0 and not d.Status.eradicated)`가 `eradicated`를 적는다. 조건이 모든 도시의 큐브를 건너 읽는데도 `Treat`로 큐브가 0이 되면 정확히 한 번 발동한다(`@when` 읽기 기록이 도시 건너 `derived`를 따라감). 박멸한 질병은 `add_cube`가 더 올리지 않는다.
- **알게 된 것:** 에픽을 섞은 덱에서 기대한 대로 도는지는 시험에서 `slot`을 직접 정해 확인한다. 엔진 구멍은 새로 안 드러남.
- **테스트:** `PandemicTests` 37개(새 9개 + 폭발 한도 시험을 "7번에서 8번째로"로 고침). 기존 시험은 에픽을 먼저 버려 두고 돌린다.

## 기여 행 설계(G294~G301)와 스크립트 내장 (2026-10-21)

- **설계:** 보너스를 손으로 넣는 것(값 기여 행)과 있으면 붙는 것(선언 보너스)으로 나눴다(G294). 선언은 `const(list[Contrib])` 칸을 커널이 모으고(G295), `contrib(...)`로 만든다(G296). 키 줄은 `key=`(G297), 모인 줄은 `source`·`component`·`key`(G298), 다른 엔티티는 `to=` 한 걸음(G300), 조건부는 `when=lambda src, dst`(G301). 이름 있는 효과(효과 Def 키 줄, 만료)는 `std.stats`의 몫. 사례 비교(RimWorld `statOffsets`·`equippedStatOffsets`, 패러독스 static·event·triggered modifier, GAS, 이전 엔진 `ModifierDef`·`Comp_ModifierProvider`)는 각 G의 이유에.
- **구현 1단계(G299):** 쓰기 내장 `add_contrib(e.C, "이름", "op", 값, source=e)` → int id, `remove_contrib(id)`, `remove_contribs(출처)` → 뗀 수(`World.Contributions.cs`의 `AddContributionBuiltins`, 검사는 호스트 API와 같음). `contribs()`의 줄에 `id`. `contribs`와 멤버 검사를 `DerivedMemberArg`로 함께 씀. 스텁과 안내서 3장·9장.
- **테스트:** `ScriptContributionTests` 6개(id로 넣고 빼기와 `c.id`, 출처로 모두 빼기와 수, 키 줄은 줄을 넘김, 실패한 커맨드는 되감음, 잘못된 인자의 안내, 순수 함수에서는 `CMD004`). 처음 쓴 오타 이름 `plian`은 가까운 `derived`가 없어 힌트가 없는 것이 맞아 `mxa`로 고침.
- **구현 2단계(G295~G298, 선언 보너스의 기본):** 스키마 타입 `Contrib`(`ContribTypeRef`)와 로드 시점 함수 `contrib("C.m", "op", 값, key=)`(`ContribFunction`, 값 `ContribValue`, 키는 만든 모드 기준 전체 이름). `const(list[Contrib])` 한 모양만 받는다: `field`·`derived`·`ui_state`의 Contrib, `const(Contrib)` 같은 다른 모양, 키 컴포넌트 줄의 Contrib 칸은 `SCH` 오류(조용히 안 모이는 대신). 링크 검사(`Linker.CheckContrib`): 컴포넌트가 있고 보이는지, `derived`인지(G98), 키 컴포넌트면 Def 키이고 `key=`가 있고 그 Def가 있는지, 키 없으면 `key=`가 없는지. 덤프는 `{"contrib", "op", "value", "key"}`. 실행: `contribs()`가 저장된 줄 뒤에 선언 줄을 붙인다(`CollectDeclared`). 선언 자리(키 없는 컴포넌트의 칸, Def 키 컴포넌트와 그 키 Def 타입의 칸)는 스키마에서 한 번 뽑고, 없으면 통째로 건너뛴다. 키 줄은 `DefRowKeys`(공개 `RowKeys`에서 빼냄) 순. 선언 자리 컴포넌트는 모두 읽은 것으로 적어 붙이기·떼기가 `@when`을 깨운다. 선언 줄은 `id` None, `source` 그 엔티티, `component`, `key`(`DeclaredContributionView`). 저장된 줄은 `component`·`key` None. `Explain`의 기여 근거에 `FromComponent`·`FromKey`(글은 `from #1 (Traits[banking:Tough])`), 거른 읽기는 선언한 컴포넌트가 안 보이면 숨김. 스텁: `Contribution`에 `component`·`key`, 스키마·Def 파일 스텁에 `Contrib`·`contrib`(로직 스텁에는 없음). 안내서 3장 "선언 보너스", 9장.
- **테스트:** `DeclaredContributionTests` 16개(컴포넌트·키 Def·스키마 기본값에서 모으기와 줄 속성, 저장된 줄이 먼저와 `Explain` 글, 다른 모드의 `patch.append`, 런타임 붙이기·떼기와 줄 붙이기·지우기, 선언 컴포넌트를 붙이면 `@when`이 깨어남, 보기 정책, 로드 오류 6가지, 칸 모양 오류 3가지, int 키 컴포넌트 겨냥).
- **구현 3단계(G300, `to=`):** `contrib(..., to="Held.holder")`·`to="units"`(`ContribValue.To`·`ToSpan`, 같음 비교와 덤프에 포함, 모양이 틀리면 `EVL` 오류). 링크 검사(`Linker.CheckContribTo`): `"C.r"`이면 컴포넌트가 있고 보이며 r이 Ref이고 C에 키가 없는지, 이름이면 보이는 역방향 컬렉션이 있는지, Def에 적은 컴포넌트 값이면(`_holder`) 그 역방향 컬렉션이 선언을 든 타입의 것인지. 닿는 엔티티 타입이 대상 컴포넌트를 가질 수 있는지는 안 봄(컴포넌트는 런타임에 아무 엔티티에나 붙음). 실행: 내용에 실제로 적힌 걸음만 한 번 뽑아 두고(`ContributionSteps`, Def 값과 스키마 기본값을 훑음), `contribs()`가 자기 엔티티의 선언 뒤에 Ref 걸음(역방향 색인 `_inverse`로 나를 가리키는 엔티티, id 순)과 역방향 컬렉션 걸음(내 Ref가 가리키는 엔티티)의 선언을 붙인다(`CollectFrom`). 읽기 기록은 Ref의 역방향 이름(없으면 엔티티 전체 쓰기로 깨어남)과 Ref를 든 컴포넌트. 안내서 3장·9장, 스텁 `to:`.
- **테스트:** `ContributionStepTests` 9개(Ref 걸음과 칼 자신의 보너스, 두 자루, 내려놓기·파괴, 역방향 걸음과 진영 파괴, 칼을 들면 `@when`이 깨어남, 로드 오류 6가지). 처음에 Def가 준 컴포넌트를 `Remove`하려다 엔진이 막아(맞는 동작) 진영 파괴로 바꿈.
- **구현 4단계(G301, `when=`):** `contrib(..., when=함수)`(`ContribValue.When`, 스크립트 함수는 `contrib()` 때 위치 인자 둘로 부를 수 있는지 봄, 함수가 아니면 `EVL`). 값 검사(`PatchOperationCheck.Mismatch`의 `ContribTypeRef`)가 `Cond` 칸과 같은 검사를 한다: 커맨드·UI·쓰기·난수 함수는 안 됨(검사를 `FunctionMismatch`로 빼 `Cond`·`Action` 칸과 함께 씀), 호스트 함수는 읽기 등급이고 위치 인자 둘로 부를 수 있고 bool을 돌려줘야 함(`WhenMismatch`). 실행: 모으는 줄마다 `WhenHolds`가 `(src, dst)` 엔티티로 부른다(스크립트는 `RunFunction`의 인자 여럿 꼴로 그 `derived` 예산 안에서, 호스트는 `BindHostReferences` 뒤 `Call`). 거짓이면 그 줄 없음, bool이 아니면 `RUN` 타입 오류, 자기 스탯을 읽으면 `RUN014`. 조건의 읽기는 그 `derived`의 읽기라 `@when`·`Explain`에 잡힌다. 덤프에 `"when"`, 스텁 `when:`. 안내서 3장·9장.
- **테스트:** `ContributionConditionTests` 13개(받는 쪽 조건과 `Explain`에 조건의 읽기, 주는 쪽 조건, 조건이 읽은 칸이 `@when`을 깨움, bool 아님, 순환, 읽기 호스트 함수, 로드 오류 7가지). 모두 첫 실행에 통과.

## 예시 게임 Pandemic 4c조각: 과학자 역할 (2026-10-21)

- **모양:** 역할은 데이터 전용 Def 타입 `Role(grants=const(list[Contrib]))`, 플레이어는 키 컴포넌트 `Roles`(키 `Role`, `keys="listed"`, 칸 없음)로 역할을 든다. 치료제에 필요한 카드 수는 `Cure.cards` `derived`(`CURE_CARDS` + `"add"` 기여의 합)이고 `DiscoverCure`가 이것을 읽는다. 과학자는 `Role("Scientist", grants=[contrib("Cure.cards", "add", -1)])`, Bob이 Def에서 과학자다. 역할 모양은 예시 모드의 선택(엔진 결정 아님).
- **알게 된 것:** 칸이 없는 키 컴포넌트도 된다(`Roles={"Scientist": Roles()}`). 런타임에 `AddRow`로 역할을 붙여도 보너스가 바로 따라온다. `engine check`는 오류·경고 0. 새 엔진 구멍은 없다.
- **테스트:** `PandemicTests` 39개(새 2개: Bob은 4장·Alice는 5장과 `Explain`의 `(Roles[pandemic:Scientist])`, 런타임에 과학자가 된 Alice가 4장으로 치료).

## 선언 기여 줄을 든 멈춘 Flow의 저장 (2026-10-22, 버그 수정)

- **증상(임시 테스트로 재현):** 커맨드가 `contribs()`에서 받은 선언 줄(2단계의 `DeclaredContributionView`)을 지역 변수에 든 채 `wait`·`ask`로 멈추면 `Save()`가 "a contribution value cannot be saved yet"로 실패했다. 2단계에서 저장된 행(`contribution` 태그)만 저장 쪽에 있었다.
- **고침:** 지역 변수 값 태그 `declared_contribution`(`source`·`component`·`key`·`op`·`value`)을 더했다. 줄은 Def 값(`ContribValue`)을 붙들지 않고 제 값을 든다(같음 비교도 그 다섯 칸). 저장 형식에 태그 하나가 늘었을 뿐 옛 저장은 그대로 읽힌다.
- **테스트:** `DeclaredContributionTests`에 1개(멈춘 Flow가 키 줄의 선언 줄을 든 채 저장·불러오기 뒤 값과 `key`가 남음).

## 데이터 전용 Def를 읽는 `defdata(id)` (G303·G304, 2026-10-22)

- **왜:** 판데믹의 의무병·운영 전문가·격리 전문가 같은 능력형 역할은 커맨드가 역할 Def의 데이터를 읽어야 하는데, 엔티티가 없는 Def의 `const`를 읽을 길이 없었다(G107은 엔티티의 Def만).
- **구현:** 순수 내장 `defdata(id)`(`PureBuiltins.DefDataName`, 등록은 `World.Derived.cs`, 본체는 새 `World.DefData.cs`로 옮김; G107의 `DefView`·`DefConst`·`BindHostReferences`도 함께 옮겨 `World.Derived.cs`가 900줄 아래로). 짧은 이름은 `defid`처럼 부른 함수의 모드 기준, 없는 Def는 실행 오류("혹시 …?"). 값 `DefDataView`는 타입 이름 속성으로 그 타입의 `const` 보기(다른 타입이면 None), 모르는 타입 이름은 실행 오류. `const` 읽기는 `ConstOf(FrozenDef, DefType, 이름)`로 G107과 함께 씀. `Explain`은 엔티티 없이 `Role.x[모드:Medic] = 값`(`ExplainNode` 렌더가 엔티티 0이면 `of`를 뺌).
- **저장:** 멈춘 Flow의 지역 값 태그 `def_data`(`def`, `type` 또는 null). 불러올 때 Def가 없거나 타입이 바뀌었으면 문제로 적고 None.
- **로드 검사:** 속성 검사기(G228)에 `DefDataStatic`: `defdata(...).타입이름`은 `DefConstsType`로 이어 `const` 이름 오타까지 잡고, 모르는 타입 이름은 오류. 리터럴 id는 `defid`처럼 링크 검사(`FunctionNameChecker`).
- **스텁:** 모드 스텁(`defid` 옆)에 `class _DefData`(보이는 Def 타입마다 `타입: _타입_consts | None`)와 `def defdata(id: str, /) -> _DefData`, 스키마 스텁엔 `-> Any`.
- **테스트:** `DefDataTests` 8개(짧은·전체 이름과 다른 타입의 None, `derived`에서 DefId 값으로 읽기와 `Explain`, 없는 Def의 실행 오류, 로드 때 타입·`const` 오타, 리터럴 id 링크 오류, 리스트를 거쳐 검사를 비켜 간 모르는 타입의 실행 오류, `defdata` 값을 든 멈춘 Flow의 저장·불러오기). `StubGeneratorTests`의 기대 스텁에 새 블록.

## 예시 게임 Pandemic 4c조각: 능력형 역할 셋 (2026-10-22)

- **모양:** `Role` Def에 켜고 끄는 `const` 넷(`treat_all`·`clears_cured`·`free_station`·`quarantine`, 이름은 이 모드가 정함). 커맨드와 함수는 `roles(p) = [defdata(k).Role for k in p.Roles]`로 읽고 `any(r.능력 for r in roles(p))`로 묻는다. 수치 보너스(과학자)는 그대로 선언 보너스.
- **의무병:** `Treat`가 한 번에 모두 걷음. 치료제가 있는 질병은 의무병이 있는 도시에 놓이지 않고(`add_cube`·에픽의 `protected`), 들어가면 걷힘(`@when(lambda p: medic_mess(p))`, 행동을 쓰지 않음). 가드 없이 `@when`만으로는 한 감염 단계 안에서 3개가 차 터지는 것을 못 막아 둘 다 둠(가드를 끈 돌연변이로 시험이 잡는 것을 확인).
- **운영 전문가:** `BuildStation(player, card: Ref[PlayerCard] | None)`. `choose(card, from_=...)` 목록에 운영 전문가에게만 `None`을 넣는다. `where=`는 None을 늘 후보에서 빼므로 `from_=`로 씀(엔진 동작, 구멍 아님).
- **격리 전문가:** 있는 도시와 이웃에 큐브가 놓이지 않음(감염·연쇄 폭발·에픽).
- **간단화:** 처음 감염(판 깔기)은 역할을 보지 않는다. 규칙은 일반 지식으로 씀(규칙서와 대조할 것).
- **알게 된 것:** 엔진 구멍 새로 없음. `defdata`로 능력형 역할이 데이터로 된다.
- **테스트:** `PandemicTests` 44개(새 5개: 의무병의 한 번에 치료, 들어가면 걷힘, 놓이지 않음(폭발 없음), 운영 전문가의 카드 없는 연구소, 격리 전문가의 이웃 보호와 연쇄 폭발).

## 판데믹 확장 모드 `brink` A단계와 `defs(타입)` (G305, 2026-10-22)

- **재현:** 확장 모드 `brink`(코어 의존)가 `City` Def 둘(부에노스아이레스·산티아고)을 더하고 `patch.append`로 기존 도시의 이웃을 이었는데, 판을 깔면 도시 엔티티는 12개 그대로였다(임시 시험). 코어 `Start`가 박아 둔 `CITIES` 목록만 돌았고, Def를 타입으로 도는 내장이 없었다 → G305.
- **구현 `defs("City")`:** 순수 내장(`PureBuiltins.DefsName`, 본체 `World.DefData.cs`의 `DefsBuiltin`)이 `DefSet.OfType`의 전체 id 목록(등록 순서, abstract는 동결본에 없음). 모르는 타입은 실행 오류("혹시 …?"). 리터럴 타입 이름은 ⑤ 링크가 봄(`ModFunctions.DefTypeLiterals` → `Linker.CheckDefType`: 없는 타입·컴포넌트 이름은 `LNK001`, 안 보이는 타입은 `LNK` 가시성 오류, 데이터 전용 타입도 됨). 속성 검사기는 원소를 `DefIdStatic`으로 보아 맨 문자열 비교를 경고(G261). 스텁: 모드 스텁 `def defs(type: Literal[보이는 타입들], /) -> list[str]`, 스키마 스텁 `type: str`.
- **코어 고침:** `DISEASES`·`CITIES` 목록을 없애고 `defs("Disease")`·`defs("City")`로(등록 순서가 목록과 같아 시드 3의 판은 그대로). 턴마다 행동 수를 `Actions.per_turn` derived(`ACTIONS` + 기여 `add`)로 두어 확장 역할이 선언 보너스를 걸 수 있게 함.
- **`brink`:** 도시 둘과 이웃 패치(`defs/map.py`), 제너럴리스트 `Role("Generalist", grants=[contrib("Actions.per_turn", "add", 1)])`(`defs/roles.py`), en·ko 도시 이름.
- **알게 된 것:** 확장이 보너스를 걸려면 코어가 그 값을 `derived`로 열어 둬야 한다(엔진 구멍이 아니라 코어 모드의 설계). 이름을 박은 목록은 확장을 조용히 무시한다.
- **테스트:** `DefDataTests`에 `defs` 4개(5케이스: 로드 순서·의존하지 않는 모드·abstract, 리터럴 타입 오타와 컴포넌트 이름, 실행 중 모르는 타입, 맨 문자열 비교 경고). `PandemicTests`는 코어 모드만 읽게(`Load(out, 모드들)`), 새 `BrinkTests` 5개(둘 다 깨끗이 로드, 새 도시와 카드, 패치한 이웃이 양쪽, 새 도시로 이동, 제너럴리스트 5행동).

## 이름 쓰는 원칙 구현 (G306~G308, 2026-10-22)

- **원칙:** 선언에서 새로 짓는 이름은 문자열, 로직 안에서 이미 있는 스키마 이름(Def 타입·컴포넌트·신호)은 맨 이름, Def id는 어디서나 문자열. `query(City)`, `query(Unit, Wounded)`, `defs(City)`, `component == Face`.
- **Def 타입 값(G306):** 실행 중 전역(`WorldGlobals`)에 보이는 Def 타입마다 부를 수 없는 순수 내장 값(`World.AddDefTypeValues`, 저장 태그는 기존 `schema`)을 둔다. 로드 때 이름 검사(`FunctionNameChecker`)는 Def 타입 이름을 보이는 스키마 이름으로 치고(`DefBuiltins.IsVisibleDefTypeName`), 함수 안에서 부르면 `EVL003` "'T' is a Def type; inside a logic function it names the type and cannot be called". 로드 시점의 `City("x")` 선언은 그대로. 속성 검사기는 Def 타입 이름을 `ConstructorType`(`DefTypeKind`)으로 보고 `City.x`는 오류(엔티티나 `defdata`로 읽으라는 힌트).
- **문자열은 오류(G307):** `query("X")`·`defs("X")`의 리터럴은 로드 때 `EVL003` "takes schema names, not strings"와 "write query(X) without quotes", 실행 중 문자열은 `RUN005` 같은 글(`World.SchemaNameArgument`). 리터럴 이름 확인은 이름 검사로 옮겨 오타는 `EVL001`("혹시 …?"), 안 보이는 이름은 `SCH003`. 데이터 전용 타입·신호·두 Def 타입은 여전히 ⑤ 링크(`LNK001`·`LNK002`). Explain의 `query(...)` 줄도 맨 이름으로.
- **컴포넌트 값(G308):** 보기 정책의 `component`와 기여 줄의 `c.component`가 `SchemaNameValue(이름)`(로직의 맨 이름과 같은 값). 속성 검사기는 정책의 매개변수를 엔티티·엔티티·컴포넌트 값으로, `contribs()`의 원소를 `ContributionStatic`(속성 여섯, 다른 이름은 오류)으로 보며, 컴포넌트 값이나 스키마 이름을 문자열 리터럴과 `==`/`!=`로 비교하면 새 경고 `EVL009`(`Codes.EvlSchemaNameString`). 정책 매개변수 타입이 생겨 `entity.Nope.x`도 로드 때 잡힘.
- **스텁:** 모드 스텁 `def query(name: type[T], /, *more: type[Any]) -> list[T]` 겹침, `def defs(t: type[Any], /)`, 스키마 스텁 `query(*names: type[Any])`, 기여 줄 `component: type[Any] | None`.
- **일괄 고침:** 파이썬 스크립트(따옴표 없는 식별자 리터럴만 `query(...)`/`defs(...)` 안과 `component ==/!= "X"`에서 맨 이름으로, UTF-8·BOM·줄 끝 유지)로 샘플·테스트·벤치·안내서 33개 파일. 결정 기록(`spec-gaps`·`gaps/`·`history`·`spine-spec`)은 건드리지 않음. 오류 글을 확인하는 테스트는 손으로 고침.
- **안내서:** 3장 "이름 쓰는 법" 표, 내장 표의 `query(타입, 컴포넌트...)`·`defs`·`defdata`, 기여 줄의 `component`, 7장 `may_see`의 `component`, 1장 경고 목록에 `EVL009`.
- **테스트:** 새 `SchemaNameValueTests` 7개(맨 이름 비교 깨끗함, 정책·기여 줄의 문자열 비교 경고, 이름 오타, 기여 줄 속성 오타, Def 타입 이름의 속성), `DefDataTests`(문자열·맨 이름 오타·컴포넌트 이름 로드 검사, 실행 중 문자열과 컴포넌트 값, Def 타입 값을 변수로, 멈춘 Flow가 든 Def 타입 값 저장), `ContentLoaderTests`(함수 안에서 Def 타입 부르기), `WorldQueryTests`(실행 중 문자열·이름 값). 샘플 `check` 세 곳 오류·경고 0.

## 다른 모드의 최상위 이름 `모드id.이름` (G309, 2026-10-22)

- **재현:** 판데믹 확장 B단계에서 `brink`가 변종 바이러스 효과로 코어의 `add_cube`·`spread`를 부르자 `EVL001 name 'add_cube' is not defined`(임시 파일로 `check`). G20의 "다른 모드의 함수를 이름으로 쓰는 방법은 아직 없다"(잠정), G218이 같은 모양으로 비워 둔 자리 → G309: 필수 의존한 모드(와 자신)의 최상위 함수·상수를 `pandemic.add_cube(c, d)`·`pandemic.ACTIONS`로.
- **로드 검사:** `ModNames`(새 파일, 보이는 모드 id → `ModScope`, 안 보이는 활성 모드 id)를 `ContentLoader.ModNamesFor`가 만들어 이름 검사와 효과 판정에 넘김(의존 대상은 로드 순서상 먼저 끝나 이름과 효과가 정해져 있음). 이름 검사(`FunctionNameChecker.ModChain`): 맨 앞이 보이는 모드 id(지역·최상위·내장·스키마 이름이 가리지 않은 것)면 첫 속성이 그 모드의 최상위 이름이어야 하고, 아니면 `EVL001` "mod 'banking' has no top-level name 'bonuss'"와 "혹시 …?". 호스트 함수가 있는 모드 id는 기존 호스트 이름 공간 검사와 글을 그대로 씀. 모드 id만은 `EVL005` "is a mod, not a value", 의존하지 않은 모드는 `SCH003`. 효과 판정(`FunctionEffects.Resolve`)은 `모드id.이름` 호출을 같은 모드 호출처럼 셈(쓰기는 문장으로만, 난수·UI 전이, 커맨드 호출·값은 G118 오류). 한 번 본 호출을 속성으로 다시 걸어 같은 오류가 두 번 나던 것을 재현하고 고침(`IsNamed`).
- **겹침(`CheckHostShadowing`):** 최상위 이름이 보이는 모드 id와 같으면 `EVL004`, 자기 모드의 호스트 함수 경로 첫 조각과 같으면 `HST002` "'banking.helper' is both a host function group and this mod's top-level name 'helper'".
- **실행:** `World.ModNames.cs`의 `ModNamespace`를 모드마다 런타임 내장에 넣음(속성은 그 모드의 `ModScope`, 없으면 같은 id의 호스트 이름 공간). 같은 이름의 엔진 내장이 있으면 내장이 이김(이름 검사도 같음). 불린 함수는 제 모드의 전역으로 돌고(`f.Function.Mod`), 짧은 Def id도 맨 위 프레임의 모드 기준(G261). 재귀는 실행 중 안전망이 잡음. 함수 값을 든 멈춘 Flow 저장은 기존 함수 값 저장 그대로 됨.
- **스텁:** `class 모드id:` 안에 그 모드의 최상위 함수(`@staticmethod def f(a: Any) -> Any`)와 상수(`int`·`str`·`bool`, 나머지 `Any`), 그 뒤에 호스트 함수 경로 클래스. 커맨드 등 입구는 뺌.
- **하지 않은 것:** Def 파일 최상위(로드 시점)의 `pandemic.f` 값(예: `Cond` 칸에 다른 모드 함수)은 로드 시점 속성 접근이라 여전히 안 됨(호스트 함수만 G291). 사례가 나오면 본다.
- **테스트:** 새 `ModNamesTests` 14개(함수·상수 부르기와 제 모드 기준 실행, 자기 이름, 함수 값을 든 멈춘 Flow 저장, 식 안의 쓰기 함수, `derived`의 난수 함수, 커맨드 부르기, 오타, 의존 안 함, 모드 id만, 최상위 이름의 가림, 같은 이름의 지역 변수, 호스트 경로와 겹침, 호스트 함수와 한 id 공유, 스텁). 스텁 기대값 둘(커널·CLI)에 모드의 최상위 이름 클래스.

## 즉시 핸들러 `@on(X, now=True)`와 변종 바이러스 첫 효과 (G310, 2026-10-22)

- **드러난 것:** brink B단계에서 변종 바이러스의 "변종의 폭발은 폭발 표시를 두 칸"을 `@on(Outbreak)`로 짜자, 더한 한 칸이 코어가 감염 카드를 다 뽑고 다음 차례 `ask`로 멈춘 뒤에야 들어감을 테스트로 재현(폭발 6에서 변종이 터지면 8에서 져야 하는데 상파울루가 감염되고 질문이 열려 있음). 보통 핸들러는 낸 Flow 뒤에 돈다(S5)는 규칙 때문이다. G176을 다시 보고, 실시간에서 즉시 핸들러가 `ask`하면 어떻게 되는지 논의한 끝에 B3(시간을 막는 `ask`만 허용)으로 정함.
- **결정:** G310(전문은 `docs/gaps/S5-reactions.md`), G176은 `일부 바뀜(G310)`. 색인 머리말의 "다음 번호"를 G311로 고침.
- **구현:**
  - 선언: `@on(..., now=True)`(`HandlerDecl.Now`), `now=`에 bool이 아니면 `CMD001`, 오타 안내에 `now`.
  - 실행: `emit` 내장(`World._emit`)이 `Immediate`(신호 → 즉시 핸들러 목록, 단계 규칙 순서)와 `ImmediateArguments`(받는 엔티티가 사라졌으면 null)를 듦. 실행기(`Session.StatementCall`)가 `emit` 문장 뒤 프레임에 `ImmediateDispatch`를 달고, `Step`이 다음 문장 전에 핸들러를 하나씩 쌓음(`CallImmediate`, 값은 버림, 쓰기 모드는 낸 쪽 그대로, 입구 검사 우회). 그래서 멈춘 실행에서도 부른 쪽 문장은 `emit(...)`이고 그 위는 핸들러 하나다. 재진입은 기존 재귀 검사(`RUN` 순환)가 잡아 세대 상한이 따로 필요 없음. 보통 핸들러 대기열(`Drain`)은 즉시 핸들러를 건너뜀.
  - 멈춤: `Frame.Immediate`(즉시 핸들러와 그것이 문장으로 부른 함수)에서 `PureBuiltin.RefusedInImmediate`가 이유를 주면 실행 오류: `wait`·`advance`는 늘, `ask`는 마감이 있거나 `blocks_time=False`일 때.
  - 저장: 프레임에 `"dispatch": {"signal", "next"}`, 불러올 때 신호로 핸들러 목록을 다시 찾고 `Relink`가 위 프레임이 그 핸들러인지 보며 부른 위치·`Immediate`를 채움(저장 때 같은 계산으로 검사).
- **샘플:** `samples/pandemic/brink`에 `schema/virulent.py`(`Virulent` 컴포넌트를 `Disease`에 붙임), `defs/virulent.py`(첫 에픽 뒤 보드에 큐브가 가장 많은 질병이 변종 `@when`, 변종 폭발 한 칸 더 `@on(Outbreak, now=True)`, 코어 함수 `pandemic.disease_of`·`pandemic.game` 사용).
- **테스트:** 새 `ImmediateHandlerTests` 16개(emit 자리에서 정의 순서로, 낸 쪽과 서로의 쓰기를 봄, 오류면 커맨드 전체 되감김, 즉시 연쇄, 자기 재진입 오류, 사라진 엔티티의 핸들러는 건너뜀, `ask`로 낸 쪽까지 멈추고 이어 감, 멈춘 채 저장·불러오기와 다시 저장한 바이트, `wait`·함수 안의 `wait`·마감 `ask`·`blocks_time=False`·`advance` 오류, 보통 핸들러는 여전히 `wait`, `now=` 오류 둘). `BrinkTests.AVirulentOutbreakCountsTwiceAndCanLoseAtOnce`(재현했던 판). 안내서 6장에 "바로 도는 핸들러" 절과 표 한 줄.

## 다른 모드의 함수를 감싸기 `@wrap` (G311~G314, 2026-10-22)

- **드러난 것:** 변종 바이러스의 "변종의 치료제는 카드가 한 장 더"(Complex Molecular Structure)를 brink가 짤 수 없음: 코어의 치료 카드 수는 플레이어마다 숫자 하나(`Cure.cards`)라 기여로 더하면 모든 질병이 바뀌고, 치료 때 신호도 없다. Chronic Effect(감염 때 큐브 2개)도 같은 종류(코드로 따진 것, 짜 보지는 않음). 코어가 열지 않은 자리는 못 바꾼다.
- **결정:** G311(원칙: 보이는 모드의 최상위 함수를 감싸고 원래 함수를 부를 수 있음, 모든 호출 자리, 로드 순서대로 겹겹이, 효과는 원래보다 넓을 수 없음), G312(`@wrap(대상)`, 첫 매개변수가 안쪽 함수, 나머지는 대상과 같음, 직접 부를 수 없음), G313(보통 함수와 칸에 걸린 함수만, 입구·호스트·상수는 오류, 게임이 돌 때만 적용), G314(안쪽 함수는 부르기만, 저장에 적지 않고 불러올 때 다시 계산). G176은 `일부 바뀜(G310·G311)`.
- **구현:**
  - 선언: `FunctionDef.IsWrap`·`FunctionValue.IsWrap`(입구로 셈, `EntryKind` "@wrap function"), `Interpreter.DeclareWrap`가 대상을 평가하지 않고 이름(`모드id.이름`이나 맨 이름)으로 `WrapDecl`에 적음. 매개변수의 기본값·타입 표기는 오류.
  - 로드: 새 `Loading/WrapCheck`가 모드의 효과 판정 뒤 대상을 `ModNames`로 풀고 검사(보이지 않는 모드 `SCH003`, 없는 이름 `EVL001`+"혹시 …?", 람다·상수·입구·호스트 함수 `CMD001`, 매개변수 모양 `CMD001`, 더 넓은 효과 `CMD004`, 첫 매개변수를 부르기 외에 쓰거나 람다 안에서 쓰면 `CMD001`, 대상이 쓰기 함수인데 식 안에서 부르면 `CMD004`). 통과하면 감싸는 함수의 효과 표시를 대상의 것으로 맞춤. `ContentLoadResult.Wraps`(로드 순서 → 정의 순서).
  - 실행: `PureEvaluator.Wrappers`(런타임 평가기에만 → 로드 중 계산은 원래 함수). `Session.Push`가 감싸진 대상을 부르면 맨 바깥 감싸기를 첫 인자 `InnerFunction(대상, 안쪽 번호)`와 함께 쌓고, `InnerFunction` 호출(문장·식)은 다음 안쪽(없으면 원래 함수를 감싸기 없이). 대상을 이름으로 다시 부르면 재귀 검사가 잡음.
  - 저장: 프레임 지역 변수에서 `InnerFunction`은 빼고, 불러올 때 `World.RestoreInner`가 지금의 감싸기 순서로 다시 넣음.
  - 스텁: `wrap`, `on(..., now=)`.
- **샘플:** 코어 `rules.py`에 `cure_cost(player, disease)`를 빼고 `DiscoverCure`가 씀(관례: 바뀔 계산은 함수로). brink `virulent.py`에 `@wrap(pandemic.cure_cost) complex_cure`.
- **테스트:** 새 `WrapTests` 26개(코어 자신의 호출, 두 모드의 겹침 순서와 이름 인자, 안쪽을 안 부르기, `derived` 함수 감싸기, 쓰기 함수 감싸기, 자기 모드 함수, 이름으로 다시 부르면 재귀, 안쪽 함수 안에서 멈춘 채 저장·불러오기·다시 저장한 바이트, 로드 오류 15가지, 의존 안 함, 직접 부르기, 호스트 함수). `BrinkTests.AVirulentCureNeedsOneMoreCard`. 알 수 없는 데코레이터 메시지 기대값 둘. 안내서 3장에 "다른 모드의 함수를 감싸기" 절.

### 2026-10-22: 판데믹 확장 B단계, Chronic Effect (`@wrap(pandemic.add_cube)`)

- **규칙(일반 지식, 규칙서 대조 전):** 변종 큐브가 없는 도시가 변종에 감염되면 큐브를 두 개 놓는다. 코어의 `add_cube` 하나가 감염 단계와 연쇄 폭발 모두에서 큐브를 올리므로, 감싸면 둘 다에 닿는다. 연쇄 폭발에도 적용되는지는 규칙서로 확인할 것(에픽은 `add_cube`를 거치지 않고 3개로 채우므로 닿지 않음).
- **샘플:** brink `virulent.py`에 `@wrap(pandemic.add_cube) chronic`: 변종이고 그 도시에 0개면 안쪽을 한 번 먼저 부르고, 터졌는지는 둘째 호출의 답. 박멸·역할의 막기는 안쪽 함수가 두 번 다 본다.
- **드러난 것:** 새 엔진 구멍 없음. 처음 `return original(...)`으로 써서 `CMD004`(쓰기 함수는 문장으로만, G117)에 걸림 → `over = original(...)`. 진단 메시지가 고칠 방법을 알려 줌.
- **테스트:** `BrinkTests` 2개 더(빈 도시는 둘·있던 도시는 하나·변종 아닌 질병은 하나, 연쇄 폭발의 빈 이웃도 둘). 모두 2464개 통과.

### 2026-10-22: Chronic Effect를 규칙서대로 고침 (`@wrap(pandemic.infect_city)`)

- **규칙서 대조:** On the Brink 규칙서의 카드 문구는 "During Infections, when a Virulent Strain city is drawn, place 2 … if the city has no Virulent Strain cubes … This does not affect Epidemics or Outbreaks." 앞 슬라이스는 `add_cube`를 감싸 연쇄 폭발의 빈 이웃에도 둘을 놓았다(틀림).
- **고침:** 코어 `rules.py`에 카드 한 장의 감염 `infect_city(city)`(큐브 하나, 넘치면 `spread`)를 빼고 `infect`가 부름. brink는 `@wrap(pandemic.infect_city) chronic`: 변종이고 0개면 `pandemic.add_cube`로 하나 먼저 올리고 안쪽을 부름. 엔진 변경 없음.
- **교훈:** 감싸기는 코어가 나눈 단위만큼만 정확하다. 규칙서가 이름 붙인 동작마다 함수를 두는 관례를 안내서 3장에 한 줄 더함.
- **함께 드러난 차이(남김):** 변종은 규칙서에서 첫 변종 에픽의 Infect 단계 직후에 정하고 동점은 무작위(brink는 에픽 뒤 `@when`, 동점은 먼저 등록된 질병). 규칙서는 변종 에픽 카드마다 효과가 하나씩 붙음(brink는 세 효과가 늘 켜짐).
- **테스트:** `BrinkTests.AVirulentOutbreakPlacesOneCubeOnCleanNeighbours`(빈 이웃에도 하나). 모두 2464개 통과.

### 2026-10-22: 변종을 정하는 때를 규칙서대로 (`@wrap(pandemic.epidemic_infect)`)

- **규칙서:** 첫 변종 에픽의 Infect 단계를 마친 뒤 보드에 큐브가 가장 많은 질병이 변종, 동점은 무작위. 그다음 Intensify와 그 카드의 효과를 마저 치른다.
- **재현(임시 테스트):** brink의 `@when(epidemics >= 1 and strain() is None)`은 흐름이 멈춘 뒤에 돌아, 같은 차례의 감염 단계가 끝난 다음에 변종을 정했다. 에픽 뒤 감염 카드로 빈 LosAngeles를 감염시켰을 때 Chronic Effect가 안 닿아 1개(규칙서대로면 2개). 차례 끝에는 노랑이 변종으로 정해져 있었다.
- **고침:** 코어 `epidemic(g)`를 규칙서의 세 단계로 나눔: `epidemics += 1`, `epidemic_infect(g)`, `intensify()`. brink는 `@wrap(pandemic.epidemic_infect) choose_strain`: 안쪽을 부르고 변종이 없으면 가장 많은 질병들 중 `random.choice`. 엔진 변경 없음. 이 자리(Infect 뒤, Intensify 전)는 카드마다 다른 즉시 효과를 붙일 자리이기도 하다.
- **테스트:** 임시 테스트를 `BrinkTests.TheStrainIsChosenRightAfterTheEpidemicsInfectStep`으로 남김, `ATieForTheStrainIsBrokenAtRandom`(시드 12개에서 둘 다 나옴). 모두 2466개 통과.

### 2026-10-22: 보기 규칙 `@view_rule` (G315, 보기 정책 하나를 대신함)

- **드러난 곳:** 판데믹 brink의 변종 에픽 카드(카드마다 효과)를 실물처럼 덱에 섞어 숨기려 하자, 보기 정책이 모드 전체에 하나뿐이라(G203) 확장이 제 컴포넌트를 숨길 길이 없었다(코어 정책은 모르는 컴포넌트를 보이게 두고, 둘째 정책은 로드 오류, `override`는 코어 규칙을 베껴야 함). "모두 참이어야 보임"이나 "코어가 판정 함수를 빼 두면 감쌈"은 사용자가 땜빵이라 물리고 엔진 차원의 답을 정함.
- **결정:** G315. `@view_rule(컴포넌트나 Def 타입) def f(viewer, entity)`, 답은 `True`·`False`·`None`(앞 규칙에 맡김), 같은 대상은 나중에 로드된 모드부터, 없거나 모두 `None`이면 보임, 한 모드 한 대상에 하나, 순서 없는 두 모드가 같은 대상이면 둘 다 로드 오류. `@view_policy`·`override=` 없앰, `requireViewPolicy` → `requireViewRules`. 한 번 보여 준 사건(몰래 보기)은 이어서 정함.
- **구현:**
  - 선언: `ViewRuleDecl`(대상 이름·컴포넌트인가), `Interpreter.DeclareViewRule`(맨 이름 하나, 매개변수 둘). `@view_policy`는 `CMD006`으로 새 표기를 알림.
  - 로드: 새 `Loading/ViewRuleCheck`. `Resolve`가 모드마다 대상을 풂(모드가 묶은 이름·신호·없는 이름 + "혹시 …?"·안 보이는 모드 `SCH003`), 같은 대상 둘째 규칙 오류. `Collect`가 `BuildDefs` 고정점 안에서 순서 없는 모드 쌍을 충돌 오류로. `ContentLoadResult.ViewRules`(로드 순서). 순수·난수 검사와 정적 타입(Def 타입 규칙의 entity는 그 타입)은 정책 자리를 그대로 옮김.
  - 실행: `World.View`가 대상마다 규칙 사슬(나중 것이 앞)을 들고, 엔티티 층은 엔티티의 Def 타입, 컴포넌트 층은 컴포넌트로 사슬을 찾음. 사슬이 없으면 시트 조회도 없이 보임.
  - 스텁 `view_rule(target)`.
- **옮긴 것:** 테스트 정책 11곳을 규칙으로(대개 `@view_rule(Unit)`의 안개, `@view_rule(Hand)`의 손패), `SchemaNameValueTests`의 컴포넌트 비교는 기여 줄 `c.component`로, 옛 정책 교체 테스트 6개 지움. 샘플 harbor·pandemic 코어, 안내서 7장(겹치는 규칙 절 더함), spine-spec 두 곳.
- **테스트:** 새 `ViewRuleTests` 19개(대상만 정함, 확장이 제 컴포넌트 숨기기, 뒤 규칙이 보이게 하고 `None`으로 맡김, 모두 `None`이면 보임, 안개·탐지·은신 세 층, 순서 없는 충돌, 다른 대상은 괜찮음, `loadAfter`면 됨, 대상 오류 7가지, 철자 제안, 안 보이는 모드, override 힌트, 로직에서 규칙 함수 부르기). 모두 2479개 통과.

### 2026-10-25: 판데믹 brink의 변종 에픽 카드 (효과를 카드에 붙여 덱에 숨김)

- **규칙서 대조(On the Brink 공식 규칙서 PDF, Z-Man 2024판):** 일반 에픽 카드 대신 변종 에픽 카드 여덟 장을 뒤집어 섞고 필요한 수만 쓴다. 카드마다 효과가 하나이고 변종에만 닿는다. 지속 효과(Chronic Effect·Complex Molecular Structure·Government Interference·Rate Effect·Slippery Slope)는 "에픽을 마친 뒤부터", 즉시 효과(Hidden Pocket·Unacceptable Loss·Uncounted Populations)는 "이 카드의 Intensify 단계 전에". 남은 넷의 문구: Government Interference "변종 큐브가 있는 도시를 떠나려면 그 차례에 먼저 그 도시의 변종 큐브를 하나 이상 Treat해야 한다", Rate Effect "감염 단계에서 뽑은 카드 중 변종 도시가 하나라도 있으면(변종이 박멸 전이면) 감염 속도보다 한 장 더 뽑는다", Hidden Pocket "변종이 박멸됐고 감염 버린 더미에 변종 카드가 하나라도 있으면 박멸을 되돌리고 그 카드들의 도시마다 큐브 하나", Unacceptable Loss "변종 큐브 4개를 공급에서 상자로(4개 미만이면 모두)".
- **샘플:** brink `schema/virulent.py`에 데이터 Def 타입 `StrainEffect`와 컴포넌트 `StrainCard(effect, done)`. `defs/virulent.py`: 효과 Def 넷(지속 셋과 즉시 Uncounted Populations), `@wrap(pandemic.seed_epidemics)`가 코어가 만든 에픽 카드마다 서로 다른 효과를 `random.sample`로 골라 `attach`, `@view_rule(StrainCard)`는 코어의 `Face`와 같이 덮인 동안 숨김. `@wrap(pandemic.intensify)`가 앞에서 즉시 효과를 치르고 뒤에서 `done`을 켬(지금 치르는 카드 = 버린 더미에서 아직 안 마친 것). 지속 효과 셋은 `strain_has(질병, 효과)`(변종이고 그 효과 카드가 `done`)일 때만. 번역 en·ko에 효과 이름.
- **인계 메모와 달라진 것:** 즉시 효과 자리를 `epidemic_infect` 감싸기 끝이 아니라 `intensify` 감싸기의 앞으로 함. 규칙서 문구가 "Intensify 전"이고 지속 효과를 켜는 "에픽을 마친 뒤"가 같은 감싸기의 뒤라서. 코어 변경 없음, 새 엔진 구멍 없음.
- **테스트:** `BrinkTests` 4개 더(에픽 카드마다 다른 효과가 숨겨져 붙음, 효과 카드를 안 뽑았으면 꺼짐, 지속 효과는 그 에픽의 Infect 단계에는 안 닿고 같은 차례 감염 단계부터, Uncounted Populations). 옛 효과 테스트는 `InEffect`로 효과를 켜게 고침. `in_effect`의 `done` 검사를 빼면 새 테스트 둘이 실패함을 확인.

### 2026-10-25: 판데믹 brink의 Rate Effect

- **규칙서:** 감염 단계에서 뽑은 카드 중 변종 도시가 하나라도 있으면(변종이 박멸 전이면) 감염 속도보다 한 장 더 뽑는다. 덧뽑은 장은 한 장뿐.
- **걸린 곳:** 뽑은 카드는 코어 `infect`의 지역 변수에만 있어 감싸는 쪽이 "변종 카드가 나왔나"를 모른다. 사용자와 고른 것: brink가 두 곳을 감싸고 본 것을 엔티티에 적음(코어 변경 없음). 뽑을 장수를 코어 함수로 빼는 길은 "코어가 미리 열어 둔다" 관례 논의로 미룸. 감염 단계 전후의 덱 비교는 덱이 다시 섞이면 틀려서 버림.
- **샘플:** brink 스키마에 `RateWatch(seen)`을 `Game`에 `extend`. `@wrap(pandemic.infect_city) rate_watch`가 변종 도시 감염 때 `seen`, `@wrap(pandemic.infect) rate_effect`가 표시를 지우고 원래 단계를 부른 뒤, 봤고·안 졌고·박멸 전이고·효과가 켜졌으면 한 장 더 뽑아 `pandemic.infect_city`(다른 감싸기도 거침). 한 모드가 같은 함수를 두 번 감싸도 됨(정의 순서대로 겹침).
- **테스트:** `BrinkTests` 3개 더(변종 도시 뒤 한 장만 더, 변종 도시 없으면 그대로, 박멸 뒤에는 그대로). 모두 2486개 통과.

### 2026-10-25: 커맨드 감싸기 (G316~G321)

- **드러난 곳:** 판데믹 brink의 Government Interference는 코어 커맨드 `Move`·`DirectFlight`에 조건을 더하고 `Treat`가 실행됐음을 알아야 하는데, `@wrap(pandemic.Move)`는 `CMD001`(G313, 재현). 코어가 판정 함수를 미리 빼 두는 길은 G315 때 물린 땜빵과 같은 모양이라, 코어의 준비 없이 끼어드는 길(커맨드 경계, 쓰기 순간의 즉시 반응, 함수 몸통 패치)을 짚고 커맨드 경계만 열기로 함.
- **결정:** G316(커맨드도 `@wrap`, 감싸는 쪽 사전 조건은 더해지고 합법 수에도), G317(감싸는 쪽 블록은 `require`와 `이름 = 식`만), G318(원래 것부터 안쪽에서 바깥쪽 감싸기로 모두 평가. 처음에 "UI에는 처음 실패 하나만 보인다"고 잘못 말했다가 코드를 보고 고침: 사전 조건은 모두 평가해 목록을 돌려줌), G319(`acts_for`·`free`·id는 원래 것), G320(감싸는 쪽 본문은 커맨드 본문의 규칙, 원래보다 넓을 수 없음은 적용 안 함), G321(`original(...)`은 낸 인자 그대로 많아야 한 번, 원래 본문은 사전 조건의 지역 변수 그대로). G313은 일부 바뀜.
- **구현:**
  - 검사: `FunctionChecker`가 `@wrap`에도 사전 조건 블록을 열고 `choose`·`cost`는 `CMD002`. `WrapCheck`가 커맨드 대상을 받아 `CommandWrap`(UI 상태 `CMD007`, `original`을 담기·식 안 호출 금지, 호출 인자는 매개변수 이름 그대로 `CMD001`), 함수 대상인데 사전 조건 블록이 있으면 `CMD002`. 통과하면 `FunctionValue.WrapsCommand`(`RunsAsCommand`).
  - 실행: `Session.Command`가 원래 커맨드의 블록을 평가한 뒤 감싸기마다(안쪽부터) 프레임을 만들어 첫 인자로 `InnerFunction(대상, 층, 바로 안쪽의 프레임)`을 주고 블록을 평가(앞에서 실패했으면 뒤의 실행 오류는 "평가 안 됨"), 본문은 맨 바깥부터. `original(...)`은 `PushInnerCommand`가 인자를 사전 조건 때 값과 견주고, 미리 검사한 프레임의 사본을 본문부터 쌓으며, 부른 쪽의 안쪽 함수를 `AsUsed()`로 바꿈(두 번째는 실행 오류).
  - 저장: 커맨드 감싸기 프레임은 `"inner"`에 아직 안 부른 안쪽 프레임의 지역 변수(그 안쪽도 이어서, 함수 이름·해시 포함)나 `{"used": true}`. 불러올 때 `RestoreInner`가 지금의 감싸기 순서와 해시를 견주어 되살림. `Relink`는 안쪽 커맨드·감싸기 프레임을 쓰기 모드로.
- **테스트:** 새 `CommandWrapTests` 20개(조건 더하기, 합법 수, 순서와 모두 보고, 본문 앞뒤, 겹침 순서, 안 부르기, `pass` 커맨드 감싸 쓰기, 사전 조건 지역 변수 유지, 두 번 부르기, 인자 바꾸기, 원래 커맨드 전·안에서 멈춘 채 저장·불러오기, 로드 오류 7가지, 키워드 인자). `WrapTests`의 "커맨드는 못 감쌈" 경우를 "함수 감싸기의 사전 조건 블록"으로 바꿈. 모두 2506개 통과.

### 2026-10-25: 판데믹 brink의 Government Interference (커맨드 감싸기의 첫 사례)

- **규칙서:** 변종 큐브가 있는 도시를 떠나려면 그 차례에 먼저 그 도시의 변종 큐브를 하나 이상 Treat해야 한다. 코어에서 떠나는 커맨드는 `Move`·`DirectFlight`.
- **샘플:** brink 스키마에 `Interference(city, turn)`를 `Player`에 `extend`. `@wrap(pandemic.Treat) note_treat`가 원래 커맨드를 마친 뒤 변종이면 도시와 차례 번호를 적고, `@wrap(pandemic.Move)`·`@wrap(pandemic.DirectFlight)`가 `require(may_leave(player), text("brink-treat-first"))`를 더함. 차례 번호로 적어 차례가 바뀌면 저절로 무효(차례 끝 감싸기 불필요). 번역 en·ko.
- **안내서:** 3장 감싸기 절에 "커맨드 감싸기" 소절(이 코드를 예시로), 관례 문단을 "함수와 커맨드의 경계에서만 끼어든다"로 고침, 앞 예시 `complex_cure`를 지금 코드로.
- **테스트:** `BrinkTests` 4개 더(Treat 전에는 거절·합법 수에서도 빠짐·Treat 뒤 이동, 직항도 막음, 앞 차례의 Treat는 무효, 다른 질병·빈 도시는 상관없음). 모두 2510개 통과.

### 2026-10-25: 판데믹 brink의 Hidden Pocket

- **규칙서:** 이 카드의 Intensify 전에, 변종이 박멸됐고 감염 버린 더미에 변종 카드가 하나라도 있으면 박멸을 되돌리고(치료제는 그대로) 그 카드들의 도시마다 큐브 하나.
- **샘플:** brink `hidden_pocket()`을 `@wrap(pandemic.intensify)`의 즉시 효과 자리에서 부름(Uncounted Populations와 같은 자리). 큐브는 `pandemic.add_cube`로 놓아 역할의 막기를 따름. 박멸 `@when`은 큐브가 생겨 다시 걸리지 않음. 새 엔진 구멍 없음. 번역 en·ko.
- **테스트:** `BrinkTests` 2개 더(되돌리고 버린 더미의 도시에만, 버린 더미에 변종 카드가 없으면 그대로). 모두 2512개 통과.

### 2026-10-25: 판데믹 brink의 Unacceptable Loss (변종 바이러스 여덟 카드 끝)

- **규칙서:** 이 카드의 Intensify 전에 변종 큐브 4개를 공급에서 상자로(4개 미만이면 모두).
- **샘플:** brink 스키마에 `Boxed(cubes)`를 `Disease`에 `extend`. `unacceptable_loss()`가 즉시 효과 자리에서 `min(4, 남은 공급)`만큼 늘리고, `@wrap(pandemic.cube_supply) boxed_supply`가 코어의 `derived` 함수를 감싸 그만큼 뺌(G313: `derived`에 걸린 함수도 감쌈). 공급이 0 밑이면 지는 것은 코어 규칙 그대로. 새 엔진 구멍 없음. 번역 en·ko.
- **테스트:** `BrinkTests` 2개 더(넷을 빼고 변종만 줄어듦, 남은 것이 넷보다 적으면 그만큼만). "그다음 큐브로 진다"도 넣으려 했으나 그 판은 상자 없이도 연쇄 폭발로 져서 효과를 가려내지 못해 뺌. 모두 2514개 통과.

### 2026-10-25: 몰래 보기 (G322)와 판데믹 brink의 트러블슈터

- **결정 G322:** G315가 미룬 (4) "한 번 보여 준 사건"은 엔진 기능을 두지 않는다. 모드가 누가 아는지를 상태에 적고 `@view_rule`이 그것을 봄(고른 것: 상태 + 보기 규칙; 버린 것: 엔진의 일회 `reveal` 메시지, `ask(data=)` 빌려 쓰기).
- **규칙서 (역할 카드):** 트러블슈터는 차례를 시작할 때 감염 덱 위에서 감염 속도만큼 카드를 보고 같은 순서로 되돌려 놓는다. 직항을 할 때 쓴 도시 카드를 보여 주고 버리지 않는다.
- **샘플 코어:** 차례 시작 함수 `begin_turn(g, p)`를 빼고 행동 채우기를 차례 끝에서 그리로 옮김(처음에는 빈 `pass`로 두었더니 쓰는 감싸기가 G311의 "원래보다 넓을 수 없음"에 걸림, 검사가 옳음). 그래서 차례를 마친 플레이어의 남은 행동은 다음 차례까지 0(`PandemicTests` 하나, 제너럴리스트 테스트 하나 고침). 콘솔 `infection_deck`은 버린 더미를 "Face가 보이는 카드"가 아니라 `pile == 1`로 고르고(아는 위 카드가 섞여 들지 않게), 이 자리가 아는 위 카드를 `known`으로 위부터.
- **샘플 brink:** 스키마 `component("Known", key=Ref["Player"])`를 `InfectionCard`에 `extend`(자유 리스트 필드는 G129로 닫혀 있어 엔티티 키 줄로 적음, 새 엔진 구멍 없음). `@wrap(pandemic.begin_turn)`이 위 카드에 줄을 붙이고, `@view_rule(Face)`가 아는 사람에게 `True`, 아니면 `None`. 잊기: `draw_infection`·`draw_infection_bottom`을 감싸 뽑은 카드의 줄을 지우고, `reshuffle`을 감싸 모두 지움. Intensify는 뽑을 더미의 순서를 바꾸지 않아 그대로 앎. 직항은 `@wrap(pandemic.DirectFlight)`가 원래 본문 뒤 카드를 손에 되돌림(이 예제의 손패는 공개라 보여 주기는 할 일 없음).
- **테스트:** `BrinkTests` 5개 더(위 두 장만 그 사람에게 보이고 순서 그대로, 화면의 `known`, 뽑으면 잊음, Intensify를 지나도 앎, 직항 카드를 지님). 처음 "뽑으면 잊음" 테스트는 밥의 뽑기에 에픽이 나와 아는 카드가 뽑히지 않아 실패했고(옳은 동작), 에픽을 빼고 따로 Intensify 테스트를 둠. 뽑기 감싸기에서 `forget`을 빼면 그 테스트가 실패함을 확인. 안내서 7장에 "몰래 보기" 절. 모두 2519개 통과.

### 2026-10-25: 문서 따라잡기 (spine-spec에 G267~G322)

- spine-spec이 G266 뒤로 G315만 반영돼 있었다. 각 절에 짧게 더함: S2에 호스트 함수를 `Cond`·`Action` 칸 값으로(G291·G292), 이름 쓰는 원칙(G306~G308·G293), `defdata`·`defs`(G303~G305), 다른 모드의 이름과 `@wrap`·커맨드 감싸기(G309·G311~G321). S3의 `query("Unit", "Wounded")` 예를 맨 이름으로. S4에 기여 두 갈래(G294~G302). S5에 즉시 핸들러(G310)와 "남의 동작은 감싼다". S6의 열린 질문 중 `@ui_action` 저장소를 닫음 표시. S9에 `engine stages`(G289). S10 3·4항에 `@ui_action`(G276~G286)과 `Call`·영수증(G245~G248·G290). X1에 `option`·`engine.pick`·예약 id `engine`(G271~G273). X4에 번역의 이어 정한 것(G267~G275·G287). 숨긴 정보 두 곳에 몰래 보기(G322).
- 안내서: 9장 "아직 없는 것"에서 낡은 줄 "다른 모드의 함수 부르기"(G309로 됨)를 지움. 4장에 커맨드 감싸기로 가는 한 줄.

### 2026-10-26: Unity 호스트로 가는 결정들 (G323~G328)

- 큰 후보 중 S10(호스트 엔진 결정과 묶임)을 고름. 첫 호스트 Unity(G323). Unreal은 C#을 바로 부를 수 없어 별도 프로세스 다리를 논의하다 Unity 연결부도 같은 일을 한다는 사용자 지적에서 엔진 무관한 경계(G324), 연결 세션·운영 둘(G325), 값 모양(G326), 비동기(G327), DLL 패키지(G328)를 차례로 정함. 커널 크기(파일 172개, 38,474줄, 빈 줄·주석 빼고 약 30,500줄)를 재고 러스트였다면 어땠을지도 이야기함(결론: 첫 호스트가 Unity라 C#이 맞음, 다른 엔진은 다리로).
- 실수: 영수증·UI 파일을 새 문제처럼 꺼냈다가 사용자가 "이미 이야기했던 것 같다"고 짚음. G244·G246·G249에 이미 있었다(닫힌 영수증, UI 파일은 호스트의 UXML/USS). 영수증은 경계에 맞게 구현만 하고, 번역도 G269·G270대로 호스트 쪽이라 묻지 않고 구현하기로 함.
