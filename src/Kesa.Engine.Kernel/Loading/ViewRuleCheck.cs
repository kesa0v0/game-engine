using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// <c>@view_rule(대상)</c>의 대상을 풀고 검사한다 (spec-gaps G315).
    /// <list type="bullet">
    /// <item>대상은 그 모드에 보이는 컴포넌트(컴포넌트 층)나 Def 타입(그 타입 엔티티의 엔티티 층)이다. 남의 것에도 걸 수 있다.</item>
    /// <item>한 모드는 한 대상에 규칙을 하나만 건다.</item>
    /// <item>같은 대상에 규칙을 건 두 모드 사이에 순서 관계가 없으면 둘 다 오류다(G21의 <c>override</c> 충돌과 같은 원리):
    /// 나중에 로드된 쪽이 먼저 물리므로 어느 답이 이기는지 근거가 있어야 한다.</item>
    /// </list>
    /// </summary>
    internal static class ViewRuleCheck
    {
        private const string Hint = "write @view_rule(Hand) for a component, or @view_rule(Unit) for whether entities of a Def type are seen " +
                                    "(spec-gaps G315)";

        /// <summary>한 모드의 규칙: 대상을 풀고 같은 대상의 둘째 규칙을 오류로 알린다. 풀리지 않은 규칙은 목록에서 뺀다.</summary>
        public static void Resolve(List<ViewRuleDecl> rules, SchemaSet schema, ModScope scope, Func<string, bool> canSee,
            CallContext context)
        {
            var first = new Dictionary<string, ViewRuleDecl>(StringComparer.Ordinal);
            rules.RemoveAll(rule =>
            {
                if (!Target(rule, schema, scope, canSee, context)) return true;
                if (first.TryGetValue(rule.Target, out var earlier))
                {
                    context.Error(Codes.CmdViewPolicy, "a mod can put only one view rule on '" + rule.Target + "'", rule.Span,
                        "decide every case for '" + rule.Target + "' in one function (spec-gaps G315)", new[] { earlier.Span });
                    return true;
                }
                first.Add(rule.Target, rule);
                return false;
            });
        }

        private static bool Target(ViewRuleDecl rule, SchemaSet schema, ModScope scope, Func<string, bool> canSee, CallContext context)
        {
            string name = rule.Target;
            if (scope.TryGet(name, out _, out var bound))
            {
                context.Error(Codes.CmdBadDecorator, "@view_rule: '" + name + "' is this mod's own name, not a component or Def type",
                    rule.TargetSpan, Hint, new[] { bound });
                return false;
            }
            string? owner = null;
            if (schema.TryGetComponent(name, out var component))
            {
                rule.IsComponent = true;
                owner = component.Mod;
            }
            else if (schema.TryGetType(name, out var type))
            {
                owner = type.Mod;
            }
            if (owner != null)
            {
                if (canSee(owner)) return true;
                context.Error(Codes.SchNotVisible, "'" + name + "' belongs to mod '" + owner + "', which this mod does not depend on",
                    rule.TargetSpan, "add \"" + owner + "\" to dependencies in mod.py");
                return false;
            }
            if (schema.TryGetSignal(name, out _))
            {
                context.Error(Codes.CmdBadDecorator, "@view_rule: '" + name + "' is a signal; a rule decides a component or a Def type",
                    rule.TargetSpan, Hint);
                return false;
            }
            var names = new List<string>();
            foreach (var c in schema.Components)
                if (canSee(c.Mod)) names.Add(c.Name);
            foreach (var t in schema.Types)
                if (canSee(t.Mod)) names.Add(t.Name);
            context.Error(Codes.EvlUnknownName, "@view_rule: there is no component or Def type '" + name + "'", rule.TargetSpan,
                Suggest.Hint(name, names) ?? Hint);
            return false;
        }

        /// <summary>
        /// 살아남은 모드의 규칙을 로드 순서대로 모은다. 같은 대상에 규칙을 건 두 모드 사이에 순서가 없으면 둘 다 충돌 오류다.
        /// </summary>
        public static List<ViewRuleDecl> Collect(IEnumerable<List<ViewRuleDecl>> modsInLoadOrder, LoadOrder order, StageDiagnostics stage)
        {
            var all = new List<ViewRuleDecl>();
            var byTarget = new Dictionary<string, List<ViewRuleDecl>>(StringComparer.Ordinal);
            foreach (var rules in modsInLoadOrder)
            foreach (var rule in rules)
            {
                if (!byTarget.TryGetValue(rule.Target, out var same)) byTarget.Add(rule.Target, same = new List<ViewRuleDecl>());
                foreach (var other in same)
                {
                    if (other.Mod == rule.Mod || order.IsOrdered(other.Mod, rule.Mod)) continue;
                    string message = "mods '" + other.Mod + "' and '" + rule.Mod + "' both put a view rule on '" + rule.Target +
                                     "', but have no load order between them, so it is not clear which is asked first";
                    const string hint = "declare loadAfter in one of them (spec-gaps G315)";
                    stage.ConflictError(rule.Mod, Codes.CmdViewPolicy, message, rule.Span, hint, new[] { other.Span });
                    stage.ConflictError(other.Mod, Codes.CmdViewPolicy, message, other.Span, hint, new[] { rule.Span });
                }
                same.Add(rule);
                all.Add(rule);
            }
            return all;
        }
    }
}
