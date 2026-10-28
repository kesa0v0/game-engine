# 9. 봇: `@bot`과 `bot_state`

사람이 앉지 않은 자리는 질문에 아무도 답하지 않아 판이 멈춘다. 그 자리를 **모드가 쓴 봇**이 맡는다.
봇은 사람 플레이어와 똑같이 한다: 그 자리가 보는 세계를 읽고, 낼 수를 적어 두면 호스트가 그 연결로 낸다.
엔진이 수를 대신 고르지 않는다. 무엇을 둘지는 모두 모드의 봇 함수가 정한다.

## 봇 함수: `@bot`

`samples/skirmish/core/defs/ai.py`:

```python
@bot
def skirmisher(me: Ref[Party]):
    submit("engine.pick", choice=0)
    ready = [u for u in query(Unit) if u.Owner.party == me and not u.Ready.acted]
    for u in ready:
        for t in query(Unit):
            if t.Owner.party != me and dist(u, t) <= u.Unit.reach:
                submit("Attack", party=me, unit=u, target=t)
                return
    for u in ready:
        t = nearest_enemy(u, me)
        d = None if t is None else step_toward(u, t)
        if d is not None:
            submit("Move", party=me, unit=u, dir=d)
            return
    submit("EndTurn", party=me)
```

- 첫 매개변수는 봇이 맡는 엔티티다. `Ref[…]`이고 기본값이 없다.
- 그 뒤 매개변수는 **설정**이다(`level: int = 2`). 모두 기본값이 있어야 한다. 호스트가 봇을 붙일 때 바꿔 넣을 수 있다.
- 호스트는 판이 바뀔 때마다 봇을 **한 번** 부른다. 한 번 부를 때 여러 수를 적어도 되고, 위처럼 한 수만 적고 다음 부름을 기다려도 된다.
- 봇은 커맨드처럼 낼 수 없고 로직에서 부를 수도 없다. 부르는 것은 호스트뿐이다.

### 본문이 할 수 있는 것과 없는 것

- 게임은 **그 자리가 보는 세계**로 읽기만 한다(7장). 숨긴 엔티티는 `query`에서 빠진다. 맡은 엔티티가 죽거나 안 보이면 그 부름은 거절된다.
- 쓸 수 있는 것: `bot.기록.칸`(아래)과 `submit`.
- 난수(`random`)를 쓸 수 있다. 게임의 난수와 따로이고, 봇을 붙일 때 정한 시드로 정해진다. 시드를 주지 않으면 월드 시드·맡은 엔티티·봇
  이름에서 만든다. 그래서 같은 판을 다시 돌리면 봇도 같은 수를 둔다.
- 다음은 `CMD007` 오류다: 엔티티 쓰기, `spawn`·`destroy`·`emit`, `ask`·`wait`·`advance`, `ui.…`.
- 한 번 부를 때 단계 예산이 있다(기본은 판의 행동 예산). 넘으면 그 부름은 없던 일이 된다: 고친 기억, 쓴 난수, 적은 수가 모두 버려진다.
  스크립트 오류도 같다.

## 수 내기: `submit`과 `engine.pick`

- `submit`은 8장의 화면 동작과 같다. 적어 두기만 하고, 부름이 끝나면 호스트가 적힌 순서대로 그 연결로 낸다. 응답 커맨드는 엔진이 그
  연결이 답할 질문을 찾아 답으로 낸다.
- **선택지 질문**(5장의 `ask(…, choices=[…])`)에는 `submit("engine.pick", choice=번호)`로 답한다. `choice`는 선택지의 0부터 센 번호다.
  - 그 연결이 답할 수 있는 선택지 질문이 꼭 하나일 때 그것에 답한다. 없거나 둘 이상이면 거절된다.
  - 맡은 엔티티가 여럿이라 답할 엔티티가 둘 이상이면 `by=엔티티`로 고른다.
  - `@ui_action`에서도 같이 쓴다.
- 차례가 아닐 때 낸 수, 이미 지나간 질문에 낸 답은 엔진이 거절한다. 봇이 지금 열린 질문을 직접 읽는 방법은 아직 없으니(10장),
  위의 스커미시 봇처럼 "답할 만한 것을 다 적고 거절은 흘려보낸다"로 쓴다.
- 적은 수가 **하나라도 받아들여지면** 호스트는 그 부름을 "무언가 했다"로 센다. 모두 거절되면 "할 것 없음"이라 호스트는 다음 변화를
  기다린다. 그래서 거절만 되는 수를 적어도 판이 멈추거나 헛돌지 않는다.

## 봇의 기억: `bot_state`

봇이 지난 부름에서 본 것(노리던 적, 지난 차례의 계획)을 다음 부름에 쓰려면 기억에 적는다.

```python
# 스키마
bot_state("Mind", target=ref("Unit", optional=True), seen=field(list[Ref["Unit"]], default=[]), turns=field(int, default=0))
```

```python
@bot
def hunter(me: Ref[Party]):
    if bot.Mind.target is None:
        bot.Mind.target = nearest_enemy_of(me)
    bot.Mind.turns = bot.Mind.turns + 1
    ...
```

- 표기와 칸 규칙은 `ui_state`(8장)와 같다: 모든 칸에 기본값, Ref는 `optional=True`, 리스트·사전 칸, 엔티티 키를 쓸 수 있다.
  이름 공간도 타입·컴포넌트·신호·`ui_state`와 하나다.
- 기억은 **봇마다 하나**다. 같은 봇 함수를 두 자리에 붙이면 기억도 둘이다.
- 가리키던 엔티티가 죽거나 안 보이면 `ui_state`처럼 `None`으로 읽히거나 리스트·사전에서 빠진다.
- 기억은 게임 상태가 아니다. 게임 세이브에 들지 않고, 호스트가 봇을 따로 저장해 판 세이브 옆에 둔다. 불러온 모드에서 사라진 칸은 버리고
  경고를 낸다(10장).
- `bot.…`은 `@bot`과 그것이 부르는 함수만 쓸 수 있다. 게임 쪽 코드와 `@ui_action`이 쓰면 `CMD007`이고, 호스트는 그런 함수를
  화면 함수로 부르지 않는다.
  `bot`에 **쓰는** 도우미 함수는 문장으로만 부른다(8장의 `ui`와 같다).

## 호스트가 봇을 붙이는 법

모더가 할 일은 아니지만 무엇이 일어나는지 알아 두면 좋다.

- 호스트는 자리를 AI로 앉히고(`Seat(…, ai: true)`), 그 연결의 세션에서 봇 이름(`skirmish.skirmisher`)과 맡을 엔티티로 봇을 만든다.
  설정 값과 시드는 고를 수 있다.
- 판이 바뀔 때마다 봇을 부른다. 문제(스크립트 오류, 거절 이유)는 호스트가 기록에 보인다.
- Unity 데모(`hosts/unity/demo`)는 파랑 자리에 `skirmish.skirmisher`를 붙여 사람(빨강)과 겨룬다. 테스트
  `TwoModBotsPlayAGameToTheEndAnsweringTheHitQuestions`는 두 진영 모두 이 봇으로 한 판을 끝까지 돈다.
