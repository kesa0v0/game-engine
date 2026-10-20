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
