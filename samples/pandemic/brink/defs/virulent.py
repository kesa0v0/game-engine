# 규칙서의 변종 에픽 카드 여덟 장 중 짠 것. 지속 효과는 그 카드의 에픽을 마친 뒤부터, 즉시 효과는 그 카드의 Intensify 단계 전에 한 번
StrainEffect("ChronicEffect")              # 지속
StrainEffect("ComplexMolecularStructure")  # 지속
StrainEffect("SlipperySlope")              # 지속
StrainEffect("RateEffect")                 # 지속
StrainEffect("GovernmentInterference")     # 지속
StrainEffect("UncountedPopulations")       # 즉시
StrainEffect("HiddenPocket")               # 즉시

def strain():
    for d in query(Disease):
        if d.Virulent.on:
            return d
    return None

# 이 지속 효과가 켜졌나: 그 효과가 붙은 에픽 카드를 뽑아 그 에픽을 마쳤으면 켜진다
def in_effect(effect):
    return any(c.StrainCard.done and c.StrainCard.effect == effect for c in query(PlayerCard, StrainCard))

# 이 질병이 변종이고 이 지속 효과가 켜졌나. 효과는 변종에만 닿는다
def strain_has(disease, effect):
    return pandemic.disease_of(disease).Virulent.on and in_effect(effect)

# ---- 덱 ----

# 덱을 만들 때 에픽 카드마다 효과 하나를 무작위로 붙인다 (규칙서: 변종 에픽 카드를 뒤집어 섞고 필요한 수만 쓴다).
# 코어가 에픽 카드를 만든 뒤에 붙이도록 코어의 덱 만들기를 감싼다 (G311)
@wrap(pandemic.seed_epidemics)
def strain_deck(original):
    original()
    cards = [c for c in query(PlayerCard) if c.Face.epidemic]
    effects = random.sample(defs(StrainEffect), len(cards))
    i = 0
    for c in cards:
        attach(c, StrainCard(effect=effects[i]))
        i += 1

# 덮인 카드의 효과는 아무도 못 본다. 코어가 앞면(Face)을 숨기는 것과 같은 때다
@view_rule(StrainCard)
def strain_down(viewer, entity):
    return entity.Card.pile != 0

# ---- 에픽 ----

# 첫 에픽의 Infect 단계를 마친 그 자리에서(Intensify 전) 보드에 큐브가 가장 많은 질병이 변종이 된다. 같으면 무작위.
# 반응(@when)은 흐름이 멈춘 뒤에 돌아 같은 차례의 감염 단계보다 늦으므로, 코어의 단계 함수를 감싼다 (G311)
@wrap(pandemic.epidemic_infect)
def choose_strain(original, g):
    original(g)
    if strain() is None:
        most = 0
        for d in query(Disease):
            if d.Status.on_board > most:
                most = d.Status.on_board
        random.choice([d for d in query(Disease) if d.Status.on_board == most]).Virulent.on = True

# 지금 치르는 에픽 카드: 뽑은 에픽 카드는 버린 더미로 가므로, 버린 더미에서 아직 마치지 않은 것이다
def drawn_strain_card():
    for c in query(PlayerCard, StrainCard):
        if c.Card.pile == 1 and not c.StrainCard.done:
            return c
    return None

# 즉시 효과는 이 카드의 Intensify 단계 전에 치르고, 지속 효과는 에픽을 마친 뒤부터 켜진다 (규칙서).
# 코어의 Intensify 단계를 감싸면 그 앞과 뒤가 곧 그 두 자리다
@wrap(pandemic.intensify)
def strain_effect(original):
    card = drawn_strain_card()
    effect = None if card is None else card.StrainCard.effect
    if effect == defid("UncountedPopulations"):
        uncounted_populations()
    elif effect == defid("HiddenPocket"):
        hidden_pocket()
    original()
    if card is not None:
        card.StrainCard.done = True

# Uncounted Populations: 변종 큐브가 꼭 하나인 도시마다 변종 큐브를 하나 놓는다(다른 색은 보지 않는다).
# 하나에서 둘이 되므로 넘치지 않는다. 박멸·역할의 막기는 add_cube가 본다
def uncounted_populations():
    s = strain()
    if s is None:
        return
    for city in query(City):
        if city.Cubes[s.defid].count == 1:
            pandemic.add_cube(city, s.defid)

