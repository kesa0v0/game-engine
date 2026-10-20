# 3. 로직: 함수 언어

규칙은 `defs/` 파일의 함수로 쓴다. 문법은 파이썬이지만 **결정론**(같은 입력이면 어느 컴퓨터에서나 같은 결과, 멀티플레이와 리플레이의
바탕)을 지키도록 줄인 언어다.

## 되는 것과 안 되는 것

**된다:** `def`, `if`/`elif`/`else`, `match`/`case`(아래), `for x in ...`, `while`, `return`, `break`, `continue`, `pass`, `=`와 `+= -= *= /= //= %=`,
`a if c else b`, `lambda`, 리스트·사전 리터럴과 컴프리헨션, `any(...)`/`all(...)` 안의 제너레이터 식, `and or not`, `in`,
`is None`/`is not None`, 모듈 상수(`SIZE = 5`).

**안 된다(로드 오류):** `import`, `class`, `try`, 함수 안의 `def`, 재귀, 튜플(여러 값 돌려주기 포함), 슬라이스, `*args`,
f-문자열, `**`·비트 연산, 연쇄 비교(`0 < x < 5`는 `0 < x and x < 5`로), `for`·`while`의 `else`, 같은 이름에 다시 대입(모듈 수준).

**`match`:** 함수 안에서만 쓰고, `case`에는 리터럴(`"Attack"`, `3`, `-1`, `True`, `None`), `|`로 이은 리터럴, `_`(나머지 전부),
가드(`case "Attack" if hp > 0:`)만 쓴다. 맞는 `case`가 없으면 아무것도 하지 않는다. 비교는 `==`와 같고 타입이 다르면 맞지 않는다
(`True`와 `1`은 다르다). **`case x:`처럼 이름만 쓰면 로드 오류다.** 파이썬에서 그것은 `x`와 비교하는 것이 아니라 아무 값이나 받아
`x`에 담는 것이라 함정이기 때문이다. 변수와 비교하려면 `case _ if pick == x:`로 쓴다. 점 이름(`case Kind.Fast:`)과 리스트·사전·클래스
패턴도 아직 안 된다. 예시는 5장의 차례 루프.

**값은 바꿀 수 없다.** 리스트·사전에 메서드가 없고 `a[i] = x`도 안 된다. 새로 만든다: `rows = rows + [x]`.

**문자열:** `+`로 잇기, 비교, `in`(부분 문자열). 인덱싱과 `str()`은 없다. 숫자를 글에 넣는 방법은 아직 없다(9장).

**숫자:** `int`는 64비트이고 넘치면 오류. `/`는 늘 `fixed`를 준다. `//`는 내림 나눗셈, `%`도 파이썬처럼. 0으로 나누면 오류.
`round`는 0에서 먼 쪽으로 반올림한다.

## 내장 함수

| 어디서나 | |
|---|---|
| 수 | `abs`, `min`, `max`, `clamp(x, lo, hi)`, `floor`, `ceil`, `round`, `sum`, `len`, `range` |
| 참거짓 | `any`, `all` |
| 글 | `text("키", 이름=값)`: 번역 키와 인자 (번역 파일은 8장). 키는 `harbor-charter`처럼 하이픈으로 잇고 점은 쓰지 않는다(번역 파일 Fluent의 이름 규칙, 점은 속성 `harbor-charter.tooltip`). 인자는 수·문자열·`True`/`False`·`None`·엔티티·안쪽 `text(...)`만 되고 list·dict·함수는 실행 오류다. 엔티티는 `text()`를 부른 때의 Def를 같이 적어 두어, 그 뒤에 엔티티가 없어져도 이름을 보일 수 있다 |
| 월드 읽기 | `query("타입", "컴포넌트"...)`, `contribs(h, "이름")`, `now()`(지금 틱), `controller(e)`(7장) |

| 쓰는 곳(커맨드·핸들러·흐름의 본문)에서만, **문장으로만** | |
|---|---|
| 엔티티 | `spawn("DefId", 컴포넌트=컴포넌트(...))`, `destroy(e)`, `attach(e, 컴포넌트(...))`, `detach(e.컴포넌트)` |
| 줄 | `add_row(e.C, 키[, [C(...)]])`, `remove_row(e.C, 키)`, `append_row`, `replace_rows` |
| 그 밖 | `emit(신호(...))`(6장), `pay(필드, 수)`(4장), `ask`·`wait`·`advance`(5장) |

"문장으로만"은 한 줄 전체이거나 `x = f(...)`의 오른쪽 전체여야 한다는 뜻이다. `if pay(...)`처럼 식 안에 넣으면 `CMD004`:
먼저 `ok = pay(...)`로 받고 `if ok:`.

빠짐없는 목록과 인자는 스텁(`engine stubs`)에 있다.

## 함수와 이름

- 한 모드의 함수·상수는 그 모드 안 어디서나 부른다(스키마 파일의 함수도 Def 파일에서 보인다).
- **다른 모드의 함수는 부를 수 없다.** 다른 모드의 컴포넌트·신호·커맨드 이름은 `dependencies`에 있으면 쓸 수 있다.
- 없는 이름은 로드 때 `EVL001`과 "혹시 …?". 타입을 아는 곳(매개변수에 `Ref[X]`를 붙였거나, `query("X")`로 돌거나, `spawn` 결과 등)에서는
  `u.Lfie.hp` 같은 컴포넌트·멤버 오타도 로드 때 잡힌다.

