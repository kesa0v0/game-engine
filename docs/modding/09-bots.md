# 9. 봇: `@bot`과 `bot_state`

사람이 앉지 않은 자리는 질문에 아무도 답하지 않아 판이 멈춘다. 그 자리를 **모드가 쓴 봇**이 맡는다.
봇은 사람 플레이어와 똑같이 한다: 그 자리가 보는 세계를 읽고, 낼 수를 적어 두면 호스트가 그 연결로 낸다.
엔진이 수를 대신 고르지 않는다. 무엇을 둘지는 모두 모드의 봇 함수가 정한다.

## 봇 함수: `@bot`

`samples/skirmish/core/defs/ai.py`:

```python
@bot
def skirmisher(me: Ref[Party]):
    hits = questions("skirmish-hit")              # 맞아서 버틸지 물으면 모두 버틴다
    for q in hits:
        respond(q, "engine.pick", choice=0)
    if len(hits) > 0 or len(questions("skirmish-turn")) == 0:
        return                                    # 내 차례가 아니면 아무것도 적지 않는다
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
- 다음은 `CMD007` 오류다: 엔티티 쓰기, `spawn`·`destroy`·`emit`, `ask`·`wait`·`advance`, `ui.…`. 단 `with imagine():`
  블록 안에서는 복사한 시험 판에 쓸 수 있다(아래 "수를 해 보기").
- 한 번 부를 때 단계 예산이 있다(기본은 판의 행동 예산). 넘으면 그 부름은 없던 일이 된다: 고친 기억, 쓴 난수, 적은 수가 모두 버려진다.
  스크립트 오류도 같다.
  - 식 하나가 한 걸음이고, 크기를 따르는 일은 그 양만큼 센다: `query`가 돌려준 엔티티 수, 목록의 `+`·`in`·비교의 길이,
    `sum`·`min`·`max`·`any`·`all`이 받은 목록의 길이, `with imagine():`의 판 복사(엔티티·값 하나에 300걸음). 그래서 큰 판에서도
    예산을 다 쓰는 데 걸리는 시간이 대략 같다(기본 예산이면 1만 유닛 판에서 약 0.25초 안).

## 수 내기: `submit`과 `engine.pick`

- `submit`은 8장의 화면 동작과 같다. 적어 두기만 하고, 부름이 끝나면 호스트가 적힌 순서대로 그 연결로 낸다. 응답 커맨드는 엔진이 그
  연결이 답할 질문을 찾아 답으로 낸다.
- **선택지 질문**(5장의 `ask(…, choices=[…])`)에는 `submit("engine.pick", choice=번호)`로 답한다. `choice`는 선택지의 0부터 센 번호다.
  - 그 연결이 답할 수 있는 선택지 질문이 꼭 하나일 때 그것에 답한다. 없거나 둘 이상이면 거절된다.
  - 맡은 엔티티가 여럿이라 답할 엔티티가 둘 이상이면 `by=엔티티`로 고른다.
  - `@ui_action`에서도 같이 쓴다.
- 차례가 아닐 때 낸 수, 이미 지나간 질문에 낸 답은 엔진이 거절한다. 지금 열린 질문은 `questions()`(8장)로 읽어, 답할 질문이 있을 때만
  수를 적는다. 같은 이름의 질문이 여럿이면 `respond(q, "engine.pick", choice=…)`로 하나씩 답한다(8장).
- 적은 수가 **하나라도 받아들여지면** 호스트는 그 부름을 "무언가 했다"로 센다. 모두 거절되면 "할 것 없음"이라 호스트는 다음 변화를
  기다린다. 그래서 거절만 되는 수를 적어도 판이 멈추거나 헛돌지 않는다.

## 수를 해 보기: `with imagine():`

상대의 수까지 읽는 봇(minimax, MCTS)은 "이 수를 두면 판이 어떻게 되나"를 알아야 한다. 규칙을 봇 쪽에 다시 짜지 않고, 판을 복사한
**시험 판**에서 진짜 커맨드를 돌려 본다.

```python
@bot
def hunter(me: Ref[Party]):
    best, best_v = None, -999
    for u in my_units(me):
        for t in enemies(me):
            with imagine():                          # 지금 판을 복사한다
                r = play("Attack", party=me, unit=u, target=t)
                if r.ok:
                    v = score(me)                    # 블록 안의 읽기는 모두 시험 판을 본다
                    if v > best_v:
                        best, best_v = (u, t), v
    if best is not None:                             # 진짜 수는 블록 밖에서 낸다
        submit("Attack", party=me, unit=best[0], target=best[1])
