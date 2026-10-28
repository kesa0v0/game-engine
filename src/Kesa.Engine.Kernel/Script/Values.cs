using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>로드 시점 평가기가 다루는 값. 스키마·Def 쪽 값은 각 영역에서 이 클래스를 상속한다.</summary>
    public abstract class Value
    {
        /// <summary>진단 메시지에 쓰는 종류 이름.</summary>
        public abstract string KindName { get; }
    }

    public sealed class NoneValue : Value
    {
        public static readonly NoneValue Instance = new NoneValue();
        private NoneValue() { }
        public override string KindName => "None";
    }

    public sealed class IntValue : Value
    {
        public IntValue(long value) => Value = value;
        public long Value { get; }
        public override string KindName => "int";
    }

    public sealed class FixedValue : Value
    {
        public FixedValue(Fixed value) => Value = value;
        public Fixed Value { get; }
        public override string KindName => "fixed";
    }

    public sealed class BoolValue : Value
    {
        public static readonly BoolValue True = new BoolValue(true);
        public static readonly BoolValue False = new BoolValue(false);
        private BoolValue(bool value) => Value = value;
        public bool Value { get; }
        public override string KindName => "bool";
    }

    public sealed class StrValue : Value
    {
        public StrValue(string value) : this(value, SourceSpan.None) { }

        public StrValue(string value, SourceSpan origin)
        {
            Value = value;
            Origin = origin;
        }

        public string Value { get; }

        /// <summary>리터럴에서 왔으면 그 위치. 리스트 안의 이름(<c>components=["Health"]</c>)을 가리키는 진단에 쓴다.</summary>
        public SourceSpan Origin { get; }

        public override string KindName => "str";
    }

    public sealed class ListValue : Value
    {
        public ListValue(IReadOnlyList<Value> items) => Items = items;
        public IReadOnlyList<Value> Items { get; }
        public override string KindName => "list";
    }

    public sealed class DictItem
    {
        public DictItem(Value key, Value value, SourceSpan span)
        {
            Key = key;
            Value = value;
            Span = span;
        }

        public Value Key { get; }
        public Value Value { get; }

        /// <summary>키 식의 위치.</summary>
        public SourceSpan Span { get; }
    }

    /// <summary>맵. 항목은 적힌 순서를 유지한다. 키는 <c>str</c> 또는 <c>int</c>만 (spec-gaps G22).</summary>
    public sealed class DictValue : Value
    {
        public DictValue(IReadOnlyList<DictItem> items) => Items = items;
        public IReadOnlyList<DictItem> Items { get; }
        public override string KindName => "dict";
    }

    /// <summary>
    /// <c>def</c>나 람다로 만든 함수. 함수는 한 종류다 (spec S2). 로딩 단계에서는 호출하지 않고 값으로만 다룬다.
    /// </summary>
    public sealed class FunctionValue : Value
    {
        public const string LambdaName = "<lambda>";

        public FunctionValue(FunctionDef definition, string mod)
        {
            Definition = definition;
            Mod = mod;
        }

        public FunctionValue(LambdaExpr lambda, string mod)
        {
            Lambda = lambda;
            Mod = mod;
        }

        /// <summary><c>def</c>로 만들었으면 그 정의.</summary>
        public FunctionDef? Definition { get; }

        /// <summary>람다로 만들었으면 그 식.</summary>
        public LambdaExpr? Lambda { get; }

        public string Name => Definition?.Name ?? LambdaName;
        public IReadOnlyList<Parameter> Parameters => Definition?.Parameters ?? Lambda!.Parameters;

        /// <summary>정의한 모드.</summary>
        public string Mod { get; }

        /// <summary>
        /// 쓰기 함수면 그렇게 만든 첫 속성 대입이나 쓰기 함수 호출의 위치 (spec-gaps G117). 순수면 null.
        /// 모드의 ②b 끝에서 <see cref="FunctionEffects"/>가 채운다.
        /// </summary>
        public SourceSpan? WriteWitness { get; internal set; }

        public bool Writes => WriteWitness != null;

        /// <summary>
        /// 난수 함수면 그렇게 만든 첫 <c>random</c> 사용이나 난수 함수 호출의 위치 (spec-gaps G178). 아니면 null.
        /// 쓰기와 달리 식 안에서도 부를 수 있지만 순수 자리(<c>derived</c>, 조건, 사전 조건, <c>Cond</c>)에는 둘 수 없다.
        /// </summary>
        public SourceSpan? RandomWitness { get; internal set; }

        public bool Random => RandomWitness != null;

        /// <summary>
        /// <c>ui.…</c>를 읽거나 쓰는 곳 (G279·G282). 이 함수나 부르는 함수(문장이든 식이든)가 UI 로컬 기록을 건드리면 처음 그런 곳이고,
        /// 게임 쪽 코드(커맨드·핸들러·반응·정책·<c>derived</c> 등)에서 쓸 수 없다. 아니면 null.
        /// </summary>
        public SourceSpan? UiTouchWitness { get; internal set; }

        public bool TouchesUi => UiTouchWitness != null;

        /// <summary>
        /// UI 로컬 기록에 쓰거나 <c>submit</c>을 하는 곳 (G282). 그런 함수는 <c>@ui_action</c>이나 다른 UI를 쓰는 함수 안에서 문장으로만
        /// 부른다(G117의 쓰기 함수와 같은 규칙). 아니면 null.
        /// </summary>
        public SourceSpan? UiWriteWitness { get; internal set; }

        public bool WritesUi => UiWriteWitness != null;

        /// <summary>
        /// <c>ui.…</c> 자체를 읽거나 쓰는 곳 (<see cref="UiTouchWitness"/>와 달리 <c>submit</c>은 세지 않는다). <c>@bot</c>은 <c>submit</c>은
        /// 하지만 화면 기록은 못 쓴다(G374). 아니면 null.
        /// </summary>
        public SourceSpan? UiStateWitness { get; internal set; }

        public bool UsesUiState => UiStateWitness != null;

        /// <summary>
        /// <c>bot.…</c>을 읽거나 쓰는 곳 (G371·G374). 그런 함수는 <c>@bot</c>과 다른 봇 함수 안에서만 부른다. 아니면 null.
        /// </summary>
        public SourceSpan? BotTouchWitness { get; internal set; }

        public bool TouchesBot => BotTouchWitness != null;

        /// <summary><c>bot.X.y</c>에 쓰는 곳 (G374). 그런 함수는 문장으로만 부른다(<see cref="WritesUi"/>와 같은 규칙). 아니면 null.</summary>
        public SourceSpan? BotWriteWitness { get; internal set; }

        public bool WritesBot => BotWriteWitness != null;

        /// <summary><c>@command</c> 함수인가 (G109). 로직에서 쓸 수 없다 (G118).</summary>
        public bool IsCommand => Definition != null && Definition.IsCommand;

        /// <summary><c>@on</c> 핸들러인가 (G145).</summary>
        public bool IsHandler => Definition != null && Definition.IsHandler;

        /// <summary>
        /// 제출이나 신호로만 실행되는 입구(커맨드·핸들러·반응, 호스트가 부르는 UI 동작)인가. 로직에서 부르거나 값으로 쓸 수 없다
        /// (G118, 핸들러는 G145, UI 동작은 G280).
        /// </summary>
        public bool IsEntry => IsCommand || IsHandler || IsWhen || IsUiAction || IsBot || IsWrap;

        /// <summary><c>@wrap</c>으로 다른 함수를 감싸는 함수인가 (G311·G312). 대상을 부르는 자리에서만 불린다.</summary>
        public bool IsWrap => Definition != null && Definition.IsWrap;

        /// <summary>커맨드를 감싸는 <c>@wrap</c> 함수인가 (G316). 로드 검사가 대상을 푼 뒤에 정해진다.</summary>
        public bool WrapsCommand { get; internal set; }

        /// <summary>커맨드 본문처럼 도는가: 커맨드와 커맨드를 감싸는 함수 (G320). 쓰기 함수가 아니어도 문장으로 불리면 쓰기 모드다.</summary>
        public bool RunsAsCommand => IsCommand || WrapsCommand;

        /// <summary><c>@when</c> 반응인가 (G169).</summary>
        public bool IsWhen => Definition != null && Definition.IsWhen;

        /// <summary><c>@ui_action</c>인가 (G276·G280).</summary>
        public bool IsUiAction => Definition != null && Definition.IsUiAction;

        /// <summary><c>@bot</c>인가 (G369).</summary>
        public bool IsBot => Definition != null && Definition.IsBot;

        /// <summary>메시지에 쓰는 입구 종류: <c>@command</c>, <c>@on handler</c>, <c>@when reaction</c> 또는 <c>@ui_action</c>.</summary>
        public string EntryKind => IsHandler ? "@on handler" : IsWhen ? "@when reaction" : IsUiAction ? "@ui_action" : IsBot ? "@bot" :
            IsWrap ? "@wrap function" : "@command";

        /// <summary><c>@view_rule</c> 함수인가 (G315). 순수 자리다.</summary>
        public bool IsViewRule => Definition != null && Definition.DecoratorName == Interpreter.ViewRuleName;

        public override string KindName => "function";
    }

    /// <summary>
    /// 지금 평가할 수 없어 식 그대로 둔 값. <c>inherit.필드</c>가 든 Def 필드 식은
    /// 상속 펼치기(spec S1 ④)에서 부모 값을 알고 나서 평가한다.
    /// </summary>
    public sealed class DeferredValue : Value
    {
        public DeferredValue(Expr expr) => Expr = expr;
        public Expr Expr { get; }
        public override string KindName => "deferred expression";
    }

    /// <summary>인자 하나: 키워드 인자면 이름이 있다.</summary>
    public sealed class ArgValue
    {
        public ArgValue(string? name, Value value, SourceSpan span)
        {
            Name = name;
            Value = value;
            Span = span;
        }

        public string? Name { get; }
        public Value Value { get; }
        public SourceSpan Span { get; }
    }

    /// <summary>호출할 수 있는 내장 값 (스키마 함수, Def 생성자 등).</summary>
    public abstract class CallableValue : Value
    {
        public abstract string Name { get; }

        /// <summary>true면 <c>inherit</c>가 든 인자를 <see cref="DeferredValue"/>로 받는다.</summary>
        public virtual bool AcceptsDeferred => false;

        public abstract Value Call(CallContext context, SourceSpan span, IReadOnlyList<ArgValue> args);
    }

    public sealed class BuiltinFunction : CallableValue
    {
        private readonly System.Func<CallContext, SourceSpan, IReadOnlyList<ArgValue>, Value> _body;

        public BuiltinFunction(string name, System.Func<CallContext, SourceSpan, IReadOnlyList<ArgValue>, Value> body)
        {
            Name = name;
            _body = body;
        }

        public override string Name { get; }
        public override string KindName => "built-in function";

        public override Value Call(CallContext context, SourceSpan span, IReadOnlyList<ArgValue> args) =>
            _body(context, span, args);
    }

    /// <summary><c>x.name</c>을 받는 내장 값 (<c>patch.set</c> 등).</summary>
    public interface IAttributeSource
    {
        Value GetAttribute(CallContext context, SourceSpan span, string name);
    }

    /// <summary><c>x[...]</c>를 받는 내장 값 (타입 표기 <c>list[str]</c>, <c>Ref["X"]</c> 등).</summary>
    public interface ISubscriptable
    {
        Value Subscript(CallContext context, SourceSpan span, IReadOnlyList<Value> indices);
    }
}
