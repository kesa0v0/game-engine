using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>내장 함수가 진단을 내는 통로. 진단에는 항상 실행 중인 모드가 붙는다.</summary>
    public sealed class CallContext
    {
        public CallContext(string mod, DiagnosticBag diagnostics, IDictionary<string, SkippedGuard>? skipped = null,
            ModFunctions? functions = null)
        {
            Mod = mod;
            Diagnostics = diagnostics;
            Skipped = skipped ?? new SortedDictionary<string, SkippedGuard>(StringComparer.Ordinal);
            Functions = functions ?? new ModFunctions();
        }

        public string Mod { get; }
        public DiagnosticBag Diagnostics { get; }

        /// <summary>실행하며 정의한 로직 함수와 최상위 이름. 모드 평가가 끝난 뒤 이름 검사에 쓴다 (spec-gaps G108).</summary>
        public ModFunctions Functions { get; }

        /// <summary>
        /// 지금 실행 중인 가지가 참이라고 가정하는 모드들 (바깥 가드부터, spec-gaps G46).
        /// <c>patch.*</c> 호출이 기록할 때 함께 저장한다.
        /// </summary>
        public List<string> Guards { get; } = new List<string>();

        /// <summary>실행하지 않은 가드 가지의 패치 호출 수 (거짓인 모드 id별).</summary>
        public IDictionary<string, SkippedGuard> Skipped { get; }

        public void Error(string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            Diagnostics.Error(code, message, span, Mod, hint, related);

        /// <summary>오류를 보고하고 현재 문장을 중단한다.</summary>
        public ScriptError Fail(string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null)
        {
            Error(code, message, span, hint, related);
            return new ScriptError();
        }
    }
}
