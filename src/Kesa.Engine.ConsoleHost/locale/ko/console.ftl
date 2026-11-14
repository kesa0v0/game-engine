# 콘솔 호스트의 글 (spec-gaps G269·G273·G274). 모드가 같은 키로 덮어쓸 수 있다.
engine-console-no-moves = 낼 수 있는 것이 없다. 끝.
engine-console-pick-number = 1부터 { $n } 사이의 번호, Move(unit=#3, to=[1, 2]) 같은 커맨드, 끝내려면 :q
engine-console-tick = == 틱 { $tick } ==
engine-console-question = 질문 { $id }: { $title } → { $who }
engine-console-seated = { $entity }에 앉음
engine-console-seat-choices = 앉을 자리:
engine-console-where = { $connection }는 어디에? >
engine-console-gone = (없음)
engine-console-cannot-list = ({ $command }의 수를 나열할 수 없다: { $why })
engine-console-needs = ({ $command }의 수를 나열할 수 없다: { $parameters }를 정해야 한다)
engine-console-pick = 질문 { $id } { $by }: { $choice }
engine-console-ok = ok
engine-console-screen = [화면]
engine-console-truncated = ({ $command }의 수는 앞의 { $n }개만 보인다: --max-moves로 늘리거나 커맨드를 적어 낸다)
engine-console-question-name = 질문 { $id }
engine-console-typed-bad = 커맨드를 읽을 수 없다: { $why }
engine-console-typed-seat = 앞에 연결을 적는다. 연결은 { $seats } 중 하나: red: Move(...)
engine-console-typed-unknown = { $command }는 자유 커맨드도, 열린 질문의 답도, 화면 동작도 아니다
engine-console-typed-many = { $command }는 열린 질문 여럿({ $ids })의 답이다: 번호로 고른다
