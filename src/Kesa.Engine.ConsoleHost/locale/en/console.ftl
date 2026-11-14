# 콘솔 호스트의 글 (spec-gaps G269·G273·G274). 모드가 같은 키로 덮어쓸 수 있다.
engine-console-no-moves = Nothing left to do. The end.
engine-console-pick-number = Enter a number from 1 to { $n }, a command like Move(unit=#3, to=[1, 2]), or :q to quit
engine-console-tick = == tick { $tick } ==
engine-console-question = question { $id }: { $title } → { $who }
engine-console-seated = sat at { $entity }
engine-console-seat-choices = Seats:
engine-console-where = Where does { $connection } sit? >
engine-console-gone = (gone)
engine-console-cannot-list = (cannot list the moves of { $command }: { $why })
engine-console-needs = (cannot list the moves of { $command }: choose { $parameters } first)
engine-console-pick = question { $id } { $by }: { $choice }
engine-console-ok = ok
engine-console-screen = [screen]
engine-console-truncated = (only the first { $n } moves of { $command } are listed: use --max-moves, or type the command)
engine-console-question-name = question { $id }
engine-console-typed-bad = cannot read the command: { $why }
engine-console-typed-seat = put the connection first, like { $seats }: Move(...)
engine-console-typed-unknown = { $command } is not a free command, an answer to an open question or a screen action
engine-console-typed-many = { $command } answers more than one open question ({ $ids }): pick it by number
