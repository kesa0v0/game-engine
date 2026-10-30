# 8. 화면에 보일 값

화면에 무엇을 보일지는 **모드의 순수 함수**가 만든다. 호스트(콘솔, Unity, Godot)는 그 함수를 이름으로 불러 값을 받아 그릴 뿐이고,
게임 규칙을 모른다.

## 화면 함수

`samples/guide/harbor/defs/view.py`:

```python
# 화면에 보일 값. ui/console.txt가 자리마다 부르고, 인자 me는 그 자리가 앉은 상회다
def purse(me):
    return {"gold": me.Purse.gold, "fleet": me.Fleet.strength, "day": town().Port.day}

def ships(me):
    return [{"ship": s, "hp": s.Hull.hp, "max": s.Hull.max_hp} for s in me.ships]
```

- 특별한 표시(데코레이터)가 필요 없다. 모드의 **아무 순수 함수**나 `모드.함수`(`harbor.purse`) 이름으로 불린다.
- 부를 수 없는 함수: `@command`·`@on`·`@when`·`@ui_action`, 월드를 쓰는 함수, 난수를 쓰는 함수, UI 기록에 쓰는 함수(그런 함수를 부르는 함수도, 아래 "화면 동작"). 호스트가 띄울 때
  이유와 함께 알려 준다(`RUN023`, 이름 오타면 `RUN001`과 "혹시 …?").
- 돌려줄 수 있는 것: 수, 문자열, 참거짓, 엔티티, 그리고 이들의 리스트·사전.
- 함수는 **그 연결이 보는 세계**에서 돈다(7장). 그래서 숨긴 정보를 따로 거를 필요가 없다. 대신 "안 보임"(`x.C is None`)을 다룬다.
- 호스트는 함수가 읽은 곳을 기록해 두고 그곳이 바뀌었을 때만 다시 부른다. 모더가 신경 쓸 것은 없다.
- 숫자를 글에 넣는 `str()`은 없다. 글로 보이려면 `text("키", gold=20)`을 돌려주고 번역문의 `NUMBER`로 서식을 정한다(아래 "번역"). 사전으로 돌려주면 호스트가 `{gold: 20, fleet: 7}`처럼 보인다.

## 호스트의 UI 파일

어떤 함수를 어디에 어떻게 그릴지는 **호스트마다 제 형식의 UI 파일**에 적는다(Unity는 UXML/USS, Godot은 장면, 콘솔은 아래).
엔진은 UI 파일을 읽지 않는다. 모드는 대개 게임이 고른 호스트 하나를 목표로 한다.

### 콘솔: `ui/console.txt`

`samples/guide/harbor/ui/console.txt`:

```
# 콘솔 호스트가 자리마다 차례로 부른다. 인자는 그 자리가 앉은 상회
harbor.purse
harbor.ships
harbor.plans
```

- 모드 폴더의 `ui/console.txt`. 한 줄에 `모드.함수` 하나, `#` 뒤와 빈 줄은 무시한다.
- 콘솔은 자리(연결)마다, 그 연결이 앉은 엔티티를 인자로 줄 순서대로 부른다.
- 문자열은 그대로, 리스트는 항목마다 한 줄, 그 밖은 값 글(`{gold: 20, fleet: 0, day: 0}`, 엔티티는 `#2 갈매기 상회`처럼 번호와
  번역된 Def 이름, 아래)로 보인다.
- 띄울 때 줄마다 부를 수 있는지 확인해, 틀리면 `harbor/ui/console.txt:2: 이유`를 보이고 멈춘다. 부르다 실패한 줄은 그 자리에 오류 줄이
  나오고 나머지는 그려진다.
- 파일이 없으면 콘솔은 모든 엔티티의 모든 컴포넌트를 늘어놓는다(디버그용).

스커미시의 판 그리기(`samples/skirmish/core/defs/view.py`):

```python
def board_row(y, me):
    row = ""
    for x in range(SIZE):
        u = unit_at(x, y)
        cell = "." if u is None else glyph(u, me)
        row = cell if x == 0 else row + " " + cell
    return row

def board(me):
    return [board_row(y, me) for y in range(SIZE)]
```

```
[red] #1 빨강
  . s a s .
  . . . . .
  . . . . .
  . . . . .
  . S A S .
```

### Unity: UXML

Unity 호스트는 UXML 파일(`samples/skirmish/core/ui/screen.uxml`)로 화면을 정한다. 모양은 UXML, 값은 위의 화면 함수가 만든다. 요소 속성을 함수 값에 묶고, 눌림을 커맨드나 화면 동작에 잇는다.

```xml
<ui:VisualElement name="screen" params="house" seat="house">          <!-- house에는 호스트가 그 연결이 앉은 진영을 넣는다 -->
  <ui:Label bind-text="skirmish.turn_number(house)" />                <!-- 속성 text를 함수 값으로 -->
  <ui:Button on-click="skirmish.EndTurn(house)" />                    <!-- 커맨드면 내고, 응답 커맨드는 열린 질문에 답한다 -->
  <ui:VisualElement for="row in skirmish.units(house)">               <!-- 리스트의 항목마다 자식 한 벌 -->
    <ui:VisualElement class="card" bind-class-selected="skirmish.is_chosen(row.unit)" bind-class-acted="row.acted">
      <ui:Label bind-text="row.hp" />                                 <!-- 사전의 키는 점으로 읽는다 -->
      <ui:Button on-click="skirmish.select(house, row.unit)" />       <!-- 화면 동작 -->
```

- 식은 `모드.함수(인자)` 하나나 변수 하나다. 인자는 변수나 고정값(숫자, 작은따옴표 글자, `True`·`False`·`None`)이고 호출 안의 호출과 연산자는 없다. 계산은 함수가 한다.
- `bind-text`·`bind-display`·`bind-enabled`·`bind-value`·`bind-opacity`·`bind-image`, `bind-class-<이름>`(참이면 클래스를 붙임), `bind-class`(함수가 돌려준 글자나 글자 목록이 클래스).
- 눌러도 되는지는 호스트가 `require`·`choose`와 입구를 미리 보아 버튼을 회색으로 한다.
- 다른 파일을 `<Include src="…" 이름="식" />`로 끼우면 그 파일이 `params`로 선언한 이름의 값을 넘겨야 한다.
- 아직 없는 것: 인라인 `style`.

#### 모양 (USS)

모양은 모드 폴더의 USS 파일로 준다. UXML에 `<Style src="…" />`로 붙이면 그 파일의 맨 위 요소에 붙는다. skirmish는 `ui/screen.uss`를 붙인다.

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
  <ui:Style src="skirmish/ui/card.uss" />
  <ui:VisualElement name="screen" params="house" seat="house"> … </ui:VisualElement>
