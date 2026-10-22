using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>회차의 결과 만들기: 스키마·Def 레지스트리 조립, 정책 함수 등록, 고정점 정리, Def 등록과 교체 (G19·G21·G28·G49·G124).</summary>
    public static partial class ContentLoader
    {
        /// <summary>
        /// 스키마를 다시 만들었으면 Def가 새 타입 객체를 가리키게 한다. 남은 Def의 타입과 컴포넌트는 남은 모드의 것이므로
        /// (실패한 모드의 것을 쓰는 모드는 함께 실패) 이름으로 항상 찾을 수 있다.
        /// </summary>
        private static void Rebind(DefRegistry defs, SchemaSet schema)
        {
            foreach (var record in defs.Defs)
            {
                for (var r = record; r != null; r = r.Replaced)
                {
                    schema.TryGetType(r.Type.Name, out var type);
                    r.Type = type;
                    foreach (var f in r.Fields) Rebind(f.Value, schema);
                }
            }
        }

        private static void Rebind(Value value, SchemaSet schema)
        {
            switch (value)
            {
                case ComponentValue cv:
                    schema.TryGetComponent(cv.Component.Name, out var component);
                    cv.Component = component;
                    foreach (var f in cv.Fields) Rebind(f.Value, schema);
                    break;
                case DictValue dict:
                    foreach (var item in dict.Items) Rebind(item.Value, schema);
                    break;
                case ListValue list:
                    foreach (var item in list.Items) Rebind(item, schema);
                    break;
            }
        }

        private static int CountSurvivors(List<ModUnit> units)
        {
            int n = 0;
            foreach (var u in units)
                if (!u.Failed) n++;
            return n;
        }

        /// <summary>
        /// 살아남은 모드로 스키마를 만든다. 오류가 난 모드는 실패시키고 의존 모드도 빼고 다시 만든다.
        /// 실패한 모드의 진단만 확정하고, 살아남은 모드의 진단은 마지막 회차의 것을 쓴다.
        /// </summary>
        private static SchemaSet BuildSchema(List<ModUnit> units, ModVisibility visibility, DiagnosticBag diagnostics)
        {
            while (true)
            {
                var stage = new StageDiagnostics(SurvivorIds(units));
                var mods = new List<ModSchema>();
                foreach (var u in units)
                    if (!u.Failed) mods.Add(u.Schema);
                var schema = SchemaBuilder.Build(mods, visibility, stage);
                if (!Settle(units, stage, diagnostics)) return schema;
            }
        }

        private static List<string> SurvivorIds(List<ModUnit> units)
        {
            var ids = new List<string>();
            foreach (var u in units)
                if (!u.Failed) ids.Add(u.Manifest.Id);
            return ids;
        }

        /// <summary>
        /// Def를 로드 순서대로 등록하고, 같은 대상에 보기 규칙을 건 모드들 사이의 순서를 본다 (G315). 고정점은 <see cref="BuildSchema"/>와 같다.
        /// </summary>
        private static DefRegistry BuildDefs(List<ModUnit> units, ModVisibility visibility, LoadOrder order,
            DiagnosticBag diagnostics)
        {
            while (true)
            {
                var stage = new StageDiagnostics(SurvivorIds(units));
                var registry = new DefRegistry();
                var viewRules = new List<List<ViewRuleDecl>>();
                foreach (var u in units)
                {
                    if (u.Failed) continue;
                    foreach (var record in u.Defs) Register(registry, record, visibility, order, stage);
                    viewRules.Add(u.Functions.ViewRules);
                }
                ViewRuleCheck.Collect(viewRules, order, stage);
                if (!Settle(units, stage, diagnostics)) return registry;
            }
        }

        /// <summary>
        /// 회차 결과를 반영한다. 자기 오류가 있는 모드를 먼저 실패시키고, 그런 모드가 없을 때만 충돌 오류가 있는 모드를
        /// 실패시킨다 (spec-gaps G28). 실패시킨 모드가 있으면 그 진단을 확정하고 의존 모드도 실패시킨 뒤 true.
        /// 없으면 살아남은 모드의 진단(경고 등)을 확정하고 false.
        /// </summary>
        private static bool Settle(List<ModUnit> units, StageDiagnostics stage, DiagnosticBag diagnostics)
        {
            bool anyFailed = false;
            foreach (bool conflictsOnly in new[] { false, true })
            {
                foreach (var u in units)
                {
                    if (u.Failed) continue;
                    string id = u.Manifest.Id;
                    if (!(conflictsOnly ? stage.HasConflictErrors(id) : stage.HasOwnErrors(id))) continue;
                    u.Failed = true;
                    anyFailed = true;
                    u.Bag.AddRange(stage.All(id));
                }
                if (anyFailed) break;
            }
            if (!anyFailed)
            {
                foreach (var u in units)
                    if (!u.Failed) u.Bag.AddRange(stage.All(u.Manifest.Id));
                return false;
            }
            foreach (var u in units) FailDependents(u, units, diagnostics);
            return true;
        }

        /// <summary>
        /// Def 하나를 등록한다. 중복 id는 오류이고, 의도적 교체는 <c>override=True</c> (spec S1).
        /// Def id는 타입과 무관하게 전역에서 하나다 (spec-gaps G19). 다른 모드의 Def는 전체 이름과 <c>override=True</c>로만 쓴다(G261).
        /// </summary>
        private static void Register(DefRegistry registry, DefRecord record, ModVisibility visibility, LoadOrder order,
            StageDiagnostics stage)
        {
            void Error(DefRecord at, string code, string message, string? hint = null, IReadOnlyList<SourceSpan>? related = null) =>
                stage.Error(at.Mod, code, message, at.Span, hint, related);
            void ConflictError(DefRecord at, string code, string message, string? hint = null,
                IReadOnlyList<SourceSpan>? related = null) =>
                stage.ConflictError(at.Mod, code, message, at.Span, hint, related);

            if (!registry.TryGet(record.Id, out var existing))
            {
                if (record.IsOverride)
                {
                    Error(record, Codes.DefBadOverride, "override=True but there is no Def '" + record.Id + "' to replace",
                        hint: DefIds.Hint(record.Id, registry.Ids) ?? "remove override=True to define a new Def");
                    return;
                }
                registry.Add(record);
                return;
            }

            var at = new[] { existing.Span };

            // 이 id의 원래 정의. 이미 교체되었어도 판정은 원래 주인을 기준으로 한다
            var original = existing;
            while (original.Replaced != null) original = original.Replaced;
            bool ownDef = original.Mod == record.Mod || existing.Mod == record.Mod;

            // 새 Def의 id는 늘 자기 모드의 것이므로(G261) 겹치는 것은 같은 모드 안의 중복뿐이다
            if (!record.IsOverride)
            {
                Error(record, Codes.DefDuplicateId, "Def id '" + record.Id + "' is already defined",
                    related: new[] { original.Span });
                return;
            }

            if (ownDef)
            {
                Error(record, Codes.DefBadOverride, "a mod cannot override its own Def '" + record.Id + "'", related: at);
                return;
            }
            if (!visibility.CanSee(record.Mod, original.Mod))
            {
                Error(record, Codes.SchNotVisible,
                    "Def '" + record.Id + "' belongs to mod '" + original.Mod + "', which mod '" + record.Mod +
                    "' does not depend on", hint: "add \"" + original.Mod + "\" to dependencies in mod.py", related: at);
                return;
            }
            if (existing.Type != record.Type)
            {
                Error(record, Codes.DefBadOverride,
                    "an override must keep the type: '" + record.Id + "' is a " + existing.Type.Name + ", not a " +
                    record.Type.Name, related: at);
                return;
            }
            // 두 모드가 같은 Def를 교체: 순서 관계가 선언되어 있으면 나중이 이기고, 아니면 둘 다 오류 (set 충돌과 같은 규칙)
            if (existing.IsOverride && existing.Mod != record.Mod && !order.IsOrdered(existing.Mod, record.Mod))
            {
                string message = "Def '" + record.Id + "' is overridden by both mod '" + existing.Mod + "' and mod '" +
                                 record.Mod + "', which have no load order between them";
                ConflictError(record, Codes.DefBadOverride, message, hint: "declare loadAfter in one of them", related: at);
                ConflictError(existing, Codes.DefBadOverride, message, hint: "declare loadAfter in one of them",
                    related: new[] { record.Span });
                return;
            }
            registry.Replace(record);
        }
    }
}
