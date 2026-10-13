# 사용 사례집 (Use Cases)

`spine-spec.md`의 결정이 **어떤 실제 상황에서 나왔는지**를 남기는 문서다. spec은 도메인 중립을 유지하고, 구체적인 게임 용어와 이전 구현은 여기에만 쓴다.

- spec은 이 문서를 사례 ID(`U1` …)로만 인용한다. 이 문서의 내용은 요구가 아니다.
- "이전 처리"는 이전 엔진과 게임에서 **그때 그렇게 했다**는 기록일 뿐이다. 맞는 방법이었다는 뜻이 아니다. 코드는 가져오지 않는다.
- 출처의 게임들은 모두 미완성 프로토타입이다. "이런 생각을 했다" 정도로만 읽는다.

## 출처

| 약칭 | 경로 | 성격 |
|---|---|---|
| ENG | `E:\Project\Game\SharedPackages\uk.kesa.engine` | 이전 엔진 (Unity, YAML Def, Effect/Condition 객체, UniTask 비동기) |
| TR | `E:\Project\Game\TurbulentRepublic` | 바이마르 공화국 카드 기반 다인 정치 게임. 라운드·페이즈, 대응 스택, 주사위 |
| WG | `E:\Project\Game\Wargame` | 턴제 진영 워게임. 페이즈·스텝 파이프라인, 차례 권한, 헤드리스 봇 |
| QU | `E:\Project\Game\Quarantined` | 봉쇄 도시 경영·정치 (WeGo 턴). 팝, 파벌, 이벤트 팝업, 물류, ECS |

---

## U1. 자기 것만 조작한다
- **상황:** "자기 유닛만 움직일 수 있다", "AP를 1 써야 한다" (WG 이동·후퇴, TR 시위)
- **이전 처리:** 커맨드 조건에 `Condition_IsOwnedBy { target: ^Subject, expectedOwner: ^Self }`, `Condition_CompareResource { target: ^Self, resource: AP }`. **`^Self`는 커맨드를 보낸 issuer 자신인 `Instance`(진영 엔티티)였다** (ENG `CommandEnvelope.IssuerId`는 `Instance.UniqueId`, `CommandProcessor.SubmitCommandAsync(Instance issuer, …)`).
- **교훈:** spec 예제의 `me.Points`, `u.owner == me`는 이 `^Self`를 옮긴 것이다. 이전에는 issuer가 엔티티라서 문제가 없었다. spec S6이 issuer를 불투명한 문자열로 바꾸면서 이 둘이 어긋났다.
- **spec:** S2 `me` 메모(❓), S6 권한 3층.

## U2. 차례 권한
- **상황:** 지금 차례인 진영만 명령을 낼 수 있다. 유닛이 보낸 명령도 그 유닛을 소유한 진영의 차례일 때만 받는다 (WG).
- **이전 처리:** `SessionCommandRouter.RouteCommandAsync`가 세 단계로 검사했다.
  1. 현재 스텝이 `acceptsCommands`인가
  2. issuer가 활성 행위자이거나 그 **소유 체인**(`OwnershipHelper.IsOwnedBy`) 안에 있는가
  3. 커맨드의 도메인 조건 (`CanExecute`)

  시스템 커맨드(`isSystemCommand`)는 1·2를 건너뛰었다. 이와 별도로 커맨드 조건에도 `Condition_HasTurnAuthority`를 넣어 UI 비활성 표시에 썼다. 그래서 같은 검사가 두 군데에 있었다.
- **교훈:** spec S6 권한 3층(인증 → 허용 목록 → `preconditions`)의 원형이다. spec은 "차례"를 특별한 개념으로 두지 않고 Request의 `accepts` 목록으로 일반화했고, "본문에 권한 검사를 반복하지 않는다"고 정했다. 이 결정은 위의 중복 검사에서 나왔다. 다만 **소유 체인**(유닛 → 진영)을 issuer 검사에 쓰던 요구는 spec에 아직 자리가 없다.
- **spec:** S6, X1 `accepts`, S2 `me` 메모.

