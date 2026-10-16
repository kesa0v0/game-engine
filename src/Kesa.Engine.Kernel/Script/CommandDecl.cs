using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// <c>@command</c>로 선언한 커맨드 (spec S6). id는 자동으로 <c>모드id.함수이름</c>이다 (spec-gaps G109).
    /// </summary>
    public sealed class CommandDecl
    {
        public CommandDecl(string mod, FunctionValue function, bool free, SourceSpan span, FunctionValue? actsFor = null)
        {
            Mod = mod;
            Function = function;
            Free = free;
            Span = span;
            ActsFor = actsFor;
        }

        /// <summary>커맨드 로그·난수열 키·UI가 가리키는 이름: <c>모드id.함수이름</c> (G109).</summary>
        public string Id => Mod + "." + Function.Definition!.Name;

        public string Mod { get; }
        public FunctionValue Function { get; }

        /// <summary><c>@command(free=True)</c>: Request 없이 제출할 수 있다 (자유 커맨드, spec S6).</summary>
        public bool Free { get; }

        /// <summary>
        /// 커맨드가 누구로서 행동하는지 고르는 람다 (<c>acts_for=lambda party: party</c>, spec-gaps G122·G130·G139).
        /// 매개변수는 커맨드 매개변수를 이름으로 골라 받는다. 응답 커맨드에서는 답하는 엔티티다.
        /// null이면 자유 커맨드의 <c>acts_for=None</c>(누구로서도 아님, G123)이다.
        /// </summary>
        public FunctionValue? ActsFor { get; }

        /// <summary><c>@command</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }

        /// <summary>
        /// 어노테이션을 푼 매개변수 (G114). ②b 끝에서 채운다. 어노테이션이 틀린 커맨드는 모드와 함께 빠지므로 로드 결과에서는 늘 있다.
        /// </summary>
        public IReadOnlyList<CommandParameter> Parameters { get; internal set; } = Array.Empty<CommandParameter>();
    }

    /// <summary>
    /// <c>@submit_policy</c>로 선언한 제출 정책 함수 (spec S6 입구, spec-gaps G122·G124·G130). 순수 함수
    /// <c>(issuer, target)</c>이고 활성 모드 전체에서 최대 하나다. 교체는 <c>override=True</c>.
    /// </summary>
    public sealed class SubmitPolicyDecl
    {
        public SubmitPolicyDecl(string mod, FunctionValue function, bool isOverride, SourceSpan span)
        {
            Mod = mod;
            Function = function;
            IsOverride = isOverride;
            Span = span;
        }

        public string Mod { get; }
        public FunctionValue Function { get; }
        public bool IsOverride { get; }

        /// <summary><c>@submit_policy</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }

        /// <summary>교체한 정책 (교체가 아니면 null).</summary>
        public SubmitPolicyDecl? Replaced { get; internal set; }
    }

    /// <summary>
    /// <c>@on(Damaged)</c>로 신호에 붙인 핸들러 (spec S5, spec-gaps G145). 매개변수는 신호의 인자 중 필요한 것을 이름으로 고른다.
    /// </summary>
    public sealed class HandlerDecl
    {
        public HandlerDecl(string mod, FunctionValue function, SignalType signal, SourceSpan span)
        {
            Mod = mod;
            Function = function;
            Signal = signal;
            Span = span;
        }

        public string Mod { get; }
        public FunctionValue Function { get; }
        public SignalType Signal { get; }

        /// <summary><c>@on</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }

        /// <summary><c>stage="core.economy"</c>의 단계 id (spec S5, spec-gaps G152). 없으면 그 신호의 단계 핸들러가 모두 끝난 뒤에 돈다.</summary>
        public string? Stage { get; internal set; }

        public SourceSpan StageSpan { get; internal set; }
    }

    /// <summary>
    /// <c>@when(조건)</c>으로 선언한 반응 (spec S5, spec-gaps G169). 함수에 매개변수가 없으면 전역 조건, <c>Ref[Def타입]</c> 하나면 그
    /// 타입의 엔티티마다 따로 평가한다. 조건은 같은 수의 매개변수를 받는 순수 함수다. 정착 지점에서 거짓→참이 되면 본문이 Flow로 돈다.
    /// </summary>
    public sealed class WhenDecl
    {
        public WhenDecl(string mod, FunctionValue function, FunctionValue condition, SourceSpan span)
        {
            Mod = mod;
            Function = function;
            Condition = condition;
            Span = span;
        }

        /// <summary>반응 id: <c>모드id.함수이름</c>. 거짓→참 기록과 결과 항목이 이 이름을 쓴다.</summary>
        public string Id => Mod + "." + Function.Definition!.Name;

        public string Mod { get; }
        public FunctionValue Function { get; }
        public FunctionValue Condition { get; }

        /// <summary><c>@when</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }

        /// <summary><c>stage=</c>의 단계 id (G152). 없으면 단계가 있는 반응들 뒤에 돈다.</summary>
        public string? Stage { get; internal set; }

        public SourceSpan StageSpan { get; internal set; }

        /// <summary>
        /// <c>repeat=True</c>: 거짓→참이 아니라 참인 동안 틱마다 한 번, 틱 끝 정착에서만 발동한다 (S5, G173). 커맨드 뒤 정착에서는 보지 않는다.
        /// </summary>
        public bool Repeat { get; internal set; }

        /// <summary>
        /// <c>cooldown=N</c>: 발동한 뒤 N틱 동안 그 (반응, 대상)의 조건을 거짓으로 본다 (S5, G174). 끝난 뒤에도 참이면 그때 거짓→참으로 발동한다. 없으면 null.
        /// </summary>
        public long? Cooldown { get; internal set; }

        /// <summary><c>once=</c> (S5, G175): 없음, <c>True</c>(반응, 엔티티마다 한 번), <c>"game"</c>(반응 전체에 한 번).</summary>
        public WhenOnce Once { get; internal set; }

        /// <summary>엔티티마다 평가하면 그 Def 타입 이름, 전역 조건이면 null. ②b 끝에서 매개변수 어노테이션을 풀어 채운다.</summary>
        public string? Subject { get; internal set; }

        /// <summary>엔티티마다 평가하는 반응의 매개변수 이름.</summary>
        public string? Parameter => Function.Definition!.Parameters.Count == 1 ? Function.Definition.Parameters[0].Name : null;
    }

    /// <summary><c>@when(once=...)</c>의 단위 (G175).</summary>
    public enum WhenOnce
    {
        None,

        /// <summary><c>once=True</c>: (반응, 엔티티)마다 한 번. 전역 조건이면 반응에 한 번.</summary>
        PerEntity,

        /// <summary><c>once="game"</c>: 어느 엔티티든 한 번 발동하면 그 반응은 끝난다.</summary>
        Game,
    }

    /// <summary>커맨드 매개변수 하나: 이름과 타입 (spec-gaps G114).</summary>
    public sealed class CommandParameter
    {
        public CommandParameter(string name, TypeRef type, SourceSpan span, Value? @default = null)
        {
            Name = name;
            Type = type;
            Span = span;
            Default = @default;
        }

        public string Name { get; }
        public TypeRef Type { get; }
        public SourceSpan Span { get; }

        /// <summary>
        /// 기본값 (G116). 리터럴을 타입에 맞춘 값이고(<c>fixed</c> 자리의 int는 올림) 제출 때 빠진 인자를 채운다. 없으면 null.
        /// </summary>
        public Value? Default { get; }
    }
}
