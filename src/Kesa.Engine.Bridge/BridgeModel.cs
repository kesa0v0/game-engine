using System;
using System.Collections.Generic;

namespace Kesa.Engine.Bridge
{
    /// <summary>엔진이 정한 이름 중 호스트가 알아야 하는 것.</summary>
    public static class BridgeNames
    {
        /// <summary>선택지 답의 엔진 예약 커맨드 (spec-gaps G272·G273): 인자 <see cref="PickChoice"/>는 선택지 번호.</summary>
        public const string Pick = "engine.pick";
        public const string PickChoice = "choice";
    }

    /// <summary>거절·실패 사유 하나의 종류.</summary>
    public enum BridgeReasonKind
    {
        /// <summary>실행 오류나 엔진의 거절(코드와 영어 메시지).</summary>
        Error,
        /// <summary>인자가 틀림(매개변수와 메시지).</summary>
        Argument,
        /// <summary><c>require</c> 실패(모드의 메시지 키와 인자, 번역할 글).</summary>
        Require,
        /// <summary><c>cost</c>가 모자람(칸).</summary>
        Cost,
        /// <summary><c>choose</c>의 후보가 아님(매개변수).</summary>
        Choose,
        /// <summary>화면 동작의 <c>submit</c>을 낼 곳이 없음 (G283).</summary>
        Refusal,
    }

    /// <summary>거절·실패 사유 하나. 모드가 준 글이면 <see cref="Text"/>, 엔진의 글이면 <see cref="Message"/>.</summary>
    public sealed class BridgeReason
    {
        public BridgeReason(BridgeReasonKind kind, BridgeText? text = null, string? message = null, string? parameter = null,
            string? slot = null, string? code = null)
        {
            Kind = kind;
            Text = text;
            Message = message;
            Parameter = parameter;
            Slot = slot;
            Code = code;
        }

        public BridgeReasonKind Kind { get; }
        public BridgeText? Text { get; }
        public string? Message { get; }
        public string? Parameter { get; }
        public string? Slot { get; }
        public string? Code { get; }

        public override string ToString() =>
            Kind + (Code == null ? "" : " " + Code) + (Text == null ? "" : " " + Text) + (Message == null ? "" : ": " + Message) +
            (Parameter == null ? "" : " (" + Parameter + ")") + (Slot == null ? "" : " [" + Slot + "]");
    }

    /// <summary>
    /// 커맨드·응답·화면 동작을 낸 결과. <see cref="Outcome"/>은 커널 결과의 이름(<c>Succeeded</c>, <c>PreconditionsFailed</c> 등)이다.
    /// 화면 동작이면 적힌 <c>submit</c>을 낸 결과가 <see cref="Deliveries"/>에 차례로 든다.
    /// </summary>
    public sealed class BridgeResult
    {
        public BridgeResult(bool succeeded, string outcome, IReadOnlyList<BridgeReason>? reasons = null,
            IReadOnlyList<BridgeDelivery>? deliveries = null)
        {
            Succeeded = succeeded;
            Outcome = outcome;
            Reasons = reasons ?? Array.Empty<BridgeReason>();
            Deliveries = deliveries ?? Array.Empty<BridgeDelivery>();
        }

        public bool Succeeded { get; }
        public string Outcome { get; }
        public IReadOnlyList<BridgeReason> Reasons { get; }
        public IReadOnlyList<BridgeDelivery> Deliveries { get; }

        public override string ToString() => Outcome + (Reasons.Count == 0 ? "" : ": " + string.Join("; ", Reasons));
    }

    /// <summary>화면 동작이 적은 <c>submit</c> 하나와 그것을 낸 결과.</summary>
    public sealed class BridgeDelivery
    {
        public BridgeDelivery(string command, IReadOnlyDictionary<string, BridgeValue> arguments, BridgeResult result)
        {
            Command = command;
            Arguments = arguments;
            Result = result;
        }

        public string Command { get; }
        public IReadOnlyDictionary<string, BridgeValue> Arguments { get; }
        public BridgeResult Result { get; }
    }

    /// <summary>화면 함수를 부른 결과: 값이나 오류 (G247). 영수증은 세션이 묶음 키에 들고 있다 (G246).</summary>
    public sealed class BridgeCall
    {
        public BridgeCall(BridgeValue? value, string? error)
        {
            Value = value;
            Error = error;
        }

        public BridgeValue? Value { get; }
        public string? Error { get; }
        public bool Succeeded => Error == null;
    }

    /// <summary>그 연결이 보는 열린 질문 하나 (G234: <see cref="Who"/>는 그 연결이 답할 수 있는 대상만).</summary>
    public sealed class BridgeRequest
    {
        public BridgeRequest(long id, IReadOnlyList<string> responses, IReadOnlyList<BridgeEntity> who, BridgeValue title,
            IReadOnlyList<BridgeOption> choices, string? hint, BridgeValue data, bool blocksTime, long? deadline)
        {
            Id = id;
            Responses = responses;
            Who = who;
            Title = title;
            Choices = choices;
            Hint = hint;
            Data = data;
            BlocksTime = blocksTime;
            Deadline = deadline;
        }

