using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
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

        /// <summary>이 실행기의 내장 함수 표 (저장이 내장 함수 값을 이름으로 적고 다시 찾는다).</summary>
        internal IReadOnlyDictionary<string, Value> Builtins => _builtins;

        /// <summary>모드 전역 이름 조회 (<see cref="Session"/>이 이름을 찾을 때 쓴다).</summary>
        internal IScriptGlobals Globals => _globals;

        /// <summary>불러온 멈춘 실행의 빈 프레임 (저장, spec S8). 칸 배치는 함수 정의에서 다시 계산한다.</summary>
        internal Frame NewFrame(FunctionValue function) =>
            new Frame(function, new LocalTable(LayoutOf(function.Definition!)));

        /// <summary>
        /// <c>def</c>의 지역 변수 칸 배치. AST에만 기대므로 함수 정의에 한 번 둔다: 같은 콘텐츠를 쓰는 여러 월드가 같은 배치와 실행 노드를 나눠 쓴다.
        /// </summary>
        internal static FrameLayout LayoutOf(FunctionDef def) =>
            (FrameLayout)(def.Layout ??= new FrameLayout(FunctionLocals.Of(def)));

        /// <summary>붙잡은 이름 공간 없이 불린 람다의 칸 배치 (매개변수만).</summary>
        internal static FrameLayout BareLambdaLayout(LambdaExpr lambda) =>
            (FrameLayout)(lambda.Layout ??= new FrameLayout(FunctionLocals.Parameters(lambda.Parameters)));
    }
}
