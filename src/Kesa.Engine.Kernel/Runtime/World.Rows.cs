using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>키 컴포넌트의 줄: 공개 API(<c>GetRow</c>·<c>AddRow</c>·<c>RowKeys</c> 등)와 그 구현 (G31·G129).</summary>
    public sealed partial class World
    {
        // ---- 키 컴포넌트의 줄 ----

        public Value GetRow(EntityId id, MemberHandle member, string key) => GetRow(id, member, RowKey.Def(key));
        public Value GetRow(EntityId id, MemberHandle member, EntityId key) => GetRow(id, member, RowKey.Entity(key));
        public void SetRow(EntityId id, MemberHandle member, string key, Value value) => SetRow(id, member, RowKey.Def(key), value);
        public void SetRow(EntityId id, MemberHandle member, EntityId key, Value value) => SetRow(id, member, RowKey.Entity(key), value);
        public bool HasRow(EntityId id, string componentName, string key) => HasRow(id, componentName, RowKey.Def(key));
        public bool HasRow(EntityId id, string componentName, EntityId key) => HasRow(id, componentName, RowKey.Entity(key));

        // key=int 컴포넌트의 줄 (G129)
        public Value GetRow(EntityId id, MemberHandle member, long key) => GetRow(id, member, RowKey.Int(key));
        public void SetRow(EntityId id, MemberHandle member, long key, Value value) => SetRow(id, member, RowKey.Int(key), value);
        public bool HasRow(EntityId id, string componentName, long key) => HasRow(id, componentName, RowKey.Int(key));
        public void AddRow(EntityId id, string componentName, long key, params Init[] inits) =>
            AddRow(id, componentName, RowKey.Int(key), inits);
        public void RemoveRow(EntityId id, string componentName, long key) => RemoveRow(id, componentName, RowKey.Int(key));

        /// <summary><c>key=int</c> 컴포넌트의 줄 키들 (키 순, 시작 시점의 목록, G129).</summary>
        public IReadOnlyList<long> RowInts(EntityId id, string componentName)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), KeyKind.Int);
            Require(entity, component);
            var keys = new List<long>();
            if (entity.Rows.TryGetValue(componentName, out var rows)) keys.AddRange(rows.ByInt.Keys);
            return keys;
        }

        /// <summary><c>keys="listed"</c>나 엔티티 키 컴포넌트에 줄을 붙인다. 줄의 생성 순간이라 G89·G93이 적용된다.</summary>
        public void AddRow(EntityId id, string componentName, string key, params Init[] inits) =>
            AddRow(id, componentName, RowKey.Def(key), inits);

        public void AddRow(EntityId id, string componentName, EntityId key, params Init[] inits) =>
            AddRow(id, componentName, RowKey.Entity(key), inits);

        /// <summary>런타임에 붙인 줄을 뗀다. Def에서 온 줄은 뗄 수 없다 (spec S4).</summary>
        public void RemoveRow(EntityId id, string componentName, string key) => RemoveRow(id, componentName, RowKey.Def(key));
        public void RemoveRow(EntityId id, string componentName, EntityId key) => RemoveRow(id, componentName, RowKey.Entity(key));

        /// <summary>Def 키 컴포넌트의 줄 키들(키 id 순, 시작 시점의 목록). <c>keys="all"</c>이면 키 타입의 모든 구체 Def.</summary>
        public IReadOnlyList<string> RowKeys(EntityId id, string componentName)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), KeyKind.Def);
            Require(entity, component);
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (component.Keys == KeysMode.All)
            {
                foreach (var d in _defs.OfType(component.Key!.Name)) keys.Add(d.Id);
            }
            else
            {
                if (entity.Def.Components.TryGet(componentName, out var rc)) foreach (var k in rc.Rows.Keys) keys.Add(k);
                if (entity.Rows.TryGetValue(componentName, out var rows))
                    foreach (var pair in rows.ByDef)
                        if (pair.Value.Attached) keys.Add(pair.Key);
            }
            return new List<string>(keys);
        }

        /// <summary>엔티티 키 컴포넌트의 줄 키들 (id 순, 시작 시점의 목록).</summary>
        public IReadOnlyList<EntityId> RowEntities(EntityId id, string componentName)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), KeyKind.Entity);
            Require(entity, component);
            var keys = new List<EntityId>();
            if (entity.Rows.TryGetValue(componentName, out var rows))
                foreach (var key in rows.ByEntity.Keys) keys.Add(new EntityId(key));
            return keys;
        }

        // ---- 줄 구현 ----

        private Value GetRow(EntityId id, MemberHandle member, RowKey key)
        {
            var entity = Record(id);
            var component = KeyedMember(member);
            var row = FindRow(entity, component, key, out var defRow, out var record);
            if (!row) throw NoRow(id, component, key);
            if (member.Kind == MemberKind.Derived) return ReadDerivedTop(entity, component, key, member.Member);
            return Read(key, component, member.Member, defRow?.Members,
                record?.Values ?? NoValues);
        }

        private void SetRow(EntityId id, MemberHandle member, RowKey key, Value value)
        {
            NoHostWrites("SetRow");
            var entity = Record(id);
            var component = KeyedMember(member);
            if (!FindRow(entity, component, key, out _, out var record)) throw NoRow(id, component, key);
            var stored = CheckWrite(member.Display, member.Member, value);
            if (record == null)
            {
                // keys="all"의 안 적힌 줄: 처음 쓸 때 기록을 만든다. 되감으면 기록이 사라져 다시 기본값 줄이 된다
                record = new RowRecord(attached: false);
                var rows = entity.RowsOf(component);
                rows.Put(key, record);
                Journal(() => rows.Remove(key));
            }
            Assign(id.Value, component, record.Values, member.Member, stored);
        }

        private bool HasRow(EntityId id, string componentName, RowKey key)
        {
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), key.Kind);
            return Has(entity, component) && FindRow(entity, component, key, out _, out _);
        }

        private void AddRow(EntityId id, string componentName, RowKey key, Init[] inits)
        {
            NoHostWrites("AddRow");
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), key.Kind);
            if (component.KeyKind == KeyKind.Def && component.Keys == KeysMode.All)
                throw new WorldException(Codes.RunNoRow,
                    "'" + componentName + "' is keys=\"all\": every key already has a row, so rows are not added");
            Require(entity, component);
            if (FindRow(entity, component, key, out _, out _))
                throw new WorldException(Codes.RunNoRow, id + " already has a '" + componentName + "' row for " + key);

            var given = Given(inits, h => h.Component == component, "the value does not belong to '" + componentName + "'");
            var values = new Planned();
            PlanMembers(values, component, null, given, "adding the '" + componentName + "' row " + key + " to " + id, key);

            var record = new RowRecord(attached: true);
            values.CopyTo(record.Values);
            var keyedRows = entity.RowsOf(component);
            Touch(id.Value);
            Stamp(id.Value, componentName);
            keyedRows.Put(key, record);
            Journal(() => keyedRows.Remove(key));
            if (key.IsEntity)
            {
                if (!_keyHolders.TryGetValue(key.EntityKey, out var holders))
                    _keyHolders.Add(key.EntityKey, holders = new SortedSet<long>());
                if (holders.Add(id.Value)) Journal(() => holders.Remove(id.Value));
            }
            if (values.Pending.Count == 0) return;
            try
            {
                FinishPending(entity, values.Pending);
            }
            catch
            {
                entity.Rows[componentName].Remove(key);
                if (key.IsEntity && _keyHolders.TryGetValue(key.EntityKey, out var held))
                {
                    held.Remove(id.Value);
                    if (held.Count == 0) _keyHolders.Remove(key.EntityKey);
                }
                throw;
            }
        }

        private void RemoveRow(EntityId id, string componentName, RowKey key)
        {
            NoHostWrites("RemoveRow");
            var entity = Record(id);
            var component = Keyed(ComponentNamed(componentName), key.Kind);
            if (component.KeyKind == KeyKind.Def && component.Keys == KeysMode.All)
                throw new WorldException(Codes.RunNoRow,
                    "'" + componentName + "' is keys=\"all\": every key always has a row, so rows are not removed");
            Require(entity, component);
            if (!FindRow(entity, component, key, out _, out var record)) throw NoRow(id, component, key);
            if (record == null || !record.Attached)
                throw new WorldException(Codes.RunNoRow,
                    "the '" + componentName + "' row " + key + " of " + id + " comes from its Def and cannot be removed");
            var keyedRows = entity.Rows[componentName];
            Touch(id.Value);
            Stamp(id.Value, componentName);
            keyedRows.Remove(key);
            Journal(() => keyedRows.Put(key, record));
            DropContributionsTo(id.Value, component, key);
        }

        /// <summary>
        /// 줄이 있는가. 있으면 Def 쪽 줄(<c>keys="all"</c>의 기본 줄 포함)과 엔티티 쪽 기록을 돌려준다.
        /// 키가 맞지 않는 종류·타입이면 오류다.
        /// </summary>
        private bool FindRow(EntityRecord entity, ComponentType component, RowKey key, out ResolvedRow? defRow,
            out RowRecord? record)
        {
            defRow = null;
            record = null;
            Require(entity, component);
            CheckKey(component, key);
            if (entity.Rows.TryGetValue(component.Name, out var rows)) record = rows.Find(key);
            if (key.Kind != KeyKind.Def) return record != null && record.Attached; // 엔티티·int 키 줄은 런타임에만 있다

            if (entity.Def.Components.Contains(component.Name) && entity.Def.TryGetRow(component.Name, key.DefKey!, out var r))
            {
                defRow = r;
                return true;
            }
            if (component.Keys == KeysMode.All) return true; // 런타임에 붙인 keys="all": 기본 줄
            return record != null && record.Attached;
        }

        private void CheckKey(ComponentType component, RowKey key)
        {
            if (component.KeyKind != key.Kind) throw WrongKey(component, key.Kind);
            if (key.IsInt) return;
            if (key.IsEntity)
            {
                if (!_entities.TryGetValue(key.EntityKey, out var keyEntity))
                    throw new WorldException(Codes.RunDeadEntity, "the key " + key + " does not exist");
                if (keyEntity.Def.Type != component.Key)
                    throw new WorldException(Codes.RunType,
                        "the key " + key + " is a " + keyEntity.Def.Type.Name + ", but '" + component.Name + "' is keyed by " + component.Key!.Name);
                return;
            }
            if (!_defs.TryGet(key.DefKey!, out var keyDef) || keyDef.Type != component.Key)
            {
                var ids = new List<string>();
                foreach (var d in _defs.OfType(component.Key!.Name)) ids.Add(d.Id);
                throw Unknown("there is no " + component.Key!.Name + " '" + key.DefKey + "' to key '" + component.Name + "' by",
                    key.DefKey!, ids);
            }
        }

        private static WorldException NoRow(EntityId id, ComponentType component, RowKey key) =>
            new WorldException(Codes.RunNoRow, id + " has no '" + component.Name + "' row for " + key,
                "add it with AddRow (keys=\"listed\" rows exist only when added)");
    }
}
