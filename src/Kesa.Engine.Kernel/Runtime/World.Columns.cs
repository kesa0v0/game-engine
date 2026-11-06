using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>호스트의 묶음 읽기·쓰기 (G478). 칸 값은 복사하지 않고 읽기 전용으로 내준다.</summary>
    public sealed partial class World
    {
        /// <summary>
        /// 그 Def 타입에서 <paramref name="members"/>를 Def로 가진 엔티티의 칼럼을 조각으로 읽는다. int·bool은
        /// <see cref="ColumnPart.Longs"/>, fixed는 <see cref="ColumnPart.Fixeds"/>로 받는다. 조각은 Def 등록 순, 조각 안은 id 순이다.
        /// 다음 구조 변경(생성, 파괴, 컴포넌트 붙이기·떼기) 뒤에는 조각을 쓸 수 없다.
        /// </summary>
        public ColumnParts Parts(string typeName, params MemberHandle[] members)
        {
            if (!_schema.TryGetType(typeName, out var type))
            {
                var names = new List<string>();
                foreach (var t in _schema.Types) names.Add(t.Name);
                throw Unknown("there is no Def type '" + typeName + "'", typeName, names);
            }
            foreach (var member in members)
            {
                var component = ColumnComponent(member);
                if (member.Member.Kind != MemberKind.Field || Column.KindOf(member.Member) == CellKind.Value)
                    throw new WorldException(Codes.RunType,
                        "'" + member.Display + "' has no number column; only int, fixed and bool fields can be read as parts",
                        "read it with Get");
                if (_entities.Side(component)?.Attached > 0)
                    throw new WorldException(Codes.RunType,
                        "'" + component.Name + "' is attached at runtime to some entities, and parts do not hold those values",
                        "read them with Get");
            }

            var tables = new List<DefTable>();
            foreach (var def in _defs.OfType(type.Name))
            {
                if (!_layouts.TryGetValue(def, out var layout)) continue;
                var table = layout.Table;
                bool all = true;
                foreach (var member in members)
                    if (table.ColumnOf(member.Member) < 0) all = false;
                if (!all) continue;
                foreach (var member in members) table.ColumnAt(table.ColumnOf(member.Member)); // 도는 중에 배열을 새로 만들지 않게
                tables.Add(table);
            }
            return new ColumnParts(this, tables, members);
        }

        /// <summary>묶음으로 다룰 수 있는 멤버의 컴포넌트.</summary>
        private ComponentType ColumnComponent(MemberHandle member)
        {
            if (member.Component == null)
                throw new WorldException(Codes.RunType, "'" + member.Display + "' is a const of the Def type and has no column");
            var component = NotKeyed(member);
            if (!_schema.TryGetComponent(component.Name, out var own) || !ReferenceEquals(own, component))
                throw new ArgumentException("the handle of '" + member.Display + "' belongs to another load of the content");
            return component;
        }

        private bool PartRowShown(EntityRecord record, MemberHandle[] members)
        {
            if (record.Dead) return false;
            if (_veiled == null && _viewer == null) return true;
            if (HiddenFromViewer(record.Id)) return false;
            foreach (var member in members)
            {
                string name = member.Component!.Name;
                if (Veiled(record.Id, name) || HiddenFromViewer(record.Id, name)) return false;
            }
            return true;
        }

        /// <summary>int나 bool 칸에 묶음으로 쓴다. bool은 0과 1이다. 하나라도 틀리면 아무것도 쓰지 않는다. 조각은 계속 유효하다.</summary>
        public void WriteColumn(MemberHandle member, ReadOnlySpan<long> ids, ReadOnlySpan<long> values)
        {
            NoHostWrites("WriteColumn");
            var kind = Column.KindOf(member.Member);
            if (kind != CellKind.Int && kind != CellKind.Bool)
                throw new WorldException(Codes.RunType, "'" + member.Display + "' is not an int or bool field; " +
                                                        (kind == CellKind.Fixed ? "pass Fixed values" : "write it with Set"));
            WriteNumbers(member, kind, ids, values);
        }

        /// <summary>fixed 칸에 묶음으로 쓴다. 하나라도 틀리면 아무것도 쓰지 않는다. 조각은 계속 유효하다.</summary>
        public void WriteColumn(MemberHandle member, ReadOnlySpan<long> ids, ReadOnlySpan<Fixed> values)
        {
            NoHostWrites("WriteColumn");
            if (Column.KindOf(member.Member) != CellKind.Fixed)
                throw new WorldException(Codes.RunType, "'" + member.Display + "' is not a fixed field");
            WriteNumbers(member, CellKind.Fixed, ids, MemoryMarshal.Cast<Fixed, long>(values));
        }

        private void WriteNumbers(MemberHandle member, CellKind kind, ReadOnlySpan<long> ids, ReadOnlySpan<long> values)
        {
            if (ids.Length != values.Length)
                throw new ArgumentException("WriteColumn got " + ids.Length + " ids but " + values.Length + " values");
            var component = ColumnComponent(member);
            var decl = member.Member;

            // 먼저 다 본다: 틀린 값이 있으면 앞의 것도 쓰지 않는다
            long low = Bound(decl.Spec.Min, kind, false), high = Bound(decl.Spec.Max, kind, true);
            var records = new EntityRecord[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                var entity = Record(new EntityId(ids[i]));
                Require(entity, component);
                if (entity.Layout.Table.ColumnOf(decl) < 0)
                    throw new WorldException(Codes.RunType,
                        new EntityId(ids[i]) + " has '" + component.Name + "' attached at runtime, and WriteColumn does not hold those values",
                        "write it with Set");
                long value = values[i];
                if (kind == CellKind.Bool && value != 0 && value != 1)
                    throw new WorldException(Codes.RunType, "'" + member.Display + "' takes 0 or 1, but got " + value);
                if (value < low)
                    throw new WorldException(Codes.RunRange, "'" + member.Display + "': " + Show(Column.Box(kind, value)) + " is below min " + Show(decl.Spec.Min!),
                        "clamp the value explicitly (spec-gaps G92)");
                if (value > high)
                    throw new WorldException(Codes.RunRange, "'" + member.Display + "': " + Show(Column.Box(kind, value)) + " is above max " + Show(decl.Spec.Max!),
                        "clamp the value explicitly (spec-gaps G92)");
                records[i] = entity;
            }

            if (_undo != null) JournalColumn(decl, records);
            string name = component.Name;
            for (int i = 0; i < records.Length; i++)
            {
                var entity = records[i];
                Touch(entity.Id, name);
                Stamp(entity.Id, name);
                entity.Values.WriteNumber(entity.Layout.Table.ColumnOf(decl), values[i]);
            }
        }

        // 묶음 하나에 되감기 기록 하나: 칸마다 값 객체와 기록을 만들지 않는다
        private void JournalColumn(MemberDecl member, EntityRecord[] records)
        {
            var numbers = new long[records.Length];
            var written = new bool[records.Length];
            for (int i = 0; i < records.Length; i++)
                numbers[i] = records[i].Values.NumberIn(records[i].Layout.Table.ColumnOf(member), out written[i]);
            Journal(() =>
            {
                for (int i = records.Length - 1; i >= 0; i--)
                    records[i].Values.RestoreNumber(records[i].Layout.Table.ColumnOf(member), numbers[i], written[i]);
            });
        }

        // min·max를 칸의 단위로. 칸에 담기지 않는 범위는 long 끝까지 열어 둔다
        private static long Bound(Value? bound, CellKind kind, bool upper)
        {
            if (bound == null || kind == CellKind.Bool) return upper ? long.MaxValue : long.MinValue;
            var scaled = Scaled(bound);
            BigInteger inCell;
            if (kind == CellKind.Fixed) inCell = scaled;
            else
            {
                inCell = BigInteger.DivRem(scaled, Fixed.Scale, out var rest);
                if (!upper && rest.Sign > 0) inCell += 1;
                if (upper && rest.Sign < 0) inCell -= 1;
            }
            if (inCell > long.MaxValue) return long.MaxValue;
            if (inCell < long.MinValue) return long.MinValue;
            return (long)inCell;
        }

        /// <summary>칼럼 조각들. 스택에만 둘 수 있다.</summary>
        public ref struct ColumnParts
        {
            private readonly World _world;
            private readonly List<DefTable> _tables;
            private readonly MemberHandle[] _members;
            private readonly long _structure;
            private int _table;
            private int _row;

            internal ColumnParts(World world, object tables, MemberHandle[] members)
            {
                _world = world;
                _tables = (List<DefTable>)tables;
                _members = members;
                _structure = world._entities.Structure;
                _table = 0;
                _row = 0;
                Current = default;
            }

            public ColumnPart Current { get; private set; }

            public ColumnParts GetEnumerator() => this;

            public bool MoveNext()
            {
                if (_world._entities.Structure != _structure) throw Stale();
                for (; _table < _tables.Count; _table++, _row = 0)
                {
                    var table = _tables[_table];
                    int count = table.Count;
                    bool checkRows = table.DeadRows > 0 || _world._veiled != null || _world._viewer != null;
                    int start = _row;
                    if (checkRows)
                        while (start < count && !_world.PartRowShown(table.RecordAt(start), _members)) start++;
                    if (start >= count) continue;

                    int end = start + 1;
                    if (!checkRows) end = count;
                    else
                        while (end < count && _world.PartRowShown(table.RecordAt(end), _members)) end++;

                    _row = end;
                    if (_world._whenReads != null)
                        for (int row = start; row < end; row++)
                            foreach (var member in _members)
                                _world.Read(table.Ids[row], member.Component!.Name);
                    Current = new ColumnPart(_world, table, _members, start, end - start, _structure);
                    return true;
                }
                return false;
            }

            internal static InvalidOperationException Stale() =>
                new InvalidOperationException("this column part is out of date: an entity or a component was created or removed after it was taken (spec-gaps G478)");
        }

        /// <summary>살아 있는 줄이 이어진 한 구간. 배열은 엔진의 것이고 읽기 전용이다.</summary>
        public readonly ref struct ColumnPart
        {
            private readonly World _world;
            private readonly DefTable _table;
            private readonly MemberHandle[] _members;
            private readonly int _start;
            private readonly long _structure;

            internal ColumnPart(World world, object table, MemberHandle[] members, int start, int count, long structure)
            {
                _world = world;
                _table = (DefTable)table;
                _members = members;
                _start = start;
                Count = count;
                _structure = structure;
            }

            public int Count { get; }

            public ReadOnlySpan<long> Ids
            {
                get
                {
                    Check();
                    return new ReadOnlySpan<long>(_table.Ids, _start, Count);
                }
            }

            /// <summary>int는 그 값, bool은 0과 1.</summary>
            public ReadOnlySpan<long> Longs(MemberHandle member)
            {
                var column = Cells(member);
                if (column.Kind == CellKind.Fixed)
                    throw new ArgumentException("'" + member.Display + "' is a fixed field; read it with Fixeds");
                return new ReadOnlySpan<long>(column.Numbers, _start, Count);
            }

            public ReadOnlySpan<Fixed> Fixeds(MemberHandle member)
            {
                var column = Cells(member);
                if (column.Kind != CellKind.Fixed)
                    throw new ArgumentException("'" + member.Display + "' is not a fixed field; read it with Longs");
                return MemoryMarshal.Cast<long, Fixed>(new ReadOnlySpan<long>(column.Numbers, _start, Count));
            }

            private Column Cells(MemberHandle member)
            {
                Check();
                if (Array.IndexOf(_members, member) < 0)
                    throw new ArgumentException("'" + member.Display + "' was not asked for when these parts were taken");
                return _table.ColumnAt(_table.ColumnOf(member.Member));
            }

            private void Check()
            {
                if (_world._entities.Structure != _structure) throw ColumnParts.Stale();
            }
        }
    }
}