## U3. 턴·페이즈·스텝 파이프라인
- **상황:**
  - WG: 턴 시작(진영별 AP·영향력 수입) → 행동 페이즈(주도권 결정 → 진영마다 차례: 행동하다 `Command_EndTurn`으로 종료) → 분쟁 해소 → 라운드 정리.
  - TR: 라운드 1~6마다 페이즈 목록(`Phase_Init → AgendaSelection → Impulse → Calc`)이 같다. 라운드 진입 효과만 다르다(안건 추가, 덱 교체). 이 차이는 `RoundDef`의 상속으로 표현했다.
- **이전 처리:** WG `TurnPhaseDef`와 `FlowStepDef`는 `runAfter`로 **위상 정렬**했다. 스텝은 `iterator`(진영 목록)를 돌며 하위 스텝을 실행하고, `Effect_WaitForAny`로 종료 커맨드를 기다렸다. 스텝을 반복할 때마다 `AdvanceTickAsync(1)`을 불렀다. 그래서 턴제에서 틱은 "논리적 진행 카운터"였다. TR `Comp_GameFlow`는 페이즈의 enter/exit 효과를 해소 스택에 넣고 스택이 빌 때까지 기다렸다.
- **교훈:** 턴 구조는 게임마다 달랐고 엔진 쪽 TickManager와는 별개였다. spec X2의 "턴 관리는 게임 쪽 코드", X1의 "페이즈 관용구는 표준 모드"가 여기서 나왔다. WG는 스텝 순서에 `runAfter`를 썼고, ENG `TriggerTopologyResolver`도 위상 정렬을 썼다. `runAfter`는 **다른 모드가 남의 턴 흐름에 스텝을 끼워 넣는** 수단이기도 했다.
- **결정 (2026-10-12):** 처음 spec 초안은 위상 정렬을 빼고 정수 `order=`를 두었다. 그러면 다른 모드의 숫자를 알아야 끼어들 수 있어서, 이름 있는 단계(`stage`)의 위상 정렬로 바꿨다(spec S5). 턴 루프는 표준 모드의 Flow가 신호를 내고, 다른 모드는 그 신호의 단계에 핸들러를 붙여 확장한다.
- **spec:** X1, X2, S5 순서, X3 `std.*`.

## U4. 저장 후 재개
- **상황:** 차례 중간이나 선택지가 떠 있는 상태에서 저장하고 불러온다.
- **이전 처리 (문제가 있었음):**
  - WG `TurnManager.ResumeFromStateAsync`는 (턴 번호, 페이즈, 진영 인덱스, 스텝)만 저장했다. 재개하면 **페이즈의 스텝을 처음부터 다시 실행**했고, 진영 인덱스만 건너뛰었다. 이미 끝난 스텝의 효과(수입 등)가 다시 실행될 수 있었다.
  - TR `Comp_ResolutionStack`은 `PendingChoice`(떠 있는 선택지)를 저장했다. 하지만 그 선택지를 기다리던 비동기 실행(`await`)은 저장 대상이 아니었다. 그래서 불러온 뒤에는 선택 이후를 이어 가지 못했을 것으로 **추정**한다(코드에서 읽은 것이고, 실행해 확인하지는 않았다). 스택 노드의 `Issuer`는 불러올 때 `null`로 복원되었다(코드상 확인).
- **교훈:** spec S2의 "명시적 프레임 스택 + 저장 = 프레임 전체", C1(어느 대기 지점에서든 저장·재개), C3(반복 중간에서 재개해도 끝난 단계를 다시 실행하지 않음)이 정확히 이 실패에서 나왔다.
- **spec:** S2 평가 모델, S8, C1, C3.

