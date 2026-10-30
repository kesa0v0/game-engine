# 맞은 칸이 잠깐 붉게 번쩍이고 피해가 떠오른다 (G398·G425·G427): 얼마 동안 어떻게는 looks/fx.look.xml
@world_fx(Hit)
def hit_flash(x, y, damage):
    place = at("square", [x, y])
    return [fx("hit-flash", at=place), fx(look("damage-number", amount=-damage), at=place)]
