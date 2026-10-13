using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ③: 같은 Def를 패치하는 호출들의 충돌 판정 (spec-gaps G37·G41·G50).
    /// 모든 연산은 로드 순서(같은 모드 안에서는 호출 순서)로 적용된다. 그 결과가 로드 순서라는 우연에 달리는 조합만 오류이고,
    /// 순서가 선언된 모드끼리는 뒤 모드가 이기므로 덮어쓰는 경우만 경고한다.
    /// </summary>
    internal static class PatchConflicts
    {
        /// <param name="patches">③의 앞선 검사를 통과한 호출 (모드 로드 순서, 호출 순서)</param>
        public static void Check(IReadOnlyList<ResolvedPatch> patches, LoadOrder order, StageDiagnostics stage)
        {
            // 같은 Def를 가리키는 호출끼리 묶는다 (부모와 자식 Def 사이의 순서는 ④의 상속이 정한다, G50)
            var byTarget = new Dictionary<string, List<ResolvedPatch>>(System.StringComparer.Ordinal);
            var targets = new List<string>();
            foreach (var p in patches)
            {
                if (!byTarget.TryGetValue(p.Call.TargetId, out var list))
                {
                    list = new List<ResolvedPatch>();
                    byTarget.Add(p.Call.TargetId, list);
                    targets.Add(p.Call.TargetId);
                }
                list.Add(p);
            }

            foreach (var target in targets)
            {
                var list = byTarget[target];
                var reported = new HashSet<string>(System.StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++) CheckPair(list[i].Call, list[j].Call, order, stage, reported);
                }
            }
        }

        /// <param name="a">앞선 호출 (로드 순서상 먼저)</param>
        /// <param name="b">뒤따르는 호출</param>
        private static void CheckPair(PatchCall a, PatchCall b, LoadOrder order, StageDiagnostics stage, HashSet<string> reported)
        {
            if (!Overlaps(a.Path, b.Path)) return;

            if (a.Mod == b.Mod)
            {
                // 같은 모드가 같은 필드를 두 번 set하면 나중 호출이 이긴다 (G37)
                if (a.Op == PatchOp.Set && b.Op == PatchOp.Set && b.Path.IsPrefixOf(a.Path))
                {
                    stage.Warning(b.Mod, Codes.PatOverridden,
                        "this patch.set replaces the value set at " + a.Span + " in the same mod", b.Span,
                        related: new[] { a.Span });
                }
                return;
            }

            if (order.IsOrdered(a.Mod, b.Mod))
            {
                // 뒤 모드가 이긴다. 앞 모드의 값을 통째로 덮어쓰면 알린다 (G37·G41)
                if (b.Op == PatchOp.Set && b.Path.IsPrefixOf(a.Path) &&
                    (a.Op == PatchOp.Set || a.Op == PatchOp.Add || a.Op == PatchOp.Append))
                {
                    stage.Warning(b.Mod, Codes.PatOverridden,
                        "this patch.set replaces what mod '" + a.Mod + "' did with patch." + a.Op.ToString().ToLowerInvariant() +
                        " at " + a.Span, b.Span, related: new[] { a.Span });
                }
                return;
            }

            // 순서가 선언되지 않은 두 모드: 같은 연산의 add끼리, append끼리만 순서와 무관하다
            if (a.Op == b.Op && (a.Op == PatchOp.Add || a.Op == PatchOp.Append) && a.Path.SameAs(b.Path)) return;

            // 한 쌍(모드 둘, 대상 Def)당 한 번만 보고한다
            string key = a.Mod + "\n" + b.Mod + "\n" + a.TargetId;
            if (!reported.Add(key)) return;

            string message = "mods '" + a.Mod + "' and '" + b.Mod + "' both patch '" + a.TargetId + "' (" +
                             Describe(a) + " at " + a.Span + ", " + Describe(b) + " at " + b.Span +
                             ") and have no load order between them";
            stage.ConflictError(a.Mod, Codes.PatConflict, message, a.Span, hint: "declare loadAfter in one of the two mods",
                related: new[] { b.Span });
            stage.ConflictError(b.Mod, Codes.PatConflict, message, b.Span, hint: "declare loadAfter in one of the two mods",
                related: new[] { a.Span });
        }

        private static bool Overlaps(PatchPath a, PatchPath b) => a.IsPrefixOf(b) || b.IsPrefixOf(a);

        private static string Describe(PatchCall call) =>
            "patch." + call.Op.ToString().ToLowerInvariant() + " " + call.Path.Canonical;
    }
}
