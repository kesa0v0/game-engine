#nullable enable
using Kesa.Engine.Bridge.Local;

namespace Kesa.Engine.Unity
{
    /// <summary>같은 프로세스에서 도는 판 하나 (G353). 연결마다 <see cref="LocalHost.Open"/>으로 세션을 연다.</summary>
    public sealed class Match
    {
        internal Match(LoadedMods mods, LocalHost host)
        {
            Mods = mods;
            Host = host;
        }

        public LoadedMods Mods { get; }

        public LocalHost Host { get; }
    }
}
