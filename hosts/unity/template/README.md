# 새 게임 템플릿

`engine new`가 복사하는 Unity 게임 프로젝트다. 이 폴더도 그 자체로 열린다(엔진 패키지를 `file:../../uk.kesa.engine.unity`로 참조). 이 README는 새 게임에 복사되지 않는다.

## 새 게임 만들기

엔진 루트에서 먼저 `dotnet build`를 한 뒤:

```
dotnet run --project src/Kesa.Engine.Cli -- new Weimar --dir ../Weimar
```

- `--engine copy`(기본): 엔진 패키지(`Plugins/` DLL 포함, `.pdb` 제외), 모더 안내서·예시·데모 호스트(`Documentation~/`, 엔진과 같은 상대 위치), CLI(`Tools~/cli/`, 커널 DLL은 `Plugins/`의 것을 씀)를 게임의 `Packages/uk.kesa.engine.unity/`에 넣는다. 게임만 clone해도 열리고 검사할 수 있다. 엔진을 새로 넣으려면 그 폴더를 지우고 같은 것을 다시 복사한다.
- `--engine link`: `Packages/manifest.json`과 게임 `CLAUDE.md`에 실제 엔진 위치까지의 상대 경로를 넣는다. 엔진과 게임을 같은 폴더에 나란히 두는 것을 권장하고, 아니면 경고한다.
- `--id`: 모드 id(기본은 이름을 소문자로). `--no-git`: `git init`과 `git lfs install --local`을 하지 않는다.
- `.gitattributes`는 그림·소리·모델·글꼴과 `*.dll`(copy의 `Plugins/`)을 Git LFS에 넣는다. git-lfs가 없으면 `new`가 경고하고, 첫 커밋 전에 깔아야 한다.

`new`는 끝에 새 모드 폴더로 `check`를 돌린다.

## 넣는 것

빈 판이다. 모드 `game`(새 게임에서는 그 id)에는 선언(`mod.py`)만 있고, `GameHost`는 모드를 불러와 진단을 적고 판을 연다. 자리·화면·판 위 그림·카메라를 붙이는 예는 `hosts/unity/demo/Assets/Scripts/KesaDemo.cs`다.

- C# 이름은 일반 이름(`GameHost`, `GameCheck`, `host/game.host.json`)이다. 게임 이름은 `CLAUDE.md` 제목, `productName`, 모드 id에만 들어간다.
- `new`가 바꾸는 곳: 모드 폴더 이름과 `mod.py`의 id, `GameHost.ModId`, `ProjectSettings.asset`의 `productName`·`productGUID`, `Packages/manifest.json`, `.md` 파일의 `{{GAME}}`·`{{MOD_ID}}`·`{{ENGINE}}`·`{{PACKAGE}}`·`{{CLI}}`.
- 템플릿을 고치면 `tests/Kesa.Engine.Cli.Tests/NewGameTests.cs`가 모드 검사와 `new`의 결과를 본다. Unity 쪽 C#은 `dotnet test`에 안 들어가므로 새로 만든 게임에서 `-executeMethod GameCheck.Run`으로 확인한다.
