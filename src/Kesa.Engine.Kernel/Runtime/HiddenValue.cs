using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 거른 읽기에서 보이는 칸의 Ref가 그 연결이 못 보는 엔티티를 가리킬 때의 값 (spec-gaps G198): "누군가 있지만 누군지 모른다".
    /// id가 없어 같은 대상인지도 알 수 없다. 커맨드 인자로는 낼 수 없다.
    /// </summary>
    public sealed class HiddenValue : Value
    {
        public static readonly HiddenValue Instance = new HiddenValue();

        private HiddenValue()
        {
        }

        public override string KindName => "hidden";
    }
}
