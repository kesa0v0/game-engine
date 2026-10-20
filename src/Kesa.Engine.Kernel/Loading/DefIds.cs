using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    /// <summary>
    /// Def id의 이름 공간 (spec-gaps G258~G261·G264): 전체 이름은 <c>모드id:경로</c>이고 경로 안의 하위 이름 공간은 <c>/</c>로 나눈다
    /// (<c>harbor:rigs/Brig</c>). 조각은 ASCII 식별자다(G259). 스크립트 안에서 <c>:</c> 없이 쓴 이름은 그 코드가 속한 모드 기준이고,
    /// 엔진 바깥(명령 인자·호스트 API·저장)은 전체 이름만 받는다.
    /// </summary>
    public static class DefIds
    {
        public const char ModSeparator = ':';
        public const char PathSeparator = '/';

        public static bool IsFull(string id) => id.IndexOf(ModSeparator) >= 0;

        /// <summary><paramref name="mod"/>의 코드가 쓴 이름을 전체 이름으로. 이미 <c>:</c>가 있으면 그대로다.</summary>
        public static string Resolve(string id, string mod) => IsFull(id) ? id : mod + ModSeparator + id;

        /// <summary>전체 이름의 모드 id. <c>:</c>가 없으면 빈 문자열.</summary>
        public static string ModOf(string id)
        {
            int i = id.IndexOf(ModSeparator);
            return i < 0 ? "" : id.Substring(0, i);
        }

        /// <summary>전체 이름의 경로(<c>:</c> 뒤). <c>:</c>가 없으면 그대로.</summary>
        public static string PathOf(string id)
        {
            int i = id.IndexOf(ModSeparator);
            return i < 0 ? id : id.Substring(i + 1);
        }

        /// <summary>
        /// 이름의 글자가 틀렸으면 그 까닭, 맞으면 null. <c>:</c>는 한 번까지이고 앞은 모드 id(<c>[a-z][a-z0-9_]*</c>, G2),
        /// 경로는 <c>/</c>로 나눈 ASCII 식별자들이다.
        /// </summary>
        public static string? Problem(string id)
        {
            if (id.Length == 0) return "a Def id must not be empty";
            int colon = id.IndexOf(ModSeparator);
            string path = id;
            if (colon >= 0)
            {
                if (id.IndexOf(ModSeparator, colon + 1) >= 0) return "a Def id has at most one ':' (mod:path)";
                string mod = id.Substring(0, colon);
                if (!IsModId(mod)) return "'" + mod + "' before ':' is not a mod id ([a-z][a-z0-9_]*)";
                path = id.Substring(colon + 1);
            }
            foreach (var segment in path.Split(PathSeparator))
            {
                if (segment.Length == 0) return "a Def id has an empty part between '/'";
                if (!PatchPath.IsIdentifier(segment))
                    return "'" + segment + "' is not a valid part of a Def id: use letters, digits and _, not starting with a digit";
            }
            return null;
        }

        private static bool IsModId(string text)
        {
            if (text.Length == 0 || text[0] < 'a' || text[0] > 'z') return false;
            foreach (var c in text)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        /// <summary>
        /// 없는 id의 "혹시 …?": 같은 경로를 가진 다른 모드의 id가 있으면 그것(<c>Sloop</c> → <c>core:Sloop</c>), 아니면 전체 이름의
        /// 편집 거리, 그래도 없으면 경로끼리의 편집 거리(<c>compat:Wod</c> → <c>core:Wood</c>).
        /// </summary>
        public static string? Hint(string id, IEnumerable<string> candidates)
        {
            string path = PathOf(id);
            string? samePath = null;
            var all = new List<string>();
            foreach (var c in candidates)
            {
                all.Add(c);
                if (c != id && PathOf(c) == path && (samePath == null || string.CompareOrdinal(c, samePath) < 0)) samePath = c;
            }
            string? best = samePath ?? Suggest.Closest(id, all);
            if (best == null)
            {
                var byPath = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var c in all)
                    if (!byPath.ContainsKey(PathOf(c))) byPath.Add(PathOf(c), c);
                var closest = Suggest.Closest(path, byPath.Keys);
                if (closest != null) best = byPath[closest];
            }
            return best == null ? null : "did you mean '" + best + "'?";
        }

        /// <summary>
        /// 타입을 아는 자리의 값에서 <c>DefId</c> 문자열을 <paramref name="mod"/> 기준 전체 이름으로 바꾼다 (G261). 리스트·맵(키 포함)·
        /// <c>T | None</c> 안까지 본다. 바뀐 것이 없으면 같은 객체를 돌려준다. 타입과 맞지 않는 값은 그대로 두고 ⑥이 알린다.
        /// </summary>
        public static Value Normalize(Value value, TypeRef type, string mod)
        {
            switch (type)
            {
                case DefIdTypeRef _:
                    if (value is StrValue s && !IsFull(s.Value) && s.Value.Length > 0) return new StrValue(Resolve(s.Value, mod), s.Origin);
                    return value;
                case OptionalTypeRef optional:
                    return Normalize(value, optional.Inner, mod);
                case ListTypeRef list when value is ListValue items:
                {
                    Value[]? changed = null;
                    for (int i = 0; i < items.Items.Count; i++)
                    {
                        var item = Normalize(items.Items[i], list.Element, mod);
                        if (ReferenceEquals(item, items.Items[i])) continue;
                        changed ??= Copy(items.Items);
                        changed[i] = item;
                    }
                    return changed == null ? value : new ListValue(changed);
                }
                case DictTypeRef dict when value is DictValue map:
                {
                    DictItem[]? changed = null;
                    for (int i = 0; i < map.Items.Count; i++)
                    {
                        var item = map.Items[i];
                        var key = Normalize(item.Key, dict.Key, mod);
                        var v = Normalize(item.Value, dict.Value, mod);
                        if (ReferenceEquals(key, item.Key) && ReferenceEquals(v, item.Value)) continue;
                        changed ??= Copy(map.Items);
                        changed[i] = new DictItem(key, v, item.Span);
                    }
                    return changed == null ? value : new DictValue(changed);
                }
            }
            return value;
        }

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            var copy = new T[items.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = items[i];
            return copy;
        }
    }
}
