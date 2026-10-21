# 인계 메모: 현재 상태와 다음 할 일

마지막 갱신: 2026-10-21. 브랜치 `spec-v0.5-scaffold`, 테스트 2353개 통과(커널 2200, CLI 42, 콘솔 호스트 62, 번역 49). 모더용 안내서는 `docs/modding/`(G252, 예시 모드 `samples/guide`). 작업 규칙은 저장소 루트의 `CLAUDE.md`. 슬라이스마다의 자세한 기록(무엇을 어떻게 했고 무엇을 확인했나)은 `docs/history.md`에 날짜순으로 있다. 이 문서는 지금 상태와 다음 할 일만 둔다.

## 한 줄 요약

**커널은 혼자서 게임 한 판을 돌릴 수 있다. 번역(X4)은 붙었고, UI의 행동(버튼)과 실제 호스트 엔진은 아직 없다.** 호스트 엔진은 미정이고(2026-10-19 사용자), 엔진 무관한 시험대로 텍스트 콘솔 호스트(`src/Kesa.Engine.ConsoleHost`, 실행 파일 `kesa-play`)와 예시 게임 `samples/skirmish`를 두었다.

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
| **S7 호스트 함수** | 결정 G215~G222, ①~⑦ 구현 끝(남긴 것은 아래 "다음 할 일") |
| 규모 벤치(spec 9절, G223) | 첫 판 측정함 (history의 "규모 벤치 첫 판") |
| 스크립트 속도(실행 예산 G224·G225, 실행기 구조 G226) | 끝남 (커밋 `cea92f6`, history의 "G226 실행기 구조 6단계"). 값 구조체화는 백본 ECS화와 함께 맨 끝 |
| 텍스트 콘솔 호스트(핫시트, 게임 지식 없이 공개 API만) + 예시 게임 `samples/skirmish` | 첫 판 돎. 드러난 API 구멍은 아래 "콘솔 호스트에서 드러난 구멍" |
| S10 UI가 보여 줄 값 읽기(`Call`·`ViewAs(…).Call`·영수증, G244~G248) | 끝남. UI 정의 파일은 호스트마다 제 형식(G249), 콘솔은 `ui/console.txt`(G250), 도장은 저장된 값의 것(G235·G251) |
| X4 번역 파일·포매터(`src/Kesa.Engine.Text`, `check`의 `LOC`, 콘솔 `--lang`) | 끝남 (G254~G275). 커맨드 이름은 엔진이 번역하지 않음(버튼 글은 호스트 UI 파일, G287) |
| S10 UI의 행동(`@ui_action`·`ui_state`·`submit`, 콘솔의 `[화면]` 메뉴) | 끝남 (G276~G286). 덮어쓰기·패치는 미룸 |

결정은 `docs/spec-gaps.md`의 G1~G293. 기능을 더하거나 바꾸면 `docs/modding/`의 해당 장과 9장 "아직 없는 것"도 고친다(예시는 `samples/`에서 가져오고 `GuideSampleTests`·`SkirmishTests`가 돌린다).

## 핵심 규칙 (자세한 것은 spec-gaps)

