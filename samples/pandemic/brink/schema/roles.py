# Troubleshooter가 몰래 본 감염 카드: 본 플레이어마다 줄 하나(엔티티 키). 카드는 그대로 덮여 있고 아는 사람만 상태에 적는다 (G322)
component("Known", key=Ref["Player"])
extend("InfectionCard", components=["Known"])
