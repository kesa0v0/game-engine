using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;

namespace Kesa.Engine.Schema
{
    /// <summary>
    /// 게임 전체의 실행 상한 (G451). 엔진이 Def 타입 <see cref="TypeName"/>과 그 Def 하나 <see cref="DefId"/>를 미리 두고, 모드가
    /// <c>patch.set("engine:Rules", "step_budget", 200000)</c>처럼 바꾼다. 두 모드가 겹치면 보통 패치 규칙(로드 순서, G41)이다.
    /// 모드는 이 타입의 Def를 새로 만들 수 없다.
    /// </summary>
    public static class EngineRules
    {
        public const string TypeName = "EngineRules";
        public const string DefId = "engine:Rules";

        /// <summary>읽기 실행 하나(<c>derived</c>, 입구·정책 함수, <c>choose</c> 후보)의 단계 예산 (G101·G224).</summary>
        public const string StepBudget = "step_budget";

        /// <summary>행동 실행 하나(커맨드, 핸들러, 이어 가는 Flow, 봇 한 번)의 단계 예산 (G224·G375).</summary>
        public const string ActionStepBudget = "action_step_budget";

        /// <summary>신호 연쇄의 세대 상한 (G147).</summary>
        public const string MaxSignalDepth = "max_signal_depth";

        /// <summary>제출·답·응답 한 번 안의 스크립트 <c>advance()</c> 합 (G168).</summary>
        public const string MaxAdvancePerCall = "max_advance_per_call";

        /// <summary>정착 한 번의 회차 상한 (G171).</summary>
        public const string MaxSettleRounds = "max_settle_rounds";

        public const int DefaultStepBudget = 100_000;

        /// <summary>한 단계 약 45ns(규모 벤치)로 약 0.5초, 유닛 하나에 7단계면 약 140만 개.</summary>
        public const int DefaultActionStepBudget = 10_000_000;

        /// <summary>지금 도는 실행들의 함수 호출 깊이(프레임 합) 상한 (G452).</summary>
        public const string MaxCallDepth = "max_call_depth";

        public const int DefaultMaxSignalDepth = 64;
        public const int DefaultMaxAdvancePerCall = 10_000;
        public const int DefaultMaxSettleRounds = 64;
        public const int DefaultMaxCallDepth = 200;

        /// <summary>
        /// <see cref="MaxCallDepth"/>의 천장: 호출 한 단이 C# 스택을 쓰므로 모드가 이보다 높일 수 없다 (넘치면 잡을 수 없이 프로세스가 죽는다).
        /// Debug 빌드에서 한 단 약 2.3KB라 1MB 스택의 약 70% (G452).
        /// </summary>
        public const int CallDepthCeiling = 300;

        private static readonly (string Name, int Default, int Max)[] Fields =
        {
            (StepBudget, DefaultStepBudget, int.MaxValue),
            (ActionStepBudget, DefaultActionStepBudget, int.MaxValue),
            (MaxSignalDepth, DefaultMaxSignalDepth, int.MaxValue),
            (MaxAdvancePerCall, DefaultMaxAdvancePerCall, int.MaxValue),
            (MaxSettleRounds, DefaultMaxSettleRounds, int.MaxValue),
            (MaxCallDepth, DefaultMaxCallDepth, CallDepthCeiling),
        };

        public static bool IsEngine(string name) => name == TypeName;

        internal static DefType Type()
        {
            var consts = new List<MemberDecl>();
            foreach (var (name, value, max) in Fields)
            {
                // C# 쪽이 int라 위도 막는다
                var spec = new MemberSpec(MemberKind.Const, PrimitiveTypeRef.Int, SourceSpan.None)
                    { Default = new IntValue(value), Min = new IntValue(1), Max = new IntValue(max) };
                consts.Add(new MemberDecl(name, SourceSpan.None, EngineSignals.Owner, spec));
            }
            return new DefType(new DefTypeDecl(TypeName, SourceSpan.None, EngineSignals.Owner, consts, new List<NameRef>()));
        }

        internal static DefRecord Record(SchemaSet schema)
        {
            schema.TryGetType(TypeName, out var type);
            return new DefRecord(type, DefId, EngineSignals.Owner, SourceSpan.None, null, false, false, Array.Empty<DefField>());
        }
    }
}
