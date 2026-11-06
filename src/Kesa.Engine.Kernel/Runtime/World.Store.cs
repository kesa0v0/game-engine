using System;
using System.Collections.Generic;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 엔티티 값의 칼럼 저장 (G476): Def마다 표 하나, 런타임에 붙인 컴포넌트는 컴포넌트마다 곁표 하나.
    /// int·fixed·bool·Ref 칸은 숫자 배열로 펴고 나머지는 값 객체를 가리킨다 (G477).
    /// </summary>
    public sealed partial class World
    {
        /// <summary>엔티티들과 그 표. 읽기마다 지나므로 인터페이스 없이 sealed로 둔다 (P6).</summary>
        private sealed class EntityStore
        {
            private const int SweepAtLeast = 64;

            private readonly Dictionary<long, EntityRecord> _records = new Dictionary<long, EntityRecord>();
            private readonly List<DefTable> _tables = new List<DefTable>();
            private SideTable?[] _sides = new SideTable?[8];
            private Dictionary<ComponentType, SideTable>? _otherSides;
            private int _dead;

            public EntityRecord this[long id] => _records[id];
            public bool ContainsKey(long id) => _records.ContainsKey(id);
            public bool TryGetValue(long id, out EntityRecord record) => _records.TryGetValue(id, out record);

            /// <summary>구조가 바뀔 때마다 는다. 묶음 조각이 자기가 아직 유효한지 이것으로 안다 (G478).</summary>
            public long Structure { get; private set; }

            public EntityRecord Create(long id, FrozenDef def, DefLayout layout)
            {
                if (!layout.Table.Listed)
                {
                    layout.Table.Listed = true;
                    _tables.Add(layout.Table);
                }
                Structure++;
                var record = new EntityRecord(this, id, def, layout);
                _records.Add(id, record);
                return record;
            }

            /// <summary>되감기로 지운 엔티티를 돌린다. 값은 줄에 그대로 있다.</summary>
            public void Restore(long id, EntityRecord record)
            {
                _records.Add(id, record);
                record.Dead = false;
                record.Layout.Table.DeadRows--;
                foreach (var component in record.RuntimeComponents) SideToWrite(component).Attached++;
                _dead--;
                Structure++;
            }

            public bool Remove(long id)
            {
                if (!_records.TryGetValue(id, out var record)) return false;
                _records.Remove(id);
                record.Dead = true;
                record.Layout.Table.DeadRows++;
                foreach (var component in record.RuntimeComponents) SideToWrite(component).Attached--;
                _dead++;
                Structure++;
                return true;
            }

            /// <summary>되감을 일이 없을 때만 부른다: 죽은 줄을 순서를 지키며 비운다.</summary>
            public void Sweep()
            {
                if (_dead < SweepAtLeast || _dead < _records.Count) return;
                foreach (var table in _tables) table.Sweep();
                _dead = 0;
                Structure++;
            }

            public void Attached(ComponentType component)
            {
                SideToWrite(component).Attached++;
                Structure++;
            }

            public void Detached(ComponentType component)
            {
                SideToWrite(component).Attached--;
                Structure++;
            }

            public SideTable? Side(ComponentType component)
            {
                int i = component.Index;
                if (i < _sides.Length && _sides[i] is SideTable side && ReferenceEquals(side.Component, component)) return side;
                if (_otherSides != null && _otherSides.TryGetValue(component, out var other)) return other;
                return null;
            }

            public SideTable SideToWrite(ComponentType component)
            {
                var found = Side(component);
                if (found != null) return found;
                var side = new SideTable(component);
                int i = component.Index;
                if (i >= _sides.Length) Array.Resize(ref _sides, Math.Max(i + 1, _sides.Length * 2));
                if (_sides[i] == null) _sides[i] = side;
                else (_otherSides ??= new Dictionary<ComponentType, SideTable>()).Add(component, side);
                return side;
            }
        }

        private enum CellKind : byte
        {
            Value,
            Int,
            Fixed,
            Bool,
            Entity,
        }

        /// <summary>
        /// 칸 한 줄. 숫자 칸의 쓰지 않은 자리는 <c>fill</c>(Def 값이나 기본값)을 담아, 배열을 그대로 내줘도 읽는 값과 같다.
        /// 쓴 값인지는 따로 적는다: 저장은 쓴 값만 남기기 때문이다.
        /// </summary>
        private sealed class Column
        {
            private readonly long _fill;
            private long[]? _numbers;
            private bool[]? _written;
            private Value?[]? _values;

            public Column(CellKind kind, int capacity, long fill)
            {
                Kind = kind;
                _fill = fill;
                if (kind == CellKind.Value)
                {
                    _values = new Value?[capacity];
                    return;
                }
                _numbers = new long[capacity];
                _written = new bool[capacity];
                if (fill != 0) Array.Fill(_numbers, fill);
            }

            public CellKind Kind { get; }

            /// <summary>숫자 칸의 배열. 줄이 늘면 새 배열이 된다.</summary>
            public long[] Numbers => _numbers!;

            public static CellKind KindOf(MemberDecl member)
            {
                if (member.Kind == MemberKind.Ref) return CellKind.Entity;
                if (member.Kind != MemberKind.Field) return CellKind.Value;
                var type = member.Spec.Type;
                if (ReferenceEquals(type, PrimitiveTypeRef.Int)) return CellKind.Int;
                if (ReferenceEquals(type, PrimitiveTypeRef.Fixed)) return CellKind.Fixed;
                if (ReferenceEquals(type, PrimitiveTypeRef.Bool)) return CellKind.Bool;
                return CellKind.Value;
            }

            public static long Number(CellKind kind, Value value)
            {
                switch (kind)
                {
                    case CellKind.Int: return ((IntValue)value).Value;
                    case CellKind.Fixed: return ((FixedValue)value).Value.Raw;
                    case CellKind.Bool: return ((BoolValue)value).Value ? 1 : 0;
                    default: return value is EntityValue entity ? entity.Id.Value : 0;
                }
            }

            public static Value Box(CellKind kind, long number)
            {
                switch (kind)
                {
                    case CellKind.Int: return IntValue.Of(number);
                    case CellKind.Fixed: return new FixedValue(Numerics.Fixed.FromRaw(number));
                    case CellKind.Bool: return number != 0 ? BoolValue.True : BoolValue.False;
                    default: return number == 0 ? NoneValue.Instance : (Value)new EntityValue(new EntityId(number));
                }
            }

            public bool Has(int row) => _values != null ? _values[row] != null : _written![row];

            public Value? Read(int row)
            {
                if (_values != null) return _values[row];
                return _written![row] ? Box(Kind, _numbers![row]) : null;
            }

            public void Write(int row, Value value)
            {
                if (_values != null)
                {
                    _values[row] = value;
                    return;
                }
                _numbers![row] = Number(Kind, value);
                _written![row] = true;
            }

            public void WriteNumber(int row, long number)
            {
                _numbers![row] = number;
                _written![row] = true;
            }

            public void Erase(int row)
            {
                if (_values != null)
                {
                    _values[row] = null;
                    return;
                }
                _numbers![row] = _fill;
                _written![row] = false;
            }

            public void Move(int from, int to)
            {
                if (_values != null)
                {
                    _values[to] = _values[from];
                    return;
                }
                _numbers![to] = _numbers[from];
                _written![to] = _written[from];
            }

            public void Grow(int capacity)
            {
                if (_values != null)
                {
                    Array.Resize(ref _values, capacity);
                    return;
                }
                int old = _numbers!.Length;
                Array.Resize(ref _numbers, capacity);
                Array.Resize(ref _written, capacity);
                if (_fill != 0) Array.Fill(_numbers, _fill, old, capacity - old);
            }
        }

        /// <summary>Def 하나의 엔티티 값. 줄은 뒤에만 붙고 옮기지 않아 살아 있는 줄은 id 순이다.</summary>
        private sealed class DefTable
        {
            private const int FirstCapacity = 4;

            private readonly ComponentType?[] _types;
            private readonly ResolvedComponent?[] _components;
            private readonly int[] _bases;
            private readonly MemberDecl[] _members;
            private readonly Column?[] _columns;
            private EntityRecord[] _records = new EntityRecord[FirstCapacity];
            private long[] _ids = new long[FirstCapacity];

            public DefTable(ComponentType?[] types, ResolvedComponent?[] components)
            {
                _types = types;
                _components = components;
                _bases = new int[types.Length];
                var members = new List<MemberDecl>();
                for (int i = 0; i < types.Length; i++)
                {
                    var type = types[i];
                    if (type == null || type.IsKeyed) continue;
                    _bases[i] = members.Count;
                    members.AddRange(type.Members);
                }
                _members = members.ToArray();
                _columns = new Column?[_members.Length];
            }

            public int Count { get; private set; }
            public bool Listed { get; set; }

            /// <summary>되감기로 돌아올 수 있어 아직 줄을 쥔 죽은 엔티티 수.</summary>
            public int DeadRows { get; set; }

            public long[] Ids => _ids;
            public EntityRecord RecordAt(int row) => _records[row];

            public int Append(EntityRecord record)
            {
                if (Count == _records.Length)
                {
                    int capacity = Count * 2;
                    Array.Resize(ref _records, capacity);
                    Array.Resize(ref _ids, capacity);
                    foreach (var column in _columns) column?.Grow(capacity);
                }
                _records[Count] = record;
                _ids[Count] = record.Id;
                return Count++;
            }

            /// <summary>이 Def 표에 있는 칸의 칼럼 번호. 곁표나 다른 로드의 멤버면 -1.</summary>
            public int ColumnOf(MemberDecl member)
            {
                var owner = member.Owner;
                if (owner == null) return -1;
                int i = owner.Index;
                if (i >= _types.Length || !ReferenceEquals(_types[i], owner) || owner.IsKeyed) return -1;
                return _bases[i] + member.Index;
            }

            public Value? Read(int column, int row) => _columns[column]?.Read(row);
            public bool Has(int column, int row) => _columns[column]?.Has(row) == true;
            public void Write(int column, int row, Value value) => ColumnAt(column).Write(row, value);
            public void Erase(int column, int row) => _columns[column]?.Erase(row);

            public Column ColumnAt(int column)
            {
                var found = _columns[column];
                if (found != null) return found;
                var member = _members[column];
                var kind = Column.KindOf(member);
                return _columns[column] = new Column(kind, _records.Length, kind == CellKind.Value ? 0 : FillOf(member, kind));
            }

            // 쓰지 않은 줄이 읽을 값: 엔티티 값이 없을 때의 차례는 Read와 같다
            private long FillOf(MemberDecl member, CellKind kind)
            {
                var spec = member.Spec;
                Value? value = null;
                var component = _components[member.Owner!.Index];
                if (component != null && component.Members.TryGet(member.Name, out var fromDef)) value = fromDef.Value;
                else if (spec.Default != null && !(spec.Default is FunctionValue)) value = spec.Default;
                return value == null || kind == CellKind.Entity ? 0 : Column.Number(kind, Normalize(spec.Type, value));
            }

            public void Sweep()
            {
                int kept = 0;
                for (int row = 0; row < Count; row++)
                {
                    var record = _records[row];
                    if (record.Dead)
                    {
                        record.Values.Release();
                        continue;
                    }
                    if (kept != row)
                    {
                        _records[kept] = record;
                        _ids[kept] = _ids[row];
                        foreach (var column in _columns) column?.Move(row, kept);
                        record.Values.MoveTo(kept);
                    }
                    kept++;
                }
                for (int row = kept; row < Count; row++)
                {
                    _records[row] = null!;
                    foreach (var column in _columns) column?.Erase(row);
                }
                Count = kept;
                DeadRows = 0;
            }
        }

        /// <summary>
        /// 런타임에 붙인 컴포넌트 하나의 값. 값이 하나라도 있는 엔티티만 줄을 가진다. 뗀 자리를 끝 줄로 메워 순서가 이력을 따르므로
        /// 이 순서를 밖에 보이지 않는다 (G476).
        /// </summary>
        private sealed class SideTable
        {
            private const int FirstCapacity = 4;

            private readonly Dictionary<long, int> _rows = new Dictionary<long, int>();
            private readonly Column?[] _columns;
            private long[] _ids = new long[FirstCapacity];
            private int[] _used = new int[FirstCapacity];
            private int _count;

            public SideTable(ComponentType component)
            {
                Component = component;
                _columns = new Column?[component.Members.Count];
            }

            public ComponentType Component { get; }

            /// <summary>이 컴포넌트를 런타임에 붙이고 있는 살아 있는 엔티티 수.</summary>
            public int Attached { get; set; }

            public Value? Read(long id, int column) =>
                _rows.TryGetValue(id, out int row) ? _columns[column]?.Read(row) : null;

            /// <summary>값을 쓴다. 칸이 비어 있었으면 true.</summary>
            public bool Write(long id, MemberDecl member, Value value)
            {
                if (!_rows.TryGetValue(id, out int row))
                {
                    if (_count == _ids.Length)
                    {
                        int capacity = _count * 2;
                        Array.Resize(ref _ids, capacity);
                        Array.Resize(ref _used, capacity);
                        foreach (var column in _columns) column?.Grow(capacity);
                    }
                    row = _count++;
                    _rows.Add(id, row);
                    _ids[row] = id;
                    _used[row] = 0;
                }
                var cells = _columns[member.Index] ??= new Column(Column.KindOf(member), _ids.Length, 0);
                bool empty = !cells.Has(row);
                cells.Write(row, value);
                if (empty) _used[row]++;
                return empty;
            }

            /// <summary>칸을 비운다. 값이 있었으면 true.</summary>
            public bool Clear(long id, int column)
            {
                if (!_rows.TryGetValue(id, out int row)) return false;
                var cells = _columns[column];
                if (cells == null || !cells.Has(row)) return false;
                cells.Erase(row);
                if (--_used[row] == 0) Drop(id, row);
                return true;
            }

            private void Drop(long id, int row)
            {
                _rows.Remove(id);
                int last = --_count;
                if (row != last)
                {
                    _ids[row] = _ids[last];
                    _used[row] = _used[last];
                    _rows[_ids[row]] = row;
                    foreach (var cells in _columns) cells?.Move(last, row);
                }
                foreach (var cells in _columns) cells?.Erase(last);
            }
        }

        /// <summary>
        /// 엔티티나 줄 하나의 값 칸들 (쓴 값, 생성 순간에 계산한 값). 줄의 값은 아직 칼럼으로 옮기지 않아 사전에 둔다 (G476).
        /// 되감기 기록이 이 객체를 가리키므로 줄이 비워져 옮겨 가도 그대로 맞다.
        /// </summary>
        private sealed class ValueSlots
        {
            public static readonly ValueSlots None = new ValueSlots();

            private Dictionary<MemberDecl, Value>? _own;
            private DefTable? _table;
            private readonly EntityStore? _store;
            private readonly EntityRecord? _record;
            private int _row;
            private int _count;

            public ValueSlots() => _own = new Dictionary<MemberDecl, Value>();

            public ValueSlots(EntityStore store, DefTable table, EntityRecord record)
            {
                _store = store;
                _table = table;
                _record = record;
                _row = table.Append(record);
            }

            public int Count => _own?.Count ?? _count;

            public Value this[MemberDecl member]
            {
                set
                {
                    if (_own != null)
                    {
                        _own[member] = value;
                        return;
                    }
                    int column = _table!.ColumnOf(member);
                    if (column >= 0)
                    {
                        if (!_table.Has(column, _row)) _count++;
                        _table.Write(column, _row, value);
                        return;
                    }
                    if (_store!.SideToWrite(member.Owner!).Write(_record!.Id, member, value)) _count++;
                }
            }

            public bool ContainsKey(MemberDecl member)
            {
                if (_own != null) return _own.ContainsKey(member);
                int column = _table!.ColumnOf(member);
                return column >= 0 ? _table.Has(column, _row) : TryGetValue(member, out _);
            }

            public bool TryGetValue(MemberDecl member, out Value value)
            {
                if (_own != null) return _own.TryGetValue(member, out value);
                int column = _table!.ColumnOf(member);
                var found = column >= 0
                    ? _table.Read(column, _row)
                    : member.Owner == null ? null : _store!.Side(member.Owner)?.Read(_record!.Id, member.Index);
                value = found!;
                return found != null;
            }

            public bool Remove(MemberDecl member)
            {
                if (_own != null) return _own.Remove(member);
                int column = _table!.ColumnOf(member);
                if (column >= 0)
                {
                    if (!_table.Has(column, _row)) return false;
                    _table.Erase(column, _row);
                    _count--;
                    return true;
                }
                if (member.Owner == null || _store!.Side(member.Owner)?.Clear(_record!.Id, member.Index) != true) return false;
                _count--;
                return true;
            }

            // ---- 묶음 쓰기 (G478): 값 객체를 거치지 않는다 ----

            public long NumberIn(int column, out bool written)
            {
                var cells = _table!.ColumnAt(column);
                written = cells.Has(_row);
                return cells.Numbers[_row];
            }

            public void WriteNumber(int column, long number)
            {
                if (!_table!.Has(column, _row)) _count++;
                _table.ColumnAt(column).WriteNumber(_row, number);
            }

            public void RestoreNumber(int column, long number, bool written)
            {
                if (written)
                {
                    WriteNumber(column, number);
                    return;
                }
                if (!_table!.Has(column, _row)) return;
                _table.Erase(column, _row);
                _count--;
            }

            public void MoveTo(int row) => _row = row;

            /// <summary>죽은 엔티티가 줄과 곁표를 놓는다. 그 뒤로는 아무 칸도 없는 빈 값이다.</summary>
            public void Release()
            {
                foreach (var component in _record!.RuntimeComponents)
                    foreach (var member in component.Members)
                        Remove(member);
                _table = null;
                _own = new Dictionary<MemberDecl, Value>();
                _count = 0;
            }
        }
    }
}
