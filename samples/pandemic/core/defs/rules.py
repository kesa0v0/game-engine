DISEASES = ["Blue", "Yellow"]
CITIES = ["Atlanta", "Chicago", "Montreal", "NewYork", "Washington", "SanFrancisco",
          "LosAngeles", "MexicoCity", "Miami", "Bogota", "Lima", "SaoPaulo"]

def game():
    for g in query("Game"):
        return g
    return None

def city_of(name):
    for c in query("City"):
        if c.defid == name:
            return c
    return None

# 말이 있는 도시에서 한 걸음에 갈 수 있는 도시들의 이름
def reachable(p):
    return p.At.city.City.neighbors

# 큐브 하나를 올린다. 이미 3개면 올리지 않고, 이번 연쇄에서 처음이면 터져야 한다는 뜻으로 True를 돌려준다
def add_cube(city, disease):
    row = city.Cubes[disease]
    if row.count < 3:
        row.count += 1
        return False
    if city.Chain.broke:
        return False
    city.Chain.broke = True
    return True

# 연쇄 폭발 한 번을 끝까지 푼다. 터진 도시의 이웃마다 그 도시 질병의 큐브가 하나씩 오르고, 넘치는 이웃은 작업 목록에 들어가 이어서 터진다.
# 신호 핸들러는 흐름이 멈춘 뒤에야 돌아서 이 순서를 못 맡는다 (spine-spec S5): 신호는 "터졌다"를 다른 모드에 알리는 용도다
def spread(start):
    g = game()
    queue = [start]
    i = 0
    while i < len(queue):
        city = queue[i]
        i += 1
        g.Match.outbreaks += 1
        emit(Outbreak(city=city))
        for name in city.City.neighbors:
            near = city_of(name)
            over = add_cube(near, city.City.disease)
            if over:
                queue = queue + [near]

# 덱은 뒷면이다: 뽑을 더미의 카드는 어느 도시인지(Face)를 아무도 못 본다. 버린 카드와 손패는 앞면이라 모두 본다.
# 규칙(커맨드·derived)은 실제 상태를 다 읽고, 걸러지는 것은 화면과 AI가 받는 쪽뿐이다.
@view_policy
def may_see(viewer, entity, component):
    if component == "Face":
        return entity.Card.pile != 0
    return True

# 버린 더미와 뽑을 더미를 합쳐 다시 섞는다 (카드 순서는 자리 값, 작을수록 위)
def reshuffle():
    deck = [c for c in query("InfectionCard")]
    slots = random.sample([i for i in range(len(deck))], len(deck))
    i = 0
    for c in deck:
        c.Card.pile = 0
        c.Card.slot = slots[i]
        i += 1

# 맨 위(자리가 가장 작은) 카드를 뽑아 버린 더미에 앞면으로 놓는다. 더미가 비면 버린 카드를 다시 섞는다 (간단히: 에픽 카드의 재섞기는 뒤 조각)
def draw_infection():
    cards = [c for c in query("InfectionCard") if c.Card.pile == 0]
    if len(cards) == 0:
        reshuffle()
        cards = [c for c in query("InfectionCard")]
    top = cards[0]
    for c in cards:
        if c.Card.slot < top.Card.slot:
            top = c
    top.Card.pile = 1
    return top

# 감염 단계: 카드 한 장마다 연쇄가 따로다 (그래서 장마다 "이번 연쇄에서 터짐" 표시를 지운다). 졌으면 바로 멈춘다
def infect(g):
    for i in range(g.Match.rate):
        for c in query("City"):
            c.Chain.broke = False
        card = draw_infection()
        city = card.Face.city
        over = add_cube(city, city.City.disease)
        if over:
            spread(city)
        if g.Match.lost:
            break

# 플레이어 덱에서 맨 위 카드를 한 장 뽑아 손에 든다. 덱이 비어 못 뽑았으면 False
def draw_player(p):
    cards = [c for c in query("PlayerCard") if c.Card.pile == 0]
    if len(cards) == 0:
        return False
    top = cards[0]
    for c in cards:
        if c.Card.slot < top.Card.slot:
            top = c
    top.Card.pile = 2
    top.Held.holder = p
    return True

# 카드 두 장을 뽑는다. 못 뽑으면 진다 (덱이 바닥난 것)
def draw_phase(g, p):
    for i in range(2):
        got = draw_player(p)
        if not got:
            g.Match.deck_out = True
            break

