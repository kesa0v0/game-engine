using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// 운영 (spec-gaps G325): 판 주인만 가진다. 세션 열기, 자리(G236·G239), 틱, 저장·불러오기. 원격 플레이어는 이것을 받지 않는다.
    /// 호출은 비동기다 (G327).
    /// </summary>
    public interface IHostOperator
    {
        /// <summary>그 연결의 세션. 같은 연결이면 같은 세션이다(UI 기록과 영수증을 함께 쓴다).</summary>
        ValueTask<IHostSession> Open(string connection);

        /// <summary>앉을 후보(<c>Seatable</c>)와 지금 앉은 연결, id 순.</summary>
        ValueTask<IReadOnlyList<BridgeSeat>> SeatChoices();

        /// <summary>엔티티에 연결을 앉힌다. <paramref name="ai"/>면 스크립트의 <c>controller(e)</c>가 <c>"ai"</c> (G241).</summary>
        ValueTask Seat(long entity, string connection, bool ai = false);

        ValueTask Unseat(long entity);

        ValueTask<BridgeAdvance> Advance(int ticks);

        ValueTask<byte[]> Save();

        /// <summary>저장을 불러와 판을 갈아 끼운다. 모든 세션의 키는 다시 부를 것이 된다.</summary>
        ValueTask<BridgeLoad> Load(byte[] save);
    }
}
