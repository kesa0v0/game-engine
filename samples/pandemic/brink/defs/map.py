# 확장 모드: 코어 파일을 건드리지 않고 지도를 넓힌다. 새 도시는 Def를 더하고, 기존 도시의 이웃은 패치로 잇는다(이웃은 양쪽에 적는다).
City("BuenosAires", disease="pandemic:Yellow", neighbors=["pandemic:Bogota", "pandemic:SaoPaulo"])
City("Santiago", disease="pandemic:Yellow", neighbors=["pandemic:Lima"])
patch.append("pandemic:Bogota", "neighbors", ["BuenosAires"])
patch.append("pandemic:SaoPaulo", "neighbors", ["BuenosAires"])
patch.append("pandemic:Lima", "neighbors", ["Santiago"])
