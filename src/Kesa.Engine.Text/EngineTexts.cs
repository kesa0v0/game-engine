using System.Collections.Generic;
using Kesa.Engine.Loading;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 엔진이 넣는 기본 번역 (spec-gaps G267·G269·G273). 가장 먼저 읽혀 호스트와 모드가 덮어쓸 수 있다.
    /// 키는 엔진 예약 이름 <c>engine-</c> 아래에 둔다.
    /// </summary>
    public static class EngineTexts
    {
        /// <summary>엔진 글의 주인 이름 (모드 id <c>engine</c>은 예약, G273).</summary>
        public const string Owner = ManifestReader.ReservedId;

        /// <summary>엔진 글의 기본 언어 (폴백 G257).</summary>
        public const string Language = "en";

        /// <summary>못 보는 값 (G198·G267).</summary>
        public const string Hidden = "engine-hidden";

        /// <summary>언어 태그 → 파일 내용.</summary>
        public static readonly IReadOnlyDictionary<string, string> Files = new SortedDictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["en"] = Hidden + " = unknown\n",
            ["ko"] = Hidden + " = 알 수 없음\n",
        };
    }
}
