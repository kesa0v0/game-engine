using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 배정 표 (spec S6 입구, spec-gaps G236·G237·G241): 엔티티마다 그 엔티티로 행동하는 연결 꼬리표 하나와 그 연결의 종류(사람·AI).
    /// 게임 상태라 저장되고, 엔티티가 죽으면 그 배정도 함께 사라진다(커맨드 안이면 되감긴다). 바꾸는 길은 호스트 API
    /// <see cref="Seat"/>·<see cref="Unseat"/>뿐이다. 스크립트는 연결 꼬리표를 보지 못하고 종류만 <c>controller(e)</c>로 읽는다.
    /// 어떤 엔티티든 앉힐 수 있다(진영이든 캐릭터든 게임이 고른다). 한 연결은 여러 엔티티에 앉을 수 있다(핫시트).
    /// </summary>
    public sealed partial class World
    {
        private readonly SortedDictionary<long, (string Issuer, SeatKind Kind)> _seats =
            new SortedDictionary<long, (string Issuer, SeatKind Kind)>();

        /// <summary>
        /// <paramref name="issuer"/> 연결을 <paramref name="id"/> 엔티티에 앉힌다. 이미 다른 연결이 앉아 있으면 바꾼다. 로비 배정·
        /// 재접속·AI 인수 모두 이 길이다(S6 "호스트가 상태에 직접 쓰는 하나뿐인 경로"). 호스트는 이 호출을 커맨드처럼 기록한다.
        /// <paramref name="kind"/>는 그 연결이 사람인지 AI인지이고 스크립트가 읽는다(G241).
        /// </summary>
        public void Seat(EntityId id, string issuer, SeatKind kind = SeatKind.Human)
        {
            NoHostWrites("Seat");
            if (string.IsNullOrEmpty(issuer))
                throw new WorldException(Codes.RunSeat, "a connection tag must not be empty",
                    "use Unseat to leave an entity without a connection (spec-gaps G236)");
            if (!IsAlive(id)) throw new WorldException(Codes.RunSeat, "cannot seat a connection at " + id + ": it is not alive");
            _seats[id.Value] = (issuer, kind);
            SeatsVersion++;
            Touch(id.Value, SeatRead); // controller(e)를 읽은 when 조건을 다시 본다
            Stamp(id.Value, SeatRead);
        }

        /// <summary><paramref name="id"/> 엔티티의 배정을 지운다(빈자리·AI가 맡음). 앉은 연결이 없으면 아무것도 하지 않는다.</summary>
        public void Unseat(EntityId id)
        {
            NoHostWrites("Unseat");
            if (!_seats.Remove(id.Value)) return;
            SeatsVersion++;
            Touch(id.Value, SeatRead);
            Stamp(id.Value, SeatRead);
        }

        /// <summary><paramref name="id"/> 엔티티에 앉은 연결. 없거나 죽은 엔티티면 null.</summary>
        public string? SeatOf(EntityId id) => _seats.TryGetValue(id.Value, out var seat) ? seat.Issuer : null;

        /// <summary><paramref name="id"/> 엔티티에 앉은 연결의 종류. 없거나 죽은 엔티티면 null.</summary>
        public SeatKind? SeatKindOf(EntityId id) => _seats.TryGetValue(id.Value, out var seat) ? seat.Kind : (SeatKind?)null;

        /// <summary><paramref name="issuer"/> 연결이 앉은 엔티티들, id 순. 없으면 빈 목록(관전자).</summary>
        public IReadOnlyList<EntityId> SeatsOf(string issuer)
        {
            var result = new List<EntityId>();
            foreach (var pair in _seats)
                if (string.Equals(pair.Value.Issuer, issuer, StringComparison.Ordinal)) result.Add(new EntityId(pair.Key));
            return result;
        }

        /// <summary>호스트가 부른 함수가 <c>seats()</c>를 읽었나: 영수증이 배정 표의 바뀜을 본다 (G421).</summary>
        private bool _seatsRead;

        /// <summary><c>seats()</c>·<c>seats(타입)</c> (G421): 보는 연결이 앉은 엔티티, id 순. 이름을 주면 <c>query</c>처럼 거른다.</summary>
        private Value SeatsBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            if (keywords.Count > 0 || positional.Count > 1)
                throw new ScriptRuntimeException(Codes.RunType, "seats() takes nothing or one type or component",
                    "write seats() for every entity this player sits at, or seats(Party) (spec-gaps G421)");
            if (_viewer == null)
                throw new ScriptRuntimeException(Codes.RunNotWritable, "seats() can only be used on a screen, in a @ui_action or in a @bot",
                    FunctionEffects.QuestionsHint);
            IReadOnlyList<EntityId> seated = SeatsOf(_viewer.Issuer);
            if (positional.Count == 1)
            {
                var name = SchemaNameArgument(PureBuiltins.SeatsName, positional[0]);
                var of = new HashSet<long>();
                foreach (var id in Guard(() => Query(name))) of.Add(id.Value);
                var kept = new List<EntityId>();
                foreach (var id in seated)
                    if (of.Contains(id.Value)) kept.Add(id);
                seated = kept;
            }
            if (_whenReads != null) _seatsRead = true;
            var result = new List<Value>(seated.Count);
            foreach (var id in seated) result.Add(new EntityView(this, id));
            return new ListValue(result);
        }

        /// <summary>
        /// 로비가 보일 앉을 후보 (G239): <see cref="Schema.EngineComponents.Seatable"/>이 붙은 살아 있는 엔티티, id 순. 후보일 뿐
        /// <see cref="Seat"/>를 막지 않는다(AI 연결을 목록 밖 엔티티에 앉힐 수 있다).
        /// </summary>
        public IReadOnlyList<EntityId> SeatChoices() => Query(Schema.EngineComponents.Seatable);

        /// <summary>
        /// 입구 검사 (G237): <c>acts_for</c>가 고른 엔티티(응답이면 답하는 엔티티)에 이 연결이 앉아 있어야 한다. <c>None</c>은 누구나 통과한다.
        /// </summary>
        private bool MayAct(string issuer, Value target) =>
            IsTrial || !(target is EntityValue entity) || // 시험 판에서는 봇이 모든 자리에 앉은 것으로 본다 (G457)
            (_seats.TryGetValue(entity.Id.Value, out var seated) && string.Equals(seated.Issuer, issuer, StringComparison.Ordinal));

        /// <summary>엔티티가 죽을 때 그 배정을 지운다. 커맨드 안이면 되감기를 기록한다.</summary>
        private void DropSeat(long id)
        {
            if (!_seats.TryGetValue(id, out var seat)) return;
            _seats.Remove(id);
            long version = SeatsVersion++;
            Journal(() =>
            {
                _seats.Add(id, seat);
                SeatsVersion = version;
            });
        }

        /// <summary>
        /// <c>controller(e)</c> (G241): 앉은 연결이 사람이면 <c>"human"</c>, AI면 <c>"ai"</c>, 빈자리거나 죽은 엔티티면 <c>None</c>.
        /// 연결 꼬리표는 주지 않는다(판마다 달라 규칙이 기대면 깨진다). when 조건이 읽으면 그 엔티티를 읽은 것으로 기록한다.
        /// </summary>
        private Value ControllerBuiltin(IReadOnlyList<Value> positional)
        {
            if (positional.Count != 1 || !(positional[0] is IEntityReference entity))
                throw new ScriptRuntimeException(Codes.RunType,
                    "controller() takes one entity, got " + (positional.Count == 1 ? positional[0].KindName : positional.Count + " arguments"),
                    "write controller(party)");
            long id = entity.EntityId.Value;
            Read(id, SeatRead);
            if (!_seats.TryGetValue(id, out var seat) || HiddenFromViewer(id)) return NoneValue.Instance;
            return new StrValue(seat.Kind == SeatKind.Ai ? ControllerAi : ControllerHuman);
        }

        private const string ControllerHuman = "human";
        private const string ControllerAi = "ai";
    }
}
