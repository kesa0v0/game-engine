using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 커맨드 매개변수의 어노테이션을 타입으로 푼다 (spec S6, spec-gaps G114). 스키마가 완성되고 모드의 이름이 다 정해진
    /// ②b 끝에서 한다. 어노테이션은 실행하지 않고 모양으로 읽는다: <c>int</c>·<c>fixed</c>·<c>bool</c>·<c>str</c>,
    /// <c>Ref[X]</c>·<c>DefId[X]</c>(X는 이름이나 문자열), <c>list[T]</c>, <c>dict[K, V]</c>, <c>T | None</c>.
    /// 엔진이 주는 이름이라 모드가 그 이름을 덮어썼으면 그 자리에 쓸 수 없다 (G112).
    /// </summary>
    public static class CommandSignatures
    {
        public static void Resolve(IReadOnlyList<CommandDecl> commands, SchemaSet schema, ModVisibility visibility,
            ModScope scope, CallContext context)
        {
            foreach (var command in commands)
            {
                var parameters = new List<CommandParameter>();
                bool ok = true;
                foreach (var p in command.Function.Definition!.Parameters)
                {
                    if (p.Annotation == null)
                    {
                        context.Error(Codes.CmdParameterType,
                            "parameter '" + p.Name + "' of command '" + command.Id + "' needs a type annotation", p.Span,
                            hint: "write " + p.Name + ": Ref[Unit], " + p.Name + ": int, ... (spec-gaps G114)");
                        ok = false;
                        continue;
                    }
                    var type = new Reader(schema, visibility, scope, context, command.Mod).Type(p.Annotation);
                    if (type == null) ok = false;
                    else parameters.Add(new CommandParameter(p.Name, type, p.Span));
                }
                if (ok) command.Parameters = parameters;
            }
        }

        private sealed class Reader
        {
            private readonly SchemaSet _schema;
            private readonly ModVisibility _visibility;
            private readonly ModScope _scope;
            private readonly CallContext _context;
            private readonly string _mod;

            public Reader(SchemaSet schema, ModVisibility visibility, ModScope scope, CallContext context, string mod)
            {
                _schema = schema;
                _visibility = visibility;
                _scope = scope;
                _context = context;
                _mod = mod;
            }

            private TypeRef? Fail(string message, SourceSpan span, string? hint = null)
            {
                _context.Error(Codes.CmdParameterType, message, span, hint);
                return null;
            }

            /// <summary>엔진의 타입 이름인가. 모드가 그 이름을 묶었으면 아니다 (G112).</summary>
            private bool Shadowed(NameExpr name, out TypeRef? failure)
            {
                failure = null;
                if (!_scope.TryGet(name.Name, out _, out _)) return false;
                failure = Fail("'" + name.Name + "' is this mod's own name, so it cannot be used as a type here", name.Span,
                    "rename it (spec-gaps G112)");
                return true;
            }

            public TypeRef? Type(Expr expr)
            {
                switch (expr)
                {
                    case NameExpr name:
                        if (Shadowed(name, out var shadowed)) return shadowed;
                        switch (name.Name)
                        {
                            case "int": return PrimitiveTypeRef.Int;
                            case "fixed": return PrimitiveTypeRef.Fixed;
                            case "bool": return PrimitiveTypeRef.Bool;
                            case "str": return PrimitiveTypeRef.Str;
                        }
                        if (_schema.TryGetType(name.Name, out _))
                            return Fail("a Def type cannot be a parameter type by itself", name.Span,
                                "write Ref[" + name.Name + "] for an entity or DefId[" + name.Name + "] for a Def id");
                        return Fail("'" + name.Name + "' is not a parameter type", name.Span,
                            "use int, fixed, bool, str, Ref[X], DefId[X], list[T], dict[K, V] or T | None (spec-gaps G114)");

                    case IndexExpr index when index.Target is NameExpr generic:
                        if (Shadowed(generic, out var shadowedGeneric)) return shadowedGeneric;
                        return Generic(generic, index);

                    case BinaryExpr union when union.Op == "|":
                        var inner = union.Right is NoneLiteral ? union.Left : union.Left is NoneLiteral ? union.Right : null;
                        if (inner == null)
                            return Fail("only 'T | None' unions are supported in parameter types", union.Span);
                        var t = Type(inner);
                        if (t == null) return null;
                        if (t is OptionalTypeRef) return Fail("'| None' is written twice", union.Span);
                        return new OptionalTypeRef(t);
                }
                return Fail("this is not a parameter type", expr.Span,
                    "use int, fixed, bool, str, Ref[X], DefId[X], list[T], dict[K, V] or T | None (spec-gaps G114)");
            }

            private TypeRef? Generic(NameExpr generic, IndexExpr index)
            {
                switch (generic.Name)
                {
                    case "Ref":
                    case "DefId":
                        if (index.Indices.Count != 1)
                            return Fail(generic.Name + "[...] takes one type name", index.Span);
                        var target = TypeName(generic.Name, index.Indices[0]);
                        if (target == null) return null;
                        if (generic.Name == "DefId") return new DefIdTypeRef(target.Value);
                        _schema.TryGetType(target.Value.Name, out var defType);
                        // 스키마의 Ref 칼럼과 같은 규칙: 이 모드에 보이는 컴포넌트가 있어야 엔티티가 된다 (schema-design 4.3)
                        bool visible = false;
                        foreach (var slot in defType.Components)
                            if (_visibility.CanSee(_mod, slot.Mod)) visible = true;
                        if (!visible)
                            return Fail(defType.IsDataOnly
                                    ? "'" + defType.Name + "' has no components, so it has no entities to pass"
                                    : "'" + defType.Name + "' has components only from mods this mod does not depend on, " +
                                      "so it has no entities to pass",
                                index.Span, "use DefId[" + defType.Name + "] to pass one of its Defs");
                        return new EntityRefTypeRef(target.Value);

                    case "list":
                        if (index.Indices.Count != 1) return Fail("list[...] takes one type", index.Span, "write list[int]");
                        var element = Type(index.Indices[0]);
                        return element == null ? null : new ListTypeRef(element);

                    case "dict":
                        if (index.Indices.Count != 2)
                            return Fail("dict[...] takes a key type and a value type", index.Span, "write dict[DefId[Resource], int]");
                        var key = Type(index.Indices[0]);
                        var value = Type(index.Indices[1]);
                        if (key == null || value == null) return null;
                        if (!ReferenceEquals(key, PrimitiveTypeRef.Str) && !ReferenceEquals(key, PrimitiveTypeRef.Int) &&
                            !(key is DefIdTypeRef) && !(key is EntityRefTypeRef))
                            return Fail("map keys must be str, int, DefId[...] or Ref[...], not " + key.Display, index.Indices[0].Span);
                        return new DictTypeRef(key, value);
                }
                return Fail("'" + generic.Name + "[...]' is not a parameter type", index.Span,
                    "use Ref[X], DefId[X], list[T] or dict[K, V] (spec-gaps G114)");
            }

            /// <summary><c>Ref[Unit]</c>나 <c>Ref["Unit"]</c>의 타입 이름. 이 모드에 보이는 Def 타입이어야 한다.</summary>
            private NameRef? TypeName(string generic, Expr expr)
            {
                string name;
                switch (expr)
                {
                    case NameExpr n:
                        if (_scope.TryGet(n.Name, out _, out _))
                        {
                            Fail("'" + n.Name + "' is this mod's own name, so it cannot name a Def type here", n.Span,
                                "rename it, or write " + generic + "[\"" + n.Name + "\"] (spec-gaps G112)");
                            return null;
                        }
                        name = n.Name;
                        break;
                    case StringLiteral s:
                        name = s.Value;
                        break;
                    default:
                        Fail(generic + "[...] takes a Def type name", expr.Span, "write " + generic + "[Unit]");
                        return null;
                }

                if (!_schema.TryGetType(name, out var type))
                {
                    var names = new List<string>();
                    foreach (var t in _schema.Types)
                        if (_visibility.CanSee(_mod, t.Mod)) names.Add(t.Name);
                    Fail("there is no Def type '" + name + "'", expr.Span, Suggest.Hint(name, names));
                    return null;
                }
                if (!_visibility.CanSee(_mod, type.Mod))
                {
                    Fail("Def type '" + name + "' belongs to mod '" + type.Mod + "', which mod '" + _mod +
                         "' does not depend on", expr.Span, "add \"" + type.Mod + "\" to dependencies in mod.py");
                    return null;
                }
                return new NameRef(name, expr.Span);
            }
        }
    }
}
