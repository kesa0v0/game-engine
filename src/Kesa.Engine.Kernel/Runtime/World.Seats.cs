using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 배정 표 (spec S6 입구, spec-gaps G236·G237): 엔티티마다 그 엔티티로 행동하는 연결 꼬리표 하나. 게임 상태라 저장되고, 엔티티가
    /// 죽으면 그 배정도 함께 사라진다(커맨드 안이면 되감긴다). 바꾸는 길은 호스트 API <see cref="Seat"/>·<see cref="Unseat"/>뿐이고
    /// 스크립트는 연결 꼬리표를 보지 못한다. 어떤 엔티티든 앉힐 수 있다(진영이든 캐릭터든 게임이 고른다). 한 연결은 여러 엔티티에
    /// 앉을 수 있다(핫시트).
    /// </summary>
    public sealed partial class World
    {
        private readonly SortedDictionary<long, string> _seats = new SortedDictionary<long, string>();

        /// <summary>
        /// <paramref name="issuer"/> 연결을 <paramref name="id"/> 엔티티에 앉힌다. 이미 다른 연결이 앉아 있으면 바꾼다. 로비 배정·
        /// 재접속·AI 인수 모두 이 길이다(S6 "호스트가 상태에 직접 쓰는 하나뿐인 경로"). 호스트는 이 호출을 커맨드처럼 기록한다.
        /// </summary>
        public void Seat(EntityId id, string issuer)
        {
            NoHostWrites("Seat");
            if (string.IsNullOrEmpty(issuer))
                throw new WorldException(Codes.RunSeat, "a connection tag must not be empty",
                    "use Unseat to leave an entity without a connection (spec-gaps G236)");
            if (!IsAlive(id)) throw new WorldException(Codes.RunSeat, "cannot seat a connection at " + id + ": it is not alive");
            _seats[id.Value] = issuer;
            Stamp(id.Value, null);
        }

        /// <summary><paramref name="id"/> 엔티티의 배정을 지운다(빈자리·AI가 맡음). 앉은 연결이 없으면 아무것도 하지 않는다.</summary>
        public void Unseat(EntityId id)
        {
            NoHostWrites("Unseat");
            if (_seats.Remove(id.Value)) Stamp(id.Value, null);
        }

        /// <summary><paramref name="id"/> 엔티티에 앉은 연결. 없거나 죽은 엔티티면 null.</summary>
        public string? SeatOf(EntityId id) => _seats.TryGetValue(id.Value, out var issuer) ? issuer : null;

        /// <summary><paramref name="issuer"/> 연결이 앉은 엔티티들, id 순. 없으면 빈 목록(관전자).</summary>
        public IReadOnlyList<EntityId> SeatsOf(string issuer)
        {
            var result = new List<EntityId>();
            foreach (var pair in _seats)
                if (string.Equals(pair.Value, issuer, StringComparison.Ordinal)) result.Add(new EntityId(pair.Key));
            return result;
        }

        /// <summary>
        /// 입구 검사 (G237): <c>acts_for</c>가 고른 엔티티(응답이면 답하는 엔티티)에 이 연결이 앉아 있어야 한다. <c>None</c>은 누구나 통과한다.
        /// </summary>
        private bool MayAct(string issuer, Value target) =>
            !(target is EntityValue entity) ||
            (_seats.TryGetValue(entity.Id.Value, out var seated) && string.Equals(seated, issuer, StringComparison.Ordinal));

        /// <summary>엔티티가 죽을 때 그 배정을 지운다. 커맨드 안이면 되감기를 기록한다.</summary>
        private void DropSeat(long id)
        {
            if (!_seats.TryGetValue(id, out var issuer)) return;
            _seats.Remove(id);
            Journal(() => _seats.Add(id, issuer));
        }
    }
}
