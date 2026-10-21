# 콘솔 화면에 보일 값 (ui/console.txt가 부른다). 인자는 그 자리가 앉은 플레이어.
def status(me):
    g = game()
    if g is None:
        return "준비 중"
    return {"turn": g.Match.turn, "you": me, "left": me.Actions.left}

def players(me):
    return [{"player": p, "at": p.At.city.defid, "left": p.Actions.left} for p in query("Player")]

def map_rows(me):
    return [{"city": c.defid, "color": c.City.color, "here": [p for p in c.here], "next": c.City.neighbors}
            for c in query("City")]
