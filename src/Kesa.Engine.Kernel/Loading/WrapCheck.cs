using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// <c>@wrap</c>의 대상을 풀고 검사한다 (spec-gaps G311~G314). 모드의 효과 판정이 끝난 뒤 돈다: 대상(자기 모드나 필수 의존한 모드의
    /// 최상위 함수)의 효과는 이미 정해져 있다.
    /// <list type="bullet">
    /// <item>대상은 보이는 모드의 최상위 <c>def</c> 함수이고 입구(커맨드·핸들러·반응·UI 동작·다른 감싸기)가 아니다 (G313).</item>
    /// <item>첫 매개변수 뒤의 매개변수는 대상과 이름·개수가 같다 (G312).</item>
    /// <item>효과(쓰기·난수·UI)는 대상보다 넓을 수 없다 (G311). 통과하면 감싸는 함수의 효과를 대상의 것으로 맞춘다: 안쪽 함수 호출이
    /// 대상의 효과를 낸다.</item>
    /// <item>첫 매개변수는 부르기만 한다(<c>original(...)</c>, G314). 대상이 쓰기 함수면 그 호출은 문장이다(G117).</item>
    /// </list>
    /// </summary>
    internal static class WrapCheck
    {
        private const string Hint = "write @wrap(mod.function) above def f(original, ...same parameters...): ... (spec-gaps G312)";

        public static void Resolve(IReadOnlyList<WrapDecl> wraps, ModNames mods, HostNames hosts, CallContext context)
        {
            foreach (var w in wraps)
            {
                var target = Target(w, mods, hosts, context);
                if (target == null || !Shape(w, target, context)) continue;
                if (target.IsCommand)
                {
                    if (!CommandWrap(w, target, context)) continue;
                    w.Function.WrapsCommand = true;
                    w.Target = target;
                    continue;
                }
                if (!Preconditions(w, target, context) || !Effects(w, target, context)) continue;
                if (!InnerUses(w, target, context)) continue;
                var fn = w.Function;
                fn.WriteWitness = target.WriteWitness;
                fn.RandomWitness = target.RandomWitness;
                fn.UiTouchWitness = target.UiTouchWitness;
                fn.UiWriteWitness = target.UiWriteWitness;
                w.Target = target;
            }
        }

        private static FunctionValue? Target(WrapDecl w, ModNames mods, HostNames hosts, CallContext context)
        {
            string mod = w.TargetMod ?? w.Mod;
            string shown = (w.TargetMod == null ? "" : w.TargetMod + ".") + w.TargetName;
            if (!mods.IsRoot(mod))
            {
                if (mods.IsHidden(mod))
                    context.Error(Codes.SchNotVisible, "the names of mod '" + mod + "' can only be used by mods that depend on it",
                        w.TargetSpan, "add \"" + mod + "\" to dependencies in mod.py (spec-gaps G309)");
                else
                    context.Error(Codes.EvlUnknownName, "@wrap: there is no mod '" + mod + "'", w.TargetSpan,
                        Suggest.Hint(mod, new List<string>(mods.Roots)) ?? Hint);
                return null;
            }
            if (!mods.TryGet(mod, w.TargetName, out var value))
            {
                if (hosts.IsFunction(mod + "." + w.TargetName))
                {
                    context.Error(Codes.CmdBadDecorator, "@wrap: '" + shown + "' is a host function, so it cannot be wrapped",
                        w.TargetSpan, "only script functions can be wrapped (spec-gaps G313)");
                    return null;
                }
                context.Error(Codes.EvlUnknownName, "mod '" + mod + "' has no top-level name '" + w.TargetName + "'", w.TargetSpan,
                    Suggest.Hint(w.TargetName, mods.Names(mod)) ?? Hint);
                return null;
            }
            if (!(value is FunctionValue fn) || fn.Definition == null || fn.Name != w.TargetName)
            {
                context.Error(Codes.CmdBadDecorator, "@wrap needs a function defined with def, but '" + shown + "' is " +
                                                     (value is FunctionValue ? "a lambda" : "a " + value.KindName), w.TargetSpan,
                    "only top-level functions can be wrapped (spec-gaps G313)");
                return null;
            }
            if (fn.IsEntry && !fn.IsCommand)
            {
                context.Error(Codes.CmdBadDecorator, "@wrap: '" + shown + "' is a " + fn.EntryKind + ", so it cannot be wrapped",
                    w.TargetSpan, "wrap a plain function or a @command it calls instead (spec-gaps G313, G316)");
                return null;
            }
            return fn;
        }

        /// <summary>사전 조건 블록은 커맨드를 감쌀 때만 연다 (G316): 함수에는 사전 조건이 없다.</summary>
        private static bool Preconditions(WrapDecl w, FunctionValue target, CallContext context)
        {
            var body = w.Function.Definition!.Body;
            if (body.Count == 0 || !(body[0] is WithStmt with)) return true;
            context.Error(Codes.CmdPreconditions,
                "'" + target.Name + "' is a plain function, so a @wrap of it cannot open 'with preconditions:'", with.Span,
                "only a @wrap of a @command adds preconditions; check the condition in the body instead (spec-gaps G316)");
            return false;
        }

        /// <summary>
        /// 커맨드 감싸기 (G316~G321). 효과는 커맨드 본문의 규칙이라 대상과 견주지 않지만 UI 상태는 못 쓴다(G320).
        /// <c>original(...)</c>은 문장으로, 감싸는 함수의 매개변수를 그대로 넘긴다(G321).
        /// </summary>
        private static bool CommandWrap(WrapDecl w, FunctionValue target, CallContext context)
        {
            var fn = w.Function;
            bool ok = true;
            if (fn.TouchesUi)
            {
                context.Error(Codes.CmdUiEffect, "the @wrap '" + fn.Name + "' of the command '" + target.Name +
                                                 "' uses UI state (ui.… or submit), so it cannot run as game code",
                    fn.Definition!.Span, "a command wrapper runs like the command's body (spec-gaps G320)", new[] { fn.UiTouchWitness!.Value });
                ok = false;
            }
            var walker = new InnerWalker(fn.Parameters[0].Name);
            walker.Block(fn.Definition!.Body);
            foreach (var span in walker.Misused)
            {
                context.Error(Codes.CmdBadDecorator, "'" + walker.Name + "' is the inner command of @wrap, so it can only be called", span,
                    "call it as " + walker.Name + "(...); it cannot be stored, passed, returned or used in a lambda (spec-gaps G314)");
                ok = false;
            }
            foreach (var span in walker.ExpressionCalls)
            {
                context.Error(Codes.CmdWriteEffect, "'" + target.Name + "' is a command, so the inner command can only be called as a statement",
                    span, "call it on its own line (spec-gaps G321)");
                ok = false;
            }
            var names = new List<string>();
            foreach (var p in target.Parameters) names.Add(p.Name);
            string shape = walker.Name + "(" + string.Join(", ", names) + ")";
            foreach (var call in walker.Calls)
            {
                if (PassesParameters(call, names)) continue;
                context.Error(Codes.CmdBadDecorator, "the inner command must get the submitted arguments as they are: " + shape, call.Span,
                    "the command's preconditions were checked with those arguments; to act on other values, call a plain " +
                    "function (spec-gaps G321)");
                ok = false;
            }
            return ok;
        }

        /// <summary>호출이 매개변수 이름을 그 순서 그대로 넘기나: 위치 인자 <c>p</c>나 키워드 <c>p=p</c>.</summary>
        private static bool PassesParameters(CallExpr call, List<string> names)
        {
            if (call.Arguments.Count != names.Count) return false;
            for (int i = 0; i < names.Count; i++)
            {
                var arg = call.Arguments[i];
                if (!(arg.Value is NameExpr n) || n.Name != names[i]) return false;
                if (arg.Name != null && arg.Name != names[i]) return false;
            }
            return true;
        }

        private static bool Shape(WrapDecl w, FunctionValue target, CallContext context)
        {
            var mine = w.Function.Parameters;
            var theirs = target.Parameters;
            bool same = mine.Count == theirs.Count + 1;
            for (int i = 0; same && i < theirs.Count; i++) same = mine[i + 1].Name == theirs[i].Name;
            if (same) return true;
            var names = new List<string> { mine[0].Name };
            foreach (var p in theirs) names.Add(p.Name);
            context.Error(Codes.CmdBadDecorator,
                "after the inner function, a @wrap function takes the same parameters as '" + target.Name + "'",
                w.Function.Definition!.Span, "write def " + w.Function.Name + "(" + string.Join(", ", names) + "): ... (spec-gaps G312)",
                new[] { target.Definition!.Span });
            return false;
        }

        private static bool Effects(WrapDecl w, FunctionValue target, CallContext context)
        {
            var fn = w.Function;
            string? wider = fn.Writes && !target.Writes ? "changes the world" :
                fn.Random && !target.Random ? "draws random numbers" :
                fn.WritesUi && !target.WritesUi ? "changes UI state" :
                fn.TouchesUi && !target.TouchesUi ? "uses ui.…" : null;
            if (wider == null) return true;
            var witness = fn.Writes && !target.Writes ? fn.WriteWitness : fn.Random && !target.Random ? fn.RandomWitness :
                fn.WritesUi && !target.WritesUi ? fn.UiWriteWitness : fn.UiTouchWitness;
            context.Error(Codes.CmdWriteEffect,
                "'" + fn.Name + "' " + wider + " but '" + target.Name + "' does not, so it cannot wrap it", w.Span,
                "a wrapper can do no more than the function it wraps, because it runs everywhere that function is called (spec-gaps G311)",
                witness == null ? null : new[] { witness.Value });
            return false;
        }

        /// <summary>첫 매개변수를 부르기 외에 쓰는 곳이 없나 (G314). 대상이 쓰기 함수면 부르는 곳은 문장이어야 한다.</summary>
        private static bool InnerUses(WrapDecl w, FunctionValue target, CallContext context)
        {
            var walker = new InnerWalker(w.Function.Parameters[0].Name);
            walker.Block(w.Function.Definition!.Body);
            foreach (var span in walker.Misused)
            {
                context.Error(Codes.CmdBadDecorator, "'" + walker.Name + "' is the inner function of @wrap, so it can only be called", span,
                    "call it as " + walker.Name + "(...); it cannot be stored, passed, returned or used in a lambda (spec-gaps G314)");
            }
            if (target.Writes)
            {
                foreach (var span in walker.ExpressionCalls)
                    context.Error(Codes.CmdWriteEffect,
                        "'" + target.Name + "' changes the world, so the inner function can only be called as a statement", span,
                        "call it on its own line, or as 'x = " + walker.Name + "(...)' (spec-gaps G117, G311)");
            }
            return walker.Misused.Count == 0 && (!target.Writes || walker.ExpressionCalls.Count == 0);
        }

        private sealed class InnerWalker
        {
            public InnerWalker(string name) => Name = name;

            public string Name { get; }
            public List<SourceSpan> Misused { get; } = new List<SourceSpan>();
            public List<SourceSpan> ExpressionCalls { get; } = new List<SourceSpan>();

            /// <summary>문장으로 부른 <c>original(...)</c>들.</summary>
            public List<CallExpr> Calls { get; } = new List<CallExpr>();

            private int _lambdas;

            public void Block(IReadOnlyList<Stmt> body)
            {
                foreach (var stmt in body) Stmt(stmt);
            }

            private void Stmt(Stmt stmt)
            {
                switch (stmt)
                {
                    case ExprStmt { Expr: CallExpr call } when IsInner(call.Callee):
                        Calls.Add(call);
                        Arguments(call);
                        return;
                    case AssignStmt { Op: "=", Target: NameExpr t, Value: CallExpr call } when IsInner(call.Callee):
                        Target(t);
                        Calls.Add(call);
                        Arguments(call);
                        return;
                    case ExprStmt s:
                        Expr(s.Expr);
                        return;
                    case AssignStmt s:
                        Target(s.Target);
                        Expr(s.Value);
                        return;
                    case WithStmt s:
                        Expr(s.Context);
                        Block(s.Body);
                        return;
                    case ReturnStmt s:
                        if (s.Value != null) Expr(s.Value);
                        return;
                    case ForStmt s:
                        Target(s.Target);
                        Expr(s.Iterable);
                        Block(s.Body);
                        return;
                    case WhileStmt s:
                        Expr(s.Condition);
                        Block(s.Body);
                        return;
                    case IfStmt s:
                        foreach (var branch in s.Branches)
                        {
                            Expr(branch.Condition);
                            Block(branch.Body);
                        }
                        if (s.OrElse != null) Block(s.OrElse);
                        return;
                }
            }

            private bool IsInner(Expr callee) => callee is NameExpr n && n.Name == Name && _lambdas == 0;

            private void Target(Expr target)
            {
                if (target is NameExpr n && n.Name == Name) Misused.Add(n.Span);
                else if (!(target is NameExpr)) Expr(target);
            }

            private void Arguments(CallExpr call)
            {
                foreach (var arg in call.Arguments) Expr(arg.Value);
            }

            private void Expr(Expr expr)
            {
                switch (expr)
                {
                    case NameExpr n:
                        if (n.Name == Name) Misused.Add(n.Span);
                        return;
                    case CallExpr e:
                        if (IsInner(e.Callee)) ExpressionCalls.Add(e.Span);
                        else Expr(e.Callee);
                        Arguments(e);
                        return;
                    case LambdaExpr e:
                        _lambdas++;
                        foreach (var p in e.Parameters)
                            if (p.Default != null) Expr(p.Default);
                        Expr(e.Body);
                        _lambdas--;
                        return;
                    case ListExpr e:
                        foreach (var item in e.Items) Expr(item);
                        return;
                    case DictExpr e:
                        foreach (var entry in e.Entries)
                        {
                            Expr(entry.Key);
                            Expr(entry.Value);
                        }
                        return;
                    case AttributeExpr e:
                        Expr(e.Target);
                        return;
                    case IndexExpr e:
                        Expr(e.Target);
                        foreach (var index in e.Indices) Expr(index);
                        return;
                    case UnaryExpr e:
                        Expr(e.Operand);
                        return;
                    case BinaryExpr e:
                        Expr(e.Left);
                        Expr(e.Right);
                        return;
                    case ConditionalExpr e:
                        Expr(e.Condition);
                        Expr(e.WhenTrue);
                        Expr(e.WhenFalse);
                        return;
                    case ComprehensionExpr e:
                        foreach (var (part, _) in e.Parts()) Expr(part);
                        return;
                }
            }
        }
    }
}
