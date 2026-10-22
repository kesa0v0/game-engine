# 질병과 도시는 이름을 박아 두지 않고 Def에서 모두 돈다(defs): 확장 모드가 도시나 질병 Def를 더하면 판에 그대로 깔린다
def game():
    for g in query(Game):
        return g
    return None

def city_of(name):
    for c in query(City):
        if c.defid == name:
            return c
    return None

# 질병의 상태를 든 엔티티 (name은 질병의 전체 이름)
def disease_of(name):
    for d in query(Disease):
        if d.defid == name:
            return d
    return None

# 말이 있는 도시에서 한 걸음에 갈 수 있는 도시들의 이름
def reachable(p):
    return p.At.city.City.neighbors

# ---- 역할의 능력 ----

# 플레이어가 든 역할들의 Def 데이터. 능력은 any(r.능력 for r in roles(p))로 묻는다
def roles(p):
    return [defdata(k).Role for k in p.Roles]

# 이 도시에 이 질병의 큐브를 놓지 못하게 막는 플레이어가 있나:
# 격리 전문가는 있는 도시와 이웃을, 의무병은 있는 도시의 치료제가 있는 질병을 막는다
def protected(city, disease):
    cured = disease_of(disease).Status.cured
    for p in query(Player):
        at = p.At.city
        if at is None:
            continue
        if any(r.quarantine for r in roles(p)) and (at == city or city.defid in at.City.neighbors):
            return True
        if cured and at == city and any(r.clears_cured for r in roles(p)):
            return True
    return False

# 의무병이 치료제가 있는 질병의 큐브가 있는 도시에 있으면 걷는다 (들어갈 때, 치료제를 찾았을 때)
def medic_mess(p):
    at = p.At.city
    if at is None or not any(r.clears_cured for r in roles(p)):
        return False
    return any(at.Cubes[d].count > 0 and disease_of(d).Status.cured for d in at.Cubes)

@when(lambda p: medic_mess(p))
def medic_clears(p: Ref[Player]):
    at = p.At.city
    for d in at.Cubes:
        if disease_of(d).Status.cured:
            at.Cubes[d].count = 0

# ---- 큐브와 폭발 ----

# 큐브 하나를 올린다. 박멸한 질병이거나 역할이 막는 도시면 올리지 않는다. 이미 3개면 올리지 않고, 이번 연쇄에서 처음이면 터져야 한다는 뜻으로 True를 돌려준다
def add_cube(city, disease):
    if disease_of(disease).Status.eradicated or protected(city, disease):
        return False
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

# 치료제가 있는 질병의 큐브가 보드에서 모두 사라지면 박멸이다. 박멸한 질병은 더는 늘지 않는다 (add_cube)
@when(lambda d: d.Status.cured and d.Status.on_board == 0 and not d.Status.eradicated)
def eradicate(d: Ref[Disease]):
    d.Status.eradicated = True

# ---- 덱의 뒷면 ----

# 덱은 뒷면이다: 뽑을 더미의 카드는 어느 도시인지(Face)를 아무도 못 본다. 버린 카드와 손패는 앞면이라 모두 본다.
# 규칙(커맨드·derived)은 실제 상태를 다 읽고, 걸러지는 것은 화면과 AI가 받는 쪽뿐이다.
@view_policy
def may_see(viewer, entity, component):
    if component == Face:
        return entity.Card.pile != 0
    return True

# ---- 감염 덱 ----

# 버린 더미와 뽑을 더미를 합쳐 다시 섞는다 (카드 순서는 자리 값, 작을수록 위). 더미가 바닥났을 때만 쓰는 안전장치다
def reshuffle():
    deck = [c for c in query(InfectionCard)]
    slots = random.sample([i for i in range(len(deck))], len(deck))
    i = 0
    for c in deck:
        c.Card.pile = 0
        c.Card.slot = slots[i]
        i += 1

# 뽑을 더미의 카드들. 비었으면 버린 카드를 다시 섞는다
def infection_pile():
    cards = [c for c in query(InfectionCard) if c.Card.pile == 0]
    if len(cards) == 0:
        reshuffle()
        cards = [c for c in query(InfectionCard)]
    return cards

# 맨 위(자리가 가장 작은) 카드를 뽑아 버린 더미에 앞면으로 놓는다
def draw_infection():
    cards = infection_pile()
    top = cards[0]
    for c in cards:
        if c.Card.slot < top.Card.slot:
            top = c
    top.Card.pile = 1
    return top

# 맨 밑(자리가 가장 큰) 카드를 뽑는다 (에픽 카드)
def draw_infection_bottom():
    cards = infection_pile()
    last = cards[0]
    for c in cards:
        if c.Card.slot > last.Card.slot:
            last = c
    last.Card.pile = 1
    return last