# Hidden Pocket: 변종이 박멸됐고 감염 버린 더미에 변종 도시 카드가 하나라도 있으면, 박멸을 되돌리고(치료제는 그대로)
# 그 카드들의 도시마다 변종 큐브를 하나 놓는다. 박멸된 질병은 보드에 큐브가 없어 하나씩 놓아도 넘치지 않는다
def hidden_pocket():
    s = strain()
    if s is None or not s.Status.eradicated:
        return
    cities = [c.Face.city for c in query(InfectionCard) if c.Card.pile == 1 and c.Face.city.City.disease == s.defid]
    if len(cities) == 0:
        return
    s.Status.eradicated = False
    for city in cities:
        pandemic.add_cube(city, s.defid)

# ---- 지속 효과 ----

# Complex Molecular Structure: 변종의 치료제는 같은 색 카드가 한 장 더 든다. 코어의 계산을 감싼다 (G311)
@wrap(pandemic.cure_cost)
def complex_cure(original, player, disease):
    return original(player, disease) + (1 if strain_has(disease, defid("ComplexMolecularStructure")) else 0)

# Chronic Effect: 감염 카드로 변종 큐브가 없는 도시를 감염시키면 큐브를 두 개 놓는다. 에픽과 폭발에는 닿지 않는다(규칙서 카드 문구).
# 그래서 큐브 하나 올리기(add_cube, 폭발도 씀)가 아니라 카드 한 장의 감염(infect_city)을 감싼다 (G311).
# 0개에서 먼저 올리는 한 개는 넘칠 수 없다
@wrap(pandemic.infect_city)
def chronic(original, city):
    disease = city.City.disease
    if strain_has(disease, defid("ChronicEffect")) and city.Cubes[disease].count == 0:
        pandemic.add_cube(city, disease)
    original(city)

# Slippery Slope: 변종의 폭발은 폭발 표시를 두 칸 옮긴다. 코어가 한 칸을 세므로 한 칸을 더한다.
# 즉시 핸들러라 코어의 감염 순서 한가운데에서 돌아, 코어가 다음 카드를 뽑기 전에 패배를 본다 (G310)
@on(Outbreak, now=True)
def slippery(city):
    if strain_has(city.City.disease, defid("SlipperySlope")):
        pandemic.game().Match.outbreaks += 1

# Rate Effect: 감염 단계에서 뽑은 카드 중 변종 도시가 하나라도 있으면(변종이 박멸 전이면) 감염 속도보다 한 장 더 뽑는다.
# 덧뽑은 장은 한 장뿐이다. 뽑은 카드는 코어의 감염 단계 안에만 있으므로, 카드 한 장의 감염을 감싸 변종 도시를 봤다고 적어 둔다
@wrap(pandemic.infect_city)
def rate_watch(original, city):
    if pandemic.disease_of(city.City.disease).Virulent.on:
        pandemic.game().RateWatch.seen = True
    original(city)

# 감염 단계를 마친 뒤 한 장 더. 그 장도 감염이라 infect_city를 거친다(Chronic Effect 등이 닿는다)
@wrap(pandemic.infect)
def rate_effect(original, g):
    g.RateWatch.seen = False
    original(g)
    s = strain()
    if g.RateWatch.seen and not g.Match.lost and not s.Status.eradicated and strain_has(s.defid, defid("RateEffect")):
        for c in query(City):
            c.Chain.broke = False
        card = pandemic.draw_infection()
        pandemic.infect_city(card.Face.city)

# Government Interference: 변종 큐브가 있는 도시를 떠나려면 그 차례에 먼저 그 도시의 변종 큐브를 하나 이상 Treat해야 한다.
# 코어의 커맨드를 감싼다 (G316): 떠나는 커맨드에는 조건을 더하고(합법 수에도 보인다), Treat는 마친 뒤에 적어 둔다
def may_leave(player):
    s = strain()
    city = player.At.city
    if s is None or city is None or city.Cubes[s.defid].count == 0 or not in_effect(defid("GovernmentInterference")):
        return True
    return player.Interference.city == city and player.Interference.turn == pandemic.game().Match.turn

@wrap(pandemic.Move)
def interfere_move(original, player, to):
    with preconditions:
        require(may_leave(player), text("brink-treat-first"))
    original(player, to)

@wrap(pandemic.DirectFlight)
def interfere_flight(original, player, card):
    with preconditions:
        require(may_leave(player), text("brink-treat-first"))
    original(player, card)

@wrap(pandemic.Treat)
def note_treat(original, player, disease):
    city = player.At.city
    original(player, disease)
    if pandemic.disease_of(disease).Virulent.on:
        player.Interference.city = city
        player.Interference.turn = pandemic.game().Match.turn
