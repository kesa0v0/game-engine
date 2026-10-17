using System;
using System.Collections;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 한 프레임의 지역 변수 (이름 → 값). 함수의 지역 변수는 몇 개뿐이라 이름과 값을 나란히 둔 배열을 앞에서부터 훑는다: 순서 있는 트리 사전보다
    /// 찾기·넣기가 싸고 넣을 때마다 노드를 할당하지 않는다(규모 벤치). 람다가 같은 표를 붙잡으므로(늦게 묶임, G104) 값이 아니라 객체로 다룬다.
    /// 돌면 이름 순(서수)이다: 저장과 진단이 순서에 기대므로 넣은 순서가 밖으로 새지 않게 한다(spec P2).
    /// </summary>
    internal sealed class LocalTable : IReadOnlyDictionary<string, Value>
    {
        private string[] _names;
        private Value[] _values;
        private int _count;

        public LocalTable(int capacity = 4)
        {
            _names = new string[Math.Max(capacity, 1)];
            _values = new Value[_names.Length];
        }

        /// <summary>같은 이름과 값을 가진 새 표 (값은 불변이라 나눠 쓴다, G104).</summary>
        public LocalTable Clone()
        {
            var copy = new LocalTable(_count);
            Array.Copy(_names, copy._names, _count);
            Array.Copy(_values, copy._values, _count);
            copy._count = _count;
            return copy;
        }

        public int Count => _count;

        private int IndexOf(string name)
        {
            for (int i = 0; i < _count; i++)
                if (string.Equals(_names[i], name)) return i;
            return -1;
        }

        public bool TryGetValue(string name, out Value value)
        {
            int i = IndexOf(name);
            if (i < 0)
            {
                value = null!;
                return false;
            }
            value = _values[i];
            return true;
        }

        public bool ContainsKey(string name) => IndexOf(name) >= 0;

        public Value this[string name]
        {
            get
            {
                int i = IndexOf(name);
                if (i < 0) throw new KeyNotFoundException("no local '" + name + "'");
                return _values[i];
            }
            set
            {
                int i = IndexOf(name);
                if (i >= 0)
                {
                    _values[i] = value;
                    return;
                }
                if (_count == _names.Length)
                {
                    Array.Resize(ref _names, _count * 2);
                    Array.Resize(ref _values, _count * 2);
                }
                _names[_count] = name;
                _values[_count] = value;
                _count++;
            }
        }

        public bool Remove(string name)
        {
            int i = IndexOf(name);
            if (i < 0) return false;
            _count--;
            _names[i] = _names[_count];
            _values[i] = _values[_count];
            _names[_count] = null!;
            _values[_count] = null!;
            return true;
        }

        /// <summary>다른 표의 이름과 값을 모두 넣는다 (순서는 상관없다: 찾기만 한다).</summary>
        public void SetAll(IReadOnlyDictionary<string, Value> from)
        {
            if (from is LocalTable table)
            {
                for (int i = 0; i < table._count; i++) this[table._names[i]] = table._values[i];
                return;
            }
            foreach (var pair in from) this[pair.Key] = pair.Value;
        }

        /// <summary>이름 순(서수) 이름 목록.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                var names = new string[_count];
                Array.Copy(_names, names, _count);
                Array.Sort(names, StringComparer.Ordinal);
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

        /// <summary>이름 순(서수)으로 돈다.</summary>
        public IEnumerator<KeyValuePair<string, Value>> GetEnumerator()
        {
            var pairs = new List<KeyValuePair<string, Value>>(_count);
            for (int i = 0; i < _count; i++) pairs.Add(new KeyValuePair<string, Value>(_names[i], _values[i]));
            pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return pairs.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
