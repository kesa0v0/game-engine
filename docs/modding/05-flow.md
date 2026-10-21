# 5. 흐름과 시간

## 질문: `ask`

스크립트는 도중에 멈추고 누군가에게 물을 수 있다. 답이 오면 **멈춘 곳에서 그대로 이어 간다.** 차례제 게임의 "차례"도, 이벤트의
선택지도 같은 장치다.

`samples/guide/harbor/defs/rules.py`:

```python
# 용선 제안: 3틱 안에 답하지 않으면 거절한 것으로 친다. 받아들이면 2틱 뒤 생선이 들어온다
@command(free=True, acts_for=lambda house: house)
def Charter(house: Ref[House]):
    r = ask(house, text("harbor-charter", gold=5), choices=["harbor-charter-accept", "harbor-charter-decline"],
            deadline=3, on_timeout="harbor-charter-decline")
    if r.choice == "harbor-charter-accept":
        house.Purse.gold += 5
        wait(ticks=2)
        house.Stock.Fish.amount += 3
```

`ask(누구, 제목, ...)`:

| 인자 | 뜻 |
|---|---|
| 누구 | 답할 엔티티, 또는 엔티티 목록. 그 엔티티에 앉은 연결이 답한다(7장) |
| 제목 | 문자열 키나 `text(...)` |
| `choices=[...]` | 고를 선택지. 답은 몇 번째를 골랐나. 문자열 선택지는 화면이 번역 키로 보인다(8장) |
| `responses=["Move", ...]` | 답으로 낼 수 있는 응답 커맨드 이름(다른 모드 것은 `"mod.Name"`) |
| `data={...}` | 화면에 보일 덧붙임 값 |
| `deadline=n`, `on_timeout=값` | n틱 안에 답이 없으면 `on_timeout`(선택지나 응답 이름)으로 답한 것으로 친다. 둘은 함께 쓴다 |
| `blocks_time=` | 이 질문이 열려 있는 동안 시간을 막는가. 기본은 마감이 없으면 막음 |

`choices`나 `responses` 중 하나는 있어야 한다. 돌려받는 답 `r`:

- `r.index`, `r.choice`: 고른 선택지의 번호와 값
- `r.response`: 낸 응답 커맨드 이름
- `r.by`: 답한 엔티티(마감으로 답했으면 `None`)
- `r.interrupted`: 응답 커맨드가 끝까지 가지 못하고 끊겼으면 참(아래)

`ask`·`wait`·`advance`는 멈추는 호출이라 **문장으로만** 쓰고, 쓰는 곳(커맨드·핸들러·흐름 본문)에서만 쓴다.

### 선택지의 보이는 모양: `option`

선택지마다 화면에 보일 이름·설명·모양을 함께 적으려면 `option(값, ...)`으로 감싼다. 꾸밈이 없으면 맨 값을 그대로 쓴다.

```python
r = ask(house, text("harbor-charter", gold=5), choices=[
    option("harbor-charter-accept", desc=text("harbor-charter-accept-desc"), style="gold-glow"),
    "harbor-charter-decline",
])
if r.choice == "harbor-charter-accept":   # 답은 감싼 것이 아니라 안의 값
    ...
```

| 칸 | 뜻 |
|---|---|
| 첫 인자 | 고를 값. 답의 `r.choice`, `on_timeout=`이 가리키는 것 |
| `label=` | 보일 이름(키나 `text(...)`). 없으면 화면이 값에서 꺼낸다 |
| `desc=` | 보일 설명(키나 `text(...)`) |
| `style=` | 보이는 모양의 이름표(문자열). 엔진은 읽지 않고 게임의 화면 파일이 그리는 법을 정한다 |
| `data=` | 화면이 읽을 자유 dict |

- 엔진은 맨 값도 `option`으로 감싸 두므로 화면은 늘 같은 모양을 받는다.
- 칸의 값은 질문과 함께 저장되므로 수·문자열·`text(...)`·엔티티와 그 리스트·dict만 된다. `option`은 `choices=` 안에서만 뜻이 있다(`data=`에 넣으면 실행 오류).
- 상대가 못 보는 엔티티는 칸 안에서도 "알 수 없음"으로 가려진다(7장).

### 응답 커맨드로 답하기

선택지가 아니라 "할 수 있는 행동 중 하나"로 답하게 하려면 `responses=`에 응답 커맨드(`free` 없는 `@command`)를 적는다.
스커미시의 차례 루프(`samples/skirmish/core/defs/rules.py`):

```python
    p = red
    while g.Match.winner is None:
        # Attack의 본문이 맞은 쪽에 물으면 그 답까지 끝난 뒤에 여기로 돌아온다 (G243)
        r = ask(p, "turn", responses=["Move", "Attack", "EndTurn"], data={"turn": g.Match.turn})
        match r.response:
            case "Attack" if alive(p.Order.next) == 0:
                g.Match.winner = p
            case "EndTurn":
                for u in query(Unit):
                    u.Ready.acted = False
                p = p.Order.next
                g.Match.turn += 1
```

답을 가르는 `match`는 3장의 "`match`"를 본다. `"Move"`처럼 맞는 `case`가 없으면 아무것도 하지 않고 다음 바퀴로 간다.

- "차례"라는 특별한 개념은 엔진에 없다. 모드가 이렇게 `ask` 루프로 만든다.
- 응답 커맨드의 `acts_for`는 엔티티를 줘야 한다(`None` 불가). 엔진은 그 엔티티가 질문의 "누구"에 있는지와, 낸 연결이 거기 앉았는지를 본다.
- 응답 커맨드의 본문이 또 물을 수 있다. 스커미시의 `Attack`은 명중하면 맞은 쪽 진영에 "버틸까, 물러날까"를 묻고, 그 답까지 끝난 뒤
  차례 루프의 `ask`가 돌아온다. 본문이 끝까지 가지 못하고 버려지면(오류 등) 바깥은 `r.interrupted`가 참인 답을 받는다.

## 시간

- 엔진의 시간 단위는 **틱**이다. 틱을 넘기는 것은 호스트(`Advance`)나 스크립트의 `advance(n)`이다. 실시간 게임은 호스트가 매 프레임
  넘기고, 차례제 게임은 모드가 정한다.
- `now()`는 지금 틱.
- `wait(ticks=n)`: n틱 뒤에 이어 간다.
- `advance(n)`: 흐름에서 그때까지를 확정하고 시간을 n틱 넘긴 뒤 이어 간다. 실제로 넘긴 틱 수를 준다(도중에 시간을 막는 질문이
  열리면 n보다 적을 수 있다). 핸들러·`@when` 안에서, 그리고 시간이 이미 흐르는 중에는 쓸 수 없다.
- 틱 하나가 넘어갈 때 순서: 틱 +1 → 만료 정리 → 잠든 흐름 깨우기와 마감 지난 질문에 `on_timeout` 넣기 → `Tick` 신호(6장) → 반응 정리.

## 저장

멈춰 있는 흐름(답을 기다리는 `ask`, 자는 `wait`)도 저장된다. 불러오면 같은 곳에서 기다린다. 다만 멈춘 함수의 본문을 고친 뒤 옛 저장을
불러오면 거절된다(9장).