## U5. 대응 스택과 우선권
- **상황:** TR. 한 정당이 행동을 내면 스택에 올라간다. 정당들이 차례로 "통과"하거나 "대응"(새 효과를 올림)할 수 있다. 모두 통과하면 맨 위부터 해소한다. 대응이 아래 노드를 취소할 수 있고, 취소되면 비용(OP, 모디파이어)을 돌려준다. 해소 도중에도 선택지가 뜬다.
- **이전 처리:** `Comp_ResolutionStack`
  - 노드 = (issuer, 효과, 환불 효과, 환불 OP, 환불 모디파이어, **컨텍스트**). 컨텍스트는 키 → 값 맵이고, 저장 시 `Dictionary<int, ISavedContextValue>`.
  - 우선권 루프: 시작 위치는 `StartMode`(Issuer / Next / Previous)로, 방향은 `Direction`(Forward / Backward)으로 정한다. 각 정당의 `Cmd_PassPriority`와 "새 노드 push" 중 먼저 오는 것을 기다린다.
  - **한 번 해소가 시작되면 남은 스택은 우선권을 다시 묻지 않고 연쇄로 해소했다.**
  - `PeekUnder(depth)`로 아래 노드를 찾아 취소했다.
- **교훈:** 검증 시나리오 ①과 `std.stack`의 원형이다. X3의 "`std.stack`의 정책(우선권이 어디서 시작하는지, 몇 번 연속 통과하면 해소하는지, 반응 후 재시작 여부)은 설치 인자나 문서에 드러나야 한다"는 문장은 위의 `StartMode`·`Direction`과 "연쇄 해소" 같은 숨은 정책에서 나왔다.
- **추정:** spec S2 표의 "행의 값 칼럼 — 맵 ❓"(옛 메모 "행 값에 맵 타입이 필요하다")는 **스택 노드의 컨텍스트 맵**을 행으로 표현해야 했던 데서 나왔을 가능성이 크다. 확인이 필요하다.
- **spec:** 7절 시나리오 ①, X3 `std.stack`, S2 리스트·맵 표.

## U6. 해소 도중의 선택과 대상 지정
- **상황:** TR 시위 주사위가 1이 나오면 "거점 제거 / 유닛 철수" 중 하나를 고른다. 7 이상이면 "거점 2개 / 안건 이동"을 고르고, 안건 이동을 고르면 이어서 안건과 목적지(거리 3 이내)를 지정한다.
- **이전 처리:** `Effect_Choice { choosingParty, titleKey, options[{labelKey, effects}] }`가 `Cmd_SubmitChoice`(issuer별)를 기다렸다. `Effect_SelectTarget { targetKey, targetConditions }`는 `ValidTargetIds`를 미리 계산해 두고 대상을 기다렸다. 선택은 주사위 결과 분기 안에 **중첩**되어 있었다.
- **교훈:** spec S2 "`ask`는 값을 돌려주는 문장, 지역 변수는 그대로 이어진다"와 X1 Request(`options`, 허용 제출자)의 원형이다. 중첩된 YAML 효과 트리를 스크립트의 `if`/`match`로 펴는 것이 새 언어의 동기다.
- **spec:** S2 `ask`, X1, C2.

## U7. 동시 선택 후 공개
- **상황:** TR 안건 선택 페이즈(`Phase_AgendaSelection`). 각 정당이 안건을 고르고(`Effect_SetSelectedAgenda`) 모두 끝나면 일괄로 해소한다(`Effect_ResolveSelectedAgendas`).
- **이전 처리:** 선택이 정당 컴포넌트의 `SelectedAgenda`에 바로 기록되었다. 다른 정당의 선택을 숨기는 장치는 확인하지 못했다. 미확인이며, 로컬 핫시트 전제였을 것으로 보인다.
- **교훈:** 검증 시나리오 ②의 원형이다. 이전에는 비공개 문제를 구조로 풀지 않았다. spec 9절 2항(가시성)이 열린 이유이기도 하다.
- **spec:** 7절 시나리오 ②, 9절 2항.

