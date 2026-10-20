using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ④ 상속 펼치기 (spec S1, spec-gaps G52·G55·G57·G58). 부모부터 자식 순서로 Def마다 <see cref="ResolvedDef"/>를 만든다:
    /// 부모의 최종 값을 물려받고, 자식이 적은 값(<c>inherit</c> 식은 부모 값으로 평가)을 병합한다.
    /// const는 적으면 교체(<c>merge="append"</c> 리스트는 부모 뒤에 붙임), 컴포넌트는 값 단위로 병합하고
    /// 키 컴포넌트는 줄 키 단위로 합친다. 패치 적용은 이 뒤의 일이다.
    /// </summary>
    internal sealed class InheritanceExpander
    {
        private readonly DefRegistry _defs;
        private readonly SchemaSet _schema;
        private readonly ModVisibility _visibility;
        private readonly Func<string, ModEnvironment> _environment;
        private readonly StageDiagnostics _stage;
        private readonly PatchLog _log;
        private readonly PatchApplier _patcher;

        private readonly Dictionary<string, ResolvedDef?> _done = new Dictionary<string, ResolvedDef?>(StringComparer.Ordinal);
        private readonly List<DefRecord> _active = new List<DefRecord>();

        private InheritanceExpander(DefRegistry defs, SchemaSet schema, ModVisibility visibility,
            Func<string, ModEnvironment> environment, StageDiagnostics stage, PatchLog log, HashSet<string> conflicted)
        {
            _defs = defs;
            _schema = schema;
            _visibility = visibility;
            _environment = environment;
            _stage = stage;
            _log = log;
            _patcher = new PatchApplier(stage, conflicted);
        }

        public static ResolvedDefs Expand(DefRegistry defs, SchemaSet schema, ModVisibility visibility,
            Func<string, ModEnvironment> environment, StageDiagnostics stage, PatchLog log, HashSet<string> conflicted)
        {
            var expander = new InheritanceExpander(defs, schema, visibility, environment, stage, log, conflicted);
            var result = new ResolvedDefs();
            foreach (var record in defs.Defs)
            {
                var resolved = expander.Resolve(record);
                if (resolved != null) result.Add(resolved);
            }
            return result;
        }

        /// <summary>null이면 이 Def 또는 조상에 오류가 있다 (자식은 연쇄 오류 없이 건너뛴다, G57).</summary>
        private ResolvedDef? Resolve(DefRecord record)
        {
            if (_done.TryGetValue(record.Id, out var cached)) return cached;

            int at = _active.IndexOf(record);
            if (at >= 0)
            {
                ReportCycle(at);
                return null;
            }

            _active.Add(record);
            ResolvedDef? result;
            try
            {
                result = Build(record);
            }
            finally
            {
                _active.RemoveAt(_active.Count - 1);
            }
            _done[record.Id] = result;
            return result;
        }

        private void ReportCycle(int start)
        {
            var cycle = new List<DefRecord>();
            for (int i = start; i < _active.Count; i++) cycle.Add(_active[i]);
            var names = new List<string>();
            foreach (var d in cycle) names.Add(d.Id);
            names.Add(cycle[0].Id);
            string chain = string.Join(" -> ", names);
            foreach (var d in cycle)
            {
                var related = new List<SourceSpan>();
                foreach (var other in cycle)
                    if (other != d) related.Add(other.Span);
                _stage.Error(d.Mod, Codes.InhCycle, "the parent chain of Def '" + d.Id + "' is a cycle: " + chain,
                    d.Parent?.Span ?? d.Span, related: related);
            }
        }

        private ResolvedDef? Build(DefRecord record)
        {
            ResolvedDef? parent = null;
            if (record.Parent is NameRef parentRef)
            {
                if (!_defs.TryGet(parentRef.Name, out var parentRecord))
                {
                    _stage.Error(record.Mod, Codes.InhParentNotFound,
                        "Def '" + record.Id + "' has parent '" + parentRef.Name + "', which does not exist", parentRef.Span,
                        hint: DefIds.Hint(parentRef.Name, _defs.Ids));
                    return null;
                }
                var original = parentRecord;
                while (original.Replaced != null) original = original.Replaced;
                if (!_visibility.CanSee(record.Mod, original.Mod))
                {
                    _stage.Error(record.Mod, Codes.SchNotVisible,
                        "parent '" + parentRef.Name + "' belongs to mod '" + original.Mod + "', which mod '" + record.Mod +
                        "' does not depend on", parentRef.Span, hint: "add \"" + original.Mod + "\" to dependencies in mod.py",
                        related: new[] { parentRecord.Span });
                    return null;
                }
                if (parentRecord.Type != record.Type)
                {
                    _stage.Error(record.Mod, Codes.InhParentType,
                        "Def '" + record.Id + "' is a " + record.Type.Name + " but its parent '" + parentRef.Name + "' is a " +
                        parentRecord.Type.Name, parentRef.Span, related: new[] { parentRecord.Span },
                        hint: "a parent must have the same Def type");
                    return null;
                }
                parent = Resolve(parentRecord);
                if (parent == null) return null;
            }

            var resolved = new ResolvedDef(record, parent);
            if (parent != null) CopyInherited(parent, resolved);

            var evaluator = new DeferredEvaluator(_environment(record.Mod), _schema, record, parent);
            bool failed = false;
            foreach (var field in record.Fields)
            {
                try
                {
                    ApplyField(resolved, field, evaluator);
                }
                catch (ScriptError)
                {
                    failed = true;
                }
            }
            foreach (var d in evaluator.Bag.ToSortedList())
                _stage.Error(record.Mod, d.Code, d.Message, d.Span, d.Hint, d.Related);
            if (failed || evaluator.Bag.HasErrors) return null;

            // 이 Def의 패치를 적용한다. 자식은 이 결과를 물려받고 inherit도 이 값을 읽는다 (G50·G55)
            _patcher.Apply(resolved, _log.ForDef(record.Id));
            return resolved;
        }

        private static void CopyInherited(ResolvedDef parent, ResolvedDef child)
        {
            foreach (var k in parent.Consts.Keys)
            {
                parent.Consts.TryGet(k, out var v);
                child.Consts.Set(k, v.AsInherited());
            }
            foreach (var k in parent.Components.Keys)
            {
                parent.Components.TryGet(k, out var c);
                child.Components.Set(k, c.CloneInherited());
            }
        }

        private void ApplyField(ResolvedDef resolved, DefField field, DeferredEvaluator evaluator)
        {
            var record = resolved.Record;
            foreach (var k in record.Type.Consts)
            {
                if (k.Name != field.Name) continue;
                // inherit 식이 새로 적은 Def id도 이 Def의 모드 기준이다 (G261)
                var value = DefIds.Normalize(Evaluated(field.Value, evaluator), k.Spec.Type, record.Mod);
                // inherit 식이 읽은 부모 값의 패치는 이 값에도 영향을 줬다 (덤프, G76)
                var patches = evaluator.ReadPatches;
                // inherit 식의 결과는 부모 값을 이미 포함한 전체 값이다 (G59)
                if (k.Spec.Merge == MergeMode.Append && !(field.Value is DeferredValue) && value is ListValue own &&
                    resolved.Consts.TryGet(k.Name, out var inherited) && inherited.Origin == ValueOrigin.Inherited &&
                    inherited.Value is ListValue baseList)
                {
                    var items = new List<Value>(baseList.Items);
                    items.AddRange(own.Items);
                    value = new ListValue(items);
                    patches = inherited.Patches;
                }
                resolved.Consts.Set(k.Name, new ResolvedValue(value, ValueOrigin.Own, record, field.Span, patches: patches));
                return;
            }

            foreach (var slot in record.Type.Components)
            {
                if (slot.Component.Name != field.Name) continue;
                ApplyComponent(resolved, slot.Component, field, Evaluated(field.Value, evaluator), evaluator);
                return;
            }
        }

        /// <summary>값 하나를 평가한다. <see cref="DeferredEvaluator.ReadPatches"/>는 이 값이 읽은 것만 남는다.</summary>
        private static Value Evaluated(Value value, DeferredEvaluator evaluator)
        {
            evaluator.ResetReads();
            return value is DeferredValue deferred ? evaluator.EvaluateData(deferred.Expr) : value;
        }

        private void ApplyComponent(ResolvedDef resolved, ComponentType component, DefField field, Value value,
            DeferredEvaluator evaluator)
        {
            var record = resolved.Record;
            if (!resolved.Components.TryGet(component.Name, out var target))
            {
                target = new ResolvedComponent(component);
                resolved.Components.Set(component.Name, target);
            }

            if (!component.IsKeyed)
            {
                if (!(value is ComponentValue cv) || cv.Component != component)
                {
                    throw Fail(evaluator, record, "'" + field.Name + "' must be " + component.Name + "(...), not " + value.KindName,
                        field.Span);
                }
                SetMembers(target.Members, cv, record, evaluator);
                return;
            }

            if (!(value is DictValue dict))
            {
                throw Fail(evaluator, record,
                    "'" + field.Name + "' is a keyed component; write {\"Key\": " + component.Name + "(...)}", field.Span);
            }
            foreach (var item in dict.Items)
            {
                if (!(item.Key is StrValue key))
                    throw Fail(evaluator, record, "keys of '" + field.Name + "' must be Def id strings", item.Span);
                var rowValue = Evaluated(item.Value, evaluator);
                if (!(rowValue is ComponentValue row) || row.Component != component)
                {
                    throw Fail(evaluator, record,
                        "each value of '" + field.Name + "' must be " + component.Name + "(...), not " + rowValue.KindName,
                        item.Span);
                }
                string rowKey = DefIds.Resolve(key.Value, record.Mod);
                if (!target.Rows.TryGet(rowKey, out var resolvedRow))
                {
                    resolvedRow = new ResolvedRow();
                    target.Rows.Set(rowKey, resolvedRow);
                }
                SetMembers(resolvedRow.Members, row, record, evaluator);
            }
        }

        private static void SetMembers(OrderedMap<ResolvedValue> members, ComponentValue source, DefRecord record,
            DeferredEvaluator evaluator)
        {
            foreach (var f in source.Fields)
            {
                var value = Evaluated(f.Value, evaluator);
                if (f.Value is DeferredValue && source.Component.TryGetMember(f.Name, out var member))
                    value = DefIds.Normalize(value, member.Spec.Type, record.Mod);
                members.Set(f.Name, new ResolvedValue(value, ValueOrigin.Own, record, f.Span, patches: evaluator.ReadPatches));
            }
        }

        private static ScriptError Fail(DeferredEvaluator evaluator, DefRecord record, string message, SourceSpan span)
        {
            evaluator.Bag.Error(Codes.DefBadValue, message, span, record.Mod);
            return new ScriptError();
        }
    }
}
