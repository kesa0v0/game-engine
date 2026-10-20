# 2. 데이터: 스키마, Def, 패치

게임 상태는 **엔티티**들이고, 엔티티는 **컴포넌트**(이름 붙은 값 묶음)를 가진다. 엔티티는 모두 어떤 **Def**(설계도)로 만들어지고,
Def는 어떤 **Def 타입**(`Ship`, `House`)에 속한다.

- **스키마**(`schema/` 파일): 어떤 컴포넌트와 Def 타입이 있는지 선언한다.
- **Def**(`defs/` 파일): 실제 설계도들. `Ship("Sloop", ...)`.
- **패치**(`defs/` 파일): 다른 모드의 Def 값을 고친다.

## 스키마

`samples/guide/harbor/schema/types.py`에서:

```python
component("Purse", gold=field(int, default=0, min=0))
component("Stock", key="Good", amount=field(int, default=0, min=0))
component("Hull", hp=field(int, default=1, min=0), max_hp=derived(int, hull_max))
component("Owner", house=ref("House", optional=True, inverse="ships"))

deftype("Good", price=const(int, default=1), label=const(TextKey, default=""))
deftype("House", components=["Purse", "Stock", "Fleet", "Secret", "Seatable"])
deftype("Ship", components=["Hull", "Owner"],
    base_hp=const(int), speed=const(int, default=1), tags=const(list[str], merge="append", default=[]))
```

### 멤버 네 가지

| 선언 | 뜻 | 예 |
|---|---|---|
| `field(타입, default=, min=, max=)` | 게임 중에 바뀌는 값 | `hp=field(int, default=1, min=0)` |
| `const(타입, default=, min=, max=, merge=)` | Def가 정하고 게임 중에 안 바뀌는 값 | `price=const(int, default=1)` |
| `derived(타입, 함수)` | 읽을 때마다 계산하는 값. 저장되지 않는다 | `max_hp=derived(int, hull_max)` |
| `ref("타입", optional=, inverse=)` | 다른 엔티티를 가리키는 값 | `house=ref("House", optional=True, inverse="ships")` |

- 타입: `int`, `fixed`(소수, 아래), `bool`, `str`, `TextKey`(번역 키), `list[T]`, `dict[K, V]`, `DefId["타입"]`(Def id),
  `Ref["타입"]`(엔티티, `const`·신호 안에서).
- `field`에는 리스트·사전을 둘 수 없다. 여러 개를 들고 싶으면 키 컴포넌트(아래)나 `ref(..., inverse=)`를 쓴다.
- `min`/`max`를 넘는 값을 쓰면 실행 오류다. 깎을 때는 `max(0, hp - n)`이나 `clamp`를 쓴다.
- `field(..., default_from="max_hp")`처럼 처음 값을 다른 멤버에서 가져올 수 있다.
- `const`의 `merge="append"`는 상속할 때 부모 리스트에 이어 붙인다(아래 상속). 리스트에만 쓴다.
- Def 타입 바로 아래(`deftype(..., base_hp=const(int))`)에는 `const`만 둘 수 있다.

### 소수: `fixed`

`1.5`처럼 소수점을 쓰면 `fixed`다. 소수점 아래 6자리까지(`LEX007`). 부동소수점은 없다. 결정론(같은 입력 → 같은 결과)을 위해서다.
`int`를 넣어야 할 곳에 `fixed`를 넣으면 오류이고, 반대는 된다.

### Ref와 역방향 컬렉션

`Owner.house`는 배가 속한 상회를 가리킨다. `inverse="ships"`를 주면 상회 쪽에서 `house.ships`로 **그 상회를 가리키는 배들**을
읽을 수 있다(엔진이 관리한다, 직접 쓰지 않는다). `optional=True`면 비어 있을 수 있고 가리키던 엔티티가 죽으면 `None`이 된다.

### 키 컴포넌트 (줄)

`component("Stock", key="Good", amount=...)`는 **Good Def마다 한 줄**씩 있는 컴포넌트다. 상회의 생선 수는
`house.Stock.Fish.amount`다.

- `key="Def타입"`: Def마다 한 줄. 기본은 `keys="all"`(그 타입의 Def 전부에 줄이 있음). `keys="listed"`면 적은 줄만.
- `key=Ref["Unit"]`: 엔티티마다 한 줄(예: 유닛별 호감도). 스크립트에서 `add_row`·`remove_row`로 줄을 더하고 뺀다.
- `key=int`: 순서 있는 목록. `append_row`·`replace_rows`.

