using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 비밀이 Flow 지역 변수에 남는 꼴을 찾는다 (spec-gaps G465, 경고). 봇의 시험 판은 멈춘 Flow를 지역 변수째 복사하므로, <c>ask</c>·<c>wait</c>로
    /// 멈출 수 있는 함수가 난수나 가려질 수 있는 컴포넌트에서 만든 값을 멈춤 뒤에도 쓰면 봇이 그 값을 안다. 도우미 함수를 거친 값은 보지 않는다.
    /// </summary>
    internal static class SecretLocals
    {
        /// <param name="hidden">어느 <c>@view_rule</c>의 대상인 컴포넌트 이름들. 비었으면 숨긴 정보가 없는 게임이라 보지 않는다.</param>
        public static void Check(ModFunctions functions, ISet<string> hidden, CallContext context)
        {
            if (hidden.Count == 0) return;
            foreach (var function in functions.Functions)
            {
                var def = function.Definition;
                if (def == null || !Suspends(def.Body)) continue;
                new Walker(hidden, context).Block(def.Body);
            }
        }

        private static bool Suspends(IReadOnlyList<Stmt> body)
        {
            foreach (var stmt in body)
            {
                switch (stmt)
                {
                    case ExprStmt s when Suspending(s.Expr): return true;
                    case AssignStmt s when Suspending(s.Value): return true;
                    case IfStmt s:
                        foreach (var branch in s.Branches)
                            if (Suspends(branch.Body)) return true;
                        if (s.OrElse != null && Suspends(s.OrElse)) return true;
                        break;
                    case ForStmt s when Suspends(s.Body): return true;
                    case WhileStmt s when Suspends(s.Body): return true;
                    case WithStmt s when Suspends(s.Body): return true;
                }
            }
            return false;
        }

        /// <summary>문장 자리의 <c>ask(...)</c>·<c>wait(...)</c> (멈춤은 문장에서만 일어난다, G136).</summary>
        private static bool Suspending(Expr expr) =>
            expr is CallExpr call && call.Callee is NameExpr name &&
            (name.Name == PureBuiltins.AskName || name.Name == PureBuiltins.WaitName);

        private sealed class Walker
        {
            private readonly ISet<string> _hidden;
            private readonly CallContext _context;
            /// <summary>비밀인 지역 변수 → 대입할 때까지 지난 멈춤 수. 읽을 때 그보다 많이 멈췄으면 멈춤을 넘어 남은 값이다.</summary>
            private readonly SortedDictionary<string, int> _secret = new SortedDictionary<string, int>(StringComparer.Ordinal);

            private readonly SortedSet<string> _warned = new SortedSet<string>(StringComparer.Ordinal);

            /// <summary>지금까지 지난 멈춤 수 (가지는 많은 쪽).</summary>
            private int _pauses;

            public Walker(ISet<string> hidden, CallContext context)
            {
                _hidden = hidden;
                _context = context;
            }

            public void Block(IReadOnlyList<Stmt> body)
            {
                foreach (var stmt in body) Statement(stmt);
            }

            private void Statement(Stmt stmt)
            {
                switch (stmt)
                {
                    case ExprStmt s:
                        Reads(s.Expr);
                        if (Suspending(s.Expr)) _pauses++;
                        break;
                    case AssignStmt s:
                        Reads(s.Value);
                        if (Suspending(s.Value)) _pauses++; // 답은 멈춘 뒤에 들어온다
                        if (s.Target is NameExpr name)
                        {
                            if (Secret(s.Value)) _secret[name.Name] = _pauses;
                            else if (s.Op == "=") _secret.Remove(name.Name);
                        }
                        else Reads(s.Target);
                        break;
                    case IfStmt s:
                    {
                        int before = _pauses, most = _pauses;
                        foreach (var branch in s.Branches)
                        {
                            Reads(branch.Condition);
                            _pauses = before;
                            Block(branch.Body);
                            most = Math.Max(most, _pauses);
                        }
                        _pauses = before;
                        if (s.OrElse != null) Block(s.OrElse);
                        _pauses = Math.Max(most, _pauses);
                        break;
                    }
                    case ForStmt s:
                        Reads(s.Iterable);
                        if (Secret(s.Iterable)) _secret[s.Target.Name] = _pauses;
                        Loop(s.Body, null);
                        break;
                    case WhileStmt s:
                        Loop(s.Body, s.Condition);
                        break;
                    case WithStmt s:
                        Block(s.Body);
                        break;
                    case ReturnStmt s:
                        if (s.Value != null) Reads(s.Value);
                        break;
                }
            }

            /// <summary>반복의 몸체: 멈춤이 있으면 두 바퀴를 걸어, 한 바퀴에서 남긴 값을 다음 바퀴가 읽는 것도 본다.</summary>
            private void Loop(IReadOnlyList<Stmt> body, Expr? condition)
            {
                int rounds = Suspends(body) ? 2 : 1;
                for (int i = 0; i < rounds; i++)
                {
                    if (condition != null) Reads(condition);
                    Block(body);
                }
            }

            /// <summary>난수를 뽑거나 가려질 수 있는 컴포넌트를 읽거나 그런 지역 변수를 쓰는 식.</summary>
            private bool Secret(Expr expr)
            {
                switch (expr)
                {
                    case NameExpr e: return _secret.ContainsKey(e.Name);
                    case CallExpr e:
                        if (e.Callee is AttributeExpr a && a.Target is NameExpr root && root.Name == PureBuiltins.RandomName) return true;
                        if (Secret(e.Callee)) return true;
                        foreach (var arg in e.Arguments)
                            if (Secret(arg.Value)) return true;
                        return false;
                    case AttributeExpr e: return _hidden.Contains(e.Name) || Secret(e.Target);
                    case ListExpr e:
                        foreach (var item in e.Items)
                            if (Secret(item)) return true;
                        return false;
                    case DictExpr e:
                        foreach (var entry in e.Entries)
                            if (Secret(entry.Key) || Secret(entry.Value)) return true;
                        return false;
                    case IndexExpr e:
                        if (Secret(e.Target)) return true;
                        foreach (var index in e.Indices)
                            if (Secret(index)) return true;
                        return false;
                    case UnaryExpr e: return Secret(e.Operand);
                    case BinaryExpr e: return Secret(e.Left) || Secret(e.Right);
                    case ConditionalExpr e: return Secret(e.Condition) || Secret(e.WhenTrue) || Secret(e.WhenFalse);
                    case ComprehensionExpr e:
                        foreach (var (part, _) in e.Parts())
                            if (Secret(part)) return true;
                        return false;
                }
                return false;
            }

            /// <summary>멈춤 뒤에 비밀 지역 변수를 읽으면 변수마다 한 번 알린다.</summary>
            private void Reads(Expr expr)
            {
                switch (expr)
                {
                    case NameExpr e:
                        if (_secret.TryGetValue(e.Name, out int at) && _pauses > at && _warned.Add(e.Name))
                            _context.Diagnostics.Warning(Codes.CmdSecretLocal,
                                "'" + e.Name + "' was made from random numbers or a hidden component and is used after the flow pauses; " +
                                "a bot imagining a move copies the paused flow and would see it",
                                e.Span, _context.Mod,
                                "keep what must stay secret in a component and hide it with @view_rule (spec-gaps G465)");
                        return;
                    case CallExpr e:
                        Reads(e.Callee);
                        foreach (var arg in e.Arguments) Reads(arg.Value);
                        return;
                    case AttributeExpr e: Reads(e.Target); return;
                    case ListExpr e:
                        foreach (var item in e.Items) Reads(item);
                        return;
                    case DictExpr e:
                        foreach (var entry in e.Entries)
                        {
                            Reads(entry.Key);
                            Reads(entry.Value);
                        }
                        return;
                    case IndexExpr e:
                        Reads(e.Target);
                        foreach (var index in e.Indices) Reads(index);
                        return;
                    case UnaryExpr e: Reads(e.Operand); return;
                    case BinaryExpr e:
                        Reads(e.Left);
                        Reads(e.Right);
                        return;
                    case ConditionalExpr e:
                        Reads(e.Condition);
                        Reads(e.WhenTrue);
                        Reads(e.WhenFalse);
                        return;
                    case ComprehensionExpr e:
                        foreach (var (part, _) in e.Parts()) Reads(part);
                        return;
                }
            }
        }
    }
}
