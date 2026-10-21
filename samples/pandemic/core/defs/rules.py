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

# 감염 덱에서 한 장 뽑는다. 더미가 비면 버린 카드를 다시 모은다 (간단히: 에픽 카드의 재섞기는 뒤 조각)
def draw_infection():
    cards = [c for c in query("InfectionCard") if c.Card.pile == 0]
    if len(cards) == 0:
        for c in query("InfectionCard"):
            c.Card.pile = 0
        cards = [c for c in query("InfectionCard")]
    card = random.choice(cards)
    card.Card.pile = 1
    return card

# 감염 단계: 카드 한 장마다 연쇄가 따로다 (그래서 장마다 "이번 연쇄에서 터짐" 표시를 지운다). 졌으면 바로 멈춘다
def infect(g):
    for i in range(g.Match.rate):
        for c in query("City"):
            c.Chain.broke = False
        card = draw_infection()
        city = card.Card.city
        over = add_cube(city, city.City.disease)
        if over:
            spread(city)
        if g.Match.lost:
            break

@command(free=True, acts_for=None)
def Start():
    with preconditions:
        require(game() is None, text("pandemic-already-started"))
    for name in CITIES:
        c = spawn(name)
        spawn("Infection", Card=Card(city=c))
    alice = spawn("Alice")
    bob = spawn("Bob")
    alice.Order.next = bob
    bob.Order.next = alice
    alice.At.city = city_of(defid("Atlanta"))
    bob.At.city = city_of(defid("Atlanta"))
    g = spawn("Pandemic")
    # 처음 감염: 세 장은 큐브 3개, 세 장은 2개, 세 장은 1개
    for amount in [3, 3, 3, 2, 2, 2, 1, 1, 1]:
        card = draw_infection()
        card.Card.city.Cubes[card.Card.city.City.disease].count = amount
    p = alice
    while not g.Match.lost:
        r = ask(p, "pandemic-turn", responses=["Move", "EndTurn"], data={"turn": g.Match.turn, "left": p.Actions.left})
        if r.response == "EndTurn" or p.Actions.left == 0:
            p.Actions.left = 4
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

@command(acts_for=lambda player: player)
def EndTurn(player: Ref[Player]):
    pass