# 버린 더미를 섞어 뽑을 더미 맨 위에 올린다: 뽑을 더미 카드는 버린 장수만큼 아래로 밀리고, 버린 카드는 0번부터 새 자리를 받는다.
# 어떤 도시가 위쪽에 있는지는 모두 안다(앞면으로 버렸으니). 순서만 모른다
def intensify():
    discard = [c for c in query(InfectionCard) if c.Card.pile == 1]
    n = len(discard)
    for c in query(InfectionCard):
        if c.Card.pile == 0:
            c.Card.slot += n
    slots = random.sample([i for i in range(n)], n)
    i = 0
    for c in discard:
        c.Card.pile = 0
        c.Card.slot = slots[i]
        i += 1

# 감염 카드 한 장으로 도시를 감염시킨다: 그 도시 질병의 큐브 하나, 넘치면 폭발. 연쇄 폭발이 이웃에 올리는 큐브는 감염이 아니다(spread).
# 확장이 "감염시킬 때만" 바뀌는 효과를 감쌀 수 있게 함수로 둔다 (안내서 3장)
def infect_city(city):
    over = add_cube(city, city.City.disease)
    if over:
        spread(city)

# 감염 단계: 카드 한 장마다 연쇄가 따로다 (그래서 장마다 "이번 연쇄에서 터짐" 표시를 지운다). 졌으면 바로 멈춘다
def infect(g):
    for i in range(g.Match.rate):
        for c in query(City):
            c.Chain.broke = False
        card = draw_infection()
        infect_city(card.Face.city)
        if g.Match.lost:
            break

# 에픽 카드: 속도를 올리고(Increase), 맨 밑 카드의 도시를 감염시키고(Infect), 버린 더미를 섞어 맨 위에 올린다(Intensify).
# 규칙서의 세 단계를 따로 두어 확장이 단계 사이에 끼어들 수 있게 한다 (안내서 3장)
def epidemic(g):
    g.Match.epidemics += 1
    epidemic_infect(g)
    intensify()

# 에픽의 Infect 단계: 맨 밑 카드의 도시에 큐브를 3개까지 채운다(이미 큐브가 있으면 3개로 채운 뒤 터진다)
def epidemic_infect(g):
    for c in query(City):
        c.Chain.broke = False
    card = draw_infection_bottom()
    city = card.Face.city
    disease = city.City.disease
    if not disease_of(disease).Status.eradicated and not protected(city, disease):
        row = city.Cubes[disease]
        had = row.count
        row.count = 3
        if had > 0:
            city.Chain.broke = True
            spread(city)

# ---- 플레이어 덱 ----

# 플레이어 덱에서 맨 위 카드를 한 장 뽑는다. 에픽 카드면 바로 치르고 버리고, 도시 카드면 손에 든다. 덱이 비어 못 뽑았으면 False
def draw_player(g, p):
    cards = [c for c in query(PlayerCard) if c.Card.pile == 0]
    if len(cards) == 0:
        return False
    top = cards[0]
    for c in cards:
        if c.Card.slot < top.Card.slot:
            top = c
    if top.Face.epidemic:
        top.Card.pile = 1
        epidemic(g)
    else:
        top.Card.pile = 2
        top.Held.holder = p
    return True

# 카드 두 장을 뽑는다. 못 뽑으면 진다 (덱이 바닥난 것)
def draw_phase(g, p):
    for i in range(2):
        got = draw_player(g, p)
        if not got:
            g.Match.deck_out = True
            break
        if g.Match.lost:
            break

def discard_card(c):
    c.Card.pile = 1
    c.Held.holder = None

