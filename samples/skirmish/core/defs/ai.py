# 진영 봇 (spec-gaps G367~G379): 호스트가 봇 자리의 진영에 붙이고 판이 바뀔 때마다 한 번씩 부른다.
# 한 번에 한 수만 적는다: 때릴 수 있으면 때리고, 아니면 가장 가까운 적에게 한 칸, 할 것이 없으면 차례를 넘긴다.
# 차례가 아닐 때 적은 수는 엔진이 거절한다. 맞아서 버틸지 물러날지 물으면 버틴다.

def nearest_enemy(u, me):
    best = None
    for t in query(Unit):
        if t.Owner.party != me and (best is None or dist(u, t) < dist(u, best)):
            best = t
    return best

def step_toward(u, t):
    for d in open_dirs(u):
        x = u.Pos.x + DIRS[d][0]
        y = u.Pos.y + DIRS[d][1]
        if abs(x - t.Pos.x) + abs(y - t.Pos.y) < dist(u, t):
            return d
    return None

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
