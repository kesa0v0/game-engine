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
        /// 자유 커맨드가 누구로서 행동하는지 고르는 람다 (<c>acts_for=lambda party: party</c>, spec-gaps G122·G130).
        /// 매개변수는 커맨드 매개변수를 이름으로 골라 받는다. null이면 <c>acts_for=None</c>(누구로서도 아님, G123)이거나
        /// 자유 커맨드가 아니다.
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
