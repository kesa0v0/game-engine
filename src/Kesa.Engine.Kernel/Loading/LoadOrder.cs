using System;
using System.Collections.Generic;

namespace Kesa.Engine.Loading
{
    /// <summary>정렬 결과: 로드 순서, 비활성화된 모드, 모드 간 순서 관계.</summary>
    public sealed class LoadOrder
    {
        private readonly Dictionary<string, int> _index;
        private readonly bool[,] _reach;

        internal LoadOrder(IReadOnlyList<ModManifest> mods, IReadOnlyList<string> disabled, bool[,] reach)
        {
            Mods = mods;
            Disabled = disabled;
            _reach = reach;
            _index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < mods.Count; i++) _index[mods[i].Id] = i;
        }

        /// <summary>로드 순서대로 정렬된 활성 모드.</summary>
        public IReadOnlyList<ModManifest> Mods { get; }

        /// <summary>비활성화된 모드 id (서수 순). 이유는 진단에 있다.</summary>
        public IReadOnlyList<string> Disabled { get; }

        /// <summary>
        /// 두 모드 사이에 (전이적인) 순서 관계가 선언되어 있는가.
        /// dependencies, loadAfter, 존재하는 optional로 만든 관계이며, 정렬의 동률 규칙으로 생긴 순서는 포함하지 않는다.
        /// 패치의 <c>set</c> 충돌 규칙(spec S1)이 쓴다.
        /// </summary>
        public bool IsOrdered(string a, string b)
        {
            if (!_index.TryGetValue(a, out int i) || !_index.TryGetValue(b, out int j)) return false;
            return _reach[i, j] || _reach[j, i];
        }
    }
}
