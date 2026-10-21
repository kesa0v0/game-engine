# 6. 반응: 신호, `@on`, `@when`

커맨드가 모든 결과를 직접 처리하지 않아도 되게, "무슨 일이 일어났다"를 알리고 다른 곳(다른 모드 포함)이 반응하게 할 수 있다.

## 신호와 `@on`

신호는 스키마 파일에 선언한다. 값은 모두 필수이고 기본값이 없다.

```python
signal("Sunk", ship=Ref["Ship"], house=Ref["House"])
stage("harbor.market")
```

내기는 쓰는 곳에서 문장으로(`samples/guide/harbor/defs/rules.py`):

```python
    if target.Hull.hp == 0:
        emit(Sunk(ship=target, house=target.Owner.house))
```

받기:

```python
# 배가 가라앉으면 보험금이 나오고 배는 사라진다
@on(Sunk, stage="harbor.market")
def insurance(ship, house):
    house.Purse.gold += 3
    destroy(ship)

# 틱마다 항구의 날짜가 간다
@on(Tick)
def market_day():
    for h in query(Harbor):
        h.Port.day += 1
```

- 핸들러의 매개변수는 신호의 값 이름으로 받는다(타입·기본값 없음). 받을 엔티티가 그새 죽었으면 그 핸들러는 건너뛴다.
- `Tick`은 엔진의 신호로, 틱이 넘어갈 때마다 온다(값 없음). 모드가 내거나 선언할 수 없다.
- 핸들러는 로직에서 부를 수 없다. `acts_for`가 없다(누구의 입력도 아니다).
- 핸들러에서 신호를 또 내면 이어서 처리된다(깊이·세대에 한도가 있다).
- 보통 핸들러는 신호를 낸 커맨드(또는 핸들러)가 끝나거나 `ask`로 멈춘 **뒤에** 따로 돈다. 그래서 낸 쪽은 핸들러가 한 일을 보지 못한다.

### 바로 도는 핸들러 `now=True`

남의 규칙 한가운데에 끼어들어야 하면 `now=True`를 붙인다. 함수를 부른 것처럼 `emit`하는 그 자리에서 돌고, 다 끝나야 낸 쪽이
`emit` 다음 줄로 간다(`samples/pandemic/brink/defs/virulent.py`):

```python
# 변종의 폭발은 폭발 표시를 두 칸 옮긴다. 코어가 다음 카드를 뽑기 전에 패배를 본다
@on(Outbreak, now=True)
def slippery(city):
    if pandemic.disease_of(city.City.disease).Virulent.on:
        pandemic.game().Match.outbreaks += 1
```

- 낸 쪽의 일부다: 오류가 나면 낸 커맨드 전체가 되돌려지고, 결과의 반응 목록에 따로 나오지 않는다.
- 여럿이면 보통 핸들러와 같은 순서(단계 → 로드 순서 → 정의 순서)로 돈다. 같은 신호의 보통 핸들러는 그대로 나중에 돈다.
- 안에서는 시간을 막는 `ask`(마감 없음)만 쓸 수 있다. 그러면 낸 쪽 전체가 그 자리에서 답을 기다린다. `wait`·`advance`·마감 있는
  `ask`는 실행 오류다. 기다려야 하는 일은 보통 `@on`으로 한다.
- 핸들러가 저 자신을 다시 부르게 되면(같은 신호를 또 냄) 함수 재귀처럼 오류다.

### 단계 `stage`

같은 신호를 여러 핸들러가 받을 때의 순서를 정한다. 스키마 파일에 `stage("모드id.이름", after=[...], before=[...])`로 선언하고,
`@on(Sunk, stage="harbor.market")`처럼 붙인다. id는 내 모드 id로 시작해야 한다. 다른 모드의 단계에 붙이거나 그 앞뒤로 내 단계를
둘 수 있다(`optional_after`·`optional_before`는 그 단계가 없어도 됨).

실제로 정해진 순서는 `engine stages samples/guide`로 본다. 단계 순서와 그 순서를 만든 `after`·`before`(어느 파일 몇째 줄인지),
신호마다 핸들러가 도는 순서, `@when` 반응의 순서를 늘어놓는다. 단계가 같으면 로드 순서, 그다음 정의 순서이고, `stage=`가 없는
핸들러는 맨 뒤에 돈다.

## 조건 반응 `@when`

"이 조건이 참이 되면"에 반응한다. 조건은 순수한 람다이고, 엔진이 상태가 자리잡는 때(커맨드·답·틱이 끝난 뒤)마다 본다.

```python
# 금화가 50을 넘은 상회는 비밀 계획을 세운다(상회마다 한 번)
@when(lambda h: h.Purse.gold >= 50, once=True)
def scheme(h: Ref[House]):
    h.Secret.plan = 1
```

- 매개변수가 하나(`Ref[타입]`)면 **엔티티마다** 따로 본다. 없으면 월드 전체 조건(`@when(lambda: now() >= 10)`).
- 기본은 거짓 → 참이 될 때 한 번. 옵션:
  - `repeat=True`: 참인 동안 틱 끝마다
  - `cooldown=n`: 한 번 뒤 n틱 쉼
  - `once=True`: 엔티티마다 평생 한 번, `once="game"`: 게임 전체에서 한 번
- 엔진은 조건이 실제로 읽은 엔티티가 바뀌었을 때만 다시 본다. 조건이 `now()`를 읽으면 틱마다 본다.
- 반응은 `ask`할 수 있지만 `advance`는 못 한다.

## 언제 무엇을 쓰나

| 하고 싶은 것 | 쓸 것 |
|---|---|
| 일어난 일에 다른 모드가 덧붙이게 하기 | 신호 + `@on` |
| 남의 순서 한가운데에서 바로 덧붙이기(낸 쪽이 결과를 봐야 함) | 신호 + `@on(..., now=True)` |
| 매 틱 할 일(생산, 성장) | `@on(Tick)` |
| 어떤 상태가 되면(돈이 50 이상, 체력 0) | `@when` |
| 계산해서 보여 줄 값(최대치, 전력 합계) | `derived`(3장) |
