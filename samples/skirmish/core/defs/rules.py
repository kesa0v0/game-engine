SIZE = 5
DIRS = {"north": [0, -1], "south": [0, 1], "west": [-1, 0], "east": [1, 0]}

def game():
    for g in query(Game):
        return g
    return None

def unit_at(x, y):
    for u in query(Unit):
        if u.Pos.x == x and u.Pos.y == y:
            return u
    return None

def free(x, y):
    return 0 <= x and x < SIZE and 0 <= y and y < SIZE and unit_at(x, y) is None

def open_dirs(u):
    return [d for d in DIRS if free(u.Pos.x + DIRS[d][0], u.Pos.y + DIRS[d][1])]

def dist(a, b):
    return abs(a.Pos.x - b.Pos.x) + abs(a.Pos.y - b.Pos.y)

def sign(n):
    if n > 0:
        return 1
    if n < 0:
        return -1
    return 0

def place(party, kind, x, y):
    u = spawn(kind, Owner=Owner(party=party), Pos=Pos(x=x, y=y))
    u.Life.hp = u.Unit.max_hp

def alive(party):
    return len([u for u in query(Unit) if u.Owner.party == party])

# 명중하면 맞은 쪽 주인이 버틸지 물러날지 고른다. 물러나면 피해가 절반이고 공격자 반대쪽으로 한 칸 간다
def resolve(a, t):
    roll = random.randint(1, 6)
    if roll < 4:
        return
    damage = a.Unit.attack
    bx = t.Pos.x + sign(t.Pos.x - a.Pos.x)
    by = t.Pos.y + sign(t.Pos.y - a.Pos.y)
    if free(bx, by):
        r = ask(t.Owner.party, "skirmish-hit", choices=["skirmish-hit-hold", "skirmish-hit-retreat"], data={"unit": t, "roll": roll, "damage": damage})
        if r.index == 1:
            damage = damage // 2
            t.Pos.x = bx
            t.Pos.y = by
    t.Life.hp -= damage
    if t.Life.hp <= 0:
        destroy(t)

# 판을 깔고 차례를 돌린다. 차례는 이 모드의 것이다: 지금 진영에게 Move·Attack·EndTurn 중 하나를 계속 묻는다
@command(free=True, acts_for=None)
def Start():
    with preconditions:
        require(game() is None, text("skirmish-already-started"))
    red = spawn("Red")
    blue = spawn("Blue")
    red.Order.next = blue
    blue.Order.next = red
    g = spawn("Battle")
    place(red, "Soldier", 1, 4)
    place(red, "Archer", 2, 4)
    place(red, "Soldier", 3, 4)
    place(blue, "Soldier", 1, 0)
    place(blue, "Archer", 2, 0)
    place(blue, "Soldier", 3, 0)
    p = red
    while g.Match.winner is None:
        # Attack의 본문이 맞은 쪽에 물으면 그 답까지 끝난 뒤에 여기로 돌아온다 (G243)
        r = ask(p, "skirmish-turn", responses=["Move", "Attack", "EndTurn"], data={"turn": g.Match.turn})
        match r.response:
            case "Attack" if alive(p.Order.next) == 0:
                g.Match.winner = p
            case "EndTurn":
                for u in query(Unit):
                    u.Ready.acted = False
                p = p.Order.next
                g.Match.turn += 1

@command(acts_for=lambda party: party)
def Move(party: Ref[Party], unit: Ref[Unit], dir: str):
    with preconditions:
        choose(unit, where=lambda u: u.Owner.party == party and not u.Ready.acted)
        choose(dir, from_=open_dirs(unit))
    unit.Pos.x += DIRS[dir][0]
    unit.Pos.y += DIRS[dir][1]
    unit.Ready.acted = True

@command(acts_for=lambda party: party)
def Attack(party: Ref[Party], unit: Ref[Unit], target: Ref[Unit]):
    with preconditions:
        choose(unit, where=lambda u: u.Owner.party == party and not u.Ready.acted)
        choose(target, where=lambda t: t.Owner.party != party and dist(unit, t) <= unit.Unit.reach)
    unit.Ready.acted = True
    resolve(unit, target)

@command(acts_for=lambda party: party)
def EndTurn(party: Ref[Party]):
    pass
