# 스키마 설계

spec이 스키마에 요구하는 것은 여러 절에 흩어져 있지만, 스키마를 **어떻게 선언하는지**는 비어 있다. 이 문서에서 그 설계를 하나씩 정하고, 결론은 spec의 해당 절에 옮긴다(5절). 이유·사례·비교는 이 문서에 남긴다.

**상태 표기:** spec과 같다. ✅ 확정 · 🟡 잠정(추천안, 합의 전) · ❓ 미정.

---

## 1. 입력

### 1.1 spec이 스키마에 거는 요구

| 출처 | 요구 |
|---|---|
| S1 | 필드별 리스트 병합 선언(`merge="replace"` 기본, `"append"`). 컴포넌트 목록은 이름을 키로 병합. ⑥에서 스키마로 Def 검사, `.pyi` 생성. |
| S2 | 스키마에 없는 속성 대입 금지. Def 참조는 문자열 + `.pyi`의 `Literal[...]`. |
| P2 | 수치는 `int`·`fixed`. 저장 폭은 스키마의 범위 선언이 정하는 구현 세부. |
| S3 | 경로를 로드 시 스키마로 검증하고 접근자 체인으로 컴파일. 경로 세그먼트 = 컴포넌트 / 필드 / Ref / 파생 프로퍼티 / 관계 컬렉션. 컴포넌트 이름은 명시. 역방향 관계는 스키마가 파생(`ref("City", inverse="units")`). optional Ref만 `None` 가능. |
| S4 | 엔티티 = ID + 세대. 컴포넌트 = 모드가 정의하는 칼럼. Def(동결, 공유)와 Entity(Def 참조 + 바꾼 값만) 분리. 런타임 필드에 리스트 없음, 가변 길이는 owner 있는 행(`keyed`). `derived` 필드, 기여 행, 수명. 구조 변경(생성/삭제/부착)은 지연. |
| S5 | `once`·`cooldown` 같은 반응 상태도 칼럼. |
| S8 | 칼럼 추가는 기본값, 삭제는 고아 영역, 이름·타입 변경은 `migrate`. |
| X3 | `ext.*`가 칼럼 타입 등록(외부 값). |
| P5 | 코어에 UI·그래픽 타입 없음. |

### 1.2 이전 엔진의 구조와 관찰

