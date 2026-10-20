namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 언어 이름 = BCP 47 태그 (spec-gaps G256): <c>defaultLanguage</c>와 번역 폴더 <c>locale/&lt;태그&gt;/</c>.
    /// 모양만 본다: 첫 조각은 영문자 2~3자 또는 5~8자, 이어지는 조각은 <c>-</c> 뒤 영문자·숫자 1~8자 (<c>en</c>, <c>ko</c>, <c>pt-BR</c>, <c>zh-Hant-TW</c>).
    /// 등록부에 있는 언어인지는 보지 않는다.
    /// </summary>
    public static class LanguageTag
    {
        public const string Rule = "use a BCP 47 language tag such as en, ko or pt-BR";

        public static bool IsValid(string tag)
        {
            var parts = tag.Split('-');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (i == 0 ? !(p.Length >= 2 && p.Length <= 3 || p.Length >= 5 && p.Length <= 8) : p.Length < 1 || p.Length > 8)
                    return false;
                foreach (char c in p)
                {
                    bool letter = c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z';
                    if (!letter && (i == 0 || !(c >= '0' && c <= '9'))) return false;
                }
            }
            return true;
        }

        /// <summary>끝 조각을 하나 줄인 태그 (<c>pt-BR</c> → <c>pt</c>), 더 줄일 수 없으면 null (G257 폴백).</summary>
        public static string? Parent(string tag)
        {
            int dash = tag.LastIndexOf('-');
            return dash <= 0 ? null : tag.Substring(0, dash);
        }
    }
}
