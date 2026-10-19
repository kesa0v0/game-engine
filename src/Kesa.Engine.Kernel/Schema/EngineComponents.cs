using System;
using System.Collections.Generic;

namespace Kesa.Engine.Schema
{
    /// <summary>
    /// 엔진이 미리 선언한 컴포넌트 (spec S6, spec-gaps G239·G240). 커널이나 호스트 API가 그 이름을 알아야 할 때만 둔다. 모든 모드에
    /// 보이고, 모드가 같은 이름을 선언하면 그 모드의 오류다(<see cref="EngineSignals"/>와 같은 규칙, G154). 선언한 모드 자리는 빈 문자열이다.
    /// </summary>
    public static class EngineComponents
    {
        /// <summary>
        /// 앉을 후보 표시 (G239): 붙은 살아 있는 엔티티를 <c>World.SeatChoices()</c>가 준다. 필드가 없다. 처음부터 후보면 Def의
        /// <c>components</c>에 적고, 상태에 따라 바뀌면 규칙이 붙이고 뗀다.
        /// </summary>
        public const string Seatable = "Seatable";

        /// <summary>엔진 컴포넌트를 선언한 "모드" 자리.</summary>
        public const string Owner = EngineSignals.Owner;

        private static readonly string[] Names = { Seatable };

        public static IReadOnlyList<string> All => Names;

        public static bool IsEngine(string name) => Array.IndexOf(Names, name) >= 0;
    }
}
