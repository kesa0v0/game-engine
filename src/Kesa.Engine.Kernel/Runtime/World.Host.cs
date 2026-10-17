using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 호스트 함수 호출 (spec S7, spec-gaps G215~G222). 소유 모드 id마다 이름 공간 값을 런타임 내장에 넣는다. 어느 모드가 그 이름을
    /// 쓸 수 있는지는 로드가 막았다 (G218). 인자는 커맨드 인자와 같은 검사(G115)를 거쳐 C# 값으로 바뀌고(G222), 돌려준 값은 서명대로
    /// 검사한다. 구현이 던진 예외는 <see cref="Codes.RunHostFailed"/>, 서명과 다른 반환은 <see cref="Codes.RunHostReturn"/>이다 (G219).
    /// </summary>
    public sealed partial class World
    {
        private IReadOnlyDictionary<string, HostSignature> _hostFunctions =
            new SortedDictionary<string, HostSignature>(StringComparer.Ordinal);

        private void AddHostBuiltins(SortedDictionary<string, Value> table)
        {
            var roots = new SortedDictionary<string, HostNamespace>(StringComparer.Ordinal);
            foreach (var signature in _hostFunctions.Values)
            {
                var function = signature.Function;
                if (!roots.TryGetValue(function.Mod, out var space))
                {
                    space = new HostNamespace(function.Mod);
                    roots.Add(function.Mod, space);
                }
                var parts = function.Path.Split('.');
                for (int i = 0; i < parts.Length - 1; i++) space = space.Group(parts[i]);
                space.Members.Add(parts[parts.Length - 1], new HostFunctionValue(this, signature));
            }
            // 내장 이름과 같은 소유 모드는 로드가 막았다 (HST001)
            foreach (var pair in roots)
                if (!table.ContainsKey(pair.Key)) table.Add(pair.Key, pair.Value);
        }

        /// <summary><c>core</c>, <c>core.path</c> 같은 이름 공간. 값으로 쓰는 것은 로드가 막는다 (G218).</summary>
        private sealed class HostNamespace : Value, IRuntimeAttributes
        {
            private readonly string _name;

            public HostNamespace(string name) => _name = name;

            public SortedDictionary<string, Value> Members { get; } = new SortedDictionary<string, Value>(StringComparer.Ordinal);

            public HostNamespace Group(string name)
            {
                if (Members.TryGetValue(name, out var existing)) return (HostNamespace)existing;
                var group = new HostNamespace(_name + "." + name);
                Members.Add(name, group);
                return group;
            }

            public override string KindName => "host function group";

            public Value GetAttribute(string name)
            {
                if (Members.TryGetValue(name, out var member)) return member;
                throw new ScriptRuntimeException(Codes.RunScript, _name + " has no host function '" + name + "'",
                    Suggest.Hint(name, Members.Keys));
            }
        }

        private sealed class HostFunctionValue : HostCallable
        {
            private readonly World _world;
            private readonly HostSignature _signature;

            public HostFunctionValue(World world, HostSignature signature)
            {
                _world = world;
                _signature = signature;
            }

            public override string Name => _signature.Function.Name;
            public override bool Writes => _signature.Function.Effect == HostEffect.Writes;

            public override Value Call(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords) =>
                _world.CallHost(_signature, positional, keywords);
        }

        private Value CallHost(HostSignature signature, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            var function = signature.Function;
            var parameters = signature.Parameters;
            string name = function.Name;

            // 묶기: 스크립트 함수와 같은 규칙
            if (positional.Count > parameters.Count)
            {
                throw new ScriptRuntimeException(Codes.RunScript, name + "() takes " + parameters.Count + " argument" +
                                                                  (parameters.Count == 1 ? "" : "s") + ", got " + positional.Count);
            }
            var given = new Value?[parameters.Count];
            for (int i = 0; i < positional.Count; i++) given[i] = positional[i];
            foreach (var pair in keywords)
            {
                int index = parameters.Count - 1;
                while (index >= 0 && parameters[index].Name != pair.Key) index--;
                if (index < 0)
                {
                    var names = new List<string>();
                    foreach (var p in parameters) names.Add(p.Name);
                    throw new ScriptRuntimeException(Codes.RunScript, name + "() has no parameter '" + pair.Key + "'",
                        Suggest.Hint(pair.Key, names));
                }
                if (given[index] != null)
                    throw new ScriptRuntimeException(Codes.RunScript, name + "() got several values for '" + pair.Key + "'");
                given[index] = pair.Value;
            }

            // 검사: 커맨드 인자와 같다 (G115)
            var rejections = new List<ArgumentRejection>();
            var fitted = new Value[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                var value = given[i];
                if (value == null)
                {
                    if (p.Default == null)
                        throw new ScriptRuntimeException(Codes.RunScript, name + "() is missing the argument '" + p.Name + "'");
                    fitted[i] = p.Default;
                    continue;
                }
                fitted[i] = Fit(p.Name, p.Name, p.Type, ToPlain(name + "()", value), rejections)!;
                if (rejections.Count > 0)
                {
                    var first = rejections[0];
                    throw new ScriptRuntimeException(
                        first.Problem == ArgumentProblem.DeadEntity ? Codes.RunDeadRef : Codes.RunType,
                        name + "(): " + first.Message);
                }
            }

            // C# 값으로 (G222)
            var implementation = function.Implementation;
            var types = implementation.GetType().GetMethod("Invoke")!.GetParameters();
            int skip = function.Effect == HostEffect.Writes ? 1 : 0;
            var args = new object?[types.Length];
            if (skip == 1) args[0] = new HostAccess();
            for (int i = 0; i < fitted.Length; i++) args[i + skip] = ToClr(fitted[i], types[i + skip].ParameterType);

            object? result;
            try
            {
                result = implementation.DynamicInvoke(args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                var inner = e.InnerException;
                throw new ScriptRuntimeException(Codes.RunHostFailed,
                    "host function " + name + " failed: " + inner.GetType().Name + ": " + inner.Message,
                    "this is a bug in the game's C# code, not in the mod (spec-gaps G219)", inner);
            }

            if (signature.Returns == null) return NoneValue.Instance;
            var returned = FromClr(result);
            var problems = new List<ArgumentRejection>();
            var checkedValue = returned == null ? null : Fit("return", "return", signature.Returns, returned, problems);
            if (checkedValue == null)
            {
                // 맨 위 값의 종류가 틀렸으면 그 종류를, 안쪽(리스트 원소·죽은 엔티티 등)이면 검사가 짚은 곳을 적는다
                bool topKind = returned == null || problems.Count == 0 ||
                               problems[0].Problem == ArgumentProblem.WrongType && problems[0].Path == "return";
                throw new ScriptRuntimeException(Codes.RunHostReturn,
                    "host function " + name + " returned " +
                    (topKind
                        ? (returned == null ? DescribeClr(result) : returned.KindName) + ", but its signature says " +
                          signature.Returns.Display
                        : "a value that does not fit its signature " + signature.Returns.Display + ": " + problems[0].Message),
                    "this is a bug in the game's C# code, not in the mod (spec-gaps G219)");
            }
            return ToScriptDeep(checkedValue);
        }

        /// <summary>검사를 마친 엔진 값을 C# 람다의 매개변수 타입으로 (G222). <see cref="Value"/>면 그대로.</summary>
        private static object? ToClr(Value value, Type type)
        {
            if (type == typeof(Value)) return value;
            if (value is NoneValue) return null;
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;
            switch (value)
            {
                case IntValue i: return i.Value;
                case FixedValue f: return f.Value;
                case BoolValue b: return b.Value;
                case StrValue s: return s.Value;
                case EntityValue e: return e.Id;
                case ListValue list:
                    var element = type.GetGenericArguments()[0];
                    var items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
                    foreach (var item in list.Items) items.Add(ToClr(item, element));
                    return items;
            }
            throw new InvalidOperationException("cannot pass " + value.KindName + " as " + type.Name);
        }

        /// <summary>C# 반환값을 엔진 값으로. 바꿀 수 없는 것이면 null (반환 오류가 된다).</summary>
        private static Value? FromClr(object? value)
        {
            switch (value)
            {
                case null: return NoneValue.Instance;
                case Value v: return ToPlain("return", v);
                case long l: return new IntValue(l);
                case Fixed f: return new FixedValue(f);
                case bool b: return b ? BoolValue.True : BoolValue.False;
                case string s: return new StrValue(s);
                case EntityId e: return new EntityValue(e);
                case IEnumerable items:
                    var list = new List<Value>();
                    foreach (var item in items)
                    {
                        var converted = FromClr(item);
                        if (converted == null) return null;
                        list.Add(converted);
                    }
                    return new ListValue(list);
            }
            return null;
        }

        private static string DescribeClr(object? value) => value == null ? "null" : "a C# " + value.GetType().Name;

        /// <summary>돌려준 값 안의 엔티티를 따라갈 수 있는 모양으로 (리스트 안까지).</summary>
        private Value ToScriptDeep(Value value)
        {
            switch (value)
            {
                case EntityValue _: return ToScript(value);
                case ListValue list:
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items) items.Add(ToScriptDeep(item));
                    return new ListValue(items);
                case DictValue dict:
                    var entries = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items) entries.Add(new DictItem(item.Key, ToScriptDeep(item.Value), item.Span));
                    return new DictValue(entries);
            }
            return value;
        }
    }
}
