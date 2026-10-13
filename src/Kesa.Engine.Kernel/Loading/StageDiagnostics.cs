using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 한 회차(스키마 만들기 또는 Def 등록)의 진단을 모드별로 모은다.
    /// 다른 모드와 같은 이름을 써서만 생긴 오류(충돌)는 따로 둔다: 자기 오류로 실패하는 모드를 먼저 빼고 다시 만들어야,
    /// 상대가 어떤 이유로 실패했는지에 따라 충돌 결과가 달라지지 않는다 (spec-gaps G28).
    /// </summary>
    public sealed class StageDiagnostics
    {
        private readonly Dictionary<string, DiagnosticBag> _own = new Dictionary<string, DiagnosticBag>(StringComparer.Ordinal);
        private readonly Dictionary<string, DiagnosticBag> _conflict =
            new Dictionary<string, DiagnosticBag>(StringComparer.Ordinal);

        public StageDiagnostics(IEnumerable<string> mods)
        {
            foreach (var m in mods)
            {
                _own[m] = new DiagnosticBag();
                _conflict[m] = new DiagnosticBag();
            }
        }

        public void Error(string mod, string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            _own[mod].Error(code, message, span, mod, hint, related);

        public void Warning(string mod, string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            _own[mod].Warning(code, message, span, mod, hint, related);

        /// <summary>다른 모드와의 충돌 때문에만 생긴 오류.</summary>
        public void ConflictError(string mod, string code, string message, SourceSpan span, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            _conflict[mod].Error(code, message, span, mod, hint, related);

        public bool HasOwnErrors(string mod) => _own[mod].HasErrors;
        public bool HasConflictErrors(string mod) => _conflict[mod].HasErrors;

        public IEnumerable<Diagnostic> All(string mod)
        {
            foreach (var d in _own[mod].ToSortedList()) yield return d;
            foreach (var d in _conflict[mod].ToSortedList()) yield return d;
        }
    }
}
