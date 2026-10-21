using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
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

        private const string Usage = "contrib(\"Component.derived\", \"op\", value, key=\"Def\")";

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
            r.RejectRest(new[] { "key" });
            return new ContribValue(component, member, op, amount, key, ArgReader.SpanOf(targetArg));
        }
    }
}
