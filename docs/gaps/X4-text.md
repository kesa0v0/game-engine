# spec 빈틈 상세: X4 로컬라이제이션

색인과 상태 규칙은 `docs/spec-gaps.md`. 이 파일은 G 번호마다 무엇이 비어 있었고 어떻게, 왜 정했는지의 전문이다.

## G254

**위치:** X4 번역 파일 형식과 포매터 (spec X4 "방식 🟡, 번역 파일 형식 미정", 콘솔 호스트 구멍 4번)

**무엇이 비어 있었나:** 커널에는 키와 인자를 들고 다니는 `text("키", 이름=값)`(G113)와 `mod.py`의 `defaultLanguage`(G12)만 있고, 키를 글로 바꾸는 쪽(번역 파일, 자리 표시자·복수형 포매터, 폴백, `check`의 번역 검사)이 없었다. spec X4는 원칙(P1~P4)만 정하고 방식은 "ICU MessageFormat 부분집합, 파일 형식 미정"으로 남겼다. 콘솔에는 질문 제목과 `require` 메시지가 키 그대로 보였다.

**어떻게 정했나:** **번역 파일은 Project Fluent 형식(`.ftl`)이고, 읽고 채우는 일은 Linguini(Fluent의 C# 구현)에 맡긴다.**
- 번역은 커널 밖의 새 어셈블리가 맡는다. 호스트와 CLI(`check`)가 참조하고 커널은 지금처럼 `TextValue`(키와 인자)만 든다(X4 P2: 시뮬레이션은 번역문을 읽지 않는다). 그래서 이 어셈블리는 결정론 분석기(`Determinism.props`) 밖이다.
- Linguini는 버전을 고정하고(처음은 0.11.0) 우리 인터페이스 뒤에 둔다. 호스트·모드 코드는 Linguini 타입을 보지 않는다. 라이브러리가 멈추면 고정 버전 유지 → 포크(MIT 또는 Apache-2.0) → 쓰는 부분만 다시 만들기 순으로 물러서며, 어느 경우에도 모드의 `.ftl` 파일은 그대로다.
- 우리가 쓰는 것: 모드별로 번역 파일을 찾고 로드 순서대로 모으기, 덮어쓰기와 충돌 규칙, 보이는 범위, 오류의 책임(G64), 폴백(X4 P3), `check`의 번역 검사, 조사 같은 언어별 사용자 함수. Linguini가 하는 것: `.ftl` 파싱(위치 포함), 자리 표시자·`select`·복수형(CLDR) 채우기.
- 이어서 정할 것: 키 표기(Fluent 식별자에는 점이 없고 점은 속성이다, 지금 모드들은 `harbor.charter`처럼 점을 씀), 파일 위치, 덮어쓰기 규칙, 폴백과 `check`, 숫자 인자의 모양(`fixed`), 조사 함수, 엔진 API.

**시험(2026-10-20, 임시 프로젝트, 저장소에 남기지 않음):** Linguini 0.11.0으로 (1) 같은 키를 `AddResource`로 다시 넣으면 `OverrideFluentError`로 거부, `AddResourceOverriding`은 그 키만 덮어씀, (2) CLDR 복수형: 러시아어 1·2·5·21이 `one`·`few`·`many`·`one`, 한국어는 `other`뿐, (3) 사용자 함수 `JOSA($name, "을")`로 `슬루프를`·`갈매기가`, (4) 파서만 따로 써서 글마다 변수 목록(`gold`, `ship`)과 오류 줄 번호, 빠진 인자는 `금화 {$gold}`와 `Unknown variable` 오류, (5) 0.8.1→0.11의 깨지는 변경은 호스트 API 몇 곳이고 `.ftl` 문법은 같음. 0.12(미출시)는 `CultureInfo`를 자체 언어 id로 바꿈.

**이유:** 후보는 Fluent+Linguini, gettext `.po`+Karambolo.PO(번역 도구가 가장 넓지만 자리 표시자 채우기와 `select`를 직접 써야 함), MessageFormat(.NET, 파일 형식은 따로), 우리 콘텐츠 언어(로더는 공짜지만 번역 도구가 없음), ICU4N(alpha, MessageFormat·PluralRules 미포팅으로 탈락)이었다. Fluent는 형식과 문법을 한 번에 주고, 모드를 받는 게임 엔진 OpenRA와 RobustToolbox(Space Station 14)가 Linguini를 쓴다(두 저장소의 프로젝트 파일로 확인). Linguini는 별이 42개이고 기여가 거의 한 사람(Ygg01)이라는 위험이 있지만 NuGet 내려받기 약 535만, 2026-10 기준 활발한 커밋이고, 형식은 Mozilla 표준(문법 1.0)이라 라이브러리와 따로 살아남는다. 사례: RimWorld는 XML Keyed·DefInjected와 언어별 C# LanguageWorker, 패러독스는 YAML 비슷한 `l_english:`과 글 안의 월드 읽기 `[ROOT.GetName]`(X4가 막은 방식, U14), Godot은 `.po`를 협업용으로 권함(일반 지식과 Godot 문서).

**상태:** 확인

## G255

**위치:** G254 번역 키의 표기 (Fluent 식별자에는 점이 없음)

**무엇이 비어 있었나:** 스크립트는 `text("harbor.charter")`처럼 점으로 이은 키를 썼지만, Fluent의 글 이름은 `[a-zA-Z][a-zA-Z0-9_-]*`이고 점은 속성(`harbor-charter.tooltip`)을 뜻한다. 스크립트의 키와 번역 파일의 이름을 어떻게 잇는지 없었다.

**어떻게 정했나:** **스크립트에 적는 키는 Fluent 이름 그대로다.** 이어 붙이기는 하이픈(`text("harbor-charter", gold=5)`), 점은 Fluent처럼 속성만(`text("harbor-charter.tooltip")`, 글 이름 뒤에 하나). 엔진은 키 문자열을 바꾸거나 모드 접두사를 붙이지 않는다. 모드 id 접두사(`harbor-`)는 관례이고, 서로 순서 없는 모드끼리 같은 키를 정의하면 어떻게 하는지는 덮어쓰기 규칙에서 정한다. 이것은 키 문자열의 규칙일 뿐이고 스크립트의 데이터 경로(`house.Purse.gold`)와 무관하며, 번역 파일의 `{ $gold }`는 넘겨받은 인자만 읽는다(X4, U14). 예시 모드의 키도 바꿈(`harbor-charter`, `harbor-already-open`, `skirmish-already-started`).

**이유:** 스크립트의 문자열로 번역 파일을 그대로 찾을 수 있고 번역자와 모더가 같은 이름을 본다. 엔진이 점을 하이픈으로 바꾸면 한 키에 표기가 둘이 되고 속성을 가리킬 길이 없어진다. 접두사를 엔진이 붙이면 남의 모드를 번역하는 모드가 대상 모드를 따로 적어야 하고 파일에 보이는 이름과 실제 이름이 달라진다. Space Station 14는 Fluent 키를 하이픈으로 길게 잇는 관례(`comp-gas-pump-ui-pump-status`), RimWorld Keyed·패러독스 키는 전역 이름에 모더가 접두사를 붙이는 관례다(일반 지식). 점을 속성으로 남기면 spec X4의 "규칙으로 도출한 키(`Scout.label`)"를 Fluent의 글과 속성(`Scout = 정찰병` / `.desc = …`)으로 옮길 길이 열린다(따로 정함).

**알아 둘 것:** 커맨드(`banking.Ask`)·단계(`harbor.market`)·호스트 함수·`Call`의 함수 이름은 지금처럼 점으로 모드를 가른다. 번역 키만 하이픈이다(Fluent 문법 때문).

**상태:** 확인
