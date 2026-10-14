using System;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 함수의 지역 변수 이름 (파이썬 규칙, spec-gaps G104·G108): 매개변수와, 본문 어디서든 대입하거나 <c>for</c>로 묶는 이름.
    /// 로드 시점 이름 검사(<see cref="FunctionNameChecker"/>)와 실행기(<see cref="PureEvaluator"/>)가 같은 규칙을 쓴다.
    /// </summary>
    public static class FunctionLocals
    {
        public static SortedSet<string> Of(FunctionDef def)
        {
            var locals = Parameters(def.Parameters);
            Collect(def.Body, locals);
            return locals;
        }

        public static SortedSet<string> Parameters(IReadOnlyList<Parameter> parameters)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var p in parameters) names.Add(p.Name);
            return names;
        }

        private static void Collect(IReadOnlyList<Stmt> body, SortedSet<string> locals)
        {
            foreach (var stmt in body)
            {
                switch (stmt)
                {
                    case AssignStmt assign when assign.Target is NameExpr name:
                        locals.Add(name.Name);
                        break;
                    case ForStmt loop:
                        locals.Add(loop.Target.Name);
                        Collect(loop.Body, locals);
                        break;
                    case IfStmt ifs:
                        foreach (var branch in ifs.Branches) Collect(branch.Body, locals);
                        if (ifs.OrElse != null) Collect(ifs.OrElse, locals);
                        break;
                }
            }
        }
    }
}
