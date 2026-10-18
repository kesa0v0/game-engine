using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>진단을 이미 보고하고 현재 문장을 중단할 때 던진다.</summary>
    public sealed class ScriptError : Exception
    {
    }
}
