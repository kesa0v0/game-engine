using System;
using System.Collections.Generic;
using System.Numerics;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ⑥ 값 검증 (spec S1, spec-gaps G67·G68): 최종 값을 스키마와 대조한다. 기본값이 없는 <c>const</c>가 비어 있는지(<c>abstract</c> Def는 면제),
    /// 적힌 값의 타입, <c>min</c>/<c>max</c> 범위를 본다. Def를 가리키는 id의 존재는 ⑤가 보았다.
    /// 오류는 값을 처음 적은 모드의 것이다(비어 있는 값은 그 Def나 컴포넌트를 붙인 패치의 모드).
    /// </summary>
    internal sealed class Validator
    {
        private readonly ModVisibility _visibility;
        private readonly StageDiagnostics _stage;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        private Validator(ModVisibility visibility, StageDiagnostics stage)
        {
            _visibility = visibility;
            _stage = stage;
        }

        public static void Check(ResolvedDefs resolved, SchemaSet schema, ModVisibility visibility, StageDiagnostics stage)
        {
            var validator = new Validator(visibility, stage);
            foreach (var def in resolved.Defs) validator.CheckDef(def);
            // 스키마가 선언한 기본값도 같은 타입·범위 규칙으로 본다. 책임은 선언한 모드다 (G73)
            foreach (var decl in MemberDefaults.DeclaredDefaults(schema)) validator.CheckValue(MemberDefaults.DeclaredOrigin(decl), decl);
        }

        private void Report(string mod, string code, string message, SourceSpan span, string? hint = null)
        {
            if (!_reported.Add(mod + "\n" + span + "\n" + code + "\n" + message)) return;
            _stage.Error(mod, code, message, span, hint);
        }

        private void CheckDef(ResolvedDef def)
        {
            foreach (var decl in def.Type.Consts)
            {
                if (!_visibility.CanSee(def.Mod, decl.Mod)) continue;
                if (def.Consts.TryGet(decl.Name, out var value)) CheckValue(value, decl);
                else CheckMissing(def, decl, def.Mod, def.Record.Span, "Def '" + def.Id + "'");
            }

            var slots = new HashSet<ComponentType>();
            foreach (var slot in def.Type.Components)
            {
                if (!_visibility.CanSee(def.Mod, slot.Mod)) continue;
                slots.Add(slot.Component);
                def.Components.TryGet(slot.Component.Name, out var rc);
                CheckComponent(def, slot.Component, rc, def.Mod, def.Record.Span);
            }
            foreach (var name in def.Components.Keys)
            {
                def.Components.TryGet(name, out var rc);
                if (slots.Contains(rc.Component)) continue;
                // 패치가 붙인 컴포넌트: 비어 있는 값은 붙인 패치의 책임
                var by = rc.AttachedBy;
                CheckComponent(def, rc.Component, rc, by?.Mod ?? def.Mod, by?.Call.Span ?? def.Record.Span);
            }
        }

        private void CheckComponent(ResolvedDef def, ComponentType component, ResolvedComponent? rc, string mod, SourceSpan at)
        {
            string where = "component '" + component.Name + "' of '" + def.Id + "'";
            if (component.IsKeyed)
            {
                if (rc == null) return;
                foreach (var key in rc.Rows.Keys)
                {
                    rc.Rows.TryGet(key, out var row);
                    var by = row.AttachedBy;
                    string rowWhere = "row '" + key + "' of '" + component.Name + "' on '" + def.Id + "'";
                    CheckMembers(def, component, row.Members, by?.Mod ?? mod, by?.Call.Span ?? at, rowWhere);
                    if (component.Keys == KeysMode.Listed) CheckCreatable(def, component, row.Members, by?.Mod ?? mod, by?.Call.Span ?? at, rowWhere);
                }
                return;
            }
            CheckMembers(def, component, rc?.Members, mod, at, where);
        }

        private void CheckMembers(ResolvedDef def, ComponentType component, OrderedMap<ResolvedValue>? members, string mod,
            SourceSpan at, string where)
        {
            foreach (var member in component.Members)
            {
                if (member.Kind == MemberKind.Derived || member.Kind == MemberKind.Ref) continue;
                if (members != null && members.TryGet(member.Name, out var value)) CheckValue(value, member);
                else if (member.Kind == MemberKind.Const) CheckMissing(def, member, mod, at, where);
            }
        }

        // ---- 필수 값 (G67) ----

        private void CheckMissing(ResolvedDef def, MemberDecl decl, string mod, SourceSpan at, string where)
        {
            if (def.IsAbstract || decl.Kind != MemberKind.Const) return;
            var spec = decl.Spec;
            if (spec.Default != null || spec.Type is ListTypeRef || spec.Type is DictTypeRef) return;
            Report(mod, Codes.ValMissing, where + " has no value for '" + decl.Name + "'", at,
                "give '" + decl.Name + "' a value, or give it a default= in the schema");
        }

        /// <summary>
        /// Def가 적은 <c>keys="listed"</c> 줄은 엔티티를 만들 때 생기는데, 그때 줄 값을 줄 길이 없다(생성 초기값은 키 없는 컴포넌트만).
        /// 그래서 줄의 필수 Ref(Def는 엔티티를 못 줌, G32)와 기본값 없는 <c>field</c>는 Def가 채워야 하고, 못 채우면 그 Def로는
        /// 엔티티를 만들 수 없으니 로드 오류다 (spec-gaps G94).
        /// </summary>
        private void CheckCreatable(ResolvedDef def, ComponentType component, OrderedMap<ResolvedValue> members, string mod,
            SourceSpan at, string where)
        {
            if (def.IsAbstract) return;
            foreach (var member in component.Members)
            {
                var spec = member.Spec;
                if (member.Kind == MemberKind.Ref && !spec.Optional)
                {
                    Report(mod, Codes.ValMissing,
                        where + " needs an entity for the required ref '" + member.Name + "', which a Def cannot give", at,
                        "declare it optional=True, or add the row at runtime with AddRow (spec-gaps G94)");
                }
                else if (member.Kind == MemberKind.Field && !members.Contains(member.Name) && spec.Default == null &&
                         spec.DefaultFrom == null)
                {
                    Report(mod, Codes.ValMissing, where + " has no value for '" + member.Name + "', which has no default", at,
                        "write '" + member.Name + "' in the row, or give it a default= in the schema (spec-gaps G94)");
                }
            }
        }

        // ---- 타입과 범위 (G68) ----

        private void CheckValue(ResolvedValue origin, MemberDecl decl)
        {
            var spec = decl.Spec;
            var blame = ValueBlame.Of(origin, SourceSpan.None, origin.Span);
            string? problem = PatchOperationCheck.Mismatch(spec.Type, origin.Value);
            if (problem != null)
            {
                string hint = ReferenceEquals(spec.Type, PrimitiveTypeRef.Int) && origin.Value is FixedValue
                    ? "an int takes whole numbers; a fixed value could lose its fraction"
                    : "";
                Report(blame.Mod, Codes.ValType, "'" + decl.Name + "': " + problem, blame.Span, hint.Length == 0 ? null : hint);
                return;
            }
            if (spec.Min == null && spec.Max == null) return;
            foreach (var leaf in NumericLeaves(origin.Value))
            {
                if (spec.Min != null && Compare(leaf, spec.Min) < 0)
                {
                    Report(blame.Mod, Codes.ValRange,
                        "'" + decl.Name + "': " + Show(leaf) + " is below min " + Show(spec.Min), blame.Span);
                }
                if (spec.Max != null && Compare(leaf, spec.Max) > 0)
                {
                    Report(blame.Mod, Codes.ValRange,
                        "'" + decl.Name + "': " + Show(leaf) + " is above max " + Show(spec.Max), blame.Span);
                }
            }
        }

        private static IEnumerable<Value> NumericLeaves(Value value)
        {
            switch (value)
            {
                case IntValue _:
                case FixedValue _:
                    yield return value;
                    break;
                case ListValue list:
                    foreach (var item in list.Items)
                        foreach (var leaf in NumericLeaves(item)) yield return leaf;
                    break;
                case DictValue dict:
                    foreach (var item in dict.Items)
                        foreach (var leaf in NumericLeaves(item.Value)) yield return leaf;
                    break;
            }
        }

        private static BigInteger Scaled(Value value) =>
            value is IntValue i ? new BigInteger(i.Value) * Fixed.Scale : new BigInteger(((FixedValue)value).Value.Raw);

        private static int Compare(Value a, Value b) => Scaled(a).CompareTo(Scaled(b));

        private static string Show(Value value) =>
            value is IntValue i ? i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ((FixedValue)value).Value.ToString();
    }
}
