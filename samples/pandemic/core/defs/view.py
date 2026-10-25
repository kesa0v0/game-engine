# 콘솔 화면에 보일 값 (ui/console.txt가 부른다). 인자는 그 자리가 앉은 플레이어.
def status(me):
    g = game()
    if g is None:
        return "준비 중"
    return {"turn": g.Match.turn, "you": me, "left": me.Actions.left, "outbreaks": g.Match.outbreaks,
            "rate": g.Match.rate, "lost": g.Match.lost, "won": g.Match.won}

def players(me):
    return [{"player": p, "at": p.At.city.defid, "left": p.Actions.left} for p in query(Player)]

def map_rows(me):
    return [{"city": c.defid, "disease": c.City.disease, "cubes": c.Load.total, "danger": c.Load.danger,
             "here": [p for p in c.here], "next": c.City.neighbors}
            for c in query(City)]

# 감염 덱: 뽑을 더미는 장수와 이 자리가 아는 위 카드(확장의 역할이 몰래 본 것, 위부터), 버린 더미는 카드의 도시.
# 뒷면은 Face가 붙지 않은 것으로 읽힌다
def infection_deck(me):
    pile = [c for c in query(InfectionCard) if c.Card.pile == 0]
    known = []
    for c in by_slot(pile):
        if c.Face is None:
            break
        known = known + [c.Face.city.defid]
    return {"draw": len(pile), "known": known,
            "discard": [c.Face.city.defid for c in query(InfectionCard) if c.Card.pile == 1]}

# 카드들을 자리 순(위부터)으로
def by_slot(cards):
    rest = cards
    out = []
    while len(rest) > 0:
        top = rest[0]
        for c in rest:
            if c.Card.slot < top.Card.slot:
                top = c
        out = out + [top]
        rest = [c for c in rest if c != top]
    return out

# 손패는 모두에게 공개다. 플레이어 덱은 장수만 보인다
def hands(me):
    return [{"player": p, "cards": [c.Face.city.defid for c in p.hand], "size": p.Hand.size}
            for p in query(Player)]

def player_deck(me):
    return {"draw": len([c for c in query(PlayerCard) if c.Card.pile == 0])}

# 질병의 상태: 치료제, 박멸, 보드 위 큐브, 남은 큐브
def diseases(me):
    return [{"disease": d.defid, "cured": d.Status.cured, "eradicated": d.Status.eradicated,
             "on_board": d.Status.on_board, "supply": d.Status.supply}
            for d in query(Disease)]
