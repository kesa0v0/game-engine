using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
    /// <summary>호스트 함수의 효과 등급 (spec S7): 읽기만 하는가, 엔티티를 쓰는가.</summary>
    public enum HostEffect
    {
        Pure,
        Writes,
    }

    /// <summary>
    /// 엔티티를 쓰는 호스트 함수가 첫 인자로 받는 접근기 (spec S7). 엔티티 접근은 이것으로만 한다.
    /// 읽기·쓰기 기능은 아직 없다 (S7 구현 3단계).
    /// </summary>
    public sealed class HostAccess
    {
        internal HostAccess()
        {
        }
    }

    /// <summary>
    /// 호스트(게임 개발자)가 C# 함수를 스크립트에 내놓는 등록부 (spec S7). 모드를 불러오기 전에 채워
    /// <see cref="ContentLoader.Load"/>에 넘긴다. 이름은 <c>소유모드id.점.경로</c>다 (spec-gaps G218):
    /// <c>host.For("core").Pure("def path.find(...) -> ...", "설명", (EntityId start, ...) => ...)</c>.
    /// 등록하는 순간 서명 문법과 람다(인자 개수·이름·C# 타입·반환 타입, G222)를 대조하고 틀리면 <see cref="ArgumentException"/>을 던진다.
    /// Def 타입 이름처럼 모드를 읽어야 아는 것은 로드할 때 본다(<see cref="Codes.HostSignature"/>).
    /// </summary>
    public sealed class HostFunctions
    {
        private readonly SortedDictionary<string, HostModFunctions> _mods =
            new SortedDictionary<string, HostModFunctions>(StringComparer.Ordinal);

        /// <summary><paramref name="mod"/>가 소유하는 호스트 함수를 등록한다. 게임 개발자의 함수는 게임의 기본 모드가 소유한다.</summary>
        public HostModFunctions For(string mod)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            if (!ManifestReader.IsValidId(mod))
                throw new ArgumentException("'" + mod + "' is not a mod id: use [a-z][a-z0-9_]* (spec-gaps G2)", nameof(mod));
            if (!_mods.TryGetValue(mod, out var functions))
            {
                functions = new HostModFunctions(mod);
                _mods.Add(mod, functions);
            }
            return functions;
        }

        /// <summary>등록한 모든 함수, 전체 이름 순.</summary>
        public IReadOnlyList<HostFunction> All
        {
            get
            {
                var all = new List<HostFunction>();
                foreach (var mod in _mods.Values) all.AddRange(mod.Functions);
                all.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                return all;
            }
        }

        /// <summary><paramref name="mod"/>가 소유한 함수, 경로 순.</summary>
        internal IReadOnlyList<HostFunction> Of(string mod) =>
            _mods.TryGetValue(mod, out var functions) ? functions.Functions : (IReadOnlyList<HostFunction>)Array.Empty<HostFunction>();
    }

    /// <summary>한 모드가 소유하는 호스트 함수의 등록 창구 (<see cref="HostFunctions.For"/>).</summary>
    public sealed partial class HostModFunctions
    {
        private readonly SortedDictionary<string, HostFunction> _functions =
            new SortedDictionary<string, HostFunction>(StringComparer.Ordinal);

        internal HostModFunctions(string mod) => Mod = mod;

        public string Mod { get; }

        internal IReadOnlyList<HostFunction> Functions => _functions.Values.ToList();

        private HostModFunctions Add(string signature, string doc, HostEffect effect, Delegate implementation)
        {
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));

            var bag = new DiagnosticBag();
            var header = Parser.ParseHostHeader(signature, HostFunction.File(Mod, null), bag);
            if (header == null)
                throw new ArgumentException("the host function signature \"" + signature + "\" of mod '" + Mod +
                                            "' cannot be read: " + bag.ToSortedList()[0].Message, nameof(signature));
            string name = Mod + "." + header.Name;
            // 진단 위치에 함수 이름이 보이게 한 번 더 읽는다
            header = Parser.ParseHostHeader(signature, HostFunction.File(Mod, header.Name), new DiagnosticBag())!;
            if (_functions.ContainsKey(header.Name))
                throw new ArgumentException("host function " + name + " is registered twice", nameof(signature));
            HostShape.Check(name, header, effect, implementation);
            _functions.Add(header.Name, new HostFunction(Mod, header.Name, effect, signature, doc, header, implementation));
            return this;
        }
    }

    /// <summary>등록된 호스트 함수 하나. 이름(<see cref="Name"/>)만 저장과 진단에 쓰고 C# 이름은 어디에도 남기지 않는다 (spec S7).</summary>
    public sealed class HostFunction
    {
        internal HostFunction(string mod, string path, HostEffect effect, string signature, string doc, FunctionDef header,
            Delegate implementation)
        {
            Mod = mod;
            Path = path;
            Effect = effect;
            Signature = signature;
            Doc = doc;
            Header = header;
            Implementation = implementation;
        }

        /// <summary>소유 모드 id.</summary>
        public string Mod { get; }

        /// <summary>모드 id 뒤의 점 경로 (<c>path.find</c>).</summary>
        public string Path { get; }

        /// <summary>스크립트가 부르는 전체 이름 (<c>core.path.find</c>).</summary>
        public string Name => Mod + "." + Path;

        public HostEffect Effect { get; }

        /// <summary>등록한 <c>def</c> 한 줄 그대로.</summary>
        public string Signature { get; }

        public string Doc { get; }

        internal FunctionDef Header { get; }

        internal Delegate Implementation { get; }

        /// <summary>서명의 진단 위치에 붙는 파일 이름.</summary>
        internal static string File(string mod, string? path) => "<host " + mod + (path == null ? "" : "." + path) + ">";
    }

    /// <summary>
    /// 등록할 때 서명과 C# 람다를 대조한다 (spec-gaps G215·G222). 서명의 모양만 보며 Def 타입이 실제로 있는지는 로드가 본다.
    /// </summary>
    internal static class HostShape
    {
        private const string TypesHint =
            "use int, fixed, bool, str, Ref[X], DefId[X], list[T], dict[K, V] or T | None (spec-gaps G222)";

        public static void Check(string name, FunctionDef header, HostEffect effect, Delegate implementation)
        {
            var names = implementation.Method.GetParameters();
            var types = implementation.GetType().GetMethod("Invoke")!.GetParameters();
            int skip = effect == HostEffect.Writes ? 1 : 0;
            int count = types.Length - skip;

            if (count != header.Parameters.Count)
                throw Fail(name, "the signature takes " + Count(header.Parameters.Count) + ", but the C# function takes " +
                                 Count(count) + (skip == 1 ? " after the HostAccess" : ""));

            for (int i = 0; i < count; i++)
            {
                var p = header.Parameters[i];
                string csharpName = names[i + skip].Name ?? "";
                if (p.Annotation == null)
                    throw Fail(name, "parameter '" + p.Name + "' needs a type: write " + p.Name + ": int, " + p.Name +
                                     ": Ref[Unit], ...");
                if (p.Name != csharpName && p.Name != Snake(csharpName))
                    throw Fail(name, "parameter " + (i + 1) + " is '" + p.Name + "' in the signature but '" + csharpName +
                                     "' in the C# function: name them the same (maxSteps for max_steps is fine)");
                var expected = Expected(name, p.Annotation, argument: true);
                if (!Fits(p.Annotation, types[i + skip].ParameterType, argument: true))
                    throw Fail(name, "parameter '" + p.Name + "' is " + Show(p.Annotation) + ", so the C# function takes " +
                                     expected + ", not " + Show(types[i + skip].ParameterType));
            }

            var returns = header.Returns!;
            var returnType = implementation.GetType().GetMethod("Invoke")!.ReturnType;
            if (returns is NoneLiteral)
            {
                if (effect == HostEffect.Pure)
                    throw Fail(name, "a pure function that returns None does nothing: return a value, or register it with " +
                                     "Writes if it writes entities");
                if (returnType != typeof(void))
                    throw Fail(name, "the signature returns None, but the C# function returns " + Show(returnType));
                return;
            }
            var expectedReturn = Expected(name, returns, argument: false);
            if (returnType == typeof(void))
                throw Fail(name, "the signature returns " + Show(returns) + ", but the C# function returns nothing");
            if (!Fits(returns, returnType, argument: false))
                throw Fail(name, "the signature returns " + Show(returns) + ", so the C# function returns " + expectedReturn +
                                 ", not " + Show(returnType));
        }

        private static ArgumentException Fail(string name, string message) =>
            new ArgumentException("host function " + name + ": " + message, "implementation");

        private static string Count(int n) => n == 1 ? "1 argument" : n + " arguments";

        /// <summary><c>maxSteps</c> → <c>max_steps</c>.</summary>
        internal static string Snake(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            foreach (char c in name)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    if (sb.Length > 0) sb.Append('_');
                    sb.Append((char)(c - 'A' + 'a'));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>서명 타입에 맞는 C# 타입의 이름. 서명 타입의 모양이 아니면 등록 오류.</summary>
        private static string Expected(string name, Expr annotation, bool argument)
        {
            switch (annotation)
            {
                case NameExpr n:
                    switch (n.Name)
                    {
                        case "int": return "long";
                        case "fixed": return "Fixed";
                        case "bool": return "bool";
                        case "str": return "string";
                    }
                    break;
                case IndexExpr index when index.Target is NameExpr generic:
                    switch (generic.Name)
                    {
                        case "Ref" when index.Indices.Count == 1: return "EntityId";
                        case "DefId" when index.Indices.Count == 1: return "string";
                        case "list" when index.Indices.Count == 1:
                            return "IReadOnlyList<" + Expected(name, index.Indices[0], argument) + ">";
                        case "dict" when index.Indices.Count == 2:
                            Expected(name, index.Indices[0], argument);
                            Expected(name, index.Indices[1], argument);
                            return "Value (a dict is passed as the engine value, spec-gaps G222)";
                    }
                    break;
                case BinaryExpr union when union.Op == "|" && (union.Right is NoneLiteral || union.Left is NoneLiteral):
                    var inner = union.Right is NoneLiteral ? union.Left : union.Right;
                    if (inner is NoneLiteral || inner is BinaryExpr) break;
                    var shown = Expected(name, inner, argument);
                    return IsValueTypeName(shown) ? shown + "?" : shown;
            }
            throw Fail(name, "'" + Show(annotation) + "' is not a host function type: " + TypesHint);
        }

        private static bool IsValueTypeName(string shown) =>
            shown == "long" || shown == "Fixed" || shown == "bool" || shown == "EntityId";

        /// <summary>
        /// C# 타입이 서명 타입에 맞는가 (G222). <see cref="Value"/>는 어느 자리든 맞는다. 인자는 엔진이 만들어 넘기는 쪽이라
        /// 정확한 타입이어야 하고, 반환은 그 타입으로 바꿀 수 있으면 된다(<c>List&lt;T&gt;</c>는 <c>list[T]</c>).
        /// </summary>
        private static bool Fits(Expr annotation, Type type, bool argument)
        {
            if (type == typeof(Value)) return true;
            if (annotation is BinaryExpr union && union.Op == "|")
            {
                var inner = union.Right is NoneLiteral ? union.Left : union.Right;
                var underlying = Nullable.GetUnderlyingType(type);
                if (underlying != null) return Fits(inner, underlying, argument);
                return !type.IsValueType && Fits(inner, type, argument);
            }
            switch (annotation)
            {
                case NameExpr n:
                    switch (n.Name)
                    {
                        case "int": return type == typeof(long);
                        case "fixed": return type == typeof(Fixed);
                        case "bool": return type == typeof(bool);
                        case "str": return type == typeof(string);
                    }
                    return false;
                case IndexExpr index when index.Target is NameExpr generic:
                    switch (generic.Name)
                    {
                        case "Ref": return type == typeof(EntityId);
                        case "DefId": return type == typeof(string);
                        case "list":
                            var element = ListElement(type, argument);
                            return element != null && Fits(index.Indices[0], element, argument);
                    }
                    return false;
            }
            return false;
        }

        /// <summary>
        /// <c>list[T]</c> 자리의 C# 원소 타입. 인자는 <c>IReadOnlyList&lt;X&gt;</c> 그 자체여야 하고, 반환은 그것을 구현하면 된다.
        /// </summary>
        private static Type? ListElement(Type type, bool argument)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                return type.GetGenericArguments()[0];
            if (argument) return null;
            foreach (var i in type.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                    return i.GetGenericArguments()[0];
            return null;
        }

        private static string Show(Type type)
        {
            if (type == typeof(long)) return "long";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(void)) return "void";
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return Show(underlying) + "?";
            if (type.IsArray) return Show(type.GetElementType()!) + "[]";
            if (!type.IsGenericType) return type.Name;
            string bare = type.Name.Substring(0, type.Name.IndexOf('`'));
            return bare + "<" + string.Join(", ", type.GetGenericArguments().Select(Show)) + ">";
        }

        internal static string Show(Expr annotation)
        {
            switch (annotation)
            {
                case NameExpr n: return n.Name;
                case NoneLiteral _: return "None";
                case StringLiteral s: return "\"" + s.Value + "\"";
                case IndexExpr index: return Show(index.Target) + "[" + string.Join(", ", index.Indices.Select(Show)) + "]";
                case BinaryExpr b: return Show(b.Left) + " " + b.Op + " " + Show(b.Right);
                case AttributeExpr a: return Show(a.Target) + "." + a.Name;
            }
            return "this";
        }
    }
}
