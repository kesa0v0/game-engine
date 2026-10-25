using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>
    /// 호스트 경계의 같은 프로세스 구현 (spec-gaps G324·G327): 커널 <see cref="World"/>를 바로 부르고 이미 끝난 결과를 준다.
    /// 커널은 한 스레드라 이 객체와 그 세션들은 한 스레드에서만 부른다.
    /// </summary>
    public sealed class LocalHost : IHostOperator
    {
        private readonly Func<byte[], WorldLoadResult> _load;
        private readonly SortedDictionary<string, LocalSession> _sessions = new SortedDictionary<string, LocalSession>(StringComparer.Ordinal);

        /// <param name="load">저장을 불러오는 법(불러오기 선택지를 정할 때). 없으면 <see cref="World.Load"/>의 기본값.</param>
        public LocalHost(ContentLoadResult content, World world, Func<byte[], WorldLoadResult>? load = null)
        {
            Content = content;
            World = world;
            _load = load ?? (save => World.Load(content, save));
        }

        public ContentLoadResult Content { get; }

        /// <summary>지금 판. 불러오면 갈아 끼워진다.</summary>
        public World World { get; private set; }

        public ValueTask<IHostSession> Open(string connection)
        {
            if (!_sessions.TryGetValue(connection, out var session))
            {
                session = new LocalSession(this, connection);
                _sessions[connection] = session;
            }
            return new ValueTask<IHostSession>(session);
        }

        public ValueTask<IReadOnlyList<BridgeSeat>> SeatChoices()
        {
            var ids = new List<EntityId>(World.SeatChoices());
            ids.Sort((a, b) => a.Value.CompareTo(b.Value));
            var seats = new List<BridgeSeat>(ids.Count);
            foreach (var id in ids) seats.Add(new BridgeSeat(new BridgeEntity(id.Value, World.DefOf(id)), World.SeatOf(id)));
            return new ValueTask<IReadOnlyList<BridgeSeat>>(seats);
        }

        public ValueTask Seat(long entity, string connection, bool ai = false)
        {
            World.Seat(new EntityId(entity), connection, ai ? SeatKind.Ai : SeatKind.Human);
            return default;
        }

        public ValueTask Unseat(long entity)
        {
            World.Unseat(new EntityId(entity));
            return default;
        }

        public ValueTask<BridgeAdvance> Advance(int ticks)
        {
            var advanced = World.Advance(ticks);
            return new ValueTask<BridgeAdvance>(new BridgeAdvance(advanced.Advanced, advanced.Tick, advanced.Blocked));
        }

        public ValueTask<byte[]> Save() => new ValueTask<byte[]>(World.Save());

        /// <summary>
        /// 불러온 판으로 갈아 끼운다. 세션의 영수증은 옛 판의 것이라 모두 바뀐 것이 된다(<see cref="CallResult.Changed"/>는 다른 월드면 참).
        /// </summary>
        public ValueTask<BridgeLoad> Load(byte[] save)
        {
            var loaded = _load(save);
            var problems = new List<string>(loaded.Diagnostics.Count);
            foreach (var d in loaded.Diagnostics) problems.Add(d.ToString());
            if (loaded.World != null) World = loaded.World;
            return new ValueTask<BridgeLoad>(new BridgeLoad(loaded.Succeeded, problems));
        }
    }
}
