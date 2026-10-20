using System;
using System.Collections.Generic;

namespace Kesa.Engine.Diagnostics
{
    /// <summary>"혹시 …?" 후보 계산 (spec S9). 결과는 입력 순서와 무관하게 결정적이다.</summary>
    public static class Suggest
    {
        /// <summary>편집 거리가 가장 작은 후보 하나. 거리 상한을 넘으면 null.</summary>
        public static string? Closest(string input, IEnumerable<string> candidates)
        {
            int limit = Math.Max(1, input.Length / 3);
            string? best = null;
            int bestDistance = int.MaxValue;
            foreach (var candidate in candidates)
            {
                int d = Distance(input, candidate);
                if (d > limit) continue;
                if (d < bestDistance || (d == bestDistance && best != null && string.CompareOrdinal(candidate, best) < 0))
                {
                    best = candidate;
                    bestDistance = d;
                }
            }
            return best;
        }

        public static string? Hint(string input, IEnumerable<string> candidates)
        {
            var closest = Closest(input, candidates);
            return closest == null ? null : "did you mean '" + closest + "'?";
        }

        /// <summary>
        /// 편집 거리 (대소문자 구분, 서수 비교): 넣기·빼기·바꾸기에 더해 이웃한 두 글자 맞바꾸기도 한 번이다(제한된 Damerau, OSA).
        /// 그래서 <c>lfet</c> → <c>left</c>가 1이다.
        /// </summary>
        public static int Distance(string a, string b)
        {
            var before = new int[b.Length + 1]; // 두 줄 전 (맞바꾸기용)
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int d = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d = Math.Min(d, before[j - 2] + 1);
                    cur[j] = d;
                }
                var tmp = before;
                before = prev;
                prev = cur;
                cur = tmp;
            }
            return prev[b.Length];
        }
    }
}
