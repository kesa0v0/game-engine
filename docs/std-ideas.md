# std 모음집 후보

공식 모드로 만들지 않기로 한 편의 라이브러리의 **모양**을 모아 둔다(G469). 지금 엔진으로 모드가 직접 짤 수 있는 것이다. 나중에 공식 모음집(`std` 같은 모드)을 만들게 되면 여기서 시작한다. 모드 id에는 점이 안 되므로(G2) `std.ai` 같은 이름은 쓸 수 없다.

항목마다 그 모양이 지금도 되는지 지키는 테스트를 하나씩 둔다.

## 점수 매겨 고르는 봇 (후보 행동)

Wesnoth의 후보 행동(RCA)처럼 후보마다 점수를 매겨 가장 높은 것을 고른다. 다른 모드가 후보를 더하거나 바꿀 수 있다.

- 후보는 Def이고, 가중치는 그 Def의 `const`다. 다른 모드가 `patch.set`으로 바꾼다.
- 점수 계산은 함수 하나가 후보의 종류(`kind`)로 나눠 한다. 다른 모드는 `@wrap`으로 새 종류를 끼운다.
- 봇은 `defs(Tactic)`로 후보를 다 돈다(의존하지 않는 모드가 더한 후보도 나온다).
- 확률로 고르려면 `random.choices(후보, weights=[...], k=1)`을 쓴다(봇 난수열, G373).

코어 모드 `brain`:

```python
# schema
deftype("Tactic", weight=const(int, default=1), kind=const(str))
bot_state("Mind", picked=field(str, default=""))

# defs
Tactic("Hit", weight=2, kind="hit")
Tactic("Rest", weight=5, kind="rest")

def score(kind, me):
    if kind == "hit":
        return len(query(Unit))
    if kind == "rest":
        return 1
    return 0

@bot
def planner(me: Ref[Party]):
    best = ""
    top = -1
    for t in defs(Tactic):
        d = defdata(t).Tactic
        s = d.weight * score(d.kind, me)
        if s > top:
            top = s
            best = t
    bot.Mind.picked = best
```

확장 모드 `raider`(`dependencies=["brain"]`):

```python
Tactic("Raid", weight=3, kind="raid")       # 후보 더하기
patch.set("brain:Rest", "weight", 1)        # 남의 가중치 바꾸기

@wrap(brain.score)                          # 새 종류의 점수 끼우기
def raid_score(original, kind, me):
    if kind == "raid":
        return 10
    return original(kind, me)
```

유닛 넷이면 Hit 2×4=8, Rest 1×1=1, Raid 3×10=30이라 `raider:Raid`를 고른다.

테스트: `tests/Kesa.Engine.Kernel.Tests/Runtime/ScoredTacticsShapeTests.cs`

**공식으로 만들면 더할 것:** G374의 함수 칸 `Score["T"]`(T를 받아 수를 돌려주는 순수 함수)와 `BotAction["T"]`(봇 본문 규칙으로 도는 함수). 그러면 `Tactic("Raid", weight=3, score=raid_score)`처럼 함수를 Def에 바로 넣어 `kind` 문자열과 `@wrap`이 필요 없다. 쓰기 편해지는 것이고, 없어서 못 하는 일은 없다.

## 더 넣을 만한 것 (아직 모양 없음)

이전에 공식 `std` 안에 넣으려던 것: 능력치(stats), 겹치기(stack). 모양이 생기면 위처럼 예시와 테스트를 단다.