## 엔티티 읽고 쓰기

`samples/guide/harbor/defs/rules.py`에서:

```python
def town():
    for h in query("Harbor"):
        return h
    return None

def rivals(house):
    return [s for s in query("Ship") if s.Owner.house != house]
```

| 쓰는 법 | 뜻 |
|---|---|
| `s.Hull.hp` | 컴포넌트 `Hull`의 멤버 |
| `s.Hull.hp -= 3` | 쓰기(`field`와 `ref`만, 쓰는 곳에서만) |
| `s.Ship.base_hp` | Def 타입 이름으로 그 엔티티 Def의 `const` 읽기 |
| `house.ships` | 역방향 컬렉션(그 상회를 가리키는 배들) |
| `house.Stock.Fish.amount`, `house.Stock[k]` | 키 컴포넌트의 줄 |
| `u.Life is None` | 그 컴포넌트가 붙어 있지 않다(또는 보이지 않는다, 7장) |
| `a == b` | 같은 엔티티인가(id 비교) |

- `query("Ship")`은 살아 있는 Ship 엔티티들, `query("Unit", "Wounded")`는 그중 `Wounded`가 붙은 것. 결과는 인덱싱할 수 없다
  (도중에 죽을 수 있어서). 필요하면 `[u for u in query(...)]`로 복사한다.
- 순서는 엔티티 id 순(만든 순)이라 결정론적이다.

## `derived`와 기여 행

`derived`는 읽을 때마다 계산하는 값이다. 함수는 그 컴포넌트를 받는다. `samples/guide/harbor/schema/types.py`:

```python
# 배의 최대 내구도: 배 종류의 기본값 + 기여 행("add")의 합
def hull_max(h):
    bonus = sum([c.value for c in contribs(h, "max_hp") if c.op == "add"])
    return h.entity.Ship.base_hp + floor(bonus)

component("Fleet", strength=derived(int, lambda f: sum([s.Hull.hp for s in f.entity.ships])))
```

- `h.entity`는 그 컴포넌트를 가진 엔티티, `h.Owner`처럼 같은 엔티티의 다른 컴포넌트도 바로 읽는다.
- 함수는 **순수**해야 한다: 쓰기·난수가 있으면 로드 오류(`CMD004`).
- **기여 행**은 `derived` 값에 붙는 "수정치" 줄이다(버프·장비 보너스). 행마다 `op`(문자열, 뜻은 모드가 정함), `value`(`fixed`),
  `source`(낸 엔티티나 `None`)가 있고, `contribs(h, "max_hp")`가 넣은 순서대로 준다. 어떻게 합칠지는 `derived` 함수가 정한다.
- 기여 행을 넣고 빼는 것은 지금 호스트 API(`AddContribution`·`RemoveContribution`)뿐이다. 출처나 대상 엔티티가 죽으면 행도 지워진다.
  스크립트에서 넣는 내장과 만료(몇 틱 뒤 사라짐)는 아직 없다(9장).
- 같은 엔티티 안에서 자기를 다시 읽는 순환은 로드 때 `SCH009`. 다른 엔티티를 거치는 순환은 실행 때 `RUN014`.

## 난수

`random.randint(a, b)`(양끝 포함), `random.randrange`, `random.choice`, `random.choices(seq, weights=, k=)`(정수 가중치),
`random.sample`, `random.random()`(`fixed`, 0 이상 1 미만). `shuffle`·`seed`는 없다.

```python
    roll = random.randint(1, 6)
```

- 씨앗은 호스트가 정한다. 같은 씨앗·같은 입력이면 같은 수가 나온다.
- **쓰는 곳의 본문에서만** 쓴다. `derived`, 사전 조건, `@when` 조건, `where=`, 보기 정책, 화면 함수에서는 로드 오류.
  실패한 커맨드는 난수를 쓰지 않은 것으로 되돌아간다.

## 실행 예산

무한 루프가 게임을 멈추지 않도록 한 번에 쓸 수 있는 평가 걸음 수에 한도가 있다.

- 읽기(`derived`, `acts_for`, 보기 정책, 후보 계산, 화면 함수): 10만 걸음.
- 쓰기(커맨드 본문·사전 조건, 핸들러, 흐름): 1000만 걸음. 읽기 한도를 넘기면 실패하지 않고 경고 `RUN021`("틱에 나누거나
  호스트 함수로 옮겨라")이 붙는다.
- 한도를 넘으면 `RUN013`이고 그 일은 되감긴다. 한 번의 읽기가 부르는 `derived`들은 같은 예산을 나눠 쓴다.

## 호스트 함수

호스트(게임 클라이언트)가 C#으로 등록한 함수를 `core.path.find(a, b)`처럼 부를 수 있다(길찾기처럼 무거운 일). 어떤 함수가 있는지는
호스트가 알려 주고, `engine check --host 선언파일`로 검사한다. 읽기만 하는 함수는 어디서나, 쓰는 함수는 쓰는 곳에서 문장으로만 부른다.
