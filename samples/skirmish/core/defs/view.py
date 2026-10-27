# 콘솔 화면에 보일 값 (ui/console.txt가 부른다, spec-gaps G250). 자리의 진영 me를 받아 그 진영이 보는 세계에서 돈다 (G248).
# 판은 줄마다 칸 다섯: 빈칸 ".", 내 병사·궁수 "S"·"A", 상대는 "s"·"a".

def glyph(u, me):
    soldier = u.Unit.reach == 1 # 병사는 사거리 1, 궁수는 2
    if u.Owner.party == me:
        return "S" if soldier else "A"
    return "s" if soldier else "a"

def board_row(y, me):
    row = ""
    for x in range(SIZE):
        u = unit_at(x, y)
        cell = "." if u is None else glyph(u, me)
        row = cell if x == 0 else row + " " + cell
    return row

def status(me):
    g = game()
    if g is None:
        return "준비 중"
    return {"turn": g.Match.turn, "you": me, "winner": g.Match.winner}

def board(me):
    return [board_row(y, me) for y in range(SIZE)]

# 이 자리가 골라 둔 유닛 (ui.py의 select)
def selection(me):
    return {"selected": ui.Selection.unit}

def units(me):
    return [{"unit": u, "x": u.Pos.x, "y": u.Pos.y, "hp": u.Life.hp, "acted": u.Ready.acted, "party": u.Owner.party}
            for u in query(Unit)]

# UXML 화면(ui/screen.uxml)이 부르는 값 함수들: 콘솔 화면이 쓰는 위의 함수들과 따로, 값 하나씩을 돌려준다.
def turn_number(me):
    g = game()
    return 0 if g is None else g.Match.turn

def mine(u, me):
    return u.Owner.party == me
