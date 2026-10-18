# spec 빈틈 목록

`spine-spec.md` 8절의 절차에 따라, 구현하다 spec이 비어 있거나 애매한 곳을 만나면 여기에 적는다. 추측으로 채운 곳도 반드시 적는다. 구현이 끝나면 각 항목을 spec의 결정으로 옮기고 이 목록에서 지운다.

**상태:** 잠정 = 구현이 이렇게 동작하지만 확인 필요 · 확인 = 사용자가 동의함 · 반영 = spec에 옮김.

**모양:** 이 파일은 색인이다. G 번호마다의 전문(무엇이 비어 있었나·어떻게 정했나·이유)은 `docs/gaps/`의 영역 파일에 있다. 새 항목은 그 영역 파일 끝에 `## G번호` 절(위치·무엇이 비어 있었나·어떻게 정했나·이유·상태)을 쓰고 이 색인의 그 영역 표에 한 행을 더한다. 번호는 영역과 상관없이 전체에서 이어 매긴다(다음 번호는 G228). 영역은 위치의 첫 표시로 고르고, 다른 G에서 이어진 항목은 그 G의 영역에 둔다. 영역 표에 "무엇을 정했나" 요약 칸을 더하는 중이다(2026-10-18 시작).

## S1 콘텐츠 로딩·스텁

