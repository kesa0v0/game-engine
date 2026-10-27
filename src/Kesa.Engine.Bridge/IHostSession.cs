using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kesa.Engine.Bridge
{
    /// <summary>
    /// 연결 세션 (spec-gaps G325): 연결 하나의 눈과 손. 그 연결이 보는 세계(<c>ViewAs</c>)로만 읽고 그 연결로만 낸다.
    /// UI 기록(G279)과 화면 함수의 영수증(G246)은 세션이 든다. 호출은 비동기이고(G327) 한 세션의 요청은 낸 순서대로 처리된다:
    /// <see cref="Submit"/> 뒤에 낸 <see cref="Call"/>은 바뀐 뒤를 본다.
    /// </summary>
    public interface IHostSession
    {
        /// <summary>연결 꼬리표.</summary>
        string Connection { get; }

        /// <summary>이 연결이 앉은 엔티티들 (id 순).</summary>
        ValueTask<IReadOnlyList<BridgeEntity>> Seats();

        ValueTask<long> Tick();

        /// <summary>모든 커맨드 (이름 순). 질문 없이 낼 수 있는 것은 <see cref="BridgeCommand.Free"/>.</summary>
        ValueTask<IReadOnlyList<BridgeCommand>> Commands();

        /// <summary>모든 화면 동작 (이름 순).</summary>
        ValueTask<IReadOnlyList<BridgeCommand>> UiActions();

        /// <summary>UI 파일을 읽을 때 화면 함수 이름을 미리 본다 (G249): 괜찮으면 null, 아니면 "혹시 …?"가 든 이유.</summary>
        ValueTask<string?> CheckCall(string function, int argumentCount);

        /// <summary>
        /// 화면 함수를 이 세션의 UI 기록과 함께 부르고(G285) 영수증을 <paramref name="key"/>에 둔다(같은 키면 갈아 끼움).
        /// 키는 호스트(해석기)가 붙이는 이름이다.
        /// </summary>
        ValueTask<BridgeCall> Call(string key, string function, IReadOnlyList<BridgeValue> arguments);

        /// <summary>
        /// 다시 불러야 하는 키 (이름 순): 영수증이 바뀌었거나, 지난 부르기가 실패했거나, 판을 불러와 갈아 끼웠다.
        /// </summary>
        ValueTask<IReadOnlyList<string>> Changed();

        /// <summary>그 키의 영수증을 놓는다 (패널을 닫음).</summary>
        ValueTask Unbind(string key);

        /// <summary>이 연결에게 보이는 엔티티를 (스키마의 종류 순, id 순)으로, 도장과 함께 (G329).</summary>
        ValueTask<IReadOnlyList<BridgeListed>> Entities();

        /// <summary>그 엔티티의 보이는 컴포넌트와 멤버 값. 없거나 못 보면 null (G329).</summary>
        ValueTask<IReadOnlyList<BridgeComponent>?> Read(long entity);

        /// <summary>이 연결이 보는 열린 질문들 (G200·G234).</summary>
        ValueTask<IReadOnlyList<BridgeRequest>> OpenRequests();

        /// <summary>그 질문에 이 연결이 낼 수 있는 답(선택지 <see cref="BridgeNames.Pick"/>와 응답 커맨드) (G232·G272).</summary>
        ValueTask<BridgeMoves> LegalResponses(long request, int max);

        /// <summary>자유 커맨드의 합법 수 (G184). <paramref name="given"/>은 호스트가 정해 준 인자.</summary>
        ValueTask<BridgeMoves> LegalMoves(string command, IReadOnlyDictionary<string, BridgeValue> given, int max);

        /// <summary>화면 동작의 합법 수 (G280).</summary>
        ValueTask<BridgeMoves> LegalUiMoves(string action, IReadOnlyDictionary<string, BridgeValue> given, int max);

        ValueTask<BridgeResult> Submit(string command, IReadOnlyDictionary<string, BridgeValue> arguments);

        ValueTask<BridgeResult> Respond(long request, string command, IReadOnlyDictionary<string, BridgeValue> arguments);

        /// <summary>화면 동작을 하고, 성공하면 적힌 <c>submit</c>을 차례로 낸다(G278·G283). 낸 결과는 <see cref="BridgeResult.Deliveries"/>.</summary>
        ValueTask<BridgeResult> Act(string action, IReadOnlyDictionary<string, BridgeValue> arguments);
    }
}
