using System.Collections.Generic;

namespace Kesa.Engine.Loading
{
    public sealed class DiscoveryResult
    {
        internal DiscoveryResult(IReadOnlyList<ModManifest> mods, IReadOnlyList<string> failedIds)
        {
            Mods = mods;
            FailedIds = failedIds;
        }

        /// <summary>매니페스트를 문제없이 읽은 모드 (폴더 이름 서수 순).</summary>
        public IReadOnlyList<ModManifest> Mods { get; }

        /// <summary>id는 읽혔지만 매니페스트에 오류가 있는 모드 (서수 순).</summary>
        public IReadOnlyList<string> FailedIds { get; }
    }
}
