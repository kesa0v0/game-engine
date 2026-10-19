Good("Fish", price=2, label="harbor.good.fish")
Good("Salt", price=5, label="harbor.good.salt")

House("Gull")
House("Crane")

# 추상 Def는 만들 수 없고 물려주기만 한다
Ship("BaseShip", abstract=True, base_hp=3, tags=["wood"])
Ship("Sloop", parent="BaseShip", speed=2, tags=["fast"])        # tags는 ["wood", "fast"] (merge="append")
Ship("Galleon", parent="BaseShip", base_hp=inherit.base_hp + 4)  # base_hp는 7

Harbor("Town")