# 남은 덱(처음 손패를 나눈 뒤)을 에픽 카드 수만큼 더미로 나누고, 더미마다 에픽 카드 하나를 섞어 넣는다:
# 에픽이 덱 전체에 고르게 퍼진다. 뒤 더미부터 처리해야 앞 더미의 새 자리가 뒤 더미를 고르는 계산에 섞이지 않는다
def seed_epidemics():
    rest = [c for c in query(PlayerCard) if c.Card.pile == 0]
    n = len(rest)
    base = rest[0].Card.slot
    for c in rest:
        if c.Card.slot < base:
            base = c.Card.slot
    sizes = [n // EPIDEMICS + (1 if k < n % EPIDEMICS else 0) for k in range(EPIDEMICS)]
    for r in range(EPIDEMICS):
        k = EPIDEMICS - 1 - r
        start = sum([sizes[i] for i in range(k)])
        size = sizes[k]
        offset = random.randint(0, size)
        first = base + start + k
        for c in rest:
            j = c.Card.slot - (base + start)
            if j >= 0 and j < size:
                c.Card.slot = first + j + (1 if j >= offset else 0)
        spawn("Back", Card=Card(slot=first + offset), Face=Face(epidemic=True))

@command(free=True, acts_for=None)
def Start():
    with preconditions:
        require(game() is None, text("pandemic-already-started"))
    for name in defs(Disease):
        spawn(name)
    cities = defs(City)
    for name in cities:
        spawn(name)
    # 카드는 섞은 순서로 만든다: 엔티티 번호 순서가 어느 도시인지를 드러내면 안 된다 (안내서 7장의 알려진 한계)
    slot = 0
    for name in random.sample(cities, len(cities)):
        spawn("Infection", Card=Card(slot=slot), Face=Face(city=city_of(name)))
        slot += 1
    slot = 0
    for name in random.sample(cities, len(cities)):
        spawn("Back", Card=Card(slot=slot), Face=Face(city=city_of(name)))
        slot += 1
    alice = spawn("Alice")
    bob = spawn("Bob")
    alice.Order.next = bob
    bob.Order.next = alice
    alice.At.city = city_of(defid("Atlanta"))
    bob.At.city = city_of(defid("Atlanta"))
    for p in [alice, bob]:
        p.Actions.left = p.Actions.per_turn
    g = spawn("Pandemic")
    # 처음 손패: 둘이서 하면 네 장씩
    for p in [alice, bob]:
        for i in range(4):
            got = draw_player(g, p)
    seed_epidemics()
    # 처음 감염: 세 장은 큐브 3개, 세 장은 2개, 세 장은 1개
    for amount in [3, 3, 3, 2, 2, 2, 1, 1, 1]:
        card = draw_infection()
        card.Face.city.Cubes[card.Face.city.City.disease].count = amount
    p = alice
    while not g.Match.over:
        r = ask(p, "pandemic-turn", responses=["Move", "DirectFlight", "BuildStation", "Treat", "DiscoverCure", "EndTurn"],
            data={"turn": g.Match.turn, "left": p.Actions.left})
        if g.Match.over:
            break
        if r.response == "EndTurn" or p.Actions.left == 0:
            p.Actions.left = p.Actions.per_turn
            draw_phase(g, p)
            # 손이 넘치면 한도까지 버려야 한다
            while p.Hand.over and not g.Match.over:
                ask(p, "pandemic-discard", responses=["Discard"], data={"size": p.Hand.size})
            if not g.Match.over:
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

# 연구소 짓기: 지금 있는 도시의 카드를 버린다. 운영 전문가는 카드 없이 짓는다 (card=None)
@command(acts_for=lambda player: player)
def BuildStation(player: Ref[Player], card: Ref[PlayerCard] | None):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        require(not player.At.city.Research.station, text("pandemic-has-station"))
        choose(card, from_=([None] if any(r.free_station for r in roles(player)) else []) +
            [c for c in player.hand if c.Face.city == player.At.city])
    player.At.city.Research.station = True
    if card is not None:
        discard_card(card)
    player.Actions.left -= 1

# 치료: 지금 있는 도시의 그 질병 큐브 하나를 걷는다. 치료제가 있는 질병이거나 의무병이면 한 번에 모두 걷는다
@command(acts_for=lambda player: player)
def Treat(player: Ref[Player], disease: DefId[Disease]):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        choose(disease, from_=[d for d in defs(Disease) if player.At.city.Cubes[d].count > 0])
    row = player.At.city.Cubes[disease]
    if disease_of(disease).Status.cured or any(r.treat_all for r in roles(player)):
        row.count = 0
    else:
        row.count -= 1
    player.Actions.left -= 1

# 이 질병의 도시 카드를 손에 든 것들
def cards_of(p, disease):
    return [c for c in p.hand if c.Face.city.City.disease == disease]

# 이 플레이어가 이 질병의 치료제를 찾는 데 드는 같은 색 카드 수. 확장이 @wrap으로 감쌀 수 있게 함수로 둔다 (안내서 3장)
def cure_cost(player, disease):
    return player.Cure.cards

# 치료제 찾기: 연구소가 있는 도시에서 같은 색 도시 카드를 cure_cost장(보통 5, 과학자 4) 버린다.
# 더 많이 들었으면 자리 값이 작은(먼저 받은) 카드부터 버린다 (간단화: 어떤 카드를 낼지 고르는 입력은 뒤로)
@command(acts_for=lambda player: player)
def DiscoverCure(player: Ref[Player], disease: DefId[Disease]):
    with preconditions:
        require(player.Actions.left > 0, text("pandemic-no-actions"))
        require(player.At.city.Research.station, text("pandemic-needs-station"))
        choose(disease, from_=[d for d in defs(Disease) if len(cards_of(player, d)) >= cure_cost(player, d) and not disease_of(d).Status.cured])
    for k in range(cure_cost(player, disease)):
        best = None
        for c in cards_of(player, disease):
            if best is None or c.Card.slot < best.Card.slot:
                best = c
        discard_card(best)
    disease_of(disease).Status.cured = True
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
