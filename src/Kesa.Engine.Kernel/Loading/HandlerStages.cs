using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 핸들러와 <c>when</c> 반응의 <c>stage=</c> 검사 (spec S5, spec-gaps G152·G153). 단계는 스키마(②a)에서 정해지므로 ②b의 선언 뒤에 본다.
    /// 없는 단계는 "혹시 …?"와 함께, 의존하지 않는 모드의 단계는 의존을 더하라고 알린다.
    /// </summary>
    public static class HandlerStages
    {
        public static void Check(IReadOnlyList<HandlerDecl> handlers, SchemaSet schema, ModVisibility visibility, CallContext context)
        {
            foreach (var h in handlers) CheckOne(h.Mod, h.Stage, h.StageSpan, schema, visibility, context);
        }

        public static void Check(IReadOnlyList<WhenDecl> whens, SchemaSet schema, ModVisibility visibility, CallContext context)
        {
            foreach (var w in whens) CheckOne(w.Mod, w.Stage, w.StageSpan, schema, visibility, context);
        }

        private static void CheckOne(string mod, string? stageId, SourceSpan span, SchemaSet schema, ModVisibility visibility,
            CallContext context)
        {
            if (stageId == null) return;
            if (!schema.TryGetStage(stageId, out var stage))
            {
                var ids = new List<string>();
                foreach (var s in schema.StageOrder) ids.Add(s.Id);
                context.Error(Codes.SchUnknownName, "unknown stage '" + stageId + "'", span,
                    hint: Suggest.Hint(stageId, ids) ?? "declare it with stage(\"" + mod + ".name\", ...) in a schema file (spec-gaps G152)");
                return;
            }
            if (!visibility.CanSee(mod, stage.Mod))
            {
                context.Error(Codes.SchNotVisible,
                    "stage '" + stageId + "' belongs to mod '" + stage.Mod + "', which mod '" + mod + "' does not depend on",
                    span, hint: "add \"" + stage.Mod + "\" to dependencies in mod.py (spec-gaps G153)");
            }
        }
    }
}