이전 엔진은 `Def`(YAML, 불변) → `comps: [Prop…]`(컴포넌트 설정) → `Instance` + `Comp`(런타임 C# 객체)였다. 타입은 모두 C# 클래스라 새 타입에는 C#이 필요했다.

| 관찰 | 예 | 새 설계에 주는 질문 |
|---|---|---|
| **O1. Prop 안에 두 종류의 값이 섞였다.** 바뀌지 않는 설정과, 게임 중 바뀌는 상태의 초깃값. 초깃값은 이름에 `starting`을 붙여 런타임 필드와 구분했다. | QU `Prop_BuildingManager { baseMaxSlots: 10 }`(설정) vs `Prop_PopEconomy { startingWealth: 1000 }`, `Prop_Sentiment { satisfaction: 0.5 }`(초깃값). TR `Prop_VictoryPoint { startingPoints: 0 }` | 설정과 상태를 스키마에서 구분할 것인가 |
| **O2. Def 직속 필드가 따로 있었다.** 컴포넌트 밖에 Def 자체의 필드. | `label`, `desc`, `tags`, `selectionPriority`, QU `population`, TR `availableActions` | Def 직속 필드를 둘 것인가, 전부 컴포넌트로 할 것인가 |
| **O3. 엔티티가 되지 않는 Def가 많았다.** | `StatDef`, `ModifierDef`, `CommandDef`, `ActionDef`, `ReactionDef`, `IssueCategoryDef`, `RoundDef` | 데이터 전용 Def를 같은 장치로 다룰 것인가 |
| **O4. 표현용 설정이 시뮬레이션 Def에 섞였다.** | `Prop_Renderer { graphicData }`, `Prop_Inspectable { targetWindowId }`, `Prop_Interactable { commands }` | P5를 지키면서 이 데이터를 어디에 둘 것인가 |
| **O5. 자식이 부모의 컴포넌트 목록에 하나를 더했다.** | WG `Unit_Infantry`가 `Unit_Base`의 comps에 `Prop_Renderer` 추가 | S1 "컴포넌트는 이름 키 병합"이 이것을 받는다 |
| **O6. 컴포넌트 설정 안에 리스트·구조체가 있었다.** | QU `needs: [{need, amount}]`, `ideologyVectors.entries: [{axis, value}]`, TR `availableActions: [...]` | Def 값으로는 리스트·맵 허용(S2 표). 런타임 상태라면 `keyed` 행 |
| **O7. 같은 모양의 Def가 정당·진영마다 복사되었다.** | TR 정당 4개, 시위 행동 4개 (U15) | 스키마와 무관. 함수로 Def를 만드는 것(S2)이 푼다 |

---

## 2. 핵심 모델: Def 타입, 컴포넌트, 엔티티의 관계

### 2.1 질문
- **Q1.** Def 직속 필드를 둘 것인가, Def도 전부 컴포넌트로 표현할 것인가. (O2)
- **Q2.** 바뀌지 않는 설정과 바뀌는 상태를 어떻게 구분하는가. (O1, spec S4 Flyweight)
- **Q3.** 엔티티가 되지 않는 Def를 어떻게 다루는가. (O3)
- **Q4.** 런타임에 Def에 없던 컴포넌트를 붙일 수 있는가(S4 "부착"). 붙였다면 기본값은 어디서 오는가.

### 2.2 선택지

**A. 전부 컴포넌트.** Def = id + 컴포넌트 맵. `label` 같은 것도 `Info` 컴포넌트에 둔다. 경로는 항상 `unit.Info.label`.
- 장점: 규칙이 하나다. ECS에 가장 가깝다.
- 단점: 데이터 전용 Def(`StatDef`)까지 컴포넌트로 감싸야 한다. 설정과 상태가 여전히 같은 자리에 섞인다(O1이 풀리지 않음).

**B. Def 필드(불변) + 컴포넌트(칼럼).** 🟡 추천
- **Def 필드:** Def 타입이 직접 가진 필드. 로드 후 동결되고 **엔티티가 바꿀 수 없다.** 설정, 표시 키, 다른 Def 참조, 리터럴 리스트·맵을 둔다.
- **컴포넌트:** 이름 있는 칼럼 묶음. 엔티티의 **바뀌는 상태**다. Def는 자기 컴포넌트의 칼럼마다 **기본값**을 줄 수 있고, 엔티티는 바꾼 값만 저장한다(S4 Flyweight).
- **Def 타입이 엔티티가 되는지**는 컴포넌트를 갖는지로 정해진다. 컴포넌트가 없는 Def 타입은 데이터 전용이다. 엔티티로 만들 수 없고, Ref 대상으로만 쓰인다(Q3).
- **런타임 부착(Q4):** 엔티티에 Def에 없던 컴포넌트를 붙일 수 있다. 기본값은 컴포넌트 스키마의 기본값이다.
- **칼럼 읽기 순서:** 엔티티가 바꾼 값 → Def가 준 기본값 → 컴포넌트 스키마 기본값.
- 장점: O1이 구조로 풀린다. "설정이냐 상태냐"가 곧 "Def 필드냐 컴포넌트 칼럼이냐"가 된다. 그래서 `startingWealth`/`wealth`처럼 이름을 둘로 나눌 필요가 없다(`Wealth.amount`의 기본값을 Def가 준다). 데이터 전용 Def도 자연스럽다.
- 단점: 규칙이 둘이다. 어떤 값을 Def 필드에 둘지 컴포넌트에 둘지 모더가 판단해야 한다.

**C. 이전 엔진식.** Def 필드 + 컴포넌트, 단 컴포넌트에 설정과 상태가 섞인다.
- 장점: 익숙하다.
- 단점: O1이 그대로 남는다. 바뀌면 안 되는 설정을 엔티티가 덮어쓸 수 있다.

**B 수정 (B′, 2026-10-13).** B 초안에서는 불변 필드를 Def 타입 바로 아래에만 두었다. 그러면 최대 체력은 `Unit.max_hp`에, 현재 체력은 `Health.current`에 가는 식으로 한 컴포넌트의 정보가 두 곳에 나뉜다. 체력을 가진 타입마다 `max_hp`를 따로 선언해야 해서 공용 코드도 쓸 수 없다. 또 "최대 체력"은 버프로 바뀌므로 고정값이 아니라 **계산 값**이다. 그래서 값의 종류를 셋으로 나누고, 불변 값도 컴포넌트 안에 둘 수 있게 했다. 외부 사례 비교(2.5b)도 같은 구조를 보인다.

| 종류 | 고르는 질문 | 쓰기 | 저장 | 둘 수 있는 곳 |
|---|---|---|---|---|
| `const` 불변 | 같은 종류면 항상 같은가? | 불가(로드 오류) | 안 함(Def에서 읽음) | Def 타입 바로 아래, 또는 컴포넌트 안 |
| `derived` 계산 | 다른 값들로부터 계산되는가? | 불가(로드 오류) | 안 함(필요할 때 계산) | 컴포넌트 안 |
| `field` 칼럼 | 커맨드가 직접 써야 하는가? | 커맨드 안에서 가능 | 함 | 컴포넌트 안 |

고르는 순서: 커맨드가 직접 써야 하면 `field` → 다른 값으로 계산되면 `derived` → 그 밖은 `const`.

### 2.3 B′의 모양 (문법은 3절에서 정한다. 아래는 모델 설명용)

```python
# 데이터 전용 Def 타입: 컴포넌트 없음 → 엔티티가 되지 않음
deftype("Need", label=const(TextKey))

# 컴포넌트: 불변 설정 + 계산 값 + 칼럼을 함께 가진다
component("Health",
    base_max=const(int, default=10, min=1),   # 종류의 설정. Def가 값을 준다
    max=derived(int, ...),                    # base_max + 기여 행 (식 표기는 3절)
    current=field(int, default_from="max"),   # 저장되는 상태. 기본값은 이 개체의 max
)
component("Wealth", amount=field(fixed, default=0))

# 엔티티가 되는 Def 타입
deftype("PopGroup",
    label=const(TextKey),
    population=const(int, default=1),
    needs=const(dict[DefId["Need"], fixed]),  # Def 값이므로 맵 허용 (O6)
    components=["Wealth", "Health"],
)

PopGroup("SlumLaborers",
    label="pop.slum_laborers",
    needs={"Hunger": 1.2},
    Wealth=Wealth(amount=50),                 # 칼럼의 기본값
    Health=Health(base_max=8),                # 컴포넌트 안 const의 값
)
```

경로 읽기:
- `pop.population`, `pop.Health.base_max`: 불변, Def에서 읽음
- `pop.Health.max`: 계산 값. 저장 안 함. 쓰면 로드 오류
- `pop.Health.current`: 칼럼. 엔티티 값 → Def 기본값 → 스키마 기본값(`default_from="max"`) 순
- 커맨드 안의 `pop.Wealth.amount -= 10`: 허용(기록되는 쓰기)
- `pop.population = 2`, `pop.Health.max = 20`: **로드 오류**

### 2.4 사례 시험 (B 기준)

| 사례 | 이전 표현 | B로 옮기면 | 걸리는 점 |
|---|---|---|---|
| QU 정착지 | `BlockDef { tags, selectionPriority, comps: [BuildingManager{baseMaxSlots}, PopHolder, ResourceStorage, Renderer, Inspectable, Interactable …] }` | `baseMaxSlots`는 `BuildingManager` 컴포넌트 안의 `const`(B′). 자원은 키 컴포넌트, 건물은 건물 쪽 Ref + `inverse`(3.3.2). `Renderer`/`Inspectable`/`Interactable`은 시뮬레이션 밖. | O4: 표현 데이터를 어디에 둘지 (2.5) |
| TR 정당 | `PartyDef { availableActions, comps: [PartyController, VictoryPoint{startingPoints}, UnitPools] }` | `availableActions`는 Def 필드(Def 참조 리스트). `VictoryPoint.points`는 칼럼, 기본값 0. | 없음 |
| WG 분쟁 목표 | `ConflictGoalDef { requiredSelections, upkeepCosts, successConditions, comps: [Owner, Progress{targetProgress: 10}] }` | `targetProgress`는 `Progress` 컴포넌트 안의 `const`, `Progress.value`는 칼럼(B′). 조건·효과 목록은 Def 필드에 리스트로 두는 대신 스크립트 함수가 된다. | 함수 값을 가진 Def 필드로 해결 (3.3.3) |
| QU 스탯 | `StatDef { defaultBaseValue, minValue, maxValue }`, 엔티티는 `Prop_StatTracker` | `StatDef`는 데이터 전용 Def 타입. 엔티티의 스탯 값은 `derived` 필드 + 기여 행(S4). | `derived`의 식으로 해결 (3.3.1) |
| WG 보병 | `Unit_Base.comps`에 `Unit_Infantry`가 `Renderer` 추가 | 자식 Def가 부모의 컴포넌트 기본값을 이름 키로 병합(S1). | 없음 |

### 2.5 표현 데이터 (O4)
P5에 따라 커널 스키마에는 UI·그래픽 타입이 없다. 이전에 Def에 섞였던 렌더러·인스펙터·상호작용 설정을 둘 곳의 후보는 다음과 같다.
- **(a) 별도 Def 타입:** UI 모듈이 같은 스키마 장치로 `Visual`, `Inspector` 같은 Def 타입을 선언하고, 대상 Def id를 Ref로 가리킨다. 커널은 이것을 그냥 데이터 전용 Def로 본다. 🟡 추천
- **(b) 불투명 필드:** Def에 커널이 해석하지 않는 필드를 허용한다. 검증이 안 되므로 "조용히 틀리면 오류" 원칙과 맞지 않는다.

(a)라면 커널에는 아무것도 추가할 필요가 없다. UI 모듈도 데이터 전용 Def 타입을 선언하는 일반 모드일 뿐이다.

### 2.5b 외부 사례 비교: "최대 체력" 같은 값을 어떻게 다루나

림월드와 패러독스 내용은 공개된 모딩 구조에 대한 일반 지식이며 이 저장소에서 검증한 것은 아니다. 이전 엔진은 코드로 확인했다.

| | 림월드 | 패러독스 (CK3 중심, EU4·Vic3 참고) | 이전 엔진 (WG) |
|---|---|---|---|
| **기본값** | Def의 `statBases`(스탯 → 값 맵). 없으면 `StatDef.defaultBaseValue` | 정의 파일의 값, 전역 상수(`defines`) | `StatDef.defaultBaseValue`(전역). 종류별 기본값은 확인 못 함 |
| **지금의 값(계산)** | `StatWorker.GetValue`: 기본값 → StatPart → 장비·질병(hediff)·특성의 offset → factor. 저장 안 함 | 엔진 속성(예: 기량, 월간 위신) = 기본값 + 모든 modifier 합. `_mult`는 비율. 저장 안 함. CK3 `script_values`는 모더가 정의하는 계산 값 | `Comp_StatTracker.GetValue(StatDef)`: StatPart들 + ModifierDef(Flat/Additive) |
| **바뀌는 상태(저장)** | `Thing.HitPoints` 같은 필드, 컴포넌트(`ThingComp`)의 필드 | 엔진 속성(금, 위신; 효과로 증감) + **스크립트 변수**(`set_variable`, 대상별, 만료 가능) + 플래그 | `Comp_Health.CurrentHealth`(저장) |
| **현재값의 초기화** | 생성 시 `HitPoints = MaxHitPoints` | — | `Initialize`에서 `CurrentHealth = GetMaxHealth()` |
| **기여(버프)의 수명** | 질병·특성·장비가 붙어 있는 동안(출처 제거) | 시간제 modifier(`years =`), 특성·건물(출처 제거) | `ModifierDef.duration`, 출처 제거는 수동(WG `onRemoveEffects`) |
| **합성 규칙** | 엔진 고정(offset 합 → factor 곱) | 엔진 고정(합 → 비율) | StatWorker 고정 |
| **"왜 이 값인가"** | 정보 카드의 설명(GetExplanation) | 툴팁에 출처별 내역 | `StatExplanationEntry` |
| **컴포넌트 설정** | `CompProperties`(Def 쪽 설정)와 `ThingComp`(런타임 상태)가 **다른 클래스** | 해당 없음(엔티티 종류가 엔진 고정) | `Prop`(설정 + 초깃값 섞임)과 `Comp` |
| **새 타입·새 필드** | 새 컴포넌트·Def 타입은 C# 필요. 남의 Def에 데이터 붙이기는 `DefModExtension`(C# 클래스 필요) | 새 엔티티 종류·새 엔진 속성 불가. 모더 상태는 **타입 없는 변수**라 이름 오타가 조용히 새 변수가 됨(로그로만 확인) | C# 필요 |
| **남의 정의 수정** | XPath 패치(Add/Replace/Remove) | 같은 이름 파일·키 덮어쓰기 | YAML 경로 패치 |

**배울 점**
1. **세 곳 모두 기본값 / 계산된 현재 값 / 저장되는 상태를 나눈다.** 이름은 달라도 모델은 같다. "최대 체력"은 셋 모두에서 **계산 값**이고, 직접 쓰는 칼럼이 아니다.
2. **기본값의 위치가 다르다.** 림월드는 Def의 스탯 맵(어느 스탯이든 Def마다 기본값), 우리 초안은 컴포넌트 안의 `const`(`Health.base_max`). 둘은 함께 쓸 수 있다. 컴포넌트 고유의 설정은 `const`로 두고, 범용 스탯은 `std.stats`가 림월드식 맵(`stats={"MaxHealth": 12}`)을 제공한다.
3. **합성 규칙은 셋 다 엔진에 고정되어 있다.** spec S4는 합성 규칙을 `derived` 식이 정하게 했으므로 더 유연하다. 대신 표준 모드가 흔한 규칙(합 → 비율)을 기본으로 줘야 한다.
4. **현재값을 최대값으로 초기화하는 것은 흔하다.** `default_from`이 필요하다는 근거다.
5. **확장성이 차별점이다.** 림월드는 새 컴포넌트에 C#이 필요하고, 패러독스는 새 엔티티 종류를 만들 수 없으며 모더 상태가 타입 없는 변수다(오타가 조용히 통과). 스크립트로 타입이 있는 스키마를 선언하고 로드 시점에 검사하는 것이 이 엔진이 풀려는 지점이다.
6. **남의 Def에 데이터 붙이기**(림월드 `DefModExtension`)는 실제로 쓰이는 수요다. 4절 질문(남의 스키마 확장)의 근거다.

### 2.6 결정
- ✅ **Q1–Q3: B′.** 값의 종류는 `const`(불변 설정) · `derived`(계산 값, 쓰기 불가, 저장 안 함) · `field`(칼럼, 커맨드가 쓰고 저장됨) 셋이다. `const`는 Def 타입 바로 아래나 컴포넌트 안, `derived`·`field`는 컴포넌트 안에 둔다. 고르는 순서: 커맨드가 직접 써야 하면 `field` → 다른 값으로 계산되면 `derived` → 그 밖은 `const`. `const`·`derived` 대입은 로드 오류. 컴포넌트가 없는 Def 타입은 데이터 전용.
- ✅ **`default_from`:** 칼럼의 기본값을 같은 컴포넌트의 다른 값(`const`·`derived`)에서 가져올 수 있다(현재값을 최대값으로 시작). `default=`에 함수도 허용하고 `default_from`은 그 설탕(3.3.1 9항).
- ✅ **Q4 읽기 순서:** 엔티티가 쓴 값 → Def가 준 기본값 → 컴포넌트 스키마 기본값.
- ✅ **런타임 부착·분리:** Def에 없던 컴포넌트는 붙이고 뗄 수 있다(기본값은 스키마에서). Def에 원래 있던 컴포넌트는 뗄 수 없다(경로 검증 S3의 보장 유지).
- ✅ **오버라이드 판정:** 쓰기가 일어나면 값이 Def 기본값과 같아도 오버라이드로 저장한다(값 비교 안 함). 이후 그 칼럼은 Def 패치를 따르지 않는다.
- ✅ **Def 없는 엔티티는 없다:** 모든 엔티티는 Def 하나를 가리킨다.
- ✅ **표현 데이터:** (a) UI 모듈이 데이터 전용 Def 타입(`Visual` 등)을 선언해 대상 Def를 Ref로 가리킨다. 커널은 추가 장치 없음.

---

## 3. 선언 문법

### 3.1 함수 호출 방식 vs 클래스 선언 방식 (pyright 실험, 2026-10-13)

후보:
- **A. 함수 호출:** `component("Health", current=...)`, `deftype("Unit", max_hp=..., components=[...])`. 엔진이 스키마를 읽고 `.pyi`를 생성한다(Def 생성자 `Unit(...)`, 컴포넌트 기본값 타입, 엔티티 접근용 클래스).
- **B. 클래스 선언:** `@deftype class Unit: max_hp: int = 10`. 엔진은 `dataclass_transform`이 붙은 고정 스텁만 제공한다. spec S2의 `class` 금지를 스키마 선언에 한해 풀어야 한다.

pyright 1.1.389(Pylance와 같은 엔진, standard 모드)로 같은 실수 7가지를 넣어 보았다.

| 실수 | A | B |
|---|---|---|
| 필드 타입 틀림 (`max_hp="three"`) | 잡힘 | 잡힘 |
| 필드 이름 오타 (`max_hpp=`) | 잡힘 | 잡힘 |
| 컴포넌트 칼럼 오타 (`curent`) | 잡힘 | 잡힘 |
| 컴포넌트 이름 오타 (`Helth=`) | 잡힘 | 잡힘 |
| 엔티티에서 Def 필드 쓰기 (`u.max_hp = 5`) | 잡힘 (읽기 전용 property 스텁) | 잡힘 (`frozen_default=True`) |
| 엔티티 칼럼 오타 (`u.Health.curent = 1`) | 잡힘 | 잡힘 |
| 정상 코드 | 오류 없음 | **컴포넌트 필드 이름이 클래스 이름과 같으면(`Health: Health`) 자기 참조 오류.** 별칭(`HealthC = Health`)을 두어야 통과 |

결과 해석:
- **잡는 능력은 같다.** 차이는 "무엇을 생성해야 하는가"와 "문법의 대가"다.
- B도 스텁 생성을 없애지 못한다. S2가 `import`를 금지하므로, 다른 파일의 `Unit`을 Pylance가 알려면 엔진이 이름을 내보내는 스텁을 어차피 생성해야 한다.
- B는 spec의 경로 표기(`unit.Health.current`, 컴포넌트 이름 = 필드 이름)와 파이썬 클래스 스코프 규칙이 충돌해 별칭이라는 우회가 필요하다.
- B는 파이썬 dataclass 의미(가변 기본값, 클래스 상속)와 엔진 의미(Def 상속은 `parent=`, 병합 규칙)가 달라 모더가 헷갈릴 여지가 크다.
- A의 비용: 스키마를 바꾸면 `engine check`로 스텁을 다시 만들어야 편집기가 새 필드를 안다. spec S1 ⑥이 이미 스텁 생성을 요구하므로 새 비용은 아니다(나중에 `check --watch`로 줄일 수 있다).

✅ **A로 확정**(2026-10-13). S2의 `class` 금지를 유지하고, 모든 선언이 Def와 같은 "키워드 인자 호출"이 된다. 실험 파일(`a`, `b`, `b2`, `c`)은 세션 임시 폴더에서 돌렸고 저장소에 남기지 않았다. 결과는 위 표와 3.2가 전부다.

### 3.2 A의 표기 ✅

spec S2·S6의 커맨드 모양(`@command def Move(unit: Ref[Unit], dest: Ref[City])`)과 맞물려야 한다. 매개변수 타입 표기에서 `Unit`은 **타입**으로 쓰이므로, 생성된 스텁에서 `Unit` 하나가 타입이자 Def 생성자여야 한다(실험 A처럼 생성자 함수와 `UnitView` 클래스로 나누면 `Ref[Unit]`이 깨진다). 아래 표기와 스텁 모양은 pyright로 확인했다(실험 `c`: 정상 코드 오류 없음, 의도한 오류 3개 모두 검출).

**스키마 선언** (spec의 기존 표기를 따른다: 스키마 함수는 소문자, 타입은 파이썬 표기 그대로)

```python
component("Health",
    base_max=const(int, default=10, min=1, max=9999),
    max=derived(int, health_max),              # 식 표기는 3.3.1
    current=field(int, default_from="max", min=0),
)
component("Location", sector=ref("Sector", inverse="units"))
component("Owner",    faction=ref("Faction", optional=True))

deftype("Unit",
    label=const(TextKey),
    tags=const(list[str], merge="append"),
    upkeep=const(dict[DefId["Resource"], fixed]),
    components=["Health", "Location", "Owner"],
)

Unit("Infantry",
    label="unit.infantry",
    Health=Health(base_max=12),    # 컴포넌트 안 const의 값: 생성자 호출 모양
)

@command
def Move(unit: Ref[Unit], dest: Ref[Sector]):
    unit.Health.current -= 1       # 칼럼 쓰기
    for u in dest.units: ...       # inverse로 파생된 역방향 관계
```

- **`const(타입, ...)` / `field(타입, ...)` / `derived(타입, ...)`:** 값의 종류(2.6 B′)를 이름으로 드러낸다. 타입은 파이썬 타입 표기 그대로(`int`, `fixed`, `str`, `TextKey`, `list[str]`, `dict[K, V]`). 매개변수 어노테이션과 같은 표기라 배울 것이 하나다. 기본값·범위·병합은 키워드(`default=`, `default_from=`, `min=`/`max=`, `merge=`). 범위는 P2의 저장 폭도 정한다. `merge=`는 Def 상속 병합 규칙이라 `const`에만 의미가 있다.
- **`ref("X", inverse=..., optional=...)`:** 엔티티 Ref 칼럼 전용(S3 표기 그대로). 대상은 문자열이라 선언 순서와 무관하다. 대상은 컴포넌트를 가진(엔티티가 되는) 타입이어야 한다.
- **참조 타입은 둘이고 자리와 무관하게 뜻이 같다**(spec-gaps G32): `Ref["X"]`는 X의 **엔티티**, `DefId["X"]`는 X의 **Def**(id). Def 필드가 다른 Def를 가리킬 때는 `const(DefId["X"])`, 런타임 칼럼이 Def를 담을 때는 `field(DefId["X"])`(예: 지금 만드는 레시피). `const` 안에는 `Ref`가 올 수 없다(로드 시점에 엔티티가 없음). 엔티티 칼럼은 `ref(...)` 하나로만 선언한다(`field(Ref["X"])`는 로드 오류).
- **스키마 안에서 다른 타입 이름은 문자열로**(`DefId["Resource"]`): 스텁이 만들어지기 전에도 편집기가 이름을 몰라 오류를 내지 않는다.
- **컴포넌트 값은 `Health(base_max=12)`** 모양. dict(`{"base_max": 12}`)도 검사되지만 오류 메시지가 덜 구체적이었다(실험 A).
- Def id는 첫 위치 인자 문자열. 스텁의 `Literal[...]`로 자동완성된다.

**생성되는 스텁의 모양** (모더는 보지 않아도 됨)

```python
type Ref[X] = X                      # 편집기에서는 Ref[Unit]이 Unit처럼 보인다

class Health:
    @property
    def base_max(self) -> int: ...   # const = 읽기 전용 → 쓰면 편집기 오류
    @property
    def max(self) -> int: ...        # derived = 읽기 전용
    current: int                     # field = 쓰기 가능
    def __init__(self, *, base_max: int = ..., current: int = ...) -> None: ...

class Unit:
    @property
    def label(self) -> TextKey: ...
    Health: Health
    def __init__(self, id: str, /, *, label: TextKey = ..., Health: Health = ...) -> None: ...

class Sector:
    @property
    def units(self) -> list[Unit]: ...   # inverse로 파생
```

- 스텁은 `type` 문을 쓰므로 엔진이 함께 생성하는 `pyrightconfig.json`(또는 VS Code 설정)에 `pythonVersion = 3.12` 이상을 둔다.
- 편집기에서 `Ref[X]`는 `X`와 구분되지 않는다. optional Ref는 스텁에서 `X | None`이 되어 None 검사를 강제한다(확인 필요).
- `DefId[X]`는 X의 Def id 문자열(`Literal["Bread", ...]`)로 보여야 한다(spec S2 "Def 참조는 문자열 + `Literal`"). 커맨드 매개변수에 모더가 `DefId[Recipe]`로 적었을 때 이를 어떻게 펼칠지는 `.pyi` 구현 때 정한다.
### 3.3 남은 질문

#### 3.3.1 `derived`의 식 ✅

`derived`는 **저장하지 않고 읽을 때마다 계산되는 값**이다. 합산 규칙 하나를 한 곳에 두고, 영향 요소(버프, 장비, 모드)는 값을 고치는 대신 **기여 행을 넣고 뺀다**. 그래서 해제가 역연산 없이 되고, 서로 모르는 모드의 기여가 같은 목록에 합쳐지며, 세이브에는 `base_max`와 기여 행만 남는다(공식을 고친 패치가 기존 세이브에도 적용된다).

```python
def health_max(h: Health) -> int:
    flat = 0
    pct = 0
    for r in contribs(h, "max"):
        if r.op == "add":
            flat += r.value
        elif r.op == "pct":
            pct += r.value
    return clamp(floor((h.base_max + flat) * (1 + pct / 100)), 1, 9999)

component("Health",
    base_max=const(int, default=10, min=1, max=9999),
    max=derived(int, health_max),
    current=field(int, default_from="max", min=0),
)
```

1. **식의 자리:** `derived(타입, 함수)`. 이름 붙은 함수 참조와 람다 둘 다 받는다. 매개변수에 타입을 단 함수 참조는 편집기 검사를 받는다. 데코레이터 모양(`@derived("Health.max")`)은 4절(남의 스키마 확장)과 함께 본다.
2. **인자는 "계산할 자리" 하나:** 그 컴포넌트(`h`)만 받는다. 영향 요소를 인자로 나열하지 않는다. 영향 요소의 목록은 열려 있고(나중 모드가 추가한다), 무엇을 읽을지가 조건에 따라 달라지기 때문이다. 다른 값은 함수 안에서 찾아 읽는다: 같은 컴포넌트(`h.base_max`), 소유 엔티티를 거친 다른 컴포넌트와 참조(`h.owner.Owner.faction.Tech.level`), 기여 행(`contribs`). RimWorld `StatWorker`(pawn 하나를 받아 안에서 장비·Hediff를 뒤진다), Paradox modifier 합산(스코프 하나에 쌓인 modifier를 모은다)과 같은 구조.
3. **순수 함수:** 읽기만 한다. 쓰기·RNG·`emit`·구조 변경은 로드 오류. 반환 타입은 선언과 맞아야 하고, `fixed` → `int`는 `floor()` 등으로 명시해 바꾼다.
4. **의존 관계:** 엔진이 실행 중 실제로 읽은 값(read-set)을 기록한다. 캐시 무효화(의미는 "매번 새로 계산"과 같다)와 trace에 쓴다.
5. **순환:** read-set이 실행 중에 정해지므로 로드 시점 검사는 보수적이다. 타입 정보로 속성 경로를 정적으로 추적하고, 모든 분기를 읽는다고 가정해 순환 가능성이 있으면 로드 오류. 빠져나간 경우는 실행 중 재진입 감지로 오류. 실제로는 순환하지 않는데 거부되는 경우가 생길 수 있고, 그때는 식을 둘로 쪼갠다.
6. **기여 행:** 커널 원시 `contribs(컴포넌트, "값 이름")`이 그 자리를 겨냥한 행들(`op`, `value`, `source`)을 준다. `op` 이름은 커널에 불투명한 문자열이고 의미는 식이 정한다. 대상 이름(`"max"`)은 로드 시점에 검사한다. 기여 행을 넣는 쪽의 모양(수명, 출처)은 S4 기여 행 설계에서 정한다.
7. **trace:** 자동. 읽은 값과 기여 행이 그대로 근거가 된다("최대 체력 13 = 기본 12, 성벽 +10%").
8. **표준 모드의 편의 함수:** `std.stats`가 `stat(int, base="base_max", min=1, max=9999)`처럼 흔한 규칙(합 → 비율 → 자르기)의 `derived`를 만들어 주는 평범한 함수를 제공한다. 커널 특권 없음.
9. **`default=`에 함수 허용**(`default=lambda h: h.max // 2`), `default_from="max"`는 그 설탕으로 남긴다. 둘 다 생성 시 초기값일 뿐이고, 그 뒤로 `field`와 원천 값은 별개다.

남는 문제:
- 🟡 **`field`가 `derived` 상한을 넘을 때**(버프 해제로 max 15 → 13, current는 15): `derived`는 쓸 수 없으므로 스스로 고치지 못한다. (a) settle 시점 `when` 반응으로 잘라낸다(저장값이 바뀜, Paradox 쪽), (b) 읽을 때 `min(current, max)`로 보인다(저장값 유지, 버프가 다시 붙으면 복원), (c) 게임이 고른다. `field`의 `max=`에 `derived` 이름을 줄 수 있게 할지와 함께 정한다.
- 🟡 **제너레이터 식:** spec S2는 컴프리헨션을 허용하고(S3 쿼리도 컴프리헨션) 제너레이터를 금지한다. 현재 파서는 컴프리헨션을 아직 구현하지 않아 PAR002를 낸다(구현 예정). 남는 것은 `sum(r.value for r in ...)` 같은 **제너레이터 식**이 "금지된 제너레이터"에 드는지다. 위 예시는 이를 피해 반복문으로 썼다. 파서에서 컴프리헨션을 구현할 때 정한다.

#### 3.3.2 가변 길이 런타임 데이터와 `keyed`

이전 게임의 런타임 리스트·맵(컴포넌트 안 `List<>`/`Dictionary<>`)은 다섯 모양이었다.

| 모양 | 사례 | 새 표현 |
|---|---|---|
| P1. Def → 값 | QU·WG `ResourceStorage.Stocks` | 키 컴포넌트 |
| P2. Def → 여러 칼럼 | QU `PopNeeds`(기본량·활성·충족률·결핍도), RimWorld `SkillRecord`(레벨·경험치·열정) | 키 컴포넌트 |
| P3. 소속 목록 | QU `BuildingIds`·`UnitIds`·`OutpostIds`, TR `UnitsHolder`·`ThreatHolder` | 원소 쪽 Ref + `inverse` |
| P4. 순서 있는 더미 | TR `Comp_Hand`(덱·손패·버림), `SocialTrack.Markers`, `TurnOrderIds` | 원소 쪽 Ref + `inverse` + `order_by` |
| P5. 엔티티 → 값·집합 | TR `OpinionTrack.IssuePositions`, 진영 간 관계값(RimWorld goodwill, CK3 opinion) | 방향을 뒤집은 Ref, 또는 엔티티 키 컴포넌트 |

- ✅ **가르는 기준:** 원소가 정체성을 가지면(따로 존재하고, 옮겨 다니고, 남이 참조한다) 엔티티 + Ref, 컬렉션은 `inverse`로 얻고 저장하지 않는다. TR `Comp_Hand`가 "카드는 한 더미에만"을 구조로 보장하지 못하던 문제가 사라진다. 정체성이 없고 "이 엔티티의 키 K 값"이면 `keyed`.
- ✅ **`keyed` = 키가 붙은 컴포넌트(후보 Y).** 엔티티 하나에 키마다 한 줄. 줄 안에서 `const`·`derived`·`field`가 그대로 동작하고, 줄마다 Def 기본값 → 엔티티 덮어쓰기(Flyweight)가 적용된다. `derived` 함수는 그 줄 하나(`s.key` 포함)를 받는다. 기여 행 대상은 `(엔티티, Skill[Medicine], "level")`.

  ```python
  component("Skill", key="SkillDef",
      level=field(int, default=0, min=0, max=20),
      xp=field(int, default=0),
      passion=const(str, default="none"),
  )
  Colonist("Doc", Skill={"Medicine": Skill(level=8)})

  pawn.Skill.Medicine.level      # 정적 키
  pawn.Skill[s].level            # 동적 키
  for s in pawn.Skill: ...       # 키 id 순
  ```

  칼럼 타입 방식(`field(keyed("Resource", fixed))`, 후보 X)은 여러 칼럼(P2)이 같은 키를 공유한다는 사실을 드러내지 못해 택하지 않았다. 값 하나짜리(P1)가 한 단계 길어지는 것(`Stock.Food.amount`)은 나중에 설탕으로.
- ✅ **키 종류:** Def 타입과 엔티티 Ref만. 런타임 `int`·`str` 키는 금지(오타 검사 불가). 엔티티 키의 줄은 키 엔티티가 죽으면 엔진이 지운다.
- ✅ **순서:** `ref("Pile", inverse="cards", order_by="pos")`. 역방향 컬렉션의 기본 순서는 엔티티 id 순(S3), `order_by=`는 원소의 칼럼으로 정렬. 섞기는 `pos`를 RNG로 다시 쓰는 것, "맨 앞에 넣고 최대 7개" 같은 규칙은 표준 모드 함수. 커널은 정렬 키만 안다.
- ✅ **없는 키의 의미는 컴포넌트가 선언한다.** 저장은 어느 쪽이든 있는 줄(쌍)만 한다. 다르게 정하는 것은 "줄의 유무가 게임에서 의미를 갖는가"뿐이다.
  - `keys="all"`(기본): 없는 키를 읽으면 기본값으로 채운 줄. 스텁 타입 `Skill`. 새 모드의 키가 기존 엔티티에 자동으로 생긴다. 기술·자원·스탯.
  - `keys="listed"`: 없는 키를 읽으면 `None`. 스텁 타입 `Need | None`(optional Ref와 같은 규칙, pyright가 None 검사를 강제). 처음 줄은 엔티티 Def 리터럴에 적힌 키, 런타임에 붙이고 뗄 수 있다(Def에서 온 줄은 못 뗀다). 유무가 쿼리 필터로 성립한다("사치품 욕구를 가진 팝"). QU `PopNeeds`, `AllowedResourceIds`. 비활성은 줄 안의 `active` 칼럼.
  - 엔티티 키는 항상 `listed`처럼 동작.
  - ECS로 보면 키 컴포넌트는 쌍(Flecs relationship의 `(Skill, Medicine)`)이고, 대상 엔티티 삭제 시 쌍 삭제도 Flecs와 같다.
- ✅ **일반 컴포넌트도 같은 규칙:** Def가 가진 컴포넌트는 항상 있다(`Health`). 런타임에만 붙는 컴포넌트는 스텁에서 `X | None`.
- ✅ 줄 부착과 분리는 즉시 보인다(spec-gaps G88: 구조 변경은 지연하지 않고, 순회는 시작 시점 목록을 돈다).

#### 3.3.3 로직이 든 Def 필드 ✅

WG 분쟁 목표(`successConditions`, `onSuccessEffects`, `onRegisterEffects`), QU `EventDef.options[{requirements, effects}]`, TR 행동 카드는 Def가 데이터와 함께 **규칙**을 가졌다. 이전 엔진은 규칙을 YAML 객체 트리(Condition/Effect)로 썼고, 중첩되면 읽기 어려웠고(U6) 리스트라 상속 시 복사했다(U15).

- 비교: RimWorld는 XML이 C# 클래스 이름을 고른다(`workerClass`, `<li Class=...>`, 로직은 C#). Paradox(CK3)는 Def 안에 `is_valid = { }`, `effect = { }` 블록을 두고, 재사용은 `scripted_trigger`/`scripted_effect`, 끼어들기는 `on_action` 목록 추가.
- ✅ **규칙은 함수로, Def 필드에 넣는다**(Paradox 방식에 진짜 함수). Def 밖의 반응 규칙으로 빼는 방식은 Def 하나를 추가할 때 두 군데를 써야 해서 택하지 않았다.

  ```python
  # 시스템 쪽 (분쟁 모드)
  deftype("ConflictGoal",
      label=const(TextKey),
      succeeded=const(Cond["ConflictGoal"]),                            # 확인용 함수
      on_success=const(list[Action["ConflictGoal"]], merge="append"),   # 바꾸는 함수 목록
      components=["Owner", "GoalTarget", "Progress"],
  )

  # 데이터 쪽 (목표를 추가하는 모드)
  def take_slot(g: ConflictGoal):
      occupy(g.GoalTarget.slot, g.Owner.faction)

  ConflictGoal("InfluenceSlot",
      label="goal.influence_slot",
      Progress=Progress(target=10),
      succeeded=lambda g: g.Progress.value >= g.Progress.target,
      on_success=[take_slot],
  )
  ```

- ✅ **언제 부르는지는 그 Def 타입을 선언한 시스템 모드가 정한다**(예: 분쟁 모드가 `when` 하나로 각 목표의 `g.def.succeeded(g)`를 본다). 커널이 아는 것은 "Def 값으로 함수가 올 수 있다"뿐.
- ✅ **확인용과 바꾸는 함수를 자리의 타입으로 나눈다.** 함수 자체는 spec S2대로 한 종류이고 효과는 엔진이 자동 판정한다. 스키마는 그 자리에 어떤 효과의 함수가 와야 하는지를 선언한다. 확인용(`Cond`·`Fn`)은 `derived`와 같은 순수 규칙(쓰기·RNG·`emit` 금지), `when`·`derived` 안에서 불려도 안전. 바꾸는 함수(`Action`)는 Flow 본문으로 실행되어 쓰기·`ask`·`rand()` 가능. 확인용 자리에 바꾸는 함수가 오면 로드 오류. 타입 이름은 `.pyi` 모양과 함께 확정.
- ✅ **확장 지점은 `list[Action]` + `merge="append"`.** 다른 모드가 남의 Def에 효과를 덧붙인다(Paradox `on_action`). 순서는 로드 순서.
- 저장: Def는 세이브에 들어가지 않으므로 Def 안의 함수는 문제없다. 세이브되는 Flow 프레임이 함수를 가리킬 때만 이름으로(S7).
- 따라 사라지는 것: `onRemoveEffects`의 표식 떼기는 기여 행의 "출처 제거" 수명(S4)이 맡는다. `requiredSelections`와 `^destroyTarget` 컨텍스트 맵은 커맨드 매개변수(`choose`)와 목표 엔티티의 Ref 칼럼이 된다.

#### 3.3.4 외부 값 표기 ✅

외부 값은 C# 확장(`ext.*`)이 등록한 칼럼 타입의 값이다(spec S2, X3). 스크립트에는 불투명 값으로 보이고, 확장의 함수와 `==`/`!=`로만 다룬다. 아래는 스키마와 Def에 적는 방법.

1. **타입 이름은 확장 이름을 붙여 적는다:** `component("Location", pos=field(spatial.Point))`. 파이썬 타입 표기 그대로이고, 확장끼리 이름이 겹쳐도 충돌하지 않는다.
2. **모드는 쓰는 확장을 `mod.py`에 선언한다:** `Mod(id=..., extensions=["spatial"])`. 게임에 그 확장이 없으면 Def 줄마다가 아니라 모드 단위 오류 하나로 알린다.
3. **Def 리터럴은 확장의 생성 함수로:** `Sector("Berlin", Location=Location(pos=spatial.point(3, 4)))`. 확장이 "로드 중에 불러도 되는 순수 생성 함수"를 등록하고, Def에서는 그것만 쓸 수 있다.
4. **기본값:** 확장이 등록할 때 기본값을 주거나, 필드에 `default=` 또는 `optional=True`가 있어야 한다. 둘 다 없으면 로드 오류.
5. **놓일 수 있는 자리:** `const`·`field`·`derived`(함수가 반환)·Def 리스트 안. 키 컴포넌트의 키로는 쓸 수 없다(3.3.2: 키는 Def와 Ref만).

생성 스텁에서는 비교만 정의된 `@final` 클래스라서 필드 접근(`pos.x`)과 연산자(`pos < other`)를 pyright가 자동으로 잡는다.

```python
# spatial.pyi (엔진이 생성)
@final
class Point:
    def __eq__(self, other: object) -> bool: ...
def point(x: int, y: int) -> Point: ...
def distance(a: Point, b: Point) -> fixed: ...
```

- 🟡 확장이 등록하는 인덱스("이 좌표 근처의 유닛")를 필드에 연결하는 방법은 `ext.*` 확장 API 모양과 함께 정한다.

#### 3.3.5 그 밖

- 생성될 `.pyi`의 배치는 4.3(모드마다 생성). 정확한 모양(파일 구성, `Cond`/`Action` 타입 이름)은 구현 때.

## 4. 선언 시점과 확장

### 4.1 스키마 단계와 Def 단계 ✅

파일을 순서대로 한 번만 실행하면 확장 결과가 로드 순서에 달린다.

```
모드 A: deftype("Unit", ...)
모드 C: Unit("Scout", ...)                       # A에만 의존, B보다 먼저 로드
모드 B: extend("Unit", components=["Morale"])    # Scout는 Morale 없이 만들어진다
```

- ✅ **두 단계로 나눈다.** (1) 모든 모드의 **스키마 파일**만 모드 순서대로 실행해 타입을 완성한다. (2) 그다음 모든 Def 파일을 실행한다. 이때 타입은 고정되어 있다.
- 스키마 파일은 따로 둔다(예: 모드 폴더의 `schema/`). `derived` 함수처럼 스키마가 가리키는 함수도 스키마 파일에 둔다. `.pyi`는 1단계만 돌려 만들 수 있다.
- 이전 엔진도 같은 구조였다: 모드 DLL 로드 → `TypeRegistry.Initialize()`가 `Def`/`Prop`/`Comp`/`ICondition`/`IEffect` 클래스 수집 → YAML Def 역직렬화. 차이는 1단계를 C# 클래스 대신 파이썬 스키마 파일이 맡는 것. 이전 엔진의 또 다른 2단계(프록시로 만든 뒤 `ResolveReferences`로 연결)는 새 파이프라인의 "연결" 단계(S1)에 해당한다.

### 4.2 남의 타입 확장 ✅

비교: Paradox는 Def 타입의 필드를 엔진이 정하고 모드가 늘릴 수 없다. RimWorld는 패치로 남의 ThingDef `comps`에 컴포넌트를 끼우거나 `DefModExtension`(추가 데이터 주머니)을 붙인다. Def 클래스 자체에 필드를 더하지는 못한다. 이전 엔진은 패치로 `comps`에 `Prop`을 끼우는 것만 가능했다.

| 하려는 것 | 허용 | 방법 |
|---|---|---|
| 남의 Def 타입 전체에 내 컴포넌트 | ✅ | `extend("Unit", components=["Morale"])` |
| 남의 특정 Def에 내 컴포넌트 | ✅ | Def 패치(S1) |
| 남의 Def 타입에 `const` 추가 | ✅ | `extend("Resource", trade_value=const(fixed, default=0))`. 데이터 전용 Def 타입을 확장하는 유일한 길 |
| 남의 컴포넌트에 칼럼 추가 | ❌ | 내 컴포넌트를 따로 만들어 붙인다(남의 컴포넌트 모양이 바뀌면 그 모드 코드가 깨질 수 있다) |
| 남의 필드의 타입·범위 변경 | ❌ | 값은 Def 패치로 |

- 같은 이름을 두 모드가 추가하면 두 모드 이름을 담은 로드 오류.

### 4.3 보이는 범위와 `.pyi` ✅

- ✅ **모드는 자기가 의존하는 모드의 것만 본다.** 범위는 `dependencies`(필수 의존)의 전이적 닫힘이다. `loadAfter`는 순서만 정하고 대상이 없을 수도 있으므로 범위에 들지 않는다. 이전 엔진의 `TypeRegistry`는 전역이라 의존하지 않은 모드의 타입도 쓸 수 있었다. 의존하지 않는 모드의 타입·필드·컴포넌트를 쓰면 로드 오류("B에 대한 의존을 선언하세요").
- ✅ **`.pyi`는 모드마다 생성.** 그 모드 + 의존 모드들의 스키마와 확장만 담는다. "내 구성에서는 되는데 다른 모드 구성에서는 깨지는" 일을 구조로 막는다.
- ✅ **선택적 의존**(`mod.py`의 `optional`, "B가 있으면 B의 기능을 쓴다")에서 B의 이름은 `if mod_loaded("B"):` 가드 안의 `patch.*`에서만 쓴다(대상 Def, 경로 이름, 값 안의 Def id). 가드 밖에서는 보이는 범위 규칙 그대로 오류다(spec-gaps G38·G46·G65).

## 5. spec 반영 (2026-10-13)

결론만 spec에 옮겼다. 새 절을 만들지 않고 기존 절에 나눠 넣었다.

| 이 문서 | spec |
|---|---|
| 2.6 값 세 종류(B′), 컴포넌트 유무 | S4 |
| 3.1–3.2 함수 호출 방식 표기 | S2 |
| 3.3.1 `derived`의 식 | S4 스탯 부품 |
| 3.3.2 가변 길이 데이터, 키 컴포넌트, `order_by` | S4, S3 관계, S2 리스트·맵 표 |
| 3.3.3 함수 값을 가진 Def 필드 | S2 함수 표 아래, 값 타입 |
| 3.3.4 외부 값 표기 | S2 외부 값, X3, S1 매니페스트 |
| 4.1 스키마 단계 | S1 ② |
| 4.2 남의 타입 확장 | S4 |
| 4.3 보이는 범위, `.pyi` 모드별 | S1 매니페스트 |

spec에도 열린 채로 옮긴 것: `field`가 `derived` 상한을 넘을 때 · 키 줄 부착 지연 · 함수 자리 타입 이름 · 확장 인덱스 연결 · `optional` 의존 대상의 이름 쓰기 · 키 컴포넌트 줄 칼럼의 맵.
