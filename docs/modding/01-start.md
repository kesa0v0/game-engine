# 1. 시작하기

## 모드 폴더

모드는 **모드 루트** 아래의 폴더 하나다. 모드 루트에는 모드 폴더가 여럿 있을 수 있고, 엔진은 그 전부를 한 번에 불러온다.

```
samples/guide/            ← 모드 루트 (engine check와 kesa-play에 주는 폴더)
  harbor/                 ← 모드 하나
    mod.py                ← 모드 선언
    schema/types.py       ← 스키마: 어떤 컴포넌트·타입이 있나
    defs/things.py        ← Def: 실제 물건들
    defs/rules.py         ← 규칙: 함수·커맨드·반응
    defs/view.py          ← 화면에 보일 값 (8장)
    ui/console.txt        ← 콘솔 호스트의 화면 파일 (8장)
  lighthouse/             ← harbor를 고치는 다른 모드
    mod.py
    defs/patches.py
```

- 폴더 이름과 모드 id는 달라도 된다(`samples/skirmish/core/`의 id는 `skirmish`).
- `.`으로 시작하는 폴더는 건너뛴다. `mod.py`가 없는 폴더는 경고 `MOD001`을 내고 건너뛴다.
- 파일은 모두 **유효한 파이썬 문법**이라 편집기의 구문 강조·들여쓰기가 그대로 된다. 다만 실행되는 것은 파이썬이 아니라
  엔진의 제한된 언어다(3장).

## `mod.py`

`samples/guide/lighthouse/mod.py`:

```python
Mod(id="lighthouse", version="0.1", dependencies=["harbor"], optional=["smuggler"])
use_all("defs/")
```

`mod.py`는 실행되지 않고 읽히기만 한다. `Mod(...)` 하나와 `use(...)`·`use_all(...)`만 쓸 수 있다(그 밖은 `MOD002`).

| 필드 | 뜻 |
|---|---|
| `id` | 필수. 소문자·숫자·`_`, 글자로 시작, 점 없음(`MOD005`). `engine`은 엔진이 쓰는 이름이라 쓸 수 없다 |
| `version` | 문자열. 저장 파일에 적힌다 |
| `dependencies` | 꼭 있어야 하는 모드. 없으면 이 모드가 꺼진다(`MOD007`). 그 모드의 이름이 보이게 된다 |
| `loadAfter` | 있으면 그 뒤에 불린다. 없어도 된다. 이름은 보이지 않는다 |
| `optional` | 있으면 그 뒤에 불리고 `mod_loaded("x")`로 물을 수 있다(2장 패치) |
| `defaultLanguage` | 언어 태그(`"en"`, `"ko"`, `"pt-BR"`). 틀린 모양이면 `MOD002`. 번역 파일은 `locale/<언어 태그>/` 아래의 `.ftl`(Fluent)이고, `locale/`이 있으면 이 칸과 그 언어 폴더가 있어야 한다. 플레이어 언어에 글이 없으면 이 언어로 보인다. 콘솔은 아직 번역을 쓰지 않는다(9장) |

- 필드 이름은 camelCase다. `load_after=`라고 쓰면 `MOD003`과 "혹시 'loadAfter'?"가 나온다.
- `use("defs/a.py")`는 파일 하나, `use_all("defs/")`는 그 폴더의 `.py` 전부(하위 폴더 포함, 경로 이름 순)를 불러온다.
- **`schema/`로 시작하는 파일은 스키마 파일**이고 나머지는 Def 파일이다. 모든 모드의 스키마 파일이 먼저 돌고, 그다음 Def 파일이 돈다.
  `use`에 적은 순서와 상관없다.

### 로드 순서와 보이는 범위

- 모드는 `dependencies`·`loadAfter`·(있는) `optional` 모드 뒤에 불린다. 순서가 정해지지 않은 모드끼리는 id 순이다.
- 순환(`a → b → a`)은 `MOD008`이고 그 모드들은 꺼진다.
- 한 모드가 **보는** 이름은 자기 것과 `dependencies`(그리고 그 의존성의 의존성)의 것뿐이다. `loadAfter`·`optional`은 순서만 정한다.
  남의 이름을 쓰면 `SCH003`과 "dependencies에 더하라"는 힌트가 나온다.
