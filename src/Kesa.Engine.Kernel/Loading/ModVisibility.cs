using System;
using System.Collections.Generic;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// 모드가 이름으로 쓸 수 있는 범위 = 자기 + 필수 의존의 전이적 닫힘 (spec S1, schema-design 4.3).
    /// <c>loadAfter</c>와 <c>optional</c>은 순서만 정하고 범위에 들지 않는다.
    /// </summary>
    public sealed class ModVisibility
    {
        private readonly Dictionary<string, SortedSet<string>> _visible =
            new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        /// <param name="mods">로드 순서대로의 활성 모드 (의존 대상이 앞에 온다)</param>
        public ModVisibility(IReadOnlyList<ModManifest> mods)
        {
            foreach (var m in mods)
            {
                var set = new SortedSet<string>(StringComparer.Ordinal) { m.Id };
                foreach (var dep in m.Dependencies)
                {
                    if (_visible.TryGetValue(dep.Id, out var depSet)) set.UnionWith(depSet);
                }
                _visible[m.Id] = set;
            }
        }

        /// <summary><paramref name="from"/> 모드가 <paramref name="owner"/> 모드의 이름을 쓸 수 있는가.</summary>
        public bool CanSee(string from, string owner) =>
            _visible.TryGetValue(from, out var set) && set.Contains(owner);
    }
}
