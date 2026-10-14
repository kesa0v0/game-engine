using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// <c>@command</c>로 선언한 커맨드 (spec S6). id는 자동으로 <c>모드id.함수이름</c>이다 (spec-gaps G109).
    /// </summary>
    public sealed class CommandDecl
    {
        public CommandDecl(string mod, FunctionValue function, bool free, SourceSpan span)
        {
            Mod = mod;
            Function = function;
            Free = free;
            Span = span;
        }

        /// <summary>커맨드 로그·난수열 키·UI가 가리키는 이름: <c>모드id.함수이름</c> (G109).</summary>
        public string Id => Mod + "." + Function.Definition!.Name;

        public string Mod { get; }
        public FunctionValue Function { get; }

        /// <summary><c>@command(free=True)</c>: Request 없이 제출할 수 있다 (자유 커맨드, spec S6).</summary>
        public bool Free { get; }

        /// <summary><c>@command</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }
    }
}
