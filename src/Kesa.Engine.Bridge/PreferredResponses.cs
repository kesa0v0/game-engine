using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// 가장 단순한 판단 (G366): 열린 질문마다 합법 답 중 좋아하는 응답 커맨드 순서로 첫 것을 낸다.
    /// 목록에 없는 커맨드는 목록에 있는 것 뒤로, 같은 순위면 합법 수의 순서대로.
    /// </summary>
    public sealed class PreferredResponses : IBotPolicy
    {
        private readonly IReadOnlyList<string> _order;
        private readonly int _max;

        public PreferredResponses(IReadOnlyList<string> order, int max = 1000)
        {
            _order = order;
            _max = max;
        }

        public async ValueTask<bool> Act(IHostSession me)
        {
            foreach (var request in await me.OpenRequests())
            {
                var legal = await me.LegalResponses(request.Id, _max);
                BridgeMove? best = null;
                int bestRank = int.MaxValue;
                foreach (var move in legal.Moves)
                {
                    int rank = Rank(move.Command);
                    if (rank < bestRank)
                    {
                        best = move;
                        bestRank = rank;
                    }
                }
                if (best == null) continue;
                if ((await me.Respond(request.Id, best.Command, best.Arguments)).Succeeded) return true;
            }
            return false;
        }

        private int Rank(string command)
        {
            for (int i = 0; i < _order.Count; i++)
                if (string.Equals(_order[i], command, StringComparison.Ordinal)) return i;
            return _order.Count;
        }
    }
}
