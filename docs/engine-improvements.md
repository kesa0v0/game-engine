# 엔진 개선 후보

게임을 만들며 만난 엔진의 제약과 비용을 적는다. 항목마다 어디서 무엇이 막혔는지, 잰 값이 있으면 그 값과 측정 조건을 적는다.

전에 있던 후보 중 남은 것은 `docs/handoff.md`로 옮겼다. 결정 기록이 "engine-improvements N번"으로 부르는 옛 내용은 `git show 0eed385:docs/engine-improvements.md`로 본다.

## 판 위 그림의 풀링

- 어디서: Commander(`hosts/unity`의 `GameObjectLookRenderer`). 그림을 만들 때 `new GameObject`와 `AddComponent`를 하고(`GameObjectLookRenderer.cs:151`, `:391`, `:485`), 지울 때 `Destroy`한다(`:320`). 풀이 없다.
- 본 것: 200m 판 5대5, 명중률 25%, Unity 에디터 Play. 교전 중에 GC가 초당 약 1회, 가장 긴 프레임이 약 10ms다. 평소 프레임은 약 4ms다. 사용자가 화면 표시로 읽은 값이고, 프로파일러로 확인하지는 않았다.
- 짐작: 사격선과 유닛 그림을 만들고 지우는 비용으로 본다. 풀링이 줄이는지는 재지 않았다. 지금 프레임에는 문제가 없고, 유닛 수를 키우면 커질 수 있다.
- 할 일: 엔진을 고칠 때 풀링이 필요한지 판단한다. 먼저 프로파일러로 할당의 출처를 확인한다.

## 새 게임 프로젝트 골격을 만드는 CLI 명령

- 어디서: `src/Kesa.Engine.Cli/Commands.cs`에는 게임 프로젝트를 만드는 명령이 없다. 지금은 Unity 프로젝트(Commander)를 통째로 복사해 새 게임을 만든다. 그러면 게임 내용, 생성물(`Library`, `Logs`, `Temp`), 원격 저장소(`origin`)가 함께 넘어온다. 실제로 Weimar의 `origin`이 아직 Commander 저장소를 가리킨다.
- 사용자와 정한 것(2026-11-08, G로 남기지 않음, 계약이 아닌 도구 기능):
  - 중심은 템플릿 폴더다. 엔진 저장소에 그 자체로 열리는 Unity 프로젝트를 두고, 그 모드 폴더가 `engine check`를 경고 없이 통과하는지 `dotnet test`로 지킨다. `engine new`는 손 복사를 대신하는 얇은 층이다. 생성물을 빼고, 이름을 넣고, `git init`을 한다.
  - 내용은 빈 판이다. 모드 선언과 빈 폴더 구조, 모드를 올리고 진단만 출력하는 호스트를 넣는다. 공통 파일도 넣는다: `.gitignore`, 게임 `CLAUDE.md`, 호스트 선언 에디터 스크립트, PanelSettings와 테마, 장면, `docs/` 빈 양식.
  - 엔진 참조는 `--engine copy|link`로 고르고 기본값은 copy다.
    - copy: 엔진을 빌드한 패키지(소스와 `Plugins/` DLL, 약 2.8MB)를 게임의 `Packages/uk.kesa.engine.unity/`에 복사한다.
    - link: `manifest.json`에 실제 엔진 위치로 계산한 상대 경로를 넣는다.
    - copy로 만든 게임에 엔진을 다시 넣는 명령은 만들지 않고 README에 "다시 복사"만 적는다.
- 제안(사용자 확인 전): C# 이름은 `GameHost`·`host/game.host.json`처럼 일반 이름으로 둔다. 게임 이름은 모드 id·메뉴 글자·`CLAUDE.md` 제목 같은 문자열에만 넣는다. 그러면 파일 이름과 `.meta`를 바꿀 필요가 없다.
- 남은 질문: copy 방식에서 게임 `CLAUDE.md`가 가리킬 안내서와 `check` CLI의 위치. 패키지에 함께 넣을지, 엔진 경로를 쓸지.
- 상태: 사용자 말로 구현을 미뤘다.
