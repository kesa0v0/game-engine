using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ③: ②b가 기록한 패치 호출을 검사한다 (spec-gaps G35). 지금은 대상 Def가 있는지와 보이는 범위(G38)까지다.
    /// 오류는 호출한 줄을 가리키고, 모두 그 모드 자신의 오류다.
    /// </summary>
    internal static class PatchResolver
    {
        /// <param name="mods">살아남은 모드와 그 호출 (모드 로드 순서)</param>
        public static void Resolve(IReadOnlyList<KeyValuePair<ModManifest, IReadOnlyList<PatchCall>>> mods, DefRegistry defs,
            ModVisibility visibility, StageDiagnostics stage)
        {
            foreach (var entry in mods)
            {
                foreach (var call in entry.Value) CheckTarget(entry.Key, call, defs, visibility, stage);
            }
        }

        /// <summary>
        /// 대상은 보이는 범위(자기 + 필수 의존의 닫힘)의 Def이거나, <c>optional</c>로 선언한 모드의 Def를 그 모드의
        /// <c>mod_loaded</c> 가드 안에서 쓰는 경우다 (G38). 패치는 id로 찾은 현재 Def(교체본)에 적용되고 (G43),
        /// 교체된 Def는 교체한 모드나 원래 정의한 모드 중 하나라도 보이면 된다.
        /// </summary>
        private static void CheckTarget(ModManifest mod, PatchCall call, DefRegistry defs, ModVisibility visibility,
            StageDiagnostics stage)
        {
            if (!defs.TryGet(call.TargetId, out var record))
            {
                stage.Error(mod.Id, Codes.PatTargetNotFound, "there is no Def '" + call.TargetId + "' to patch",
                    call.TargetSpan, hint: Suggest.Hint(call.TargetId, defs.Ids));
                return;
            }

            string? optionalOwner = null;
            for (var r = record; r != null; r = r.Replaced)
            {
                if (visibility.CanSee(mod.Id, r.Mod)) return;
                if (optionalOwner == null && IsOptional(mod, r.Mod)) optionalOwner = r.Mod;
            }

            if (optionalOwner != null)
            {
                if (IsGuarded(call, optionalOwner)) return;
                stage.Error(mod.Id, Codes.PatNeedsGuard,
                    "Def '" + call.TargetId + "' belongs to mod '" + optionalOwner + "', which is optional for mod '" + mod.Id +
                    "'; patches of its Defs must be inside a guard",
                    call.TargetSpan, hint: "wrap the patch in: if mod_loaded(\"" + optionalOwner + "\"):",
                    related: new[] { record.Span });
                return;
            }

            stage.Error(mod.Id, Codes.PatTargetNotVisible,
                "Def '" + call.TargetId + "' belongs to mod '" + record.Mod + "', which mod '" + mod.Id + "' does not depend on",
                call.TargetSpan, hint: "add \"" + record.Mod + "\" to dependencies in mod.py", related: new[] { record.Span });
        }

        private static bool IsGuarded(PatchCall call, string owner)
        {
            foreach (var g in call.Guards)
                if (g == owner) return true;
            return false;
        }

        private static bool IsOptional(ModManifest mod, string owner)
        {
            foreach (var o in mod.Optional)
                if (o.Id == owner) return true;
            return false;
        }
    }
}
