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
}
