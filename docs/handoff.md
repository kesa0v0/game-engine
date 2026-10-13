# 인계 메모: 로딩 파이프라인 슬라이스

마지막 갱신: 2026-10-13. 브랜치 `spec-v0.5-scaffold`, 테스트 648개 통과. 작업 규칙은 저장소 루트의 `CLAUDE.md`.

## 한 줄 요약

spine-spec S1의 6단계 콘텐츠 로딩 파이프라인(①~⑥)이 모두 구현되었다. 결정은 `docs/spec-gaps.md`의 G34~G77에 있고, spec 본문(S1·S2·S9, use-cases U16)에도 반영했다. 남은 일은 CLI와 로직 쪽이다.

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

흐름: `ContentLoader.Load`가 회차 루프를 돈다. ③ 이후에 모드가 비활성화되면 그 모드와 의존 모드를 빼고 ②a부터 다시 돈다(G49). 한 회차의 ③~⑥ 검사는 `ExcludeFailedAtPatchStage`에서 같은 `StageDiagnostics`로 모은다.

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다.
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## 남은 일 (사용자와 우선순위를 정할 것)

추천 순서의 첫 항목은 CLI다. 지금까지 만든 `CheckReport`·`DefDump`·`StubGenerator`를 실제 모드 폴더에 돌려 볼 첫 진입점이다.

1. **CLI `engine check mods/`**: 파일 읽기, 종료 코드, JSON 출력, 스텁 파일 배치, `pyrightconfig.json`(pythonVersion 3.12). 경고를 오류로 올리는 옵션(S9 열린 질문).
2. **로직용 `.pyi`**: 엔티티 접근 클래스, `derived`, `ref` 칼럼, 함수 서명. 엔티티 모델(S4)과 로직 슬라이스가 먼저다.
3. 스텁에서 멤버 이름이 `id`/`self` 같은 예약 이름이면 중복 인자가 생긴다(스키마 단계에서 막거나 나중에 처리).
4. S9의 나머지 "왜?" 도구 4가지(파생값 추적, 전제 조건 보고서, 반응 추적, 단계 순서 덤프).
5. **G32 규칙**(`Ref`=엔티티, `DefId`=Def)을 spec 본문으로 옮기는 일. 예시만 고쳐 둔 상태다.
6. **`check` 모드에서 `optional` 모드를 전부 설치로 볼지**(G38 열린 점).
7. 덤프의 `patches`가 부모 체인 전체를 훑어, 자식이 같은 값을 직접 적어 영향이 없었던 부모 패치도 나온다(G76 알려진 한계).
8. 속성 접근 표기(`Resource.Wood`, 따옴표 없는 Def 참조)는 후속 과제다(G63). 내부 값은 같은 id 문자열이라 ⑤는 바뀌지 않는다.

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 648개 통과를 확인한다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(끝의 G70~G77), `docs/spine-spec.md` S1을 읽는다.
3. 위 "남은 일" 중 하나를 고르고, 이전처럼 spec이 비어 있는 곳부터 하나씩 짚어 달라고 요청한다.
   예: "CLI(`engine check`) 슬라이스를 시작하자. S9와 spec-gaps를 읽고, 구현 전에 spec이 비어 있는 곳부터 하나씩 짚어 줘."
