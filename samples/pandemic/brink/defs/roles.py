# 확장 역할. 제너럴리스트는 턴마다 행동을 하나 더 받는다: 코어가 행동 수를 derived(Actions.per_turn)로 두어 선언 보너스로 된다
Role("Generalist", grants=[contrib("Actions.per_turn", "add", 1)])
# 트러블슈터: 차례를 시작할 때 감염 덱 위에서 감염 속도만큼 카드를 보고 같은 순서로 되돌려 놓는다.
# 직항을 할 때 쓴 도시 카드를 보여 주고 버리지 않는다 (규칙서 역할 카드). 능력은 아래 함수들이 역할을 보고 켠다
Role("Troubleshooter")

def troubleshooter(p):
    return any(k == defid("Troubleshooter") for k in p.Roles)

# ---- 몰래 보기 (G322) ----

# 이 플레이어가 이 감염 카드를 아는가
def knows(card, p):
    return any(k == p for k in card.Known)

# 뽑을 더미의 위 n장 (위부터)
def top_infection(n):
    pile = [c for c in query(InfectionCard) if c.Card.pile == 0]
    top = []
    while len(top) < n and len(pile) > 0:
        best = pile[0]
        for c in pile:
            if c.Card.slot < best.Card.slot:
                best = c
        top = top + [best]
        pile = [c for c in pile if c != best]
    return top

# 차례를 시작할 때 본다. 카드는 움직이지 않고 본 사람만 적는다
@wrap(pandemic.begin_turn)
def troubleshoot(original, g, p):
    original(g, p)
    if troubleshooter(p):
        for c in top_infection(g.Match.rate):
            if not knows(c, p):
                add_row(c.Known, p)

# 아는 사람에게는 덮인 카드의 도시가 보인다. 아니면 코어의 규칙(덮였으면 안 보임)에 맡긴다
@view_rule(Face)
def troubleshooter_sees(viewer, entity):
    if viewer is not None and entity.Known is not None and knows(entity, viewer):
        return True
    return None

# 언제 잊나: 아는 것은 카드가 뽑을 더미에서 그 순서로 있는 동안만이다. 뽑힌 카드는 앞면이 되고, Intensify로 돌아오면
# 섞인 뒤라 모른다. 그래서 뽑을 때 지운다. 덱이 바닥나 다시 섞으면(reshuffle) 모두 지운다. Intensify는 뽑을 더미의 순서를 바꾸지 않는다
def forget(card):
    for k in [k for k in card.Known]:
        remove_row(card.Known, k)

@wrap(pandemic.draw_infection)
def forget_top(original):
    card = original()
    forget(card)
    return card

@wrap(pandemic.draw_infection_bottom)
def forget_bottom(original):
    card = original()
    forget(card)
    return card

@wrap(pandemic.reshuffle)
def forget_all(original):
    original()
    for c in query(InfectionCard):
        forget(c)

# ---- 직항 ----

# 쓴 카드를 버리지 않고 손에 되돌린다. 이 예제의 손패는 모두에게 공개라(코어 view.py) "보여 주기"는 따로 할 일이 없다
@wrap(pandemic.DirectFlight)
def troubleshooter_flight(original, player, card):
    original(player, card)
    if troubleshooter(player):
        card.Card.pile = 2
        card.Held.holder = player
