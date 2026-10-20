using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 본문에서 덮어쓰지 않은 <c>submit("커맨드", 이름=값)</c>에 커맨드 이름을 리터럴로 적은 호출 (spec-gaps G278).
    /// 모든 모드의 커맨드가 정해진 뒤 로더가 그 커맨드가 있는지, 보이는지, 매개변수 이름이 맞는지 본다. 이름을 계산한 호출은 없다.
    /// </summary>
    public sealed class SubmitUse
    {
        public SubmitUse(string command, IReadOnlyList<string> argumentNames, IReadOnlyList<SourceSpan> argumentSpans, SourceSpan span)
        {
            Command = command;
            ArgumentNames = argumentNames;
            ArgumentSpans = argumentSpans;
            Span = span;
        }

        /// <summary>적힌 그대로의 이름: 점이 없으면 쓴 모드의 커맨드, 있으면 <c>모드.이름</c>.</summary>
        public string Command { get; }

        /// <summary>넘긴 이름 인자, 적은 순서대로.</summary>
        public IReadOnlyList<string> ArgumentNames { get; }

        public IReadOnlyList<SourceSpan> ArgumentSpans { get; }

        /// <summary>커맨드 이름 리터럴의 위치.</summary>
        public SourceSpan Span { get; }
    }
}
