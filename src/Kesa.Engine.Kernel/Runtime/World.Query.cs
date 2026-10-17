using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 전역 순회 <c>query(...)</c> (spec S3, spec-gaps G192·G193). 출처는 Def 타입 하나 그리고/또는 컴포넌트들이고 모두 만족하는
    /// 엔티티를 id 순으로 준다. 값으로 거르기는 컴프리헨션이 한다. 읽으면 그 출처의 목록을 읽은 것으로 기록한다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary>
        /// 그 Def 타입이고 그 컴포넌트를 모두 가진 살아 있는 엔티티 (id 순, 시작 시점의 목록). 이름은 Def 타입이나 컴포넌트이고
        /// 둘 다 한 이름 공간이라 겹치지 않는다(SCH001). 컴포넌트는 Def에서 왔든 런타임에 붙였든 붙어 있으면 맞는다.
        /// </summary>
        public IReadOnlyList<EntityId> Query(params string[] names)
        {
            var (type, components) = QuerySources(names);
            var result = new List<EntityId>();
            IEnumerable<long> candidates;
            if (type != null)
                candidates = _byType.TryGetValue(type, out var set) ? set : (IEnumerable<long>)new long[0];
            else
            {
                // 컴포넌트만이면 모든 타입의 엔티티를 id 순으로 모아 본다
                var every = new SortedSet<long>();
                foreach (var t in _schema.Types)
                    if (_byType.TryGetValue(t, out var ofType)) every.UnionWith(ofType);
                candidates = every;
            }
            foreach (long id in candidates)
            {
                var entity = _entities[id];
                bool all = true;
                foreach (var c in components)
                    if (!Has(entity, c))
                    {
                        all = false;
                        break;
                    }
                if (all) result.Add(new EntityId(id));
            }
            return result;
        }

        private (DefType? Type, List<ComponentType> Components) QuerySources(string[] names)
        {
            if (names.Length == 0)
                throw new WorldException(Codes.RunScript, "query() needs at least one Def type or component name",
                    "write query(\"Unit\"), query(\"Wounded\") or query(\"Unit\", \"Wounded\")");
            DefType? type = null;
            var components = new List<ComponentType>();
            foreach (var name in names)
            {
                if (_schema.TryGetComponent(name, out var component))
                {
                    components.Add(component);
                    continue;
                }
                if (_schema.TryGetType(name, out var t))
                {
                    if (t.IsDataOnly)
                        throw new WorldException(Codes.RunNotEntity,
                            "'" + name + "' is a data-only Def type with no components, so it has no entities to query");
                    if (type != null && type != t)
                        throw new WorldException(Codes.RunScript,
                            "query() was given two Def types ('" + type.Name + "' and '" + name + "'); an entity has only one",
                            "give one Def type and any number of components");
                    type = t;
                    continue;
                }
                throw Unknown("there is no Def type or component '" + name + "'", name, QueryNames());
            }
            return (type, components);
        }

        /// <summary>쿼리할 수 있는 이름: 엔티티가 되는 Def 타입과 컴포넌트.</summary>
        private List<string> QueryNames()
        {
            var names = new List<string>();
            foreach (var t in _schema.Types)
                if (!t.IsDataOnly) names.Add(t.Name);
            foreach (var c in _schema.Components) names.Add(c.Name);
            return names;
        }

        /// <summary>스크립트의 <c>query("Unit", "Wounded")</c>: 엔티티 묶음 (G194).</summary>
        private Value QueryBuiltin(IReadOnlyList<Value> positional)
        {
            var names = new string[positional.Count];
            for (int i = 0; i < positional.Count; i++)
            {
                if (!(positional[i] is StrValue s))
                    throw new ScriptRuntimeException(Codes.RunType,
                        "query() takes Def type and component names as strings, got " + positional[i].KindName,
                        "write query(\"Unit\")");
                names[i] = s.Value;
            }
            var ids = Guard(() => Query(names));
            if (_trace != null)
            {
                var items = new List<Value>(ids.Count);
                foreach (var id in ids) items.Add(new EntityView(this, id));
                _trace.Add(ExplainNode.Query(names, new ListValue(items)));
            }
            // when 조건이 이 목록을 읽었다: 그 출처의 엔티티가 생기거나 죽거나 컴포넌트가 붙거나 떨어지면 다시 본다 (G193)
            if (_whenReadsSources != null)
                foreach (var name in names) _whenReadsSources.Add(name);
            return new EntityGroup(this, ids, PureBuiltins.QueryName + "(...)");
        }

        /// <summary>
        /// 엔진이 준 엔티티 묶음: <c>query(...)</c>의 결과와 역방향 컬렉션(<c>city.units</c>) (G194, 파이썬 <c>weakref.WeakSet</c>처럼).
        /// 받을 때의 목록(id 순)을 들고 있고 엔티티를 살려 두지 않는다. <c>for</c>는 시작 시점의 목록을 돌며 그사이 죽은 것은 건너뛰고(G88),
        /// <c>len</c>·<c>in</c>·참/거짓은 지금 살아 있는 것만 본다. 인덱스와 리스트 연산은 받지 않는다: 리스트가 필요하면
        /// 컴프리헨션으로 그 순간의 사본을 만든다(<c>[u for u in c.units]</c>).
        /// </summary>
        private sealed class EntityGroup : Value, IRuntimeLiveCollection, IRuntimeIndexable, IValueEquality
        {
            /// <summary>살아 있는 원소가 같으면 같다 (<c>WeakSet</c>끼리의 비교처럼). 리스트와는 다르다.</summary>
            public bool ValueEquals(Value other)
            {
                if (!(other is EntityGroup g)) return false;
                var mine = Iterate();
                var theirs = g.Iterate();
                if (mine.Count != theirs.Count) return false;
                for (int i = 0; i < mine.Count; i++)
                    if (((IEntityReference)mine[i]).EntityId != ((IEntityReference)theirs[i]).EntityId) return false;
                return true;
            }

            private readonly World _world;

            public EntityGroup(World world, IReadOnlyList<EntityId> ids, string label)
            {
                _world = world;
                Ids = ids;
                Label = label;
            }

            /// <summary>받을 때의 목록. 저장은 이것을 적는다.</summary>
            public IReadOnlyList<EntityId> Ids { get; }

            /// <summary>메시지에 쓰는 이름 (<c>units</c>, <c>query(...)</c>).</summary>
            public string Label { get; }

            public override string KindName => "entity group";

            public IReadOnlyList<Value> Iterate()
            {
                var items = new List<Value>(Ids.Count);
                foreach (var id in Ids)
                    if (_world.IsAlive(id)) items.Add(new EntityView(_world, id));
                return items;
            }

            public int Size()
            {
                int n = 0;
                foreach (var id in Ids)
                    if (_world.IsAlive(id)) n++;
                return n;
            }

            public bool ContainsKey(Value key)
            {
                if (!(key is IEntityReference r) || !_world.IsAlive(r.EntityId)) return false;
                foreach (var id in Ids)
                    if (id == r.EntityId) return true;
                return false;
            }

            public bool IsGone(Value item) => item is IEntityReference r && !_world.IsAlive(r.EntityId);

            public Value GetItem(Value key) =>
                throw new ScriptRuntimeException(Codes.RunType,
                    "an entity group (" + Label + ") cannot be indexed; its members can die while you hold it",
                    "loop over it, or make a list first: xs = [u for u in ...] (spec-gaps G194)");
        }

        /// <summary>엔티티가 생기거나 죽었다: 그 타입과 가진 컴포넌트를 출처로 읽은 조건을 다시 본다.</summary>
        private void TouchSources(EntityRecord entity)
        {
            if (_whens.Count == 0) return;
            _whenTouchedSources.Add(entity.Def.Type.Name);
            foreach (var c in ComponentsOf(entity)) _whenTouchedSources.Add(c.Name);
        }

        /// <summary>컴포넌트가 붙거나 떨어졌다.</summary>
        private void TouchSource(string name)
        {
            if (_whens.Count > 0) _whenTouchedSources.Add(name);
        }
    }
}
