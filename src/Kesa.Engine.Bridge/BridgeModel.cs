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

    /// <summary>
    /// 월드 물체 하나 (G401·G407): 엔티티와 그 그림·자리. 칸 함수가 실패했으면 <see cref="Look"/>·<see cref="Where"/>가 null이고
    /// <see cref="Error"/>가 이유다(호스트는 대체 그림과 경고 한 번, G392).
    /// </summary>
    public sealed class BridgeWorldObject
    {
        public BridgeWorldObject(BridgeEntity entity, BridgeLook? look, BridgeWhere? where, string? error)
        {
            Entity = entity;
            Look = look;
            Where = where;
            Error = error;
        }

        public BridgeEntity Entity { get; }
        public BridgeLook? Look { get; }
        public BridgeWhere? Where { get; }
        public string? Error { get; }

        public override bool Equals(object? obj) =>
            obj is BridgeWorldObject o && o.Entity.Equals(Entity) && Equals(o.Look, Look) && Equals(o.Where, Where) && o.Error == Error;

        public override int GetHashCode() => Entity.GetHashCode();
    }

    /// <summary><see cref="IHostSession.WorldObjects"/>의 결과: 바뀐 것(id 순), 빠진 엔티티 id(순서대로), 다음에 넘길 도장.</summary>
    public sealed class BridgeWorldObjects
    {
        public BridgeWorldObjects(long stamp, IReadOnlyList<BridgeWorldObject> changed, IReadOnlyList<long> gone)
        {
            Stamp = stamp;
            Changed = changed;
            Gone = gone;
        }

        public long Stamp { get; }
        public IReadOnlyList<BridgeWorldObject> Changed { get; }
        public IReadOnlyList<long> Gone { get; }
    }

    /// <summary>
    /// <c>@world_marks</c>가 낸 표시 하나 (G395·G396·G408). <see cref="Key"/>는 견주기용 열쇠로 호스트는 뜻을 읽지 않는다: 같은 열쇠면 같은 물체를
    /// 고치고 없어지면 지운다. 표시 함수가 실패했으면 그 함수의 표시는 이것 하나이고 <see cref="Look"/>·<see cref="Where"/>가 null, <see cref="Error"/>가
    /// 이유다(호스트는 경고만, 그릴 자리가 없다).
    /// </summary>
    public sealed class BridgeMark
    {
        public BridgeMark(string key, BridgeLook? look, BridgeWhere? where, BridgeMarkClick? click, string? error,
            IReadOnlyDictionary<string, BridgeMarkClick>? inputs = null)
        {
            Inputs = inputs ?? new SortedDictionary<string, BridgeMarkClick>(StringComparer.Ordinal);
            Key = key;
            Look = look;
            Where = where;
            Click = click;
            Error = error;
        }

        public string Key { get; }
        public BridgeLook? Look { get; }
        public BridgeWhere? Where { get; }

        /// <summary>누르면 낼 화면 동작. null이면 보이기만 한다(누름은 아래 엔티티나 자리로 넘어감).</summary>
        public BridgeMarkClick? Click { get; }

        /// <summary>
        /// 입력 이름마다 낼 화면 동작 (<c>inputs=</c>, G418, 이름 순). 주 누름은 <see cref="Click"/>이 먼저이고, 둘 다 있으면 호스트가 경고하고
        /// <see cref="Click"/>을 쓴다.
        /// </summary>
        public IReadOnlyDictionary<string, BridgeMarkClick> Inputs { get; }

        public string? Error { get; }

        public override bool Equals(object? obj) =>
            obj is BridgeMark o && o.Key == Key && Equals(o.Look, Look) && Equals(o.Where, Where) && Equals(o.Click, Click) && o.Error == Error &&
            SameInputs(o.Inputs, Inputs);

        private static bool SameInputs(IReadOnlyDictionary<string, BridgeMarkClick> a, IReadOnlyDictionary<string, BridgeMarkClick> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var pair in a)
                if (!b.TryGetValue(pair.Key, out var other) || !Equals(other, pair.Value)) return false;
            return true;
        }

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
    }

    /// <summary>표시를 누르면 낼 화면 동작: <see cref="IHostSession.Act"/>에 그대로 넘긴다.</summary>
    public sealed class BridgeMarkClick
    {
        public BridgeMarkClick(string action, IReadOnlyDictionary<string, BridgeValue> arguments)
        {
            Action = action;
            Arguments = arguments;
        }

        public string Action { get; }

        /// <summary>이름 순.</summary>
        public IReadOnlyDictionary<string, BridgeValue> Arguments { get; }

        public override bool Equals(object? obj)
        {
            if (!(obj is BridgeMarkClick o) || o.Action != Action || o.Arguments.Count != Arguments.Count) return false;
            foreach (var pair in Arguments)
                if (!o.Arguments.TryGetValue(pair.Key, out var v) || !Equals(v, pair.Value)) return false;
            return true;
        }

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Action);
    }

    /// <summary><see cref="IHostSession.WorldMarks"/>의 결과: 바뀐 표시(열쇠 순), 없어진 열쇠(순서대로), 다음에 넘길 도장.</summary>
    public sealed class BridgeWorldMarks
    {
        public BridgeWorldMarks(long stamp, IReadOnlyList<BridgeMark> changed, IReadOnlyList<string> gone)
        {
            Stamp = stamp;
            Changed = changed;
            Gone = gone;
        }

        public long Stamp { get; }
        public IReadOnlyList<BridgeMark> Changed { get; }
        public IReadOnlyList<string> Gone { get; }
    }

    /// <summary>
    /// 도장 뒤에 난 효과 (G426). <see cref="Missed"/>면 커널이 더 들고 있지 않은 효과를 건너뛰었다. 처음(도장 0)은 효과 없이 도장만 온다.
    /// </summary>
    public sealed class BridgeWorldFx
    {
        public BridgeWorldFx(long stamp, bool missed, IReadOnlyList<BridgeFxBatch> batches)
        {
            Stamp = stamp;
            Missed = missed;
            Batches = batches;
        }

        public long Stamp { get; }
        public bool Missed { get; }

        /// <summary>커맨드 묶음 순서대로 (G399).</summary>
        public IReadOnlyList<BridgeFxBatch> Batches { get; }
    }

    /// <summary>한 커맨드 묶음에서 난 효과, 신호가 난 순서대로.</summary>
    public sealed class BridgeFxBatch
    {
        public BridgeFxBatch(long batch, IReadOnlyList<BridgeFx> effects)
        {
            Batch = batch;
            Effects = effects;
        }

        public long Batch { get; }
        public IReadOnlyList<BridgeFx> Effects { get; }
    }

    /// <summary><c>fx(그림, at=자리)</c> 하나. 효과 함수가 실패하면 그림·자리 없이 <see cref="Error"/>가 온다.</summary>
    public sealed class BridgeFx
    {
        public BridgeFx(string function, BridgeLook? look, BridgeWhere? where, string? error, long signal = 0)
        {
            Signal = signal;
            Function = function;
            Look = look;
            Where = where;
            Error = error;
        }

        /// <summary>낸 <c>@world_fx</c> 함수 (<c>모드.함수</c>).</summary>
        public string Function { get; }

        public BridgeLook? Look { get; }
        public BridgeWhere? Where { get; }
        public string? Error { get; }

        /// <summary>효과가 난 신호의 번호. 같으면 같은 신호이고 <c>staged</c>는 신호마다 한 단계다 (G430).</summary>
        public long Signal { get; }
    }

    /// <summary>그 연결이 보는 열린 질문 하나 (G234: <see cref="Who"/>는 그 연결이 답할 수 있는 대상만).</summary>
    public sealed class BridgeRequest
    {
        public BridgeRequest(long id, string name, IReadOnlyList<string> responses, IReadOnlyList<BridgeEntity> who, BridgeValue title,
            IReadOnlyList<BridgeOption> choices, BridgeValue data, bool blocksTime, long? deadline)
        {
            Id = id;
            Name = name;
            Responses = responses;
            Who = who;
            Title = title;
            Choices = choices;
            Data = data;
            BlocksTime = blocksTime;
            Deadline = deadline;
        }

        public long Id { get; }
        /// <summary>질문 종류의 이름 (<c>ask(name=)</c>, G382). 같은 이름의 질문이 동시에 여럿일 수 있다.</summary>
        public string Name { get; }
        public IReadOnlyList<string> Responses { get; }
        public IReadOnlyList<BridgeEntity> Who { get; }
        /// <summary>제목: 텍스트 키(문자열)나 글.</summary>
        public BridgeValue Title { get; }
        public IReadOnlyList<BridgeOption> Choices { get; }
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

    /// <summary><c>@world_input</c> 핸들러 하나 (G424). <see cref="Target"/>은 Def 타입이나 공간 이름(G422)이다.</summary>
    public sealed class BridgeWorldInput
    {
        public BridgeWorldInput(string input, string target, string action, string mod)
        {
            Input = input;
            Target = target;
            Action = action;
            Mod = mod;
        }

        public string Input { get; }
        public string Target { get; }
        /// <summary>받는 화면 동작(<c>모드.동작</c>).</summary>
        public string Action { get; }
        public string Mod { get; }
    }

    public sealed class BridgeParameter
    {
        public BridgeParameter(string name, string type, BridgeValue? @default = null)
        {
            Name = name;
            Type = type;
            Default = @default;
        }

        public string Name { get; }
        /// <summary>스크립트에 적힌 타입(<c>Ref[Player]</c> 등).</summary>
        public string Type { get; }
        /// <summary>선언한 기본값. 없으면 null.</summary>
        public BridgeValue? Default { get; }
    }

    /// <summary>모드의 <c>@bot</c> 하나: 맡을 엔티티의 타입과 호스트가 넘길 수 있는 설정 (G370·G378).</summary>
    public sealed class BridgeBot
    {
        public BridgeBot(string name, string entity, IReadOnlyList<BridgeParameter> settings)
        {
            Name = name;
            Entity = entity;
            Settings = settings;
        }

        public string Name { get; }
        /// <summary>첫 매개변수의 타입(<c>Ref[Party]</c> 등).</summary>
        public string Entity { get; }
        /// <summary>첫 매개변수 뒤의 매개변수. 모두 기본값이 있다.</summary>
        public IReadOnlyList<BridgeParameter> Settings { get; }
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

    /// <summary>보이는 엔티티 하나와 그 도장 (G329). 도장이 그대로면 <see cref="IHostSession.Read"/>의 결과도 같다. 단 <see cref="Volatile"/>이면 아니다.</summary>
    public sealed class BridgeListed
    {
        public BridgeListed(BridgeEntity entity, long version, bool isVolatile)
        {
            Entity = entity;
            Version = version;
            Volatile = isVolatile;
        }

        public BridgeEntity Entity { get; }
        public long Version { get; }
        /// <summary>남의 엔티티를 읽는 <c>derived</c> 칸이 있어 도장이 바뀜을 다 담지 못한다 (G251).</summary>
        public bool Volatile { get; }
    }

    /// <summary>엔티티에 붙은, 보이는 컴포넌트 하나: 멤버 이름과 값 (스키마의 선언 순) (G329).</summary>
    public sealed class BridgeComponent
    {
        public BridgeComponent(string name, IReadOnlyList<KeyValuePair<string, BridgeValue>> members)
        {
            Name = name;
            Members = members;
        }

        public string Name { get; }
        public IReadOnlyList<KeyValuePair<string, BridgeValue>> Members { get; }
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

    /// <summary>연출 방식 이름 (G429).</summary>
    public static class BridgePresentation
    {
        /// <summary>결과를 바로 그리고 효과는 얹는다.</summary>
        public const string Instant = "instant";

        /// <summary>결과를 붙잡고 이번 커맨드 묶음의 효과를 순서대로 보여 준 뒤 적용한다.</summary>
        public const string Staged = "staged";
    }

    /// <summary>불린 모드 하나: 호스트가 번역 파일(<c>locale/</c>)과 UI 파일을 찾는 곳 (G256·G269).</summary>
    public sealed class BridgeMod
    {
        public BridgeMod(string id, string directory, string folder, string? defaultLanguage, string? presentation = null)
        {
            Presentation = presentation;
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

        /// <summary><c>Mod(presentation=…)</c>: <c>instant</c>·<c>staged</c> 또는 적지 않음 (G429).</summary>
        public string? Presentation { get; }
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

        /// <summary>연출 방식 (G399·G429): 로드 순서가 뒤인 모드가 적은 것, 아무도 안 적으면 <see cref="BridgePresentation.Instant"/>.</summary>
        public string Presentation
        {
            get
            {
                for (int i = Mods.Count - 1; i >= 0; i--)
                    if (Mods[i].Presentation != null) return Mods[i].Presentation!;
                return BridgePresentation.Instant;
            }
        }

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
