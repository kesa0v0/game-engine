# 화면에 보일 값. ui/console.txt가 자리마다 부르고, 인자 me는 그 자리가 앉은 상회다
def purse(me):
    return {"gold": me.Purse.gold, "fleet": me.Fleet.strength, "day": town().Port.day}

def ships(me):
    return [{"ship": s, "hp": s.Hull.hp, "max": s.Hull.max_hp} for s in me.ships]

def plans(me):
    return [{"house": h, "plan": "?" if h.Secret is None else h.Secret.plan} for h in query("House")]
