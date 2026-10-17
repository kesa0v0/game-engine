using System;
using System.Collections;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 한 프레임의 지역 변수: 이름 공간의 칸 배치(<see cref="FrameLayout"/>)대로 놓인 칸 배열 (G226 ④). 빈 칸(null)은 아직 대입하지 않은 이름이다.
    /// 실행기는 칸 번호로, 저장과 진단은 이름으로 읽는다. 람다가 같은 표를 붙잡으므로(늦게 묶임, G104) 값이 아니라 객체로 다룬다.
    /// 돌면 이름 순(서수)이다: 저장과 진단이 순서에 기대므로 넣은 순서가 밖으로 새지 않게 한다(spec P2).
    /// </summary>
    internal sealed class LocalTable : IReadOnlyDictionary<string, Value>
    {
        /// <summary>
        /// 칸 하나. 값을 구조체로 감싸 배열에 넣는다: 클래스 배열에 넣을 때마다 하는 형 검사(공변 배열 검사)를 피한다 (프로파일에서 보임).
        /// </summary>
        private struct Slot
        {
            public Value? Value;
        }

        private Slot[] _values;
        private int _count;

        /// <summary>이름을 미리 모르는 표: 넣을 때 칸이 는다.</summary>
        public LocalTable() : this(FrameLayout.Open())
        {
        }

        public LocalTable(FrameLayout layout)
        {
            Layout = layout;
            _values = new Slot[Math.Max(layout.Count, 1)];
        }

        public FrameLayout Layout { get; }

        /// <summary>같은 배치·이름·값을 가진 새 표 (값은 불변이라 나눠 쓴다, G104).</summary>
        public LocalTable Clone()
        {
            var copy = new LocalTable(Layout);
            if (copy._values.Length < _values.Length) copy._values = new Slot[_values.Length];
            Array.Copy(_values, copy._values, _values.Length);
            copy._count = _count;
            return copy;
        }

        /// <summary>
        /// 자식 배치(<see cref="FrameLayout.Child"/>)의 새 표에 이 표의 값을 옮긴다 (람다 호출, 컴프리헨션의 이름 공간).
        /// </summary>
        public LocalTable ChildTable(FrameLayout child)
        {
            var table = new LocalTable(child);
            var map = child.ParentSlots!;
            int n = Math.Min(map.Length, _values.Length);
            for (int i = 0; i < n; i++)
            {
                var v = _values[i].Value;
                if (v == null) continue;
                table._values[map[i]].Value = v;
                table._count++;
            }
            return table;
        }

        public int Count => _count;

        // ---- 칸 번호로 (실행기) ----

        /// <summary>칸의 값. 아직 대입하지 않았으면 null.</summary>
        public Value? At(int slot) => slot < _values.Length ? _values[slot].Value : null;

        public void Set(int slot, Value value)
        {
            if (slot >= _values.Length) Array.Resize(ref _values, Math.Max(slot + 1, _values.Length * 2));
            if (_values[slot].Value == null) _count++;
            _values[slot].Value = value;
        }

        /// <summary>칸을 비운다 (대입 전 상태로).</summary>
        public void Unset(int slot)
        {
            if (slot >= _values.Length || _values[slot].Value == null) return;
            _values[slot].Value = null;
            _count--;
        }

        // ---- 이름으로 (저장, 진단, 호스트) ----

        public bool TryGetValue(string name, out Value value)
        {
            int slot = Layout.SlotOf(name);
            var v = slot < 0 ? null : At(slot);
            value = v!;
            return v != null;
        }

        public bool ContainsKey(string name) => TryGetValue(name, out _);

        public Value this[string name]
        {
            get
            {
                if (TryGetValue(name, out var v)) return v;
                throw new KeyNotFoundException("no local '" + name + "'");
            }
            set
            {
                int slot = Layout.SlotOf(name);
                if (slot < 0) slot = Layout.Add(name); // 정해진 배치면 엔진 결함이다: Add가 던진다
                Set(slot, value);
            }
        }

        /// <summary>이 표의 배치에 그 이름의 칸이 있으면 넣고 true. 없으면 넣지 않고 false (불러온 저장의 이름 검사).</summary>
        public bool TrySet(string name, Value value)
        {
            int slot = Layout.SlotOf(name);
            if (slot < 0)
            {
                if (!Layout.IsOpen) return false;
                slot = Layout.Add(name);
            }
            Set(slot, value);
            return true;
        }

        public bool Remove(string name)
        {
            int slot = Layout.SlotOf(name);
            if (slot < 0 || At(slot) == null) return false;
            Unset(slot);
            return true;
        }

        /// <summary>이름 순(서수) 이름 목록.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                var names = new List<string>(_count);
                foreach (var pair in this) names.Add(pair.Key);
                return names;
            }
        }

        public IEnumerable<Value> Values
        {
            get
            {
                var values = new List<Value>(_count);
                foreach (var pair in this) values.Add(pair.Value);
                return values;
            }
        }

        /// <summary>이름 순(서수)으로 돈다. 정해진 배치는 칸 순서가 이름 순이다.</summary>
        public IEnumerator<KeyValuePair<string, Value>> GetEnumerator()
        {
            var pairs = new List<KeyValuePair<string, Value>>(_count);
            int n = Math.Min(Layout.Count, _values.Length);
            for (int i = 0; i < n; i++)
            {
                var v = _values[i].Value;
                if (v != null) pairs.Add(new KeyValuePair<string, Value>(Layout.NameAt(i), v));
            }
            if (Layout.IsOpen) pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return pairs.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
