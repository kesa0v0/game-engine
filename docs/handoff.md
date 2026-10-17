# 인계 메모: 커널(로딩·런타임·커맨드·Flow·반응·시간·숨긴 정보·저장) 구현 현황

마지막 갱신: 2026-10-17. 브랜치 `spec-v0.5-scaffold`, 테스트 1742개 통과(커널 1714, CLI 28). 작업 규칙은 저장소 루트의 `CLAUDE.md`.

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
| **S7 호스트 함수** | 결정 G215~G222, ① 등록과 서명 검사 구현 → 지금 하는 것 |
| 규모 벤치(spec 9절), X4 번역 파일·포매터, S10 UI 구독 API(호스트 엔진 미정) | 없음 |

결정은 `docs/spec-gaps.md`의 G1~G214. 아래 절들은 슬라이스마다의 자세한 기록이다(날짜순). 처음 슬라이스들의 기록:

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

## 다음 할 일 (사용자와 고를 것)

**지금 하는 것: S7 호스트 함수 (G215~G222 결정).** 구현 순서는 ① 등록과 서명 검사 ② 스크립트에서 이름으로 부르기(소유 모드에 의존한 모드만, 모드 최상위 이름이 다른 모드 id와 같으면 오류) ③ 접근기와 실행 중 World 직접 쓰기 막기 ④ 오류 RUN019·RUN020과 실행 예산 ⑤ 함수 값 저장 `{"host": ...}`과 호스트 정보 ⑥ 서명 내보내기 파일과 CLI `--host`·스텁 ⑦ 분석기 안내(G220).
- **① 했음 (커밋 전):** `Host/HostFunctions.cs`(`host.For("core").Pure(...)`/`Writes(...)`, 인자 0~8개 타입 있는 람다 오버로드는 `HostOverloads.cs`, 등록 순간 `def` 한 줄 문법·경로·같은 모드 안 중복·인자 개수·이름(`maxSteps`=`max_steps`)·C# 타입(G222)·반환 타입을 보고 `ArgumentException`), `Parser.ParseHostHeader`(점 경로 이름, 기본값 허용, 반환 타입 필수, 본문 없음), `Host/HostSignatures.cs`(②b에서 소유 모드의 스키마·가시성으로 타입과 기본값을 풀고 틀리면 소유 모드의 `HST001`로 소유 모드와 의존 모드가 빠짐, 소유 모드가 없으면 건너뜀), `ContentLoader.Load(..., host)`와 `ContentLoadResult.HostFunctions`. `CommandSignatures.Reader`·`Defaults`를 진단 코드와 빈 스코프를 받게 열었다. 인자가 8개를 넘는 함수의 이름으로 꺼내기(`call.Int("max_steps")`)는 ②에서 호출과 함께 한다. 테스트 `Loading/HostFunctionTests`(53개). 이름 대조와 로드 해석을 끊으면 실패하는 것을 확인했다.
- **② 했음 (커밋 전, ④도 함께):** 로드: `Script/HostNames.cs`(모드마다 보이는 호스트 이름, 소유 모드가 자신이거나 필수 의존), `FunctionNameChecker`가 `core.path.find` 줄을 끝까지 풀어 없는 이름은 `EVL` + "혹시 …?", 이름 공간을 값으로 쓰거나 함수에 속성을 붙이면 `HST002`, 의존하지 않은 소유 모드는 `SCH003` + 의존 추가 안내, 모드 최상위 이름이 이름 공간을 가리면 `HST002`, 지역 변수로 가리는 것은 그냥 지역 변수. `FunctionEffects`가 `Writes` 호스트 함수를 쓰는 내장처럼 판정(식 안이면 `CMD`, 부른 함수는 쓰기 함수). 등록: 한 경로가 함수이면서 이름 공간이면 등록 오류, 소유 모드 id가 런타임 내장 이름이면 `HST001`. 실행: `Script/HostCallable.cs`(실행기는 자리 규칙만), `Runtime/World.Host.cs`(소유 모드 id마다 이름 공간 값을 런타임 내장에 넣음, 인자는 스크립트 함수처럼 묶고 커맨드 인자 검사 `Fit`, C# 값으로 바꿔 `DynamicInvoke`, 예외는 `RUN019`이고 `InnerException`이 `WorldException`까지 이어짐, 반환은 서명대로 검사해 어긋나면 `RUN020`, 엔티티는 리스트 안까지 따라갈 수 있는 모양으로). `Writes` 함수는 아직 기능 없는 `HostAccess`를 받는다(③). 테스트 `Loading/HostFunctionTests`(69), `Runtime/HostCallTests`(15). 실행기의 쓰기 자리 검사와 로드 가시성 검사를 끊으면 실패하는 것을 확인했다.
- **②에서 남긴 것:** 인자 8개 초과용 이름으로 꺼내기(`call.Int`), 콘텐츠 파일 최상위에서 호스트 함수를 값으로 참조(G221, 지금은 "이름 없음" 오류. Def의 `Cond`·`Action` 칸은 스크립트 함수만 받으므로 따로 정해야 함).
- 그다음 후보는 규모 벤치(호스트 함수가 생긴 뒤에 재야 실제에 가깝다).

지금까지의 기록(날짜순): 반응 ①(G145~G153)이 끝났고 시간 슬라이스 ①(틱·`Advance`·`Tick`·`now()`, G154~G156)과 ② `wait`(G157·G158)를 했고 ③ 마감(G159~G162, 커밋함)과 ④ 대상 소멸(G163~G166)과 스크립트의 `advance`(G167)·호출당 상한(G168)을 했다(커밋 `7b0d5c9`). 반응 ② `when`과 정착(G169~G172), `repeat`·`cooldown`·`once`(G173~G175)를 했다(커밋 `e9a65d6`). S5 열린 질문 G176·G177과 "읽은 칸이 바뀔 때만 재평가"를 했다(커밋 `f44f472`). `field`가 `derived` 상한을 넘는 경우(G99의 패턴 B 확인, 문서 갱신)를 했다(커밋 `d4703fe`). S5 열린 질문은 끝났다. 적합성 점검 뒤 난수(G178~G182)를 했다(커밋 `d908c91`). `cost`·`pay`·`choose` 슬라이스(G183~G188)를 했다(커밋 `52b97e6`). 검증 시나리오 ③을 런타임 테스트로 했다(커밋 `9584650`). 컴프리헨션(G189~G191)을 했다(커밋 `fcf766d`). 전역 순회 `query`(G192·G193)를 했다(커밋 `f4a6902`). 스크립트 순회의 G88(G194)을 고쳤다(커밋 `0e924a6`). 검증 시나리오 ①과 `while`(G195)을 했다(커밋 `68a4398`). 검증 시나리오 ②와 보기 정책·거른 읽기의 첫 조각(G196~G203)을 했다(커밋 `427345c`). 거른 결과 `Show`(G201·G204)와 `LegalMoves`·`Candidates`(G202)를 했다(커밋 `12d1872`). 거른 `Explain`(G205)을 했다(커밋 `780e595`). 검증 시나리오 ④를 코어 수정 없이 런타임 테스트로 했다(`ScenarioRealtimeTests`). 이로써 시나리오 ①~④가 다 됐다. 거른 `Advance` 결과(`Show`, `BlockedBy`는 답할 수 있는 질문만, `Blocked`는 그대로)도 정해 구현했다(G206, 커밋 `d987345`). 로직용 `.pyi`(G207·G208, 커밋 `09da3e8`)와 모드별 스키마 스텁(G209, 커밋 `ad362ba`)을 했다(아래 남은 일 2). 적합성 기준 C1~C10을 점검했다(spine-spec 7절, C9의 `Respond` 테스트만 더함, 커밋 `eb04421`). S8 칼럼 삭제는 고아 영역 대신 "기본 거부, 호스트가 허락하면 버리고 경고"로 정해 구현했다(G210, 커밋 `4b5f955`). Def 삭제도 같은 옵션으로 "불러올 때 파괴"로 했다(G211, 커밋 `21ef858`). 모드 내용 해시(G212, 엔진은 파일별 지문이라는 사실만, 무시할지는 사용처)와 그 전제인 파일 이름 NFC 정규화(G213, 재현한 자소 분리 버그)를 했다. 이어 "Flow 취소 후 로드"(G214, 호스트 옵션 `cancelBrokenFlows`, `SAV006`)를 했다(G212~G214 커밋 `012f5f4`). S8에 남은 것은 `migrate`다.

그 밖의 후보:

- 규모 벤치(spec 9절): 엔티티 1천·1만·10만에서 읽기, `derived` 추적, 쿼리, 커맨드 롤백 비용.
- X4 로컬라이제이션(번역 파일 형식·폴백·포매터, spec은 "v1 직전 재확인").
- S10 UI 구독 API(호스트 엔진 결정과 묶임).
- S8 `migrate`(실제 모드 업데이트 사례가 생기면).
- 아래 "남은 일"의 작은 것들.

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

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 1742개 통과(커널 1714, CLI 28)를 확인한다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(끝의 G109~G135), `docs/spine-spec.md` S2·S4·S6·7절(C7·C8)을 읽는다. 커맨드 코드는 `Runtime/World.Submit.cs`(제출), `Runtime/World.Commands.cs`(인자·사전 검사), `Runtime/World.Undo.cs`(롤백), `Runtime/World.Structure.cs`(구조 변경 내장), `Script/Execution/PureEvaluator.cs`(`RunCommand`, `Frame.Writable`), `Script/Execution/FunctionEffects.cs`, `Loading/CommandSignatures.cs`다.
3. 위 "다음 할 일 (사용자와 고를 것)"에서 사용자와 다음 항목을 고른다.

## 메모: 저장소 구조와 속도 (2026-10-16, 논의만, 결정 아님)

- 지금 월드 저장은 ECS식 struct 배열이 아니다. `World`가 `Dictionary<long, EntityRecord>`와 엔티티마다 `Dictionary<MemberDecl, Value>`(바뀐 값만), 줄은 `SortedDictionary`, 색인은 `SortedSet`으로 들고 있다(결정론 때문에 정렬 컬렉션). 값은 `Value` 객체다.
- 스크립트 `@on("tick")` 핸들러가 System 자리이고, 엔티티 순회는 본문이 직접 한다.
- 저장 접근이 `World.cs`·`World.Derived.cs`·`World.Save.cs`·`World.Commands.cs`·`World.Explain.cs` 다섯 파일에 있다. 저장소 인터페이스는 아직 없다(spec P6은 "인터페이스 뒤 격리"). 단순 맵 교체는 쉽지만 컴포넌트별 배열로 가려면 값 표현·undo·역방향 색인·저장 불러오기를 함께 건드려야 한다.
- 벤치가 아직 없어 병목이 저장인지 인터프리터인지 모른다. 저장을 바꾸기 전에 임시 벤치(엔티티 N개를 `tick`에서 갱신)로 먼저 재고, 저장이 병목이면 인터페이스 추출 → 교체 순으로 한다.
