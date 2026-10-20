# 작은 전투 (콘솔 호스트 예시): 5x5 판, 두 진영, 진영마다 병사 둘과 궁수 하나.
# 어느 연결이 어느 진영을 맡는지는 이 모드가 아니라 엔진의 배정 표다 (호스트가 World.Seat로 앉힌다). 여기는 차례 순서만.
# 진영이 로비에 보일 앉을 후보라는 표시는 엔진 컴포넌트 Seatable이다 (World.SeatChoices).
component("Order", next=ref("Party", optional=True))
component("Pos", x=field(int, default=0), y=field(int, default=0))
component("Life", hp=field(int, default=1))
component("Owner", party=ref("Party", optional=True))
component("Ready", acted=field(bool, default=False))
component("Match", turn=field(int, default=1), winner=ref("Party", optional=True))
deftype("Party", components=["Order", "Seatable"])
deftype("Unit", components=["Pos", "Life", "Owner", "Ready"],
    max_hp=const(int), attack=const(int), reach=const(int))
deftype("Game", components=["Match"])
# 화면에만 있는 기록: 연결마다 하나, 저장되지 않는다 (spec-gaps G277·G279). 고른 유닛
ui_state("Selection", unit=ref("Unit", optional=True))
