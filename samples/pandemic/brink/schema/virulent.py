# 변종 바이러스 (On the Brink의 Virulent Strain): 첫 에픽 때 보드에 큐브가 가장 많은 질병이 변종이 된다
component("Virulent", on=field(bool, default=False))
extend("Disease", components=["Virulent"])

# 변종 에픽 카드의 효과. 규칙서의 카드마다 Def 하나 (defs/virulent.py)
deftype("StrainEffect")
# 에픽 카드에 붙은 효과: 덱을 만들 때 붙이고 덮인 동안은 숨긴다. done은 이 카드의 에픽을 마쳤나(지속 효과는 그때부터 켜진다)
component("StrainCard", effect=field(DefId["StrainEffect"]), done=field(bool, default=False))

# Rate Effect가 보는 것: 이번 감염 단계에서 변종 도시 카드를 뽑았나. 뽑은 카드는 코어의 감염 단계 안에만 있어서 여기에 적어 둔다
component("RateWatch", seen=field(bool, default=False))
extend("Game", components=["RateWatch"])
