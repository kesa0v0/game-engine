using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 최종 값의 책임 모드와 위치 (spec-gaps G64): 직접 적은 값은 그 Def의 모드, 패치가 쓴 값은 패치 모드,
    /// 물려받은 값은 처음 적은 모드다. ⑤ 링크와 ⑥ 검증이 같은 규칙을 쓴다.
    /// </summary>
    internal readonly struct ValueBlame
    {
        public ValueBlame(string mod, SourceSpan span, IReadOnlyList<string> guards)
        {
            Mod = mod;
            Span = span;
            Guards = guards;
        }

        public string Mod { get; }
        public SourceSpan Span { get; }

        /// <summary>값을 쓴 패치를 감싼 <c>mod_loaded</c> 가드들. 패치가 아니면 비어 있다.</summary>
        public IReadOnlyList<string> Guards { get; }

        /// <param name="literal">값 안의 리터럴 위치 (문자열 리터럴의 Origin 등). 없으면 <see cref="SourceSpan.None"/></param>
        /// <param name="fallback">리터럴 위치를 모를 때 쓸 위치</param>
        public static ValueBlame Of(ResolvedValue origin, SourceSpan literal, SourceSpan fallback)
        {
            if (origin.Patch != null)
            {
                var span = literal.File.Length > 0 ? literal : origin.Patch.Call.ValueSpan;
                return new ValueBlame(origin.Patch.Mod, span, origin.Patch.Call.Guards);
            }
            var at = literal.File.Length > 0 ? literal : fallback;
            if (at.File.Length == 0) at = origin.Source.Span;
            return new ValueBlame(origin.Source.Mod, at, Array.Empty<string>());
        }
    }
}
