# 확장 역할. 제너럴리스트는 턴마다 행동을 하나 더 받는다: 코어가 행동 수를 derived(Actions.per_turn)로 두어 선언 보너스로 된다
Role("Generalist", grants=[contrib("Actions.per_turn", "add", 1)])
