# 협력 보드게임 Pandemic 따라 해 보기, 1조각: 지도와 말. 도시는 그래프이고 말은 한 도시에 서서 턴마다 4행동을 쓴다.
# 도시는 Def(지도 데이터)이면서 게임 시작에 엔티티로도 깔린다: 뒤 조각에서 큐브가 도시 엔티티에 붙는다.
component("Site", city=field(DefId["City"]))
component("At", city=ref("City", optional=True, inverse="here"))
component("Actions", left=field(int, default=4))
component("Order", next=ref("Player", optional=True))
component("Match", turn=field(int, default=1))
deftype("City", components=["Site"], color=const(str), neighbors=const(list[DefId["City"]]))
deftype("Player", components=["Order", "At", "Actions", "Seatable"])
deftype("Game", components=["Match"])
