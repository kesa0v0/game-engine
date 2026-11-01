# 화면 동작 (spec-gaps G276~G286): 내 유닛을 골라 두고, 고른 유닛으로 공격한다. 게임은 바꾸지 않고 Attack을 낼 뿐이다.
# me에는 콘솔이 그 자리가 앉은 진영을 넣는다 (G286).

# 고른 유닛이 아직 움직일 수 있으면 그 유닛
def chosen():
    u = ui.Selection.unit
    return None if u is None or u.Ready.acted else u

def in_reach(t, me):
    u = chosen()
    return u is not None and t.Owner.party != me and dist(u, t) <= u.Unit.reach

@ui_action
def select(me: Ref[Party], unit: Ref[Unit]):
    with preconditions:
        choose(unit, where=lambda u: u.Owner.party == me and not u.Ready.acted and u != ui.Selection.unit)
    ui.Selection.unit = unit

@ui_action
def strike(me: Ref[Party], target: Ref[Unit]):
    with preconditions:
        require(chosen() is not None, text("skirmish-select-first"))
        choose(target, where=lambda t: in_reach(t, me))
    # Attack은 차례 질문의 답이다: 엔진이 이 연결이 답할 질문을 찾아 낸다 (G283)
    submit("Attack", party=me, unit=chosen(), target=target)
    ui.Selection.unit = None

# 판 위 유닛을 누름 (G416·G421): 내 유닛이면 고르고, 고른 유닛의 사거리 안 적이면 공격한다. 내 진영은 seats()로 안다
def is_mine(u):
    return u.Owner.party in seats()

def can_pick(u):
    return is_mine(u) and not u.Ready.acted and u != ui.Selection.unit

def can_strike(t):
    u = chosen()
    return u is not None and is_mine(u) and not is_mine(t) and dist(u, t) <= u.Unit.reach

@world_input("primary", "Unit")
@ui_action
def press_unit(unit: Ref[Unit]):
    with preconditions:
        require(playing(), text("skirmish-over"))
        choose(unit, where=lambda u: can_pick(u) or can_strike(u))
    if can_pick(unit):
        ui.Selection.unit = unit
    else:
        u = chosen()
        submit("Attack", party=u.Owner.party, unit=u, target=unit)
        ui.Selection.unit = None

# 판의 빈 칸을 누름 (G422): 고른 유닛을 바로 옆 빈 칸으로 옮긴다. 칸은 at("square", [x, y])의 [x, y]
def step_cells():
    u = chosen()
    if u is None or not is_mine(u):
        return []
    return [[u.Pos.x + DIRS[d][0], u.Pos.y + DIRS[d][1]] for d in open_dirs(u)]

@world_input("primary", "square")
@ui_action
def press_square(cell: list[int]):
    with preconditions:
        require(playing(), text("skirmish-over"))
        choose(cell, from_=step_cells())
    u = chosen()
    for d in open_dirs(u):
        if [u.Pos.x + DIRS[d][0], u.Pos.y + DIRS[d][1]] == cell:
            submit("Move", party=u.Owner.party, unit=u, dir=d)
    ui.Selection.unit = None

# UXML 화면(ui/screen.uxml)이 부른다: 이 유닛이 고른 유닛인가
def is_chosen(u):
    return ui.Selection.unit == u

# 유닛 정보창 (UXML의 보통 창, G348~G355): 연 유닛을 UI 기록에 두고 창은 그 값에 보임을 묶는다.
# 사전 조건은 뜻이 있을 때만 합법이게 한다: 판이 끝나면 화면 동작도 남지 않는다
def playing():
    g = game()
    return g is not None and g.Match.winner is None

# 판 위 유닛을 오른쪽 누름이나 I 키로도 연다 (G416, 키는 keys.input.xml)
@world_input("secondary", "Unit")
@world_input("inspect", "Unit")
@ui_action
def inspect(unit: Ref[Unit]):
    with preconditions:
        require(playing(), text("skirmish-over"))
        choose(unit, where=lambda u: u != ui.Inspect.unit)
    ui.Inspect.unit = unit

@ui_action
def close_inspect():
    with preconditions:
        require(ui.Inspect.unit is not None, text("skirmish-not-open"))
    ui.Inspect.unit = None

def inspected():
    return ui.Inspect.unit

# 턴 끝내기 확인 (UXML의 모달): 물은 턴을 적어 두므로 턴이 넘어가면 저절로 닫힌다
def turn_now():
    g = game()
    return 0 if g is None else g.Match.turn

@ui_action
def ask_end_turn():
    with preconditions:
        require(playing(), text("skirmish-over"))
        require(not asking_end_turn(), text("skirmish-already-open"))
    ui.Confirm.turn = turn_now()

@ui_action
def cancel_end_turn():
    with preconditions:
        require(asking_end_turn(), text("skirmish-not-open"))
    ui.Confirm.turn = 0

def asking_end_turn():
    return ui.Confirm.turn != 0 and ui.Confirm.turn == turn_now()

# 맞은 유닛의 질문 (rules.py의 resolve가 묻는다, G381·G383). 이 연결이 답할 수 있는 것만 보인다
def hit_question(u):
    for q in questions("skirmish-hit"):
        if q.data["unit"] == u:
            return q
    return None

# UXML 화면이 카드마다 부른다: 이 유닛이 버틸지 물러날지 답을 기다리는가
def is_hit(u):
    return hit_question(u) is not None

@ui_action
def hold(unit: Ref[Unit]):
    with preconditions:
        choose(unit, where=lambda u: is_hit(u))
    respond(hit_question(unit), "engine.pick", choice=0)

@ui_action
def retreat(unit: Ref[Unit]):
    with preconditions:
        choose(unit, where=lambda u: is_hit(u))
    respond(hit_question(unit), "engine.pick", choice=1)

# 판 위 유닛 (G389·G394): 그림의 뜻은 looks/units.look.xml. 고른 유닛에는 고리, 이번 차례에 움직인 유닛은 흐리게
def unit_look(u):
    art = "art/soldier.png" if u.Unit.reach == 1 else "art/archer.png"
    return look("unit", art=art, color=u.Owner.party.Party.color, picked=ui.Selection.unit == u, acted=u.Ready.acted)

def unit_where(u):
    return at("square", [u.Pos.x, u.Pos.y])

# 판 위 표시 (G395): 맞은 유닛이 물러날 칸. 누르면 retreat로 답한다
@world_marks
def retreat_spots():
    return [mark("retreat-spot", at=at("square", q.data["to"]), click=["skirmish.retreat", {"unit": q.data["unit"]}])
            for q in questions("skirmish-hit")]
