using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// 세션이 만든 모드 봇 (G378). <see cref="IBotPolicy.Act"/>는 한 번 생각하고 적은 수를 그 연결로 차례로 내며, 하나라도 받아들여지면
    /// 참이다(G377). 기억은 <see cref="Save"/>로 호스트가 저장 파일에 넣고 <see cref="IHostSession.LoadBot"/>로 되살린다(G372).
    /// </summary>
    public interface IScriptBot : IBotPolicy
    {
        /// <summary><c>@bot</c> id.</summary>
        string Bot { get; }

        /// <summary>맡은 엔티티.</summary>
        long Entity { get; }

        /// <summary>
        /// 지난 <see cref="IBotPolicy.Act"/>의 문제: 스크립트 오류·예산 초과, 맡은 엔티티가 없음, 거절된 수의 이유. 되살린 직후에는
        /// 되살리며 버린 것의 경고(코드 <c>bot-load</c>).
        /// </summary>
        IReadOnlyList<BridgeReason> LastProblems { get; }

        ValueTask<byte[]> Save();
    }
}
