using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>모드 이름 공간(<see cref="ModScope"/>)들을 함수 본문의 전역 이름 조회로 보여 주는 어댑터.</summary>
    public sealed class ScopeGlobals : IScriptGlobals
    {
        private readonly IReadOnlyDictionary<string, ModScope> _scopes;

        public ScopeGlobals(IReadOnlyDictionary<string, ModScope> scopes) => _scopes = scopes;

        public Value? Lookup(string mod, string name) =>
            _scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var value, out _) ? value : null;
    }

    /// <summary>
    /// 순수 함수 실행기 (spec S2·S4, spec-gaps G95~G103). 함수 본문을 평가해 값을 돌려준다.
    /// 읽기만 하고(대입은 지역 변수뿐), 평가 노드마다 단계를 세어 한 번의 <see cref="Run"/>이
    /// 예산을 넘으면 오류를 낸다(G101). 함수 호출 체인에 같은 함수가 다시 나오면 재귀 오류다(G102).
    /// 오류는 <see cref="ScriptRuntimeException"/>이고 오류가 난 식의 위치와 호출 흔적이 붙는다.
    /// 호스트가 읽기 도중 다시 <see cref="Run"/>을 불러도 된다: 호출 체인은 새로 시작하고, 단계는 넘겨준
    /// <see cref="StepMeter"/>가 있으면 이어서 센다.
    /// <para>
    /// 구조 (G226): ① 풀기 — 함수마다 지역 변수 칸 배치(<see cref="FrameLayout"/>), 연산자 번호(<see cref="Operators"/>). ② 값 연산
    /// (<see cref="PureOps"/>). ④ 프레임의 지역 변수는 칸 배열(<see cref="LocalTable"/>). ⑥ 멈춘 실행은 블록 자료(문장 경로)이고 저장 형식은 그대로다.
    /// ⑦ 실행 — 식과 문장을 칸 배치에서 풀어 만든 실행 노드(<c>PureEvaluator.Nodes.cs</c>, <c>PureEvaluator.Statements.cs</c>)가 계산한다.
    /// 나중에 바이트코드로 바꾸면 ⑦만 새로 쓴다.
    /// </para>
    /// </summary>
    public sealed partial class PureEvaluator
    {
        /// <summary>한 번의 실행에 주는 기본 단계 수. 정할 근거 자료가 아직 없어 넉넉하게 둔다 (G101).</summary>
        public const int DefaultStepBudget = 100_000;

        /// <summary>키워드 인자 없음. 읽기만 하므로 하나를 나눠 쓴다(호출마다 빈 사전을 만들지 않는다).</summary>
        private static readonly IReadOnlyDictionary<string, Value> EmptyKeywords =
            new SortedDictionary<string, Value>(StringComparer.Ordinal);

        private readonly IScriptGlobals _globals;
        private readonly IReadOnlyDictionary<string, Value> _builtins;
        private readonly int _budget;

        /// <summary>
        /// <see cref="Run"/>이 다 쓰고 돌려준 세션. <c>derived</c> 읽기마다 세션과 그 목록들을 새로 만들지 않는다(규모 벤치).
        /// 실행 중에 다시 불린 <see cref="Run"/>(다른 <c>derived</c> 읽기)은 다른 세션을 꺼내므로 겹치지 않는다.
        /// </summary>
        private readonly Stack<Session> _spareSessions = new Stack<Session>();

        public PureEvaluator(IScriptGlobals globals, IReadOnlyDictionary<string, Value> builtins,
            int stepBudget = DefaultStepBudget)
        {
            _globals = globals;
            _builtins = builtins;
            _budget = stepBudget;
        }

        /// <summary>
        /// 함수 값(<c>def</c>나 람다)을 위치 인자로 부른다. <paramref name="meter"/>가 없으면 이 실행만의 예산을 새로 주고,
        /// 있으면 그 계량기의 남은 단계를 쓴다 (최상위 읽기 하나에 예산 하나, G101).
        /// </summary>
        public Value Run(Value function, IReadOnlyList<Value> args, SourceSpan callSite = default, StepMeter? meter = null)
        {
            var session = _spareSessions.Count > 0 ? _spareSessions.Pop() : new Session(this, null);
            session.Begin(meter ?? new StepMeter(_budget));
            try
            {
                return session.Invoke(function, args, EmptyKeywords, callSite);
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite; // 호출 자체의 오류(인자 수 등)는 부른 위치를 가리킨다
                throw;
            }
            finally
            {
                session.Clear();
                _spareSessions.Push(session);
            }
        }

        /// <summary>
        /// 커맨드의 <c>with preconditions:</c>만 평가해 <c>require</c>마다 결과를 돌려준다 (spec-gaps G110·G111).
        /// 인자는 매개변수 이름으로 받는다. 블록이 없으면 빈 목록이다.
        /// </summary>
        public List<RequireOutcome> RunPreconditions(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            SourceSpan callSite = default, StepMeter? meter = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            try
            {
                return session.Preconditions(command, args, callSite);
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite;
                throw;
            }
        }

        /// <summary>
        /// <c>choose</c> 하나의 후보 식(<c>where=</c>의 함수나 <c>from_=</c>의 목록)만 계산한다 (spec-gaps G184). 그 식이 읽는 블록 대입만
        /// 블록 순서대로 실행하고 <c>require</c>·<c>cost</c>·다른 <c>choose</c>는 보지 않는다. 아직 정하지 않은 매개변수는 값이 없어도 된다
        /// (식이 읽지 않는다는 것을 <see cref="ChooseDependencies"/>로 보았다).
        /// </summary>
        public Value RunChooseOption(FunctionValue command, IReadOnlyDictionary<string, Value> args, ChooseInfo choose,
            SourceSpan callSite = default, StepMeter? meter = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            try
            {
                return session.ChooseOption(command, args, choose, callSite);
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite;
                throw;
            }
        }

        /// <summary>
        /// 커맨드를 실행한다: 사전 조건을 평가하고 실패한 <c>require</c>가 없으면 같은 지역 변수로 본문을 이어서 실행한다
        /// (spec-gaps G132). 본문의 속성 대입은 상태 쓰기다. 결과는 <c>require</c>마다의 결과이고, 실패가 있으면 본문은 돌지 않았다.
        /// </summary>
        public List<RequireOutcome> RunCommand(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            SourceSpan callSite = default, StepMeter? meter = null) =>
            RunCommand(command, args, out _, callSite, meter);

        /// <summary>
        /// <see cref="RunCommand(FunctionValue, IReadOnlyDictionary{string, Value}, SourceSpan, StepMeter?)"/>와 같고,
        /// 본문이 문장 수준의 <c>ask</c>에서 멈추면 멈춘 실행을 <paramref name="suspended"/>로 돌려준다 (spec S2 "막힐 때까지 실행").
        /// </summary>
        public List<RequireOutcome> RunCommand(FunctionValue command, IReadOnlyDictionary<string, Value> args,
            out Suspended? suspended, SourceSpan callSite = default, StepMeter? meter = null, Action? bodyStarts = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            try
            {
                var results = session.Command(command, args, callSite, runBody: true, bodyStarts);
                suspended = session.TakeSuspended();
                return results;
            }
            catch (ScriptRuntimeException e) when (e.Span.Line <= 0)
            {
                e.Span = callSite;
                throw;
            }
        }

        /// <summary>
        /// 멈춘 실행을 답 <paramref name="answer"/>로 이어서 다음 막힘이나 끝까지 실행한다. 다시 멈추면 그 실행을 돌려주고, 끝나면 null이다.
        /// <paramref name="from"/>은 바꾸지 않는다(사본에서 실행한다): 실행이 오류로 끝나면 부른 쪽은 그대로 다시 기다릴 수 있다 (C8·C9).
        /// </summary>
        public Suspended? Resume(Suspended from, Value answer, StepMeter? meter = null)
        {
            var session = new Session(this, meter ?? new StepMeter(_budget));
            session.Restore(from, answer);
            session.RunToEnd();
            return session.TakeSuspended();
        }

        /// <summary>
        /// 문장 수준의 <c>ask</c>에서 멈춘 실행 (spec S2 평가 모델: 프레임 스택 전체가 데이터다). 질문의 내용은 <see cref="Request"/>이고
        /// 그 모양은 <c>ask</c>를 준 쪽(월드)이 정한다.
        /// </summary>
        public sealed class Suspended
        {
            internal Suspended(List<Frame> frames, string? answerTo, Value request)
            {
                Frames = frames;
                AnswerTo = answerTo;
                Request = request;
            }

            internal List<Frame> Frames { get; }

            /// <summary>답을 받을 맨 위 프레임의 지역 변수 (<c>r = ask(...)</c>). 한 줄 전체의 <c>ask</c>면 null이다.</summary>
            internal string? AnswerTo { get; }

            /// <summary><c>ask</c> 내장의 몸체가 돌려준 질문의 내용.</summary>
            public Value Request { get; }

            /// <summary>가장 바깥 프레임(커맨드)의 함수 이름.</summary>
            public string Origin => Frames[0].Function.Name;
        }

        /// <summary>
        /// 이름 자리의 풀이 (<see cref="NameExpr.Cache"/>): 어느 칸 배치에서 지역 칸 몇 번인가, 아니면(<see cref="Slot"/> -1) 어느 실행기·모드에서
        /// 찾은 전역·내장 값인가.
        /// </summary>
        private sealed class NameSite
        {
            public NameSite(FrameLayout layout, int slot, PureEvaluator? owner, string? mod, Value? value)
            {
                Layout = layout;
                Slot = slot;
                Owner = owner;
                Mod = mod;
                Value = value;
            }

            public FrameLayout Layout { get; }
            public int Slot { get; }
            public PureEvaluator? Owner { get; }
            public string? Mod { get; }
            public Value? Value { get; }
        }

        /// <summary>이 실행기의 내장 함수 표 (저장이 내장 함수 값을 이름으로 적고 다시 찾는다).</summary>
        internal IReadOnlyDictionary<string, Value> Builtins => _builtins;

        /// <summary>불러온 멈춘 실행의 빈 프레임 (저장, spec S8). 칸 배치는 함수 정의에서 다시 계산한다.</summary>
        internal Frame NewFrame(FunctionValue function) =>
            new Frame(function, new LocalTable(LayoutOf(function.Definition!)));

        /// <summary>
        /// <c>def</c>의 지역 변수 칸 배치. AST에만 기대므로 함수 정의에 한 번 둔다: 같은 콘텐츠를 쓰는 여러 월드가 같은 배치와 실행 노드를 나눠 쓴다.
        /// </summary>
        private static FrameLayout LayoutOf(FunctionDef def) =>
            (FrameLayout)(def.Layout ??= new FrameLayout(FunctionLocals.Of(def)));

        /// <summary>붙잡은 이름 공간 없이 불린 람다의 칸 배치 (매개변수만).</summary>
        private static FrameLayout BareLambdaLayout(LambdaExpr lambda) =>
            (FrameLayout)(lambda.Layout ??= new FrameLayout(FunctionLocals.Parameters(lambda.Parameters)));
        internal sealed class Frame
        {
            public Frame(FunctionValue function, LocalTable locals)
            {
                Function = function;
                Locals = locals;
            }

            /// <summary>
            /// 이어서 실행할 사본. 지역 변수와 블록 위치는 새로 담고, 값은 불변이라(G104) 그대로 나눠 쓴다.
            /// </summary>
            public Frame Copy()
            {
                var copy = new Frame(Function, Locals.Clone())
                {
                    CallSite = CallSite,
                    Writable = Writable,
                    Result = Result,
                    ResultName = ResultName,
                };
                foreach (var block in Blocks) copy.Blocks.Add(block.Copy());
                return copy;
            }

            public FunctionValue Function { get; private set; }
            public LocalTable Locals { get; private set; }

            /// <summary>다시 쓰는 프레임을 새 호출로 채운다.</summary>
            public void Reuse(FunctionValue function, LocalTable locals)
            {
                Function = function;
                Locals = locals;
            }

            /// <summary>끝난 프레임이 붙잡은 것을 놓는다 (다시 쓰기 전).</summary>
            public void Release()
            {
                Function = null!;
                Locals = null!;
                CallSite = default;
                Writable = false;
                Result = ResultTo.Host;
                ResultName = null;
                Returned = NoneValue.Instance;
                Blocks.Clear();
            }

            /// <summary>
            /// 이 함수의 지역 변수 칸 배치 (파이썬 규칙, G104). 여기 든 이름은 값이 아직 없어도 전역·내장으로 넘어가지 않는다.
            /// </summary>
            public FrameLayout Layout => Locals.Layout;

            public SourceSpan CallSite { get; set; }

            /// <summary>
            /// 쓰기 모드 (G117 런타임 안전망). 커맨드 본문, 그리고 쓰기 모드 프레임이 문장으로 부른 쓰기 함수만 참이다.
            /// 순수 함수에서 시작한 호출, 식 안의 호출, <c>derived</c>·사전 조건·<c>Cond</c>는 커맨드 실행 중이라도 순수다.
            /// </summary>
            public bool Writable { get; set; }

            /// <summary>
            /// 실행 중인 블록들, 바깥에서 안쪽 순 (S2 평가 모델: 문장 실행은 C# 호출 스택이 아니라 이 데이터에 있다).
            /// 비면 함수가 끝까지 실행된 것이다.
            /// </summary>
            public List<Block> Blocks { get; } = new List<Block>();

            /// <summary>이 함수가 끝났을 때 돌려준 값을 어디에 둘지.</summary>
            public ResultTo Result { get; set; }

            /// <summary><see cref="ResultTo.Local"/>일 때 부른 쪽 프레임의 지역 변수 이름 (<c>x = f()</c>).</summary>
            public string? ResultName { get; set; }

            /// <summary><see cref="ResultTo.Host"/>일 때 함수가 돌려준 값.</summary>
            public Value Returned { get; set; } = NoneValue.Instance;
        }

        internal enum ResultTo
        {
            /// <summary>C# 쪽(호스트 실행이나 식 안의 호출)이 끝까지 실행하고 <see cref="Frame.Returned"/>를 읽는다.</summary>
            Host,

            /// <summary>문장 하나 전체인 호출: 값을 버린다.</summary>
            Discard,

            /// <summary>이름 대입의 오른쪽 전체인 호출: 부른 쪽 지역 변수에 넣는다.</summary>
            Local,
        }

        /// <summary>
        /// 실행 중인 문장 목록 하나와 다음에 실행할 문장의 번호. <c>for</c>의 몸체면 반복 상태(남은 항목과 위치)도 든다.
        /// </summary>
        internal sealed class Block
        {
            public Block(IReadOnlyList<Stmt> body, int next = 0)
            {
                Body = body;
                Next = next;
            }

            /// <summary>AST 문장 목록 (저장이 문장 경로를 적고 다시 찾는다).</summary>
            public IReadOnlyList<Stmt> Body { get; private set; }

            /// <summary>
            /// <see cref="Body"/>를 이 프레임의 칸 배치에서 풀어 둔 실행 노드 (G226). 실행 고리가 채운다: 불러온 저장의 블록은 처음 실행할 때 찾는다.
            /// </summary>
            internal StmtNode[]? Code { get; set; }

            public int Next { get; set; }

            /// <summary>다시 쓰는 블록을 새 몸체로 채운다 (나머지는 <see cref="Release"/>가 비웠다).</summary>
            internal void Reuse(IReadOnlyList<Stmt> body, StmtNode[] code)
            {
                Body = body;
                Code = code;
            }

            /// <summary>스택에서 내린 블록이 붙잡은 것을 놓는다 (다시 쓰기 전).</summary>
            internal void Release()
            {
                Body = null!;
                Code = null;
                Next = 0;
                Loop = null;
                While = null;
                Items = null;
                Range = null;
                Live = null;
                Count = 0;
                Index = -1;
            }

            /// <summary>이 블록이 <c>for</c>의 몸체면 그 문장.</summary>
            public ForStmt? Loop { get; set; }

            /// <summary>이 블록이 <c>while</c>의 몸체면 그 문장. 바퀴마다 조건을 다시 평가한다 (G195).</summary>
            public WhileStmt? While { get; set; }

            /// <summary><c>break</c>·<c>continue</c>가 찾는 반복의 몸체인가.</summary>
            public bool IsLoop => Loop != null || While != null;

            /// <summary>반복 항목: <see cref="Range"/>가 아니면 시작할 때 펼친 목록.</summary>
            public IReadOnlyList<Value>? Items { get; set; }

            public RangeValue? Range { get; set; }

            /// <summary>엔진이 준 묶음을 돌면 그 묶음: 한 바퀴마다 사라진 항목을 건너뛴다 (G88·G194).</summary>
            public IRuntimeLiveCollection? Live { get; set; }

            public long Count { get; set; }

            /// <summary>지금 실행 중인 항목의 위치 (아직 시작 전이면 -1).</summary>
            public long Index { get; set; } = -1;

            public Value ItemAt(long i) => Range != null ? new IntValue(Range.At(i)) : Items![(int)i];

            public Block Copy() => new Block(Body, Next)
            {
                Code = Code,
                Loop = Loop,
                While = While,
                Items = Items,
                Range = Range,
                Live = Live,
                Count = Count,
                Index = Index,
            };
        }

        internal sealed partial class Session
        {
            private readonly PureEvaluator _owner;
            private readonly List<Frame> _stack = new List<Frame>();
            private StepMeter _meter;

            /// <param name="meter">null이면 쓰기 전에 <see cref="Begin"/>으로 준다.</param>
            public Session(PureEvaluator owner, StepMeter? meter)
            {
                _owner = owner;
                _meter = meter!;
            }

            /// <summary>다시 쓰는 세션의 새 실행을 시작한다.</summary>
            public void Begin(StepMeter meter) => _meter = meter;

            /// <summary>실행이 끝난 세션을 비운다(다음 실행이 앞 실행의 흔적을 보지 않게, 붙잡은 값을 놓게).</summary>
            public void Clear()
            {
                _stack.Clear();
                _costs.Clear();
                _pendingRequest = null;
                _answerTo = null;
                _suspended = null;
                _meter = null!;
            }

            // ---- 멈춤 ----

            /// <summary>문장 수준의 <c>ask</c>가 낸 질문. 있으면 실행 고리가 멈춘다.</summary>
            private Value? _pendingRequest;

            /// <summary>사전 조건 블록의 <c>cost</c>들, 적힌 순서대로. 본문이 시작될 때 이 금액을 뺀다 (G185).</summary>
            private readonly List<CostEntry> _costs = new List<CostEntry>();

            private sealed class CostEntry
            {
                public CostEntry(IRuntimeSlots owner, string name, Value amount, SourceSpan span)
                {
                    Owner = owner;
                    Name = name;
                    Amount = amount;
                    Span = span;
                }

                public IRuntimeSlots Owner { get; }
                public string Name { get; }
                public Value Amount { get; }
                public SourceSpan Span { get; }
            }

            private string? _answerTo;

            /// <summary>멈춘 실행. 멈출 때 스택에서 떼어 둔다.</summary>
            private Suspended? _suspended;

            /// <summary>가장 바깥 실행 고리가 멈췄으면 스택 전체를 떼어 멈춘 실행으로 만든다.</summary>
            private void Detach()
            {
                if (_pendingRequest == null) return;
                _suspended = new Suspended(new List<Frame>(_stack), _answerTo, _pendingRequest);
                _stack.Clear();
                _pendingRequest = null;
                _answerTo = null;
            }

            public Suspended? TakeSuspended()
            {
                var s = _suspended;
                _suspended = null;
                return s;
            }

            /// <summary>멈춘 실행의 사본을 스택에 올리고 답을 받을 변수에 넣는다.</summary>
            public void Restore(Suspended from, Value answer)
            {
                _stack.AddRange(CopyStack(from.Frames));
                if (from.AnswerTo != null) _stack[_stack.Count - 1].Locals[from.AnswerTo] = answer;
            }

            /// <summary>
            /// 프레임 스택의 사본. 람다는 만든 프레임의 지역 변수 사전을 잡고 늦게 읽으므로(G104), 사본 안의 람다는 사본의 사전을
            /// 잡도록 바꾼다. 원본 람다 하나에 사본 하나라 같은 람다를 든 두 변수는 사본에서도 같은 객체다 (G143).
            /// 이미 끝난 함수의 사전은 더 바뀌지 않지만 스택의 사전을 잡은 람다를 들 수 있어서 함께 옮긴 사본을 쓴다.
            /// </summary>
            private static List<Frame> CopyStack(List<Frame> frames)
            {
                var copies = new List<Frame>(frames.Count);
                foreach (var frame in frames) copies.Add(frame.Copy());
                var closures = new Dictionary<ClosureValue, ClosureValue>();
                var envs = new Dictionary<LocalTable, LocalTable>();

                LocalTable Env(LocalTable captured)
                {
                    for (int i = 0; i < frames.Count; i++)
                        if (ReferenceEquals(captured, frames[i].Locals)) return copies[i].Locals;
                    if (envs.TryGetValue(captured, out var copied)) return copied;
                    copied = new LocalTable(captured.Layout);
                    envs.Add(captured, copied); // 채우기 전에 등록한다: 사전이 자기를 잡은 람다를 들 수 있다
                    var names = new SortedSet<string>(StringComparer.Ordinal);
                    foreach (var pair in captured) names.Add(pair.Key);
                    foreach (var name in names) copied[name] = Remap(captured[name]);
                    return copied;
                }

                Value Remap(Value value)
                {
                    switch (value)
                    {
                        case ClosureValue closure:
                            if (closures.TryGetValue(closure, out var copy)) return copy;
                            // 사전을 옮기는 동안 같은 람다를 다시 만날 수 있으니 먼저 만들 자리를 잡을 수 없다: 사전부터 옮긴다
                            var env = Env(closure.Captured);
                            if (closures.TryGetValue(closure, out copy)) return copy;
                            copy = new ClosureValue(closure.Function, env);
                            closures.Add(closure, copy);
                            return copy;
                        case ListValue list:
                            return new ListValue(RemapAll(list.Items));
                        case DictValue dict:
                            var entries = new List<DictItem>(dict.Items.Count);
                            foreach (var item in dict.Items) entries.Add(new DictItem(Remap(item.Key), Remap(item.Value), item.Span));
                            return new DictValue(entries);
                        case TextValue text:
                            var args = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                            foreach (var pair in text.Args) args[pair.Key] = Remap(pair.Value);
                            return new TextValue(text.Key, args);
                        case IValueContainer container:
                            return container.MapValues(Remap);
                    }
                    // 저장(SaveScriptValue)이 들어가는 곳은 여기도 모두 들어간다: 두 길이 같아야 C1이다
                    return value;
                }

                List<Value> RemapAll(IReadOnlyList<Value> items)
                {
                    var result = new List<Value>(items.Count);
                    foreach (var item in items) result.Add(Remap(item));
                    return result;
                }

                foreach (var copy in copies)
                {
                    foreach (var name in new List<string>(copy.Locals.Keys)) copy.Locals[name] = Remap(copy.Locals[name]);
                    foreach (var block in copy.Blocks)
                        if (block.Items != null) block.Items = RemapAll(block.Items);
                }
                return copies;
            }

            public void RunToEnd()
            {
                try
                {
                    RunUntil(0);
                    Detach();
                }
                finally
                {
                    Truncate(0);
                }
            }

            // ---- 오류 ----

            private static bool HasSpan(ScriptRuntimeException e) => e.Span.Line > 0;

            public ScriptRuntimeException Fail(string code, string message, string? hint = null) =>
                new ScriptRuntimeException(code, message, hint);

            /// <summary>
            /// 이 실행 단위의 프레임을 오류의 흔적에 한 번 덧붙인다. 위치가 아직 없으면 이 식이 오류 위치다.
            /// 이미 있으면 오류는 안쪽 실행(이 식이 읽은 다른 <c>derived</c>)에서 났으므로, 이 식의 위치는 가장 안쪽
            /// 프레임에 "at …"으로 붙여 어디서 읽었는지 남긴다.
            /// </summary>
            public void Attach(ScriptRuntimeException e, SourceSpan span)
            {
                if (ReferenceEquals(e.TracedBy, this)) return;
                e.TracedBy = this;
                bool nested = HasSpan(e);
                if (!nested) e.Span = span;
                for (int i = _stack.Count - 1; i >= 0; i--)
                {
                    var frame = _stack[i];
                    string line = frame.Function.Name;
                    if (nested && i == _stack.Count - 1) line += " at " + span;
                    if (frame.CallSite.Line > 0) line += " (called at " + frame.CallSite + ")";
                    e.AddTrace(line);
                }
            }

            public PureEvaluator Owner => _owner;

            /// <summary>오류에 노드의 위치를 붙여 돌려준다 (감싸지 않는 노드가 스스로 낸 오류).</summary>
            public ScriptRuntimeException Placed(ScriptRuntimeException e, ExprNode node)
            {
                Attach(e, node.Span);
                return e;
            }

            /// <summary>감싸지 않는 노드의 한 단계: 넘치면 그 노드의 위치를 붙인다.</summary>
            public void Tick(ExprNode node)
            {
                if (++_meter.Used > _meter.Budget) throw Placed(BudgetExceeded(), node);
            }

            public void Tick()
            {
                if (++_meter.Used > _meter.Budget) throw BudgetExceeded();
            }

            private ScriptRuntimeException BudgetExceeded() =>
                Fail(Codes.RunBudget, "the function used more than " + _meter.Budget + " evaluation steps",
                    "a loop may be too large or endless; the budget is shared by everything one read computes, " +
                    "including the derived values it reads");
        }
    }
}
