using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    internal static partial class AttributeChecker
    {
        // ---- derived 순환 (G229) ----

        /// <summary>
        /// 같은 엔티티 안의 <c>derived</c> 순환은 로드 오류다 (spec-gaps G229, G100 보강). <c>derived</c>마다 함수를 훑어 자기
        /// 엔티티(인자, 그 형제 컴포넌트, <c>h.entity</c>, 같은 줄)의 어느 <c>derived</c>를 읽는지 모은다. 모든 갈래를 읽는다고 보고,
        /// 자기 엔티티를 넘겨받은 모드 함수 안도 따라간다. Ref·역방향·<c>query</c>·다른 줄로 간 읽기는 데이터에 따라 순환이 되므로
        /// 보지 않는다(실행 중 재진입 감지가 잡는다). 순환은 어떤 데이터에서든 읽는 순간 실패한다.
        /// </summary>
        private static void CheckDerivedCycles(IReadOnlyList<ModCode> mods, DefRegistry defs, SchemaSet schema, StageDiagnostics stage)
        {
            var order = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < mods.Count; i++) order[mods[i].Mod] = i;

            var reads = new DerivedReads(schema, defs, mods);
            var nodes = new List<DerivedNode>();
            var byLabel = new Dictionary<string, DerivedNode>(StringComparer.Ordinal);
            foreach (var component in schema.Components)
            foreach (var member in component.Members)
            {
                var fn = member.Spec.Function;
                if (member.Kind != MemberKind.Derived || fn == null || fn.Parameters.Count != 1 || !order.ContainsKey(fn.Mod)) continue;
                var self = new ComponentStatic(component, null, component.IsKeyed, self: true);
                var edges = reads.Walk(fn, new[] { new KeyValuePair<string, StaticType?>(fn.Parameters[0].Name, self) });
                var node = new DerivedNode(component.Name + "." + member.Name, fn.Mod, edges);
                nodes.Add(node);
                byLabel[node.Label] = node;
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1: 지금 경로 위, 2: 끝남
            var path = new List<DerivedNode>();
            var pathEdges = new List<ReadEdge>();
            var reported = new HashSet<string>(StringComparer.Ordinal);

            void Visit(DerivedNode node)
            {
                state[node.Label] = 1;
                path.Add(node);
                foreach (var edge in node.Edges)
                {
                    if (!byLabel.TryGetValue(edge.Target, out var target)) continue;
                    state.TryGetValue(target.Label, out int seen);
                    if (seen == 1)
                    {
                        int start = path.IndexOf(target);
                        var cycle = path.GetRange(start, path.Count - start);
                        var cycleEdges = pathEdges.GetRange(start, pathEdges.Count - start);
                        cycleEdges.Add(edge);
                        Report(cycle, cycleEdges, order, reported, stage);
                    }
                    else if (seen == 0)
                    {
                        pathEdges.Add(edge);
                        Visit(target);
                        pathEdges.RemoveAt(pathEdges.Count - 1);
                    }
                }
                path.RemoveAt(path.Count - 1);
                state[node.Label] = 2;
            }

            foreach (var node in nodes)
                if (!state.ContainsKey(node.Label)) Visit(node);
        }

        /// <summary>
        /// 순환 하나를 한 번 알린다. 책임은 순환의 읽기 중 가장 늦게 로드되는 모드의 것이다(그 모드가 오기 전에는 순환이 없었다).
        /// 글은 그 읽기부터 돈다.
        /// </summary>
        private static void Report(List<DerivedNode> cycle, List<ReadEdge> edges, Dictionary<string, int> order,
            HashSet<string> reported, StageDiagnostics stage)
        {
            foreach (var node in cycle)
                if (reported.Contains(node.Label)) return;
            foreach (var node in cycle) reported.Add(node.Label);

            int blame = 0;
            for (int i = 1; i < cycle.Count; i++)
            {
                int a = order[cycle[i].Mod], b = order[cycle[blame].Mod];
                if (a > b || a == b && string.CompareOrdinal(cycle[i].Label, cycle[blame].Label) < 0) blame = i;
            }

            var chain = new List<string>();
            var related = new List<SourceSpan>();
            for (int k = 0; k < cycle.Count; k++)
            {
                int i = (blame + k) % cycle.Count;
                chain.Add(cycle[i].Label);
                if (k > 0) related.Add(edges[i].Span);
            }
            chain.Add(cycle[blame].Label);
            stage.Error(cycle[blame].Mod, Codes.SchDerivedCycle,
                "derived '" + cycle[blame].Label + "' reads itself on the same entity: " + string.Join(" -> ", chain),
                edges[blame].Span, "this fails whenever it is read; split the formula in two (schema-design 3.3.1)", related);
        }

        private sealed class DerivedNode
        {
            public DerivedNode(string label, string mod, List<ReadEdge> edges)
            {
                Label = label;
                Mod = mod;
                Edges = edges;
            }

            /// <summary><c>컴포넌트.값</c></summary>
            public string Label { get; }

            /// <summary>함수를 정의한 모드.</summary>
            public string Mod { get; }

            /// <summary>자기 엔티티에서 읽는 <c>derived</c>들, 처음 읽은 곳과 함께 (같은 값은 한 번).</summary>
            public List<ReadEdge> Edges { get; }
        }

        private readonly struct ReadEdge
        {
            public ReadEdge(string target, SourceSpan span)
            {
                Target = target;
                Span = span;
            }

            public string Target { get; }

            /// <summary><c>derived</c> 함수 안의 자리: 읽은 속성, 함수 안에서 읽었으면 그 호출.</summary>
            public SourceSpan Span { get; }
        }

        /// <summary><c>derived</c> 함수 하나가 자기 엔티티에서 읽는 <c>derived</c>를 모은다 (<see cref="Walker"/>가 채운다).</summary>
        private sealed class DerivedReads
        {
            private readonly SchemaSet _schema;
            private readonly DefRegistry _defs;
            private readonly Dictionary<string, ModCode> _mods = new Dictionary<string, ModCode>(StringComparer.Ordinal);
            private readonly Dictionary<string, ModNames> _globals = new Dictionary<string, ModNames>(StringComparer.Ordinal);

            /// <summary>따라 들어가는 중인 함수들. 로직 함수의 재귀는 로드 오류지만(EVL002) 그 모드가 꺼지기 전에도 이 검사는 돈다.</summary>
            private readonly HashSet<object> _active = new HashSet<object>();

            private List<ReadEdge> _edges = new List<ReadEdge>();
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>따라 들어간 함수 안이면 <c>derived</c> 함수 안의 그 호출 자리.</summary>
            private SourceSpan? _site;

            public DerivedReads(SchemaSet schema, DefRegistry defs, IReadOnlyList<ModCode> mods)
            {
                _schema = schema;
                _defs = defs;
                foreach (var mod in mods) _mods[mod.Mod] = mod;
            }

            public List<ReadEdge> Walk(FunctionValue function, IReadOnlyList<KeyValuePair<string, StaticType?>> parameters)
            {
                _edges = new List<ReadEdge>();
                _seen.Clear();
                Enter(function, parameters);
                return _edges;
            }

            public void Add(ComponentType component, MemberDecl member, SourceSpan span)
            {
                string label = component.Name + "." + member.Name;
                if (_seen.Add(label)) _edges.Add(new ReadEdge(label, _site ?? span));
            }

            /// <summary>
            /// 모드 함수 호출: 자기 엔티티의 값을 넘기면 그 매개변수는 자기 것으로 보고 본문을 훑는다. 나머지 매개변수는 적은 타입이나 모름.
            /// </summary>
            public void Follow(string mod, string name, IReadOnlyList<Argument> arguments, List<StaticType?> types, SourceSpan site)
            {
                if (!_mods.TryGetValue(mod, out var code) || !code.Scope.TryGet(name, out var value, out _)) return;
                if (!(value is FunctionValue fn) || fn.IsEntry || _active.Contains(Node(fn))) return;

                var given = new StaticType?[fn.Parameters.Count];
                bool any = false;
                for (int i = 0, position = 0; i < arguments.Count; i++)
                {
                    int index = arguments[i].Name == null ? position++ : IndexOf(fn, arguments[i].Name!);
                    if (index < 0 || index >= given.Length || !IsSelf(types[i])) continue;
                    given[index] = types[i];
                    any = true;
                }
                if (!any) return;

                var parameters = new List<KeyValuePair<string, StaticType?>>();
                for (int i = 0; i < given.Length; i++)
                {
                    var p = fn.Parameters[i];
                    var type = given[i] ?? (p.Annotation != null ? Annotation(p.Annotation, _schema) : StaticType.Unknown);
                    parameters.Add(new KeyValuePair<string, StaticType?>(p.Name, type));
                }
                var outer = _site;
                _site ??= site;
                Enter(fn, parameters);
                _site = outer;
            }

            private void Enter(FunctionValue fn, IReadOnlyList<KeyValuePair<string, StaticType?>> parameters)
            {
                _active.Add(Node(fn));
                new Walker(_schema, _defs, Globals(fn.Mod), null, fn.Mod, this).Function(fn, parameters);
                _active.Remove(Node(fn));
            }

            private ModNames Globals(string mod)
            {
                if (_globals.TryGetValue(mod, out var names)) return names;
                names = _mods.TryGetValue(mod, out var code)
                    ? AttributeChecker.Globals(code, _schema)
                    : new ModNames();
                _globals[mod] = names;
                return names;
            }

            private static int IndexOf(FunctionValue fn, string name)
            {
                for (int i = 0; i < fn.Parameters.Count; i++)
                    if (fn.Parameters[i].Name == name) return i;
                return -1;
            }

            private static bool IsSelf(StaticType? type) =>
                type is EntityType e && e.Self || type is ComponentStatic c && c.Self;
        }
    }
}
