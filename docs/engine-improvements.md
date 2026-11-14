# 엔진 개선 후보

게임을 만들며 만난 엔진의 제약과 비용을 적는다. 항목마다 어디서 무엇이 막혔는지, 잰 값이 있으면 그 값과 측정 조건을 적는다.

전에 있던 후보 중 남은 것은 `docs/handoff.md`로 옮겼다. 결정 기록이 "engine-improvements N번"으로 부르는 옛 내용은 `git show 0eed385:docs/engine-improvements.md`로 본다.

## 판 위 그림의 풀링

- 어디서: Commander(`hosts/unity`의 `GameObjectLookRenderer`). 그림을 만들 때 `new GameObject`와 `AddComponent`를 하고(`GameObjectLookRenderer.cs:151`, `:391`, `:485`), 지울 때 `Destroy`한다(`:320`). 풀이 없다.
- 본 것: 200m 판 5대5, 명중률 25%, Unity 에디터 Play. 교전 중에 GC가 초당 약 1회, 가장 긴 프레임이 약 10ms다. 평소 프레임은 약 4ms다. 사용자가 화면 표시로 읽은 값이고, 프로파일러로 확인하지는 않았다.
- 짐작: 사격선과 유닛 그림을 만들고 지우는 비용으로 본다. 풀링이 줄이는지는 재지 않았다. 지금 프레임에는 문제가 없고, 유닛 수를 키우면 커질 수 있다.
- 2026-11-14 잰 값: Commander를 임시 폴더에 복사하고 Unity 에디터를 창 없이 띄워 쟀다(편집 모드, Release DLL, `-releaseCodeOptimization`). 판 `commander:Small`과 `commander_checks:Endless`, 몸풀기 300틱 뒤 600틱. 관리 힙은 `Profiler.GetMonoUsedSizeLong`으로 쟀다. Unity Mono에서 `GC.GetAllocatedBytesForCurrentThread`는 늘 0이다. GC가 낀 표본은 뺐다.
  - 틱마다 관리 할당: 틱 31~44KB, 판 갱신(`LookBoard.RefreshAsync`) 약 35KB, 화면 갱신 1.4KB, 판 시간 6프레임 0.2KB.
  - 렌더러가 틱마다 새로 만드는 노드는 1.1~1.7개다(살아 있는 노드는 최대 45개). 물체 하나에 Sprite와 Text를 만들고 지우는 데 약 29µs, 관리 할당 373B다(편집 모드라 `DestroyImmediate`).
  - 그래서 이 판에서 풀링이 줄이는 것은 틱마다 관리 할당 약 0.5KB(전체 약 80KB의 1% 미만)와 CPU 약 0.04ms다. GC를 부르는 것은 커널 틱과 판 갱신의 C# 쪽 할당이다.
  - 풀링이 효과를 내는 때: 틱마다 노드를 수십 개 넘게 만들고 지울 때(예: 100개면 약 3ms). Commander의 큰 판에서 사격선이 그만큼 생기는지는 재지 않았다.
  - 에디터 Play에서 본 "초당 약 1회"는 에디터 자신의 할당도 섞인 값일 수 있다. 플레이어 빌드에서는 재지 않았다.
- 할 일: 틱과 판 갱신의 관리 할당 출처를 찾는다(.NET 쪽에서 같은 모드를 돌려 할당을 재면 `GC.GetAllocatedBytesForCurrentThread`가 된다). 풀링은 노드를 많이 만드는 판이 생기면 한다.
