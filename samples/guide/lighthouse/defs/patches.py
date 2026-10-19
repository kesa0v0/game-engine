# harbor의 Def를 고치는 모드. harbor를 dependencies에 적었으므로 그 Def가 보이고, harbor 뒤에 불린다
patch.add("Galleon", "speed", 1)
patch.set("Salt", "price", 4)
patch.append("Sloop", "tags", ["lit"])

# 새 Def도 더할 수 있다
Ship("Cutter", parent="BaseShip", speed=3)

# smuggler 모드가 있을 때만
if mod_loaded("smuggler"):
    patch.set("Salt", "price", 9)
