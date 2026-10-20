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
- 부를 수 없는 함수: `@command`·`@on`·`@when`, 월드를 쓰는 함수, 난수를 쓰는 함수(그런 함수를 부르는 함수도). 호스트가 띄울 때
  이유와 함께 알려 준다(`RUN023`, 이름 오타면 `RUN001`과 "혹시 …?").
- 돌려줄 수 있는 것: 수, 문자열, 참거짓, 엔티티, 그리고 이들의 리스트·사전.
- 함수는 **그 연결이 보는 세계**에서 돈다(7장). 그래서 숨긴 정보를 따로 거를 필요가 없다. 대신 "안 보임"(`x.C is None`)을 다룬다.
- 호스트는 함수가 읽은 곳을 기록해 두고 그곳이 바뀌었을 때만 다시 부른다. 모더가 신경 쓸 것은 없다.
- 숫자를 글에 넣는 `str()`이 아직 없다. 지금은 사전으로 돌려주면 호스트가 `{gold: 20, fleet: 7}`처럼 보인다(9장).

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
- `text()`의 인자는 `{ $gold }`로 들어간다. 수는 적은 그대로(`1.5`), 엔티티와 Def id는 그 Def의 번역된 이름으로 보인다.
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

### 검사

`engine check`가 번역도 본다(`LOC`, 1장): 구문 오류, 한 모드에서 겹친 키, 모르는 조사, 쓸 수 없는 `def-`·`engine-` 키는 오류,
`text("키")`의 키가 어느 언어에도 없거나 번역문이 쓰는 변수를 넘기지 않으면 경고, 언어마다 빠진 글의 수는 정보다.
게임 중에 번역이 틀린 글은 판을 멈추지 않고 할 수 있는 만큼 보인다(콘솔은 stderr에 한 번 알린다).

## 버튼

화면에서 커맨드를 내는 것(버튼 → 커맨드와 인자)은 아직 정하지 않았다(9장). 콘솔은 낼 수 있는 수를 엔진에게서 받아 번호로 보인다.
