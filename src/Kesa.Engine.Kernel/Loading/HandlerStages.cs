using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 핸들러의 <c>stage=</c> 검사 (spec S5, spec-gaps G152·G153). 단계는 스키마(②a)에서 정해지므로 ②b의 핸들러 선언 뒤에 본다.
    /// 없는 단계는 "혹시 …?"와 함께, 의존하지 않는 모드의 단계는 의존을 더하라고 알린다.
    /// </summary>
    public static class HandlerStages
    {
        public static void Check(IReadOnlyList<HandlerDecl> handlers, SchemaSet schema, ModVisibility visibility, CallContext context)
        {
            foreach (var h in handlers)
            {
                if (h.Stage == null) continue;
                if (!schema.TryGetStage(h.Stage, out var stage))
                {
                    var ids = new List<string>();
                    foreach (var s in schema.StageOrder) ids.Add(s.Id);
                    context.Error(Codes.SchUnknownName, "unknown stage '" + h.Stage + "'", h.StageSpan,
                        hint: Suggest.Hint(h.Stage, ids) ?? "declare it with stage(\"" + h.Mod + ".name\", ...) in a schema file (spec-gaps G152)");
                    continue;
                }
                if (!visibility.CanSee(h.Mod, stage.Mod))
                {
                    context.Error(Codes.SchNotVisible,
                        "stage '" + h.Stage + "' belongs to mod '" + stage.Mod + "', which mod '" + h.Mod + "' does not depend on",
                        h.StageSpan, hint: "add \"" + stage.Mod + "\" to dependencies in mod.py (spec-gaps G153)");
                }
            }
        }
    }
}
