# 변종 바이러스 (On the Brink의 Virulent Strain): 첫 에픽 때 보드에 큐브가 가장 많은 질병이 변종이 된다
component("Virulent", on=field(bool, default=False))
# Unacceptable Loss로 공급에서 상자로 뺀 큐브 수. 코어의 남은 큐브 계산(cube_supply)을 감싸 뺀다 (defs/virulent.py)
component("Boxed", cubes=field(int, default=0))
extend("Disease", components=["Virulent", "Boxed"])

# 변종 에픽 카드의 효과. 규칙서의 카드마다 Def 하나 (defs/virulent.py)
deftype("StrainEffect")
# 에픽 카드에 붙은 효과: 덱을 만들 때 붙이고 덮인 동안은 숨긴다. done은 이 카드의 에픽을 마쳤나(지속 효과는 그때부터 켜진다)
component("StrainCard", effect=field(DefId["StrainEffect"]), done=field(bool, default=False))

# Rate Effect가 보는 것: 이번 감염 단계에서 변종 도시 카드를 뽑았나. 뽑은 카드는 코어의 감염 단계 안에만 있어서 여기에 적어 둔다
component("RateWatch", seen=field(bool, default=False))
extend("Game", components=["RateWatch"])

# Government Interference가 보는 것: 플레이어가 변종 큐브를 마지막으로 Treat한 도시와 그 차례 번호
component("Interference", city=ref("City", optional=True), turn=field(int, default=0))
extend("Player", components=["Interference"])
