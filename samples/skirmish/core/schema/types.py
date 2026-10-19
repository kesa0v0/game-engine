# 작은 전투 (콘솔 호스트 예시): 5x5 판, 두 진영, 진영마다 병사 둘과 궁수 하나.
component("Seat", conn=field(str, default=""), next=ref("Party", optional=True))
component("Pos", x=field(int, default=0), y=field(int, default=0))
component("Life", hp=field(int, default=1))
component("Owner", party=ref("Party", optional=True))
component("Ready", acted=field(bool, default=False))
component("Match", turn=field(int, default=1), winner=ref("Party", optional=True))
component("Combat", attacker=ref("Unit", optional=True), target=ref("Unit", optional=True))
deftype("Party", components=["Seat"])
deftype("Unit", components=["Pos", "Life", "Owner", "Ready"],
    max_hp=const(int), attack=const(int), reach=const(int))
deftype("Game", components=["Match", "Combat"])