| # | 위치 | 상태 |
|---|---|---|
| [G1](gaps/S1-loading.md#g1) | S1 ① 정렬 | 확인 |
| [G2](gaps/S1-loading.md#g2) | S1 매니페스트 | 잠정 |
| [G3](gaps/S1-loading.md#g3) | S1 매니페스트 | 잠정 |
| [G7](gaps/S1-loading.md#g7) | S1 ① 발견 | 잠정 |
| [G8](gaps/S1-loading.md#g8) | S1 ① 정렬 | 확인 |
| [G9](gaps/S1-loading.md#g9) | S1 ① 정렬 | 잠정 |
| [G10](gaps/S1-loading.md#g10) | S1 ① 정렬 | 잠정 |
| [G11](gaps/S1-loading.md#g11) | S1 ① 정렬 | 확인 |
| [G12](gaps/S1-loading.md#g12) | S1 매니페스트 | 잠정 |
| [G15](gaps/S1-loading.md#g15) | S1 ① 정렬 | 잠정 |
| [G16](gaps/S1-loading.md#g16) | S1 ②a 스키마 파일 | 확인 |
| [G18](gaps/S1-loading.md#g18) | S1 `use_all` | 잠정 |
| [G19](gaps/S1-loading.md#g19) | S1 중복 Def ID | 잠정 |
| [G21](gaps/S1-loading.md#g21) | S1 `override=True` | 잠정 |
| [G25](gaps/S1-loading.md#g25) | S1 Def 리터럴 | 잠정 |
| [G26](gaps/S1-loading.md#g26) | S1 ② 단계 분리 | 잠정 |
| [G28](gaps/S1-loading.md#g28) | S1 오류 정책 (②) | 잠정 |
| [G29](gaps/S1-loading.md#g29) | S1 매니페스트 `extensions` | 잠정 |
| [G30](gaps/S1-loading.md#g30) | S1 `inherit.필드` | 잠정 |
| [G34](gaps/S1-loading.md#g34) | S1 패치 호출 모양 | 확인 |
| [G35](gaps/S1-loading.md#g35) | S1 ③ 패치 파일·실행 시점 | 확인 |
| [G36](gaps/S1-loading.md#g36) | S1 패치 연산별 허용 필드 | 확인 |
| [G37](gaps/S1-loading.md#g37) | S1 `set` 충돌의 "의존 관계" | 확인 |
| [G38](gaps/S1-loading.md#g38) | S1 패치 대상의 보이는 범위 | 확인 |
| [G39](gaps/S1-loading.md#g39) | S1 패치 값의 검사 시점 | 확인 |
| [G41](gaps/S1-loading.md#g41) | S1 패치 적용 순서 (spec의 "단계 고정"을 바꿈) | 확인 |
| [G42](gaps/S1-loading.md#g42) | S1 ③·④ 값에 의존하는 패치 검사는 ④로 (G42 일반화) | 확인 |
| [G43](gaps/S1-loading.md#g43) | S1·S3 추상 Def·`override`된 Def의 패치 | 확인 |
| [G44](gaps/S1-loading.md#g44) | S1·S4 컴포넌트를 붙이고 떼는 패치 | 확인 |
| [G45](gaps/S1-loading.md#g45) | S1·S9 패치 변경 이력의 구현 모양 | 확인 |
| [G47](gaps/S1-loading.md#g47) | S1·S3 패치 경로의 키 표기 (G34 보강) | 확인 |
| [G49](gaps/S1-loading.md#g49) | S1 오류 정책 (G28 확장) | 확인 |
| [G50](gaps/S1-loading.md#g50) | S1 패치 "같은 필드"의 판정과 Def별 적용 순서 | 확인 |
| [G51](gaps/S1-loading.md#g51) | S1 패치 연산의 값 모양 (G36 보강) | 확인 |
| [G52](gaps/S1-loading.md#g52) | S1 ④의 결과를 담는 자료구조 | 확인 |
| [G53](gaps/S1-loading.md#g53) | S1 `add`의 기준값 (G42 보강) | 확인 |
| [G54](gaps/S1-loading.md#g54) | S1 맵 항목 경로 `set`에서 없는 키 (G36 보강) | 확인 |
| [G55](gaps/S1-loading.md#g55) | S1 ④ Def 하나의 처리 순서와 `inherit` 식 (G30·G50 보강) | 확인 |
| [G56](gaps/S1-loading.md#g56) | S1 `inherit` 식의 범위 (G30·G55 보강) | 확인 |
| [G57](gaps/S1-loading.md#g57) | S1 ④ `parent` 검증 | 확인 |
| [G58](gaps/S1-loading.md#g58) | S1 ④ 컴포넌트 값의 상속 병합 단위 | 확인 |
| [G59](gaps/S1-loading.md#g59) | S1 `merge="append"` 필드의 `inherit` 식 (G58 보강) | 확인 |
| [G60](gaps/S1-loading.md#g60) | S1 ④ 패치 적용과 실패한 패치 (G42·G44 보강) | 확인 |
| [G62](gaps/S1-loading.md#g62) | S1 패치 값 안의 `inherit` (G30 보강) | 확인 |
| [G63](gaps/S1-loading.md#g63) | S1 ⑤ 링크가 만드는 것 | 확인 |
| [G64](gaps/S1-loading.md#g64) | S1 ⑤ 참조가 유효한 조건 | 확인 |
| [G65](gaps/S1-loading.md#g65) | S1 ⑤ 가드 안 패치 값의 `optional` 모드 Def (G38 보강) | 확인 |
| [G66](gaps/S1-loading.md#g66) | S1 ⑤ `abstract` Def를 가리키는 참조 (G57 보강) | 확인 |
| [G67](gaps/S1-loading.md#g67) | S1 ⑥ 필수 값 (G25 보강) | 확인 |
| [G68](gaps/S1-loading.md#g68) | S1 ⑥ 최종 값의 타입 검사와 `min`/`max` (G25·G39·G48 모음) | 확인 |
| [G69](gaps/S1-loading.md#g69) | S1 ⑥ 동결본이 기본값을 채우는가 (S4 보강) | 확인 |
| [G70](gaps/S1-loading.md#g70) | S1 ⑥ 동결 집합과 `abstract` Def (G57·G66 보강) | 확인 |
| [G71](gaps/S1-loading.md#g71) | S1 ⑥ `.pyi` 스텁의 범위와 생성 방식 (schema-design 4.3 보강) | 확인 |
| [G72](gaps/S1-loading.md#g72) | S1 ⑥ 작성용 `.pyi`의 모양 (G71 보강) | 확인 |
| [G73](gaps/S1-loading.md#g73) | S1 ⑤⑥ 스키마가 선언한 `default` 값의 검사 (G69 보강) | 확인 |
| [G74](gaps/S1-loading.md#g74) | S1 ⑥ `extend`로 더한 `const`의 기본값 (G67·G69 보강) | 확인 |
| [G77](gaps/S1-loading.md#g77) | S1 ⑥ `keys="all"` 키 컴포넌트의 줄과 기본값 (G69·G74 보강) | 확인 |
| [G81](gaps/S1-loading.md#g81) | S1 ⑥ `.pyi` 스텁의 파일 배치와 CLI (G71 보강, schema-design 4.3) | 확인 |
| [G82](gaps/S1-loading.md#g82) | S1 `.pyi` 스텁: 매니페스트와 스키마 파일 (G72·G81 보강) | 확인 |
| [G83](gaps/S1-loading.md#g83) | S1 비활성화된 모드의 `.pyi` 스텁 (G81의 열린 점, G49 보강) | 확인 |
| [G94](gaps/S1-loading.md#g94) | S1 ⑥·S4 Def가 적은 `keys="listed"` 줄의 필수 칸 (G93 보강) | 확인 |
| [G207](gaps/S1-loading.md#g207) | 로직용 `.pyi`(handoff 남은 일 2)에서 `Ref[T]`와 엔티티 타입 | 확인 |
| [G208](gaps/S1-loading.md#g208) | G207 로직용 스텁에서 None일 수 있는 값을 확인 없이 읽을 때 | 확인 |
| [G209](gaps/S1-loading.md#g209) | G82 스키마 스텁에 컴포넌트·Def 타입 이름이 없음 | 확인 |
| [G213](gaps/S1-loading.md#g213) | S1 ① 파일 이름의 유니코드 정규화 (G7 보강) | 확인 |

## S2 로직 언어·실행기 (P2·P6, 실행 예산·벤치 포함)

| # | 위치 | 상태 |
|---|---|---|
| [G4](gaps/S2-logic.md#g4) | P2 `fixed` 리터럴 | 확인 |
| [G13](gaps/S2-logic.md#g13) | S2 언어 | 잠정 |
| [G14](gaps/S2-logic.md#g14) | S2 언어 | 확인 |
| [G17](gaps/S2-logic.md#g17) | S2 함수 정의 | 확인 |
| [G20](gaps/S2-logic.md#g20) | S2 최상위 이름 | 잠정 |
| [G22](gaps/S2-logic.md#g22) | S2 로드 시점 평가 | 잠정 |
| [G24](gaps/S2-logic.md#g24) | S2 함수 자리 타입 이름 | 잠정 |
| [G32](gaps/S2-logic.md#g32) | S2·S3 참조 타입 | 확인 (예시만 반영: 옛 `const(Ref[...])` 예시가 로드되지 않게 되므로 spec·schema-design의 예시와 3.2 설명은 먼저 고쳤다. 규칙 자체를 spec에 옮기는 것은 남음) |
| [G33](gaps/S2-logic.md#g33) | S2 람다 | 확인 |
| [G40](gaps/S2-logic.md#g40) | S2 효과 표의 `patch.*` | 확인 |
| [G46](gaps/S2-logic.md#g46) | S2 조건문 `if`와 `mod_loaded` | 확인 |
| [G48](gaps/S2-logic.md#g48) | S2·S1 데이터 값의 `int` → `fixed` | 확인 |
| [G90](gaps/S2-logic.md#g90) | S2·S4 `derived` 계산과 첫 런타임 슬라이스 (G86 보강) | 확인 |
| [G95](gaps/S2-logic.md#g95) | P2·S2 `/` 연산자와 `int`·`fixed` 혼합 (순수 함수 실행기) | 확인 |
| [G96](gaps/S2-logic.md#g96) | P2·S2 로직에서 허용하는 연산자 집합 (G95 보강) | 확인 |
| [G97](gaps/S2-logic.md#g97) | S2 로직에서 쓰는 내장 함수 (순수 함수 실행기) | 확인 |
| [G101](gaps/S2-logic.md#g101) | S2 실행 단계 상한 가드 (순수 함수 실행기) | 확인 |
| [G102](gaps/S2-logic.md#g102) | S2 순수 함수 본문에서 허용할 문장과 식 (순수 함수 실행기) | 확인 |
| [G104](gaps/S2-logic.md#g104) | S2 순수 함수 실행기의 구현 세부 (G95~G103 보강) | 확인 |
| [G107](gaps/S2-logic.md#g107) | S2·S4 스크립트가 Def 타입 바로 아래의 `const`를 읽는 길 (G103 보강, schema-design 3.3.3) | 확인 |
| [G108](gaps/S2-logic.md#g108) | S2·S9 함수 본문의 이름 오타 검사 (G102·G105 보강) | 확인 |
| [G111](gaps/S2-logic.md#g111) | S2 `with preconditions:`의 위치와 안에 올 수 있는 문장 (S2 문구를 넓힘) | 확인 |
| [G112](gaps/S2-logic.md#g112) | S2 엔진이 주는 이름과 모드 이름의 충돌 (G20의 “내장 이름은 다시 묶을 수 없다”를 바꿈) | 확인 |
| [G113](gaps/S2-logic.md#g113) | S2·S6 `require`의 모양과 메시지 평가 시점 | 확인 |
| [G117](gaps/S2-logic.md#g117) | S2 함수 효과 표의 “엔티티 쓰기” 판정과 G104 불변식의 경계 | 확인 |
| [G120](gaps/S2-logic.md#g120) | S2 `me` ❓: 커맨드에서 행동하는 진영(행위자)은 어디서 오는가 | 확인 |
| [G135](gaps/S2-logic.md#g135) | G117의 “문장으로만 부른다”와 값을 돌려주는 쓰기 함수 (G134에서 이어짐) | 확인 |
| [G136](gaps/S2-logic.md#g136) | S2 `ask`/`await`/`wait`를 일반 함수 안에 쓸 수 있는가 | 확인 |
| [G141](gaps/S2-logic.md#g141) | S2·S8 멈춘 Flow의 지역 변수에 든 런타임 값의 저장 | 확인 |
| [G142](gaps/S2-logic.md#g142) | S2 `==`가 런타임 값을 비교하는 기준 (G104 보강) | 확인 |
| [G143](gaps/S2-logic.md#g143) | S2 람다 값의 `==`와 S8 람다 저장 (G141·G142 보강) | 확인 |
| [G178](gaps/S2-logic.md#g178) | S2 난수를 부를 수 있는 곳 | 확인 |
| [G179](gaps/S2-logic.md#g179) | S2 난수 API의 모양 | 확인 |
| [G180](gaps/S2-logic.md#g180) | S2 월드 시드 | 확인 |
| [G181](gaps/S2-logic.md#g181) | S2 난수 해시 H의 구체 (spec은 후보로 SplitMix64) | 확인 |
| [G182](gaps/S2-logic.md#g182) | S2 난수열 키와 횟수 카운터의 세부 | 확인 |
| [G183](gaps/S2-logic.md#g183) | S2 `choose`가 하는 일 | 확인 |
| [G184](gaps/S2-logic.md#g184) | S2·S6 후보와 합법 수를 호스트에 주는 API, `choose` 후보가 큰 경우(S6 열린 질문) | 확인 |
| [G185](gaps/S2-logic.md#g185) | S2·S6 `cost`가 하는 일 (G120은 누가 내는지만 정함) | 확인 |
| [G186](gaps/S2-logic.md#g186) | S2 `cost`의 첫 인자 (G185에서 이어짐) | 확인 |
| [G187](gaps/S2-logic.md#g187) | S2 본문 중간의 `pay()`와 취소 때 돌려주기 | 확인 |
| [G188](gaps/S2-logic.md#g188) | S2·S6 `choose` 후보 식이 다른 매개변수를 읽을 때 (G184에서 이어짐) | 확인 |
| [G189](gaps/S2-logic.md#g189) | S2 컴프리헨션과 제너레이터 식 (G102가 미룸, schema-design 3.3.1의 🟡) | 확인 |
| [G191](gaps/S2-logic.md#g191) | S2 최상위 콘텐츠 파일의 컴프리헨션 (G189에서 이어짐) | 확인 |
| [G195](gaps/S2-logic.md#g195) | S2 `while`과 `limit` (G102가 미룸, 검증 시나리오 ①에서 드러남) | 확인 |
| [G223](gaps/S2-logic.md#g223) | spec 9절 규모 벤치의 방식과 범위 | 확인 |
| [G224](gaps/S2-logic.md#g224) | 읽기와 행동의 단계 예산 (G101 보강, 규모 벤치에서 드러남) | 확인 |
| [G225](gaps/S2-logic.md#g225) | 무거운 행동의 경고 (G224와 함께 고름) | 확인 |
| [G226](gaps/S2-logic.md#g226) | S2 실행기의 구조 (속도, G223 벤치에서 드러남) | 확인 |
| [G227](gaps/S2-logic.md#g227) | P6 스크립트 경로의 바인딩 시점 (G226 뒤 spec 대조) | 확인 |

## S3·S4 데이터

| # | 위치 | 상태 |
|---|---|---|
| [G23](gaps/S3-S4-data.md#g23) | S4·schema-design 4.2 이름 충돌 | 잠정 |
| [G27](gaps/S3-S4-data.md#g27) | S3·S4 같은 컴포넌트 참조 | 잠정 |
| [G31](gaps/S3-S4-data.md#g31) | S4 키 컴포넌트의 키 | 확인 |
| [G86](gaps/S3-S4-data.md#g86) | S4 런타임 첫 슬라이스의 범위와 입구 | 확인 |
| [G87](gaps/S3-S4-data.md#g87) | S3·S4·S8 엔티티 id 할당과 세대 | 확인 |
| [G88](gaps/S3-S4-data.md#g88) | S4 구조 변경의 반영 시점 (S4 ✅ “지연(command buffer)”을 바꿈) | 확인 |
| [G89](gaps/S3-S4-data.md#g89) | S4 `field` 초기값을 저장하는 시점 (schema-design 3.3.1 9항 보강) | 확인 |
| [G91](gaps/S3-S4-data.md#g91) | S3·S4 Ref 칼럼의 처음 값과 죽은 대상 | 확인 |
| [G92](gaps/S3-S4-data.md#g92) | S4·P2 런타임 쓰기와 `min`·`max` | 확인 |
| [G93](gaps/S3-S4-data.md#g93) | S4 기본값이 없는 칸의 런타임 값 (G67·G69·G91 보강) | 확인 |
| [G98](gaps/S3-S4-data.md#g98) | S4 기여 행의 모양·수명과 `contribs` (G90 보강) | 확인 |
| [G99](gaps/S3-S4-data.md#g99) | S4 `field`가 `derived` 상한을 넘는 경우 (schema-design 3.3.1 🟡) | 확인 |
| [G100](gaps/S3-S4-data.md#g100) | S4·S2 `derived` 순환 검사 (schema-design 3.3.1 5항) | 확인 |
| [G103](gaps/S3-S4-data.md#g103) | S4·schema-design 3.3.1 2항 `derived` 함수 안에서 형제 컴포넌트와 엔티티에 닿는 길 | 확인 |
| [G105](gaps/S3-S4-data.md#g105) | S4 `derived`의 월드 연결과 읽은 값 근거(trace) (G90·G100·G103 보강) | 확인 |
| [G106](gaps/S3-S4-data.md#g106) | S4 생성 순간에 계산하는 기본값의 순서와 실패 (G89·G90 보강) | 확인 |
| [G129](gaps/S3-S4-data.md#g129) | S4 상태에 담는 “이름 없는 값의 순서 있는 목록” (G128에서 이어짐) | 확인 |
| [G134](gaps/S3-S4-data.md#g134) | S4·S6 스크립트에서 엔티티 생성·삭제, 런타임 컴포넌트 부착·분리, 키 줄 붙이기·떼기의 표기 | 확인 |
| [G190](gaps/S3-S4-data.md#g190) | S3 전역 순회 `all(Type)`과 파이썬 `all(xs)`의 이름 충돌 (G189에서 이어짐) | 확인 |
| [G192](gaps/S3-S4-data.md#g192) | S3 스크립트의 전역 순회 (G190에서 이어짐) | 확인 |
| [G193](gaps/S3-S4-data.md#g193) | S3·S5 `query`를 쓸 수 있는 자리와 반응이 목록 변화를 아는 법 (G192에서 이어짐) | 확인 |
| [G194](gaps/S3-S4-data.md#g194) | S4 스크립트 순회에서 G88의 "죽은 것은 건너뛴다" (G88 구현 누락, 재현함) | 확인 |

## S5 반응

| # | 위치 | 상태 |
|---|---|---|
| [G145](gaps/S5-reactions.md#g145) | S5 신호의 선언과 이름, `emit` 인자와 핸들러 매개변수 | 확인 |
| [G146](gaps/S5-reactions.md#g146) | S5 `emit`을 부를 수 있는 자리 | 확인 |
| [G147](gaps/S5-reactions.md#g147) | S5 `emit` 연쇄의 상한 | 확인 |
| [G148](gaps/S5-reactions.md#g148) | S5 핸들러 실행 결과를 알리는 자리 | 확인 |
| [G149](gaps/S5-reactions.md#g149) | S5 핸들러 Flow의 행위자 (`acts_for`, G122·G139에서 이어짐) | 확인 |
| [G150](gaps/S5-reactions.md#g150) | G147 연쇄 상한을 바꾸는 자리 | 확인 |
| [G151](gaps/S5-reactions.md#g151) | S5 핸들러가 받을 엔티티가 핸들러 전에 사라짐 | 확인 |
| [G152](gaps/S5-reactions.md#g152) | S5 단계(stage)의 표기와 선언 위치 (G145에서 미룸) | 확인 |
| [G153](gaps/S5-reactions.md#g153) | G152 남의 모드 단계를 가리킬 때의 의존 | 확인 |
| [G163](gaps/S5-reactions.md#g163) | S5 멈춘 Flow의 "대상"이 무엇인가 (G151에서 이어짐) | 확인 |
| [G164](gaps/S5-reactions.md#g164) | S5 멈춘 Flow의 대상 소멸을 확인하는 때 (G163에서 이어짐) | 확인 |
| [G165](gaps/S5-reactions.md#g165) | S5 간접 대상 (G163 보강) | 확인 |
| [G166](gaps/S5-reactions.md#g166) | S5 `on_stale=`의 다른 동작 | 확인 |
| [G169](gaps/S5-reactions.md#g169) | S5 `when`의 주어: 무엇마다 거짓→참을 보나 | 확인 |
| [G170](gaps/S5-reactions.md#g170) | S5 `when`의 거짓→참 기록과 처음 상태 (G169에서 이어짐) | 확인 |
| [G171](gaps/S5-reactions.md#g171) | S5 정착 반복의 상한과 넘었을 때 (G169에서 이어짐) | 확인 |
| [G172](gaps/S5-reactions.md#g172) | S5 정착 안에서 조건을 다시 보는 시점 (G170·G171에서 이어짐) | 확인 |
| [G173](gaps/S5-reactions.md#g173) | S5 `repeat=True`가 반복하는 박자 (G172에서 이어짐) | 확인 |
| [G174](gaps/S5-reactions.md#g174) | S5 `cooldown=N`의 뜻 (G172·G173에서 이어짐) | 확인 |
| [G175](gaps/S5-reactions.md#g175) | S5 `once=True`의 단위 (G174에서 이어짐) | 확인 |
| [G176](gaps/S5-reactions.md#g176) | S5 열린 질문 `prevent_default`(막는 훅) | 확인 |
| [G177](gaps/S5-reactions.md#g177) | S5 열린 질문 "`wait` 중 Flow의 취소 규칙 세부" | 확인 |

## S6 커맨드 (C8 롤백, S10 UI의 나 포함)

| # | 위치 | 상태 |
|---|---|---|
| [G109](gaps/S6-commands.md#g109) | S6 커맨드의 이름(id) | 확인 |
| [G110](gaps/S6-commands.md#g110) | S6·C7 여러 `require`의 평가와 서로 기대는 조건 | 확인 |
| [G114](gaps/S6-commands.md#g114) | S6 커맨드 매개변수의 타입 (S6 열린 질문 “매개변수 타입의 표현 범위”) | 확인 |
| [G115](gaps/S6-commands.md#g115) | S6·C7 제출된 인자가 매개변수 타입에 맞지 않을 때 | 확인 |
| [G116](gaps/S6-commands.md#g116) | S6 커맨드 매개변수의 기본값 (spec “타입 있는 매개변수(기본값 포함)”) | 확인 |
| [G118](gaps/S6-commands.md#g118) | S6 커맨드를 로직에서 일반 함수처럼 쓰기 (handoff 남은 일 14) | 확인 |
| [G119](gaps/S6-commands.md#g119) | S6 제출자(issuer)와 S2 `me`의 관계 (S2 `me` ❓의 일부) | 확인 |
| [G121](gaps/S6-commands.md#g121) | S6 입구(②): “연결 A가 진영 P를 조종해도 되는가”의 자료와 검사는 어디에 있는가 | 확인 |
| [G122](gaps/S6-commands.md#g122) | S6 입구(②)에서 모드가 정한 검사를 부르는 자리의 모양 (G121) | 확인 |
| [G123](gaps/S6-commands.md#g123) | G122의 `acts_for`가 없는 자유 커맨드 | 확인 |
| [G124](gaps/S6-commands.md#g124) | G122의 정책 함수가 없거나 둘 이상일 때 | 확인 |
| [G127](gaps/S6-commands.md#g127) | S10 UI의 “나”(이전 `^Self`의 다섯째 역할) | 확인 |
| [G130](gaps/S6-commands.md#g130) | G122의 표기: `acts_for` 람다가 인자를 받는 방식, 정책 데코레이터 이름 | 확인 |
| [G131](gaps/S6-commands.md#g131) | S6 제출 API(`Submit`)가 돌려주는 결과의 모양 | 확인 |
| [G132](gaps/S6-commands.md#g132) | S6 커맨드 본문 실행: 쓰기가 닿는 곳과 실행 모양 (G117 2단계) | 확인 |
| [G133](gaps/S6-commands.md#g133) | C8 커맨드 단위 롤백의 방식 | 확인 |

## S7 호스트 함수

| # | 위치 | 상태 |
|---|---|---|
| [G215](gaps/S7-host.md#g215) | S7 호스트 함수의 서명 선언 방식 (S7 열린 질문) | 확인 |
| [G216](gaps/S7-host.md#g216) | S7 효과 등급과 부를 수 있는 곳 | 확인 |
| [G217](gaps/S7-host.md#g217) | S7 호스트 함수에 건네는 접근기 | 확인 |
| [G218](gaps/S7-host.md#g218) | S7 호스트 함수의 호출 모양과 이름 공간 | 확인 |
| [G219](gaps/S7-host.md#g219) | S7 호스트 함수 경계의 오류와 실행 예산 | 확인 |
| [G220](gaps/S7-host.md#g220) | S7 결정론 위반 방어 (S7 열린 질문, S9) | 확인 |
| [G221](gaps/S7-host.md#g221) | S7 호스트 함수의 저장, 바뀜 알림, 게임 없이 도는 도구 | 확인 |
| [G222](gaps/S7-host.md#g222) | S7 호스트 함수의 스크립트 타입과 C# 타입 대응 | 확인 |

## S8 저장

| # | 위치 | 상태 |
|---|---|---|
| [G140](gaps/S8-save.md#g140) | S8 저장 형식 | 확인 |
| [G144](gaps/S8-save.md#g144) | S8·C5 멈춘 Flow의 함수 본문 해시가 덮는 것 | 확인 |
| [G210](gaps/S8-save.md#g210) | S8 칼럼 삭제의 고아 데이터 영역 | 확인 |
| [G211](gaps/S8-save.md#g211) | S8 Def가 없어진 엔티티 (G210에서 이어짐) | 확인 |
| [G212](gaps/S8-save.md#g212) | S8 세이브 메타의 모드 내용 해시 | 확인 |
| [G214](gaps/S8-save.md#g214) | S8 "해당 Flow 취소 후 로드" | 확인 |

## S9 진단·CLI

| # | 위치 | 상태 |
|---|---|---|
| [G5](gaps/S9-diagnostics.md#g5) | S9 진단 | 잠정 |
| [G6](gaps/S9-diagnostics.md#g6) | S9 진단 코드 | 잠정 |
| [G75](gaps/S9-diagnostics.md#g75) | S9 `check` 출력 스키마 | 확인 |
| [G76](gaps/S9-diagnostics.md#g76) | S9 최종 Def 덤프 (“왜?” 도구 1) | 확인 |
| [G78](gaps/S9-diagnostics.md#g78) | S9 `engine check`의 종료 코드 | 확인 |
| [G79](gaps/S9-diagnostics.md#g79) | S9 `engine check`의 출력 형식 | 확인 |
| [G80](gaps/S9-diagnostics.md#g80) | S9 최종 Def 덤프의 CLI 진입점 (G76 보강) | 확인 |
| [G84](gaps/S9-diagnostics.md#g84) | S9 경고를 오류로 올리는 옵션 (S9 열린 질문, G78 보강) | 확인 |
| [G85](gaps/S9-diagnostics.md#g85) | S9 `check`와 `optional` 모드 (G38의 열린 점) | 확인 |

## X1 질문·X2 시간·X3 스택

| # | 위치 | 상태 |
|---|---|---|
| [G125](gaps/X-flow-time.md#g125) | X1 Request `accepts`와 `ask`의 반환값: 누구에게 묻는가 | 확인 |
| [G126](gaps/X-flow-time.md#g126) | G125의 남은 둘: 응답이 어느 엔티티로서인지, 시간 초과 주입값 | 확인 |
| [G128](gaps/X-flow-time.md#g128) | X3 `std.stack`: 대기 중인 행동(“나중에 해소할 것”)의 표현과 커널의 몫 | 확인 |
| [G137](gaps/X-flow-time.md#g137) | X1 질문의 선택 방법(창·지도 강조 클릭 등)을 누가 정하나, 질문에 실을 추가 정보 | 확인 |
| [G138](gaps/X-flow-time.md#g138) | X1 `ask`의 호출 모양과 돌려주는 값 (G126이 미룬 필드 이름) | 확인 |
| [G139](gaps/X-flow-time.md#g139) | X1·S6 응답 커맨드의 본문이 답한 엔티티를 아는 방법 (G126·G130에서 이어짐) | 확인 |
| [G154](gaps/X-flow-time.md#g154) | X2 엔진의 틱 신호와 지금 틱 읽기 (G145에서 미룸) | 확인 |
| [G155](gaps/X-flow-time.md#g155) | G154 `now()`를 쓸 수 있는 자리 | 확인 |
| [G156](gaps/X-flow-time.md#g156) | G154 스크립트가 `Tick`을 내는 것 | 확인 |
| [G157](gaps/X-flow-time.md#g157) | X2 `wait`에서 깨어난 Flow의 오류 | 확인 |
| [G158](gaps/X-flow-time.md#g158) | X2 `wait`로 잠든 것을 호스트가 아는 방법 | 확인 |
| [G159](gaps/X-flow-time.md#g159) | X2 `ask`가 시간을 막는지의 기본값 | 확인 |
| [G160](gaps/X-flow-time.md#g160) | X1·X2 `deadline=`의 표기 | 확인 |
| [G161](gaps/X-flow-time.md#g161) | X2·G126 `on_timeout=`이 가리키는 것 | 확인 |
| [G162](gaps/X-flow-time.md#g162) | X2 마감만 있고 `on_timeout=`이 없는 질문 | 확인 |
| [G167](gaps/X-flow-time.md#g167) | X2 스크립트의 `advance(n)` | 확인 |
| [G168](gaps/X-flow-time.md#g168) | X2 스크립트 `advance`가 호출 하나를 붙잡는 양 (G167에서 이어짐) | 확인 |

## 숨긴 정보 (9절 미결 2, G196~G206)

| # | 위치 | 상태 |
|---|---|---|
| [G196](gaps/hidden-info.md#g196) | 9절 미결 2 공개/비공개 정보 표현 (검증 시나리오 ②에서 이어짐) | 확인 |
| [G197](gaps/hidden-info.md#g197) | G196 읽기 정책의 서명과 단위 | 확인 |
| [G198](gaps/hidden-info.md#g198) | G196 거른 읽기에서 숨긴 것의 모습 | 확인 |
| [G199](gaps/hidden-info.md#g199) | G196 거른 읽기의 모양과 상태 읽기의 범위 | 확인 |
| [G200](gaps/hidden-info.md#g200) | G199 거른 읽기에서 열린 질문 | 확인 |
| [G201](gaps/hidden-info.md#g201) | G199 거른 읽기에서 제출·답의 결과 | 확인 |
| [G202](gaps/hidden-info.md#g202) | G199 거른 읽기에서 합법 수와 후보 | 확인 |
| [G203](gaps/hidden-info.md#g203) | G197 보기 정책이 없거나 둘이거나 실패할 때 | 확인 |
| [G204](gaps/hidden-info.md#g204) | G201 거른 결과에서 `cost`의 칸과 가진 양 | 확인 |
| [G205](gaps/hidden-info.md#g205) | G199 거른 `Explain`에서 못 보는 근거를 보이는 방법 | 확인 |
| [G206](gaps/hidden-info.md#g206) | G201 거른 읽기에서 `Advance` 결과의 `BlockedBy` | 확인 |
