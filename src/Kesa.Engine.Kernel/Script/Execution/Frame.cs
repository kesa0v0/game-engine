using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
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
                UiWritable = UiWritable,
                Result = Result,
                ResultName = ResultName,
                Immediate = Immediate,
                Dispatch = Dispatch?.Copy(),
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
            UiWritable = false;
            Result = ResultTo.Host;
            ResultName = null;
            Returned = NoneValue.Instance;
            Immediate = false;
            Dispatch = null;
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
        /// UI 쓰기 모드 (G282 런타임 안전망). <c>@ui_action</c> 본문, 그리고 UI 쓰기 모드 프레임이 문장으로 부른 UI를 쓰는 함수만 참이다.
        /// 이 모드에서는 <c>ui.기록.칸</c>에 쓰고 <c>submit</c>할 수 있지만 게임은 바꾸지 못한다(<see cref="Writable"/>과 따로).
        /// </summary>
        public bool UiWritable { get; set; }

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

        /// <summary>
        /// 즉시 핸들러 안인가 (spec-gaps G310): 즉시 핸들러 본문과 그것이 문장으로 부른 함수. 시간을 막는 <c>ask</c>만 멈출 수 있다.
        /// </summary>
        public bool Immediate { get; set; }

        /// <summary>이 프레임의 <c>emit</c> 문장이 아직 다 부르지 않은 즉시 핸들러. 없으면 null.</summary>
        public ImmediateDispatch? Dispatch { get; set; }
    }

    /// <summary>
    /// 감싸는 함수의 첫 인자: 바로 안쪽 함수 (spec-gaps G312·G314). <see cref="Level"/>번째 감싸기(안쪽부터 0)이고, 음수면 원래 함수다.
    /// 부르기만 할 수 있어(로드 검사) 저장에 적지 않고, 불러올 때 지금의 감싸기 순서로 다시 만든다.
    /// </summary>
    internal sealed class InnerFunction : Value
    {
        public InnerFunction(FunctionValue target, int level)
        {
            Target = target;
            Level = level;
        }

        public FunctionValue Target { get; }
        public int Level { get; }

        public override string KindName => "inner function";
    }

    /// <summary>
    /// <c>emit</c> 문장 하나가 부르는 즉시 핸들러들 (spec-gaps G310). 한 번에 하나씩 프레임을 쌓아, 멈춘 실행에서도 부른 쪽 문장이
    /// <c>emit(...)</c>이고 그 위가 지금 도는 핸들러 하나다.
    /// </summary>
    internal sealed class ImmediateDispatch
    {
        public ImmediateDispatch(PureBuiltin emit, Value signal, IReadOnlyList<FunctionValue> handlers, SourceSpan site, int next = 0)
        {
            Emit = emit;
            Signal = signal;
            Handlers = handlers;
            Site = site;
            Next = next;
        }

        public PureBuiltin Emit { get; }
        public Value Signal { get; }
        public IReadOnlyList<FunctionValue> Handlers { get; }

        /// <summary><c>emit(...)</c> 호출의 위치 (핸들러 프레임의 부른 위치).</summary>
        public SourceSpan Site { get; set; }

        /// <summary>다음에 부를 핸들러의 번호. 지금 도는 핸들러는 그 앞의 것이다.</summary>
        public int Next { get; set; }

        public ImmediateDispatch Copy() => new ImmediateDispatch(Emit, Signal, Handlers, Site, Next);
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
}
