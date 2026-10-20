using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// <c>inherit</c>가 든 식을 ④에서 평가한다 (spec-gaps G30·G55·G56). 쓸 수 있는 것은 리터럴, 리스트·맵, 수의 <c>+ - *</c>과
    /// 단항 <c>-</c>, 리스트·문자열의 <c>+</c>, 같은 모드의 이름, 컴포넌트 값 생성자 호출, <c>inherit.이름</c>과
    /// <c>inherit.컴포넌트.값</c> 읽기다. 오류는 <see cref="Bag"/>에 모으고 식 하나를 중단한다.
    /// </summary>
    internal sealed class DeferredEvaluator
    {
        private readonly ModEnvironment _env;
        private readonly SchemaSet _schema;
        private readonly DefRecord _record;
        private readonly ResolvedDef? _parent;
        private readonly CallContext _context;

        public DeferredEvaluator(ModEnvironment env, SchemaSet schema, DefRecord record, ResolvedDef? parent)
        {
            _env = env;
            _schema = schema;
            _record = record;
            _parent = parent;
            Bag = new DiagnosticBag();
            _context = new CallContext(record.Mod, Bag);
        }

        /// <summary>식을 평가하는 동안 난 진단.</summary>
        public DiagnosticBag Bag { get; }

        /// <summary>
        /// <see cref="ResetReads"/> 뒤로 <c>inherit.이름</c>이 읽은 부모 값들을 만든 패치 (적용 순서, 덤프의 <c>patches</c>, G76).
        /// </summary>
        public IReadOnlyList<PatchLogEntry> ReadPatches { get; private set; } = Array.Empty<PatchLogEntry>();

        public void ResetReads() => ReadPatches = Array.Empty<PatchLogEntry>();

        private Value Read(ResolvedValue resolved)
        {
            ReadPatches = ResolvedValue.Join(ReadPatches, resolved.Patches);
            return resolved.Value;
        }

        /// <summary>식을 평가하고 결과가 Def 값으로 쓸 수 있는 데이터인지 본다 (내장 이름·타입 표기 등이 값에 남지 않게).</summary>
        public Value EvaluateData(Expr expr)
        {
            var value = Evaluate(expr);
            CheckData(value, expr.Span);
            return value;
        }

        private void CheckData(Value value, SourceSpan span)
        {
            switch (value)
            {
                case ListValue list:
                    foreach (var item in list.Items) CheckData(item, span);
                    return;
                case DictValue dict:
                    foreach (var item in dict.Items) CheckData(item.Value, item.Span);
                    return;
                case ComponentValue cv:
                    foreach (var f in cv.Fields) CheckData(f.Value, f.Span);
                    return;
                case DefHandleValue _:
                case CallableValue _:
                case TypeRef _:
                case MemberSpec _:
                case GenericType _:
                    throw _context.Fail(Codes.DefBadValue, "a " + value.KindName + " is not a value", span,
                        hint: "an inherit expression must produce numbers, strings, lists, maps or component values");
            }
        }

        public Value Evaluate(Expr expr)
        {
            switch (expr)
            {
                case IntLiteral i: return new IntValue(i.Value);
                case FixedLiteral f: return new FixedValue(f.Value);
                case StringLiteral s: return new StrValue(s.Value, s.Span);
                case BoolLiteral b: return b.Value ? BoolValue.True : BoolValue.False;
                case NoneLiteral _: return NoneValue.Instance;
                case ListExpr list: return EvaluateList(list);
                case DictExpr dict: return EvaluateDict(dict);
                case NameExpr name: return LookupName(name);
                case AttributeExpr attr: return ReadInherit(attr);
                case CallExpr call: return EvaluateCall(call);
                case UnaryExpr unary: return EvaluateUnary(unary);
                case BinaryExpr bin: return EvaluateBinary(bin);
                case UnsupportedExpr _: throw new ScriptError(); // 파서가 이미 보고함
            }
            throw _context.Fail(Codes.EvlUnsupported, "this expression is not supported in an inherit expression yet",
                expr.Span, hint: "use literals, + - *, and inherit.name");
        }

        private Value EvaluateList(ListExpr list)
        {
            var items = new List<Value>(list.Items.Count);
            foreach (var item in list.Items) items.Add(Evaluate(item));
            return new ListValue(items);
        }

        private Value EvaluateDict(DictExpr dict)
        {
            var items = new List<DictItem>(dict.Entries.Count);
            var seenStr = new Dictionary<string, SourceSpan>(StringComparer.Ordinal);
            var seenInt = new Dictionary<long, SourceSpan>();
            foreach (var entry in dict.Entries)
            {
                var key = Evaluate(entry.Key);
                SourceSpan first;
                bool duplicate;
                switch (key)
                {
                    case StrValue s:
                        duplicate = seenStr.TryGetValue(s.Value, out first);
                        if (!duplicate) seenStr.Add(s.Value, entry.Key.Span);
                        break;
                    case IntValue n:
                        duplicate = seenInt.TryGetValue(n.Value, out first);
                        if (!duplicate) seenInt.Add(n.Value, entry.Key.Span);
                        break;
                    default:
                        throw _context.Fail(Codes.EvlType, "dict keys must be str or int, not " + key.KindName, entry.Key.Span);
                }
                if (duplicate)
                    throw _context.Fail(Codes.EvlType, "duplicate dict key", entry.Key.Span, related: new[] { first });
                items.Add(new DictItem(key, Evaluate(entry.Value), entry.Key.Span));
            }
            return new DictValue(items);
        }

        private Value LookupName(NameExpr name)
        {
            if (name.Name == Interpreter.InheritName)
            {
                throw _context.Fail(Codes.EvlUnsupported, "'inherit' must be followed by a field name", name.Span,
                    hint: "write inherit.name or inherit.Component.value");
            }
            if (_env.Scope.TryLookup(name.Name, out var value)) return value;
            var builtin = _env.Builtins.Lookup(name.Name);
            if (builtin != null) return builtin;

            var candidates = new List<string>(_env.Scope.Names);
            candidates.AddRange(_env.Builtins.Names);
            throw _context.Fail(Codes.EvlUnknownName, "unknown name '" + name.Name + "'", name.Span,
                hint: Suggest.Hint(name.Name, candidates));
        }

        /// <summary>컴포넌트 값 생성자 호출만 허용한다. Def 생성자는 ④에서 Def를 새로 만들 수 없으므로 거부한다.</summary>
        private Value EvaluateCall(CallExpr call)
        {
            if (!(call.Callee is NameExpr calleeName) || !_schema.TryGetComponent(calleeName.Name, out _))
            {
                throw _context.Fail(Codes.EvlUnsupported,
                    "only component values (like Health(...)) can be called in an inherit expression", call.Span);
            }
            var callee = Evaluate(call.Callee);
            if (!(callee is CallableValue callable))
                throw _context.Fail(Codes.EvlType, "a " + callee.KindName + " value is not callable", call.Span);

            var args = new List<ArgValue>(call.Arguments.Count);
            foreach (var arg in call.Arguments)
                args.Add(new ArgValue(arg.Name, Evaluate(arg.Value), arg.Name == null ? arg.Value.Span : arg.Span));
            return callable.Call(_context, call.Span, args);
        }

        // ---- inherit.이름 ----

        private Value ReadInherit(AttributeExpr attr)
        {
            var names = new List<string>();
            Expr root = attr;
            while (root is AttributeExpr a)
            {
                names.Insert(0, a.Name);
                root = a.Target;
            }
            if (!(root is NameExpr rootName) || rootName.Name != Interpreter.InheritName)
            {
                throw _context.Fail(Codes.EvlUnsupported, "attribute access is only supported on 'inherit' in an inherit expression",
                    attr.Span);
            }
            if (_parent == null)
            {
                throw _context.Fail(Codes.InhNoParent, "'inherit' can only be used in a Def that has a parent", attr.Span,
                    hint: "add parent=\"...\" to Def '" + _record.Id + "'");
            }
            if (names.Count > 2)
            {
                throw _context.Fail(Codes.EvlUnsupported, "inherit." + string.Join(".", names) + " is too deep", attr.Span,
                    hint: "write inherit.name or inherit.Component.value");
            }
            return names.Count == 1 ? ReadConst(names[0], attr.Span) : ReadComponentValue(names[0], names[1], attr.Span);
        }

        private Value ReadConst(string name, SourceSpan span)
        {
            var type = _parent!.Type;
            foreach (var k in type.Consts)
            {
                if (k.Name != name) continue;
                if (_parent.Consts.TryGet(name, out var resolved)) return Read(resolved);
                if (k.Spec.Default != null) return k.Spec.Default;
                throw _context.Fail(Codes.InhNoValue, "parent '" + _parent.Id + "' has no value for '" + name + "'", span,
                    hint: "give '" + name + "' a value in the parent, or use a default= in its schema");
            }
            if (type.Components.Exists(s => s.Component.Name == name))
            {
                throw _context.Fail(Codes.EvlUnsupported, "'inherit." + name + "' is a whole component", span,
                    hint: "read one value: inherit." + name + ".value_name");
            }
            var known = new List<string>();
            foreach (var k in type.Consts) known.Add(k.Name);
            foreach (var s in type.Components) known.Add(s.Component.Name);
            throw _context.Fail(Codes.DefUnknownField, "'" + type.Name + "' has no field or component '" + name + "'", span,
                hint: Suggest.Hint(name, known));
        }

        private Value ReadComponentValue(string componentName, string memberName, SourceSpan span)
        {
            var type = _parent!.Type;
            ComponentSlot? slot = null;
            foreach (var s in type.Components)
                if (s.Component.Name == componentName) slot = s;
            if (slot == null)
            {
                var known = new List<string>();
                foreach (var s in type.Components) known.Add(s.Component.Name);
                throw _context.Fail(Codes.DefUnknownField, "'" + type.Name + "' has no component '" + componentName + "'", span,
                    hint: Suggest.Hint(componentName, known));
            }
            var component = slot.Component;
            if (component.IsKeyed)
            {
                throw _context.Fail(Codes.EvlUnsupported, "inherit cannot read the rows of keyed component '" + componentName + "' yet",
                    span);
            }
            if (!component.TryGetMember(memberName, out var member))
            {
                var names = new List<string>();
                foreach (var m in component.Members) names.Add(m.Name);
                throw _context.Fail(Codes.DefUnknownField, "component '" + componentName + "' has no value '" + memberName + "'",
                    span, hint: Suggest.Hint(memberName, names));
            }
            // 타입에 붙은 컴포넌트는 모든 Def가 가지므로, 부모가 적지 않았어도 기본값은 있다
            OrderedMap<ResolvedValue>? members = null;
            if (_parent.Components.TryGet(componentName, out var resolvedComponent))
            {
                members = resolvedComponent.Members;
                if (members.TryGet(memberName, out var resolved)) return Read(resolved);
            }
            var fallback = MemberDefaults.Of(component, member, members);
            if (fallback != null) return fallback;
            throw _context.Fail(Codes.InhNoValue,
                "parent '" + _parent.Id + "' has no value for '" + componentName + "." + memberName + "'", span);
        }

        // ---- 산술 ----

        private Value EvaluateUnary(UnaryExpr unary)
        {
            var operand = Evaluate(unary.Operand);
            if (unary.Op != "-" && unary.Op != "+")
            {
                throw _context.Fail(Codes.EvlUnsupported, "operator '" + unary.Op + "' is not supported in an inherit expression yet",
                    unary.Span, hint: "supported operators: + - *");
            }
            switch (operand)
            {
                case IntValue i:
                    if (unary.Op == "+") return i;
                    if (!FixedMath.TryNegate(i.Value, out long n)) throw Overflow(unary.Span, "integer");
                    return new IntValue(n);
                case FixedValue f:
                    if (unary.Op == "+") return f;
                    if (!FixedMath.TryNegate(f.Value.Raw, out long raw)) throw Overflow(unary.Span, "fixed");
                    return new FixedValue(Fixed.FromRaw(raw));
            }
            throw _context.Fail(Codes.EvlType, "unary '" + unary.Op + "' needs a number, not " + operand.KindName, unary.Span);
        }

        private Value EvaluateBinary(BinaryExpr bin)
        {
            if (bin.Op != "+" && bin.Op != "-" && bin.Op != "*")
            {
                throw _context.Fail(Codes.EvlUnsupported, "operator '" + bin.Op + "' is not supported in an inherit expression yet",
                    bin.Span, hint: "supported operators: + - *");
            }
            var left = Evaluate(bin.Left);
            var right = Evaluate(bin.Right);

            if (bin.Op == "+")
            {
                if (left is ListValue ll && right is ListValue rl)
                {
                    var items = new List<Value>(ll.Items);
                    items.AddRange(rl.Items);
                    return new ListValue(items);
                }
                if (left is StrValue ls && right is StrValue rs) return new StrValue(ls.Value + rs.Value);
            }

            if ((left is IntValue || left is FixedValue) && (right is IntValue || right is FixedValue))
                return Arithmetic(bin, left, right);

            throw _context.Fail(Codes.EvlType,
                "cannot apply '" + bin.Op + "' to " + left.KindName + " and " + right.KindName, bin.Span);
        }

        private Value Arithmetic(BinaryExpr bin, Value left, Value right)
        {
            if (left is IntValue a && right is IntValue b)
            {
                bool ok = bin.Op == "+" ? FixedMath.TryAdd(a.Value, b.Value, out long r)
                    : bin.Op == "-" ? FixedMath.TrySubtract(a.Value, b.Value, out r)
                    : FixedMath.TryMultiply(a.Value, b.Value, out r);
                if (!ok) throw Overflow(bin.Span, "integer");
                return new IntValue(r);
            }

            // int * fixed는 정확하다 (반올림 없음). 그 밖의 섞임은 int를 fixed로 올린다 (G48)
            if (bin.Op == "*" && (left is IntValue || right is IntValue))
            {
                long factor = left is IntValue li ? li.Value : ((IntValue)right).Value;
                long raw = left is FixedValue lf ? lf.Value.Raw : ((FixedValue)right).Value.Raw;
                if (!FixedMath.TryMultiply(factor, raw, out long product)) throw Overflow(bin.Span, "fixed");
                return new FixedValue(Fixed.FromRaw(product));
            }

            var x = ToFixed(left, bin.Span);
            var y = ToFixed(right, bin.Span);
            if (bin.Op == "*")
            {
                if (!FixedMath.TryMultiply(x, y, out var p)) throw Overflow(bin.Span, "fixed");
                return new FixedValue(p);
            }
            bool added = bin.Op == "+" ? FixedMath.TryAdd(x.Raw, y.Raw, out long sum) : FixedMath.TrySubtract(x.Raw, y.Raw, out sum);
            if (!added) throw Overflow(bin.Span, "fixed");
            return new FixedValue(Fixed.FromRaw(sum));
        }

        private Fixed ToFixed(Value value, SourceSpan span)
        {
            if (value is FixedValue f) return f.Value;
            if (!FixedMath.TryFromInt(((IntValue)value).Value, out var converted)) throw Overflow(span, "fixed");
            return converted;
        }

        private ScriptError Overflow(SourceSpan span, string kind) =>
            _context.Fail(Codes.EvlType, kind + " value out of range", span);
    }
}