```

- **블록 안의 읽기는 모두 시험 판이다.** 블록 전에 잡은 `u`·`t`도, 평소의 `score` 같은 함수도 그대로 시험 판을 읽는다. 블록이 끝나면
  시험 판은 버려지고 다시 진짜 판을 읽는다. 진짜 판과 화면에는 아무것도 가지 않는다.
- **`play`는 `submit`·`respond`와 같은 꼴이다.** `play("커맨드", 이름=값)`은 엔진이 짝을 찾고, `play(q, "커맨드", …)`는 그 질문에
  답한다(`"engine.pick"` 포함). 시험 판에서는 봇이 모든 자리에 앉은 것으로 보므로 상대 진영의 수도 둘 수 있다. 차례와 전제 조건은 그대로
  검사한다.
- **결과 `r`:** `r.ok`(본문까지 돌았나), `r.outcome`(`"succeeded"`·`"refused"`(낼 곳이 없음)·`"arguments_rejected"`·
  `"preconditions_failed"`·`"script_error"`, 질문에 답했으면 답의 결과 이름, 예: `"preconditions_failed"`), `r.failed`(실패한 `require`의 문구 키),
  `r.error`(오류 글), `r.question`(이 수가 연 질문). 실패한 `play`는 시험 판에 아무것도 남기지 않는다.
- **한 블록 안의 `play`는 같은 판에 이어 둔다**(MCTS가 끝까지 두는 꼴). **블록 안의 블록**은 그 자리를 다시 복사한다(minimax의 가지).
- **질문:** 시험 판에서 커맨드가 `ask`에서 멈추면 질문이 열린 채 남는다. 블록 안의 `questions()`는 시험 판의 열린 질문을 모두 주므로
  `play` 뒤에 "다음에 누가 무엇을 정하나"를 본다. 상대의 답마다 블록을 나눠 둔다.
- **시간:** 블록 안의 `advance(n)`은 시험 판을 n틱 굴리고 실제로 간 틱 수를 돌려준다. 막는 질문이 열려 있으면 덜 간다.
- **가정 쓰기:** 블록 안에서는 커맨드 본문처럼 판에 쓸 수 있다(`u.Pos.x = 3`, `spawn`, 쓰는 도우미 함수). "차례를 넘겼다고 치자",
  "여기 서 있다고 치자" 같은 가정에 쓴다. 엔진은 있을 수 없는 판을 막지 않는다. 틀린 가정의 끝은 봇의 나쁜 판단뿐이다.
  `ask`·`wait`·`emit`은 블록 안에서 직접 부를 수 없고(`RUN025`), 그 일을 하는 커맨드를 `play`한다.
- **블록 안에서 못 쓰는 것:** `submit`·`respond`(`RUN025`). 알아낸 것은 지역 변수에 들고 나와 블록 밖에서 낸다. `bot.…`은 쓸 수 있고
  블록이 끝나도 남는다(메모, 점수 표).
- **난수:** 시험 판의 주사위는 진짜 판의 다음 값이 아니다. 블록마다 봇 난수열에서 새 시드를 받는다. `with imagine(seed=1):`처럼 같은
  `seed`를 준 블록끼리는 같은 운이라, 두 수를 같은 운에서 비교할 수 있다.
- **시험 판에서 생긴 엔티티**는 블록 밖에서는 없는 엔티티다(죽은 것처럼 읽힌다). 시험 판에서 죽은 엔티티는 블록 안에서 `bot.…`에 적을
  수 없으므로, 블록 밖에서 적는다.
- **숨긴 정보:** 시험 판은 **봇이 본 판**이다(7장). 못 보는 엔티티는 없고, 못 보는 컴포넌트는 블록 밖과 같이 붙지 않은 것(`None`)으로
  보인다. 추측이 필요하면 `attach(card, Face(city=추측))`으로 채운다. 채우지 않은 곳을 커맨드가 건드리면 `r.error`가 온다.
  블록 밖에서 못 보던 질문(다른 자리에게 연 질문)은 시험 판에서 이름·응답·받는 쪽·선택지 번호만 읽히고, `title`·`data`와 선택지의
  `value`·`label`·`desc`·`data`는 가려짐이다. 번호로 답해(`play(q, "engine.pick", choice=n)`) 상대의 답마다 가지를 나눌 수 있다.
  **숨길 것은 Flow의 지역 변수가 아니라 컴포넌트에 두고 `@view_rule`로 가린다.** 시험 판은 멈춘 Flow를 지역 변수째 복사하므로,
  `deck = random.sample(…)`을 들고 `ask`에서 멈추면 봇이 다음 카드를 안다. 그런 꼴은 로드 경고 `CMD009`로 알린다.
- **쓰기 등급 호스트 함수**를 시험 판에서 부르면 `RUN025`다(C# 쪽의 일은 복사로 되돌릴 수 없다).
- 시험 판의 일도 봇 한 번 부르기의 단계 예산으로 센다. 복사는 판 크기만큼 들고, 단계도 복사하기 전에 그만큼 센다(엔티티·값 하나에
  300걸음, 10장).

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
