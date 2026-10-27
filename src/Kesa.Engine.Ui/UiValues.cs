using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>함수 값을 요소 속성의 타입으로 바꾸는 규칙 (spec-gaps G341)과 반복의 키 (G339).</summary>
    internal static class UiValues
    {
        /// <summary>
        /// 모드 언어의 참거짓 판정(G104): <c>None</c>·<c>False</c>·0·빈 글자·빈 리스트·빈 사전은 거짓이고 그 밖은 참이다.
        /// 숨김 값은 아직 정하지 않아 오류다(G341).
        /// </summary>
        public static bool TryTruthy(BridgeValue value, out bool result, out string error)
        {
            error = "";
            switch (value)
            {
                case BridgeNone _: result = false; return true;
                case BridgeBool b: result = b.Value; return true;
                case BridgeInt i: result = i.Value != 0; return true;
                case BridgeFixed f: result = f.Raw != 0; return true;
                case BridgeStr s: result = s.Value.Length > 0; return true;
                case BridgeList l: result = l.Items.Count > 0; return true;
                case BridgeDict d: result = d.Items.Count > 0; return true;
                case BridgeHidden _:
                    result = false;
                    error = "a hidden value cannot be used as true or false";
                    return false;
                default: result = true; return true;
            }
        }

        /// <summary>정수와 고정소수점만 받는다. 고정소수점의 원래 정수값으로 돌려준다.</summary>
        public static bool TryNumber(BridgeValue value, out long raw, out string error)
        {
            error = "";
            switch (value)
            {
                case BridgeFixed f: raw = f.Raw; return true;
                case BridgeInt i:
                    try
                    {
                        raw = checked(i.Value * BridgeFixed.Scale);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        raw = 0;
                        error = "the number " + i.Value + " is too large";
                        return false;
                    }
                default:
                    raw = 0;
                    error = "a " + value.Kind.ToString().ToLowerInvariant() + " value is not a number";
                    return false;
            }
        }

        /// <summary>클래스 이름: 글자 하나, 글자 목록, <c>None</c>(없음).</summary>
        public static bool TryClassNames(BridgeValue value, SortedSet<string> names, out string error)
        {
            error = "";
            switch (value)
            {
                case BridgeNone _:
                    return true;
                case BridgeStr s:
                    if (s.Value.Length > 0) names.Add(s.Value);
                    return true;
                case BridgeList list:
                    foreach (var item in list.Items)
                    {
                        if (!(item is BridgeStr text))
                        {
                            error = "a class name list holds only text, got a " + item.Kind.ToString().ToLowerInvariant();
                            return false;
                        }
                        if (text.Value.Length > 0) names.Add(text.Value);
                    }
                    return true;
                default:
                    error = "class names are text or a list of text, got a " + value.Kind.ToString().ToLowerInvariant();
                    return false;
            }
        }

        /// <summary>
        /// 반복 항목의 키 (G339): 엔티티는 id, 글자·정수·참거짓은 값, 그 밖은 위치. 같은 값이 겹치면 <paramref name="seen"/>으로 n번째를 센다.
        /// </summary>
        public static string RepeatKey(BridgeValue item, int position, SortedDictionary<string, int> seen)
        {
            string key;
            switch (item)
            {
                case BridgeEntity e: key = "e" + e.Id; break;
                case BridgeStr s: key = "s" + s.Value; break;
                case BridgeInt i: key = "i" + i.Value; break;
                case BridgeBool b: key = b.Value ? "bT" : "bF"; break;
                default: key = "p" + position; break;
            }
            seen.TryGetValue(key, out int count);
            seen[key] = count + 1;
            return count == 0 ? key : key + "#" + count;
        }
    }

    /// <summary>변수의 이름 범위 (G330): 안쪽은 바깥 변수도 본다. 값은 갱신 때 바뀔 수 있다.</summary>
    internal sealed class UiScope
    {
        private readonly Dictionary<string, BridgeValue> _values = new Dictionary<string, BridgeValue>(StringComparer.Ordinal);

        public UiScope(UiScope? parent)
        {
            Parent = parent;
        }

        public UiScope? Parent { get; }

        public void Set(string name, BridgeValue value) => _values[name] = value;

        public bool TryGet(string name, out BridgeValue value)
        {
            for (var scope = this; scope != null; scope = scope.Parent)
                if (scope._values.TryGetValue(name, out value!)) return true;
            value = BridgeNone.Instance;
            return false;
        }
    }
}
