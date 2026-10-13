using System;
using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    public enum PatchOp
    {
        Set,
        Add,
        Append,
        Remove,
    }

    /// <summary>
    /// 패치 경로의 조각 하나: 이름(<c>Cost</c>), 문자열 키(<c>['core.Wood']</c>), 정수 키(<c>[3]</c>).
    /// 같은 경로인지는 정규형으로 비교한다: 이름과 문자열 키는 같은 글자면 같다 (<c>Cost.Wood</c> = <c>Cost['Wood']</c>,
    /// spec-gaps G47·G50). 어떤 조각이 필드인지 키인지는 스키마를 봐야 알 수 있다.
    /// </summary>
    public readonly struct PathSegment : IEquatable<PathSegment>
    {
        private PathSegment(string? text, long number, bool isNumber, bool bracketed)
        {
            Text = text;
            Number = number;
            IsNumber = isNumber;
            Bracketed = bracketed;
        }

        public static PathSegment Name(string text) => new PathSegment(text, 0, false, false);
        public static PathSegment StringKey(string text) => new PathSegment(text, 0, false, true);
        public static PathSegment IntKey(long number) => new PathSegment(null, number, true, true);

        /// <summary>이름 또는 문자열 키의 글자. 정수 키면 null.</summary>
        public string? Text { get; }

        public long Number { get; }
        public bool IsNumber { get; }

        /// <summary>대괄호로 적었는가. 정규형 비교에는 쓰지 않는다.</summary>
        public bool Bracketed { get; }

        public bool Equals(PathSegment other) =>
            IsNumber == other.IsNumber && (IsNumber ? Number == other.Number : string.Equals(Text, other.Text, StringComparison.Ordinal));

        public override bool Equals(object? obj) => obj is PathSegment other && Equals(other);
        public override int GetHashCode() => IsNumber ? Number.GetHashCode() : StringComparer.Ordinal.GetHashCode(Text ?? "");

        /// <summary>정규형 표기. 식별자로 쓸 수 있는 이름과 문자열 키는 점 표기, 그 밖은 대괄호.</summary>
        public string Canonical(bool first)
        {
            if (IsNumber) return "[" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            if (PatchPath.IsIdentifier(Text!)) return first ? Text! : "." + Text;
            return "['" + Text + "']";
        }
    }

    /// <summary>
    /// 패치 경로 문자열 (<c>"Health.base_max"</c>, <c>"Cost['core.Wood']"</c>, <c>"Table[3]"</c>, spec-gaps G34·G47).
    /// 문법: 이름으로 시작하고, 이름 뒤에 점으로 이름을 잇거나 대괄호로 문자열 키(따옴표 <c>'</c>/<c>"</c>, 역슬래시 없음)나
    /// 정수 키를 붙인다.
    /// </summary>
    public sealed class PatchPath
    {
        private PatchPath(string text, IReadOnlyList<PathSegment> segments)
        {
            Text = text;
            Segments = segments;
        }

        /// <summary>적힌 그대로.</summary>
        public string Text { get; }

        public IReadOnlyList<PathSegment> Segments { get; }

        /// <summary>정규형 표기 (<c>Cost.Wood</c>과 <c>Cost['Wood']</c>가 같은 글자).</summary>
        public string Canonical
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Segments.Count; i++) sb.Append(Segments[i].Canonical(i == 0));
                return sb.ToString();
            }
        }

        public bool SameAs(PatchPath other)
        {
            if (Segments.Count != other.Segments.Count) return false;
            for (int i = 0; i < Segments.Count; i++)
                if (!Segments[i].Equals(other.Segments[i])) return false;
            return true;
        }

        /// <summary>이 경로가 <paramref name="other"/>의 앞부분(같거나 더 짧은 접두사)인가.</summary>
        public bool IsPrefixOf(PatchPath other)
        {
            if (Segments.Count > other.Segments.Count) return false;
            for (int i = 0; i < Segments.Count; i++)
                if (!Segments[i].Equals(other.Segments[i])) return false;
            return true;
        }

        public static bool IsIdentifier(string text)
        {
            if (text.Length == 0 || !(IsLetter(text[0]) || text[0] == '_')) return false;
            foreach (var c in text)
                if (!(IsLetter(c) || c == '_' || (c >= '0' && c <= '9'))) return false;
            return true;
        }

        private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        public static bool TryParse(string text, out PatchPath path, out string error)
        {
            path = null!;
            var segments = new List<PathSegment>();
            int i = 0;
            while (true)
            {
                int start = i;
                while (i < text.Length && (IsLetter(text[i]) || text[i] == '_' || (i > start && text[i] >= '0' && text[i] <= '9')))
                    i++;
                if (i == start)
                {
                    error = i >= text.Length
                        ? (segments.Count == 0 ? "the path is empty" : "the path ends after a '.'")
                        : "expected a field or component name at position " + (i + 1) + ", found '" + text[i] + "'";
                    return false;
                }
                segments.Add(PathSegment.Name(text.Substring(start, i - start)));

                while (i < text.Length && text[i] == '[')
                {
                    i++;
                    if (i >= text.Length)
                    {
                        error = "unterminated '['";
                        return false;
                    }

                    if (text[i] == '\'' || text[i] == '"')
                    {
                        char quote = text[i++];
                        int close = text.IndexOf(quote, i);
                        if (close < 0)
                        {
                            error = "unterminated string key";
                            return false;
                        }
                        string key = text.Substring(i, close - i);
                        if (key.IndexOf('\\') >= 0)
                        {
                            error = "backslashes are not allowed in a string key";
                            return false;
                        }
                        segments.Add(PathSegment.StringKey(key));
                        i = close + 1;
                    }
                    else
                    {
                        int numStart = i;
                        if (text[i] == '-') i++;
                        int digits = i;
                        while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
                        if (i == digits || !long.TryParse(text.Substring(numStart, i - numStart),
                                System.Globalization.NumberStyles.AllowLeadingSign,
                                System.Globalization.CultureInfo.InvariantCulture, out long number))
                        {
                            error = "a key in [...] must be a quoted string or an integer";
                            return false;
                        }
                        segments.Add(PathSegment.IntKey(number));
                    }

                    if (i >= text.Length || text[i] != ']')
                    {
                        error = "expected ']'";
                        return false;
                    }
                    i++;
                }

                if (i == text.Length) break;
                if (text[i] != '.')
                {
                    error = "unexpected '" + text[i] + "' at position " + (i + 1);
                    return false;
                }
                i++;
            }

            path = new PatchPath(text, segments);
            error = "";
            return true;
        }
    }

    /// <summary>
    /// ②b에서 기록한 <c>patch.*</c> 호출 하나 (spec-gaps G35). 아직 적용하지 않았고 대상 Def·경로·값은 검사 전이다.
    /// </summary>
    public sealed class PatchCall
    {
        internal PatchCall(PatchOp op, string mod, int index, string targetId, SourceSpan targetSpan, PatchPath path,
            SourceSpan pathSpan, Value? value, SourceSpan valueSpan, SourceSpan span, IReadOnlyList<string> guards)
        {
            Op = op;
            Mod = mod;
            Index = index;
            TargetId = targetId;
            TargetSpan = targetSpan;
            Path = path;
            PathSpan = pathSpan;
            Value = value;
            ValueSpan = valueSpan;
            Span = span;
            Guards = guards;
        }

        public PatchOp Op { get; }

        /// <summary>호출한 모드.</summary>
        public string Mod { get; }

        /// <summary>그 모드 안에서 호출한 순서 (0부터). 모드 순서와 함께 적용 순서를 이룬다 (G41·G50).</summary>
        public int Index { get; }

        public string TargetId { get; }
        public SourceSpan TargetSpan { get; }
        public PatchPath Path { get; }
        public SourceSpan PathSpan { get; }

        /// <summary><c>remove</c>의 2인자 형(컴포넌트 떼기)이면 null.</summary>
        public Value? Value { get; }

        public SourceSpan ValueSpan { get; }

        /// <summary>호출 전체의 위치. 이후 단계의 오류와 변경 이력(S9)이 가리킨다.</summary>
        public SourceSpan Span { get; }

        /// <summary>이 호출을 감싼 <c>if mod_loaded("m")</c>의 m들 (바깥부터, G46).</summary>
        public IReadOnlyList<string> Guards { get; }
    }
}
