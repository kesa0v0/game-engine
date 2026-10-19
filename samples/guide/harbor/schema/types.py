# 모더 안내서(docs/modding)의 예시 모드. 작은 항구: 상회(House)가 배를 사고, 화물을 쌓고, 남의 배를 습격한다.
# 안내서의 예시 코드는 이 파일들에서 가져오고, 테스트(GuideSampleTests)가 이 모드를 불러 한 판을 돌린다.

# 배의 최대 내구도: 배 종류의 기본값 + 기여 행("add")의 합
def hull_max(h):
    bonus = sum([c.value for c in contribs(h, "max_hp") if c.op == "add"])
    return h.entity.Ship.base_hp + floor(bonus)

component("Purse", gold=field(int, default=0, min=0))
component("Stock", key="Good", amount=field(int, default=0, min=0))
component("Hull", hp=field(int, default=1, min=0), max_hp=derived(int, hull_max))
component("Owner", house=ref("House", optional=True, inverse="ships"))
component("Fleet", strength=derived(int, lambda f: sum([s.Hull.hp for s in f.entity.ships])))
component("Secret", plan=field(int, default=0))
component("Port", day=field(int, default=0))

deftype("Good", price=const(int, default=1), label=const(TextKey, default=""))
deftype("House", components=["Purse", "Stock", "Fleet", "Secret", "Seatable"])
deftype("Ship", components=["Hull", "Owner"],
    base_hp=const(int), speed=const(int, default=1), tags=const(list[str], merge="append", default=[]))
deftype("Harbor", components=["Port"])

signal("Sunk", ship=Ref["Ship"], house=Ref["House"])
stage("harbor.market")
