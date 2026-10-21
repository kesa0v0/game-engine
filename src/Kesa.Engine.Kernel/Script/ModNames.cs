using System;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 한 모드의 함수 본문에서 <c>모드id.이름</c>으로 쓸 수 있는 모드들의 최상위 이름 (spec-gaps G309). 그 모드 자신과 필수 의존한
    /// 모드(전이)만 보인다(G218의 호스트 함수와 같은 범위). 로드 시점 이름 검사(<see cref="FunctionNameChecker"/>)와 효과 판정
    /// (<see cref="FunctionEffects"/>)이 같은 것을 본다. 의존 대상은 로드 순서상 먼저 끝나 있어 그 함수의 효과가 이미 정해져 있다.
    /// </summary>
    public sealed class ModNames
    {
        public static readonly ModNames None = new ModNames(new KeyValuePair<string, ModScope>[0], new string[0]);

        private readonly SortedDictionary<string, ModScope> _visible = new SortedDictionary<string, ModScope>(StringComparer.Ordinal);
        private readonly SortedSet<string> _hidden = new SortedSet<string>(StringComparer.Ordinal);

        /// <param name="visible">보이는 모드 id → 그 모드의 최상위 이름</param>
        /// <param name="hidden">활성이지만 이 모드가 의존하지 않아 보이지 않는 모드 id</param>
        public ModNames(IEnumerable<KeyValuePair<string, ModScope>> visible, IEnumerable<string> hidden)
        {
            foreach (var pair in visible) _visible[pair.Key] = pair.Value;
            _hidden.UnionWith(hidden);
        }

        /// <summary>이 모드에 보이는 모드 id인가.</summary>
        public bool IsRoot(string name) => _visible.ContainsKey(name);

        public IEnumerable<string> Roots => _visible.Keys;

        /// <summary>활성이지만 이 모드가 의존하지 않아 보이지 않는 모드 id인가.</summary>
        public bool IsHidden(string name) => _hidden.Contains(name);

        /// <summary>보이는 모드의 최상위 이름 하나.</summary>
        public bool TryGet(string mod, string name, out Value value)
        {
            value = NoneValue.Instance;
            return _visible.TryGetValue(mod, out var scope) && scope.TryGet(name, out value, out _);
        }

        /// <summary>보이는 모드의 최상위 이름들 ("혹시 …?" 후보).</summary>
        public IReadOnlyList<string> Names(string mod) =>
            _visible.TryGetValue(mod, out var scope) ? scope.Names : (IReadOnlyList<string>)Array.Empty<string>();
    }
}