## U8. 예약 실행 (WeGo)
- **상황:** QU는 WeGo 턴이다. 모두가 명령을 예약하고, 턴이 끝나면 페이즈(행정 → 경제 → 정치 → 통계) 순서로 일괄 처리한다.
- **이전 처리:** ENG `CommandProcessor.TryQueueCommand`는 issuer 엔티티의 `Comp_CommandContainer`에 명령을 쌓았다(`CommandDef.ExecutionPhase`). 페이즈가 되면 모든 컨테이너에서 꺼내 `Priority`(내림차순) → `QueueIndex`(오름차순)로 정렬해 실행했다. 페이즈 그룹은 ECS `ComponentSystemGroup` + `UpdateAfter`로 구성했다.
- **교훈:** spec S6 "예약 실행 큐 없음(엔진 수준). 지연 실행은 게임이 `Order` 같은 상태 데이터로 만든다"는 이 컨테이너를 엔진 밖으로 뺀 결정이다. 저장되는 예약은 명령 객체가 아니라 상태 데이터가 된다.
- **틱 안의 C# 작업:** QU는 틱 안에서 무거운 C# 작업(ECS 경제 페이즈, AI 계획 tickable, 우선순위 1500의 트리거 시스템)을 스크립트 단계와 섞어 돌렸다. 새 spec에서는 `on("tick", stage=…)` 핸들러가(행정 → 경제 → 정치 → 통계를 단계로) S7 호스트 함수(효과 등급: 엔티티 쓰기)를 부르는 방식으로 같은 순서를 표현할 수 있는 것으로 보인다. 대량 연산의 비용과 접근기 제약은 9절 벤치 항목에 걸린다.
- **spec:** S6, S7, X2.

## U9. 주사위 판정표
- **상황:** TR. 주사위 N개(스탯에서 옴) + 보너스를 굴려 범위별 결과(1 / 2–4 / 5–6 / 7+)를 낸다. QU는 분대 전투에서 가중치를 준 주사위의 최대값을 쓴다.
- **이전 처리:** `Effect_RollTable { diceCount: $Target.Stats[DiceCount], bonus, evalMode: HighestWithMaxBonus, outcomes[{min,max,effects}] }`. 난수 시드는 ENG `DeterministicSeedManager.GetBaseSeedForTick(tick, Hash(defId))`였다.
- **이전 RNG의 문제:**
  - 시드가 (마스터 시드, 틱, 정의 id)로만 정해져서 **같은 틱에 같은 커맨드를 두 번 실행하면 같은 난수가 나왔다.**
  - WG `TurnManager`는 시드 솔트에 `string.GetHashCode()`를 썼다. 이 값은 .NET에서 **프로세스마다 다르다.**
  - 난수 상태가 커맨드 단위의 임시 컨텍스트에 있어서 롤백이나 재시도 개념이 없었다.
- **교훈:** spec P2(프로세스 무관 해시), C6, C10과 S2의 새 RNG 설계(Flow별 카운터 기반, 실행 횟수 키)가 이 문제들을 겨냥한다.
- **spec:** S2 난수, P2, C6, C10.

## U10. 강제 이벤트와 기본 선택지
- **상황:** QU 배급소 습격. 만족도가 낮은 정착지 하나를 무작위로 골라 팝업을 띄운다. 선택지마다 요구 조건이 있고(치안 ≥ 50, 식량 ≥ 500), "방관"이 기본 선택지다. **처리하기 전에는 턴을 넘길 수 없다.**
- **이전 처리:** `EventDef { isMandatory, presentationStyle: Popup, severity, queries[selector: Random], options[{requirements, effects, isDefault}] }`, `Condition_NoPendingMandatoryEvents`로 턴 종료를 막았다. 발동은 `TriggerDef { triggers: [OnTickPulse], conditions, cooldownTicks: 50 } → Effect_QueueEvent`였다. ENG `SystemEventTriggerSystem`이 틱마다(우선순위 1500) 이벤트 큐를 처리했다.
- **교훈:**
  - `isMandatory`는 spec X1/X2의 `blocks_time`으로 일반화됐다.
  - `isDefault`는 `on_timeout`으로 일반화됐다.
  - `presentationStyle`·`severity`가 UI 장르 요소라서 "Event는 커널에 두지 않는다(`std.events`)"가 되었다.
  - `TriggerDef`의 쿨다운·조건은 S5 `when` + `cooldown=`으로 옮겨졌다.
  - 이전 트리거는 조건을 **매 틱 폴링**했고(`OnTickPulse`), 쿨다운 상태만 저장했다(`MaxOccurrences` 카운트는 저장 안 됨).
- **spec:** X1, X2, S5, X3 `std.events`.