        public long Id { get; }
        public IReadOnlyList<string> Responses { get; }
        public IReadOnlyList<BridgeEntity> Who { get; }
        /// <summary>제목: 텍스트 키(문자열)나 글.</summary>
        public BridgeValue Title { get; }
        public IReadOnlyList<BridgeOption> Choices { get; }
        public string? Hint { get; }
        public BridgeValue Data { get; }
        public bool BlocksTime { get; }
        public long? Deadline { get; }
    }

    /// <summary>합법 수 하나. 응답이면 <see cref="By"/>가 답하는 엔티티다.</summary>
    public sealed class BridgeMove
    {
        public BridgeMove(string command, IReadOnlyDictionary<string, BridgeValue> arguments, BridgeEntity? by = null)
        {
            Command = command;
            Arguments = arguments;
            By = by;
        }

        public string Command { get; }
        public IReadOnlyDictionary<string, BridgeValue> Arguments { get; }
        public BridgeEntity? By { get; }
    }

    /// <summary>호스트가 먼저 정해 줘야 나열할 수 있는 응답 커맨드와 그 매개변수.</summary>
    public sealed class BridgeNeeds
    {
        public BridgeNeeds(string command, IReadOnlyList<string> parameters)
        {
            Command = command;
            Parameters = parameters;
        }

        public string Command { get; }
        public IReadOnlyList<string> Parameters { get; }
    }

    /// <summary>합법 수 목록. <see cref="Problems"/>는 나열하지 못한 이유(정해 준 인자가 틀림, 나열 중 오류).</summary>
    public sealed class BridgeMoves
    {
        public BridgeMoves(IReadOnlyList<BridgeMove> moves, bool truncated, IReadOnlyList<BridgeNeeds>? needs = null,
            IReadOnlyList<BridgeReason>? problems = null)
        {
            Moves = moves;
            Truncated = truncated;
            Needs = needs ?? Array.Empty<BridgeNeeds>();
            Problems = problems ?? Array.Empty<BridgeReason>();
        }

        public IReadOnlyList<BridgeMove> Moves { get; }
        public bool Truncated { get; }
        public IReadOnlyList<BridgeNeeds> Needs { get; }
        public IReadOnlyList<BridgeReason> Problems { get; }
    }

    /// <summary>커맨드나 화면 동작 하나의 이름과 매개변수.</summary>
    public sealed class BridgeCommand
    {
        public BridgeCommand(string name, bool free, IReadOnlyList<BridgeParameter> parameters)
        {
            Name = name;
            Free = free;
            Parameters = parameters;
        }

        public string Name { get; }
        /// <summary>질문 없이 언제든 낼 수 있다 (<c>free=True</c>).</summary>
        public bool Free { get; }
        public IReadOnlyList<BridgeParameter> Parameters { get; }
    }

    public sealed class BridgeParameter
    {
        public BridgeParameter(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }
        /// <summary>스크립트에 적힌 타입(<c>Ref[Player]</c> 등).</summary>
        public string Type { get; }
    }

    /// <summary>앉을 후보 엔티티 하나와 지금 앉은 연결(없으면 null) (G239).</summary>
    public sealed class BridgeSeat
    {
        public BridgeSeat(BridgeEntity entity, string? connection)
        {
            Entity = entity;
            Connection = connection;
        }

        public BridgeEntity Entity { get; }
        public string? Connection { get; }
    }

    /// <summary>틱 진행 결과. 시간을 막는 질문이 열려 있으면 <see cref="Blocked"/>.</summary>
    public sealed class BridgeAdvance
    {
        public BridgeAdvance(int advanced, long tick, bool blocked)
        {
            Advanced = advanced;
            Tick = tick;
            Blocked = blocked;
        }

        public int Advanced { get; }
        public long Tick { get; }
        public bool Blocked { get; }
    }

    /// <summary>불린 모드 하나: 호스트가 번역 파일(<c>locale/</c>)과 UI 파일을 찾는 곳 (G256·G269).</summary>
    public sealed class BridgeMod
    {
        public BridgeMod(string id, string directory, string folder, string? defaultLanguage)
        {
            Id = id;
            Directory = directory;
            Folder = folder;
            DefaultLanguage = defaultLanguage;
        }

        public string Id { get; }
        /// <summary>모드 뿌리 아래의 폴더 이름 (진단에 보이는 경로).</summary>
        public string Directory { get; }
        /// <summary>모드 폴더의 실제 경로.</summary>
        public string Folder { get; }
        public string? DefaultLanguage { get; }
    }

    /// <summary>판의 내용 중 호스트가 알아야 하는 것: 로드 순서대로의 모드와 Def의 전체 이름(이름 순).</summary>
    public sealed class BridgeContent
    {
        public BridgeContent(IReadOnlyList<BridgeMod> mods, IReadOnlyList<string> defs)
        {
            Mods = mods;
            Defs = defs;
        }

        public IReadOnlyList<BridgeMod> Mods { get; }
        /// <summary>문자열 값이 이 중 하나와 같으면 번역기가 그 Def의 이름을 보인다 (G267·G270).</summary>
        public IReadOnlyList<string> Defs { get; }
    }

    /// <summary>저장 불러오기 결과. 실패하면 판은 그대로이고 <see cref="Problems"/>에 이유.</summary>
    public sealed class BridgeLoad
    {
        public BridgeLoad(bool succeeded, IReadOnlyList<string> problems)
        {
            Succeeded = succeeded;
            Problems = problems;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<string> Problems { get; }
    }
}
