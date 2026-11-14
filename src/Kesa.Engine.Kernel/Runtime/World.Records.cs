using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 월드의 내부 저장 구조: 엔티티·줄 기록, 줄 키, Def별 컴포넌트 배치. 저장 방식은 약속이 아니다(S4).
    /// 이 타입들은 일부러 <see cref="World"/>의 private 안쪽 타입으로 둔다: 바깥으로 빼면 커널 어디서나 저장 구조를 만질 수 있게 되고,
    /// 값의 저장 방식은 <see cref="ValueSlots"/>와 <see cref="EntityStore"/> 안에만 둔다(G475).
    /// </summary>
    public sealed partial class World
    {
        // ---- 저장 구조 ----

        /// <summary>함수 기본값이나 <c>derived</c>를 원천으로 하는 <c>default_from</c>: 생성 순간에 엔티티를 만든 뒤 계산한다 (G89·G106).</summary>
        private sealed class PendingDefault
        {
            public PendingDefault(ComponentType component, RowKey? row, MemberDecl member, FunctionValue? function,
                MemberDecl? source, string where)
            {
                Component = component;
                Row = row;
                Member = member;
                Function = function;
                Source = source;
                Where = where;
            }

            public ComponentType Component { get; }
            public RowKey? Row { get; }
            public MemberDecl Member { get; }
            public FunctionValue? Function { get; }
            public MemberDecl? Source { get; }
            public string Where { get; }
        }

        /// <summary>생성 순간에 정한 값들 (멤버 선언 순서). 검사가 다 끝난 뒤에 월드로 옮긴다.</summary>
        private sealed class Planned
        {
            private readonly List<KeyValuePair<MemberDecl, Value>> _items = new List<KeyValuePair<MemberDecl, Value>>();

            public int Count => _items.Count;

            /// <summary>엔티티가 있어야 계산되는 기본값 (선언 순서).</summary>
            public List<PendingDefault> Pending { get; } = new List<PendingDefault>();

            public void Add(MemberDecl member, Value value) => _items.Add(new KeyValuePair<MemberDecl, Value>(member, value));

            public void CopyTo(ValueSlots target)
            {
                foreach (var pair in _items) target[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// Def 하나의 컴포넌트를 컴포넌트 번호(<see cref="ComponentType.Index"/>)로 바로 찾는 표. 읽기·쓰기마다 "이 Def가 이 컴포넌트를
        /// 가졌나"를 이름 해시로 찾지 않는다(규모 벤치). Def마다 월드가 한 번 만들어 그 Def의 엔티티들이 나눠 쓴다.
        /// </summary>
        private sealed class DefLayout
        {
            private readonly ComponentType?[] _types;
            private readonly ResolvedComponent?[] _components;
            private readonly FrozenDef _def;

            public DefLayout(FrozenDef def, SchemaSet schema)
            {
                _def = def;
                _types = new ComponentType?[schema.Components.Count];
                _components = new ResolvedComponent?[_types.Length];
                foreach (var name in def.Components.Keys)
                {
                    if (!schema.TryGetComponent(name, out var type)) continue;
                    def.Components.TryGet(name, out var rc);
                    _types[type.Index] = type;
                    _components[type.Index] = rc;
                }
                Table = new DefTable(_types, _components);
            }

            /// <summary>이 Def의 엔티티 값을 담는 표.</summary>
            public DefTable Table { get; }

            /// <summary>이 Def의 그 컴포넌트, 없으면 null. 다른 로드의 컴포넌트 타입이면(번호가 같아도 다른 객체) 이름으로 찾는다.</summary>
            public ResolvedComponent? Find(ComponentType component)
            {
                int i = component.Index;
                if (i < _types.Length && ReferenceEquals(_types[i], component)) return _components[i];
                return _def.Components.TryGet(component.Name, out var rc) ? rc : null;
            }
        }

        private readonly Dictionary<FrozenDef, DefLayout> _layouts = new Dictionary<FrozenDef, DefLayout>();

        private DefLayout LayoutOf(FrozenDef def)
        {
            if (!_layouts.TryGetValue(def, out var layout)) _layouts.Add(def, layout = new DefLayout(def, _schema));
            return layout;
        }

        private sealed class EntityRecord
        {
            private readonly SortedDictionary<string, ComponentType> _runtime =
                new SortedDictionary<string, ComponentType>(StringComparer.Ordinal);

            private readonly SortedDictionary<string, KeyedRows> _rows = new SortedDictionary<string, KeyedRows>(StringComparer.Ordinal);

            private readonly EntityStore _store;

            public EntityRecord(EntityStore store, long id, FrozenDef def, DefLayout layout)
            {
                _store = store;
                Id = id;
                Def = def;
                Layout = layout;
                Values = new ValueSlots(store, layout.Table, this);
            }

            /// <summary>지워졌으나 되감기로 돌아올 수 있어 줄을 아직 쥐고 있다.</summary>
            public bool Dead { get; set; }

            public long Id { get; }
            public FrozenDef Def { get; }

            /// <summary>Def의 컴포넌트를 번호로 찾는 표 (같은 Def의 엔티티가 나눠 씀).</summary>
            public DefLayout Layout { get; }

            /// <summary>키 없는 컴포넌트의 엔티티 값.</summary>
            public ValueSlots Values { get; }

            // ---- 런타임에 붙인 컴포넌트 (이름 순) ----

            public int RuntimeCount => _runtime.Count;
            public IEnumerable<string> RuntimeNames => _runtime.Keys;
            public IEnumerable<ComponentType> RuntimeComponents => _runtime.Values;
            public bool IsRuntime(string name) => _runtime.Count > 0 && _runtime.ContainsKey(name);

            public void AttachRuntime(ComponentType component)
            {
                _runtime.Add(component.Name, component);
                _store.Attached(Id, component);
            }

            public bool DetachRuntime(string name)
            {
                if (!_runtime.TryGetValue(name, out var component)) return false;
                _runtime.Remove(name);
                _store.Detached(Id, component);
                return true;
            }

            // ---- 키 컴포넌트별 줄 기록 (이름 순) ----

            public IEnumerable<KeyValuePair<string, KeyedRows>> AllRows => _rows;
            public IEnumerable<KeyedRows> AllRowGroups => _rows.Values;
            public KeyedRows RowsNamed(string name) => _rows[name];
            public bool TryGetRows(string name, out KeyedRows rows) => _rows.TryGetValue(name, out rows);
            public void PutRows(string name, KeyedRows rows) => _rows.Add(name, rows);
            public bool DropRows(string name) => _rows.Remove(name);

            public KeyedRows RowsOf(ComponentType component)
            {
                if (!_rows.TryGetValue(component.Name, out var rows)) _rows.Add(component.Name, rows = new KeyedRows());
                return rows;
            }

            // ---- 스크립트가 보는 컴포넌트 뷰 ----

            private ComponentView[]? _views;
            private int _viewCount;

            /// <summary>
            /// <c>u.Health</c>의 뷰. 뷰는 (월드, id, 컴포넌트)만 들고 읽을 때마다 지금 상태를 보므로, 떼고 다시 붙여도 같은 것을 다시 준다.
            /// 스크립트가 컴포넌트에 닿을 때마다 새로 만들면 틱의 할당에서 가장 큰 몫이었다.
            /// </summary>
            public ComponentView View(World world, ComponentType component)
            {
                for (int i = 0; i < _viewCount; i++)
                    if (ReferenceEquals(_views![i].Component, component)) return _views[i];
                var view = new ComponentView(world, new EntityId(Id), component, null);
                if (_views == null) _views = new ComponentView[4];
                else if (_viewCount == _views.Length) Array.Resize(ref _views, _viewCount * 2);
                _views[_viewCount++] = view;
                return view;
            }
        }

        private sealed class KeyedRows
        {
            public SortedDictionary<string, RowRecord> ByDef { get; } = new SortedDictionary<string, RowRecord>(StringComparer.Ordinal);
            public SortedDictionary<long, RowRecord> ByEntity { get; } = new SortedDictionary<long, RowRecord>();

            /// <summary><c>key=int</c> 컴포넌트의 줄 (키 순, G129).</summary>
            public SortedDictionary<long, RowRecord> ByInt { get; } = new SortedDictionary<long, RowRecord>();

            public RowRecord? Find(RowKey key)
            {
                RowRecord? record;
                if (key.IsEntity) ByEntity.TryGetValue(key.Number, out record);
                else if (key.IsInt) ByInt.TryGetValue(key.Number, out record);
                else ByDef.TryGetValue(key.DefKey!, out record);
                return record;
            }

            public void Put(RowKey key, RowRecord record)
            {
                if (key.IsEntity) ByEntity[key.Number] = record;
                else if (key.IsInt) ByInt[key.Number] = record;
                else ByDef[key.DefKey!] = record;
            }

            public void Remove(RowKey key)
            {
                if (key.IsEntity) ByEntity.Remove(key.Number);
                else if (key.IsInt) ByInt.Remove(key.Number);
                else ByDef.Remove(key.DefKey!);
            }
        }

        /// <summary>줄 하나의 엔티티 쪽 기록. <see cref="Attached"/>면 런타임에 붙인 줄(뗄 수 있음).</summary>
        private sealed class RowRecord
        {
            public RowRecord(bool attached) => Attached = attached;
            public bool Attached { get; }
            public ValueSlots Values { get; } = new ValueSlots();
        }

        private readonly struct RowKey
        {
            private RowKey(KeyKind kind, string? defKey, long number)
            {
                Kind = kind;
                DefKey = defKey;
                Number = number;
            }

            public static RowKey Def(string key) => new RowKey(KeyKind.Def, key, 0);
            public static RowKey Entity(EntityId key) => new RowKey(KeyKind.Entity, null, key.Value);
            public static RowKey Int(long key) => new RowKey(KeyKind.Int, null, key);

            public KeyKind Kind { get; }
            public string? DefKey { get; }

            /// <summary>엔티티 키의 엔티티 id, 또는 int 키.</summary>
            public long Number { get; }

            public long EntityKey => Number;
            public bool IsEntity => Kind == KeyKind.Entity;
            public bool IsInt => Kind == KeyKind.Int;

            public bool Same(RowKey other) => Kind == other.Kind && Number == other.Number && DefKey == other.DefKey;

            public override string ToString() =>
                Kind == KeyKind.Def ? "'" + DefKey + "'"
                : Kind == KeyKind.Entity ? new EntityId(Number).ToString()
                : Number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
