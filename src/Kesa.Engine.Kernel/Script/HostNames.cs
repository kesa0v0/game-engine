using System;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 한 모드의 함수 본문에서 보이는 호스트 함수 이름 (spec S7, spec-gaps G218). 소유 모드 자신과 그 모드에 의존한 모드에게만 보인다.
    /// 로드 시점 이름 검사(<see cref="FunctionNameChecker"/>)와 쓰기 판정(<see cref="FunctionEffects"/>)이 같은 것을 본다.
    /// </summary>
    public sealed class HostNames
    {
        public static readonly HostNames None = new HostNames(new KeyValuePair<string, bool>[0], new string[0]);

        /// <summary>보이는 함수의 전체 이름 → 엔티티를 쓰는가.</summary>
        private readonly SortedDictionary<string, bool> _functions = new SortedDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>이름 공간(소유 모드 id와 경로 중간) → 바로 아래 이름들.</summary>
        private readonly SortedDictionary<string, SortedSet<string>> _children =
            new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        private readonly SortedSet<string> _hidden = new SortedSet<string>(StringComparer.Ordinal);

        /// <param name="visible">보이는 함수의 전체 이름 → 엔티티를 쓰는가</param>
        /// <param name="hidden">호스트 함수가 있지만 이 모드에 보이지 않는 소유 모드 id</param>
        public HostNames(IEnumerable<KeyValuePair<string, bool>> visible, IEnumerable<string> hidden)
        {
            foreach (var pair in visible)
            {
                _functions[pair.Key] = pair.Value;
                var parts = pair.Key.Split('.');
                string prefix = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    if (!_children.TryGetValue(prefix, out var set))
                    {
                        set = new SortedSet<string>(StringComparer.Ordinal);
                        _children.Add(prefix, set);
                    }
                    set.Add(parts[i]);
                    prefix += "." + parts[i];
                }
            }
            _hidden.UnionWith(hidden);
        }

        /// <summary>이 모드에 보이는 이름 공간의 맨 앞(소유 모드 id)인가.</summary>
        public bool IsRoot(string name) => name.IndexOf('.') < 0 && _children.ContainsKey(name);

        /// <summary>보이는 이름 공간의 맨 앞들 (소유 모드 id).</summary>
        public IEnumerable<string> Roots
        {
            get
            {
                foreach (var name in _children.Keys)
                    if (name.IndexOf('.') < 0) yield return name;
            }
        }

        /// <summary>호스트 함수가 있지만 이 모드가 의존하지 않아 보이지 않는 소유 모드 id인가.</summary>
        public bool IsHidden(string name) => _hidden.Contains(name);

        public bool IsWriting(string fullName) => _functions.TryGetValue(fullName, out bool writes) && writes;

        /// <summary><c>core.path.find</c> 같은 속성 줄을 이름 줄로 편다. 맨 앞이 이름이 아니면 false.</summary>
        public static bool TryChain(Expr expr, out NameExpr root, out List<AttributeExpr> attributes)
        {
            attributes = new List<AttributeExpr>();
            while (expr is AttributeExpr attr)
            {
                attributes.Insert(0, attr);
                expr = attr.Target;
            }
            root = (expr as NameExpr)!;
            return root != null;
        }

        /// <summary>속성 줄 하나를 푼 결과.</summary>
        public readonly struct Resolution
        {
            public Resolution(string? function, int used, string prefix, string? missing)
            {
                Function = function;
                Used = used;
                Prefix = prefix;
                Missing = missing;
            }

            /// <summary>함수에 닿았으면 그 전체 이름.</summary>
            public string? Function { get; }

            /// <summary>함수나 막힌 곳까지 쓴 속성 수. 함수 뒤에 속성이 더 있으면 <c>attributes.Count</c>보다 작다.</summary>
            public int Used { get; }

            /// <summary>마지막으로 찾은 이름 공간이나 함수의 전체 이름.</summary>
            public string Prefix { get; }

            /// <summary>이름 공간에 없는 이름이면 그 이름.</summary>
            public string? Missing { get; }
        }

        /// <summary>
        /// 맨 앞이 <see cref="IsRoot"/>인 속성 줄을 푼다: 함수에 닿거나(뒤에 속성이 더 있을 수 있음), 이름 공간에서 끝나거나,
        /// 없는 이름에서 막힌다.
        /// </summary>
        public Resolution Resolve(string root, IReadOnlyList<AttributeExpr> attributes)
        {
            string prefix = root;
            for (int i = 0; i < attributes.Count; i++)
            {
                string next = prefix + "." + attributes[i].Name;
                if (_functions.ContainsKey(next)) return new Resolution(next, i + 1, next, null);
                if (!_children.ContainsKey(next)) return new Resolution(null, i, prefix, attributes[i].Name);
                prefix = next;
            }
            return new Resolution(null, attributes.Count, prefix, null);
        }

        /// <summary>이름 공간 바로 아래 이름들 ("혹시 …?" 후보).</summary>
        public IReadOnlyCollection<string> Children(string prefix) =>
            _children.TryGetValue(prefix, out var set) ? set : (IReadOnlyCollection<string>)Array.Empty<string>();

        /// <summary>이름 공간 아래의 함수 하나 (안내 문구에 쓸 예).</summary>
        public string FirstFunctionUnder(string prefix)
        {
            foreach (var name in _functions.Keys)
                if (name.StartsWith(prefix + ".", StringComparison.Ordinal)) return name;
            return prefix;
        }
    }
}
