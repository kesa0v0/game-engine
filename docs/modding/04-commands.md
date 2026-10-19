# 4. 커맨드

플레이어(사람이든 AI든)가 게임을 바꾸는 **유일한 길**은 커맨드다. 호스트는 "연결 X가 커맨드 Y를 인자 Z로 냈다"를 넘길 뿐이고,
그 기록만 있으면 같은 판을 다시 돌릴 수 있다(리플레이·네트워크).

## 모양

`samples/guide/harbor/defs/rules.py`:

```python
# 배를 산다. 그 상회에 앉은 연결만 낼 수 있다
@command(free=True, acts_for=lambda house: house)
def Buy(house: Ref[House], kind: str):
    with preconditions:
        choose(kind, from_=["Sloop", "Galleon"])
        cost(house.Purse.gold, PRICES[kind])
    ship = spawn(kind, Owner=Owner(house=house))
    ship.Hull.hp = ship.Hull.max_hp
```

- 커맨드 이름은 `모드.이름`(`harbor.Buy`)이다.
- **매개변수에는 타입을 꼭 적는다**(`CMD003`): `int`, `fixed`, `bool`, `str`, `Ref[타입]`, `DefId[타입]`, `list[T]`, `dict[K, V]`, `T | None`.
  기본값은 커맨드에만 줄 수 있다.
- `free=True`: 언제든 낼 수 있는 커맨드. 빼면 **응답 커맨드**로, 질문(`ask`)이 열려 있을 때 그 답으로만 낸다(5장).
- `acts_for`: 이 커맨드가 **누구로서** 행동하는지. 매개변수 이름을 받는 람다다(`lambda house: house`, `lambda unit: unit.Owner.party`).
  엔진은 그 엔티티에 앉은 연결만 이 커맨드를 내게 한다(7장). `acts_for=None`은 "아무나"이고 `free` 커맨드에만 쓸 수 있다.
- 로직 함수는 커맨드를 부를 수 없다(`CMD004`). 커맨드는 밖에서 들어오는 입력이다.

## 사전 조건 `with preconditions:`

본문의 첫 문장으로 둔다. 여기 적은 것은 커맨드를 **실제로 돌리기 전에** 검사하고, 같은 내용이 UI의 "왜 못 하지?"와 AI의 합법 수
나열에도 쓰인다.

| 쓸 수 있는 것 | |
|---|---|
| `require(조건, text("키", 이름=값))` | 거짓이면 거절. 메시지는 꼭 `text(...)`다(문자열은 `CMD002`) |
| `choose(매개변수, where=lambda x: ...)` | 그 매개변수의 후보: 조건을 만족하는 엔티티들 |
| `choose(매개변수, from_=[...])` | 그 매개변수의 후보: 이 목록 |
| `cost(필드 경로, 수)` | 낼 수 있어야 하는 비용. 통과하면 본문 전에 빠진다 |
| `이름 = 식` | 계산한 값을 이름에 두기. 본문에서도 보인다 |

- 사전 조건은 순수해야 한다(쓰기·난수 금지).
- `choose`는 매개변수마다 하나. 뒤에서 고를 매개변수를 앞의 `choose`가 읽으면 안 된다(순서가 있다).
- `choose`가 없는 매개변수는 호스트가 값을 그대로 준다. AI·UI가 후보를 펼치려면 `choose`가 있어야 한다.

`samples/skirmish/core/defs/rules.py`의 응답 커맨드:

```python
@command(acts_for=lambda party: party)
def Move(party: Ref[Party], unit: Ref[Unit], dir: str):
    with preconditions:
        choose(unit, where=lambda u: u.Owner.party == party and not u.Ready.acted)
        choose(dir, from_=open_dirs(unit))
    unit.Pos.x += DIRS[dir][0]
    unit.Pos.y += DIRS[dir][1]
    unit.Ready.acted = True
```

## 본문에서 내는 비용: `pay`

사전 조건이 아니라 본문 중간에 비용을 내야 하면 `ok = pay(house.Purse.gold, 5)`. 모자라면 `False`를 주고 아무것도 빠지지 않는다.
돌려받기는 없으니 필요하면 직접 더한다.

## 실패와 되감기

- `require`가 거짓이거나 `cost`가 모자라면 **거절**이고 상태는 그대로다.
- 본문에서 스크립트 오류(0으로 나누기, 범위 밖 값, 죽은 엔티티 읽기 등)가 나면 그 커맨드가 쓴 것이 **모두 되감긴다**.
  쓴 난수도 쓰지 않은 것이 된다. 반쯤 적용된 상태는 생기지 않는다.
- 호스트는 결과를 구조로 받는다: 성공, 입구에서 막힘(앉지 않은 연결), 인자 오류(없는 커맨드·타입 틀림·후보 밖), 사전 조건 거절
  (실패한 `require`의 `text`), 스크립트 오류(코드와 위치).

## 판 깔기

게임 시작도 커맨드다. 스커미시의 `Start`, 항구의 `Open`:

```python
# 판을 깐다. 아무나 낼 수 있다(acts_for=None)
@command(free=True, acts_for=None)
def Open():
    with preconditions:
        require(town() is None, text("harbor.already_open"))
    spawn("Town")
    gull = spawn("Gull")
    gull.Purse.gold = 20
    crane = spawn("Crane")
    crane.Purse.gold = 20
```

`spawn("Gull")`의 결과는 새 엔티티다. 컴포넌트 시작 값은 `spawn("Sloop", Owner=Owner(house=h))`처럼 줄 수 있다(키 컴포넌트는 안 됨).
