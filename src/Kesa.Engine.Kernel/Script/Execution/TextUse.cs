using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 본문에서 덮어쓰지 않은 <c>text("키", 이름=값)</c>에 키를 리터럴로 적은 호출 (spec-gaps G257).
    /// 커널은 보지 않고 <c>check</c>의 번역 검사(키가 있는지, 번역문이 쓰는 변수를 넘기는지)가 본다. 키를 계산한 호출은 없다.
    /// </summary>
    public sealed class TextUse
    {
        public TextUse(string key, IReadOnlyList<string> argumentNames, SourceSpan span)
        {
            Key = key;
            ArgumentNames = argumentNames;
            Span = span;
        }

        public string Key { get; }

        /// <summary>넘긴 이름 인자, 적은 순서대로.</summary>
        public IReadOnlyList<string> ArgumentNames { get; }

        /// <summary>키 리터럴의 위치.</summary>
        public SourceSpan Span { get; }
    }
}
