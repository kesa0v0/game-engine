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

        /// <summary>
        /// 그릴 엔티티의 그림과 자리 (G401·G407). <paramref name="since"/>가 0이면 전부, 아니면 그 도장 뒤에 바뀐 것과 빠진 것
        /// (사라짐, 안 보이게 됨, <c>look</c>·<c>where</c>가 <c>None</c>, G405). 결과의 <see cref="BridgeWorldObjects.Stamp"/>를 다음에 넘긴다.
        /// </summary>
        ValueTask<BridgeWorldObjects> WorldObjects(long since);

        /// <summary>한 엔티티의 그림과 자리. 그리지 않을 엔티티면 null. 영수증은 <see cref="Call"/>처럼 <paramref name="key"/>에 둔다.</summary>
        ValueTask<BridgeWorldObject?> WorldObject(string key, long entity);

        /// <summary>
        /// <c>@world_marks</c> 함수들이 이 연결에 낸 표시 (G395·G408). <paramref name="since"/>가 0이면 전부, 아니면 그 도장 뒤에 생기거나 바뀐 것과
        /// 없어진 열쇠. 결과의 <see cref="BridgeWorldMarks.Stamp"/>를 다음에 넘긴다. 도장은 <see cref="WorldObjects"/>와 따로 센다.
        /// </summary>
        ValueTask<BridgeWorldMarks> WorldMarks(long since);

        /// <summary>
        /// 도장 <paramref name="since"/> 뒤에 난 신호마다 <c>@world_fx</c> 함수가 이 연결에 낸 효과 (G398·G426). 처음은 0을 넘겨 지금 도장을 받고,
        /// 결과의 <see cref="BridgeWorldFx.Stamp"/>를 다음에 넘긴다. 늦어서 커널이 버린 효과가 있으면 <see cref="BridgeWorldFx.Missed"/>.
        /// </summary>
        ValueTask<BridgeWorldFx> WorldFx(long since);

        /// <summary>
        /// 판 위 입력 (G416·G419·G420·G422): <paramref name="input"/>이 커서 아래 엔티티(<paramref name="entities"/>, 위에서부터)와 자리
        /// (<paramref name="place"/>, <c>at</c> 꼴이고 공간 이름이 있어야 쓰임)에 들어왔다. 커널이 엔티티 타입의, 그다음 그 공간의 <c>@world_input</c>
        /// 화면 동작 가운데 사전 조건을 통과한 첫 것을 하고 <see cref="Act"/>처럼 적힌 <c>submit</c>을 낸다. 모두 막혔으면 처음 막힌 결과, 받을 것이
        /// 없으면 null. 표시의 누름은 호스트가 먼저 본다.
        /// </summary>
        ValueTask<BridgeResult?> WorldInput(string input, IReadOnlyList<long> entities, BridgeWhere? place = null);

        /// <summary>판 위 입력 핸들러(<c>@world_input</c>), 로드 순서대로 (G424). 선언에 없는 입력 이름을 알리거나 키 도움말에 쓴다.</summary>
        ValueTask<IReadOnlyList<BridgeWorldInput>> WorldInputs();

        /// <summary>이 연결에게 보이는 엔티티를 (스키마의 종류 순, id 순)으로, 도장과 함께 (G329).</summary>
        ValueTask<IReadOnlyList<BridgeListed>> Entities();

        /// <summary>그 엔티티의 보이는 컴포넌트와 멤버 값. 없거나 못 보면 null (G329).</summary>
        ValueTask<IReadOnlyList<BridgeComponent>?> Read(long entity);

        /// <summary>이 연결이 보는 열린 질문들 (G200·G234).</summary>
        ValueTask<IReadOnlyList<BridgeRequest>> OpenRequests();

        /// <summary>
        /// 열린 질문 가운데 지금 든 영수증의 화면 함수 어느 것도 <c>questions()</c>로 그 이름을 읽지 않은 것 (G386). 호스트는 이것만 엔진
        /// 기본 창으로 보인다.
        /// </summary>
        ValueTask<IReadOnlyList<BridgeRequest>> UndrawnRequests();

        /// <summary>그 질문에 이 연결이 낼 수 있는 답(선택지 <see cref="BridgeNames.Pick"/>와 응답 커맨드) (G232·G272).</summary>
        ValueTask<BridgeMoves> LegalResponses(long request, int max);

        /// <summary>자유 커맨드의 합법 수 (G184). <paramref name="given"/>은 호스트가 정해 준 인자.</summary>
        ValueTask<BridgeMoves> LegalMoves(string command, IReadOnlyDictionary<string, BridgeValue> given, int max);

        /// <summary>화면 동작의 합법 수 (G280).</summary>
        ValueTask<BridgeMoves> LegalUiMoves(string action, IReadOnlyDictionary<string, BridgeValue> given, int max);

        /// <summary>
        /// 눌러 보지 않고 사전 조건만 본다(회색 버튼과 그 이유, G342). 이름이 커맨드든 화면 동작이든 된다. 상태를 바꾸지 않는다.
        /// 결과의 <see cref="BridgeResult.Outcome"/>은 <c>Passed</c>, <c>ArgumentsRejected</c>, <c>PreconditionsFailed</c>나 오류다.
        /// </summary>
        ValueTask<BridgeResult> Check(string name, IReadOnlyDictionary<string, BridgeValue> arguments);

        /// <summary>세계 변경 도장. 바뀌었으면 보이는 버튼의 <see cref="Check"/>를 다시 한다 (G342).</summary>
        ValueTask<long> Version();

        /// <summary>이 연결의 UI 기록 도장. 화면 동작은 <see cref="Version"/>을 움직이지 않으므로 이것도 보고 버튼을 다시 확인한다 (G345).</summary>
        ValueTask<long> UiVersion();

        /// <summary>
        /// 커맨드를 이름으로 낸다 (G283·G336): 자유 커맨드면 <see cref="Submit"/>, 아니면 이 연결이 답할 수 있고 그 커맨드를 받는 열린 질문
        /// 하나에 답한다. 화면의 버튼이 응답 커맨드(<c>EndTurn</c> 등)를 가리킬 때 쓴다. 낼 곳이 없으면 거절 이유가 든 결과다.
        /// </summary>
        ValueTask<BridgeResult> Deliver(string command, IReadOnlyDictionary<string, BridgeValue> arguments);

        ValueTask<BridgeResult> Submit(string command, IReadOnlyDictionary<string, BridgeValue> arguments);

        ValueTask<BridgeResult> Respond(long request, string command, IReadOnlyDictionary<string, BridgeValue> arguments);

        /// <summary>화면 동작을 하고, 성공하면 적힌 <c>submit</c>을 차례로 낸다(G278·G283). 낸 결과는 <see cref="BridgeResult.Deliveries"/>.</summary>
        ValueTask<BridgeResult> Act(string action, IReadOnlyDictionary<string, BridgeValue> arguments);

        /// <summary>모드의 <c>@bot</c>들 (이름 순, G378).</summary>
        ValueTask<IReadOnlyList<BridgeBot>> Bots();

        /// <summary>
        /// 이 연결로 도는 모드 봇을 만든다 (G378). <paramref name="settings"/>에 빠진 설정은 기본값, <paramref name="seed"/>가 없으면 월드 시드·엔티티·
        /// 봇에서(G373), <paramref name="stepBudget"/>이 없으면 판의 행동 예산(G375). 없는 봇·설정은 <see cref="System.ArgumentException"/>이다.
        /// </summary>
        ValueTask<IScriptBot> Bot(string bot, long entity, IReadOnlyDictionary<string, BridgeValue>? settings = null, long? seed = null,
            int? stepBudget = null);

        /// <summary><see cref="IScriptBot.Save"/>한 봇을 이 연결로 되살린다. 버린 것의 경고는 <see cref="IScriptBot.LastProblems"/>에 있다.</summary>
        ValueTask<IScriptBot> LoadBot(byte[] saved, int? stepBudget = null);
    }
}
