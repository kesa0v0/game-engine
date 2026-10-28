using System.Threading.Tasks;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// AI 자리의 판단 (G366): 그 자리의 세션으로만 읽고 낸다. 게임 C#이든 모드 스크립트를 부르는 것이든 여기에 끼운다.
    /// </summary>
    public interface IBotPolicy
    {
        /// <summary>할 일을 하나 하고 참, 할 일이 없으면 거짓.</summary>
        ValueTask<bool> Act(IHostSession me);
    }
}
