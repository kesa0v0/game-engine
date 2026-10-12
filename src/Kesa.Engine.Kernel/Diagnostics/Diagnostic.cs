using System;
using System.Collections.Generic;

namespace Kesa.Engine.Diagnostics
{
    public enum Severity
    {
        Error,
        Warning,
        Info,
    }

    /// <summary>
    /// 소스 위치. 경로는 모드 루트 기준 상대 경로이며 구분자는 '/'.
    /// 줄과 열은 1부터 시작하고, 열은 UTF-16 코드 단위로 센다.
    /// </summary>
    public readonly struct SourceSpan : IEquatable<SourceSpan>
    {
        public SourceSpan(string file, int line, int col)
        {
            File = file;
            Line = line;
            Col = col;
        }

        public string File { get; }
        public int Line { get; }
        public int Col { get; }

        public static SourceSpan None => new SourceSpan("", 0, 0);

        public bool Equals(SourceSpan other) =>
            string.Equals(File, other.File, StringComparison.Ordinal) && Line == other.Line && Col == other.Col;

        public override bool Equals(object? obj) => obj is SourceSpan other && Equals(other);

        public override int GetHashCode() => Line * 397 ^ Col;

        public override string ToString() => File + ":" + Line.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                             ":" + Col.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>진단은 데이터다 (spec S9).</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(string code, Severity severity, string message, string? mod, SourceSpan span,
            string? hint = null, IReadOnlyList<SourceSpan>? related = null)
        {
            Code = code;
            Severity = severity;
            Message = message;
            Mod = mod;
            Span = span;
            Hint = hint;
            Related = related ?? Array.Empty<SourceSpan>();
        }

        public string Code { get; }
        public Severity Severity { get; }
        public string Message { get; }
        public string? Mod { get; }
        public SourceSpan Span { get; }
        public string File => Span.File;
        public int Line => Span.Line;
        public int Col => Span.Col;
        public string? Hint { get; }
        public IReadOnlyList<SourceSpan> Related { get; }

        public override string ToString() => Span + ": " + Code + ": " + Message + (Hint == null ? "" : " (" + Hint + ")");
    }

    /// <summary>
    /// 진단을 모아 한 번에 보고한다 (spec S1 오류 정책).
    /// 출력 순서는 (파일, 줄, 열, 코드) 순으로 정렬해 결정적이다.
    /// </summary>
    public sealed class DiagnosticBag
    {
        private readonly List<Diagnostic> _items = new List<Diagnostic>();

        public int Count => _items.Count;

        public bool HasErrors
        {
            get
            {
                foreach (var d in _items)
                    if (d.Severity == Severity.Error) return true;
                return false;
            }
        }

        public void Add(Diagnostic diagnostic) => _items.Add(diagnostic);

        public void AddRange(IEnumerable<Diagnostic> diagnostics) => _items.AddRange(diagnostics);

        public void Error(string code, string message, SourceSpan span, string? mod = null, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            _items.Add(new Diagnostic(code, Severity.Error, message, mod, span, hint, related));

        public void Warning(string code, string message, SourceSpan span, string? mod = null, string? hint = null,
            IReadOnlyList<SourceSpan>? related = null) =>
            _items.Add(new Diagnostic(code, Severity.Warning, message, mod, span, hint, related));

        public IReadOnlyList<Diagnostic> ToSortedList()
        {
            var copy = new List<Diagnostic>(_items);
            copy.Sort(DiagnosticComparer.Instance);
            return copy;
        }

        private sealed class DiagnosticComparer : IComparer<Diagnostic>
        {
            public static readonly DiagnosticComparer Instance = new DiagnosticComparer();

            public int Compare(Diagnostic? x, Diagnostic? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                int c = string.CompareOrdinal(x.File, y.File);
                if (c != 0) return c;
                c = x.Line.CompareTo(y.Line);
                if (c != 0) return c;
                c = x.Col.CompareTo(y.Col);
                if (c != 0) return c;
                c = string.CompareOrdinal(x.Code, y.Code);
                if (c != 0) return c;
                return string.CompareOrdinal(x.Message, y.Message);
            }
        }
    }
}
