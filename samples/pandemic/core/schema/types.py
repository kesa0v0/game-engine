# 협력 보드게임 Pandemic 따라 해 보기. 1조각: 지도와 말(도시는 그래프, 말은 턴마다 4행동).
# 2조각: 감염. 도시에 질병별 큐브가 쌓이고, 턴이 끝나면 감염 카드를 뽑아 큐브를 올리고, 넘치면 연쇄 폭발한다.
# 도시는 Def(지도 데이터)이면서 게임 시작에 엔티티로도 깔린다. 엔티티는 c.defid로 자기 Def를 안다.
RATES = [2, 2, 2, 3, 3, 4, 4]
OUTBREAK_LIMIT = 8
HAND_LIMIT = 7
CUBE_SUPPLY = 16   # 질병마다 큐브 수. 실제 판은 24개인데 도시가 12개라 줄였다
CURE_CARDS = 5     # 치료제에 필요한 같은 색 도시 카드 수
EPIDEMICS = 2      # 플레이어 덱에 섞는 에픽 카드 수

# 감염 속도: 에픽 카드를 뽑은 수에 따라 2,2,2,3,3,4,4
def infection_rate(m):
    return RATES[min(m.epidemics, 6)]

# 이 질병의 큐브가 지금 보드(모든 도시)에 몇 개 있나. 도시들을 건너 읽는 derived다
def board_cubes(s):
    return sum([c.Cubes[s.entity.defid].count for c in query("City")])

def cube_supply(s):
    return CUBE_SUPPLY - s.on_board

# 폭발이 한도에 닿거나, 덱이 바닥나거나, 어느 질병이든 큐브가 모자라면 진다
def is_lost(m):
    return m.outbreaks >= OUTBREAK_LIMIT or m.deck_out or any(d.Status.supply < 0 for d in query("Disease"))

# 모든 질병의 치료제를 찾으면 이긴다
def is_won(m):
    return all(d.Status.cured for d in query("Disease"))

def is_over(m):
    return m.lost or m.won

# 치료제에 필요한 같은 색 카드 수: 기본 CURE_CARDS에 기여("add")를 더한다. 과학자 역할이 -1을 선언한다 (defs/cities.py)
def cure_cards(c):
    return CURE_CARDS + floor(sum([x.value for x in contribs(c, "cards") if x.op == "add"]))

def hand_size(h):
    return len(h.entity.hand)

def hand_over(h):
    return len(h.entity.hand) > HAND_LIMIT

def cube_total(l):
    return sum([l.entity.Cubes[d].count for d in l.entity.Cubes])

# 어느 질병이든 3개가 차 있으면 다음 감염에서 폭발한다
def is_danger(l):
    return any(l.entity.Cubes[d].count >= 3 for d in l.entity.Cubes)

component("At", city=ref("City", optional=True, inverse="here"))
component("Actions", left=field(int, default=4))
component("Order", next=ref("Player", optional=True))
component("Match", turn=field(int, default=1), outbreaks=field(int, default=0), epidemics=field(int, default=0), deck_out=field(bool, default=False),
    rate=derived(int, infection_rate), lost=derived(bool, is_lost), won=derived(bool, is_won), over=derived(bool, is_over))
# 질병의 상태: 치료제를 찾았나, 박멸했나(치료제가 있고 보드에 큐브가 없음), 보드 위 큐브와 남은 큐브
component("Status", cured=field(bool, default=False), eradicated=field(bool, default=False),
    on_board=derived(int, board_cubes), supply=derived(int, cube_supply))
component("Research", station=field(bool, default=False))
# 질병마다 한 줄(키 컴포넌트). 큐브는 한 도시에 같은 질병이 3개까지다
component("Cubes", key="Disease", count=field(int, default=0, min=0, max=3))
# 폭발은 한 연쇄에서 도시마다 한 번이다: 이번 연쇄에서 이미 터졌는지
component("Chain", broke=field(bool, default=False))
component("Load", total=derived(int, cube_total), danger=derived(bool, is_danger))
# 덱의 카드: 뽑을 더미(0)인지 버린 더미(1)인지(플레이어 덱은 손패 2도), 뽑을 더미 안의 자리(작을수록 위)
component("Card", pile=field(int, default=0), slot=field(int, default=0))
# 카드가 어느 도시인지: 뽑을 더미에 있는 동안은 아무도 못 본다 (보기 정책, defs/rules.py)
component("Face", city=ref("City", optional=True), epidemic=field(bool, default=False))
# 손패: 카드가 누구 손에 있는지. 플레이어 쪽에서는 p.hand로 읽는다
component("Held", holder=ref("Player", optional=True, inverse="hand"))
component("Hand", size=derived(int, hand_size), over=derived(bool, hand_over))
component("Cure", cards=derived(int, cure_cards))
# 플레이어의 역할: 역할 Def마다 한 줄. 역할의 보너스는 역할 Def에 적고 엔진이 모은다(선언 보너스, 안내서 3장)
component("Roles", key="Role", keys="listed")
signal("Outbreak", city=Ref["City"])

deftype("Disease", components=["Status"])
deftype("City", components=["Research", "Cubes", "Chain", "Load"], disease=const(DefId["Disease"]), neighbors=const(list[DefId["City"]]))
deftype("Role", grants=const(list[Contrib], default=[]))
deftype("Player", components=["Order", "At", "Actions", "Hand", "Cure", "Roles", "Seatable"])
deftype("Game", components=["Match"])
deftype("InfectionCard", components=["Card", "Face"])
# 플레이어 덱의 카드는 모두 같은 뒷면 Def이고 도시 카드인지 에픽인지는 숨긴 Face에 있다
deftype("PlayerCard", components=["Card", "Face", "Held"])
