# 맞은 칸이 잠깐 붉게 번쩍인다 (G398·G425): 얼마 동안 어떻게는 looks/fx.look.xml
@world_fx(Hit)
def hit_flash(x, y):
    return [fx("hit-flash", at=at("square", [x, y]))]
