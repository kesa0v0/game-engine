using System;
using System.Collections.Generic;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <c>derived</c> 읽기 (spec S4, spec-gaps G100·G103·G105). 저장하지 않고 읽을 때마다 순수 함수로 계산한다.
    /// 함수는 계산할 컴포넌트(키 컴포넌트면 줄) 하나를 받고, 그 컴포넌트의 멤버와 같은 엔티티의 형제 컴포넌트,
    /// Ref를 따라간 다른 엔티티를 읽는다.
    /// </summary>
    public sealed partial class World
    {
        /// <summary><c>derived</c> 안에서 다른 <c>derived</c>를 읽는 연쇄의 깊이 상한 (스택이 넘치기 전에 오류로 알린다).</summary>
        public const int MaxDerivedDepth = 64;

        private readonly IReadOnlyDictionary<string, ModScope> _scopes;
        private readonly int _stepBudget;
        private PureEvaluator? _evaluator;

        /// <summary>지금 계산 중인 <c>derived</c>들 (바깥부터). 같은 값을 다시 읽으면 순환이다 (G100).</summary>
        private readonly List<string> _computing = new List<string>();

        private PureEvaluator Evaluator =>
            _evaluator ??= new PureEvaluator(new ScopeGlobals(_scopes), RuntimeBuiltins(), _stepBudget);

        private IReadOnlyDictionary<string, Value> RuntimeBuiltins()
        {
            var table = PureBuiltins.Create();
            table.Add("contribs", new PureBuiltin("contribs", ContribsBuiltin));
            return table;
        }

        // ---- 계산 ----

        /// <summary>월드 API에서 부르는 <c>derived</c> 읽기. 스크립트 오류는 위치와 호출 흔적이 붙은 <see cref="WorldException"/>이 된다.</summary>
        private Value ReadDerivedTop(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            try
            {
                return ComputeDerived(entity, component, row, member);
            }
            catch (ScriptRuntimeException e)
            {
                throw ToWorldException(e, DerivedLabel(entity, component, row, member));
            }
        }

        private static WorldException ToWorldException(ScriptRuntimeException e, string computing)
        {
            var text = new StringBuilder();
            text.Append("computing ").Append(computing).Append(": ").Append(e.Message);
            if (e.Span.Line > 0) text.Append(" (at ").Append(e.Span).Append(')');
            foreach (var step in e.Trace) text.Append("\n  in ").Append(step);
            return new WorldException(e.Code, text.ToString(), e.Hint);
        }

        private static string DerivedLabel(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member) =>
            component.Name + "." + member.Name + (row != null ? "[" + row.Value + "]" : "") + " of " + new EntityId(entity.Id);

        private Value ComputeDerived(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            string label = DerivedLabel(entity, component, row, member);
            int index = _computing.IndexOf(label);
            if (index >= 0)
            {
                var chain = new List<string>(_computing.GetRange(index, _computing.Count - index)) { label };
                throw new ScriptRuntimeException(Codes.RunCycle,
                    label + " reads itself: " + string.Join(" -> ", chain),
                    "a derived value cannot depend on itself; split the formula in two (schema-design 3.3.1)");
            }
            if (_computing.Count >= MaxDerivedDepth)
            {
                throw new ScriptRuntimeException(Codes.RunCycle,
                    "derived values are nested more than " + MaxDerivedDepth + " deep while reading " + label + ": " +
                    string.Join(" -> ", _computing),
                    "a very long chain of derived reads is treated like a cycle");
            }

            var function = member.Spec.Function!;
            _computing.Add(label);
            try
            {
                var self = new ComponentView(this, new EntityId(entity.Id), component, row);
                var span = function.Definition?.Span ?? function.Lambda!.Span;
                var result = Evaluator.Run(function, new Value[] { self }, span);
                return CheckDerivedResult(component.Name + "." + member.Name, member, result);
            }
            finally
            {
                _computing.RemoveAt(_computing.Count - 1);
            }
        }

        /// <summary>반환 값이 선언한 타입과 맞는지 본다. <c>int</c>는 <c>fixed</c>로 올리지만 그 반대는 <c>floor()</c> 등으로 명시해야 한다.</summary>
        private Value CheckDerivedResult(string display, MemberDecl member, Value value)
        {
            var type = member.Spec.Type;
            var plain = ToPlain(display, value);
            if (type is EntityRefTypeRef refType)
            {
                if (plain is EntityValue ev && _entities.TryGetValue(ev.Id.Value, out var target) &&
                    target.Def.Type.Name == refType.Target.Name)
                    return plain;
                throw new ScriptRuntimeException(Codes.RunType,
                    "'" + display + "' is declared " + type.Display + ", but the function returned " + plain.KindName);
            }

            var normalized = Normalize(type, plain);
            string? problem = PatchOperationCheck.Mismatch(type, normalized);
            if (problem != null)
            {
                throw new ScriptRuntimeException(Codes.RunType,
                    "'" + display + "' is declared " + type.Display + ": " + problem,
                    plain is FixedValue && ReferenceEquals(type, PrimitiveTypeRef.Int)
                        ? "turn the fixed value into an int with floor(), ceil() or round() (spec-gaps G95)"
                        : null);
            }
            return normalized;
        }

        /// <summary>함수 본문에서 보이던 엔티티 값을 저장 가능한 값(<see cref="EntityValue"/>)으로 바꾼다. 그 밖의 런타임 객체는 돌려줄 수 없다.</summary>
        private static Value ToPlain(string display, Value value)
        {
            switch (value)
            {
                case IEntityReference r: return new EntityValue(r.EntityId);
                case ListValue list:
                    var items = new List<Value>(list.Items.Count);
                    foreach (var item in list.Items) items.Add(ToPlain(display, item));
                    return new ListValue(items);
                case DictValue dict:
                    var entries = new List<DictItem>(dict.Items.Count);
                    foreach (var item in dict.Items) entries.Add(new DictItem(item.Key, ToPlain(display, item.Value), item.Span));
                    return new DictValue(entries);
                case ComponentViewBase _:
                    throw new ScriptRuntimeException(Codes.RunType,
                        "'" + display + "' returned a component, which cannot be a value", "return one of its members");
            }
            return value;
        }

        /// <summary>월드의 값을 함수 본문에서 보는 값으로: 엔티티는 따라갈 수 있는 모양으로 바꾼다.</summary>
        private Value ToScript(Value value) => value is EntityValue e ? new EntityView(this, e.Id) : value;

        /// <summary>월드 API가 던진 <see cref="WorldException"/>을 함수 본문 안의 오류로 바꾼다 (코드는 그대로).</summary>
        private static T Guard<T>(Func<T> read)
        {
            try
            {
                return read();
            }
            catch (WorldException e)
            {
                throw new ScriptRuntimeException(e.Code, e.Message, e.Hint);
            }
        }

        /// <summary>컴포넌트(줄)의 멤버 하나를 읽는다. <c>derived</c>면 계산한다.</summary>
        private Value ReadMember(EntityRecord entity, ComponentType component, RowKey? row, MemberDecl member)
        {
            if (member.Kind == MemberKind.Derived) return ComputeDerived(entity, component, row, member);
            string display = component.Name + "." + member.Name;
            if (row == null) return Read(display, component, member, DefMembers(entity, component), entity.Values);
            if (!FindRow(entity, component, row.Value, out var defRow, out var record))
                throw NoRow(new EntityId(entity.Id), component, row.Value);
            return Read(display + "[" + row.Value + "]", component, member, defRow?.Members, record?.Values ?? NoValues);
        }

        // ---- 함수 본문이 보는 값 ----

        private abstract class ComponentViewBase : Value
        {
        }

        /// <summary>엔티티. 컴포넌트 이름으로 그 컴포넌트를, 역방향 이름으로 그 컬렉션을 읽는다 (spec S3).</summary>
        private sealed class EntityView : Value, IRuntimeAttributes, IValueEquality, IEntityReference
        {
            private readonly World _world;

            public EntityView(World world, EntityId id)
            {
                _world = world;
                Id = id;
            }

            public EntityId Id { get; }
            public override string KindName => "entity";
            EntityId IEntityReference.EntityId => Id;

            public bool ValueEquals(Value other) => other is IEntityReference r && r.EntityId == Id;

            public Value GetAttribute(string name) => Guard(() => _world.EntityAttribute(Id, name));
        }

        /// <summary>컴포넌트 하나(키 컴포넌트면 줄 하나). 자기 멤버, 형제 컴포넌트, <c>entity</c>, <c>key</c>를 읽는다 (G103).</summary>
        private sealed class ComponentView : ComponentViewBase, IRuntimeAttributes
        {
            private readonly World _world;
            private readonly EntityId _id;
            private readonly RowKey? _row;

            public ComponentView(World world, EntityId id, ComponentType component, RowKey? row)
            {
                _world = world;
                _id = id;
                Component = component;
                _row = row;
            }

            public ComponentType Component { get; }
            public EntityId Id => _id;
            public RowKey? Row => _row;
            public override string KindName => Component.Name;

            public Value GetAttribute(string name) => Guard(() => _world.ComponentAttribute(this, name));
        }

        /// <summary>키 컴포넌트 전체: 키로 줄 하나를 꼬집는다 (<c>h.Skill.Medicine</c>, <c>h.Skill[s]</c>, <c>for s in h.Skill</c>).</summary>
        private sealed class RowsView : ComponentViewBase, IRuntimeAttributes, IRuntimeIndexable, IRuntimeIterable
        {
            private readonly World _world;
            private readonly EntityId _id;
            private readonly ComponentType _component;

            public RowsView(World world, EntityId id, ComponentType component)
            {
                _world = world;
                _id = id;
                _component = component;
            }

            public override string KindName => _component.Name + " rows";

            public Value GetAttribute(string name) =>
                Guard(() => _world.RowOrNone(_id, _component, RowKey.Def(name)));

            public Value GetItem(Value key)
            {
                switch (key)
                {
                    case StrValue s: return Guard(() => _world.RowOrNone(_id, _component, RowKey.Def(s.Value)));
                    case IEntityReference r: return Guard(() => _world.RowOrNone(_id, _component, RowKey.Entity(r.EntityId)));
                }
                throw new ScriptRuntimeException(Codes.RunType,
                    "'" + _component.Name + "' rows are keyed by " + _component.Key!.Name + ", not " + key.KindName);
            }

            public IReadOnlyList<Value> Iterate() => Guard(() => _world.RowKeyValues(_id, _component));
        }

        private Value EntityAttribute(EntityId id, string name)
        {
            var entity = Record(id);
            if (_schema.TryGetComponent(name, out var component)) return SiblingOrNone(entity, component);

            var candidates = new List<string>();
            foreach (var c in ComponentsOf(entity)) candidates.Add(c.Name);
            foreach (var inverse in entity.Def.Type.Inverses)
            {
                if (inverse.Name != name)
                {
                    candidates.Add(inverse.Name);
                    continue;
                }
                var handle = new MemberHandle(inverse.Source, null, inverse.Member);
                var items = new List<Value>();
                foreach (var source in Inverse(id, handle)) items.Add(new EntityView(this, source));
                return new ListValue(items);
            }
            throw new ScriptRuntimeException(Codes.RunScript,
                id + " (" + entity.Def.Id + ") has no component or collection '" + name + "'", Suggest.Hint(name, candidates));
        }

        private Value SiblingOrNone(EntityRecord entity, ComponentType component)
        {
            // Def가 가진 컴포넌트는 늘 있고, 런타임에 붙는 컴포넌트는 없을 수 있다 (None)
            if (!Has(entity, component)) return NoneValue.Instance;
            var id = new EntityId(entity.Id);
            if (component.IsKeyed) return new RowsView(this, id, component);
            return new ComponentView(this, id, component, null);
        }

        private Value ComponentAttribute(ComponentView view, string name)
        {
            var entity = Record(view.Id);
            var component = view.Component;
            if (component.TryGetMember(name, out var member)) return ToScript(ReadMemberTraced(entity, component, view.Row, member));
            if (name == "entity") return new EntityView(this, view.Id);
            if (name == "key" && view.Row != null)
                return view.Row.Value.IsEntity ? (Value)new EntityView(this, new EntityId(view.Row.Value.EntityKey))
                                               : new StrValue(view.Row.Value.DefKey!);
            if (_schema.TryGetComponent(name, out var sibling)) return SiblingOrNone(entity, sibling);

            var candidates = new List<string> { "entity" };
            if (view.Row != null) candidates.Add("key");
            foreach (var m in component.Members) candidates.Add(m.Name);
            foreach (var c in ComponentsOf(entity)) candidates.Add(c.Name);
            throw new ScriptRuntimeException(Codes.RunScript,
                "'" + component.Name + "' has no member, sibling component or property '" + name + "'",
                Suggest.Hint(name, candidates));
        }

        /// <summary>키로 꼬집은 줄. 줄이 없으면 <c>None</c>이다 (<c>keys="listed"</c>, spec S4).</summary>
        private Value RowOrNone(EntityId id, ComponentType component, RowKey key)
        {
            var entity = Record(id);
            if (!FindRow(entity, component, key, out _, out _)) return NoneValue.Instance;
            return new ComponentView(this, id, component, key);
        }

        private IReadOnlyList<Value> RowKeyValues(EntityId id, ComponentType component)
        {
            var keys = new List<Value>();
            if (component.KeyKind == KeyKind.Def)
            {
                foreach (var key in RowKeys(id, component.Name)) keys.Add(new StrValue(key));
            }
            else
            {
                foreach (var key in RowEntities(id, component.Name)) keys.Add(new EntityView(this, key));
            }
            return keys;
        }
    }
}