## U11. 모디파이어와 스탯
- **상황:**
  - QU 정책 모디파이어(기본소득 +20, 부유층 임금 −0.5)는 영구다. "증진된 위생관념: 위생 +10% (5턴 남음)"처럼 기간이 있는 것도 있다.
  - WG 분쟁 목표: 파괴 대상에게 "후퇴 불가" 표식을 달고 **목표가 사라지면 표식도 해제**한다.
- **이전 처리:** `ModifierDef { duration: -1, statChanges[{stat, type: Flat|Additive, value}] }`, `StatDef { defaultBaseValue, minValue, maxValue, statParts[StatPart_DataPath …] }`. WG는 `onRegisterEffects`/`onRemoveEffects`로 표식을 수동으로 달고 뗐다. 스탯 값의 출처 설명은 `StatExplanationEntry`로 했다.
- **교훈:** spec S4의 세 부품(`derived` 필드, 기여 행 `(owner, 대상, 연산, 값, 출처)`, 수명: 영구 / N틱 / 이벤트 / **출처 제거**)과 S9 "파생값 추적"의 원형이다. "출처 제거" 수명은 WG의 수동 해제를 엔진이 맡도록 한 것이다.
- **spec:** S4, S9 (2), `std.stats`.

## U12. 여러 단계 입력과 초안
- **상황:**
  - QU 물류 노선: 출발지 클릭 → 도착지 클릭 → 설정 창 → 확정.
  - WG 분쟁 목표: 지도에서 필터에 맞는 대상을 클릭해 고른다.
  - TR 시위: UI에서 OP를 추가로 냈는지(`PaidExtraOP`)를 넘긴다.
- **이전 처리:**
  - QU는 단계마다 별도 커맨드를 썼고(`Cmd_DraftRoute_SelectStart` → `SelectEnd` → `Cmd_EstablishLogisticRoute`), **초안을 시뮬레이션 상태(`Self.Draft.StartNode`)에 기록했다.** 이어지는 단계는 `Effect_EnterTargetingMode { nextCommandId }`로 연결했다.
  - WG는 `requiredSelections[{key, mode: MapClick, targetFilter, prompt}]`를 썼다.
- **교훈:** spec S6 "draft = 제출 전의 매개변수 값, 제출 전에는 엔진 상태가 아니다"와 `choose(…, where=…)`는 QU처럼 초안이 세이브와 네트워크로 새던 문제를 막는 결정이다. 다단계 UI는 `@ui_action`과 UI 로컬 상태가 맡는다.
- **spec:** S6 draft, S2 `choose`, S10 3항.

## U13. AI 참가자
- **상황:** WG 헤드리스 봇(`HeadlessBot`, `RuleBasedBotPolicy`, `PassTurnBotPolicy`), QU AI 계획·명령 tickable.
- **이전 처리:** WG `HeadlessBot`은 `FlowStepStartedEvent`를 구독하고, 그 스텝이 `acceptsCommands`이며 활성 진영이 자기일 때 `IBotPolicy.ExecuteTurnDecisionAsync`를 부른다. 명령은 사람과 같은 `ICommandReceiver`(`SessionCommandRouter`)로 보낸다. 정책 내부와 합법 수를 구하는 방식, QU 쪽 AI는 보지 않았다(미확인).
- **교훈:** spec "커맨드 = AI/네트워크/테스트의 기준점", `preconditions`로 합법 수를 나열한다는 결정, 시나리오 ③과 연결된다.
- **spec:** S6, S2 `preconditions`, 7절 시나리오 ③.

## U14. 텍스트 템플릿
- **상황:** "`{Target.TestPop.Wealth | format:N0} {Target.Label | josa:을} 획득했습니다.`", 이벤트 설명 "`{Target.Label}의 하층민 주민들이…`" (QU)
- **이전 처리:** 템플릿이 `DataPathResolver`로 **월드 경로를 직접 평가했다.** 키 규칙은 `l_` 접두사와 `_Desc` 접미사였다. 일부 Def는 번역 키 대신 한국어 문장을 그대로 썼다(`label: "배급소 습격 사건 …"`).
- **교훈:** spec X4 "템플릿은 호출 쪽이 넘긴 이름 있는 인자만 보고 월드 경로는 평가하지 않음", "조사(을/를)는 언어 팩의 확장 포매터", S2 "화면 문자열은 텍스트 키 타입만"이 이 둘을 고친다.
- **spec:** X4, S2 텍스트 키.