# 이 도시의 카드를 손에 들고 있나
def card_of(p, city):
    for c in p.hand:
        if c.Face.city == city:
            return c
    return None

def discard_card(c):
    c.Card.pile = 1
    c.Held.holder = None

@command(free=True, acts_for=None)
def Start():
    with preconditions:
        require(game() is None, text("pandemic-already-started"))
    for name in CITIES:
        spawn(name)
    # 카드는 섞은 순서로 만든다: 엔티티 번호 순서가 어느 도시인지를 드러내면 안 된다 (안내서 7장의 알려진 한계)
    slot = 0
    for name in random.sample(CITIES, len(CITIES)):
        spawn("Infection", Card=Card(slot=slot), Face=Face(city=city_of(defid(name))))
        slot += 1
    slot = 0
    for name in random.sample(CITIES, len(CITIES)):
        spawn("Back", Card=Card(slot=slot), Face=Face(city=city_of(defid(name))))
        slot += 1
    alice = spawn("Alice")
    bob = spawn("Bob")
    alice.Order.next = bob
    bob.Order.next = alice
    alice.At.city = city_of(defid("Atlanta"))
    bob.At.city = city_of(defid("Atlanta"))
    g = spawn("Pandemic")
    # 처음 손패: 둘이서 하면 네 장씩
    for p in [alice, bob]:
        for i in range(4):
            got = draw_player(p)
    # 처음 감염: 세 장은 큐브 3개, 세 장은 2개, 세 장은 1개
    for amount in [3, 3, 3, 2, 2, 2, 1, 1, 1]:
        card = draw_infection()
        card.Face.city.Cubes[card.Face.city.City.disease].count = amount
    p = alice
    while not g.Match.lost:
        r = ask(p, "pandemic-turn", responses=["Move", "DirectFlight", "BuildStation", "Treat", "EndTurn"],
            data={"turn": g.Match.turn, "left": p.Actions.left})
        if r.response == "EndTurn" or p.Actions.left == 0:
            p.Actions.left = 4
            draw_phase(g, p)
            # 손이 넘치면 한도까지 버려야 한다
            while p.Hand.over and not g.Match.lost:
                ask(p, "pandemic-discard", responses=["Discard"], data={"size": p.Hand.size})
            if not g.Match.lost:
                infect(g)
            p = p.Order.next
            g.Match.turn += 1

@command(acts_for=lambda player: player)
def Move(player: Ref[Player], to: DefId[City]):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        choose(to, from_=reachable(player))
    player.At.city = city_of(to)
    player.Actions.left -= 1

# 직항: 가려는 도시의 카드를 버리고 그 도시로 간다
@command(acts_for=lambda player: player)
def DirectFlight(player: Ref[Player], card: Ref[PlayerCard]):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        choose(card, where=lambda c: c.Held.holder == player and c.Face.city is not None and c.Face.city != player.At.city)
    player.At.city = card.Face.city
    discard_card(card)
    player.Actions.left -= 1

# 연구소 짓기: 지금 있는 도시의 카드를 버린다
@command(acts_for=lambda player: player)
def BuildStation(player: Ref[Player], card: Ref[PlayerCard]):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        require(not player.At.city.Research.station, text("pandemic-has-station"))
        choose(card, where=lambda c: c.Held.holder == player and c.Face.city == player.At.city)
    player.At.city.Research.station = True
    discard_card(card)
    player.Actions.left -= 1

# 치료: 지금 있는 도시의 그 질병 큐브 하나를 걷는다
@command(acts_for=lambda player: player)
def Treat(player: Ref[Player], disease: DefId[Disease]):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        choose(disease, from_=[defid(d) for d in DISEASES if player.At.city.Cubes[defid(d)].count > 0])
    player.At.city.Cubes[disease].count -= 1
    player.Actions.left -= 1

# 손이 넘칠 때 한 장 버린다 (질문 "pandemic-discard"에 대한 답)
@command(acts_for=lambda player: player)
def Discard(player: Ref[Player], card: Ref[PlayerCard]):
    with preconditions:
        choose(card, where=lambda c: c.Held.holder == player)
    discard_card(card)

@command(acts_for=lambda player: player)
def EndTurn(player: Ref[Player]):
    pass
