using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

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

    /// <summary>
    /// 모드 정렬 (spec S1 ①). 사이클과 누락은 오류이며, 실패한 모드에 필수로 의존하는 모드도 비활성화된다.
    /// loadAfter·optional 대상이 비활성화되면 없는 것으로 보고 순서만 잃는다.
    /// 동률 규칙: 앞에 올 수 있는 모드가 여럿이면 id가 서수상 가장 작은 것을 먼저 둔다 (spec-gaps G1).
    /// </summary>
    public static class ModSorter
    {
        public static LoadOrder Sort(DiscoveryResult discovery, DiagnosticBag diagnostics) =>
            Sort(discovery.Mods, diagnostics, discovery.FailedIds);

        /// <param name="failedIds">설치되어 있지만 매니페스트 오류로 로드할 수 없는 모드 id</param>
        public static LoadOrder Sort(IReadOnlyList<ModManifest> discovered, DiagnosticBag diagnostics,
            IReadOnlyList<string>? failedIds = null)
        {
            var byId = new SortedDictionary<string, List<ModManifest>>(StringComparer.Ordinal);
            foreach (var m in discovered)
            {
                if (!byId.TryGetValue(m.Id, out var list)) byId[m.Id] = list = new List<ModManifest>();
                list.Add(m);
            }

            var installed = new SortedSet<string>(byId.Keys, StringComparer.Ordinal);
            var disabled = new SortedSet<string>(StringComparer.Ordinal);
            if (failedIds != null)
            {
                foreach (var id in failedIds)
                {
                    installed.Add(id);
                    if (!byId.ContainsKey(id)) disabled.Add(id);
                }
            }

            var allIds = new List<string>(installed);
            var candidates = new SortedDictionary<string, ModManifest>(StringComparer.Ordinal);

            // 1. id 중복: 어느 쪽도 고를 수 없으므로 모두 비활성화
            foreach (var pair in byId)
            {
                if (pair.Value.Count == 1)
                {
                    candidates[pair.Key] = pair.Value[0];
                    continue;
                }

                foreach (var m in pair.Value)
                {
                    var others = new List<SourceSpan>();
                    foreach (var o in pair.Value)
                        if (!ReferenceEquals(o, m)) others.Add(o.Span);
                    diagnostics.Error(Codes.ModDuplicateId, "mod id '" + pair.Key + "' is declared by more than one folder",
                        m.Span, mod: pair.Key, related: others);
                }
                disabled.Add(pair.Key);
            }

            // 2. 설치되지 않은 참조
            foreach (var m in candidates.Values)
            {
                foreach (var dep in m.Dependencies)
                {
                    if (installed.Contains(dep.Id)) continue;
                    diagnostics.Error(Codes.ModMissingDependency,
                        "mod '" + m.Id + "' requires '" + dep.Id + "', which is not installed",
                        dep.Span, mod: m.Id, hint: Suggest.Hint(dep.Id, allIds));
                }
                WarnIfLikelyTypo(m, m.LoadAfter, "loadAfter", installed, allIds, diagnostics);
                WarnIfLikelyTypo(m, m.Optional, "optional", installed, allIds, diagnostics);
            }

            PropagateFailedDependencies(candidates, disabled, installed, diagnostics);

            // 3. 정렬. 사이클이 있으면 사이클 모드를 빼고 다시 정렬한다.
            while (true)
            {
                var graph = Graph.Build(candidates);
                var order = graph.TopologicalOrder();
                if (order.Count == graph.Ids.Count)
                {
                    return BuildResult(graph, order, candidates, disabled);
                }

                foreach (var id in ReportCycles(graph, order, candidates, diagnostics))
                {
                    candidates.Remove(id);
                    disabled.Add(id);
                }
                PropagateFailedDependencies(candidates, disabled, installed, diagnostics);
            }
        }

        /// <summary>필수 의존성이 비활성화된 모드를 고정점까지 비활성화한다.</summary>
        private static void PropagateFailedDependencies(SortedDictionary<string, ModManifest> candidates,
            SortedSet<string> disabled, SortedSet<string> installed, DiagnosticBag diagnostics)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in new List<ModManifest>(candidates.Values))
                {
                    foreach (var dep in m.Dependencies)
                    {
                        if (candidates.ContainsKey(dep.Id)) continue;
                        if (installed.Contains(dep.Id))
                        {
                            diagnostics.Error(Codes.ModDependencyFailed,
                                "mod '" + m.Id + "' was disabled because its dependency '" + dep.Id + "' failed to load",
                                dep.Span, mod: m.Id);
                        }
                        candidates.Remove(m.Id);
                        disabled.Add(m.Id);
                        changed = true;
                        break;
                    }
                }
            }
        }

        private sealed class Graph
        {
            private Graph(List<string> ids, List<SortedSet<int>> successors, List<SortedSet<int>> predecessors)
            {
                Ids = ids;
                Successors = successors;
                Predecessors = predecessors;
            }

            /// <summary>서수 정렬된 id. 인덱스 순서 = id 순서.</summary>
            public List<string> Ids { get; }

            /// <summary>앞서야 하는 모드 → 뒤에 오는 모드.</summary>
            public List<SortedSet<int>> Successors { get; }

            public List<SortedSet<int>> Predecessors { get; }

            public static Graph Build(SortedDictionary<string, ModManifest> candidates)
            {
                var ids = new List<string>(candidates.Keys);
                var index = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < ids.Count; i++) index[ids[i]] = i;

                var succ = new List<SortedSet<int>>();
                var pred = new List<SortedSet<int>>();
                for (int i = 0; i < ids.Count; i++)
                {
                    succ.Add(new SortedSet<int>());
                    pred.Add(new SortedSet<int>());
                }

                for (int i = 0; i < ids.Count; i++)
                {
                    foreach (var r in Before(candidates[ids[i]]))
                    {
                        if (!index.TryGetValue(r.Id, out int before)) continue;
                        succ[before].Add(i);
                        pred[i].Add(before);
                    }
                }
                return new Graph(ids, succ, pred);
            }

            /// <summary>Kahn: 준비된 모드 중 인덱스(= id)가 가장 작은 것부터. 사이클이 있으면 일부만 돌려준다.</summary>
            public List<int> TopologicalOrder()
            {
                var inDegree = new int[Ids.Count];
                for (int i = 0; i < Ids.Count; i++) inDegree[i] = Predecessors[i].Count;

                var ready = new SortedSet<int>();
                for (int i = 0; i < Ids.Count; i++)
                    if (inDegree[i] == 0) ready.Add(i);

                var order = new List<int>();
                while (ready.Count > 0)
                {
                    int n = ready.Min;
                    ready.Remove(n);
                    order.Add(n);
                    foreach (int s in Successors[n])
                        if (--inDegree[s] == 0) ready.Add(s);
                }
                return order;
            }
        }

        private static IEnumerable<ModRef> Before(ModManifest m)
        {
            foreach (var r in m.Dependencies) yield return r;
            foreach (var r in m.LoadAfter) yield return r;
            foreach (var r in m.Optional) yield return r;
        }

        private static LoadOrder BuildResult(Graph graph, List<int> order, SortedDictionary<string, ModManifest> candidates,
            SortedSet<string> disabled)
        {
            var mods = new List<ModManifest>();
            var position = new int[graph.Ids.Count];
            for (int p = 0; p < order.Count; p++)
            {
                mods.Add(candidates[graph.Ids[order[p]]]);
                position[order[p]] = p;
            }

            // 위상 순서의 역순으로 도달 집합을 합친다
            var reach = new bool[mods.Count, mods.Count];
            for (int p = order.Count - 1; p >= 0; p--)
            {
                foreach (int s in graph.Successors[order[p]])
                {
                    int q = position[s];
                    reach[p, q] = true;
                    for (int k = 0; k < mods.Count; k++)
                        if (reach[q, k]) reach[p, k] = true;
                }
            }

            return new LoadOrder(mods, new List<string>(disabled), reach);
        }

        /// <summary>
        /// 정렬되지 못한 모드 사이의 사이클을 찾아 보고하고, 사이클에 든 모드 id를 돌려준다.
        /// 정렬되지 못한 모드는 모두 정렬되지 못한 선행자를 가지므로, 선행자를 거슬러 가면 반드시 사이클에 닿는다.
        /// </summary>
        private static List<string> ReportCycles(Graph graph, List<int> order, SortedDictionary<string, ModManifest> candidates,
            DiagnosticBag diagnostics)
        {
            int count = graph.Ids.Count;
            var placed = new bool[count];
            foreach (int n in order) placed[n] = true;
            var inCycle = new bool[count];
            var members = new List<string>();

            for (int start = 0; start < count; start++)
            {
                if (placed[start] || inCycle[start]) continue;

                var path = new List<int>();
                var seenAt = new Dictionary<int, int>();
                int cur = start;
                while (!seenAt.ContainsKey(cur) && !inCycle[cur])
                {
                    seenAt[cur] = path.Count;
                    path.Add(cur);
                    int next = -1;
                    foreach (int p in graph.Predecessors[cur])
                    {
                        if (placed[p]) continue;
                        next = p;
                        break;
                    }
                    cur = next;
                }

                if (inCycle[cur]) continue;

                // path[seenAt[cur]..]는 선행자 방향의 사이클. 로드 방향으로 뒤집고 id가 가장 작은 모드부터 쓴다.
                var cycle = path.GetRange(seenAt[cur], path.Count - seenAt[cur]);
                cycle.Reverse();
                int minPos = 0;
                for (int i = 1; i < cycle.Count; i++)
                    if (cycle[i] < cycle[minPos]) minPos = i;

                var names = new List<string>();
                var related = new List<SourceSpan>();
                for (int i = 0; i < cycle.Count; i++)
                {
                    int n = cycle[(minPos + i) % cycle.Count];
                    inCycle[n] = true;
                    names.Add(graph.Ids[n]);
                    members.Add(graph.Ids[n]);
                    if (i > 0) related.Add(candidates[graph.Ids[n]].Span);
                }
                names.Add(names[0]);

                var first = candidates[names[0]];
                diagnostics.Error(Codes.ModCycle, "load order cycle: " + string.Join(" -> ", names), first.Span,
                    mod: first.Id, related: related);
            }

            return members;
        }

        /// <summary>없어도 되는 참조라도 설치된 모드와 이름이 아주 비슷하면 오타일 가능성이 크다.</summary>
        private static void WarnIfLikelyTypo(ModManifest m, IReadOnlyList<ModRef> refs, string field,
            SortedSet<string> installed, List<string> allIds, DiagnosticBag diagnostics)
        {
            foreach (var r in refs)
            {
                if (installed.Contains(r.Id)) continue;
                var hint = Suggest.Hint(r.Id, allIds);
                if (hint == null) continue;
                diagnostics.Warning(Codes.ModMissingDependency,
                    "'" + r.Id + "' in " + field + " of mod '" + m.Id + "' is not installed", r.Span, mod: m.Id, hint: hint);
            }
        }
    }
}
