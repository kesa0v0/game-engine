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

@command(free=True, acts_for=None)
def Start():
    with preconditions:
        require(game() is None, text("pandemic-already-started"))
    for name in CITIES:
        spawn(name)
    alice = spawn("Alice")
    bob = spawn("Bob")
    alice.Order.next = bob
    bob.Order.next = alice
    alice.At.city = city_of(defid("Atlanta"))
    bob.At.city = city_of(defid("Atlanta"))
    g = spawn("Pandemic")
    p = alice
    while True:
        r = ask(p, "pandemic-turn", responses=["Move", "EndTurn"], data={"turn": g.Match.turn, "left": p.Actions.left})
        if r.response == "EndTurn" or p.Actions.left == 0:
            p.Actions.left = 4
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
