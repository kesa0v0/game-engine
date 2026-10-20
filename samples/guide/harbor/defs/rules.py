PRICES = {"Sloop": 8, "Galleon": 15}

def town():
    for h in query("Harbor"):
        return h
    return None

def rivals(house):
    return [s for s in query("Ship") if s.Owner.house != house]

# 판을 깐다. 아무나 낼 수 있다(acts_for=None)
@command(free=True, acts_for=None)
def Open():
    with preconditions:
        require(town() is None, text("harbor-already-open"))
    spawn("Town")
    gull = spawn("Gull")
    gull.Purse.gold = 20
    crane = spawn("Crane")
    crane.Purse.gold = 20

# 배를 산다. 그 상회에 앉은 연결만 낼 수 있다
@command(free=True, acts_for=lambda house: house)
def Buy(house: Ref[House], kind: str):
    with preconditions:
        choose(kind, from_=["Sloop", "Galleon"])
        cost(house.Purse.gold, PRICES[kind])
    ship = spawn(kind, Owner=Owner(house=house))
    ship.Hull.hp = ship.Hull.max_hp

# 남의 배를 습격한다. 주사위만큼 내구도가 깎이고, 0이 되면 Sunk 신호를 낸다
@command(free=True, acts_for=lambda house: house)
def Raid(house: Ref[House], target: Ref[Ship]):
    with preconditions:
        choose(target, where=lambda s: s.Owner.house != house)
    roll = random.randint(1, 6)
    target.Hull.hp = max(0, target.Hull.hp - roll)
    if target.Hull.hp == 0:
        emit(Sunk(ship=target, house=target.Owner.house))

# 용선 제안: 3틱 안에 답하지 않으면 거절한 것으로 친다. 받아들이면 2틱 뒤 생선이 들어온다
@command(free=True, acts_for=lambda house: house)
def Charter(house: Ref[House]):
    r = ask(house, text("harbor-charter", gold=5), choices=["harbor-charter-accept", "harbor-charter-decline"],
            deadline=3, on_timeout="harbor-charter-decline")
    if r.choice == "harbor-charter-accept":
        house.Purse.gold += 5
        wait(ticks=2)
        house.Stock.Fish.amount += 3

# 배가 가라앉으면 보험금이 나오고 배는 사라진다
@on(Sunk, stage="harbor.market")
def insurance(ship, house):
    house.Purse.gold += 3
    destroy(ship)

# 틱마다 항구의 날짜가 간다
@on(Tick)
def market_day():
    for h in query("Harbor"):
        h.Port.day += 1

# 금화가 50을 넘은 상회는 비밀 계획을 세운다(상회마다 한 번)
@when(lambda h: h.Purse.gold >= 50, once=True)
def scheme(h: Ref[House]):
    h.Secret.plan = 1

# 비밀 계획은 그 상회만 본다
@view_policy
def may_see(viewer, entity, component):
    if component == "Secret":
        return viewer == entity
    return True
