using System;
using System.Collections.Generic;

namespace Kesa.Engine.Schema
{
    /// <summary>
    /// 엔진이 미리 선언하고 엔진만 내는 신호 (spec X2, spec-gaps G154·G156). 모든 모드에 보이고, 스크립트는 그 값을 만들 수 없다.
    /// 선언한 모드 자리는 빈 문자열이다.
    /// </summary>
    public static class EngineSignals
    {
        /// <summary><c>advance</c>가 틱마다 낸다. 값이 없고 지금 틱은 <c>now()</c>로 읽는다.</summary>
        public const string Tick = "Tick";

        /// <summary>엔진 신호를 선언한 "모드" 자리.</summary>
        public const string Owner = "";

        private static readonly string[] Names = { Tick };

        public static IReadOnlyList<string> All => Names;

        public static bool IsEngine(string name) => Array.IndexOf(Names, name) >= 0;

        /// <summary>엔진 신호의 값을 만들려 할 때의 안내.</summary>
        public static string Why(string name) =>
            name + " is sent by the engine when time advances, so scripts cannot make it; the host calls World.Advance (spec-gaps G156)";
    }
}
