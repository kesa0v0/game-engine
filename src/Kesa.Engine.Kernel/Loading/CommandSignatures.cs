using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
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
                    if (type == null)
                    {
                        ok = false;
                        continue;
                    }
                    Value? @default = null;
                    if (p.Default != null)
                    {
                        var literal = Defaults.Literal(p.Default, context);
                        @default = literal == null ? null : Defaults.Fit(p.Name, type, literal, p.Default.Span, context);
                        if (@default == null)
                        {
                            ok = false;
                            continue;
                        }
                    }
                    parameters.Add(new CommandParameter(p.Name, type, p.Span, @default));
                }
                if (ok) command.Parameters = parameters;
            }
        }

        /// <summary>기본값: 리터럴만 읽고 매개변수 타입에 맞춘다 (G116). <c>DefId</c>의 존재는 ⑤ 링크가 본다.</summary>
        private static class Defaults
        {
            private const string LiteralHint =
                "a default must be a number, a string, True/False, None, or a list/dict of those (spec-gaps G116)";

            public static Value? Literal(Expr expr, CallContext context)
            {
                switch (expr)
                {
                    case IntLiteral i: return new IntValue(i.Value);
                    case FixedLiteral f: return new FixedValue(f.Value);
                    case StringLiteral s: return new StrValue(s.Value, s.Span);
                    case BoolLiteral b: return b.Value ? BoolValue.True : BoolValue.False;
                    case NoneLiteral _: return NoneValue.Instance;
                    case UnaryExpr u when (u.Op == "-" || u.Op == "+") && (u.Operand is IntLiteral || u.Operand is FixedLiteral):
                        if (u.Op == "+") return Literal(u.Operand, context);
                        return u.Operand is IntLiteral n
                            ? new IntValue(-n.Value)
                            : (Value)new FixedValue(Fixed.FromRaw(-((FixedLiteral)u.Operand).Value.Raw));
                    case ListExpr list:
                    {
                        var items = new List<Value>(list.Items.Count);
                        foreach (var item in list.Items)
                        {
                            var v = Literal(item, context);
                            if (v == null) return null;
                            items.Add(v);
                        }
                        return new ListValue(items);
                    }
                    case DictExpr dict:
                    {
                        var entries = new List<DictItem>(dict.Entries.Count);
                        foreach (var entry in dict.Entries)
                        {
                            var key = Literal(entry.Key, context);
                            if (key == null) return null;
                            var value = Literal(entry.Value, context);
                            if (value == null) return null;
                            foreach (var other in entries)
                            {
                                if (SameKey(other.Key, key))
                                {
                                    context.Error(Codes.CmdParameterType, "the key " + Show(key) + " is written twice",
                                        entry.Key.Span, related: new[] { other.Span });
                                    return null;
                                }
                            }
                            entries.Add(new DictItem(key, value, entry.Key.Span));
                        }
                        return new DictValue(entries);
                    }
                }
                context.Error(Codes.CmdParameterType, "a default parameter value must be a literal", expr.Span, LiteralHint);
                return null;
            }

            private static bool SameKey(Value a, Value b) =>
                (a is StrValue sa && b is StrValue sb && sa.Value == sb.Value) ||
                (a is IntValue ia && b is IntValue ib && ia.Value == ib.Value);

            private static string Show(Value v) =>
                v is StrValue s ? "\"" + s.Value + "\""
                : v is IntValue i ? i.Value.ToString(CultureInfo.InvariantCulture)
                : v.KindName;

            /// <summary>리터럴 값을 타입에 맞춘다. 맞지 않으면 오류를 내고 null.</summary>
            public static Value? Fit(string parameter, TypeRef type, Value value, SourceSpan span, CallContext context,
                TypeRef? declared = null)
            {
                string shown = (declared ?? type).Display;

                Value? Fail(string message, string? hint = null)
                {
                    context.Error(Codes.CmdParameterType, "the default of '" + parameter + "' " + message, span, hint);
                    return null;
                }

                if (value is NoneValue && !(type is OptionalTypeRef))
                    return Fail("is None, but its type " + shown + " does not allow None", "write the type as " + shown + " | None");

                switch (type)
                {
                    case OptionalTypeRef optional:
                        return value is NoneValue ? value : Fit(parameter, optional.Inner, value, span, context, type);

                    case PrimitiveTypeRef primitive:
                        if (ReferenceEquals(primitive, PrimitiveTypeRef.Fixed))
                        {
                            if (value is FixedValue) return value;
                            if (value is IntValue i)
                            {
                                try
                                {
                                    return new FixedValue(Fixed.FromRaw(checked(i.Value * Fixed.Scale)));
                                }
                                catch (OverflowException)
                                {
                                    return Fail("is too large for a fixed value");
                                }
                            }
                        }
                        else if (ReferenceEquals(primitive, PrimitiveTypeRef.Int))
                        {
                            if (value is IntValue) return value;
                            if (value is FixedValue)
                                return Fail("must be int, not fixed", "a fixed value is not turned into an int (spec-gaps G95)");
                        }
                        else if (ReferenceEquals(primitive, PrimitiveTypeRef.Bool) && value is BoolValue) return value;
                        else if (ReferenceEquals(primitive, PrimitiveTypeRef.Str) && value is StrValue) return value;
                        break;

                    case EntityRefTypeRef _:
                        return Fail("cannot be written as a literal: an entity default can only be None",
                            "use " + shown + " | None = None");

                    case DefIdTypeRef _:
                        if (value is StrValue) return value;
                        break;

                    case ListTypeRef list:
                        if (value is ListValue items)
                        {
                            var fitted = new List<Value>(items.Items.Count);
                            foreach (var item in items.Items)
                            {
                                var v = Fit(parameter, list.Element, item, span, context);
                                if (v == null) return null;
                                fitted.Add(v);
                            }
                            return new ListValue(fitted);
                        }
                        break;

                    case DictTypeRef dict:
                        if (value is DictValue map)
                        {
                            var fitted = new List<DictItem>(map.Items.Count);
                            foreach (var item in map.Items)
                            {
                                var k = Fit(parameter, dict.Key, item.Key, item.Span, context);
                                if (k == null) return null;
                                var v = Fit(parameter, dict.Value, item.Value, item.Span, context);
                                if (v == null) return null;
                                fitted.Add(new DictItem(k, v, item.Span));
                            }
                            return new DictValue(fitted);
                        }
                        break;
                }
                return Fail("must be " + shown + ", not " + value.KindName);
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
