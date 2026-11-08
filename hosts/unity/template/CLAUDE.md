# {{GAME}}

Kesa 엔진으로 만드는 게임이다. 게임 전체가 모드이고, 규칙·데이터·화면은 `Assets/StreamingAssets/mods/{{MOD_ID}}/`에 있다. C#(`Assets/Scripts/GameHost.cs`)은 모드를 불러와 판을 열고 화면을 갱신만 한다. 규칙을 C#에 넣지 않는다.

답은 한국어로 한다.

## 모드를 쓰기 전에

모드 파일은 파이썬 문법이지만 파이썬이 아니라 엔진의 제한된 언어다. 기억이나 파이썬 지식으로 쓰지 말고, 아래를 읽고 쓴다. 경로는 이 폴더 기준이다.

- 안내서 목차: `{{ENGINE}}/docs/modding/README.md`. 처음이면 이것과 `01-start.md`를 읽고, 작업에 맞는 장을 읽는다. 안내서의 `samples/…` 경로는 `{{ENGINE}}/samples/…`다.

| 하려는 일 | 장 |
|---|---|
| 스키마, Def, 패치 | `02-data.md` |
| 함수, `query`, 난수 | `03-logic.md` |
| 커맨드, `choose`, `cost` | `04-commands.md` |
| `ask`, 턴 흐름, `wait` | `05-flow.md` |
| 신호, `@on`, `@when` | `06-reactions.md` |
| 자리, 숨긴 정보 | `07-seats-and-hidden.md` |
| 화면, 번역, 판 위 그림 | `08-screen.md` |
| 봇 | `09-bots.md` |
| 저장 호환, 아직 없는 기능 | `10-updates-and-limits.md` |

- 내장 함수와 이름의 전체 목록은 안내서에 없다. `stubs`가 만드는 `__builtins__.pyi`가 레퍼런스다.
- 동작하는 예시: `{{ENGINE}}/samples/` (`skirmish`, `guide`, `pandemic`). 복사해 오지 말고 쓰는 법을 참고한다.
- 호스트 C#: `{{PACKAGE}}/README.md`. 자리·화면·판 위 그림·카메라·입력·봇을 붙인 예는 `{{ENGINE}}/hosts/unity/demo/Assets/Scripts/KesaDemo.cs`다.

## 고친 뒤 확인

```
{{CLI}} check Assets/StreamingAssets/mods --host host/game.host.json
{{CLI}} stubs Assets/StreamingAssets/mods --host host/game.host.json
```

- 모드를 고칠 때마다 `check`를 돌린다. 오류·경고가 남으면 끝난 것이 아니다.
- 스키마를 고치면 `stubs`를 다시 돌린다. 스텁은 손으로 고치지 않는다.
- 호스트 함수(`GameHost.NewHost`)를 바꾸면 선언 파일 `host/game.host.json`을 다시 만든다(메뉴 `Game/Write Host Declarations` 또는 `-executeMethod HostDeclarations.Write`). 손으로 고치지 않는다.
- Unity 확인: `Unity.exe -batchmode -nographics -projectPath . -executeMethod GameCheck.Run -logFile <로그>` 후 로그에서 `GAME CHECK: OK`를 찾는다. `-quit`은 붙이지 않는다. 에디터가 이 프로젝트를 열고 있으면(`Temp/UnityLockfile`) 바로 끝나므로 `Library` 없이 복사본에서 돌린다.

## 문서

- `docs/handoff.md`: 인계만. 지금 어디까지 왔고 다음에 무엇을 하는지. 세션을 시작할 때 읽고 끝낼 때 다시 쓴다. 쌓아 두지 않는다.
- `docs/design.md`: 게임 구조, 정한 값, 그렇게 정한 이유.
- `docs/measurements.md`: 잰 값과 조건.

## 엔진과의 경계

- 엔진(`{{PACKAGE}}`)은 읽기만 한다. 엔진에 없는 기능이나 버그를 만나면 고치지 말고 재현한 내용과 함께 사용자에게 알린다. 엔진 저장소가 있으면 개선 후보는 그쪽 `docs/engine-improvements.md`에 적어도 된다.
- 코드, 주석, 문서에 참고한 기존 게임을 적지 않는다.
