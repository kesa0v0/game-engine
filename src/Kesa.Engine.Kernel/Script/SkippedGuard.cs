using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary><c>if mod_loaded("m")</c>가 거짓이라 실행하지 않은 가지 안의 <c>patch.*</c> 호출 수 (모드 m 하나당).</summary>
    public sealed class SkippedGuard
    {
        public SkippedGuard(SourceSpan first) => First = first;

        public int Count { get; internal set; }

        /// <summary>처음 건너뛴 가지의 <c>if</c>/<c>elif</c> 위치.</summary>
        public SourceSpan First { get; }
    }
}
