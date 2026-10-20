using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Schema
{
    /// <summary><c>list</c>, <c>dict</c>, <c>Ref</c>, <c>Cond</c>, <c>Action</c>: <c>[...]</c>로 타입을 만든다.</summary>
    public sealed class GenericType : Value, ISubscriptable
    {
        public static readonly GenericType List = new GenericType("list");
        public static readonly GenericType Dict = new GenericType("dict");
        public static readonly GenericType Ref = new GenericType("Ref");
        public static readonly GenericType DefId = new GenericType("DefId");
        public static readonly GenericType Cond = new GenericType("Cond");
        public static readonly GenericType Action = new GenericType("Action");

        private GenericType(string name) => Name = name;

        public string Name { get; }
        public override string KindName => "generic type '" + Name + "' (write " + Name + "[...])";

        public Value Subscript(CallContext ctx, SourceSpan span, IReadOnlyList<Value> indices)
        {
            switch (Name)
            {
                case "list":
                    Expect(ctx, span, indices, 1, "list[str]");
                    return new ListTypeRef(AsType(ctx, span, indices[0]));
                case "dict":
                    Expect(ctx, span, indices, 2, "dict[str, int]");
                    var key = AsType(ctx, span, indices[0]);
                    // Def 리터럴의 맵 키는 str·int뿐이다 (spec-gaps G22). DefId는 리터럴에서 id 문자열로 적는다.
                    // 엔티티 키는 ui_state의 칸에서만 된다(G277): 다른 자리는 선언 함수가 막는다
                    if (!ReferenceEquals(key, PrimitiveTypeRef.Str) && !ReferenceEquals(key, PrimitiveTypeRef.Int) &&
                        !(key is DefIdTypeRef) && !(key is EntityRefTypeRef))
                    {
                        throw ctx.Fail(Codes.SchBadDeclaration, "map keys must be str, int or DefId[...], not " + key.Display,
                            span);
                    }
                    return new DictTypeRef(key, AsType(ctx, span, indices[1]));
                case "Ref":
                    Expect(ctx, span, indices, 1, "Ref[\"Type\"]");
                    return new EntityRefTypeRef(AsName(ctx, span, indices[0]));
                case "DefId":
                    Expect(ctx, span, indices, 1, "DefId[\"Type\"]");
                    return new DefIdTypeRef(AsName(ctx, span, indices[0]));
                case "Cond":
                    Expect(ctx, span, indices, 1, "Cond[\"Type\"]");
                    return new FunctionTypeRef(FunctionSlotKind.Cond, AsName(ctx, span, indices[0]));
                default:
                    Expect(ctx, span, indices, 1, "Action[\"Type\"]");
                    return new FunctionTypeRef(FunctionSlotKind.Action, AsName(ctx, span, indices[0]));
            }
        }

        private void Expect(CallContext ctx, SourceSpan span, IReadOnlyList<Value> indices, int count, string usage)
        {
            if (indices.Count != count)
                throw ctx.Fail(Codes.EvlType, Name + "[...] takes " + count + " type argument" + (count == 1 ? "" : "s"),
                    span, hint: "write " + usage);
        }

        private TypeRef AsType(CallContext ctx, SourceSpan span, Value v)
        {
            if (v is TypeRef t) return t;
            throw ctx.Fail(Codes.EvlType, "expected a type inside " + Name + "[...], not " + v.KindName, span,
                hint: v is StrValue s
                    ? "write DefId[\"" + s.Value + "\"] for a Def or Ref[\"" + s.Value + "\"] for an entity"
                    : null);
        }

        private NameRef AsName(CallContext ctx, SourceSpan span, Value v)
        {
            if (v is StrValue s) return new NameRef(s.Value, s.Origin.File.Length == 0 ? span : s.Origin);
            throw ctx.Fail(Codes.EvlType, Name + "[...] takes a type name as a string", span,
                hint: "write " + Name + "[\"Type\"] (schema-design 3.2)");
        }
    }
}
