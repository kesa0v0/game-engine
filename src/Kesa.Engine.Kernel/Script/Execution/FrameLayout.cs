using System;
using System.Collections.Generic;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 한 이름 공간(<c>def</c> 본문, 람다 본문, 컴프리헨션)의 지역 변수 칸 배치 (G226 ①④). 이름은 서수 순이고 칸 번호는 그 순서라, 칸 순서로
    /// 돌면 이름 순이다(저장과 진단이 순서에 기댄다, spec P2). 같은 이름 집합이면 칸 번호가 같다.
    /// <para>
    /// 람다와 컴프리헨션의 배치는 바깥 배치에서 나온다(바깥 이름 + 매개변수·반복 변수): 바깥 배치가 자식 배치를 AST 자리마다 기억하고,
    /// 바깥 칸이 자식의 몇 번 칸인지(<see cref="ParentSlots"/>)도 함께 든다. 이름을 미리 모르는 표(<see cref="Open"/>)는 넣을 때 칸을 늘린다.
    /// </para>
    /// </summary>
    internal sealed class FrameLayout
    {
        private readonly List<string> _names;
        private readonly Dictionary<string, int> _index;
        private Dictionary<object, FrameLayout>? _children;

        private FrameLayout(List<string> names, bool open)
        {
            _names = names;
            IsOpen = open;
            _index = new Dictionary<string, int>(names.Count, StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++) _index.Add(names[i], i);
        }

        /// <summary>정해진 이름 집합의 배치. 서수 순으로 칸을 준다.</summary>
        public FrameLayout(IEnumerable<string> names) : this(Sorted(names), false)
        {
        }

        /// <summary>이름을 넣을 때마다 칸이 느는 배치 (이름을 미리 모르는 표). 칸 순서는 넣은 순서다.</summary>
        public static FrameLayout Open() => new FrameLayout(new List<string>(), true);

        private static List<string> Sorted(IEnumerable<string> names)
        {
            var list = new List<string>(new SortedSet<string>(names, StringComparer.Ordinal));
            return list;
        }

        /// <summary>넣을 때 칸이 느는 배치인가 (칸 순서가 이름 순이 아니다).</summary>
        public bool IsOpen { get; }

        public int Count => _names.Count;

        public string NameAt(int slot) => _names[slot];

        /// <summary>이름의 칸 번호. 없으면 -1.</summary>
        public int SlotOf(string name) => _index.TryGetValue(name, out int slot) ? slot : -1;

        public bool Contains(string name) => _index.ContainsKey(name);

        /// <summary>이름을 서수 순으로.</summary>
        public SortedSet<string> Names() => new SortedSet<string>(_names, StringComparer.Ordinal);

        /// <summary>열린 배치에 이름을 더하고 칸 번호를 돌려준다.</summary>
        public int Add(string name)
        {
            if (!IsOpen) throw new InvalidOperationException("engine: a fixed layout has no local '" + name + "'");
            int slot = _names.Count;
            _names.Add(name);
            _index.Add(name, slot);
            return slot;
        }

        /// <summary>
        /// 함수(<c>def</c>나 람다)의 배치면 매개변수마다(선언 순) 칸 번호. 부를 때 인자를 이름을 찾지 않고 곧장 넣는다 (G226 ④, 호출 규약).
        /// </summary>
        public int[]? ParameterSlots { get; private set; }

        /// <summary>이 배치를 함수 배치로 쓴다: 매개변수의 칸 번호를 한 번 정해 둔다.</summary>
        public int[] SlotsOf(IReadOnlyList<Parameter> parameters)
        {
            if (ParameterSlots != null) return ParameterSlots;
            var slots = new int[parameters.Count];
            for (int i = 0; i < slots.Length; i++) slots[i] = SlotOf(parameters[i].Name);
            ParameterSlots = slots;
            return slots;
        }

        /// <summary>
        /// 이 배치의 칸이 자식 배치의 몇 번 칸인가 (자식이면). 자식 표를 만들 때 바깥 값을 옮긴다.
        /// </summary>
        public int[]? ParentSlots { get; private set; }

        private Dictionary<object, object>? _code;

        /// <summary>이 배치에서 풀어 둔 문장 목록의 실행 노드 (목록 객체로 찾는다, G226).</summary>
        public bool TryCode(object body, out object code)
        {
            if (_code != null && _code.TryGetValue(body, out code!)) return true;
            code = null!;
            return false;
        }

        public void SetCode(object body, object code) => (_code ??= new Dictionary<object, object>())[body] = code;

        /// <summary>
        /// 이 배치가 <c>def</c>의 배치면 그 본문의 실행 노드 (부르기마다 목록으로 찾지 않는다). 배치는 함수마다 하나다.
        /// </summary>
        public object? FunctionCode { get; set; }

        /// <summary>이미 만든 자식 배치 (이름 목록을 만들지 않고 찾는다).</summary>
        public bool TryChild(object site, out FrameLayout child)
        {
            if (_children != null && _children.TryGetValue(site, out child!)) return true;
            child = null!;
            return false;
        }

        /// <summary>
        /// 이 이름 공간 안의 AST 자리(<paramref name="site"/>, 람다나 컴프리헨션)가 만드는 자식 배치: 이 배치의 이름에 <paramref name="extra"/>를 더한 것.
        /// 자리마다 한 번 만든다.
        /// </summary>
        public FrameLayout Child(object site, IReadOnlyList<string> extra)
        {
            if (TryChild(site, out var known)) return known;
            _children ??= new Dictionary<object, FrameLayout>(); // AST 노드는 같음을 덮어쓰지 않는다: 객체로 찾는다
            var names = new List<string>(_names);
            names.AddRange(extra);
            var child = new FrameLayout(names);
            var map = new int[_names.Count];
            for (int i = 0; i < map.Length; i++) map[i] = child.SlotOf(_names[i]);
            child.ParentSlots = map;
            _children.Add(site, child);
            return child;
        }
    }
}
