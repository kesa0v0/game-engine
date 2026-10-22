def strain():
    for d in query(Disease):
        if d.Virulent.on:
            return d
    return None

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

# Complex Molecular Structure: 변종의 치료제는 같은 색 카드가 한 장 더 든다. 코어의 계산을 감싼다 (G311)
@wrap(pandemic.cure_cost)
def complex_cure(original, player, disease):
    return original(player, disease) + (1 if pandemic.disease_of(disease).Virulent.on else 0)

# Chronic Effect: 감염 카드로 변종 큐브가 없는 도시를 감염시키면 큐브를 두 개 놓는다. 에픽과 폭발에는 닿지 않는다(규칙서 카드 문구).
# 그래서 큐브 하나 올리기(add_cube, 폭발도 씀)가 아니라 카드 한 장의 감염(infect_city)을 감싼다 (G311).
# 0개에서 먼저 올리는 한 개는 넘칠 수 없다
@wrap(pandemic.infect_city)
def chronic(original, city):
    disease = city.City.disease
    if pandemic.disease_of(disease).Virulent.on and city.Cubes[disease].count == 0:
        pandemic.add_cube(city, disease)
    original(city)

# Slippery Slope: 변종의 폭발은 폭발 표시를 두 칸 옮긴다. 코어가 한 칸을 세므로 한 칸을 더한다.
# 즉시 핸들러라 코어의 감염 순서 한가운데에서 돌아, 코어가 다음 카드를 뽑기 전에 패배를 본다 (G310)
@on(Outbreak, now=True)
def slippery(city):
    if pandemic.disease_of(city.City.disease).Virulent.on:
        pandemic.game().Match.outbreaks += 1
