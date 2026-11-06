# 묶음 API 확인 프로젝트

`Parts`와 `WriteColumn`(G478)이 Unity에서 도는지 본다: Burst 작업에 칼럼 메모리를 그대로 넘기기, 세 방식(`Get`+`Set`, 조각, 조각+Burst)의 저장 해시 비교, 호스트 함수에서의 커맨드와 되감기, 런타임에 붙인 값, 지난 조각 거절.

저장소 루트에서 `dotnet build`를 먼저 한다. 측정값은 `-c Release`로 빌드한 DLL로 잰다.

```
Unity.exe -batchmode -nographics -projectPath hosts/unity/bulk-check -executeMethod BulkBuild.RunEditor -logFile -
Unity.exe -batchmode -nographics -projectPath hosts/unity/bulk-check -executeMethod BulkBuild.BuildPlayer -logFile -
hosts/unity/bulk-check/Build/bulk.exe -batchmode -nographics -logFile -
```

IL2CPP는 `BuildPlayer`에 `-kesaIl2cpp`를 붙인다(Unity의 IL2CPP 모듈이 있어야 한다). 결과는 `KESA BULK:`로 시작하는 줄이고 틀리면 종료 코드가 1이다.
