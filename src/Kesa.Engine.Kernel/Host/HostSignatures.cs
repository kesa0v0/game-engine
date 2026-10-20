using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
    /// <summary>로드한 스키마로 푼 호스트 함수의 서명 (spec S7, spec-gaps G215).</summary>
    public sealed class HostSignature
    {
        internal HostSignature(HostFunction function, IReadOnlyList<CommandParameter> parameters, TypeRef? returns)
        {
            Function = function;
            Parameters = parameters;
            Returns = returns;
        }

        public HostFunction Function { get; }

        /// <summary>매개변수와 그 타입·기본값. 커맨드 매개변수와 같은 모양이다.</summary>
        public IReadOnlyList<CommandParameter> Parameters { get; }

        /// <summary>반환 타입. <c>-> None</c>이면 null.</summary>
        public TypeRef? Returns { get; }

        /// <summary>
        /// 풀린 서명의 정규 글: <c>(start: Ref["Tile"], max_steps: int = 50) -> list[Ref["Tile"]]</c>. 공백·따옴표 같은 적은 모양이 아니라
        /// 뜻이 같으면 같다. 저장에 싣고 불러올 때 비교한다 (G221).
        /// </summary>
        public string Canonical
        {
            get
            {
                var parts = new List<string>();
                foreach (var p in Parameters)
                    parts.Add(p.Name + ": " + p.Type.Display + (p.Default == null ? "" : " = " + Show(p.Default)));
                return "(" + string.Join(", ", parts) + ") -> " + (Returns == null ? "None" : Returns.Display);
            }
        }

        private static string Show(Value value)
        {
            switch (value)
            {
                case NoneValue _: return "None";
                case BoolValue b: return b.Value ? "True" : "False";
                case IntValue i: return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case FixedValue f: return f.Value.ToString();
                case StrValue s: return Reporting.CheckReport.Quote(s.Value);
                case ListValue list:
                    var items = new List<string>();
                    foreach (var item in list.Items) items.Add(Show(item));
                    return "[" + string.Join(", ", items) + "]";
                case DictValue dict:
                    var entries = new List<string>();
                    foreach (var item in dict.Items) entries.Add(Show(item.Key) + ": " + Show(item.Value));
                    return "{" + string.Join(", ", entries) + "}";
            }
            return value.KindName;
        }
    }

    /// <summary>
    /// ②b에서 소유 모드의 호스트 함수 서명을 스키마로 푼다. 타입 이름은 소유 모드에 보여야 하고(그 모드가 의존하는 모드의 타입),
    /// 기본값은 타입에 맞아야 한다. 틀리면 소유 모드의 <see cref="Codes.HostSignature"/> 로드 오류다:
    /// 호스트의 버그라 소유 모드와 거기 의존한 모드가 함께 빠진다. 소유 모드가 로드되지 않으면 여기 오지 않는다 (G218).
    /// </summary>
    internal static class HostSignatures
    {
        public static List<HostSignature> Resolve(IReadOnlyList<HostFunction> functions, SchemaSet schema,
            ModVisibility visibility, CallContext context)
        {
            var resolved = new List<HostSignature>();
            // 내장 이름이 먼저 찾아지므로 그 이름 공간에 닿을 수 없다 (G218)
            if (functions.Count > 0 && PureBuiltins.IsRuntimeName(functions[0].Mod))
            {
                context.Error(Codes.HostSignature,
                    "mod id '" + functions[0].Mod + "' is a built-in name, so scripts cannot reach its host functions",
                    new SourceSpan(HostFunction.File(functions[0].Mod, functions[0].Path), 1, 1),
                    "register them for another mod (spec-gaps G218)");
                return resolved;
            }
            foreach (var function in functions)
            {
                var reader = new CommandSignatures.Reader(schema, visibility, null, context, function.Mod, Codes.HostSignature);
                var parameters = new List<CommandParameter>();
                bool ok = true;
                foreach (var p in function.Header.Parameters)
                {
                    var type = reader.Type(p.Annotation!);
                    if (type == null)
                    {
                        ok = false;
                        continue;
                    }
                    Value? @default = null;
                    if (p.Default != null)
                    {
                        var literal = CommandSignatures.Defaults.Literal(p.Default, context, Codes.HostSignature);
                        @default = literal == null
                            ? null
                            : CommandSignatures.Defaults.Fit(p.Name, type, literal, p.Default.Span, context, null,
                                Codes.HostSignature);
                        if (@default == null)
                        {
                            ok = false;
                            continue;
                        }
                        @default = Loading.DefIds.Normalize(@default, type, context.Mod);
                    }
                    parameters.Add(new CommandParameter(p.Name, type, p.Span, @default));
                }
                TypeRef? returns = null;
                if (!(function.Header.Returns is NoneLiteral))
                {
                    returns = reader.Type(function.Header.Returns!);
                    if (returns == null) ok = false;
                }
                if (ok) resolved.Add(new HostSignature(function, parameters, returns));
            }
            return resolved;
        }
    }
}
