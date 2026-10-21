using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 선언 보너스 하나 (spec-gaps G295~G297): <c>contrib("Health.max", "add", 3, key="Medicine")</c>. Def의 <c>const(list[Contrib])</c>
    /// 칸에 살고, 커널이 붙은 것들의 이 값을 모아 <c>contribs()</c>에 준다. 키는 만들 때 쓴 모드 기준 전체 이름으로 바꿔 둔다.
    /// </summary>
    public sealed class ContribValue : Value, IValueEquality, IRuntimeAttributes
    {
        public ContribValue(string component, string member, string op, Fixed amount, string? key, SourceSpan span)
        {
            Component = component;
            Member = member;
            Op = op;
            Amount = amount;
            Key = key;
            Span = span;
        }

        /// <summary>대상 컴포넌트 이름 (<c>"Health.max"</c>의 <c>Health</c>).</summary>
        public string Component { get; }

        /// <summary>대상 <c>derived</c> 이름.</summary>
        public string Member { get; }

        public string Op { get; }
        public Fixed Amount { get; }

        /// <summary>키 컴포넌트 줄의 키 Def 전체 이름. 키 없는 컴포넌트면 null.</summary>
        public string? Key { get; }

        /// <summary><c>contrib(...)</c>을 쓴 곳.</summary>
        public SourceSpan Span { get; }

        public string Target => Component + "." + Member;

        public override string KindName => "contrib";

        /// <summary>대상·연산·값·키가 같으면 같다 (쓴 곳은 상관없다, G142).</summary>
        public bool ValueEquals(Value other) =>
            other is ContribValue c && c.Component == Component && c.Member == Member && c.Op == Op && c.Amount.Equals(Amount) &&
            c.Key == Key;

        public Value GetAttribute(string name)
        {
            switch (name)
            {
                case "target": return new StrValue(Target);
                case "op": return new StrValue(Op);
                case "value": return new FixedValue(Amount);
                case "key": return Key == null ? (Value)NoneValue.Instance : new StrValue(Key);
            }
            throw new ScriptRuntimeException(Codes.RunScript, "a contrib has no '" + name + "'",
                Suggest.Hint(name, new[] { "target", "op", "value", "key" }));
        }
    }
}