### 엔진 컴포넌트

`Seatable`(값 없음)은 엔진이 주는 컴포넌트로, "로비에서 앉을 수 있는 엔티티"라는 표시다(7장). 누구나 의존성 없이 쓴다.

### 이름 규칙

Def 타입·컴포넌트·신호는 이름 하나를 함께 쓴다. 두 모드가 같은 이름을 선언하면 `SCH001`이고 둘 다 꺼진다.
남의 Def 타입에 컴포넌트를 더하려면 `extend("Unit", components=["Morale"])`를 쓴다(더하는 `const`는 기본값이 있어야 한다).

## Def

`samples/guide/harbor/defs/things.py`:

```python
Good("Fish", price=2, label="harbor-good-fish")
Good("Salt", price=5, label="harbor-good-salt")

House("Gull")
House("Crane")

# 추상 Def는 만들 수 없고 물려주기만 한다
Ship("BaseShip", abstract=True, base_hp=3, tags=["wood"])
Ship("Sloop", parent="BaseShip", speed=2, tags=["fast"])        # tags는 ["wood", "fast"] (merge="append")
Ship("Galleon", parent="BaseShip", base_hp=inherit.base_hp + 4)  # base_hp는 7
```

- `타입("id", 값들...)`. 이 Def의 id는 `모드id:id`다(harbor의 `"Sloop"`은 `harbor:Sloop`, 아래 "Def id의 이름 공간").
  한 모드 안에서 id는 **모든 타입을 통틀어** 하나뿐이어야 한다(`DEF001`). 다른 모드와는 이름이 같아도 된다.
- 기본값 없는 `const`(리스트·사전 제외)는 꼭 적어야 한다(`VAL001`). 추상 Def는 빼도 된다.
- 컴포넌트의 시작 값은 `Ship("X", Hull=Hull(hp=5))`처럼 준다. 키 컴포넌트는 `House("X", Stock={"Fish": Stock(amount=3)})`.
- **다른 Def를 가리킬 때는 id 문자열**을 쓴다(`"Fish"`). 있는지, 타입이 맞는지, 보이는지, 추상이 아닌지는 로드 때 검사한다
  (`LNK001`~`LNK004`, 오타면 "혹시 …?").

### Def id의 이름 공간

- Def id의 전체 이름은 `모드id:경로`다(`harbor:Sloop`). 경로 안을 `/`로 더 나눌 수 있다(`Ship("rigs/Brig")` → `harbor:rigs/Brig`).
  조각은 영문자·숫자·`_`이고 대소문자를 가린다(`DEF005`).
- 스크립트에서 `:` 없이 쓴 이름은 **그 코드를 쓴 모드의 것**이다. 다른 모드의 Def는 전체 이름으로 쓴다
  (lighthouse에서 harbor의 배는 `"harbor:Sloop"`, 그냥 `"Sloop"`이면 `lighthouse:Sloop`을 찾는다).
- 게임 중의 Def id 값(`DefId` 필드, `spawn`이 만든 엔티티의 Def)은 **언제나 전체 이름**이다. 타입이 `DefId`인 자리
  (`DefId` 필드·const, `parent=`, `spawn("Sloop")`, `patch.*`의 대상, `DefId` 매개변수의 `choose(from_=...)`)에 쓴 짧은 이름은 엔진이 바꿔 준다.
- 타입을 모르는 자리(`==` 비교, 사전 키)에는 전체 이름이나 `defid("Sloop")`을 쓴다. `defid`는 쓴 모드 기준 전체 이름을 돌려주고,
  없는 Def면 오류다. `DefId` 값을 맨 문자열과 비교하면 언제나 거짓이라 경고 `LNK005`가 난다.

```python
if ship.Mark.kind == "Sloop":          # LNK005: 값은 "harbor:Sloop"이라 언제나 거짓
if ship.Mark.kind == defid("Sloop"):   # 맞음
PRICES = {defid("Sloop"): 8}           # 사전 키도
```

