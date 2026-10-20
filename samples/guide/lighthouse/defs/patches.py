# harbor의 Def를 고치는 모드. harbor를 dependencies에 적었으므로 그 Def가 보이고, harbor 뒤에 불린다
# 다른 모드의 Def는 "모드id:이름"으로 쓴다. ":" 없이 쓰면 이 모드(lighthouse)의 것이다
patch.add("harbor:Galleon", "speed", 1)
patch.set("harbor:Salt", "price", 4)
patch.append("harbor:Sloop", "tags", ["lit"])

# 새 Def도 더할 수 있다. 이 Def의 id는 "lighthouse:Cutter"다
Ship("Cutter", parent="harbor:BaseShip", speed=3)

# smuggler 모드가 있을 때만
if mod_loaded("smuggler"):
    patch.set("harbor:Salt", "price", 9)
