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
- 아직 없는 것: 런타임에 읽는 스타일시트(USS), 인라인 `style`.

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
