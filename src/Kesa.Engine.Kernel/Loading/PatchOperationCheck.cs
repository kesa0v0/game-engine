using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// ③: 경로를 통과한 패치 호출에서 연산이 그 대상에 허용되는지(spec-gaps G36·G44·G51)와 값의 모양이 맞는지(G39·G48)를 본다.
    /// 값의 모양은 "이 자리에 이 종류의 값이 올 수 있는가"까지다. 범위와 가리키는 Def id의 존재는 ⑥, 그 칸에 값이 있는지와
    /// 컴포넌트가 이미 붙어 있는지는 ④가 본다 (G42). 오류는 호출한 줄을 가리키고 모두 그 모드 자신의 오류다.
    /// </summary>
    internal static class PatchOperationCheck
    {
        public static bool Check(ModManifest mod, ResolvedPatch patch, StageDiagnostics stage)
        {
            var call = patch.Call;
            var target = patch.Target;
            string op = "patch." + call.Op.ToString().ToLowerInvariant();

            bool Fail(string code, string message, SourceSpan span, string? hint = null)
            {
                stage.Error(mod.Id, code, message, span, hint);
                return false;
            }

            switch (target.Kind)
            {
                case PatchTargetKind.Component:
                case PatchTargetKind.Row:
                    return CheckAttachOrDetach(mod, patch, op, stage);
            }

            if (target.Member!.Kind == MemberKind.Ref)
            {
                return Fail(Codes.PatBadOp,
                    "'" + target.Member.Name + "' is a Ref column holding runtime entities and cannot be patched", call.PathSpan);
            }

            // 맵 항목은 경로가 아니라 키로 지운다 (G51). 값 타입에 따른 허용 검사보다 먼저 안내한다
            if (call.Op == PatchOp.Remove && target.Entry != null && target.EntryKey != null)
            {
                return Fail(Codes.PatBadOp, "a map entry is removed by key, not by path", call.PathSpan,
                    hint: "write patch.remove(\"" + call.TargetId + "\", \"" + EntryOwnerPath(call, target) +
                          "\", " + KeyLiteral(target.EntryKey.Value) + ")");
            }

            var type = target.ValueType!;
            var allowed = AllowedOperations(type);
            if (!allowed.Contains(call.Op))
            {
                return Fail(Codes.PatBadOp,
                    op + " cannot be used on a value of type " + type.Display + " ('" + call.Path.Text + "')", call.PathSpan,
                    hint: "use " + Join(allowed));
            }

            switch (call.Op)
            {
                case PatchOp.Set:
                case PatchOp.Add:
                    return CheckShape(mod, call, type, call.Value!, stage);

                case PatchOp.Append:
                    // 컬렉션에는 같은 종류의 컬렉션을 붙인다 (G51)
                    if (type is ListTypeRef && !(call.Value is ListValue))
                    {
                        return Fail(Codes.PatBadValue,
                            "patch.append to a list takes a list of elements, found " + call.Value!.KindName, call.ValueSpan,
                            hint: "write [value] to append one element");
                    }
                    if (type is DictTypeRef && !(call.Value is DictValue))
                    {
                        return Fail(Codes.PatBadValue,
                            "patch.append to a map takes a map {key: value}, found " + call.Value!.KindName, call.ValueSpan);
                    }
                    return CheckShape(mod, call, type, call.Value!, stage);

                default: // Remove
                    if (call.Value == null)
                    {
                        return Fail(Codes.PatBadOp,
                            op + " on " + type.Display + " needs the " + (type is ListTypeRef ? "element" : "key") +
                            " to remove as a third argument", call.Span);
                    }
                    var piece = type is ListTypeRef list ? list.Element : ((DictTypeRef)type).Key;
                    return CheckShape(mod, call, piece, call.Value, stage);
            }
        }

        /// <summary>컴포넌트와 키 컴포넌트의 줄: 붙이기(<c>set</c> + 컴포넌트 값)와 떼기(<c>remove</c> 두 인자)만 된다 (G44·G51).</summary>
        private static bool CheckAttachOrDetach(ModManifest mod, ResolvedPatch patch, string op, StageDiagnostics stage)
        {
            var call = patch.Call;
            var target = patch.Target;
            var component = target.Component!;
            string what = target.Kind == PatchTargetKind.Row
                ? "row '" + target.RowKey + "' of '" + component.Name + "'"
                : "component '" + component.Name + "'";

            bool Fail(string code, string message, SourceSpan span, string? hint = null)
            {
                stage.Error(mod.Id, code, message, span, hint);
                return false;
            }

            if (call.Op == PatchOp.Add || call.Op == PatchOp.Append)
            {
                return Fail(Codes.PatBadOp, op + " cannot be used on " + what, call.PathSpan,
                    hint: "use patch.set(..., " + component.Name + "(...)) to attach it or patch.remove to detach it");
            }

            // 모든 Def가 가지는 것은 붙이거나 뗄 수 없다. 값은 경로를 따라 하나씩 패치한다 (G44)
            bool alwaysThere = target.Kind == PatchTargetKind.Row
                ? target.IsTypeSlot && component.Keys == KeysMode.All
                : target.IsTypeSlot;
            if (alwaysThere)
            {
                string why = target.Kind == PatchTargetKind.Row
                    ? "every row of '" + component.Name + "' exists on every " + target.DefType.Name
                    : "every " + target.DefType.Name + " has '" + component.Name + "'";
                return Fail(Codes.PatBadOp, op + " cannot attach or detach " + what + ": " + why, call.PathSpan,
                    hint: "patch its values one by one, for example " + component.Name +
                          (target.Kind == PatchTargetKind.Row ? "." + target.RowKey + ".<value>" : ".<value>"));
            }

            if (call.Op == PatchOp.Remove)
            {
                if (call.Value != null)
                {
                    return Fail(Codes.PatBadOp, "removing " + what + " takes no third argument", call.ValueSpan,
                        hint: "write patch.remove(\"" + call.TargetId + "\", \"" + call.Path.Text + "\")");
                }
                return true;
            }

            // Set: 붙이기. 값은 그 컴포넌트의 값 하나다
            if (!(call.Value is ComponentValue cv) || cv.Component != component)
            {
                return Fail(Codes.PatBadValue,
                    "attaching " + what + " takes a " + component.Name + "(...) value, found " +
                    (call.Value is ComponentValue other ? other.Component.Name + "(...)" : call.Value!.KindName),
                    call.ValueSpan, hint: "write patch.set(\"" + call.TargetId + "\", \"" + call.Path.Text + "\", " +
                                          component.Name + "(...))");
            }
            return true;
        }

        private static List<PatchOp> AllowedOperations(TypeRef type)
        {
            var ops = new List<PatchOp> { PatchOp.Set };
            if (type is PrimitiveTypeRef p && p.IsNumeric) ops.Add(PatchOp.Add);
            if (type is ListTypeRef || type is DictTypeRef)
            {
                ops.Add(PatchOp.Append);
                ops.Add(PatchOp.Remove);
            }
            return ops;
        }

        private static string Join(List<PatchOp> ops)
        {
            var names = new List<string>();
            foreach (var o in ops) names.Add("patch." + o.ToString().ToLowerInvariant());
            return string.Join(", ", names);
        }

        private static string EntryOwnerPath(PatchCall call, PatchTarget target)
        {
            // 항목 경로에서 마지막 키 조각을 뺀 경로
            var segments = call.Path.Segments;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < segments.Count - 1; i++) sb.Append(segments[i].Canonical(i == 0));
            return sb.ToString();
        }

        private static string KeyLiteral(PathSegment key) =>
            key.IsNumber ? key.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "\"" + key.Text + "\"";

        // ---- 값의 모양 (G39·G48) ----

        private static bool CheckShape(ModManifest mod, PatchCall call, TypeRef type, Value value, StageDiagnostics stage)
        {
            string? problem = Mismatch(type, value);
            if (problem == null) return true;
            string hint = type is PrimitiveTypeRef p && ReferenceEquals(p, PrimitiveTypeRef.Int) && value is FixedValue
                ? "an int field takes whole numbers; a fixed value could lose its fraction"
                : "";
            stage.Error(mod.Id, Codes.PatBadValue, problem, call.ValueSpan, hint.Length == 0 ? null : hint);
            return false;
        }

        /// <summary>값이 타입 자리에 올 수 없으면 이유, 올 수 있으면 null. <c>int</c> → <c>fixed</c>는 허용한다 (G48).</summary>
        internal static string? Mismatch(TypeRef type, Value value)
        {
            switch (type)
            {
                case PrimitiveTypeRef p:
                    if (ReferenceEquals(p, PrimitiveTypeRef.Int)) return value is IntValue ? null : Expected(p, value);
                    if (ReferenceEquals(p, PrimitiveTypeRef.Fixed)) return value is IntValue || value is FixedValue ? null : Expected(p, value);
                    if (ReferenceEquals(p, PrimitiveTypeRef.Bool)) return value is BoolValue ? null : Expected(p, value);
                    return value is StrValue ? null : Expected(p, value);

                case DefIdTypeRef d:
                    return value is StrValue ? null : "expected a Def id string for " + d.Display + ", found " + value.KindName;

                case ListTypeRef list:
                    if (!(value is ListValue lv)) return Expected(list, value);
                    foreach (var item in lv.Items)
                    {
                        string? inner = Mismatch(list.Element, item);
                        if (inner != null) return "in a " + list.Display + ": " + inner;
                    }
                    return null;

                case DictTypeRef dict:
                    if (!(value is DictValue dv)) return Expected(dict, value);
                    foreach (var item in dv.Items)
                    {
                        string? key = Mismatch(dict.Key, item.Key);
                        if (key != null) return "a key of " + dict.Display + ": " + key;
                        string? inner = Mismatch(dict.Value, item.Value);
                        if (inner != null) return "a value of " + dict.Display + ": " + inner;
                    }
                    return null;

                case FunctionTypeRef f:
                    if (!(value is FunctionValue fn)) return "expected a function for " + f.Display + ", found " + value.KindName;
                    // 커맨드는 제출로만 실행된다 (G118)
                    if (fn.IsCommand)
                        return "'" + fn.Name + "' is a @command, so it cannot be a value of " + f.Display +
                               "; use a plain function";
                    // 확인용 자리에는 쓰기 함수를 둘 수 없다 (G117). 판정은 ②b 끝에 끝나 있다
                    if (f.Kind == FunctionSlotKind.Cond && fn.Writes)
                        return "'" + fn.Name + "' changes the world (at " + fn.WriteWitness + "), but " + f.Display +
                               " only holds functions that read";
                    return null;
            }
            return "values of type " + type.Display + " cannot be written in a patch";
        }

        private static string Expected(TypeRef type, Value value) => "expected " + type.Display + ", found " + value.KindName;
    }
}
