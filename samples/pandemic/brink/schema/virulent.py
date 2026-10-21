# 변종 바이러스 (On the Brink의 Virulent Strain을 일반 지식으로 줄여 씀): 첫 에픽 때 보드에 큐브가 가장 많은 질병이 변종이 된다
component("Virulent", on=field(bool, default=False))
extend("Disease", components=["Virulent"])
