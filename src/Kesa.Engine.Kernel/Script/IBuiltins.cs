using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>내장 이름의 출처. 단계(스키마 파일 / Def 파일)마다 다르다.</summary>
    public interface IBuiltins
    {
        Value? Lookup(string name);

        /// <summary>"혹시 …?" 후보.</summary>
        IEnumerable<string> Names { get; }

        /// <summary>
        /// 이 단계에서 쓸 수 없지만 다른 단계에서는 뜻이 있는 이름이면 그 이유 (예: 스키마 파일에서 Def 생성자).
        /// 진단 코드와 메시지를 돌려준다.
        /// </summary>
        (string Code, string Message)? Explain(string name);
    }
}
