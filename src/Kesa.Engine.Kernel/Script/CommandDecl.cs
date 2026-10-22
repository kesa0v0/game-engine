using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// <c>@command</c>로 선언한 커맨드 (spec S6). id는 자동으로 <c>모드id.함수이름</c>이다 (spec-gaps G109).
    /// <c>@ui_action</c>(G276·G280)도 이 모양이다: 매개변수와 사전 조건이 같고 <see cref="IsUiAction"/>이 참이며, 커맨드와 다른 목록
    /// (<see cref="ModFunctions.UiActions"/>)에 든다. 그래서 제출 길에는 나타나지 않는다.
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

        /// <summary><c>@ui_action</c>인가 (G280). 그러면 <see cref="Free"/>는 거짓이고 <see cref="ActsFor"/>는 없다(입구 검사가 없다).</summary>
        public bool IsUiAction => Function.IsUiAction;

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
    /// <c>@view_rule(대상)</c>으로 선언한 보기 규칙 (spec-gaps G315). 순수 함수 <c>(viewer, entity)</c>이고 답은 <c>True</c>·<c>False</c>·
    /// <c>None</c>(앞 규칙에 맡김)이다. 대상이 컴포넌트면 그 컴포넌트 층, Def 타입이면 그 타입 엔티티의 엔티티 층(G197)이다.
    /// 같은 대상의 규칙은 나중에 로드된 모드부터 묻는다.
    /// </summary>
    public sealed class ViewRuleDecl
    {
        public ViewRuleDecl(string mod, FunctionValue function, string target, SourceSpan targetSpan, SourceSpan span)
        {
            Mod = mod;
            Function = function;
            Target = target;
            TargetSpan = targetSpan;
            Span = span;
        }

        /// <summary>메시지에 쓰는 이름.</summary>
        public string Noun => "view rule";

        public string Mod { get; }
        public FunctionValue Function { get; }

        /// <summary>대상의 이름 (컴포넌트나 Def 타입). 무엇인지는 로더가 푼다(<see cref="IsComponent"/>).</summary>
        public string Target { get; }

        public SourceSpan TargetSpan { get; }

        /// <summary>대상이 컴포넌트인가 (아니면 Def 타입). 로더가 대상을 푼 뒤에 정해진다.</summary>
        public bool IsComponent { get; internal set; }

        /// <summary><c>@view_rule</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }
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

        /// <summary>
        /// <c>now=True</c>: 즉시 핸들러 (spec-gaps G310). 새 Flow가 아니라 <c>emit</c>한 흐름 안에서 그 자리에서 함수처럼 돈다.
        /// </summary>
        public bool Now { get; internal set; }
    }

    /// <summary>
    /// <c>@wrap(pandemic.cure_cost)</c>로 다른 함수를 감싸는 함수 (spec-gaps G311~G314). 첫 매개변수가 바로 안쪽 함수이고 나머지는
    /// 대상과 같다. 대상은 모드의 이름이 다 정해진 뒤 로더가 푼다(<see cref="Target"/>).
    /// </summary>
    public sealed class WrapDecl
    {
        public WrapDecl(string mod, FunctionValue function, string? targetMod, string targetName, SourceSpan span, SourceSpan targetSpan)
        {
            Mod = mod;
            Function = function;
            TargetMod = targetMod;
            TargetName = targetName;
            Span = span;
            TargetSpan = targetSpan;
        }

        public string Mod { get; }
        public FunctionValue Function { get; }

        /// <summary><c>pandemic.cure_cost</c>의 <c>pandemic</c>. 맨 이름(<c>@wrap(cure_cost)</c>)이면 null: 자기 모드다.</summary>
        public string? TargetMod { get; }

        public string TargetName { get; }

        /// <summary><c>@wrap</c> 데코레이터의 위치.</summary>
        public SourceSpan Span { get; }

        public SourceSpan TargetSpan { get; }

        /// <summary>푼 대상 함수. 로드 오류면 null이고 그 감싸기는 쓰이지 않는다.</summary>
        public FunctionValue? Target { get; internal set; }
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
