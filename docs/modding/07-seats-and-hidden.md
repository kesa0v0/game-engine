# 7. 자리와 숨긴 정보

## 연결과 자리

게임에 들어온 사람이나 AI를 **연결**이라 한다. 연결이 어느 엔티티(진영, 상회, 캐릭터)를 맡는지는 엔진의 **배정 표**가 든다.
배정은 호스트가 한다(로비, 재접속, AI가 이어받기). **모드는 연결 이름을 모르고 알아서도 안 된다.** `"red"` 같은 연결 이름을 모드에
박으면 판마다 달라 깨진다.

모드가 할 일은 셋이다.

1. **앉을 수 있는 엔티티에 `Seatable`을 붙인다.** 로비가 이 목록을 보인다.

   ```python
   deftype("House", components=["Purse", "Stock", "Fleet", "Secret", "Seatable"])
   ```

   게임 중에 후보가 되었다 말았다 하면 Def 목록 대신 `attach(e, Seatable())`·`detach(e.Seatable)`로 붙이고 뗀다.
   팀 색 같은 게임 데이터는 `Seatable`이 아니라 같은 엔티티의 내 컴포넌트에 둔다.

2. **커맨드마다 `acts_for`로 누구로서 행동하는지 말한다**(4장). 엔진은 낸 연결이 그 엔티티에 앉아 있을 때만 통과시킨다.
   통과하지 못하면 사전 조건도 돌지 않는다(입구에서 막힘). "내 유닛만 움직일 수 있다" 같은 소유 검사는 입구가 아니라
   `require`/`choose`로 쓴다.

   ```python
   @command(free=True, acts_for=lambda house: house)
   def Buy(house: Ref[House], kind: str):
   ```

3. **사람인지 AI인지가 규칙에 필요하면 `controller(e)`** 로 읽는다: `"human"`, `"ai"`, 빈자리면 `None`.

한 연결이 여러 엔티티에 앉을 수 있다(핫시트). 한 엔티티에 여러 연결(공동 조종)은 아직 없다.

## 숨긴 정보: 보기 정책

카드 게임의 손패, 안개 속 유닛처럼 **누가 무엇을 보는지**는 모드가 함수 하나로 정한다. `samples/guide/harbor/defs/rules.py`:

```python
# 비밀 계획은 그 상회만 본다
@view_policy
def may_see(viewer, entity, component):
    if component == "Secret":
        return viewer == entity
    return True
```

- `viewer`: 보는 연결이 앉은 엔티티. 여러 곳에 앉았으면 자리마다 부르고 하나라도 참이면 보인다. 관전자는 `None`.
- `entity`: 보일지 묻는 엔티티. `component`가 `None`이면 "엔티티 자체가 보이나", 이름이면 "그 컴포넌트가 보이나"다.
- 참·거짓을 돌려준다. 순수해야 한다(`CMD004`). 오류가 나거나 참거짓이 아니면 "안 보임"으로 치고 `RUN018`.
- 정책은 모든 모드를 통틀어 하나다. 다른 모드의 정책을 바꾸려면 `@view_policy(override=True)`.
- 정책이 없으면 모두 보인다.

### 규칙은 다 보고, 화면과 AI만 거른다

커맨드·핸들러·`derived`는 **실제 상태를 다 읽는다.** 거르는 것은 호스트가 연결에게 보여 주는 쪽이다.

- 화면 함수(8장)를 그 연결로 부르면 함수 안의 읽기가 모두 걸러진다. 안 보이는 엔티티는 `query`에서 빠지고, 안 보이는 컴포넌트는
  붙지 않은 것(`h.Secret is None`)으로 읽히며, 안 보이는 엔티티를 가리키는 Ref는 `hidden` 값이 된다.
  `samples/guide/harbor/defs/view.py`:

  ```python
  def plans(me):
      return [{"house": h, "plan": "?" if h.Secret is None else h.Secret.plan} for h in query("House")]
  ```

  Gull 자리에서 부르면 자기 계획은 숫자, Crane 것은 `"?"`다.
- AI와 UI가 받는 합법 수·질문도 그 연결이 볼 수 있고 낼 수 있는 것만 남는다.

### 모더가 챙길 것

- **보이는 `derived`는 숨긴 값으로 계산되어도 그대로 보인다.** 숨긴 값을 요약한 `derived`(손패 점수 합 등)는 그 컴포넌트도 숨긴다.
- 공개해야 할 사실(손패가 5장이다, 이미 골랐다)은 보이는 컴포넌트에 따로 둔다.
- 카드 뒷면처럼 "무엇인지는 모르지만 있다"는 공통 뒷면 Def와 숨긴 컴포넌트(진짜 DefId)로 만든다.
- 엔티티 id가 차례로 붙어, 숨긴 엔티티가 있다는 사실 자체는 번호 틈으로 드러날 수 있다(알려진 한계).
