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
- 커널 쪽 할당 줄이기(2026-11-14): .NET에서 같은 판을 돌려(`commander:Small` 1500틱, Release) 틱의 관리 할당을 36.5KB → 18.9KB로 줄였다. 고친 것: `ReadMember`의 클로저, ChaCha20 블록과 난수 키 해시, fixed 곱·나눗셈·비교의 BigInteger, 컴포넌트 뷰 다시 쓰기, 세션과 지역 변수 표 다시 쓰기. 판 갱신은 틱과 따로 재며 틱마다 `WorldObjects` 14.8KB → 12.9KB, `WorldFx` 6.7KB → 6.6KB다.
  - 남은 출처(할당 표본 순): `Value[]`·`List<Value>`(목록 값), `FixedValue`(값 객체), `UInt32[]`(`atan2_deg` 등 G472 수학 함수의 BigInteger 급수, 사격 효과에서), 문자열, `SortedSet<long>`의 노드와 열거자, `MemberHandle`·`EntityView`. 값 객체를 구조체로 바꾸는 것은 값 표현 전체에 퍼지는 결정이라 미뤘다.
- 미룸(사용자, 2026-11-15): 지금 판에서는 프레임에 문제가 없다. 노드를 많이 만드는 판(초당 수십 개의 효과·모델)이 생기면 그 판에서 Unity GC 빈도부터 다시 재고, 풀링·판 갱신(`WorldObjects`) 할당·위의 남은 출처를 그때 고른다. Unity의 증분 GC 설정이 Commander에서 켜져 있는지도 그때 본다.

## 콘솔 호스트가 호스트 함수를 받지 않는다

- 어디서: Coup(2026-11-16). 모드 `coup`가 게임 C#의 호스트 함수 `geom.*`에 의존하게 됐다. 대본으로 판을 돌리는 Coup의 `tools/night.py`는 엔진의 콘솔 호스트(`kesa-play`, `src/Kesa.Engine.ConsoleHost`의 `Play.cs`)를 부르는데, 그 호스트에는 게임의 `HostFunctions`를 넘길 길이 없다. `engine check`·`stubs`는 `--host 선언파일`로 이름과 서명만 받으니 검사는 되지만, 판을 실제로 돌리지는 못한다.
- 지금: 모드가 `geom.*`을 아직 부르지 않아 `night.py`는 돈다. 부르기 시작하면 멈춘다. Commander는 같은 까닭으로 자기 `tools/RuleRunner`(Assets의 C#을 링크하고 Kernel을 참조)를 만들었다. Coup도 `tools/CoupTool`에 같은 길을 붙일 생각이다.
- 후보: 콘솔 호스트가 호스트 함수를 등록하는 어셈블리를 받는다(예: `--host-assembly 경로` 와 정해진 등록 진입점). 아니면 게임마다 러너를 만드는 것을 표준 길로 정하고, 러너가 쓸 대본 실행부를 라이브러리로 내놓는다. 게임 둘이 같은 러너를 따로 만들고 있다.

## 표준 호스트 함수 후보: 다각형 지역(`geom`)

- 어디서: Coup(2026-11-16) `Assets/Scripts/Geom/`. 결행 밤의 부대가 지도 위에 자유형으로 그린 지역을 맡는다. 확대하면 다리 하나, 정문 하나가 구별되는 도시 지도라 칸 대신 꼭짓점으로 나타낸다.
- 무엇: 지역은 층의 목록 `list[list[fixed]]`, 층은 `[연산(1 더하기, -1 빼기), x0, y0, ...]`이고 차례로 쌓는다. 고리마다 감은 수가 0이 아닌 곳이 안이라 스스로 꼬인 올가미도 된다. 함수는 `region.contains`, `region.area`, `region.perimeter`, `region.edge_facing(region, other)`(경계 중 바깥이 other 안인 길이), `ring.simplify`. 합치기·깎기의 결과 다각형은 만들지 않고 층으로 답한다.
- 결정성: 부동소수점 없이 정수와 BigInteger로만 계산한다. 교점은 동차 유리수로 정확히 두고, 길이는 가장 가까운 raw로 반올림한다(제곱근은 정수 뉴턴법). 좌표는 raw ±1e15까지.
- 잰 값: 200꼭짓점 올가미 + 빼기 층 + 물 지역과의 맞닿음을 한 번 계산하는 데 약 13ms(.NET 9, `dotnet test` 기본 설정의 NUnit 안에서 한 번 잰 값).
- 준비해 둔 것: Coup의 개념을 넣지 않았다. 주인 모드 `geom`은 매니페스트만 있다. 엔진으로 올리면 `std`의 이름으로 옮기고 Coup는 의존을 지운다. 시험은 Coup의 `tools/GeomTests`에 있다.
