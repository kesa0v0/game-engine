using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// Def 파일(②b)에서 쓰는 내장 이름: 이 모드에 보이는 Def 타입의 생성자와 컴포넌트 값 생성자.
    /// 타입은 ②a에서 완성되어 고정이다. Def는 등록만 하고 값은 적힌 그대로 둔다.
    /// </summary>
    public sealed class DefBuiltins : IBuiltins
    {
        private readonly SchemaSet _schema;
        private readonly ModVisibility _visibility;
        private readonly string _mod;
        private readonly List<DefRecord> _sink;
        private readonly HashSet<string> _schemaNames = new HashSet<string>(SchemaBuiltins.AllNames, StringComparer.Ordinal);

        /// <param name="sink">이 모드가 만든 Def를 실행 순서대로 모을 곳</param>
        public DefBuiltins(SchemaSet schema, ModVisibility visibility, string mod, List<DefRecord> sink)
        {
            _schema = schema;
            _visibility = visibility;
            _mod = mod;
            _sink = sink;
        }

        public Value? Lookup(string name)
        {
            if (_schema.TryGetType(name, out var t) && _visibility.CanSee(_mod, t.Mod)) return new DefConstructor(this, t);
            if (_schema.TryGetComponent(name, out var c) && _visibility.CanSee(_mod, c.Mod))
                return new ComponentConstructor(c);
            return null;
        }

        public IEnumerable<string> Names
        {
            get
            {
                foreach (var t in _schema.Types)
                    if (_visibility.CanSee(_mod, t.Mod)) yield return t.Name;
                foreach (var c in _schema.Components)
                    if (_visibility.CanSee(_mod, c.Mod)) yield return c.Name;
            }
        }

        public (string Code, string Message)? Explain(string name)
        {
            if (_schemaNames.Contains(name))
                return (Codes.SchSchemaInDefFile, "'" + name + "' is only available in schema files (schema/...)");
            string? owner = null;
            if (_schema.TryGetType(name, out var t)) owner = t.Mod;
            else if (_schema.TryGetComponent(name, out var c)) owner = c.Mod;
            if (owner != null)
                return (Codes.SchNotVisible,
                    "'" + name + "' belongs to mod '" + owner + "', which mod '" + _mod +
                    "' does not depend on; add \"" + owner + "\" to dependencies in mod.py");
            return null;
        }

        /// <summary>
        /// Def 값은 데이터여야 한다: 수, 문자열, bool, None, 리스트, 맵, 함수, <c>inherit</c> 지연 값. 리스트·맵 안까지 본다.
        /// 값의 타입이 스키마와 맞는지는 검증 단계(⑥)가 본다.
        /// </summary>
        private static bool CheckData(CallContext ctx, Value value, SourceSpan span)
        {
            switch (value)
            {
                case ListValue list:
                    foreach (var item in list.Items)
                        if (!CheckData(ctx, item, span)) return false;
                    return true;
                case DictValue dict:
                    foreach (var item in dict.Items)
                        if (!CheckData(ctx, item.Value, item.Span)) return false;
                    return true;
                case DefHandleValue handle:
                    ctx.Error(Codes.DefBadValue, "a Def cannot be used as a value; refer to it by its id string", span,
                        hint: "write \"" + handle.Record.Id + "\"");
                    return false;
                case ComponentValue cv:
                    ctx.Error(Codes.DefBadValue,
                        "a " + cv.Component.Name + "(...) value can only be given as the '" + cv.Component.Name + "' component",
                        span);
                    return false;
                case TypeRef _:
                case MemberSpec _:
                case GenericType _:
                    // 스키마 파일에서 묶은 이름은 같은 모드의 Def 파일에서도 보인다 (G20). 그래도 Def 값이 될 수는 없다 (G26)
                    ctx.Error(Codes.SchSchemaInDefFile, "a " + value.KindName + " is a schema declaration, not a Def value",
                        span);
                    return false;
                case CallableValue callable:
                    ctx.Error(Codes.DefBadValue, "a " + callable.KindName + " is not a value", span,
                        hint: "call it as " + callable.Name + "(...), or refer to a Def by its id string");
                    return false;
            }
            return true;
        }

        private sealed class DefConstructor : CallableValue
        {
            private readonly DefBuiltins _owner;
            private readonly DefType _type;

            public DefConstructor(DefBuiltins owner, DefType type)
            {
                _owner = owner;
                _type = type;
            }

            public override string Name => _type.Name;
            public override string KindName => "Def type '" + _type.Name + "'";
            public override bool AcceptsDeferred => true;

            public override Value Call(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
            {
                var r = new ArgReader(ctx, _type.Name, span, args);
                r.ExpectPositional(1, _type.Name + "(\"Id\", ...)");
                var idArg = r.Positional[0];
                if (!(idArg.Value is StrValue idValue) || idValue.Value.Length == 0)
                    throw ctx.Fail(Codes.DefBadValue, "a Def id must be a non-empty string literal", idArg.Span);
                var idSpan = ArgReader.SpanOf(idArg);

                NameRef? parent = null;
                var parentArg = r.Take("parent");
                if (parentArg != null) parent = new NameRef(r.String(parentArg, "parent"), ArgReader.SpanOf(parentArg));
                var abstractArg = r.Take("abstract");
                bool isAbstract = abstractArg != null && r.Bool(abstractArg, "abstract");
                var overrideArg = r.Take("override");
                bool isOverride = overrideArg != null && r.Bool(overrideArg, "override");

                var fields = new List<DefField>();
                bool failed = false;
                foreach (var arg in r.Rest())
                {
                    if (!CheckField(ctx, arg)) failed = true;
                    else fields.Add(new DefField(arg.Name!, arg.Value, arg.Span));
                }
                if (failed) throw new ScriptError();

                var record = new DefRecord(_type, idValue.Value, ctx.Mod, idSpan, parent, isAbstract, isOverride, fields);
                _owner._sink.Add(record);
                return new DefHandleValue(record);
            }

            /// <summary>키워드 인자가 이 모드에 보이는 const 또는 컴포넌트인지, 값의 모양이 맞는지.</summary>
            private bool CheckField(CallContext ctx, ArgValue arg)
            {
                var vis = _owner._visibility;
                string name = arg.Name!;

                foreach (var k in _type.Consts)
                {
                    if (k.Name != name) continue;
                    if (!vis.CanSee(ctx.Mod, k.Mod)) return NotVisible(ctx, arg, k.Mod);
                    return CheckData(ctx, arg.Value, arg.Span);
                }

                foreach (var slot in _type.Components)
                {
                    if (slot.Component.Name != name) continue;
                    if (!vis.CanSee(ctx.Mod, slot.Mod)) return NotVisible(ctx, arg, slot.Mod);
                    return CheckComponentValue(ctx, arg, slot.Component);
                }

                var known = new List<string>();
                foreach (var k in _type.Consts)
                    if (vis.CanSee(ctx.Mod, k.Mod)) known.Add(k.Name);
                foreach (var slot in _type.Components)
                    if (vis.CanSee(ctx.Mod, slot.Mod)) known.Add(slot.Component.Name);
                known.AddRange(SchemaBuiltins.ReservedDefKeywords);
                string? hint = Suggest.Hint(name, known);
                if (hint == null && _owner._schema.TryGetComponent(name, out _))
                    hint = "to add a component to one Def, use a Def patch (spec S1)";
                ctx.Error(Codes.DefUnknownField, "'" + _type.Name + "' has no field or component '" + name + "'", arg.Span,
                    hint: hint);
                return false;
            }

            private static bool NotVisible(CallContext ctx, ArgValue arg, string owner)
            {
                ctx.Error(Codes.SchNotVisible,
                    "'" + arg.Name + "' was added by mod '" + owner + "', which mod '" + ctx.Mod + "' does not depend on",
                    arg.Span, hint: "add \"" + owner + "\" to dependencies in mod.py");
                return false;
            }

            private static bool CheckComponentValue(CallContext ctx, ArgValue arg, ComponentType component)
            {
                if (component.KeyKind == KeyKind.Entity)
                {
                    // 로드 시점에는 엔티티가 없다
                    ctx.Error(Codes.DefBadValue,
                        "'" + arg.Name + "' is keyed by " + component.Decl.Key!.Value.Name +
                        " entities; its rows are created at runtime and cannot be written in a Def", arg.Span);
                    return false;
                }
                switch (arg.Value)
                {
                    case DeferredValue _:
                        return true;
                    case ComponentValue cv when !component.IsKeyed:
                        if (cv.Component == component) return true;
                        break;
                    case DictValue dict when component.IsKeyed:
                        foreach (var item in dict.Items)
                        {
                            if (!(item.Key is StrValue))
                            {
                                ctx.Error(Codes.DefBadValue, "keys of '" + arg.Name + "' must be Def id strings", item.Span);
                                return false;
                            }
                            if (item.Value is DeferredValue) continue;
                            if (!(item.Value is ComponentValue row) || row.Component != component)
                            {
                                ctx.Error(Codes.DefBadValue,
                                    "each value of '" + arg.Name + "' must be " + component.Name + "(...), not " +
                                    item.Value.KindName, item.Span);
                                return false;
                            }
                        }
                        return true;
                }
                ctx.Error(Codes.DefBadValue,
                    component.IsKeyed
                        ? "'" + arg.Name + "' is a keyed component; write {\"Key\": " + component.Name + "(...)}"
                        : "'" + arg.Name + "' must be " + component.Name + "(...), not " + arg.Value.KindName,
                    arg.Span);
                return false;
            }
        }

        private sealed class ComponentConstructor : CallableValue
        {
            private readonly ComponentType _component;

            public ComponentConstructor(ComponentType component) => _component = component;

            public override string Name => _component.Name;
            public override string KindName => "component '" + _component.Name + "'";
            public override bool AcceptsDeferred => true;

            public override Value Call(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
            {
                var r = new ArgReader(ctx, _component.Name, span, args);
                r.ExpectPositional(0, _component.Name + "(value=...)");
                var fields = new List<DefField>();
                bool failed = false;
                foreach (var arg in r.Rest())
                {
                    if (!_component.TryGetMember(arg.Name!, out var member))
                    {
                        var names = new List<string>();
                        foreach (var m in _component.Members) names.Add(m.Name);
                        ctx.Error(Codes.DefUnknownField, "component '" + _component.Name + "' has no value '" + arg.Name + "'",
                            arg.Span, hint: Suggest.Hint(arg.Name!, names));
                        failed = true;
                    }
                    else if (member.Kind == MemberKind.Derived)
                    {
                        ctx.Error(Codes.DefBadValue, "'" + arg.Name + "' is a derived value; it is computed and cannot be set",
                            arg.Span, related: new[] { member.Span });
                        failed = true;
                    }
                    else if (!CheckData(ctx, arg.Value, arg.Span))
                    {
                        failed = true;
                    }
                    else
                    {
                        fields.Add(new DefField(arg.Name!, arg.Value, arg.Span));
                    }
                }
                if (failed) throw new ScriptError();
                return new ComponentValue(_component, fields, span);
            }
        }
    }
}
