using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 순수 함수 실행 중의 오류 (spec-gaps G95~G103). 코드는 <c>RUN</c> 영역이다. 호스트가 값을 읽다가 던질 때는 위치가 비어 있고,
    /// 실행기가 오류가 난 식의 위치와 호출 흔적을 채운다.
    /// </summary>
    public sealed class ScriptRuntimeException : Exception
    {
        public ScriptRuntimeException(string code, string message, string? hint = null) : base(message)
        {
            Code = code;
            Hint = hint;
            Trace = Array.Empty<string>();
        }

        public string Code { get; }
        public string? Hint { get; }

        /// <summary>오류가 난 식의 위치. 아직 모르면 <see cref="SourceSpan.None"/>.</summary>
        public SourceSpan Span { get; internal set; }

        /// <summary>호출 흔적: 안쪽 함수부터 "이름 (호출한 위치)".</summary>
        public IReadOnlyList<string> Trace { get; internal set; }

        public override string ToString() =>
            Code + ": " + Message + (Span.Line > 0 ? " at " + Span : "") + (Hint == null ? "" : " (" + Hint + ")");
    }

    /// <summary>함수 본문이 이름을 찾는 모드 최상위 이름 공간 (같은 모드의 함수·상수).</summary>
    public interface IScriptGlobals
    {
        Value? Lookup(string mod, string name);
    }

    /// <summary>
    /// <c>x.name</c>을 받는 런타임 값 (엔티티의 컴포넌트, 기여 행 등). 읽을 수 없으면
    /// <see cref="ScriptRuntimeException"/>을 던진다.
    /// </summary>
    public interface IRuntimeAttributes
    {
        Value GetAttribute(string name);
    }

    /// <summary><c>x[key]</c>를 받는 런타임 값 (키 컴포넌트의 줄 등).</summary>
    public interface IRuntimeIndexable
    {
        Value GetItem(Value key);
    }

    /// <summary>값 동등 비교를 스스로 정하는 값 (엔티티 id 등). 그 밖의 값은 참조 동등이다.</summary>
    public interface IValueEquality
    {
        bool ValueEquals(Value other);
    }

    /// <summary><c>range(...)</c>의 결과. 원소를 만들어 두지 않고 반복할 때 센다.</summary>
    public sealed class RangeValue : Value
    {
        public RangeValue(long start, long step, long count)
        {
            Start = start;
            Step = step;
            Count = count;
        }

        public long Start { get; }
        public long Step { get; }
        public long Count { get; }

        public long At(long index) => checked(Start + index * Step);

        public override string KindName => "range";
    }

    /// <summary>
    /// 함수 본문 안에서 만든 람다: 만든 함수의 지역 변수를 함께 들고 있고 부를 때 읽는다 (파이썬처럼 늦게 묶임).
    /// </summary>
    public sealed class ClosureValue : Value
    {
        public ClosureValue(FunctionValue function, IReadOnlyDictionary<string, Value> captured)
        {
            Function = function;
            Captured = captured;
        }

        public FunctionValue Function { get; }
        public IReadOnlyDictionary<string, Value> Captured { get; }

        public override string KindName => "function";
    }

    /// <summary>실행기가 부르는 내장 함수. 인자는 위치 인자뿐이다.</summary>
    public sealed class PureBuiltin : Value
    {
        public PureBuiltin(string name, Func<IReadOnlyList<Value>, Value> body)
        {
            Name = name;
            Body = body;
        }

        public string Name { get; }
        public Func<IReadOnlyList<Value>, Value> Body { get; }

        public override string KindName => "built-in function";
    }
}
