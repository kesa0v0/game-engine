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

            foreach (var body in bodies)
            foreach (var call in body.ExpressionCalls)
            {
                if (!call.Callee.Writes) continue;
                context.Error(Codes.CmdWriteEffect,
                    "'" + call.Callee.Name + "' changes the world, so it can only be called as a statement", call.Span,
                    "call it on its own line in a @command body or in another function that changes the world (spec-gaps G117)",
                    new[] { call.Callee.WriteWitness!.Value });
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
                if (fn == null || fn.Mod != context.Mod || !fn.Writes) return;
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
        }

        private sealed class Walker
        {
            private readonly ModScope _scope;
            private readonly Body _body;

            public Walker(ModScope scope, Body body)
            {
                _scope = scope;
                _body = body;
            }

            /// <summary>지역 변수가 아니고 모드가 <c>def</c> 함수로 묶은 이름이면 그 함수 (G112의 덮어쓰기와 같은 규칙).</summary>
            private FunctionValue? Resolve(Expr callee, SortedSet<string> locals)
            {
                if (!(callee is NameExpr name) || locals.Contains(name.Name)) return null;
                if (!_scope.TryGet(name.Name, out var value, out _)) return null;
                return value is FunctionValue fn && fn.Definition != null ? fn : null;
            }

            public void Block(IReadOnlyList<Stmt> body, SortedSet<string> locals)
            {
                foreach (var stmt in body)
                {
                    switch (stmt)
                    {
                        case ExprStmt es:
                            if (es.Expr is CallExpr call)
                            {
                                var callee = Resolve(call.Callee, locals);
                                if (callee != null) _body.StatementCalls.Add(new Call(callee, call.Span));
                                else Expr(call.Callee, locals);
                                foreach (var arg in call.Arguments) Expr(arg.Value, locals);
                            }
                            else Expr(es.Expr, locals);
                            break;
                        case AssignStmt assign:
                            if (assign.Target is AttributeExpr target)
                            {
                                if (_body.FirstWrite == null) _body.FirstWrite = assign.Span;
                                Expr(target.Target, locals);
                            }
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
                            foreach (var inner in with.Body)
                            {
                                if (inner is ExprStmt e) Expr(e.Expr, locals);
                                else if (inner is AssignStmt a) Expr(a.Value, locals);
                            }
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
                        if (callee != null) _body.ExpressionCalls.Add(new Call(callee, call.Span));
                        else Expr(call.Callee, locals);
                        foreach (var arg in call.Arguments) Expr(arg.Value, locals);
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