</ui:UXML>
```

```css
/* skirmish/ui/card.uss */
:root { --accent: #ffb000; }
.card { flex-direction: row; background-color: rgb(30, 34, 40); }
.card:hover .name { color: var(--accent); }
.card.selected { border-color: var(--accent); border-width: 2px; }
.icon { background-image: url("skirmish/art/icon.png"); }
```

- 문법은 Unity USS 그대로다(Unity 문서를 보면 된다): 타입·`.클래스`·`#이름`·`*`, 이어 붙이기, 쉼표 목록, 자손(띄어쓰기)·자식(`>`), `:hover`·`:active`·`:inactive`·`:focus`·`:disabled`·`:enabled`·`:checked`·`:root`, Unity가 아는 속성 전부, `--변수`와 `var()`.
- 경로(`<Style src>`와 `url()`)는 이미지·`Include`와 같은 공간이다: 모든 모드 폴더를 겹친 경로이고 같은 경로는 나중에 로드된 모드가 이긴다. 시트 파일 기준의 상대 경로가 아니다. 이름이 우연히 겹치지 않게 자기 모드 id 폴더 아래에 둔다.
- UI Builder가 저장하는 `project://database/Assets/…?fileID=…` 같은 Unity 프로젝트 경로, `/`나 드라이브로 시작하는 경로, `..`는 받지 않는다(G365). 진단이 "Unity 프로젝트 경로다, 모드 안의 경로로 쓰라"고 알려 주니 `ui/screen.uss`처럼 고쳐 쓴다. `Include`의 `src`도 같다(이쪽은 다른 UXML 문제처럼 화면을 막는다).
- 의존하는 모드의 시트도 그 경로로 붙인다. 조금 고치려면 자기 시트를 뒤에 하나 더 붙이고, 같은 경로에 파일을 두면 통째로 바뀐다(스킨 모드).
- 우선순위는 Unity 규칙이다: 명시도가 높은 규칙이 이기고, 같으면 요소에 더 가까이 붙은 시트, 같은 요소면 나중에 붙은 시트가 이긴다. 게임이 화면 맨 바깥에 붙인 기본 모양과 명시도가 같으면 모드 시트가 이긴다.
- 없는 것: `@import`(시트마다 `<Style src>`를 쓴다), `resource()`(게임 빌드 안을 가리키므로 `url()`을 쓴다), `url()`로 그림이 아닌 파일(폰트 등) 읽기.
- 잘못은 화면을 막지 않는다. 모르는 속성(`colr` → "혹시 'color'?"), 깨진 문법, 못 찾은 파일·그림, 바꿀 수 없는 값은 그 줄이나 규칙만 빠지고 오류 목록(`Errors`)에 `파일:줄`과 함께 담긴다.

#### 창

창은 평범한 요소에 `window`를 붙인 것이다. 열렸는지는 UI 기록에 두고 `bind-display`로 묶는다. 닫는 것도 화면 동작이 UI 기록을 바꿔서 한다. skirmish 화면(`screen.uxml`, 동작은 `defs/ui.py`)에서:

```xml
<ui:VisualElement name="unit-info" window="normal"
    bind-display="skirmish.inspected()" on-close="skirmish.close_inspect()">   <!-- 연 유닛이 있으면 열림 -->
  <ui:Label class="title" text="unit" window-drag="true" />                    <!-- 이것을 끌면 창이 움직인다 -->
  <ui:Label bind-text="skirmish.inspected_hp()" />
</ui:VisualElement>
<ui:VisualElement name="confirm" window="modal"
    bind-display="skirmish.asking_end_turn()" on-close="skirmish.cancel_end_turn()">  <!-- 대답할 때까지 다른 것을 못 누른다 -->
  <ui:Button text="end turn" on-click="skirmish.EndTurn(house)" />
</ui:VisualElement>
```

```python
@ui_action
def ask_end_turn():
    with preconditions:
        require(playing(), text("skirmish-over"))
        require(not asking_end_turn(), text("skirmish-already-open"))
    ui.Confirm.turn = turn_now()      # 물은 턴을 적는다

def asking_end_turn():                # 턴이 넘어가면 저절로 거짓이 되어 모달이 닫힌다
    return ui.Confirm.turn != 0 and ui.Confirm.turn == turn_now()
```

창을 여닫는 동작에도 사전 조건을 붙여 두면 좋다. 버튼이 뜻이 없을 때 회색이 되고, 화면 동작을 모두 늘어놓는 호스트(콘솔)에서 판이 끝난 뒤 남는 선택지가 생기지 않는다.

- `window="normal"`이나 `window="modal"`이다. 모달은 나중에 열린 보통 창보다도 늘 위에 있고 그 아래 입력을 막는다. 같은 종류끼리는 나중에 연 창이 위다.
- 보통 창은 누르면 맨 위로 온다. 막으려면 `window-raise="false"`.
- `on-close`는 닫힘 요청(ESC, 켰으면 바깥 클릭)에 부를 커맨드나 화면 동작이다. 엔진은 창을 직접 숨기지 않는다: 그 동작이 UI 기록을 바꿔 `bind-display`가 거짓이 되면 닫힌다.
- `on-close`가 있으면 ESC가 맨 위 창부터 닫는다. 막으려면 `close-on-escape="false"`. ESC로 닫히지 않는 모달이 맨 위면 ESC는 그 아래로 가지 않는다.
- 바깥 클릭은 기본으로 닫지 않는다. `close-on-outside="true"`면 모달은 뒤의 막이를, 보통 창은 그 창 밖을 누를 때 `on-close`를 부른다.
- 창 안에 창은 둘 수 없다(끼운 파일이어도 오류다). 창끼리는 나란히 둔다.
- `window-drag="true"`는 창 안의 요소에만 붙는다(끼운 파일이어도 된다). 끌어 놓은 자리는 게임을 켜 둔 동안 남는다.
- 창은 화면 맨 위의 겹침 층에 그려지므로 자리는 USS의 `left`·`top` 등으로 정한다. 모달 뒤의 막이는 USS 클래스 `modal-blocker`로 모양을 정한다(기본은 투명).
- 창 안에 화면을 여는 버튼과 닫는 버튼을 두는 것은 평범한 `on-click`이다.

## 번역

화면에 보일 글은 모드가 키로 적고(`text("harbor-charter", gold=5)`, `TextKey`, 문자열 선택지), 글 자체는 **번역 파일**에 언어마다 둔다.
번역 파일은 [Fluent](https://projectfluent.org/) 형식이다.

`samples/guide/harbor/locale/ko/harbor.ftl`:

```
harbor-charter = 금화 { $gold }닢에 배를 빌려줄까요?
harbor-charter-accept = 빌려준다
harbor-charter-decline = 거절한다

# Def 이름
def-harbor-Gull = 갈매기 상회
def-harbor-Sloop = 슬루프
    .desc = 작고 빠른 배
```

- 자리: 모드 폴더의 `locale/<언어 태그>/` 아래 아무 이름의 `.ftl`. 언어 태그는 `en`, `ko`, `pt-BR` 꼴이다.
  `locale/`이 있으면 `mod.py`에 `defaultLanguage`(1장)가 있어야 한다.
- 키는 하이픈으로 잇는다(`harbor-charter`). `.desc`처럼 들여 쓴 줄은 그 키의 속성이다.
- `text()`의 인자는 `{ $gold }`로 들어간다. 수는 적은 그대로(`1.5`, 서식은 아래 `NUMBER`), 엔티티와 Def id는 그 Def의 번역된 이름으로 보인다.
- **Def 이름**은 `def-모드id-경로` 키에 둔다(`skirmish:Red` → `def-skirmish-Red`, `harbor:rigs/Brig` → `def-harbor-rigs-Brig`). 설명 같은 덧붙임은 속성(`.desc`)으로 둔다.
  번역이 없는 Def는 Def id(`harbor:Gull`)로 보인다.
- `engine-`로 시작하는 키는 엔진과 호스트의 글이다(`engine-hidden` = 보이지 않는 엔티티). 모드가 같은 키를 두면 그 글로 바뀐다.
  그 밖의 `engine-`·`def-` 키(없는 Def의 이름)는 `check`가 오류로 본다.

### 언어를 고르는 순서

플레이어가 고른 언어들을 차례로 보고, 어디에도 없으면 **그 키를 둔 모드의 `defaultLanguage`**, 그래도 없으면 키 자체를 보인다.
`ko-KR`을 골랐는데 `ko-KR` 폴더가 없으면 `ko`를 본다. 다른 모드의 번역도 같은 키를 두면 뒤에 불리는 모드의 글이 이긴다(1장의 순서).
두 모드 사이에 순서가 없으면 어느 쪽이 이길지 모르므로 `check`가 경고한다.

콘솔은 `--lang ko,en`으로 언어를 받고, 없으면 OS 표시 언어 뒤에 `en`을 본다. 콘솔 자신의 글(`낼 수 있는 것이 없다` 등)도
`engine-console-` 키로 번역되어 있다.

### 한국어 조사와 Def 속성

번역문 안에서 쓰는 함수 둘:

```
# "갈매기 상회를", "슬루프를", "갈레온을": 이름의 받침에 맞춰 붙인다
harbor-sunk = { JOSA($ship, "을") } 잃었습니다.

# Def의 속성 글로 가지를 고른다. 속성이 없으면 *로 표시한 기본 가지
harbor-arrived = { DEFATTR($ship, "size") ->
    [big] 큰 배가 들어왔습니다.
   *[other] 배가 들어왔습니다.
}
```

(위 두 줄은 설명용이고 예시 모드에는 없다.)

- `JOSA(값, "조사")`: `이/가`, `을/를`, `은/는`, `과/와`, `으로/로`, `이나/나`, `이랑/랑`, `아/야`, `이여/여`, `이라/라` 중 짝의 아무 쪽이나
  적으면 앞 글의 끝소리에 맞는 쪽을 붙인다. `ㄹ` 받침 뒤의 `으로`는 `로`가 된다. 수는 읽는 소리(`3` → 삼)로 본다. 끝소리를 알 수
  없으면(영문 등) `을(를)`처럼 둘 다 적는다. 모르는 조사는 `check`가 오류로 보고, 게임 중에는 적힌 그대로 붙인다.
- `DEFATTR(Def나 엔티티, "속성")`: `def-` 키의 그 속성 글. 없으면 빈 글이라 기본 가지로 간다.

### 숫자 서식: `NUMBER`

`{ $n }`은 수를 가진 자릿수 그대로 보인다(`12.345678`, `1234567`). 자릿수·천 단위·퍼센트는 번역문의 `NUMBER`로 정한다.
화면에 숫자를 넣으려면 모드 함수가 `text("키", n=값)`을 돌려주고 번역문이 서식을 정한다.

```
hp-line = 체력 { NUMBER($hp, maximumFractionDigits: 0) }          # 9.6 → 체력 10
gold-line = 금화 { NUMBER($gold) }                                 # 1234567 → 금화 1,234,567
tax-line = 세금 { NUMBER($rate, style: "percent", signDisplay: "always") }   # 0.1 → 세금 +10%
```

(설명용이고 예시 모드에는 없다.)

- 옵션은 `Intl.NumberFormat`의 이름 중 다섯만 받는다. 그 밖의 옵션이나 값은 `check`의 오류(`LOC012`)다.

  | 옵션 | 값 | 기본 |
  |---|---|---|
  | `minimumFractionDigits` | 0~20 | 0 (모자라면 0을 채움) |
  | `maximumFractionDigits` | 0~20 | 3, 퍼센트는 0 |
  | `useGrouping` | `"true"`·`"false"` | `"true"` (천 단위 구분) |
  | `style` | `"decimal"`·`"percent"` (100을 곱해 `%`) | `"decimal"` |
  | `signDisplay` | `"auto"`·`"always"` (`+3`, `+0`)·`"exceptZero"` (`+3`, `0`) | `"auto"` (음수만 `-`) |

- 자를 때는 0에서 먼 쪽으로 반올림한다(`2.5` → `3`, `-2.5` → `-3`). 로직 언어의 `round`와 같다. 반올림해 0이 된 음수는 `0`이다.
- 결과도 수라 복수형 가지(`{ NUMBER($n, maximumFractionDigits: 0) -> [one] … *[other] … }`)는 반올림한 값으로 고른다.
- 기호는 언어마다 엔진 글 `engine-number-decimal`(`.`)·`engine-number-group`(`,`)·`engine-number-percent`(`{ $n }%`)이다.
  언어를 더하는 모드가 그 언어 파일에서 덮어쓴다. 공백은 앞뒤가 잘리므로 따옴표로 적는다.

  ```
  # locale/de/…  →  1.234,5   12,5 %
  engine-number-decimal = ,
  engine-number-group = .
  engine-number-percent = { $n } %
  # locale/fr/…  →  1 234,5
  engine-number-group = {" "}
  ```

### 검사

`engine check`가 번역도 본다(`LOC`, 1장): 구문 오류, 한 모드에서 겹친 키, 모르는 조사, `NUMBER`의 모르는 옵션, 쓸 수 없는 `def-`·`engine-` 키는 오류,
`text("키")`의 키가 어느 언어에도 없거나 번역문이 쓰는 변수를 넘기지 않으면 경고, 언어마다 빠진 글의 수는 정보다.
게임 중에 번역이 틀린 글은 판을 멈추지 않고 할 수 있는 만큼 보인다(콘솔은 stderr에 한 번 알린다).

## 화면 동작: `@ui_action`과 UI 기록

"유닛을 고르고, 그다음 공격"처럼 **게임은 바꾸지 않고 화면에만 남는 것**은 UI 기록에 적고, 그 기록을 고치는 동작은 `@ui_action`으로 쓴다.
게임을 바꾸는 일은 여전히 커맨드가 하고, 화면 동작은 그 커맨드를 내 달라고 적어 둘 뿐이다.

### UI 기록: `ui_state`

`samples/skirmish/core/schema/types.py`:

```python
# 화면에만 있는 기록: 연결마다 하나, 저장되지 않는다 (spec-gaps G277·G279). 고른 유닛
ui_state("Selection", unit=ref("Unit", optional=True))
```

- 표기는 `component`와 같다. 다른 점:
  - 모든 칸에 기본값이 있어야 한다.
  - Ref는 `optional=True`여야 하고 처음은 `None`이다.
- 기록은 **연결(자리)마다 하나**다. 빨강의 "고른 유닛"과 파랑의 것은 따로다.
- 기록은 세이브·네트워크·되감기에 들지 않는다. 호스트가 들고, 언제 비울지도 호스트가 정한다.
- 칸에 넣을 수 있는 값은 커맨드 인자와 같다: 수, 문자열, 참거짓, Ref, 리스트, 사전. 리스트·사전 칸(`field(list[Ref["Unit"]], default=[])`)은
  `ui_state`에만 쓸 수 있다.
- 가리키던 엔티티가 죽거나 그 연결에게 안 보이게 되면 이렇게 읽힌다. 저장된 값은 그대로다.
  - 한 칸짜리 Ref는 `None`이다.
  - 리스트·사전에서는 그 항목이 빠진다.
- 이름은 타입·컴포넌트·신호와 한 이름 공간이다. 같은 이름이면 `SCH` 중복 오류다.

### 화면 동작: `@ui_action`

`samples/skirmish/core/defs/ui.py`:

```python
# 고른 유닛이 아직 움직일 수 있으면 그 유닛
def chosen():
    u = ui.Selection.unit
    return None if u is None or u.Ready.acted else u

def in_reach(t, me):
    u = chosen()
    return u is not None and t.Owner.party != me and dist(u, t) <= u.Unit.reach

@ui_action
def select(me: Ref[Party], unit: Ref[Unit]):
    with preconditions:
        choose(unit, where=lambda u: u.Owner.party == me and not u.Ready.acted and u != ui.Selection.unit)
    ui.Selection.unit = unit

@ui_action
def strike(me: Ref[Party], target: Ref[Unit]):
    with preconditions:
        require(chosen() is not None, text("skirmish-select-first"))
        choose(target, where=lambda t: in_reach(t, me))
    # Attack은 차례 질문의 답이다: 엔진이 이 연결이 답할 질문을 찾아 낸다 (G283)
    submit("Attack", party=me, unit=chosen(), target=target)
    ui.Selection.unit = None
```

- 모양은 커맨드와 같다. 매개변수에 타입을 적고, `with preconditions:` 안에 `require`·`choose`를 쓴다. `cost`는 쓸 수 없다.
- 호스트는 사전 조건으로 다음을 한다(4장과 같은 실행기).
  - 회색 버튼과 그 이유를 보인다.
  - 누를 수 있는 수를 나열한다.
  위의 `strike`는 아직 아무것도 고르지 않았으면 나오지 않는다.
- 본문은 게임을 **그 연결이 보는 세계**로 읽기만 한다(7장). 숨긴 엔티티는 죽은 것처럼 다룬다.
- 본문이 쓸 수 있는 것은 `ui.기록.칸`뿐이다. 다음은 모두 `CMD007` 오류다.
  - 엔티티 쓰기, `spawn`·`destroy`·`emit`.
  - 난수.
  - `ask`·`wait`·`advance`.
- `ui.Selection.unit.Life.hp = 0`은 기록이 가리키는 **엔티티**에 쓰는 것이라 게임 쓰기이고 오류다.
- `ui`는 늘 `ui.기록.칸` 꼴로 적는다. `s = ui.Selection`처럼 기록을 값으로 들 수 없다.
- 리스트 칸에 `+=`는 쓰지 않는다. `ui.Selection.group = ui.Selection.group + [u]`로 쓴다.
- 본문이 도중에 실패하면 그 동작이 고친 칸과 적어 둔 `submit`이 모두 없던 일이 된다.

### 커맨드 내기: `submit`

- `submit("커맨드", 인자=…)`는 그 커맨드를 내 달라고 **적어 두기만** 한다. 인자는 이름으로만 넘긴다.
- 동작이 끝나면 호스트가 적힌 순서대로 낸다. 본문은 결과를 모른다. 결과에 따른 화면은 다음에 화면 함수가 게임 상태를 보고 그린다.
- 내는 방법은 커맨드에 따라 다르다.
  - 자유 커맨드(`free=True`)는 그냥 제출된다.
  - 응답 커맨드(5장, 스커미시의 `Attack`)는 엔진이 그 연결이 답할 수 있는 열린 질문 가운데 이 커맨드를 받는 것을 찾아 거기에 답으로 낸다. 모더는
    질문 번호를 다루지 않는다.
  - 받는 질문이 없거나 둘 이상이면 거절되고, 호스트가 이유를 보인다.
- 커맨드 이름을 글자로 적었으면 `engine check`가 다음을 본다(`CMD008`).
  - 있는 커맨드인가.
  - `@ui_action`이 아닌가.
  - 의존하는 모드의 것인가.
  - 매개변수 이름이 맞고, 기본값 없는 매개변수를 다 넘겼나.
- `submit`은 문장으로만 쓴다(`x = submit(...)`은 오류).

### 도우미 함수와 게임 쪽 코드

- `ui`를 읽거나 쓰는 보통 함수(위의 `chosen`)를 둘 수 있다.
  - `ui`에 **쓰는** 함수와 `submit`하는 함수는 `@ui_action` 안에서 문장으로만 부른다(3장 쓰기 함수의 규칙과 같다).
- `ui`를 건드리는 함수는 게임 쪽 코드가 쓸 수 없다(`CMD007`). 이 표시는 그 함수를 부르는 함수에도 퍼진다.
  - 게임 쪽 코드: `@command`, `@on`, `@when`, `acts_for`, 보기 정책, `derived`.
  - 까닭: UI 기록은 컴퓨터마다 달라서 게임이 한 번이라도 읽으면 판이 갈라진다.

### 화면 함수에서 읽기

화면 함수도 `ui.…`를 읽을 수 있다(`samples/skirmish/core/defs/view.py`):

```python
# 이 자리가 골라 둔 유닛 (ui.py의 select)
def selection(me):
    return {"selected": ui.Selection.unit}
```

- 호스트는 그 연결의 기록과 함께 부른다. 기록이 바뀌면 다시 부른다(영수증).
- 화면 함수는 기록을 읽기만 한다. 쓰는 함수는 호스트가 부르지 않는다.

### 열린 질문 읽기: `questions()`

화면 함수·`@ui_action`·`@bot`은 이 연결이 지금 답할 수 있는 열린 질문(5장의 `ask`)을 읽을 수 있다.

```python
# 맞은 유닛마다 강조할 칸
def hit_marks(me):
    return [q.data["unit"] for q in questions("skirmish-hit")]
```

- `questions()`는 이 연결이 답할 수 있는 열린 질문 전부, `questions("이름")`은 `ask(name=)`이 그 이름인 것만 준다. 늘 목록이다.
  같은 이름의 질문이 동시에 여럿 열릴 수 있다(두 유닛이 함께 맞음).
- 질문 값의 칸:

| 칸 | 뜻 |
|---|---|
| `q.name` | `ask`의 `name=` |
| `q.title`, `q.hint`, `q.data` | `ask`에 적은 것. `hint`가 없으면 `None` |
| `q.choices` | 선택지마다 `value`·`label`·`desc`·`style`·`data`·`index`. `index`는 `engine.pick`에 넘길 번호 |
| `q.responses` | 받는 응답 커맨드(`"모드.이름"`) |
| `q.who` | 이 질문에 답할 수 있는 내 엔티티들 |

- 보이는 범위는 7장과 같다. 못 보는 엔티티는 가려진다.
- 질문이 열리거나 닫히면 이 함수를 읽은 화면 함수를 호스트가 다시 부른다(영수증).
- 질문 값은 담아 둘 수 없다. `ui.…`·`bot.…`에 넣거나 `submit`의 인자로 넘기면 오류다. 필요한 칸(`q.name`, 선택지 번호)만 꺼내 둔다.

그 질문에 답하려면 `respond(q, 커맨드, 인자=…)`를 쓴다.

```python
# 맞은 유닛을 누르면 그 유닛의 맞음 질문에 "물러남"으로 답한다
@ui_action
def retreat(unit: Ref[Unit]):
    for q in questions("skirmish-hit"):
        if q.data["unit"] == unit:
            respond(q, "engine.pick", choice=1)
```

- 커맨드는 그 질문이 받는 응답 커맨드(`respond(q, "Move", to=cell)`)이거나, 선택지 질문이면 `"engine.pick"`(9장의 `choice=`·`by=`)이다.
- `submit`과 같이 적어 두기만 하고, 동작이 끝나면 호스트가 그 질문에 낸다. `@ui_action`·`@bot`에서 문장으로만 쓴다.
- `submit`은 엔진이 짝을 찾으므로 받는 질문이 둘 이상이면 거절된다. 같은 이름의 질문이 여럿 열릴 수 있으면 `respond`로 하나를 가리킨다.
- 내는 때 그 질문이 이미 닫혔거나 그 커맨드를 받지 않으면 이유를 붙여 거절된다.
- 게임 쪽 코드(`@command`·`@on`·`@when`·`acts_for`·보기 정책·`derived`)에서는 쓸 수 없다(`CMD007`). 연결마다 답할 질문이 달라서다.
  이 연결이 앉은 엔티티를 주는 `seats()`(아래 "판 위 엔티티 누르기")도 같다.

### 아무도 그리지 않은 질문: 엔진 기본 창

모드 화면이 그리지 않은 질문이 열려도 판이 멈추지 않게, UXML 해석기는 그런 질문을 제목과 답 버튼이 든 엔진 기본 창(`kesa-question` 클래스)으로 보이고 `Errors`에 경고를 한 번 남긴다. 다음 둘 가운데 하나면 모드가 그린 것으로 치고 창을 띄우지 않는다.

- 화면에 붙은 바인딩이 `questions("그 이름")`이나 `questions()`를 읽는다.
- 화면에 붙은 `on-click`이 그 질문의 응답 커맨드를 가리킨다(차례 넘기기 버튼 `on-click="skirmish.EndTurn(house)"`처럼).

`@ui_action` 안의 `respond`로만 답하는 화면은 바인딩 어딘가에서 `questions("그 이름")`을 읽어 두면 된다(보통 버튼 보임을 거기에 묶는다). 기본 창은 개발 중의 안전망이라 경고가 뜨면 그 질문을 모드 화면에 그린다.

### 콘솔에서

- 콘솔은 자리마다 기록 하나를 든다. `ui/console.txt`의 함수는 늘 그 기록과 함께 부른다.
- 모든 화면 동작의 누를 수 있는 수를, 커맨드 수 뒤에 `[화면]`을 붙여 번호 메뉴에 보인다.
  - 메뉴에 띄울 동작을 따로 적지 않는다.
  - 어떤 버튼을 어디에 둘지는 Unity·Godot 같은 호스트의 UI 파일이 정할 일이다.
- 메뉴의 커맨드·화면 동작 이름(`skirmish.select(...)`)은 번역하지 않는다. 보통 화면에서는 버튼 글을 호스트의 UI 파일이 정하고
  커맨드는 그 뒤에 감춰지기 때문이다(spec-gaps G287). 콘솔은 시험용이라 코드 이름을 보인다.
- **콘솔의 약속:** 이름이 `me`인 매개변수가 있으면 콘솔이 그 자리가 앉은 엔티티를 넣고 나머지만 나열한다. 엔진은 이 이름을 모른다.
  다른 호스트는 제 방식으로 넘긴다.

```
  6) [red] skirmish.EndTurn(party=#1 빨강)
  7) [red] [화면] skirmish.select(unit=#4 병사)
  8) [red] [화면] skirmish.select(unit=#5 궁수)
...
> [red] ok
  {selected: #5 궁수}
...
  9) [red] [화면] skirmish.strike(target=#8 궁수)
> [red] skirmish.Attack(party=#1 빨강, target=#8 궁수, unit=#5 궁수): ok
```

## 월드 표시: 그림과 자리

패널(UXML) 말고 판 위에 놓이는 물체(유닛 스프라이트, 3D 모델, 칸 위의 표시)도 모드 함수가 정한다. 모드는 **그림의 이름**과
**놓을 자리**만 말하고, 그 이름이 어떤 스프라이트·모델인지와 자리가 화면의 어디인지는 호스트(그래픽 파일과 공간 종류)가 정한다.
Unity 패키지는 SpriteRenderer로 그리고 표시를 누르면 그 `click`을 낸다. skirmish가 유닛과 물러날 칸을 이렇게 그린다(`defs/ui.py`의 `unit_look`, `looks/`).

### `look`·`where` 칸

```python
# 판에 올릴 타입: 이름이 정확히 look과 where인 칸이 둘 다 있으면 보는 쪽 눈에 보일 때 판에 올라간다
deftype("Unit", components=["Pos", "Life", "Owner", "Ready"],
    max_hp=const(int), attack=const(int), reach=const(int),
    look=const(Look["Unit"]), where=const(Where["Unit"]))
```

```python
def unit_look(u):
    return look("soldier", team=u.Owner.party, hurt=u.Life.hp < u.Unit.max_hp, picked=ui.Selection.unit == u)

def unit_where(u):
    return at("square", [u.Pos.x, u.Pos.y])

Unit("Soldier", max_hp=3, attack=2, reach=1, look=unit_look, where=unit_where)
Unit("Tree", ..., look="pine", where=lambda u: at("square", [u.Pos.x, u.Pos.y]))   # 늘 같은 그림이면 이름만
```

- 칸 타입은 `Look["타입"]`·`Where["타입"]`이다. 값은 그 엔티티 하나를 받는 함수이고, `look`에는 그림 이름 글자를 바로 적어도 된다.
- 판에 올리는 것은 이름이 정확히 `look`·`where`인 칸뿐이다. `portrait=const(Look["Unit"])`처럼 다른 이름의 칸은 화면 함수가 꺼내 쓰는 보통 값이다.
  다른 모드의 타입에 붙이려면 `extend`로 `look`·`where`를 더한다.
- 칸 함수는 화면 함수와 같은 규칙이다. `ui.…`와 `questions()`를 읽을 수 있고, 세계를 바꾸거나 난수를 쓰면 로드 오류다.
  호스트 함수(C#)는 그림·자리를 돌려줄 수 없어 넣을 수 없다.
- 둘 중 하나가 `None`을 돌려주면 그 엔티티는 지금은 판에 올리지 않는다(수송선 안의 유닛처럼). 엔티티 자체는 그대로 보이므로 패널에서는 읽힌다.
- 칸 함수가 다른 것을 돌려주거나 실행 오류가 나면 그 물체만 오류이고 오류 문구가 그 물체에 실려 호스트로 간다. 다른 물체는 그대로 그려진다.
- 게임 쪽 코드(`@command`·`@on`·`derived` 등)는 이 칸을 읽을 수 없다(`CMD007`). 화면마다 다른 값이기 때문이다.

### 그림: `look(이름, 키=값…)`

- 이름은 호스트 그래픽 파일의 정의를 고른다. 키=값은 아무 평범한 값(수·`fixed`·글자·`bool`·`None`·엔티티·`text(…)`·목록·사전)이고,
  그 뜻(변형을 고를지, 색을 칠할지)은 그래픽 파일이 정한다. 엔진은 키를 모른다.
- 못 보는 엔티티는 가려진 값으로 실린다(7장).

### 자리: `at`·`onto`·`between`

| 쓰는 법 | 뜻 |
|---|---|
| `at("공간", 값)` | 호스트의 이름 붙은 공간(네모 칸, 육각 칸, 연속 좌표, 구역+순번 등)의 한 자리. 값은 아무 평범한 값 |
| `at(값)` | 게임의 기본 공간 |
| `onto(엔티티, "붙는 곳")` | 그 엔티티의 물체에 붙어 함께 움직인다. 붙는 곳은 그 그림 정의의 노드 이름이고, 빼면 물체 자체 |
| `between(자리, 자리)` | 두 자리 사이. 공간을 건너도 된다 |

- 공간 이름과 값의 뜻(어느 칸이 화면의 어디인지, 한 칸에 여럿일 때 어떻게 늘어놓는지)은 호스트의 공간 종류가 정한다.

### 표시: `@world_marks`와 `mark`

엔티티가 아닌 것(물러날 칸, 사거리 원, 화살표)은 `@world_marks` 함수가 지금 보일 목록으로 돌려준다.
`samples/skirmish/core/defs/ui.py`:

```python
# 맞은 유닛이 물러날 칸. 누르면 retreat로 답한다
@world_marks
def retreat_spots():
    return [mark("retreat-spot", at=at("square", q.data["to"]), click=["skirmish.retreat", {"unit": q.data["unit"]}])
            for q in questions("skirmish-hit")]
```

- 매개변수가 없다. 호스트가 화면마다 부르고, `questions()`·`ui.…`·엔티티 읽기가 그 화면의 것만 준다.
  화면 함수와 같은 규칙이라 세계를 바꾸거나 난수를 쓰면 로드 오류다. 여러 모드에 여럿 있어도 된다.
- `mark(그림, at=자리, click=[동작, 인자], key=열쇠)`: 그림은 `look(…)`이나 그림 이름 글자, `at=`는 꼭 적는다.
  `click=`은 누르면 부를 `@ui_action`과 그 인자 사전이고, 없으면 보이기만 한다. `None`을 돌려주면 표시가 없다.
- 주 누름 말고 다른 입력은 `inputs={"secondary": ["skirmish.move_to", {"x": x, "y": y}]}`처럼 입력 이름마다 적는다(값 모양은 `click=`과 같다).
  `click=`은 `inputs={"primary": …}`의 짧은 꼴이고, 둘 다 적으면 호스트가 경고하고 `click=`을 쓴다. 표시가 받지 않는 입력은 아래 엔티티와 칸으로 넘어간다.
- 지우기는 적지 않는다. 읽은 것이 바뀌면 다시 불러 지난 목록과 견주고 새것은 만들고 빠진 것은 지운다.
  같은 표시는 그림 이름과 자리로 알아본다: 그림의 키=값만 바뀌면 같은 표시를 고친다(고리의 반지름).
  자리가 바뀌어도 같은 표시로 이어 가려면(옮겨 가는 화살표) `key=`를 적는다.
- 함수가 실패하면 그 함수의 표시만 사라지고 호스트가 경고한다.

### 판 위 엔티티 누르기: `@world_input`

판 위 엔티티에 들어온 입력은 `@ui_action` 위에 `@world_input("입력", "Def 타입")`을 쌓아 받는다.
`samples/skirmish/core/defs/ui.py`:

```python
# 판 위 유닛을 오른쪽 누름으로도 연다
@world_input("secondary", "Unit")
@ui_action
def inspect(unit: Ref[Unit]):
    with preconditions:
        require(playing(), text("skirmish-over"))
        choose(unit, where=lambda u: u != ui.Inspect.unit)
    ui.Inspect.unit = unit
```

- 입력은 이름이다. 엔진 내장은 `primary`(왼쪽 누름·탭)와 `secondary`(오른쪽 누름)이고, 실제 단추를 이름으로 바꾸는 것은 호스트다.
  모드 입력은 아래 `.input.xml`에 이름과 기본 키를 적는다.
- 화면 동작은 매개변수 하나, 그 타입의 엔티티(`Ref[타입]`)만 받는다. 한 동작에 `@world_input`을 여러 줄 쌓아도 된다.
- 누르면 그 자리의 표시가 먼저 받고(`click=`, 주 누름만), 없으면 커서 아래 엔티티를 위에서부터 본다.
- 같은 입력과 타입의 핸들러가 여러 모드에 있으면 나중에 불러온 모드의 것부터 사전 조건을 본다. 통과한 첫 것이 받고, 막히면 다음 것으로 넘어간다.
  그래서 고르기 모드를 더하는 모드는 `require(…)`로 제 차례만 받는 핸들러를 더하면 되고 남의 것을 덮지 않는다.
  모두 막히면 처음 막힌 이유가 호스트에 간다. 한 모드 안에서 같은 입력과 타입을 두 번 받으면 로드 오류다.

#### 모드 입력의 키: `.input.xml`

`primary`·`secondary` 말고 모드가 만든 입력은 `.input.xml` 파일(모드 폴더 어디든)에 이름과 기본 키를 적는다.
키도 커서 아래 엔티티와 칸으로 간다. 가리킨 유닛에 `I`를 누르면 그 유닛이 `inspect`의 매개변수가 된다.
`samples/skirmish/core/keys.input.xml`:

```xml
<Inputs>
  <Input name="inspect" key="I" />
</Inputs>
```

- `key=`는 수정 키 `SHIFT`·`CTRL`·`ALT`(둘까지) 뒤에 키 하나를 `+`로 잇는다. 예: `"R"`, `"SHIFT + secondary"`, `"CTRL + Digit1"`.
- 키 이름은 Unity Input System의 `Key` 이름이다(`A`~`Z`, `Digit0`~`Digit9`, `F1`~`F24`, `Enter`, `Space`, `Tab`, `Escape`, `Numpad0`, `LeftArrow` 등).
  마우스 단추는 `primary`·`secondary`로 적는다. 대소문자는 가리지 않는다.
- 같은 키에 수정 키를 더 붙인 입력이 함께 눌리면 그것만 간다. `R`과 `SHIFT + R`이 둘 다 있으면 Shift+R은 `SHIFT + R`만 낸다.
- 같은 이름을 나중 모드가 다시 적으면 그 기본 키가 이긴다. 한 모드 안에서 두 번 적거나 `primary`·`secondary`를 적으면 문제로 알리고 그 줄만 뺀다.
  모르는 키 이름도 같다("혹시 …?"를 붙인다).
- 적은 키는 기본값이다. 플레이어가 바꾼 키는 게임이 저장하고 모드 파일은 그대로다.
- `@world_input`이나 표시의 `inputs=`가 어느 `.input.xml`에도 없는 이름을 쓰면 키가 닿지 않으므로 호스트가 경고한다("혹시 …?").
  핸들러는 로드 때, 표시는 처음 그릴 때 알린다.
- 대상을 `choose(unit, where=…)`로 거르면 콘솔 같은 호스트가 누를 수 있는 유닛을 나열할 수 있다.

누른 유닛이 내 것인지는 `seats()`로 안다. skirmish의 주 누름은 내 유닛이면 고르고, 고른 유닛의 사거리 안 적이면 공격한다.

```python
def is_mine(u):
    return u.Owner.party in seats()

@world_input("primary", "Unit")
@ui_action
def press_unit(unit: Ref[Unit]):
    with preconditions:
        require(playing(), text("skirmish-over"))
        choose(unit, where=lambda u: can_pick(u) or can_strike(u))
    if can_pick(unit):
        ui.Selection.unit = unit
    else:
        u = chosen()
        submit("Attack", party=u.Owner.party, unit=u, target=unit)
        ui.Selection.unit = None
```

- `seats()`는 이 연결이 앉은 엔티티 목록(id 순, 관전자면 빈 목록), `seats(Party)`는 그 가운데 그 타입(또는 컴포넌트)인 것만이다.
- `questions()`와 같이 화면 쪽 코드에서만 쓰고, 자리가 바뀌면 읽은 화면 함수를 다시 부른다.

빈 자리는 대상에 공간 이름을 적어 받는다. 화면 동작은 `at("공간", 값)`의 값 하나를 받고, 타입은 그 공간의 자리 값 모양대로 적는다.

```python
# 고른 유닛을 바로 옆 빈 칸으로 옮긴다
@world_input("primary", "square")
@ui_action
def press_square(cell: list[int]):
    with preconditions:
        require(playing(), text("skirmish-over"))
        choose(cell, from_=step_cells())
    ...
```

- 대상이 Def 타입이 아니면 공간 이름이다. 공간은 호스트 파일(`.space.xml`)에 있어서 이름이 맞는지는 엔진이 아니라 호스트가 알린다.
  매개변수가 `Ref[…]`인데 대상이 Def 타입이 아니면 타입 이름 오타로 보고 로드 오류다.
- 커서 아래 엔티티가 그 입력을 받지 않으면(핸들러가 없거나 모두 막힘) 그 자리의 공간 핸들러가 받는다. 유닛이 선 칸도 그렇다.
- `choose(cell, from_=[…])`로 갈 수 있는 칸을 적으면 콘솔도 수를 나열한다.

### 한 번 일어나는 효과: `@world_fx`와 `fx`

맞는 순간의 숫자처럼 상태가 아닌 사건은 규칙이 신호를 내고(`emit(Hit(...))`), 화면 쪽 함수 `@world_fx(신호)`가 효과를 돌려준다.

```python
@world_fx(Hit)
def hit_flash(x, y, damage):
    place = at("square", [x, y])
    return [fx("hit-flash", at=place), fx(look("damage-number", amount=-damage), at=place)]
```

- 매개변수는 `@on`처럼 신호의 값을 이름으로 고른다(타입·기본값은 적지 않는다). 돌려주는 것은 `fx(그림, at=자리)`의 리스트이고 `None`은 빈 리스트다.
- 화면 함수와 같은 규칙이다: 판을 바꾸지 못하고, 그 연결이 보는 판으로 돈다. 못 보는 대상이면 빈 리스트를 돌려주면 된다.
  신호 자체는 화면으로 나가지 않으므로 비밀 신호가 새지 않는다.
- 효과 함수는 신호가 난 뒤의 판에서 돈다. 맞고 바로 죽어 사라진 유닛처럼 그때 없을 수 있는 값(자리 등)은 신호에 싣는다.
- 같은 신호에 여러 모드가 각자 함수를 더할 수 있고, 로드 순서대로 모두 돈다. 함수가 실패하면 그 함수 몫만 빠지고 호스트가 경고한다.
- 화면 없이 돌면(봇끼리, 콘솔) 부르지 않는다. 판은 효과를 기다리지 않는다.
- 효과가 얼마 동안 어떻게 보이는지는 그림 정의의 `<Look>` 칸이 정한다. `samples/skirmish/core/looks/fx.look.xml`:

```xml
<Look name="hit-flash" fade-in="0.05" solid="0.15" fade-out="0.4">
  <Sprite src="art/ring.png" tint="#ff4040ff" size="1.2,1.2" layer="5" />
</Look>
<Look name="damage-number" fade-in="0.05" solid="0.5" fade-out="0.4" rise="0.8">
  <Text bind-text="amount" size="0.45" color="#ffe070ff" offset="0,0.3" layer="6" />
</Look>
```

- `fade-in`·`solid`·`fade-out`은 초이고 투명도가 0 → 1 → 0으로 간다. 수명은 셋의 합이고, 셋 다 없으면 바로 끝나므로 호스트가 경고한다.
  `rise`는 초당 위로 떠오르는 칸 수다(음수면 가라앉음).
- 이 칸들은 `bind-solid="hold"`처럼 그림 값에 묶을 수 있다. `fx(look("hit-flash", hold=1.2), …)`이면 그 효과만 1.2초 머물고, 값이 `None`이면 적힌 값을 쓴다.
- 이 칸들은 `mark`·엔티티 그림에는 쓰이지 않는다.

### Unity: 그림 정의 `.look.xml`과 공간 `.space.xml`

그림 이름과 공간 이름의 뜻은 Unity 패키지가 읽는 XML 파일에 적는다. 확장자로 알아보므로 모드 안 어디에 두어도 되고, 관례는
`looks/` 아래다. 같은 이름은 로드 순서가 뒤인 모드가 통째로 이기고, 한 모드 안에 같은 이름이 둘이면 문제다.
`samples/skirmish/core/looks/`:

```xml
<!-- board.space.xml -->
<Spaces>
  <Space name="square" kind="grid" cell="1" />
</Spaces>

<!-- marks.look.xml -->
<Looks>
  <Look name="retreat-spot">
    <Sprite src="art/white.png" tint="#4080ff80" />
  </Look>
</Looks>
```

공간 종류 `grid`(네모 칸):

| 설정 | 뜻 |
|---|---|
| `cell` | 한 칸의 크기(기본 1) |
| `origin` | 칸 `[0, 0]`의 중심이 놓일 곳(`"1,0.5"`, 기본 원점) |
| `plane` | `xy`(기본, 2D: 모드 y가 화면 위쪽) 또는 `xz`(3D 바닥: 모드 y가 앞쪽, z가 높이) |
| `flip-y` | `true`면 y가 커질수록 아래(뒤)로 |

- 자리 값은 정수 `[x, y]`나 `[x, y, z]`다(z는 높이, `plane="xz"`에서만).
- 공간 이름 없는 `at(값)`의 기본 공간은 아직 선언할 수 없다.

그림 요소:

| 요소 | 속성 |
|---|---|
| `Sprite` | `src`(그림 경로), `size`(`"1,0.5"`, 빼면 긴 변이 한 칸이고 그림 비율을 지킴), `tint`(`#rrggbb`·`#rrggbbaa`) |
| `Text` | `text`(글), `size`(글자 높이, 칸 단위, 기본 0.5), `color`(`#rrggbb`·`#rrggbbaa`). 글꼴은 호스트 기본 |
| `Group` | 자식을 함께 옮기고 함께 숨긴다 |
| 모든 요소 | `layer`(정수, 큰 것이 위), `offset`(`"0,0.5"`, 묶음 안에서는 부모 기준), `bind-속성`, `bind-display`, `for` |

- `bind-속성="키"`는 그림 값의 키를 읽는다: `look("soldier", art="art/red.png", hurt=True)`이면 `bind-src="art"`·`bind-display="hurt"`.
  사전 키는 점으로(`gear.flag`), `for="f in flags"`는 목록의 항목마다 자식을 만든다.
- 식에서 모드 함수를 부를 수 없고 연산자도 없다. 경로를 이어 붙이거나 비교하는 일은 그림 값을 만드는 함수에서 한다.
- 그림 값에 없는 키는 `None`으로 읽는다. 그래서 `bind-display="hurt"`는 다쳤을 때만 `hurt=True`를 넘기면 된다.
  `bind-text`는 아무 값이나 받는다: 수는 그대로, `text("키")`는 화면 UXML처럼 번역되고, `None`은 빈 글이다.
  `src`·`tint`처럼 `None`을 받지 못하는 속성은 그 속성만 직전 값으로 두고 경고한다(`for`의 목록도 같다).
- 로드 때 모르는 요소·속성·공간 종류·설정, 틀린 값, 없는 그림 파일을 알린다. 그릴 때 모르는 그림 이름은 눈에 띄는 대체 그림과 경고,
  모르는 공간 이름과 틀린 자리 값은 경고와 함께 그리지 않는다. 판은 멈추지 않는다.
- 게임은 C#으로 요소 종류와 공간 종류를 더할 수 있다(`LookKinds`).
- 판을 누르면 그 자리에 그린 표시 중 `click=`이 있는 것이, 없으면 엔티티의 `@world_input`이 받는다. 오른쪽 누름은 `secondary`다. 화면 UXML이 판 위를 덮고 있으면 그 요소는 누름을 가로채므로,
  자리만 잡는 요소에는 Unity UXML의 `picking-mode="Ignore"`를 적는다(skirmish `ui/screen.uxml`의 `screen`·`units`).

### 어디서 쓰나

`look`·`at`·`onto`·`between`·`mark`가 만든 값은 화면으로만 나가고 담아 둘 수 없다. 그래서 화면 쪽 코드(칸 함수, 화면 함수,
`@ui_action`, `@world_marks`)에서만 만든다.

- 게임 쪽 코드에서 쓰면 로드 오류다(`CMD007`). `Cond`·`Action` 칸과 `@wrap`도 같다.
- 칸을 변수에 담아 부르는 것처럼 이름으로 드러나지 않는 길은 실행 중에 오류가 난다(`at() can only be used on a screen`).
- `field`에 담으면 타입이 맞지 않아 오류다.
