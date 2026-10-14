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
        private readonly List<string> _trace = new List<string>();

        public ScriptRuntimeException(string code, string message, string? hint = null) : base(message)
        {
            Code = code;
            Hint = hint;
        }

        public string Code { get; }
        public string? Hint { get; }

        /// <summary>오류가 난 식의 위치. 아직 모르면 <see cref="SourceSpan.None"/>.</summary>
        public SourceSpan Span { get; internal set; }

        /// <summary>
        /// 호출 흔적, 안쪽부터: "이름 (호출한 위치)"와, 다른 <c>derived</c>를 읽다가 난 오류면 그 경계의 "reading …"과
        /// 바깥 함수들 (G105 보강). 실행 단위마다 자기 프레임을 한 번씩 덧붙인다.
        /// </summary>
        public IReadOnlyList<string> Trace => _trace;

        /// <summary>마지막으로 프레임을 덧붙인 실행 단위 (같은 실행 안에서 거듭 붙이지 않게).</summary>
        internal object? TracedBy { get; set; }

        internal void AddTrace(string line) => _trace.Add(line);

        public override string ToString() =>
            Code + ": " + Message + (Span.Line > 0 ? " at " + Span : "") + (Hint == null ? "" : " (" + Hint + ")");
    }

    /// <summary>
    /// 평가 단계 계량기 (spec-gaps G101). 실행 하나가 새로 만들어 쓰고, 호스트가 최상위 읽기 하나 동안 같은 계량기를 넘기면
    /// 그 안의 중첩 실행(다른 <c>derived</c> 읽기)이 남은 단계를 이어서 쓴다.
    /// </summary>
    public sealed class StepMeter
    {
        public StepMeter(int budget) => Budget = budget;

        public int Budget { get; }

        /// <summary>지금까지 쓴 단계 수.</summary>
        public int Used { get; internal set; }
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

    /// <summary><c>for x in value</c>로 돌 수 있는 런타임 값 (키 컴포넌트의 키들 등). 시작 시점의 목록을 돌려준다.</summary>
    public interface IRuntimeIterable
    {
        IReadOnlyList<Value> Iterate();
    }

    /// <summary>
    /// 크기와 키 포함을 아는 런타임 묶음 (키 컴포넌트의 줄 묶음). 파이썬 <c>dict</c>처럼 <c>len</c>, <c>in</c>(키가 있는가),
    /// 참/거짓(비면 거짓)을 받는다 (spec-gaps G104).
    /// </summary>
    public interface IRuntimeCollection : IRuntimeIterable
    {
        int Size();

        bool ContainsKey(Value key);
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
        public ClosureValue(FunctionValue function, IReadOnlyDictionary<string, Value> captured,
            SortedSet<string> capturedLocals)
        {
            Function = function;
            Captured = captured;
            CapturedLocals = capturedLocals;
        }

        public FunctionValue Function { get; }
        public IReadOnlyDictionary<string, Value> Captured { get; }

        /// <summary>만든 함수의 지역 변수 이름 전체 (아직 값이 없는 것 포함). 람다 안에서도 전역 대신 이 이름을 본다 (G104).</summary>
        public SortedSet<string> CapturedLocals { get; }

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

        /// <summary>키워드 인자도 받는 내장 (<c>text("키", n=3)</c>).</summary>
        public PureBuiltin(string name, Func<IReadOnlyList<Value>, IReadOnlyDictionary<string, Value>, Value> body)
        {
            Name = name;
            KeywordBody = body;
            Body = positional => body(positional, new SortedDictionary<string, Value>(StringComparer.Ordinal));
        }

        public string Name { get; }
        public Func<IReadOnlyList<Value>, Value> Body { get; }

        /// <summary>키워드 인자를 받으면 있다. 없으면 키워드 인자는 오류다.</summary>
        public Func<IReadOnlyList<Value>, IReadOnlyDictionary<string, Value>, Value>? KeywordBody { get; }

        public override string KindName => "built-in function";
    }

    /// <summary>
    /// 화면에 나갈 메시지: 텍스트 키와 이름 있는 인자 (<c>text("msg.walls_full", walls=3)</c>, spec S2 텍스트 키 타입·X4 P1,
    /// spec-gaps G113). 번역은 하지 않는다: 키와 인자만 들고 다니고 화면이 번역한다.
    /// </summary>
    public sealed class TextValue : Value
    {
        public TextValue(string key, IReadOnlyDictionary<string, Value> args)
        {
            Key = key;
            Args = args;
        }

        public string Key { get; }

        /// <summary>인자 (이름 서수 순).</summary>
        public IReadOnlyDictionary<string, Value> Args { get; }

        public override string KindName => "text";
    }
}
