using System;
using System.Threading.Tasks;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// AI 연결 하나를 돌린다 (G366). 호스트가 자리를 <c>SeatKind.Ai</c>로 앉히고 그 연결의 세션과 판단을 준다.
    /// </summary>
    public sealed class BotSeat
    {
        private readonly int _maxActs;
        private long? _idleAt;

        /// <param name="maxActs">한 번의 <see cref="Run"/>에서 할 수 있는 일의 상한 (판단이 끝없이 내는 것을 막음).</param>
        public BotSeat(IHostSession session, IBotPolicy policy, int maxActs = 1000)
        {
            if (maxActs < 1) throw new ArgumentOutOfRangeException(nameof(maxActs));
            Session = session;
            Policy = policy;
            _maxActs = maxActs;
        }

        public IHostSession Session { get; }
        public IBotPolicy Policy { get; }

        /// <summary>
        /// 할 일이 없을 때까지 판단을 부르고 한 일의 수를 준다. 세계가 지난번 "할 일 없음" 뒤로 그대로면 부르지 않는다.
        /// </summary>
        public async ValueTask<int> Run()
        {
            long version = await Session.Version();
            if (_idleAt == version) return 0;
            int acts = 0;
            while (acts < _maxActs)
            {
                if (!await Policy.Act(Session))
                {
                    _idleAt = await Session.Version();
                    return acts;
                }
                acts++;
            }
            _idleAt = null;
            return acts;
        }
    }
}
