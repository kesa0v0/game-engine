using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 스크립트의 구조 변경 (spec S4, spec-gaps G134): <c>spawn</c>·<c>destroy</c>·<c>attach</c>·<c>detach</c>·<c>add_row</c>·<c>remove_row</c>와
    /// 처음 값을 담는 컴포넌트 값 생성자(<c>Health(current=3)</c>). 검사는 호스트 API와 같다(G89·G91~G93).
    /// 쓰는 내장이라 쓰기 모드 프레임의 문장으로만 불린다(G117).
    /// </summary>
    public sealed partial class World
    {
        private void AddStructureBuiltins(SortedDictionary<string, Value> table)
        {
            void Writer(string name, Func<IReadOnlyList<Value>, IReadOnlyDictionary<string, Value>, Value> body) =>
                table.Add(name, new PureBuiltin(name, (p, k) => Guard(() => body(p, k))) { Writes = true });

            Writer(PureBuiltins.SpawnName, Spawn);
            Writer(PureBuiltins.DestroyName, (p, k) =>
            {
                Expect(PureBuiltins.DestroyName, p, k, 1, "destroy(entity)");
                Destroy(EntityArg(PureBuiltins.DestroyName, p[0]));
                return NoneValue.Instance;
            });
            Writer(PureBuiltins.AttachName, (p, k) =>
            {
                Expect(PureBuiltins.AttachName, p, k, 2, "attach(entity, Component(...))");
                var id = EntityArg(PureBuiltins.AttachName, p[0]);
                var value = InitArg(PureBuiltins.AttachName, p[1]);
                Add(id, value.Component.Name, value.Inits());
                return NoneValue.Instance;
            });
            Writer(PureBuiltins.DetachName, Detach);
            Writer(PureBuiltins.AddRowName, AddRowBuiltin);
            Writer(PureBuiltins.ReplaceRowsName, ReplaceRows);
            Writer(PureBuiltins.AppendRowName, AppendRow);
            Writer(PureBuiltins.RemoveRowName, (p, k) =>
            {
                Expect(PureBuiltins.RemoveRowName, p, k, 2, "remove_row(entity.Component, key)");
                var rows = RowsArg(PureBuiltins.RemoveRowName, p[0]);
                RemoveRow(rows.Id, rows.Component.Name, RowKeyArg(rows.Component, p[1]));
                return NoneValue.Instance;
            });

            // 컴포넌트 값 생성자: 이름 검사(G108)가 그 모드에 보이는 컴포넌트만 통과시킨다
            foreach (var component in _schema.Components)
            {
                var c = component;
                if (table.ContainsKey(c.Name)) continue; // 내장과 같은 이름의 컴포넌트는 생성자로 쓸 수 없다
                table.Add(c.Name, new PureBuiltin(c.Name, (p, k) => ConstructComponent(c, p, k)));
            }
        }

        /// <summary><c>spawn("Def id", Component=Component(...), ...)</c>: 엔티티를 만들고 그 엔티티를 돌려준다.</summary>
        private Value Spawn(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "spawn(\"Def id\", Component=Component(...), ...)";
            if (positional.Count != 1)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "spawn() takes the Def id as its only positional argument, got " + positional.Count, "write " + usage);
            if (!(positional[0] is StrValue defId))
                throw new ScriptRuntimeException(Codes.RunType,
                    "the Def id given to spawn() must be a str, not " + positional[0].KindName, "write " + usage);

            var inits = new List<Init>();
            foreach (var pair in keywords)
            {
                var value = InitArg(PureBuiltins.SpawnName, pair.Value);
                if (value.Component.Name != pair.Key)
                    throw new ScriptRuntimeException(Codes.RunType,
                        "spawn() got " + value.Component.Name + "(...) for '" + pair.Key + "'",
                        "write " + value.Component.Name + "=" + value.Component.Name + "(...)");
                if (value.Component.IsKeyed)
                    throw new ScriptRuntimeException(Codes.RunType,
                        "'" + pair.Key + "' is a keyed component; spawn() does not take its rows",
                        "spawn the entity first, then add rows with add_row(entity." + pair.Key + ", key, " + pair.Key +
                        "(...)) (spec-gaps G134)");
                inits.AddRange(value.Inits());
            }
            return new EntityView(this, Create(defId.Value, inits.ToArray()));
        }

        /// <summary><c>detach(entity.Component)</c>: 런타임에 붙인 컴포넌트(키 컴포넌트면 줄 묶음 전체)를 뗀다.</summary>
        private Value Detach(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            Expect(PureBuiltins.DetachName, positional, keywords, 1, "detach(entity.Component)");
            switch (positional[0])
            {
                case ComponentView view when view.Row == null:
                    Remove(view.Id, view.Component.Name);
                    return NoneValue.Instance;
                case RowsView rows:
                    Remove(rows.Id, rows.Component.Name);
                    return NoneValue.Instance;
                case ComponentView _:
                    throw new ScriptRuntimeException(Codes.RunType, "detach() got one row of a keyed component",
                        "remove one row with remove_row(entity.Component, key), or detach the whole entity.Component");
                case NoneValue _:
                    throw new ScriptRuntimeException(Codes.RunNoComponent, "detach() got None: the component is not attached",
                        "check it with 'is not None' first");
            }
            throw new ScriptRuntimeException(Codes.RunType,
                "detach() takes a component of an entity (entity.Component), not " + positional[0].KindName);
        }

        /// <summary><c>add_row(entity.Component, key, Component(...))</c>: 키 줄 하나를 붙인다. 값을 생략하면 기본값으로 채운다.</summary>
        private Value AddRowBuiltin(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string usage = "add_row(entity.Component, key, Component(...))";
            if (keywords.Count > 0 || positional.Count < 2 || positional.Count > 3)
                throw new ScriptRuntimeException(Codes.RunScript,
                    "add_row() takes 2 or 3 positional arguments, got " + positional.Count +
                    (keywords.Count > 0 ? " and keyword arguments" : ""), "write " + usage);
            var rows = RowsArg(PureBuiltins.AddRowName, positional[0]);
            var key = RowKeyArg(rows.Component, positional[1]);
            var inits = Array.Empty<Init>();
            if (positional.Count == 3)
            {
                var value = InitArg(PureBuiltins.AddRowName, positional[2]);
                if (value.Component != rows.Component)
                    throw new ScriptRuntimeException(Codes.RunType,
                        "add_row() adds a '" + rows.Component.Name + "' row, but got " + value.Component.Name + "(...)",
                        "write " + rows.Component.Name + "(...)");
                inits = value.Inits();
            }
            AddRow(rows.Id, rows.Component.Name, key, inits);
            return NoneValue.Instance;
        }

        // ---- 인자 ----

        private static void Expect(string name, IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords,
            int count, string usage)
        {
            if (positional.Count == count && keywords.Count == 0) return;
            throw new ScriptRuntimeException(Codes.RunScript,
                name + "() takes " + count + " positional argument" + (count == 1 ? "" : "s") + ", got " + positional.Count +
                (keywords.Count > 0 ? " and keyword arguments" : ""), "write " + usage);
        }

        private EntityId EntityArg(string name, Value value)
        {
            if (value is IEntityReference r) return r.EntityId;
            throw new ScriptRuntimeException(Codes.RunType, name + "() takes an entity, not " + value.KindName);
        }

        private static ComponentInitValue InitArg(string name, Value value)
        {
            if (value is ComponentInitValue init) return init;
            throw new ScriptRuntimeException(Codes.RunType,
                name + "() takes a component value like Health(current=3), not " + value.KindName);
        }

        private static RowsView RowsArg(string name, Value value)
        {
            switch (value)
            {
                case RowsView rows: return rows;
                case NoneValue _:
                    throw new ScriptRuntimeException(Codes.RunNoComponent,
                        name + "() got None: the keyed component is not attached",
                        "attach it first with attach(entity, Component())");
            }
            throw new ScriptRuntimeException(Codes.RunType,
                name + "() takes the rows of a keyed component (entity.Component), not " + value.KindName);
        }

        private static RowKey RowKeyArg(ComponentType component, Value key)
        {
            switch (key)
            {
                case StrValue s when component.KeyKind == KeyKind.Def: return RowKey.Def(s.Value);
                case IEntityReference r when component.KeyKind == KeyKind.Entity: return RowKey.Entity(r.EntityId);
                case IntValue n when component.KeyKind == KeyKind.Int: return RowKey.Int(n.Value);
            }
            throw new ScriptRuntimeException(Codes.RunType,
                "'" + component.Name + "' rows are keyed by " + component.KeyDisplay + ", not " + key.KindName);
        }

        // ---- int 키 줄을 리스트처럼 (G129) ----

        private static RowsView IntRowsArg(string name, Value value)
        {
            var rows = RowsArg(name, value);
            if (rows.Component.KeyKind == KeyKind.Int) return rows;
            throw new ScriptRuntimeException(Codes.RunType,
                name + "() works on a component keyed by int, but '" + rows.Component.Name + "' is keyed by " +
                rows.Component.KeyDisplay, "use add_row and remove_row for other keyed components (spec-gaps G129)");
        }

        private static Init[] RowInitArg(string name, ComponentType component, Value value)
        {
            var init = InitArg(name, value);
            if (init.Component != component)
                throw new ScriptRuntimeException(Codes.RunType,
                    name + "() takes " + component.Name + "(...) values, not " + init.Component.Name + "(...)");
            return init.Inits();
        }

        /// <summary>
        /// <c>replace_rows(node.Die, [Die(value=5), ...])</c>: 줄을 모두 지우고 0부터 다시 만든다. 지워진 줄을 겨냥한
        /// 기여 행도 함께 지워진다 (번호를 다시 매기는 것은 지우고 새로 만들기다).
        /// </summary>
        private Value ReplaceRows(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = PureBuiltins.ReplaceRowsName;
            Expect(name, positional, keywords, 2, "replace_rows(entity.Component, [Component(...), ...])");
            var rows = IntRowsArg(name, positional[0]);
            if (!(positional[1] is ListValue list))
                throw new ScriptRuntimeException(Codes.RunType,
                    name + "() takes a list of " + rows.Component.Name + "(...) values, not " + positional[1].KindName);
            // 바꾸기 전에 모든 원소를 본다
            var items = new List<Init[]>(list.Items.Count);
            foreach (var item in list.Items) items.Add(RowInitArg(name, rows.Component, item));

            foreach (var key in RowInts(rows.Id, rows.Component.Name)) RemoveRow(rows.Id, rows.Component.Name, RowKey.Int(key));
            for (int i = 0; i < items.Count; i++) AddRow(rows.Id, rows.Component.Name, RowKey.Int(i), items[i]);
            return NoneValue.Instance;
        }

        /// <summary><c>append_row(node.Die, Die(value=5))</c>: 가장 큰 키 + 1(없으면 0)에 줄을 붙인다. 값을 생략하면 기본값이다.</summary>
        private Value AppendRow(IReadOnlyList<Value> positional, IReadOnlyDictionary<string, Value> keywords)
        {
            const string name = PureBuiltins.AppendRowName;
            const string usage = "append_row(entity.Component, Component(...))";
            if (keywords.Count > 0 || positional.Count < 1 || positional.Count > 2)
                throw new ScriptRuntimeException(Codes.RunScript,
                    name + "() takes 1 or 2 positional arguments, got " + positional.Count +
                    (keywords.Count > 0 ? " and keyword arguments" : ""), "write " + usage);
            var rows = IntRowsArg(name, positional[0]);
            var inits = positional.Count == 2 ? RowInitArg(name, rows.Component, positional[1]) : Array.Empty<Init>();
            var keys = RowInts(rows.Id, rows.Component.Name);
            long next = keys.Count == 0 ? 0 : checked(keys[keys.Count - 1] + 1);
            AddRow(rows.Id, rows.Component.Name, RowKey.Int(next), inits);
            return NoneValue.Instance;
        }

        // ---- 컴포넌트 값 ----

        /// <summary>
        /// <c>Health(current=3)</c>: 엔티티나 줄이 생길 때의 처음 값 (Def 리터럴의 컴포넌트 값과 같은 모양, G134).
        /// 멤버 이름은 여기서 보고, 값의 타입·쓸 수 있는지는 생성 순간에 호스트 API와 같이 본다.
        /// </summary>
        private Value ConstructComponent(ComponentType component, IReadOnlyList<Value> positional,
            IReadOnlyDictionary<string, Value> keywords)
        {
            if (positional.Count > 0)
                throw new ScriptRuntimeException(Codes.RunScript,
                    component.Name + "(...) takes values by name, like " + component.Name + "(value=...)");
            var values = new List<KeyValuePair<MemberDecl, Value>>();
            foreach (var pair in keywords)
            {
                if (!component.TryGetMember(pair.Key, out var member))
                {
                    var names = new List<string>();
                    foreach (var m in component.Members) names.Add(m.Name);
                    throw new ScriptRuntimeException(Codes.RunUnknownName,
                        "component '" + component.Name + "' has no value '" + pair.Key + "'", Suggest.Hint(pair.Key, names));
                }
                values.Add(new KeyValuePair<MemberDecl, Value>(member, ToPlain(component.Name + "." + member.Name, pair.Value)));
            }
            return new ComponentInitValue(component, values);
        }

        private sealed class ComponentInitValue : Value, IValueEquality, IValueContainer
        {
            public Value MapValues(Func<Value, Value> map)
            {
                var mapped = new List<KeyValuePair<MemberDecl, Value>>(_values.Count);
                foreach (var pair in _values) mapped.Add(new KeyValuePair<MemberDecl, Value>(pair.Key, map(pair.Value)));
                return new ComponentInitValue(Component, mapped);
            }

            /// <summary>같은 컴포넌트에 같은 값들을 줬으면 같다(준 순서는 상관없다, G142).</summary>
            public bool ValueEquals(Value other)
            {
                if (!(other is ComponentInitValue c) || c.Component != Component || c._values.Count != _values.Count) return false;
                foreach (var mine in _values)
                {
                    bool found = false;
                    foreach (var theirs in c._values)
                        if (theirs.Key == mine.Key) found = PureOps.ValuesEqual(mine.Value, theirs.Value);
                    if (!found) return false;
                }
                return true;
            }

            private readonly IReadOnlyList<KeyValuePair<MemberDecl, Value>> _values;

            public ComponentInitValue(ComponentType component, IReadOnlyList<KeyValuePair<MemberDecl, Value>> values)
            {
                Component = component;
                _values = values;
            }

            public ComponentType Component { get; }
            public IReadOnlyList<KeyValuePair<MemberDecl, Value>> Values => _values;
            public override string KindName => Component.Name + "(...)";

            public Init[] Inits()
            {
                var inits = new Init[_values.Count];
                for (int i = 0; i < inits.Length; i++)
                    inits[i] = new Init(new MemberHandle(Component, null, _values[i].Key), _values[i].Value);
                return inits;
            }
        }
    }
}