## U15. 진영별로 거의 같은 정의
- **상황:** TR `Action_Demonstration_KPD/SPD/Zentrum/DNVP`, `Reaction_GeneralAction_*`가 정당 이름(`PartyBase_KPD` 등)과 결과 몇 줄만 다르고 나머지는 복사되어 있다.
- **이전 처리:** `abstract` 부모 + `parentName` 상속. 그러나 효과 목록은 리스트라 통째로 교체되므로 결국 복사했다.
- **교훈:** 상속만으로는 "매개변수만 다른 정의"를 표현하지 못한다. 새 언어에서는 함수와 루프로 Def를 만들 수 있다(S2 "Def 생성 — 로드 시점"). 리스트 병합 방식을 필드별로 선언하는 것(S1)도 이 문제를 겨냥한다.
- **spec:** S1 상속·병합, S2 Def 생성 함수.

## U16. 패치
- **상황:** 다른 모드의 정의를 파일을 수정하지 않고 바꾼다.
- **이전 처리:** ENG `PatchOperation`은 YAML 경로에 대해 `Replace` / `Add`(리스트 끝에 추가, 맵은 키 병합·덮어쓰기) / `Remove`를 지원했다. 대상이 없으면 `false`만 반환했다. 수치 연산(`add`, `mul`)은 없었다.
- **교훈:** spec S1의 `set` / `append` / `remove`가 이를 이어받았다. 수치 `add`는 새로 생겼다. "대상 없음은 오류"와 필드별 변경 이력이 추가되었다. 적용은 연산 종류별 단계가 아니라 **로드 순서 하나**로 정해, 의존하는 모드가 마지막에 이기게 했다(spec-gaps G41). 서로 순서가 없는 모드의 겹치는 패치는 오류다.
- **spec:** S1 패치.

---

## 이전 엔진에 있었지만 spec이 척추에서 뺀 것

| 이전 구성 요소 (ENG) | spec에서의 위치 |
|---|---|
| `Job`, `Toil`, `WorkGiver`, `Designation`, `ReservationManager` (림월드식 작업) | 6절 제외: 작업 시스템(장르 모듈) |
| `SelectionManager`, `TargetingService`, `LensManager` | 6절 제외: 선택·지정·예약·렌즈 매니저 |
| `StatisticCacheManager`, `IStatisticBaker` | 6절 제외: 통계 캐시 |
| `BaseEcsComp`, Burst/Job, ECS 페이즈 그룹 (QU) | P4: ECS/Burst 의존 제외 |
| `AppRegistry` 정적 접근, MessagePipe 전역 이벤트 | P3: 전역 상태 금지 |
| `TickScheduler`, `ScheduledTask`, `Effect_Delay` | S5: 예약 API 대신 `wait`(자는 Flow) |
| R3 반응형 바인딩(`IBindablePathTarget`) | S10: 읽기 집합 기반 반응성, 6절: 반응형 UI 바인딩 제외 |
| `[SaveID]`, `TypeNameHandling.Auto`, `CommandTypeFullName` | S7: 세이브에 C# 클래스 이름 금지 |
| `ITickable` + `TickPriority`, `await` 중인 tickable이 틱 전체를 멈춤 | X2: `advance(1)` 순서, S5: `on("tick", stage=…)` + 단계 위상 정렬, `blocks_time` |

## 이 문서로 풀리거나 바뀐 spec 빈틈

- **`me`의 출처** (S2 메모): U1, U2. 이전에는 issuer가 엔티티(`^Self`)였고 소유 체인으로 권한을 검사했다. 새 spec은 issuer를 불투명한 인식표로 바꿨다. 남은 결정은 "규칙이 요청자 엔티티에 닿는 방법"과 "소유 체인 검사를 어디에 둘지"이다.
- **행의 맵 값** (S2 리스트·맵 표): U5 추정. 스택 노드 컨텍스트를 행으로 표현하려면 맵이 필요했다.
- **턴제에서 틱의 의미** (X2): U3. 턴제에서는 틱이 논리적 진행 카운터로 쓰였다.
