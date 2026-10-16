using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수 효과 판정 (spec S2 함수 효과 표, spec-gaps G117). 본문에 속성 대입(엔티티 쓰기)이 있거나 같은 모드의 쓰기 함수를
    /// 이름으로 문장 호출하는 함수는 쓰기 함수다(전이적). 모드 사이에는 함수를 가져오는 길이 없으므로 모드 안에서 끝난다.
    /// 모드의 이름이 다 정해진 ②b 끝에서 한다. 이름으로 알 수 없는 호출(속성 호출, 함수 값)은 실행 중 안전망이 막는다.
    /// <list type="bullet">
    /// <item>쓰기 함수는 문장으로만 부른다: 식 안의 호출은 로드 오류다 (람다 본문은 식이라 늘 오류).</item>
    /// <item>순수 자리(<c>derived</c> 함수, <c>field</c>의 함수 기본값, <c>Cond[...]</c> 기본값)에 쓰기 함수를 둘 수 없다.
    /// Def 값과 패치로 넣은 <c>Cond[...]</c> 값은 값 검사(<c>PatchOperationCheck.Mismatch</c>)가 본다.</item>
    /// </list>
    /// </summary>
    public static class FunctionEffects
    {
        public static void Check(ModFunctions functions, ModScope scope, SchemaSet schema, CallContext context)
        {
            var bodies = new List<Body>();
            foreach (var function in functions.Functions)
            {
                var body = new Body(function);
                var walker = new Walker(scope, body);
                if (function.Definition != null)
                    walker.Block(function.Definition.Body, FunctionLocals.Of(function.Definition));
                else
                    walker.Expr(function.Lambda!.Body, FunctionLocals.Parameters(function.Parameters));
                bodies.Add(body);
            }

            // 쓰기 함수 판정: 정의 순서대로 고정점까지 (순서가 정해진 목록만 돈다, P2)
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (body.Function.Writes) continue;
                    SourceSpan? witness = body.FirstWrite;
                    foreach (var call in body.StatementCalls)
                    {
                        if (!call.Callee.Writes) continue;
                        // 대입과 쓰기 호출 중 먼저 적힌 쪽
                        if (witness == null || Before(call.Span, witness.Value)) witness = call.Span;
                        break;
                    }
                    if (witness == null) continue;
                    body.Function.WriteWitness = witness;
                    changed = true;
                }
            }

            // 난수 함수 판정: random을 쓰거나 난수 함수를 부르는(문장이든 식이든) 함수, 고정점까지 (G178)
            changed = true;
            while (changed)
            {
                changed = false;
                foreach (var body in bodies)
                {
                    if (body.Function.Random) continue;
                    SourceSpan? witness = body.FirstRandom;
                    foreach (var calls in new[] { body.StatementCalls, body.ExpressionCalls })
                    foreach (var call in calls)
                        if (call.Callee.Random && (witness == null || Before(call.Span, witness.Value))) witness = call.Span;
                    if (witness == null) continue;
                    body.Function.RandomWitness = witness;
                    changed = true;
                }
            }

            // 사전 조건은 UI가 거듭 확인하므로 같은 답을 내야 한다 (G178)
            const string randomHint = "draw random numbers in the body instead; derived values, conditions and preconditions must give " +
                                      "the same answer every time (spec-gaps G178)";
            foreach (var body in bodies)
            {
                foreach (var span in body.PreconditionRandom)
                    context.Error(Codes.CmdWriteEffect, "random numbers cannot be drawn in 'with preconditions:'", span, randomHint);
                foreach (var call in body.PreconditionCalls)
                {
                    if (!call.Callee.Random) continue;
                    context.Error(Codes.CmdWriteEffect,
                        "'" + call.Callee.Name + "' draws random numbers, so it cannot be used in 'with preconditions:'", call.Span,
                        randomHint, new[] { call.Callee.RandomWitness!.Value });
                }
            }

            // 커맨드는 제출로만 실행된다: 로직에서 부르거나 값으로 쓰면 사전 조건과 권한을 건너뛴다 (G118)
            foreach (var body in bodies)
            foreach (var use in body.CommandUses)
            {
                context.Error(Codes.CmdWriteEffect,
                    "'" + use.Callee.Name + "' is a " + use.Callee.EntryKind + ", so logic cannot call it or use it as a value", use.Span,
                    "move the shared part into a plain function that the commands call (spec-gaps G118)",
                    new[] { use.Callee.Definition!.Span });
            }

            const string statementHint =
                "call it on its own line, or as 'x = ...(...)', in a @command body or in another function that changes " +
                "the world (spec-gaps G117, G135)";
            foreach (var body in bodies)
            foreach (var call in body.ExpressionCalls)
            {
                if (!call.Callee.Writes || call.Callee.IsEntry) continue;
                context.Error(Codes.CmdWriteEffect,
                    "'" + call.Callee.Name + "' changes the world, so it can only be called as a statement", call.Span,
                    statementHint, new[] { call.Callee.WriteWitness!.Value });
            }
            foreach (var body in bodies)
            foreach (var call in body.BuiltinExpressionCalls)
            {
                context.Error(Codes.CmdWriteEffect,
                    call.Key + "() " + PureBuiltins.EffectPhrase(call.Key) + ", so it can only be called as a statement",
                    call.Value, statementHint);
            }

            // 입구 검사는 순수하다: 거부가 상태를 건드리지 않는다 (C10, G122)
            foreach (var policy in functions.Policies)
            {
                if (policy.Function.Random)
                    context.Error(Codes.CmdWriteEffect,
                        "the submit policy '" + policy.Function.Name + "' draws random numbers; it must give the same answer every time",
                        policy.Function.Definition!.Span, randomHint, new[] { policy.Function.RandomWitness!.Value });
                if (!policy.Function.Writes) continue;
                context.Error(Codes.CmdWriteEffect,
                    "the submit policy '" + policy.Function.Name + "' changes the world; it can only read",
                    policy.Function.Definition!.Span, "the gate must not change anything (spec-gaps G122)",
                    new[] { policy.Function.WriteWitness!.Value });
            }

            // when 조건은 순수하다: 정착마다 다시 평가하므로 읽기만 한다 (S5, G169)
            foreach (var when in functions.Whens)
            {
                if (when.Condition.Random)
                    context.Error(Codes.CmdWriteEffect,
                        "the condition of @when on '" + when.Function.Name + "' is '" + when.Condition.Name +
                        "', which draws random numbers; it must give the same answer every time",
                        when.Span, "draw random numbers in the reaction's body instead (spec-gaps G178)",
                        new[] { when.Condition.RandomWitness!.Value });
                if (!when.Condition.Writes) continue;
                context.Error(Codes.CmdWriteEffect,
                    "the condition of @when on '" + when.Function.Name + "' is '" + when.Condition.Name + "', which changes the world; it can only read",
                    when.Span, "change the world in the reaction's body instead (spec-gaps G117, G169)",
                    new[] { when.Condition.WriteWitness!.Value });
            }

            CheckPureSlots(schema, context);
        }

        private static bool Before(SourceSpan a, SourceSpan b) =>
            a.Line < b.Line || (a.Line == b.Line && a.Col < b.Col);

        /// <summary>스키마가 선언한 순수 자리에 이 모드의 쓰기 함수가 있으면 오류다.</summary>
        private static void CheckPureSlots(SchemaSet schema, CallContext context)
        {
            void Pure(FunctionValue? fn, string what, SourceSpan span)
            {
                if (fn == null || fn.Mod != context.Mod) return;
                if (fn.IsEntry)
                {
                    context.Error(Codes.CmdWriteEffect, what + " uses '" + fn.Name + "', which is a " + fn.EntryKind, span,
                        "a command only runs when it is submitted; use a plain function (spec-gaps G118)",
                        new[] { fn.Definition!.Span });
                    return;
                }
                if (fn.Random)
                {
                    context.Error(Codes.CmdWriteEffect,
                        what + " uses '" + fn.Name + "', which draws random numbers; it must give the same answer every time", span,
                        "draw random numbers in a @command body instead (spec-gaps G178)", new[] { fn.RandomWitness!.Value });
                    return;
                }
                if (!fn.Writes) return;
                context.Error(Codes.CmdWriteEffect,
                    what + " uses '" + fn.Name + "', which changes the world; it can only read", span,
                    "change the world in a @command body instead (spec-gaps G117)", new[] { fn.WriteWitness!.Value });
            }

            void Members(string owner, IEnumerable<MemberDecl> members)
            {
                foreach (var m in members)
                {
                    string display = "'" + owner + "." + m.Name + "'";
                    if (m.Kind == MemberKind.Derived) Pure(m.Spec.Function, "derived " + display, m.Span);
                    else if (m.Kind == MemberKind.Field) Pure(m.Spec.Default as FunctionValue, "the default of " + display, m.Span);
                    else if (m.Spec.Type is FunctionTypeRef slot && slot.Kind == FunctionSlotKind.Cond)
                        Pure(m.Spec.Default as FunctionValue, "the default of " + display + " (" + slot.Display + ")", m.Span);
                }
            }

            foreach (var component in schema.Components) Members(component.Name, component.Members);
            foreach (var type in schema.Types) Members(type.Name, type.Consts);
        }

        private readonly struct Call
        {
            public Call(FunctionValue callee, SourceSpan span)
            {
                Callee = callee;
                Span = span;
            }

            public FunctionValue Callee { get; }
            public SourceSpan Span { get; }
        }

        private sealed class Body
        {
            public Body(FunctionValue function) => Function = function;

            public FunctionValue Function { get; }

            /// <summary>본문의 첫 속성 대입.</summary>
            public SourceSpan? FirstWrite { get; set; }

            /// <summary>문장으로 부른 같은 모드의 함수 (적힌 순서).</summary>
            public List<Call> StatementCalls { get; } = new List<Call>();

            /// <summary>식 안에서 부른 같은 모드의 함수.</summary>
            public List<Call> ExpressionCalls { get; } = new List<Call>();

            /// <summary>본문에서 커맨드 이름을 쓴 곳 (호출이든 값이든, G118).</summary>
            public List<Call> CommandUses { get; } = new List<Call>();

            /// <summary>본문의 첫 <c>random</c> 사용 (G178).</summary>
            public SourceSpan? FirstRandom { get; set; }

            /// <summary><c>with preconditions:</c> 안의 <c>random</c> 사용과 함수 호출 (G178).</summary>
            public List<SourceSpan> PreconditionRandom { get; } = new List<SourceSpan>();

            public List<Call> PreconditionCalls { get; } = new List<Call>();

            /// <summary>식 안에서 부른 쓰는 내장 (이름, 위치; G135).</summary>
            public List<KeyValuePair<string, SourceSpan>> BuiltinExpressionCalls { get; } =
                new List<KeyValuePair<string, SourceSpan>>();
        }

        private sealed class Walker
        {
            private readonly ModScope _scope;
            private readonly Body _body;

            /// <summary><c>with preconditions:</c> 블록 안을 걷는 중인가 (G178: 거기서는 난수를 쓸 수 없다).</summary>
            private bool _inPreconditions;

            public Walker(ModScope scope, Body body)
            {
                _scope = scope;
                _body = body;
            }

            /// <summary>지역 변수가 아니고 모드가 <c>def</c> 함수로 묶은 이름이면 그 함수 (G112의 덮어쓰기와 같은 규칙).</summary>
            /// 커맨드면 쓴 곳을 기록하고 null이다 (호출로도 세지 않는다, G118).
            private FunctionValue? Resolve(Expr callee, SortedSet<string> locals)
            {
                if (!(callee is NameExpr name) || locals.Contains(name.Name)) return null;
                if (!_scope.TryGet(name.Name, out var value, out _)) return null;
                if (!(value is FunctionValue fn) || fn.Definition == null) return null;
                if (!fn.IsEntry) return fn;
                _body.CommandUses.Add(new Call(fn, name.Span));
                return null;
            }

            /// <summary>덮어쓰지 않은 쓰는 내장의 이름이면 그 이름 (G112의 덮어쓰기와 같은 규칙).</summary>
            private string? WritingBuiltin(Expr callee, SortedSet<string> locals)
            {
                if (!(callee is NameExpr name) || locals.Contains(name.Name) || _scope.TryGet(name.Name, out _, out _))
                    return null;
                return PureBuiltins.IsWritingName(name.Name) ? name.Name : null;
            }

            /// <summary>문장 자리의 호출: 한 줄 전체이거나 이름 대입의 오른쪽 전체 (G117·G135).</summary>
            private void StatementCall(CallExpr call, SortedSet<string> locals)
            {
                var callee = Resolve(call.Callee, locals);
                if (callee != null) _body.StatementCalls.Add(new Call(callee, call.Span));
                else if (WritingBuiltin(call.Callee, locals) != null)
                {
                    if (_body.FirstWrite == null) _body.FirstWrite = call.Span;
                }
                else if (!(call.Callee is NameExpr)) Expr(call.Callee, locals);
                foreach (var arg in call.Arguments) Expr(arg.Value, locals);
            }

            public void Block(IReadOnlyList<Stmt> body, SortedSet<string> locals)
            {
                foreach (var stmt in body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es:
                            if (es.Expr is CallExpr call) StatementCall(call, locals);
                            else Expr(es.Expr, locals);
                            break;
                        case AssignStmt assign:
                            if (assign.Target is AttributeExpr target)
                            {
                                if (_body.FirstWrite == null) _body.FirstWrite = assign.Span;
                                Expr(target.Target, locals);
                            }
                            if (assign.Op == "=" && assign.Target is NameExpr && assign.Value is CallExpr assigned)
                                StatementCall(assigned, locals);
                            else
                                Expr(assign.Value, locals);
                            break;
                        case IfStmt ifs:
                            foreach (var branch in ifs.Branches)
                            {
                                Expr(branch.Condition, locals);
                                Block(branch.Body, locals);
                            }
                            if (ifs.OrElse != null) Block(ifs.OrElse, locals);
                            break;
                        case ForStmt loop:
                            Expr(loop.Iterable, locals);
                            Block(loop.Body, locals);
                            break;
                        case WithStmt with:
                            // 사전 조건 블록의 문장은 require 호출과 이름 대입뿐이다 (G111): 그 안의 호출은 모두 식 안이다
                            _inPreconditions = true;
                            foreach (var inner in with.Body)
                            {
                                if (inner is ExprStmt e) Expr(e.Expr, locals);
                                else if (inner is AssignStmt a) Expr(a.Value, locals);
                            }
                            _inPreconditions = false;
                            break;
                        case ReturnStmt ret:
                            if (ret.Value != null) Expr(ret.Value, locals);
                            break;
                    }
                }
            }

            public void Expr(Expr expr, SortedSet<string> locals)
            {
                switch (expr)
                {
                    case CallExpr call:
                        var callee = Resolve(call.Callee, locals);
                        var builtin = callee == null ? WritingBuiltin(call.Callee, locals) : null;
                        if (callee != null)
                        {
                            _body.ExpressionCalls.Add(new Call(callee, call.Span));
                            if (_inPreconditions) _body.PreconditionCalls.Add(new Call(callee, call.Span));
                        }
                        else if (builtin != null)
                            _body.BuiltinExpressionCalls.Add(new KeyValuePair<string, SourceSpan>(builtin, call.Span));
                        else if (!(call.Callee is NameExpr)) Expr(call.Callee, locals);
                        foreach (var arg in call.Arguments) Expr(arg.Value, locals);
                        break;
                    case NameExpr name:
                        Resolve(name, locals); // 값으로 쓴 커맨드 이름을 기록한다 (G118)
                        // 덮어쓰지 않은 random을 쓰면 난수 함수다 (호출이든 값이든, G178)
                        if (name.Name == PureBuiltins.RandomName && !locals.Contains(name.Name) && !_scope.TryGet(name.Name, out _, out _))
                        {
                            if (_body.FirstRandom == null) _body.FirstRandom = name.Span;
                            if (_inPreconditions) _body.PreconditionRandom.Add(name.Span);
                        }
                        break;
                    case BinaryExpr bin:
                        Expr(bin.Left, locals);
                        Expr(bin.Right, locals);
                        break;
                    case UnaryExpr unary:
                        Expr(unary.Operand, locals);
                        break;
                    case AttributeExpr attr:
                        Expr(attr.Target, locals);
                        break;
                    case IndexExpr index:
                        Expr(index.Target, locals);
                        foreach (var i in index.Indices) Expr(i, locals);
                        break;
                    case ListExpr list:
                        foreach (var item in list.Items) Expr(item, locals);
                        break;
                    case DictExpr dict:
                        foreach (var entry in dict.Entries)
                        {
                            Expr(entry.Key, locals);
                            Expr(entry.Value, locals);
                        }
                        break;
                    case ConditionalExpr cond:
                        Expr(cond.Condition, locals);
                        Expr(cond.WhenTrue, locals);
                        Expr(cond.WhenFalse, locals);
                        break;
                    case LambdaExpr lambda:
                        var inner = new SortedSet<string>(locals, System.StringComparer.Ordinal);
                        foreach (var p in lambda.Parameters) inner.Add(p.Name);
                        Expr(lambda.Body, inner);
                        break;
                }
            }
        }
    }
}