- Def 참조는 id 문자열이다(S2). id는 `모드id:경로`이고 `:` 없이 쓰면 쓴 코드의 모드 기준, 실행 중 값은 언제나 전체 이름(G258~G266). ⑤는 존재·타입·보이는 범위·`abstract` 금지만 검사한다.
- 모든 패치 연산은 로드 순서 하나로 적용한다(단계 구분 없음, G41). 서로 순서가 없는 모드의 겹치는 패치는 오류(G37).
- 오류는 **값을 처음 적은 모드**의 책임이다(G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드, 물려받은 값은 부모 쪽.
- 기본값 없는 `const`(리스트·맵 제외)는 필수다(G67). `extend`가 더한 `const`와 `keys="all"` 키 컴포넌트의 `const`는 기본값이 있어야 한다(G74·G77). 그래서 동결본은 구멍이 없다. 런타임도 생성 순간에 구멍을 막는다(G93).
- 동결본은 `abstract`를 뺀다(G70). 안 적힌 `keys="all"` 줄은 `FrozenDef.TryGetRow`가 기본값으로 채워 돌려준다.

## 다음 할 일 (사용자와 고를 것)

**진행 중 (2026-10-21): 기여 행과 선언 보너스(G294).** 보너스를 손으로 넣는 것(값 기여 행)과 있으면 붙는 것(선언 보너스)을 나눴다. 커널은 선언 보너스(붙은 컴포넌트·키 줄의 키 Def가 선언, `contribs()`가 읽을 때 모음)와 스크립트용 기여 행 넣기·빼기, 이름 있는 효과는 `std.stats`. 표기는 G295(보너스 값 타입 목록인 `const` 칸을 모음). 이름은 G296(`contrib(...)` → `Contrib`). 키 줄은 G297(`key="Medicine"`). 출처는 G298(`source`·`component`·`key`). 스크립트 내장은 G299(`add_contrib`·`remove_contrib`·`remove_contribs`). 다른 엔티티는 G300(`to=` 한 걸음). 조건부는 G301(`when=lambda src, dst`). **설계는 끝났다. 구현:** 1단계 스크립트 내장 셋과 `c.id` 끝(G299, `ScriptContributionTests`). 2단계 선언 보너스의 기본 끝(G295~G298: `Contrib`·`contrib(...)`, 로드 검사, `contribs()`가 자기 엔티티의 선언을 모음, `component`·`key`, `Explain`, `DeclaredContributionTests`). 3단계 `to=` 끝(G300, `ContributionStepTests`; 닿는 엔티티의 컴포넌트는 로드 때 안 봄, G302). 다음 4단계 `when=`(G301), 5단계 남은 스텁·안내서·판데믹 4c. 판데믹 4c 역할(과학자 치료 카드 4장)은 이것을 써 보는 예시로.

**진행 중 (2026-10-21): 예시 게임 키우기, Pandemic 따라 해 보기.** 1조각(지도와 말)과 2조각(감염·연쇄 폭발·`derived`) 끝: `samples/pandemic`, `PandemicTests` 14개. 다음 조각(4b 에픽·치료제·박멸 `@when`·승패 끝, `PandemicTests` 37개): 4c 역할(기여 행은 스크립트 내장이 없어 그때 선택). 그 뒤 치료·역할(기여 행), 확장 모드. 규칙은 일반 지식으로 쓰므로 규칙서와 대조할 것. 드러난 구멍은 `e.defid`(G293)로 메움. 신호 핸들러는 Flow가 멈춘 뒤에 돈다(의도): 순서가 중요한 연쇄는 모드가 작업 목록으로 푼다. 자세한 것은 history의 "예시 게임 키우기".


**끝남 (2026-10-21): S10 UI의 행동(G276~G286).** 화면만 바꾸는 동작은 모드 스크립트의 `@ui_action`이 쓰고 상태는 스키마의 `ui_state` 기록(연결마다 하나, 호스트가 `UiState`로 듦)에 둔다. 본문은 보는 세계로 읽기만 하고 `submit`은 적어 두기만 하며, 호스트가 `Deliver`로 낸다(응답 커맨드면 엔진이 짝지을 질문을 찾음). 화면 함수는 `WorldView.Call(ui, …)`로 기록을 읽는다. 콘솔은 `[화면]` 메뉴와 `me` 약속, 스커미시는 "유닛 고르기 → 공격". 모더용 설명은 안내서 8장 "화면 동작", 자세한 기록은 history의 "S10 UI의 행동". **미룸:** 모드가 `@ui_action`을 덮어쓰거나 패치하는 것(G276), `submit(..., request=…)`(G283, 사례가 생기면).

**끝남 (2026-10-20): X4 번역.** 화면 글의 숫자는 `text("키", n=5)`의 인자를 번역 파일이 채우므로 `str()`은 화면용으로는 필요 없다(id 조합용 `str()`은 작은 별개 항목으로 미룸). 정한 것: 형식은 Fluent `.ftl`, Linguini 0.11.0을 커널 밖 새 어셈블리에서 인터페이스 뒤에(G254, 임시 프로젝트로 덮어쓰기·CLDR 복수형·조사 함수·파서의 변수 목록과 줄 번호를 확인함), 키는 Fluent 이름 그대로 하이픈으로 잇고 점은 속성만(G255, 예시 모드 키도 바꿈), 파일은 `locale/<BCP 47 태그>/**/*.ftl`이고 모드 사이의 같은 키는 로드 순서상 나중이 이김(순서 없으면 경고, G256), 폴백은 플레이어 언어 목록 → 주인 모드의 기본 언어 → 키이고 번역 오류는 모드를 끄지 않음(G257, `check` 표 포함). Def 이름 번역 키를 정하다 **Def id 이름 공간**으로 갈라짐(사용자 제안, 구현 끝 2026-10-20, 아래 "방금 끝낸 것"): Def id는 `모드id:경로`, 경로 안은 `/`(`harbor:rigs/Brig`), 첫 조각은 만든 모드로 강제(G258), 조각은 ASCII 식별자·대소문자 구분(G259), `:` 있으면 전체 이름 없으면 내 모드 기준(G260·G261), 실행 중 값은 언제나 전체 이름이고 비교·사전 키는 전체 이름이나 `defid()`(G261), 엔진 바깥(명령 인자·호스트 API·저장)은 전체 이름만(G264), 스텁 `Literal`은 내 모드 짧은 이름과 보이는 전체 이름 함께(G265), 변환 내장은 `defid`(G266). 기존 결정 정리 끝(G21·G34·G47·G63·G72 일부 바뀜, S8 저장은 그대로). Def의 번역 키는 `def-harbor-rigs-Brig`(G262), 화면 이름은 메시지 값이고 다른 글은 Fluent 속성(G263). 번역 인자 값 표(G267, Linguini 임시 실험): int·fixed는 수, str은 그대로이되 불린 Def의 전체 이름과 같으면 그 Def의 이름 값, 엔티티는 `text()` 때 커널이 적어 둔 Def의 이름(`kesa-gone` 없음), 숨김은 `engine-hidden`, 안쪽 `text()`는 먼저 번역, bool·None은 `true`/`false`/`none`, list·dict 등은 `text()`의 실행 오류. 직접 만든 값으로는 `select`가 안 되어 Def에 따른 고르기는 함수로. 조사는 번역문의 함수 `{ JOSA($ship, "을") }`(짝 중 아무 쪽, 이름에 붙여 돌려줌, 숫자는 읽는 소리, 받침 모르면 `을(를)`, 모르는 조사는 `check` 오류), Def 속성으로 고르기는 `DEFATTR($ship, "gender")`, 뒤처리 표시 `(을)를`은 안 씀(G268). 호스트 API는 `Translations.Load`(불변) → `For(언어 목록)`의 `Translator`(`Format`·`Value`·`Text`·`DefName`, 늘 문자열), 호스트 제 `.ftl` 폴더는 먼저 읽혀 모드가 덮어씀, 다시 읽기는 `Load`를 다시 불러 갈아 끼움(G269). 제목·선택지 자리의 문자열은 키라 `tr.Label`, 속성은 `tr.Attr(값, "desc")`, 화면 값의 맨 값은 `tr.Value`, 스크립트 `deftext`는 안 만듦(G270). 엔진 예약 이름은 `engine`: 모드 id `engine`은 `MOD005`, 엔진 커맨드 `engine.pick`, 엔진·호스트 번역 키 `engine-hidden`·`engine-console-…`(G273, 앞서 쓴 `kesa` 이름을 바꿈). 선택지는 맨 값이나 `option(값, label=, desc=, style=, data=)`이고 커널이 맨 값을 감싸 호스트는 늘 option 목록, `style`은 커널이 안 읽는 이름표(G271). `LegalResponses`가 선택지마다 `engine.pick(choice=번호)` 수를 응답 수와 같은 목록에 내고 호스트는 무엇이든 `Respond`, 커널 안은 지금의 `Answer` 길(G272). 설계 결정은 끝(예시 모드의 선택지 키 `"accept"`·`"hold"` 등은 G255 관례대로 `harbor-charter-accept`처럼 바꿀 것). 구현 진행: 모드 id `engine` 예약 끝(`ManifestReader.ReservedId`), `option` 끝(`OptionValue`, `Request.Choices`는 option 목록, 꾸밈 없는 선택지는 저장에 값만, 꾸밈은 `{"option":…}` 태그, 거른 읽기가 칸 안까지 가림, 테스트 `WorldAskOptionTests`), `engine.pick` 끝(`World.PickCommand`, 인자 `by`·`choice`, `LegalResponses`가 선택지 수를 먼저 냄, `Respond`가 `Answer`로 보냄, 콘솔 호스트는 이 한 길만 씀, 테스트 `PickMoveTests`), `text()` 인자 끝(`PureBuiltins.MakeText`가 수·str·bool·None·text만 바로 받고 나머지는 월드의 `TextArgument`에 물어 엔티티면 Def를 `TextValue.EntityDefs`에 적음, 저장은 적은 것이 있을 때만 셋째 칸 `"defs"`, 거른 읽기가 숨긴 엔티티의 Def를 뺌, 테스트 `TextArgumentTests`), 번역 어셈블리 1단계 끝(`src/Kesa.Engine.Text`, netstandard2.1: `Translations.Load(content, diagnostics, hostFolder)`가 엔진 글 → 호스트 폴더 → 모드 순으로 모으고 `For(언어 목록, onError)`의 `Translator`가 `Format`·`Text`·`Label`·`Attr`·`Value`, 인자 표 G267은 문자열로 다 됨, 수는 `ExactNumber`로 십진 표기 유지, 진단 `LOC001~005`, `defaultLanguage` 태그 검사 `MOD002`, `ContentLoadResult.Order`·`ModsRoot`, 테스트 `Kesa.Engine.Text.Tests`). 번역 어셈블리 2단계 끝(번역 인자 자리의 Def·엔티티는 이름과 Def id를 든 `DefName`, 모든 언어 번들에 `JOSA`(`Josa.cs`, 숫자는 읽는 소리 10 → 십)·`DEFATTR`(없는 속성은 빈 문자열, 오류 아님), 테스트 `JosaTests`). 안내서의 번역 장은 콘솔 연결 때 함께 쓴다. `check`의 번역 검사 끝(`TranslationCheck`, `LOC006~011`, 커널이 함수 본문의 `text("리터럴", …)`을 `ContentLoadResult.TextUses`로 모음, `engine check`가 번역 파일을 읽고 검사, 샘플 두 모드에 `defaultLanguage="en"`과 en·ko 번역 파일). 콘솔 호스트 연결 끝(구멍 4번, G274 `--lang`·OS 언어 뒤 `en`·콘솔 글은 실행 파일 옆 `locale/`의 `engine-console-…`, G275 번역 없는 Def 이름은 Def id, 예시 모드 키를 G255 관례로 바꾸고 en·ko 번역, 안내서 8장 "번역" 절).
1. 그 밖은 아래 구멍과 "큰 후보".

**방금 끝낸 것 (2026-10-21):** S10 UI의 행동(위). 그 전(2026-10-20): Def id 이름 공간 구현(G258~G266, 4단계). 로더·실행 중 풀이(`DefIds`, 실행 중 짧은 이름은 지금 도는 스크립트의 모드 기준, 호스트 입력은 전체 이름만), 스텁(G265), `defid()`(G266), `DefId` 매개변수의 `choose(from_=)` 풀이와 리터럴 링크, 맨 문자열 비교 경고 `LNK005`, 예시 모드·안내서 2장·spine-spec. 콘솔 호스트는 지금 Def 전체 이름을 보이고 받는다(`--seat red=skirmish:Red`). history의 "Def id 이름 공간 구현".

**그 전에 끝낸 것:** 안내서를 쓰며 드러난 어긋남 모두. `match`는 구현을 spec에 맞춰 열었고(G253), 나머지는 이미 정한 결정에 문서·주석을 맞춤: spec S2의 `ask` 선택지는 `choices=`(G138)와 멈추는 호출 `ask`·`wait`·`advance`, S5·X2의 신호 표기 `@on(Name)`·`emit(Name(...))`·엔진 신호 `Tick`(G145·G152·G154), G248 본문의 `has()` → `u.Life is None`(고침 줄), `World.cs`의 예산 주석(사전 조건은 행동 예산, G224), `World.Save.cs`의 머리 주석(멈춘 Flow도 저장). history의 "`match`/`case`"와 "안내서에서 드러난 어긋남 고치기".

**그보다 전:** UI가 보여 줄 값 읽기(G244~G251, 콘솔 호스트 구멍 3번). 모드의 순수 함수를 `world.Call`·`ViewAs(연결).Call`로 불러 값과 영수증(`Changed`)을 받고, UI 정의 파일은 호스트의 것(G249), 콘솔은 `ui/console.txt`(G250), 도장은 저장된 값의 것(G251). history의 "UI 값 함수 부르기 1~3단계", "UI 정의 파일은 호스트의 것", "도장은 저장된 값의 것". 그 전은 멈추는 응답 커맨드(G243, 콘솔 호스트 구멍 6번). history의 "멈추는 응답 커맨드"(커밋 `a26b870`)와 "예시 게임의 공격 안 질문". 그 전은 배정 표의 남은 것(G239~G242). 그 전은 배정 표와 입구·보기 정책 다시 짜기(G236·G237·G238, 콘솔 호스트 구멍 5번). history의 "배정 표와 입구·보기 정책 다시 짜기". 그 전은 바뀜 도장(G235, 구멍 3번). history의 "바뀜 도장". 그 전은 함께 물은 질문의 거른 대상 목록(G234). history의 "함께 물은 질문의 거른 대상 목록". 그 전은 거른 합법 수에 입구 정책(G233, 콘솔 호스트 구멍 2번). history의 "거른 합법 수에 입구 정책". 그 전은 질문 단위 응답 합법 수 `LegalResponses`(G232, 구멍 1번). history의 "질문 단위 응답 합법 수". 그 전은 텍스트 콘솔 호스트와 예시 게임(작은 전투: 5×5, 두 진영, 차례는 모드의 `ask` 루프, 명중 때 맞은 쪽에 버팀/후퇴 질문). history의 "텍스트 콘솔 호스트와 예시 게임". 실행: `kesa-play samples/skirmish --seat red=skirmish:Red --seat blue=skirmish:Blue [--seed N]`(G236·G264 뒤). 그 전은 속성 이름 오타의 로드 검사 1차(G228)와 같은 엔티티 안의 `derived` 순환 로드 검사(G229, schema-design 3.3.1 5항을 고침), 컴포넌트·신호 이름이 보이는 모드 안에서 내장을 이기는 규칙과 스텁의 이름 충돌 수정(G230), 생성자에서 속성을 읽는 것의 로드 검사(G228·G230 보강), 속성 오타 검사의 함수 반환 타입 추론(G231), 덤프의 `patches`를 값을 만든 패치만으로(G76 구현 보강). history의 "속성 이름 오타의 로드 검사 1차", "`derived` 순환의 로드 검사", "스키마 이름과 내장의 충돌". 그 전은 문서와 큰 파일 정리(handoff·spec-gaps 나누기, 큰 소스 파일 partial 나누기와 안쪽 타입 빼기, 커밋 `c621954`~`7301b1b`). 자세한 것은 history의 "문서와 큰 파일 정리". 그 전은 S7 호스트 함수(G215~G222)와 스크립트 속도(G223~G227, history의 "S7 호스트 함수와 스크립트 속도"). 속도는 여기서 멈추기로 했다(남은 큰 몫은 값 구조체화, 백본 ECS화와 함께 맨 끝).

### 콘솔 호스트에서 드러난 구멍 (2026-10-19, 실제로 우회한 것. 결정 아님)

호스트 코드(`ConsoleGame.cs`)의 "구멍:" 주석 자리다. 하나씩 사용자와 정한다.

1. ~~응답 커맨드의 합법 수를 질문 하나에 대해 물을 수 없다.~~ **해결(G232):** `LegalResponses(질문, by, 최대)`. 콘솔 호스트의 람다 들여다보기를 지웠다.
2. ~~이 엔티티로 행동할 수 있는 연결이 누구인지 물을 수 없다.~~ **해결(G233):** `ViewAs(연결).LegalMoves`가 `acts_for` → 입구 검사(지금은 배정 표, G237)도 거른다. 콘솔 호스트는 자리마다 `ViewAs`로 할 수 있는 것을 모으고 그 자리로 바로 낸다(자리 차례로 내 보기를 지웠다). 그때 본 작은 후보(함께 물은 질문의 `Who`에 답할 수 없는 대상도 보임)도 **해결(G234):** 거른 질문의 `Who`는 답할 수 있는 대상만.
3. ~~무엇이 바뀌었나 구독이 없다.~~ **해결(G235):** 바뀜 도장 `Version()`·`Version(id)`·`Version(id, 이름)`. 콘솔 호스트는 도장이 그대로인 엔티티 줄을 다시 읽지 않는다. 남은 몫이던 거른 읽기(`ViewAs`)와 남의 엔티티를 읽는 `derived`는 도장에 담지 않고 `Call` 영수증이 맡기로 함(G251). 콘솔 덤프는 `derived` 칸이 있는 엔티티를 매번 다시 읽는다.
   - **진행 중(3에서 이어짐): UI가 보여 줄 값 읽기.** G244로 방향을 정함: 호스트용 문자열 경로를 두지 않고 모드 스크립트의 순수 함수가 값을 만들며, 호스트는 그 함수를 불러 값과 영수증(읽은 곳)을 받는다. 부를 수 있는 함수는 모드의 아무 순수 함수(G245), 영수증은 닫힌 것으로 `@when`의 기록과 같은 내용(G246), API는 `world.Call(이름, 값들…)`→`CallResult{Value, Changed(world)}`와 `world.CheckCall`, 실패는 예외(G247), `ViewAs(연결).Call`은 함수 안의 읽기를 보는 쪽 세계로 돌린다(G248). 구현: 1단계 `Call`·`CheckCall`·`CallResult` 됨(`World.Call.cs`, `RUN023`), 2단계 영수증 `CallResult.Changed(world)`와 출처 도장 됨, 3단계 `ViewAs(연결).Call` 됨(`_viewer`로 읽는 길마다 거름, 정책·derived 안은 풀림, 배정 표 횟수도 영수증에). UI 정의 파일은 호스트마다 제 형식이고 엔진은 읽지 않는다(G249). 4단계 콘솔 호스트 됨: `ui/console.txt`(G250, `ConsoleUi.cs`), 스커미시의 `status`·`board`·`units`(`defs/view.py`). 거기서 본 구멍 후보: 순수 함수에 `str()`이 없어 숫자를 글에 넣을 수 없다(스커미시는 사전으로 돌려줘 콘솔이 값 글로 보임). 번역(4번)과 함께 볼지 정할 것. 다음: `str()`과 숫자가 든 글(번역 X4와 함께 볼지부터)을 볼지, 다른 구멍으로 갈지 사용자에게 묻는다. 아래는 그 전 분석.
   - **옛 분석: 호스트용 경로 읽기.** 이전 엔진(`kesa0v0/unity-kesa-engine`)은 UXML `binding-path="Self.X.Y"`를 `DataPathResolver`가 풀어 R3로 갱신했다. 잘한 점(UI 파일은 경로만, 참조가 바뀌면 다시 붙음)은 잇고, 문제(필드마다 모더가 `TryResolveBinding`을 손으로, 로드 때 검사 없음, 커맨드 한가운데서 즉시 알림, 조용히 멈추는 바인딩·구독 누수, 숨긴 정보 거름 없음)는 피한다. 지금 호스트는 핸들(`Member`·`Get`)로 한 단계씩 따라가야 하고, 경로는 모드 스크립트 안에서만 된다. 방향: 경로를 로드 때 스키마로 컴파일해 (엔티티, 칸) 읽기 집합을 얻고 G235 도장으로 다시 읽을지 정한다(S3·S10 4항). 2026-10-19 분석, 결정 아님.
4. ~~번역이 없다(X4).~~ **해결(G254~G275):** 콘솔이 제목·선택지·`require` 메시지·엔티티 이름·자기 글을 번역해 보인다(`--lang`). 수 목록의 커맨드 이름(`skirmish.EndTurn`)은 엔진이 번역하지 않기로 함(버튼 글은 호스트 UI 파일, G287).
5. ~~연결 이름을 모드가 박아 둔다.~~ **해결(G236·G237·G238):** 커널의 배정 표(`World.Seat`·`Unseat`·`SeatOf`·`SeatsOf`, 저장됨), 입구는 표만 봄(`may_act`·`@submit_policy` 없앰), 보기 정책은 `may_see(viewer, entity, component)`. 로비 후보는 엔진 컴포넌트 `Seatable`과 `World.SeatChoices()`(G239·G240). 콘솔 호스트는 `--seat red`면 후보가 생길 때 화면에서 고르게 묻고(작은 로비), `--seat red=Red`면 묻지 않고 그 Def의 엔티티에 앉힌다(테스트·자동 실행용). 스크립트는 `controller(e)`로 사람·AI·빈자리만 읽는다(G241, 호스트는 `Seat(e, issuer, kind)`). **미룸:** 한 엔티티에 연결 여럿(공동 조종). 지금은 호스트가 한 꼬리표를 여러 사람에게 나눠 주면 된다(규칙이 그중 누가 냈는지 가르는 것만 안 됨). 사례가 생기면 `Seat`가 바꾸나 더하나, `SeatOf`의 목록화, 섞인 종류의 `controller()`, 저장 형식을 함께 정한다. API 이름은 지금 그대로 확정(G242: 호스트는 자리 계열, 스크립트는 `controller`). G236에서 이어서 정할 것은 공동 조종(미룸) 말고 모두 끝났다.
6. ~~응답 커맨드 본문에서 `ask`를 하면 "not supported yet" 실행 오류다.~~ **해결(G243):** 멈춘 응답 본문은 새 Flow가 되고 바깥은 그것이 끝난 뒤 `r.response`·`r.by`로 이어 간다(여러 겹 가능). 끝까지 못 가고 버려지면 `r.interrupted`가 참, 불러오며 취소되면 바깥도 함께 취소. 예시 게임도 `Attack` 본문에서 맞은 쪽에 묻게 고쳤다(우회용 `Combat` 컴포넌트를 지움).

큰 후보:

- 위 "콘솔 호스트에서 드러난 구멍"을 하나씩.
- 호스트 함수에서 남긴 것: 인자 8개 초과(2026-10-21 사용자: 사례가 생길 때까지 넘김. 필요하면 `HostOverloads.cs`의 오버로드를 16개까지 늘리면 되고, 이름으로 꺼내기 `call.Int`는 G215에서 접은 이유가 그대로라 하지 않는다), 호스트 함수를 `Cond`·`Action` 칸의 값으로 쓰는 것은 끝(2026-10-21 G291·G292, history의 "호스트 함수를 `Cond` 칸의 값으로"와 "`Action` 칸의 값으로"), 호스트 창구의 `choices`.
- 속도의 작은 후보(이득은 작을 것으로 추정): 람다 없는 함수의 지역 변수 표 다시 쓰기(부를 때마다 약 64B), 문장 수준 호출(`x = f()`)의 결과 칸 번호, 컴프리헨션 이름 공간 프레임 재사용, `contribs()` 빈 목록.
- ~~영수증을 (엔티티, 컴포넌트) 단위로.~~ **끝(2026-10-21, G290 확인):** `Record(id, 이름)`·`CallRead`, 쓸어 보기 테스트 `CallReceiptNarrowingTests`. `@when` 읽기 기록도 같은 단위로 바꿨다(`Touch(id, 이름)`, `_whenReaders`는 엔티티 → 칸 이름 → 조건, 빠뜨림은 `WhenTrackingTests`의 전체 훑기 견주기, 덜 평가하는지는 `WhenNarrowingTests`). history의 "`@when` 읽기 기록을 (엔티티, 컴포넌트) 단위로".
- S10 UI 연동(호스트 엔진 결정과 묶임). 아래 "S10 논의 메모" 참고.

### S10 논의 메모 (2026-10-19, 논의만, 결정 아님)

- **무엇이 급한가:** 남은 일·"필요해지면 할 것"보다 외부 연결(S10·X4·실제 호스트)이 급하다고 제안했다. 남은 일은 미뤄도 비용이 늘지 않지만 커널 API의 구멍(구독, 합법 수 그리기, `ask` 이어 가기, 번역)은 화면을 붙여야 드러나고 늦을수록 비싸다. "필요해지면 할 것"도 실제 게임이 돌아야 사례가 생긴다. **호스트 엔진은 미정(2026-10-19 사용자 답)이라 텍스트 콘솔 호스트부터 했다.** 정해지면 그 엔진에 얇은 연결부터, 아니면 엔진 무관한 것(X4, 텍스트 콘솔 호스트로 구독·합법 수·`ask` 응답을 실제로 써 보기, 커널 안의 읽기 집합 구독 API)부터.
- **버튼 = 커맨드인가:** 아니다(spec S6 "동작 종류", S10 3항에 이미 있음). 기준은 "게임 상태를 바꾸나". 버튼은 시뮬레이션 커맨드·응답 커맨드·`@ui_action`(클라이언트 로컬, 상태는 읽기만, 커맨드 제출 가능)·호스트 함수 중 하나를 가리킬 뿐이다. 사례: QU가 초안 단계마다 커맨드를 써서 초안이 세이브·네트워크로 샜다(use-cases U12), RimWorld는 버튼(`Command` Gizmo)이 델리게이트로 상태를 바로 고쳐 멀티플레이 모드가 일일이 동기화 패치를 해야 했다(일반 지식).
- **UI도 모드 가능하게 하려면:** 사용자는 UI 형태와 동작도 모드로 고칠 수 있게 하려 한다. 제안한 방향: `@ui_action`이 "UI용 유사 커맨드"다. **모양은 커맨드와 같게**(이름, 타입 있는 매개변수, 로드 때 검사, UI 정의 파일에서 이름으로 가리킴, 모드가 덮어쓰기·패치) **의미는 로컬**(로그·저장·네트워크·롤백·권한 없음, 게임을 바꾸려면 커맨드를 제출). 사례: CK3는 GUI 파일이 모드 가능하고 버튼이 UI 로컬 변수(`GetVariableSystem`)와 모더가 선언한 `scripted_gui`(`effect`·`is_shown`·`is_valid`, 멀티플레이 동기화)로 나뉜다(일반 지식, 세부 미확인). S10을 시작할 때 이 방향을 첫 제안으로 올린다.
- **S10에서 정할 것:** UI 로컬 저장소의 모양, `@ui_action` 본문이 할 수 있는 범위, 레이아웃(UI 정의) 파일 형식, `command=`·`ui_action=`를 두 속성으로 둘지 한 문법으로 합칠지. 회색 지대: 일시정지·게임 속도(멀티에서는 모두 같아야 함), 저장·불러오기·종료(호스트 API 호출로 보는 것이 자연스러움), 채팅·지도 핑(남에게 가지만 규칙엔 영향 없음).
- 아래 "남은 일"의 작은 것들.

## 남은 일 (2026-10-19 점검, 우선순위 순)

1. ~~실행 중 이름 오류의 "혹시 …?" 후보가 모드 전역을 빼먹는다.~~ **고침(2026-10-21):** `IScriptGlobals.Names`로 모드 전역도 후보에 넣음. 함께 `Suggest.Distance`가 이웃 두 글자 맞바꾸기를 한 번으로 셈(`lfet` → `left`, 전에는 힌트 없음).
2. ~~종료 코드 3(엔진 내부 예외) 경로의 테스트가 없다.~~ **고침(2026-10-21):** `CommandsTests.AnEngineExceptionExitsThreeAndSaysItIsABug`(`LoadAndReport`를 internal로 열어 보고 단계에서 던짐).
3. ~~단계 순서 덤프(S9 "왜?" 도구, G80).~~ **끝(2026-10-21, G289):** `engine stages <mods-dir> [--host <file>]`(`Reporting/StageReport.cs`). `dump`에서 특정 Def만 고르는 옵션은 여전히 미룸(G80).
4. ~~파일 정리의 작은 나머지.~~ **끝(2026-10-21):** 동작 클래스 파일에 얹혀 있던 타입 아홉을 제 파일로, `src/`의 쓰지 않는 using을 모두 지움(방법은 history의 "파일 정리의 작은 나머지").

**필요해지면 할 것** (실제 모드 사례가 생기면 정한다):

- 따옴표 없는 Def 참조와 속성 접근 표기(`Resource.Wood`, G63).
- 로직 연산자 확장(G96): 지금은 `**`, 비트 연산, `@`를 거부한다. 복리·레벨 곡선이 필요해지면 `int ** 음이 아닌 int`부터, 플래그가 필요하면 `int` 비트 연산.
- S8 `migrate`(실제 모드 업데이트 사례).
- 값 구조체화와 백본 ECS화(저장 구조를 인터페이스 뒤로, G226, 맨 끝).

## 새 세션에서 시작하는 법

1. `git pull` 후 `dotnet test`로 2282개 통과(커널 2166, CLI 42, 콘솔 호스트 25, 번역 49)를 확인한다. 클라우드 세션에서 .NET 9 SDK를 내려받지 못하면 `apt-get install -y dotnet-sdk-10.0`으로 깔고, 저장소의 `global.json`을 피하려고 `cd /tmp && DOTNET_ROLL_FORWARD=Major dotnet test /home/user/game-engine/Kesa.Engine.slnx`로 돌린다.
2. `CLAUDE.md`, 이 문서, `docs/spec-gaps.md`(색인, 고를 항목의 영역은 `docs/gaps/`의 그 파일), 고를 항목에 맞는 `docs/spine-spec.md` 절을 읽는다. 실행기 구조는 `Script/Execution/PureEvaluator.cs`(입구)·`Session.cs`(+`.Calls`·`.Preconditions`·`.Stepping`)·`Frame.cs`·`Block.cs`·`PureEvaluator.Nodes.cs`(식 노드)·`PureEvaluator.Statements.cs`(문장 노드)·`FrameLayout.cs`·`LocalTable.cs`다. 고른 영역의 지난 기록이 필요하면 `docs/history.md`에서 그 절만 찾아 읽는다(전부 읽을 필요 없음). 옛 커맨드 첫 슬라이스의 읽을 곳: 커맨드 코드는 `Runtime/World.Submit.cs`(제출), `Runtime/World.Commands.cs`(인자·사전 검사), `Runtime/World.Undo.cs`(롤백), `Runtime/World.Structure.cs`(구조 변경 내장), `Script/Execution/PureEvaluator.cs`(`RunCommand`)·`Frame.cs`(`Writable`), `Script/Execution/FunctionEffects.cs`, `Loading/CommandSignatures.cs`다.
3. 위 "다음 할 일 (사용자와 고를 것)"에서 사용자와 다음 항목을 고른다.
