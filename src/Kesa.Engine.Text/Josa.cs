using System.Collections.Generic;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 번역문의 <c>{ JOSA($ship, "을") }</c> (spec-gaps G268): 이름의 끝소리에 맞는 조사를 골라 이름 뒤에 붙인다.
    /// 짝 중 아무 쪽이나 적는다. 끝이 한글이면 받침, 숫자면 읽는 소리, 그 밖은 두 형태를 다 쓴다(<c>Sloop을(를)</c>).
    /// </summary>
    internal static class Josa
    {
        /// <summary>(받침 뒤, 받침 없음 뒤). "으로/로"는 ㄹ 받침 뒤에도 "로"다.</summary>
        private static readonly (string After, string Open)[] Pairs =
        {
            ("이", "가"), ("을", "를"), ("은", "는"), ("과", "와"), ("으로", "로"),
            ("이나", "나"), ("이랑", "랑"), ("아", "야"), ("이여", "여"), ("이라", "라"),
        };

        private const int Rieul = 8; // 종성 ㄹ의 번호

        /// <summary>모르는 조사면 false (<c>check</c> 오류, 실행 중에는 적힌 그대로 붙인다).</summary>
        public static bool IsKnown(string particle) => Find(particle) != null;

        public static string Attach(string name, string particle)
        {
            var pair = Find(particle);
            if (pair == null) return name + particle;
            var (after, open) = pair.Value;
            int? final = FinalConsonant(name);
            if (final == null) return name + after + "(" + open + ")";
            if (final == 0) return name + open;
            return name + (after == "으로" && final == Rieul ? open : after);
        }

        private static (string After, string Open)? Find(string particle)
        {
            foreach (var p in Pairs)
                if (p.After == particle || p.Open == particle) return p;
            return null;
        }

        /// <summary>끝소리의 종성 번호(0 = 받침 없음), 알 수 없으면 null.</summary>
        private static int? FinalConsonant(string name)
        {
            if (name.Length == 0) return null;
            char last = name[name.Length - 1];
            if (last >= '가' && last <= '힣') return (last - 0xAC00) % 28;
            if (last >= '0' && last <= '9') return FinalOf(NumberReading(name));
            return null;
        }

        private static int FinalOf(char syllable) => (syllable - 0xAC00) % 28;

        private static readonly char[] Digits = { '영', '일', '이', '삼', '사', '오', '육', '칠', '팔', '구' };
        private static readonly char[] Places = { '십', '백', '천' };
        private static readonly char[] Myriads = { '만', '억', '조', '경' };

        /// <summary>
        /// 숫자로 끝나는 글의 마지막 읽는 소리 한 글자: 3 → 삼, 10 → 십, 100 → 백, 20000 → 만, 1.5 → 오.
        /// 소수점 뒤는 숫자를 하나씩 읽는다.
        /// </summary>
        private static char NumberReading(string text)
        {
            int end = text.Length;
            int start = end;
            while (start > 0 && char.IsDigit(text[start - 1])) start--;
            string digits = text.Substring(start, end - start);
            bool fraction = start > 0 && text[start - 1] == '.' && start > 1 && char.IsDigit(text[start - 2]);
            char lastDigit = digits[digits.Length - 1];
            if (fraction || lastDigit != '0') return Digits[lastDigit - '0'];
            string trimmed = digits.TrimStart('0');
            if (trimmed.Length == 0) return Digits[0];
            int zeros = trimmed.Length - trimmed.TrimEnd('0').Length;
            int place = zeros % 4;
            if (place != 0) return Places[place - 1];
            int myriad = zeros / 4 - 1;
            return myriad < Myriads.Length ? Myriads[myriad] : Myriads[Myriads.Length - 1];
        }

        /// <summary>알려진 조사 전부 (안내서·<c>check</c>의 힌트).</summary>
        public static IEnumerable<string> Known()
        {
            foreach (var p in Pairs)
            {
                yield return p.After;
                yield return p.Open;
            }
        }
    }
}
