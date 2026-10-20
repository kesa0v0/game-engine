using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Runtime
{
    /// <summary><see cref="World.Load"/>의 결과. 오류가 하나라도 있으면 <see cref="World"/>는 null이다.</summary>
    public sealed class WorldLoadResult
    {
        internal WorldLoadResult(World? world, IReadOnlyList<Diagnostic> diagnostics)
        {
            World = world;
            Diagnostics = diagnostics;
        }

        public World? World { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
        public bool Succeeded => World != null;
    }
}
