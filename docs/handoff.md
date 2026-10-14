# 인계 메모: 로딩 파이프라인, CLI, 런타임 World

마지막 갱신: 2026-10-14. 브랜치 `spec-v0.5-scaffold`, 테스트 710개 통과(커널 682, CLI 28). 작업 규칙은 저장소 루트의 `CLAUDE.md`.

## 한 줄 요약

spine-spec S1의 6단계 콘텐츠 로딩 파이프라인(①~⑥)과 CLI(`engine check`·`dump`·`stubs`), 런타임 데이터 층 `World`의 첫 슬라이스가 구현되었다. 결정은 `docs/spec-gaps.md`의 G34~G94에 있고, spec 본문(S1·S2·S4·S9, schema-design, use-cases U16)에도 반영했다. 다음은 순수 함수 실행기(`derived` 계산)다.

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
| 런타임 월드 | `Runtime/World`, `RuntimeTypes`(`EntityId`, `EntityValue`, `MemberHandle`, `Init`, `WorldException`) | G86~G93 |

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
- **아직 안 되는 것:** `derived` 읽기와 `default=함수`, `derived`를 원천으로 한 `default_from`은 RUN004(G90). 키 줄 안의 Ref는 역방향 색인이 없다. 저장(S8)·트랜잭션·벤치는 아직 없다. 내부 저장은 엔티티마다 바뀐 값만 맵으로 둔 단순한 모양이다(저장 방식은 공개 약속이 아님).
- G93은 로더 규칙도 바꿨다: `keys="all"` 줄의 `field`는 고정 기본값이 있어야 하고(`default_from`·함수 기본값 금지), Ref는 `optional`이어야 한다(SCH004).

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다. 런타임도 생성 순간에 구멍을 막는다(G93).
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## 남은 일 (사용자와 우선순위를 정할 것)

1. **순수 함수 실행기 슬라이스**(G90): `def` 본문 파싱, `int`·`fixed` 산술(P2), 비교, `if`·`for`·`return`, `floor`·`clamp`·`min`·`max`, `contribs`, read-set, 재진입 감지. 그다음 `derived`·`default=함수`를 World에 켠다. 기여 행의 모양·수명과 `field`가 `derived` 상한을 넘는 경우(S4 🟡)도 이때 정한다. `default_from`이 "생성 때 한 번만" 계산되는지(원천이 바뀌어도 따라가지 않는지)는 `derived`가 생긴 뒤 테스트한다.
2. **로직용 `.pyi`**: 엔티티 접근 클래스, `derived`, `ref` 칼럼, 함수 서명. 스키마 스텁의 `derived` 함수 매개변수 타입도 이때 좁힌다.
3. **스텁의 이름 충돌**: 멤버 이름이 `id`/`self` 같은 예약 이름이면 중복 인자가 생기고, Def 타입·컴포넌트 이름이 `Mod`·`use`·`use_all`·`patch`·`mod_loaded`·`inherit`이면 스텁 안에서 겹친다(G82 알려진 한계). 스키마 단계에서 막는 것이 유력하다.
4. S9의 나머지 "왜?" 도구 4가지(파생값 추적, 전제 조건 보고서, 반응 추적, 단계 순서 덤프). 각자 `engine` 명령이 될 자리다(G80).
5. **G32 규칙**(`Ref`=엔티티, `DefId`=Def)을 spec 본문으로 옮기는 일. 예시만 고쳐 둔 상태다.
6. 덤프의 `patches`가 부모 체인 전체를 훑어, 자식이 같은 값을 직접 적어 영향이 없었던 부모 패치도 나온다(G76 알려진 한계). `dump`에서 특정 Def만 고르는 옵션도 미뤘다(G80).
7. 속성 접근 표기(`Resource.Wood`, 따옴표 없는 Def 참조)는 후속 과제다(G63).
8. 진단 코드 체계(S9 열린 질문, G6 잠정).
9. 종료 코드 3(엔진 내부 예외) 경로는 테스트가 없다.

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 710개 통과를 확인한다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(끝의 G78~G94), `docs/spine-spec.md` S1·S4·S9를 읽는다.
3. 위 "남은 일" 중 하나를 고르고, 이전처럼 spec이 비어 있는 곳부터 하나씩 짚어 달라고 요청한다.
   예: "순수 함수 실행기 슬라이스를 시작하자. spec S2와 schema-design 3.3.1, spec-gaps G90을 읽고, 구현 전에 spec이 비어 있는 곳부터 하나씩 짚어 줘."
