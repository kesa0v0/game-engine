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

# UXML 화면(ui/screen.uxml)이 부른다: 이 유닛이 고른 유닛인가
def is_chosen(u):
    return ui.Selection.unit == u

# 유닛 정보창 (UXML의 보통 창, G348~G355): 연 유닛을 UI 기록에 두고 창은 그 값에 보임을 묶는다.
# 사전 조건은 뜻이 있을 때만 합법이게 한다: 판이 끝나면 화면 동작도 남지 않는다
def playing():
    g = game()
    return g is not None and g.Match.winner is None

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

def inspected_hp():
    u = ui.Inspect.unit
    return 0 if u is None else u.Life.hp

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
