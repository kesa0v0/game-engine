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
- 문자열은 그대로, 리스트는 항목마다 한 줄, 그 밖은 값 글(`{gold: 20, fleet: 0, day: 0}`, 엔티티는 `#2 Gull`)로 보인다.
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
[red] #1 Red
  . s a s .
  . . . . .
  . . . . .
  . . . . .
  . S A S .
```

## 버튼

화면에서 커맨드를 내는 것(버튼 → 커맨드와 인자)은 아직 정하지 않았다(9장). 콘솔은 낼 수 있는 수를 엔진에게서 받아 번호로 보인다.
