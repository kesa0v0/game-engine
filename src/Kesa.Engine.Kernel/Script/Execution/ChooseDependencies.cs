using System;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>사전 조건 블록의 <c>choose</c> 하나와 그 후보 식이 기대는 것 (spec-gaps G184·G188).</summary>
    public sealed class ChooseInfo
    {
        public ChooseInfo(string parameter, bool where, CallExpr call, SortedSet<string> needs, SortedSet<int> statements)
        {
            Parameter = parameter;
            Where = where;
            Call = call;
            Needs = needs;
            Statements = statements;
        }

        public string Parameter { get; }

        /// <summary><c>where=</c>면 참, <c>from_=</c>면 거짓.</summary>
        public bool Where { get; }

        public CallExpr Call { get; }

        /// <summary>후보 식이 (블록 변수를 거쳐) 읽는 커맨드 매개변수.</summary>
        public SortedSet<string> Needs { get; }

        /// <summary>후보 식을 계산하기 전에 실행할 블록 대입의 번호 (블록 안 순서).</summary>
        public SortedSet<int> Statements { get; }
    }

    /// <summary>
    /// <c>choose</c>의 후보 식이 읽는 매개변수를 정적으로 찾는다 (G184·G188). 식이 읽는 이름 중 블록 변수는 그 변수의 마지막 대입을
    /// 따라가고, 람다의 매개변수는 뺀다. 조건식의 양쪽처럼 실제로는 안 읽힐 수 있는 이름도 읽는 것으로 친다.
    /// </summary>
    public static class ChooseDependencies
    {
        public static List<ChooseInfo> Of(WithStmt with, IReadOnlyList<Parameter> parameters)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var p in parameters) names.Add(p.Name);
            var locals = new SortedDictionary<string, Reads>(StringComparer.Ordinal);
            var result = new List<ChooseInfo>();
            for (int i = 0; i < with.Body.Count; i++)
            {
                switch (with.Body[i])
                {
                    case AssignStmt assign when assign.Op == "=" && assign.Target is NameExpr target:
                        var reads = Read(assign.Value, names, locals);
                        reads.Statements.Add(i);
                        locals[target.Name] = reads;
                        break;

                    case ExprStmt es when es.Expr is CallExpr call && call.Callee is NameExpr callee &&
                                          callee.Name == FunctionChecker.ChooseName && call.Arguments.Count == 2 &&
                                          call.Arguments[0].Name == null && call.Arguments[0].Value is NameExpr chosen &&
                                          (call.Arguments[1].Name == FunctionChecker.WhereName ||
                                           call.Arguments[1].Name == FunctionChecker.FromName):
                        var option = Read(call.Arguments[1].Value, names, locals);
                        result.Add(new ChooseInfo(chosen.Name, call.Arguments[1].Name == FunctionChecker.WhereName, call,
                            option.Parameters, option.Statements));
                        break;
                }
            }
            return result;
        }

        private sealed class Reads
        {
            public SortedSet<string> Parameters { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public SortedSet<int> Statements { get; } = new SortedSet<int>();
        }

        private static Reads Read(Expr expr, SortedSet<string> parameters, SortedDictionary<string, Reads> locals)
        {
            var reads = new Reads();
            Walk(expr, new SortedSet<string>(StringComparer.Ordinal), parameters, locals, reads);
            return reads;
        }

        private static void Walk(Expr expr, SortedSet<string> bound, SortedSet<string> parameters,
            SortedDictionary<string, Reads> locals, Reads reads)
        {
            switch (expr)
            {
                case NameExpr name:
                    if (bound.Contains(name.Name)) return;
                    if (locals.TryGetValue(name.Name, out var local))
                    {
                        reads.Parameters.UnionWith(local.Parameters);
                        reads.Statements.UnionWith(local.Statements);
                    }
                    else if (parameters.Contains(name.Name)) reads.Parameters.Add(name.Name);
                    return;
                case BinaryExpr bin:
                    Walk(bin.Left, bound, parameters, locals, reads);
                    Walk(bin.Right, bound, parameters, locals, reads);
                    return;
                case UnaryExpr unary:
                    Walk(unary.Operand, bound, parameters, locals, reads);
                    return;
                case CallExpr call:
                    Walk(call.Callee, bound, parameters, locals, reads);
                    foreach (var arg in call.Arguments) Walk(arg.Value, bound, parameters, locals, reads);
                    return;
                case AttributeExpr attr:
                    Walk(attr.Target, bound, parameters, locals, reads);
                    return;
                case IndexExpr index:
                    Walk(index.Target, bound, parameters, locals, reads);
                    foreach (var i in index.Indices) Walk(i, bound, parameters, locals, reads);
                    return;
                case ListExpr list:
                    foreach (var item in list.Items) Walk(item, bound, parameters, locals, reads);
                    return;
                case DictExpr dict:
                    foreach (var entry in dict.Entries)
                    {
                        Walk(entry.Key, bound, parameters, locals, reads);
                        Walk(entry.Value, bound, parameters, locals, reads);
                    }
                    return;
                case ConditionalExpr cond:
                    Walk(cond.Condition, bound, parameters, locals, reads);
                    Walk(cond.WhenTrue, bound, parameters, locals, reads);
                    Walk(cond.WhenFalse, bound, parameters, locals, reads);
                    return;
                case LambdaExpr lambda:
                    var inner = new SortedSet<string>(bound, StringComparer.Ordinal);
                    foreach (var p in lambda.Parameters) inner.Add(p.Name);
                    Walk(lambda.Body, inner, parameters, locals, reads);
                    return;
            }
        }
    }
}
