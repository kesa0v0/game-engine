using System;
using System.Collections.Generic;

namespace Kesa.Engine.Ui
{
    /// <summary>요소 속성이 받는 값의 종류 (spec-gaps G341).</summary>
    public enum UiPropertyType
    {
        /// <summary>글자: 번역기가 정한 맨 값 규칙.</summary>
        Text,

        /// <summary>참거짓: 모드 언어의 참거짓 판정.</summary>
        Flag,

        /// <summary>숫자: 정수와 고정소수점만. 렌더러에는 고정소수점의 원래 정수값이 간다.</summary>
        Number,

        /// <summary>이미지 경로: 글자 (G335).</summary>
        Image,
    }

    /// <summary><c>bind-&lt;속성&gt;</c>이 묶을 수 있는 요소 속성의 표 (G331).</summary>
    public static class UiProperties
    {
        private static readonly SortedDictionary<string, UiPropertyType> Table =
            new SortedDictionary<string, UiPropertyType>(StringComparer.Ordinal)
            {
                ["text"] = UiPropertyType.Text,
                ["display"] = UiPropertyType.Flag,
                ["enabled"] = UiPropertyType.Flag,
                ["value"] = UiPropertyType.Number,
                ["opacity"] = UiPropertyType.Number,
                ["image"] = UiPropertyType.Image,
            };

        public static bool TryGet(string property, out UiPropertyType type) => Table.TryGetValue(property, out type);

        /// <summary>이름 순.</summary>
        public static IEnumerable<string> Names => Table.Keys;
    }

    /// <summary>오타에 "혹시 …?"를 붙이는 작은 도우미 (커널의 <c>Suggest</c>와 같은 뜻, 이 어셈블리는 커널을 모른다).</summary>
    internal static class UiSuggest
    {
        public static string Hint(string wrong, IEnumerable<string> known)
        {
            string? best = null;
            int bestDistance = int.MaxValue;
            foreach (var candidate in known)
            {
                int d = Distance(wrong, candidate);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }
            int limit = Math.Max(1, wrong.Length / 3);
            return best != null && bestDistance <= limit ? " (did you mean '" + best + "'?)" : "";
        }

        private static int Distance(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }
    }
}
