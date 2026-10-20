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
