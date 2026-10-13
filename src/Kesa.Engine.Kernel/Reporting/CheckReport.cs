using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;

namespace Kesa.Engine.Reporting
{
    /// <summary>
    /// <c>engine check</c>의 JSON 출력 (spec S9, spec-gaps G75). 라이브러리 없이 직접 쓰고(Unity 소스 패키지, P1a),
    /// 키 순서와 들여쓰기가 고정이라 같은 입력이면 같은 문자열이다. 파일 읽기와 종료 코드는 CLI의 일이다.
    /// </summary>
    public static class CheckReport
    {
        public const int Version = 1;

        /// <param name="diagnostics">로드 전체(① 발견·정렬부터 ⑥까지)의 진단. 정렬된 목록이어야 한다</param>
        public static string ToJson(IReadOnlyList<Diagnostic> diagnostics, ContentLoadResult result)
        {
            int errors = 0, warnings = 0, infos = 0;
            foreach (var d in diagnostics)
            {
                if (d.Severity == Severity.Error) errors++;
                else if (d.Severity == Severity.Warning) warnings++;
                else infos++;
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(Version).Append(",\n");
            sb.Append("  \"ok\": ").Append(errors == 0 ? "true" : "false").Append(",\n");
            sb.Append("  \"counts\": {\"error\": ").Append(errors).Append(", \"warning\": ").Append(warnings)
                .Append(", \"info\": ").Append(infos).Append("},\n");

            var loaded = new List<string>();
            foreach (var m in result.Mods) loaded.Add(m.Id);
            sb.Append("  \"mods\": {\"loaded\": ").Append(StringArray(loaded)).Append(", \"disabled\": ")
                .Append(StringArray(result.Disabled)).Append("},\n");

            if (diagnostics.Count == 0)
            {
                sb.Append("  \"diagnostics\": []\n");
            }
            else
            {
                sb.Append("  \"diagnostics\": [\n");
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    AppendDiagnostic(sb, diagnostics[i]);
                    sb.Append(i + 1 < diagnostics.Count ? ",\n" : "\n");
                }
                sb.Append("  ]\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void AppendDiagnostic(StringBuilder sb, Diagnostic d)
        {
            sb.Append("    {\n");
            sb.Append("      \"code\": ").Append(Quote(d.Code)).Append(",\n");
            sb.Append("      \"severity\": ").Append(Quote(SeverityName(d.Severity))).Append(",\n");
            sb.Append("      \"message\": ").Append(Quote(d.Message)).Append(",\n");
            sb.Append("      \"mod\": ").Append(d.Mod == null ? "null" : Quote(d.Mod)).Append(",\n");
            sb.Append("      \"file\": ").Append(Quote(d.Span.File)).Append(",\n");
            sb.Append("      \"line\": ").Append(Number(d.Span.Line)).Append(",\n");
            sb.Append("      \"col\": ").Append(Number(d.Span.Col)).Append(",\n");
            sb.Append("      \"hint\": ").Append(d.Hint == null ? "null" : Quote(d.Hint)).Append(",\n");
            sb.Append("      \"related\": [");
            for (int i = 0; i < d.Related.Count; i++)
            {
                var r = d.Related[i];
                if (i > 0) sb.Append(", ");
                sb.Append("{\"file\": ").Append(Quote(r.File)).Append(", \"line\": ").Append(Number(r.Line))
                    .Append(", \"col\": ").Append(Number(r.Col)).Append('}');
            }
            sb.Append("]\n");
            sb.Append("    }");
        }

        private static string SeverityName(Severity s) =>
            s == Severity.Error ? "error" : s == Severity.Warning ? "warning" : "info";

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string StringArray(IReadOnlyList<string> items)
        {
            var parts = new List<string>();
            foreach (var item in items) parts.Add(Quote(item));
            return "[" + string.Join(", ", parts) + "]";
        }

        /// <summary>JSON 문자열. 따옴표·역슬래시·제어 문자만 이스케이프하고 나머지(한글 포함)는 그대로 둔다.</summary>
        internal static string Quote(string text)
        {
            var sb = new StringBuilder(text.Length + 2);
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