- 호스트(C# API, 명령 인자, 저장 파일)는 전체 이름만 받는다.
- `TextKey` 값은 번역 키 문자열이다(`label="harbor-good-fish"`). 글은 `locale/`의 번역 파일에 둔다(8장).
  Def 자신의 화면 이름은 따로 적지 않고 번역 파일의 `def-harbor-Sloop` 키에 둔다(`/`도 `-`로: `def-harbor-rigs-Brig`).
- Def에는 엔티티 Ref를 적을 수 없다. 엔티티는 게임 중에 생긴다.

### 상속

- `parent="BaseShip"`이면 적지 않은 값은 부모에게서 물려받는다. 컴포넌트는 값 하나씩, 키 컴포넌트는 줄 하나씩 합쳐진다.
- 리스트는 기본적으로 덮어쓰고, `merge="append"`인 `const`는 부모 것 뒤에 이어 붙는다.
- `inherit.base_hp + 4`, `inherit.Hull.hp`처럼 부모의 최종 값을 읽어 계산할 수 있다(`+ - *`, 리스트·문자열 `+`만).
- 남의 모드의 Def를 통째로 바꾸려면 전체 이름으로 `Ship("harbor:Sloop", override=True, ...)`(같은 타입, 보이는 Def여야 함).
  `override` 없이 남의 전체 이름을 쓰면 `DEF005`다. 값 몇 개만 바꿀 때는 패치를 쓴다.

## 패치

`samples/guide/lighthouse/defs/patches.py`:

```python
# harbor의 Def를 고치는 모드. harbor를 dependencies에 적었으므로 그 Def가 보이고, harbor 뒤에 불린다
# 다른 모드의 Def는 "모드id:이름"으로 쓴다. ":" 없이 쓰면 이 모드(lighthouse)의 것이다
patch.add("harbor:Galleon", "speed", 1)
patch.set("harbor:Salt", "price", 4)
patch.append("harbor:Sloop", "tags", ["lit"])

# 새 Def도 더할 수 있다. 이 Def의 id는 "lighthouse:Cutter"다
Ship("Cutter", parent="harbor:BaseShip", speed=3)

# smuggler 모드가 있을 때만
if mod_loaded("smuggler"):
    patch.set("harbor:Salt", "price", 9)
```

| 호출 | 무엇에 |
|---|---|
| `patch.set(id, 경로, 값)` | 아무 값. 타입의 `components=`에 없는 컴포넌트를 그 Def에만 붙이기도(`patch.set("Sloop", "Morale", Morale(value=7))`) |
| `patch.add(id, 경로, 수)` | `int`·`fixed`에 더하기 |
| `patch.append(id, 경로, 리스트나 사전)` | 리스트·사전에 이어 붙이기 |
| `patch.remove(id, 경로)` / `patch.remove(id, 경로, 원소나 키)` | 붙인 컴포넌트·줄 떼기 / 리스트·사전에서 빼기 |

- 경로: `"speed"`, `"Hull.hp"`, `"Stock.Fish.amount"`. 경로 안의 Def 키도 짧으면 패치하는 모드 기준이라, 다른 모드의 Def 키는
  `"Stock['harbor:Fish'].amount"`처럼 `['...']`로 쓴다.
- Def를 지우는 패치는 없다.
- 상속이 끝난 뒤 패치가 **로드 순서대로 한 번에** 적용된다.
- **순서 없는 두 모드가 같은 값(또는 그 안쪽)을 건드리면 둘 다 오류(`PAT010`)이고 꺼진다.** 한쪽에 `loadAfter`를 적어 순서를 정한다.
  `add`끼리, `append`끼리는 겹쳐도 된다. 순서가 있으면 뒤 모드가 이기고 덮어쓴 경우 경고 `PAT011`이 난다.
- `if mod_loaded("x"):` 안에는 패치만 둘 수 있고, `x`는 `dependencies`나 `optional`에 있어야 한다. 그 모드가 없으면 정보
  `PAT002`(위 `engine check` 출력)가 나고 건너뛴다.

### 오류는 누구 책임인가

로드 때 값이 틀리면(없는 Def를 가리킴, 범위 밖) 그 **값을 처음 적은 모드**의 오류가 되고 그 모드가 꺼진다. Def에 직접 적은 값이면
그 Def의 모드, 패치가 적은 값이면 그 패치의 모드와 줄, 물려받은 값이면 그 값을 적은 조상 쪽이다. 그래서 남의 모드를 고친 패치가
틀리면 원래 모드는 멀쩡하고 내 모드만 꺼진다.
