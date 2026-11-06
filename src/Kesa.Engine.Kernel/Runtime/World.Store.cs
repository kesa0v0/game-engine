using System.Collections.Generic;
using Kesa.Engine.Loading;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 엔티티 값의 칼럼 저장 (G476): Def마다 표 하나, 런타임에 붙인 컴포넌트는 컴포넌트마다 곁표 하나.
    /// 칸이 null이면 쓴 적 없다는 뜻이라 읽는 쪽이 Def 값과 기본값으로 간다.
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

            public EntityRecord Create(long id, FrozenDef def, DefLayout layout)
            {
                if (!layout.Table.Listed)
                {
                    layout.Table.Listed = true;
                    _tables.Add(layout.Table);
                }
                var record = new EntityRecord(this, id, def, layout);
                _records.Add(id, record);
                return record;
            }

            /// <summary>되감기로 지운 엔티티를 돌린다. 값은 줄에 그대로 있다.</summary>
            public void Restore(long id, EntityRecord record)
            {
                _records.Add(id, record);
                record.Dead = false;
                _dead--;
            }

            public bool Remove(long id)
            {
                if (!_records.TryGetValue(id, out var record)) return false;
                _records.Remove(id);
                record.Dead = true;
                _dead++;
                return true;
            }

            /// <summary>되감을 일이 없을 때만 부른다: 죽은 줄을 순서를 지키며 비운다.</summary>
            public void Sweep()
            {
                if (_dead < SweepAtLeast || _dead < _records.Count) return;
                foreach (var table in _tables) table.Sweep();
                _dead = 0;
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
                if (i >= _sides.Length) System.Array.Resize(ref _sides, System.Math.Max(i + 1, _sides.Length * 2));
                if (_sides[i] == null) _sides[i] = side;
                else (_otherSides ??= new Dictionary<ComponentType, SideTable>()).Add(component, side);
                return side;
            }
        }

        /// <summary>Def 하나의 엔티티 값. 줄은 뒤에만 붙고 옮기지 않아 살아 있는 줄은 id 순이다.</summary>
        private sealed class DefTable
        {
            private const int FirstCapacity = 4;

            private readonly ComponentType?[] _types;
            private readonly int[] _bases;
            private readonly Value?[]?[] _columns;
            private EntityRecord[] _records = new EntityRecord[FirstCapacity];

            public DefTable(ComponentType?[] types)
            {
                _types = types;
                _bases = new int[types.Length];
                int columns = 0;
                for (int i = 0; i < types.Length; i++)
                {
                    var type = types[i];
                    if (type == null || type.IsKeyed) continue;
                    _bases[i] = columns;
                    columns += type.Members.Count;
                }
                _columns = new Value?[columns][];
            }

            public int Count { get; private set; }
            public bool Listed { get; set; }

            public int Append(EntityRecord record)
            {
                if (Count == _records.Length)
                {
                    int capacity = Count * 2;
                    System.Array.Resize(ref _records, capacity);
                    for (int i = 0; i < _columns.Length; i++)
                        if (_columns[i] != null) System.Array.Resize(ref _columns[i], capacity);
                }
                _records[Count] = record;
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

            public Value? Read(int column, int row) => _columns[column]?[row];

            public void Write(int column, int row, Value? value)
            {
                var cells = _columns[column];
                if (cells == null)
                {
                    if (value == null) return;
                    _columns[column] = cells = new Value?[_records.Length];
                }
                cells[row] = value;
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
                        foreach (var cells in _columns)
                            if (cells != null) cells[kept] = cells[row];
                        record.Values.MoveTo(kept);
                    }
                    kept++;
                }
                for (int row = kept; row < Count; row++)
                {
                    _records[row] = null!;
                    foreach (var cells in _columns)
                        if (cells != null) cells[row] = null;
                }
                Count = kept;
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
            private readonly Value?[]?[] _columns;
            private long[] _ids = new long[FirstCapacity];
            private int[] _used = new int[FirstCapacity];
            private int _count;

            public SideTable(ComponentType component)
            {
                Component = component;
                _columns = new Value?[component.Members.Count][];
            }

            public ComponentType Component { get; }

            public Value? Read(long id, int column) =>
                _rows.TryGetValue(id, out int row) ? _columns[column]?[row] : null;

            /// <summary>값을 쓴다. 칸이 비어 있었으면 true.</summary>
            public bool Write(long id, int column, Value value)
            {
                if (!_rows.TryGetValue(id, out int row))
                {
                    if (_count == _ids.Length)
                    {
                        int capacity = _count * 2;
                        System.Array.Resize(ref _ids, capacity);
                        System.Array.Resize(ref _used, capacity);
                        for (int i = 0; i < _columns.Length; i++)
                            if (_columns[i] != null) System.Array.Resize(ref _columns[i], capacity);
                    }
                    row = _count++;
                    _rows.Add(id, row);
                    _ids[row] = id;
                    _used[row] = 0;
                }
                var cells = _columns[column] ??= new Value?[_ids.Length];
                bool empty = cells[row] == null;
                cells[row] = value;
                if (empty) _used[row]++;
                return empty;
            }

            /// <summary>칸을 비운다. 값이 있었으면 true.</summary>
            public bool Clear(long id, int column)
            {
                if (!_rows.TryGetValue(id, out int row)) return false;
                var cells = _columns[column];
                if (cells == null || cells[row] == null) return false;
                cells[row] = null;
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
                    foreach (var cells in _columns)
                        if (cells != null) cells[row] = cells[last];
                }
                foreach (var cells in _columns)
                    if (cells != null) cells[last] = null;
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
                        if (_table.Read(column, _row) == null) _count++;
                        _table.Write(column, _row, value);
                        return;
                    }
                    if (_store!.SideToWrite(member.Owner!).Write(_record!.Id, member.Index, value)) _count++;
                }
            }

            public bool ContainsKey(MemberDecl member) => TryGetValue(member, out _);

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
                    if (_table.Read(column, _row) == null) return false;
                    _table.Write(column, _row, null);
                    _count--;
                    return true;
                }
                if (member.Owner == null || _store!.Side(member.Owner)?.Clear(_record!.Id, member.Index) != true) return false;
                _count--;
                return true;
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
