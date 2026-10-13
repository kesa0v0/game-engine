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
        private readonly ModManifest _manifest;
        private readonly Func<string, bool> _isActive;
        private readonly BuiltinFunction _modLoaded;
        private readonly PatchNamespace _patch;
        private readonly List<PatchCall> _patchSink;

        /// <param name="manifest">이 모드의 매니페스트 (<c>mod_loaded</c>가 선언된 모드만 받는다)</param>
        /// <param name="isActive">지금 활성인 모드인가 (설치되어 있고 로드에 실패하지 않았음, spec-gaps G8)</param>
        /// <param name="sink">이 모드가 만든 Def를 실행 순서대로 모을 곳</param>
        /// <param name="patchSink">이 모드의 <c>patch.*</c> 호출을 실행 순서대로 모을 곳</param>
        public DefBuiltins(SchemaSet schema, ModVisibility visibility, ModManifest manifest, Func<string, bool> isActive,
            List<DefRecord> sink, List<PatchCall> patchSink)
        {
            _schema = schema;
            _visibility = visibility;
            _mod = manifest.Id;
            _manifest = manifest;
            _isActive = isActive;
            _sink = sink;
            _patchSink = patchSink;
            _modLoaded = new BuiltinFunction(Interpreter.ModLoadedName, ModLoaded);
            _patch = new PatchNamespace(this);
        }

        /// <summary><c>patch</c>: <c>set</c>/<c>add</c>/<c>append</c>/<c>remove</c>를 속성으로 가진 이름 (spec-gaps G34).</summary>
        private sealed class PatchNamespace : Value, IAttributeSource
        {
            private static readonly string[] Names = { "set", "add", "append", "remove" };
            private readonly Dictionary<string, BuiltinFunction> _functions = new Dictionary<string, BuiltinFunction>();

            public PatchNamespace(DefBuiltins owner)
            {
                var ops = new[] { PatchOp.Set, PatchOp.Add, PatchOp.Append, PatchOp.Remove };
                for (int i = 0; i < ops.Length; i++)
                {
                    var op = ops[i];
                    string name = Names[i];
                    _functions.Add(name, new BuiltinFunction(Interpreter.PatchName + "." + name,
                        (ctx, span, args) => owner.RecordPatch(op, name, ctx, span, args)));
                }
            }

            public override string KindName => "patch";

            public Value GetAttribute(CallContext context, SourceSpan span, string name)
            {
                if (_functions.TryGetValue(name, out var f)) return f;
                throw context.Fail(Codes.EvlUnknownName, "patch has no function '" + name + "'", span,
                    hint: Suggest.Hint(name, Names) ?? "use patch.set, patch.add, patch.append or patch.remove");
            }
        }

        /// <summary>
        /// <c>patch.set/add/append(Def id, 경로, 값)</c>, <c>patch.remove(Def id, 경로[, 원소나 키])</c>를 기록만 한다 (G35).
        /// 여기서는 호출 모양만 본다: 인자 개수·종류, 경로 문법(G47), 값이 데이터인지.
        /// 대상 Def와 경로가 있는지, 필드 종류에 허용된 연산인지, 값의 모양은 ③에서 본다 (G39·G42).
        /// </summary>
        private Value RecordPatch(PatchOp op, string name, CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            string callee = Interpreter.PatchName + "." + name;
            var r = new ArgReader(ctx, callee, span, args);
            r.RejectRest(Array.Empty<string>());
            int min = op == PatchOp.Remove ? 2 : 3;
            if (r.Positional.Count < min || r.Positional.Count > 3)
            {
                string usage = op == PatchOp.Remove
                    ? callee + "(\"Def\", \"path\") or " + callee + "(\"Def\", \"path\", element_or_key)"
                    : callee + "(\"Def\", \"path\", value)";
                throw ctx.Fail(Codes.EvlArguments, callee + "() got " + r.Positional.Count + " positional arguments",
                    r.Positional.Count > 3 ? r.Positional[3].Span : span, hint: "write " + usage);
            }

            var idArg = r.Positional[0];
            string id = r.String(idArg, "the Def id");
            if (id.Length == 0) throw ctx.Fail(Codes.EvlArguments, "the Def id must not be empty", idArg.Span);

            var pathArg = r.Positional[1];
            string pathText = r.String(pathArg, "the path");
            if (!PatchPath.TryParse(pathText, out var path, out string error))
            {
                throw ctx.Fail(Codes.PatBadPath, "invalid patch path \"" + pathText + "\": " + error, ArgReader.SpanOf(pathArg),
                    hint: "write Component.field, or Field['key with dots'] / Field[3] for map keys");
            }

            Value? value = null;
            var valueSpan = span;
            if (r.Positional.Count == 3)
            {
                var valueArg = r.Positional[2];
                value = valueArg.Value;
                valueSpan = valueArg.Span;
                // 패치에는 부모라는 맥락이 없다: inherit 식은 거부한다 (G62)
                if (ContainsDeferred(value))
                {
                    throw ctx.Fail(Codes.EvlUnsupported, "'inherit' cannot be used in a patch value", valueSpan,
                        hint: "a patch has no parent; use patch.add to build on the current value");
                }
                // 컴포넌트 값(Morale(...))은 컴포넌트를 붙이는 값이다 (G44). 그 밖은 Def 값과 같은 데이터 검사
                if (!(value is ComponentValue) && !CheckData(ctx, value, valueSpan)) throw new ScriptError();
            }

            _patchSink.Add(new PatchCall(op, _mod, _patchSink.Count, id, ArgReader.SpanOf(idArg), path,
                ArgReader.SpanOf(pathArg), value, valueSpan, span, ctx.Guards.ToArray()));
            return NoneValue.Instance;
        }

        /// <summary>값 안(리스트·맵·컴포넌트 값의 인자까지)에 <c>inherit</c> 지연 값이 있는가.</summary>
        private static bool ContainsDeferred(Value value)
        {
            switch (value)
            {
                case DeferredValue _:
                    return true;
                case ListValue list:
                    foreach (var item in list.Items)
                        if (ContainsDeferred(item)) return true;
                    return false;
                case DictValue dict:
                    foreach (var item in dict.Items)
                        if (ContainsDeferred(item.Value)) return true;
                    return false;
                case ComponentValue cv:
                    foreach (var f in cv.Fields)
                        if (ContainsDeferred(f.Value)) return true;
                    return false;
            }
            return false;
        }

        /// <summary>
        /// <c>mod_loaded("m")</c> (spec-gaps G46): m이 지금 활성이면 True. m은 이 모드의 <c>dependencies</c>나
        /// <c>optional</c>에 선언된 모드여야 한다.
        /// </summary>
        private Value ModLoaded(CallContext ctx, SourceSpan span, IReadOnlyList<ArgValue> args)
        {
            var r = new ArgReader(ctx, Interpreter.ModLoadedName, span, args);
            r.ExpectPositional(1, "mod_loaded(\"mod_id\")");
            r.RejectRest(Array.Empty<string>());
            var arg = r.Positional[0];
            string id = r.String(arg, "the mod id");

            var declared = new List<string>();
            foreach (var d in _manifest.Dependencies) declared.Add(d.Id);
            foreach (var o in _manifest.Optional) declared.Add(o.Id);
            if (!declared.Contains(id))
            {
                throw ctx.Fail(Codes.EvlModNotDeclared,
                    "mod '" + id + "' is not listed in dependencies or optional of mod '" + _mod + "'",
                    ArgReader.SpanOf(arg),
                    hint: Suggest.Hint(id, declared) ?? "declare it in optional=[...] in mod.py");
            }
            return _isActive(id) ? BoolValue.True : BoolValue.False;
        }

        public Value? Lookup(string name)
        {
            if (name == Interpreter.ModLoadedName) return _modLoaded;
            if (name == Interpreter.PatchName) return _patch;
            if (_schema.TryGetType(name, out var t) && _visibility.CanSee(_mod, t.Mod)) return new DefConstructor(this, t);
            if (_schema.TryGetComponent(name, out var c) && _visibility.CanSee(_mod, c.Mod))
                return new ComponentConstructor(c);
            return null;
        }

        public IEnumerable<string> Names
        {
            get
            {
                yield return Interpreter.ModLoadedName;
                yield return Interpreter.PatchName;
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
