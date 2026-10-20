using System.Collections.Generic;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 열린 질문 (spec X1, spec-gaps G125·G126·G137·G138). 멈춘 Flow 하나가 질문 하나를 들고 있다. 값은 모두 저장 가능한 모양이다
    /// (엔티티는 <see cref="EntityValue"/>).
    /// </summary>
    public sealed class Request
    {
        internal Request(long id, long flow, string command, IReadOnlyList<EntityId> who, Value title,
            IReadOnlyList<OptionValue> choices, IReadOnlyList<string> responses, IReadOnlyList<string> responseNames,
            string? hint, Value data)
        {
            Id = id;
            Flow = flow;
            Command = command;
            Who = who;
            Title = title;
            Choices = choices;
            Responses = responses;
            ResponseNames = responseNames;
            Hint = hint;
            Data = data;
        }

        /// <summary>받는 응답 커맨드의 id (<c>모드id.함수이름</c>, G138·G139). 응답은 <see cref="World.Respond"/>로 낸다.</summary>
        public IReadOnlyList<string> Responses { get; }

        /// <summary><see cref="Responses"/>를 <c>ask</c>에 적은 그대로 (<c>r.response</c>로 돌려준다).</summary>
        internal IReadOnlyList<string> ResponseNames { get; }

        /// <summary>질문 번호. 시뮬레이션 상태 안의 카운터다 (X1). 답은 이 번호로 낸다.</summary>
        public long Id { get; }

        /// <summary>질문을 들고 멈춘 Flow의 번호.</summary>
        public long Flow { get; }

        /// <summary>Flow를 만든 커맨드 id, 또는 신호 핸들러가 만든 Flow면 핸들러 id (둘 다 <c>모드id.함수이름</c>, S5).</summary>
        public string Command { get; }

        /// <summary>답할 수 있는 엔티티 (G125). 질문이 열릴 때 굳는다.</summary>
        public IReadOnlyList<EntityId> Who { get; }

        /// <summary>제목: 텍스트 키(<see cref="StrValue"/>)나 <see cref="TextValue"/>.</summary>
        public Value Title { get; }

        /// <summary>선택지 (G138). 맨 값도 option으로 감싸 둔다 (G271). 어떻게 보일지는 UI가 정한다 (G137).</summary>
        public IReadOnlyList<OptionValue> Choices { get; }

        /// <summary>선택 방법의 제안. 커널은 해석하지 않는다 (G137).</summary>
        public string? Hint { get; }

        /// <summary>UI가 읽을 자유 데이터 (<see cref="DictValue"/>, 없으면 <see cref="NoneValue"/>, G137).</summary>
        public Value Data { get; }

        /// <summary>열려 있는 동안 <see cref="World.Advance"/>가 시간을 흘리지 않는다 (spec X2, spec-gaps G159).</summary>
        public bool BlocksTime { get; internal set; } = true;

        /// <summary>마감 틱 (spec X2, spec-gaps G160). 질문이 열린 틱 + <c>deadline=</c>. 마감이 없으면 null.</summary>
        public long? Deadline { get; internal set; }

        /// <summary>마감이 지나면 엔진이 고를 선택지 번호 (<c>on_timeout=</c>, spec-gaps G161). 답한 엔티티는 없다.</summary>
        public long? TimeoutChoice { get; internal set; }

        /// <summary>마감이 지나면 엔진이 낼 응답의 <see cref="Responses"/> 번호 (G161). 응답 커맨드의 본문은 돌지 않는다.</summary>
        public long? TimeoutResponse { get; internal set; }

        /// <summary>마감이 지나면 기본 답이 들어간다.</summary>
        public bool TimesOut => Deadline != null && (TimeoutChoice != null || TimeoutResponse != null);
    }
}
