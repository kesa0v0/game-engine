#nullable enable
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Ui;

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

        /// <summary>한 연결의 판 위 그림 (G412). 프레임마다 <see cref="LookBoard.RefreshAsync"/>를 부른다.</summary>
        public LookBoard Board(IHostSession session, ILookRenderer renderer) => new LookBoard(session, new LookStage(Mods.Looks, renderer, Mods.Inputs, Mods.Format));
    }
}
