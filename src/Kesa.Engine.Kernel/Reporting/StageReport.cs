using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;

namespace Kesa.Engine.Reporting
{
    /// <summary>
    /// 단계 순서 덤프 (spec S9 "왜?" 도구 5, spec-gaps G289): 단계 순서와 그 순서를 정한 <c>after</c>·<c>before</c>, 신호별 핸들러,
    /// <c>@when</c> 반응을 실행 순서대로 늘어놓는 사람용 텍스트. 순서는 런타임과 같은 규칙이다(G152, <c>World.SetHandlers</c>).
    /// </summary>
    public static class StageReport
    {
        public static string ToText(ContentLoadResult result, string pathPrefix)
        {
            var schema = result.Schema;
            var sb = new StringBuilder();
            sb.Append("stages (after/before order; when several can come next, the smaller id first):\n");
            if (schema.StageOrder.Count == 0) sb.Append("  (none)\n");
            for (int i = 0; i < schema.StageOrder.Count; i++)
            {
                var stage = schema.StageOrder[i];
                sb.Append("  ").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(stage.Id)
                    .Append("    ").Append(CheckReport.Location(stage.Span, pathPrefix)).Append('\n');
                foreach (var line in Before(stage, schema, pathPrefix)) sb.Append("       ").Append(line).Append('\n');
            }

            var bySignal = new SortedDictionary<string, List<(string Stage, string Name, SourceSpan Span)>>(StringComparer.Ordinal);
            foreach (var h in result.Handlers)
            {
                if (!bySignal.TryGetValue(h.Signal.Name, out var list)) bySignal[h.Signal.Name] = list = new List<(string, string, SourceSpan)>();
                list.Add((h.Stage ?? "", h.Mod + "." + h.Function.Name, h.Span));
            }
            sb.Append("\nhandlers, per signal (stage order, then load order, then definition order; no stage= last):\n");
            if (bySignal.Count == 0) sb.Append("  (none)\n");
            foreach (var pair in bySignal)
            {
                sb.Append("  signal ").Append(pair.Key).Append(":\n");
                Grouped(sb, pair.Value, schema, pathPrefix);
            }

            var whens = new List<(string Stage, string Name, SourceSpan Span)>();
            foreach (var w in result.Whens) whens.Add((w.Stage ?? "", w.Id, w.Span));
            sb.Append("\n@when reactions (the same order):\n");
            if (whens.Count == 0) sb.Append("  (none)\n");
            else Grouped(sb, whens, schema, pathPrefix);
            return sb.ToString();
        }

        /// <summary>이 단계를 뒤에 오게 한 간선: 제 <c>after</c>와 남의 <c>before</c>. 없는 단계를 가리킨 <c>optional_*</c>는 간선이 아니다.</summary>
        private static List<string> Before(StageDecl stage, SchemaSet schema, string pathPrefix)
        {
            var lines = new List<string>();
            foreach (var r in stage.After) lines.Add(After(r.Name, r.Span, null, schema, pathPrefix));
            foreach (var r in stage.OptionalAfter)
                if (schema.StageIndex(r.Name) >= 0) lines.Add(After(r.Name, r.Span, null, schema, pathPrefix));
            foreach (var other in schema.StageOrder)
            {
                foreach (var r in other.Before)
                    if (r.Name == stage.Id) lines.Add(After(other.Id, r.Span, "before= of " + other.Id, schema, pathPrefix));
                foreach (var r in other.OptionalBefore)
                    if (r.Name == stage.Id) lines.Add(After(other.Id, r.Span, "optional_before= of " + other.Id, schema, pathPrefix));
            }
            return lines;
        }

        private static string After(string id, SourceSpan span, string? via, SchemaSet schema, string pathPrefix) =>
            "after " + id + "    " + CheckReport.Location(span, pathPrefix) + (via == null ? "" : " (" + via + ")");

        /// <summary>받은 순서(로드 → 정의)를 지키며 단계 순서로 묶는다. 단계 없는 것은 맨 뒤, 순서를 얻지 못한 단계(사이클)는 그 앞.</summary>
        private static void Grouped(StringBuilder sb, List<(string Stage, string Name, SourceSpan Span)> items, SchemaSet schema,
            string pathPrefix)
        {
            var keyed = new List<(long Key, int Index)>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                string stage = items[i].Stage;
                long rank = stage.Length == 0 ? int.MaxValue : schema.StageIndex(stage) >= 0 ? schema.StageIndex(stage) : int.MaxValue - 1;
                keyed.Add((rank * items.Count + i, i));
            }
            keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
            string? current = null;
            foreach (var (_, index) in keyed)
            {
                var item = items[index];
                string group = item.Stage.Length == 0 ? "(no stage)"
                    : schema.StageIndex(item.Stage) >= 0 ? item.Stage : item.Stage + " (not ordered)";
                if (group != current)
                {
                    sb.Append("    ").Append(group).Append('\n');
                    current = group;
                }
                sb.Append("      ").Append(item.Name).Append("    ").Append(CheckReport.Location(item.Span, pathPrefix)).Append('\n');
            }
        }
    }
}
