using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
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
