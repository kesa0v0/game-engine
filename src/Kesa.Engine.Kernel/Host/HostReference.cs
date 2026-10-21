using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Host
{
    /// <summary>
    /// Def 파일이나 패치 값 자리에 적은 호스트 함수 (<c>Goal("B", check=core.path.reachable)</c>). 로드 때는 월드가 없으므로 이름과
    /// 서명만 든다. 칸에 맞는지(효과 등급·서명)는 로드가 보고, 게임이 읽을 때 그 월드의 호스트 함수 값으로 바뀐다
    /// (spec-gaps G221·G291). 이 값을 직접 부르면 오류다.
    /// </summary>
    public sealed class HostReference : HostCallable
    {
        public HostReference(HostSignature signature) => Signature = signature;

        public HostSignature Signature { get; }

        public override string Name => Signature.Function.Name;

        public override bool Writes => Signature.Function.Effect == HostEffect.Writes;

        public override Value Call(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords) =>
            throw new ScriptRuntimeException(Codes.RunScript, Name + "() is a host function and only runs in a started game",
                "a content file can name it as a value (check=" + Name + ") but not call it");
    }

    /// <summary>
    /// Def 파일 실행 중 보이는 호스트 이름 공간 (<c>core</c>, <c>core.path</c>): 속성으로 내려가 함수에 닿으면 <see cref="HostReference"/>다.
    /// </summary>
    internal sealed class HostReferenceNamespace : Value, IAttributeSource
    {
        private readonly HostNames _names;
        private readonly System.Func<string, HostSignature?> _signatures;
        private readonly string _prefix;

        public HostReferenceNamespace(HostNames names, System.Func<string, HostSignature?> signatures, string prefix)
        {
            _names = names;
            _signatures = signatures;
            _prefix = prefix;
        }

        public override string KindName => "host function group";

        public Value GetAttribute(CallContext context, SourceSpan span, string name)
        {
            string full = _prefix + "." + name;
            if (_names.IsFunction(full) && _signatures(full) is { } signature) return new HostReference(signature);
            if (_names.IsNamespace(full)) return new HostReferenceNamespace(_names, _signatures, full);
            throw context.Fail(Codes.EvlUnknownName, _prefix + " has no host function '" + name + "'", span,
                hint: Suggest.Hint(name, _names.Children(_prefix)));
        }
    }
}