- 엔진이 주는 이름(`Seatable`, `Tick`)은 누구나 본다.
- 오류가 난 모드는 **통째로 꺼진다**(일부만 불리는 일은 없다). 그 모드에 `dependencies`로 기대는 모드도 꺼진다(`MOD009`).

## `engine check`

모드를 고칠 때마다 돌리는 검사기다. 모드 루트를 준다.

```
dotnet run --project src/Kesa.Engine.Cli -- check samples/guide
```

(빌드한 실행 파일 이름은 `engine`이다: `engine check samples/guide`.)

```
samples/guide/lighthouse/defs/patches.py:11:1: info PAT002: mod 'smuggler' is not loaded, so 1 patch call was not applied

errors: 0, warnings: 0, info: 1; mods loaded: 2, disabled: 0
```

- 한 줄이 진단 하나다: `파일:줄:열: 등급 코드: 내용`. 그 아래 `hint:`(대개 "did you mean 'X'?")와 `related:`(관련 위치)가 붙는다.
- 이름 오타는 거의 다 로드 때 잡힌다: 없는 Def(`LNK001`), 없는 이름(`EVL001`), 컴포넌트·멤버 이름 오타(`EVL001`, 타입을 아는 곳에서),
  같은 엔티티 안의 `derived` 순환(`SCH009`). Def id 값을 `"Sloop"` 같은 맨 문자열과 비교하면 경고 `LNK005`(2장).
- 번역 파일(`locale/`)도 본다(`LOC`): 구문 오류와 모르는 조사는 오류, `text("키")`의 키가 어느 언어에도 없거나 번역문이 쓰는 변수를
  넘기지 않으면 경고, 언어마다 빠진 글의 수는 정보. 번역 오류는 모드를 끄지 않고 그 글만 빼지만 `check`의 종료 코드는 1이다.
- 종료 코드: 0 오류 없음(경고는 괜찮음), 1 내용 오류, 2 사용법 오류, 3 엔진 버그.
- `--warnings-as-errors`는 경고도 오류로 친다(배포 전 확인용). `--json`은 도구용 출력이다.
- `engine dump samples/guide`는 상속·패치가 끝난 **최종 Def**를 JSON으로 보인다. 값마다 어느 모드가 적었고 어느 패치가 바꿨는지가
  붙어 있어 "이 값이 왜 이렇지?"를 찾을 때 쓴다.

## 편집기: `engine stubs`

```
dotnet run --project src/Kesa.Engine.Cli -- stubs samples/guide
```

- 모드마다 `__builtins__.pyi`를(스키마 폴더에는 `schema/__builtins__.pyi`도) 쓰고, 모드 루트에 `pyrightconfig.json`이 없으면 만든다.
- 모드 루트를 편집기의 작업 폴더로 열면 pyright가 이 스텁을 내장 이름으로 읽어, 내 스키마의 컴포넌트·Def id·내장 함수가 자동완성되고
  타입이 틀리면 밑줄이 그어진다. 스텁은 Python 3.12 이상 문법이다(pyright로 확인함. Pylance는 아직 확인하지 않음).
- 스텁은 만들어진 파일이다. 손으로 고치지 말고, 스키마를 고치면 다시 만든다.
- **스텁이 레퍼런스다.** 어떤 내장 함수가 있고 무엇을 받는지는 스텁을 본다.

## 돌려 보기: `kesa-play`

텍스트 콘솔 호스트로 한 판을 직접 돌릴 수 있다.

```
dotnet run --project src/Kesa.Engine.ConsoleHost -- samples/skirmish --seat red=skirmish:Red --seat blue=skirmish:Blue
```

- `--seat 연결=Def`는 그 Def의 엔티티가 생기면 그 연결을 앉힌다(7장). 호스트는 Def의 전체 이름(`모드id:이름`, 2장)을 받는다. `--seat red`처럼 Def를 빼면 앉을 자리를 화면에서 묻는다.
- `--seed N`으로 난수 씨앗을 바꾼다. 같은 씨앗이면 같은 판이 나온다.
- 화면에는 모드의 `ui/console.txt`가 정한 값(8장), 열린 질문, 낼 수 있는 수가 번호와 함께 나온다. 번호를 넣어 고르고 `:q`로 끝낸다.
- 모드에 오류가 있으면 `engine check`와 같은 형식으로 보이고 1로 끝난다.
