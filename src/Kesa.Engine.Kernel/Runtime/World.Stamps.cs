using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 바뀜 도장 (spec S10 4항, spec-gaps G235): 쓰기마다 전역 시계를 하나 올리고 그 값을 (엔티티, 칸 이름)에 적는다. 칸 이름은 컴포넌트
    /// 이름이거나 역방향 컬렉션(<c>inverse</c>) 이름이다. 호스트는 그린 때의 도장을 기억했다가 지금 도장과 견주어 바뀐 것만 다시 읽는다.
    /// 실패한 커맨드는 도장도 되감는다(C8). 도장은 저장하지 않는다: 불러온 월드는 새 월드다.
    /// </summary>
    public sealed partial class World
    {
        private sealed class Stamps
        {
            public Stamps(long created)
            {
                Created = created;
                Any = created;
            }

            /// <summary>만들어진(불러온) 때. 아직 찍히지 않은 칸의 도장이다.</summary>
            public long Created { get; }

            public long Any { get; set; }

            public Dictionary<string, long>? ByName { get; set; }
        }

        private long _clock;

        /// <summary>지금 커맨드가 시작할 때의 시계. 이보다 큰 도장은 이 커맨드가 찍은 것이라 다시 기록하지 않는다.</summary>
        private long _stampFloor;

        private readonly Dictionary<long, Stamps> _stamps = new Dictionary<long, Stamps>();

        /// <summary>엔티티 상태가 마지막으로 바뀐 때 (전역 시계). 그대로면 어느 엔티티도 바뀌지 않았다.</summary>
        public long Version() => _clock;

        /// <summary>이 엔티티가 마지막으로 바뀐 때 (어느 칸이든, 만들어진 때 포함). 죽었거나 없는 엔티티는 0.</summary>
        public long Version(EntityId id) =>
            _entities.ContainsKey(id.Value) && _stamps.TryGetValue(id.Value, out var s) ? s.Any : 0;

        /// <summary>
        /// 이 엔티티의 칸 하나(컴포넌트나 역방향 컬렉션 이름)가 마지막으로 바뀐 때. 값·줄·붙이기·떼기·그 칸을 겨냥한 기여 행이 바꾼다.
        /// 다른 엔티티를 읽는 <c>derived</c> 값의 바뀜은 담지 않는다. 죽었거나 없는 엔티티는 0.
        /// </summary>
        public long Version(EntityId id, string name)
        {
            if (!_entities.TryGetValue(id.Value, out var entity)) return 0;
            if (!_schema.TryGetComponent(name, out _) && !HasInverse(entity, name))
                throw new WorldException(Codes.RunNoComponent, "'" + name + "' is neither a component nor an inverse of " + entity.Def.Type.Name);
            if (!_stamps.TryGetValue(id.Value, out var s)) return 0;
            return s.ByName != null && s.ByName.TryGetValue(name, out var v) ? v : s.Created;
        }

        private static bool HasInverse(EntityRecord entity, string name)
        {
            foreach (var inverse in entity.Def.Type.Inverses)
                if (inverse.Name == name) return true;
            return false;
        }

        /// <summary>엔티티가 생겼다(만들기·불러오기). 되감으면 도장도 없어진다.</summary>
        private void StampCreated(long id)
        {
            _stamps[id] = new Stamps(++_clock);
            Journal(() => _stamps.Remove(id));
        }

        private void StampDestroyed(long id)
        {
            ++_clock;
            if (!_stamps.TryGetValue(id, out var s)) return;
            _stamps.Remove(id);
            Journal(() => _stamps.Add(id, s));
        }

        /// <summary>되감는 중이면 참: 되감기가 부르는 기본 연산은 도장을 찍지 않는다(도장은 따로 되감긴다).</summary>
        private bool _rewinding;

        /// <summary>
        /// 엔티티 <paramref name="id"/>의 칸 <paramref name="name"/>이 바뀌었다(null이면 이름 없는 바뀜, 엔티티 도장만). 커맨드 안에서는
        /// 칸마다 처음 찍을 때만 옛 도장을 기록한다.
        /// </summary>
        private void Stamp(long id, string? name)
        {
            if (_rewinding || !_stamps.TryGetValue(id, out var s)) return;
            long now = ++_clock;
            if (_undo != null && s.Any <= _stampFloor)
            {
                long oldAny = s.Any;
                Journal(() => s.Any = oldAny);
            }
            s.Any = now;
            if (name == null) return;
            var byName = s.ByName ??= new Dictionary<string, long>(StringComparer.Ordinal);
            if (byName.TryGetValue(name, out var old))
            {
                if (_undo != null && old <= _stampFloor) Journal(() => byName[name] = old);
            }
            else if (_undo != null) Journal(() => byName.Remove(name));
            byName[name] = now;
        }
    }
}
