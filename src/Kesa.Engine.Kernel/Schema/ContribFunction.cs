using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Schema
{
    /// <summary>
    /// 스키마 파일(기본값)과 Def 파일에서 쓰는 <c>contrib("Health.max", "add", 3, key="Medicine")</c> (spec-gaps G295~G297).
    /// 여기서는 모양만 본다: 대상은 <c>컴포넌트.이름</c>, 값은 수, 키는 Def id 글자. 대상과 키가 있는지는 ⑤ 링크가 본다.
    /// </summary>
    public static class ContribFunction
    {
        public const string Name = "contrib";

        private const string Usage = "contrib(\"Component.derived\", \"op\", value, key=\"Def\", to=\"Component.ref\")";

        public static readonly BuiltinFunction Instance = new BuiltinFunction(Name, Call);

        private static Value Call(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, Name, span, args);
            r.ExpectPositional(3, Usage);
            var targetArg = r.Positional[0];
            string target = r.String(targetArg, "the target");
            int dot = target.IndexOf('.');
            string component = dot < 0 ? "" : target.Substring(0, dot);
            string member = dot < 0 ? "" : target.Substring(dot + 1);
            if (!SchemaBuiltins.IsIdentifier(component) || !SchemaBuiltins.IsIdentifier(member))
                throw ctx.Fail(Codes.EvlArguments, "the target of contrib() is \"Component.derived\", not \"" + target + "\"",
                    ArgReader.SpanOf(targetArg), hint: "write " + Usage);

            string op = r.String(r.Positional[1], "the op");

            var valueArg = r.Positional[2];
            Fixed amount;
            switch (valueArg.Value)
            {
                case FixedValue f:
                    amount = f.Value;
                    break;
                case IntValue i when FixedMath.TryFromInt(i.Value, out amount):
                    break;
                case IntValue i:
                    throw ctx.Fail(Codes.EvlType, i.Value + " is too large for a fixed value", valueArg.Span);
                default:
                    throw ctx.Fail(Codes.EvlType, "the value of contrib() must be int or fixed, not " + valueArg.Value.KindName,
                        valueArg.Span);
            }

            string? key = null;
            var keyArg = r.Take("key");
            if (keyArg != null)
            {
                string id = r.String(keyArg, "key");
                string? problem = DefIds.Problem(id);
                if (problem != null)
                    throw ctx.Fail(Codes.DefBadId, "invalid Def id \"" + id + "\" in contrib(key=...): " + problem, ArgReader.SpanOf(keyArg));
                key = DefIds.Resolve(id, ctx.Mod); // 쓴 모드 기준 전체 이름 (G260·G297)
            }
            // to=: 선언을 든 엔티티에서 한 걸음 (G300). "Held.holder"(Ref) 또는 "units"(역방향 컬렉션)
            string? to = null;
            var toArg = r.Take("to");
            if (toArg != null)
            {
                to = r.String(toArg, "to");
                int toDot = to.IndexOf('.');
                bool ok = toDot < 0
                    ? SchemaBuiltins.IsIdentifier(to)
                    : SchemaBuiltins.IsIdentifier(to.Substring(0, toDot)) && SchemaBuiltins.IsIdentifier(to.Substring(toDot + 1));
                if (!ok)
                    throw ctx.Fail(Codes.EvlArguments,
                        "to= is \"Component.ref\" or the name of an inverse collection, not \"" + to + "\"", ArgReader.SpanOf(toArg),
                        hint: "one step from the entity that declares it, e.g. to=\"Held.holder\" or to=\"units\" (spec-gaps G300)");
            }
            // when=: 조건 (G301). Cond 칸과 같은 함수(순수 스크립트 함수나 읽기 호스트 함수)이고 (src, dst) 둘로 부른다.
            // 쓰기·난수·UI는 효과 판정 뒤에 값 검사(PatchOperationCheck.Mismatch)가 본다
            Value? when = null;
            var whenArg = r.Take("when");
            if (whenArg != null)
            {
                switch (whenArg.Value)
                {
                    case FunctionValue fn:
                        if (!TakesTwo(fn))
                            throw ctx.Fail(Codes.EvlArguments,
                                "the when= function of contrib() is called with two arguments (src, dst), but '" + fn.Name + "' takes " +
                                fn.Parameters.Count, whenArg.Span,
                                hint: "write when=lambda src, dst: ... (src declares the contribution, dst receives it; spec-gaps G301)");
                        break;
                    case HostReference _:
                        break;
                    case NoneValue _:
                        break;
                    default:
                        throw ctx.Fail(Codes.EvlType, "when= of contrib() takes a function, not " + whenArg.Value.KindName,
                            whenArg.Span, hint: "write when=lambda src, dst: ... (spec-gaps G301)");
                }
                if (!(whenArg.Value is NoneValue)) when = whenArg.Value;
            }
            r.RejectRest(new[] { "key", "to", "when" });
            return new ContribValue(component, member, op, amount, key, ArgReader.SpanOf(targetArg))
            {
                To = to,
                ToSpan = toArg == null ? SourceSpan.None : ArgReader.SpanOf(toArg),
                When = when,
            };
        }

        /// <summary>위치 인자 둘로 부를 수 있는가: 매개변수가 둘 이상이고 셋째부터는 기본값이 있다.</summary>
        private static bool TakesTwo(FunctionValue fn)
        {
            var parameters = fn.Parameters;
            if (parameters.Count < 2) return false;
            for (int i = 2; i < parameters.Count; i++)
                if (parameters[i].Default == null) return false;
            return true;
        }
    }
}
