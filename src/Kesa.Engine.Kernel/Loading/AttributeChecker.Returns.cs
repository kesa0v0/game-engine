using System;
using System.Collections.Generic;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Loading
{
    internal static partial class AttributeChecker
    {
        /// <summary>
        /// 모드 함수 호출의 반환 타입 (spec-gaps G231, G228의 2차). 적은 반환 타입(<c>-&gt; Ref[Unit]</c>)이 있으면 그것이고, 없으면
        /// 본문의 <c>return</c>들을 합친 것이다. 적지 않은 매개변수는 그 호출에서 넘긴 인자의 타입으로 본문을 다시 훑는다(pyright의
        /// 호출 자리 추론과 같음). 같은 함수에 같은 인자 타입이면 기억한 결과를 쓴다. 모드 사이에는 함수를 가져오는 길이 없어 같은 모드만 본다.
        /// </summary>
        private sealed class ReturnTypes
        {
            private readonly SchemaSet _schema;
            private readonly DefRegistry _defs;
            private readonly Dictionary<string, ModCode> _mods = new Dictionary<string, ModCode>(StringComparer.Ordinal);
            private readonly Dictionary<string, ModNames> _globals = new Dictionary<string, ModNames>(StringComparer.Ordinal);
            private readonly Dictionary<object, List<KeyValuePair<StaticType[], StaticType>>> _known =
                new Dictionary<object, List<KeyValuePair<StaticType[], StaticType>>>();

            /// <summary>훑는 중인 함수들. 로직 함수의 재귀는 로드 오류지만(EVL002) 그 모드가 꺼지기 전에도 이 검사는 돈다.</summary>
            private readonly HashSet<object> _active = new HashSet<object>();

            public ReturnTypes(SchemaSet schema, DefRegistry defs, IReadOnlyList<ModCode> mods)
            {
                _schema = schema;
                _defs = defs;
                foreach (var mod in mods) _mods[mod.Mod] = mod;
            }

            public ModNames Globals(string mod)
            {
                if (_globals.TryGetValue(mod, out var names)) return names;
                names = _mods.TryGetValue(mod, out var code) ? AttributeChecker.Globals(code, _schema) : new ModNames();
                _globals[mod] = names;
                return names;
            }

            /// <summary>
            /// <paramref name="mod"/>의 이름 <paramref name="name"/>을 부른 결과의 타입. 모드 함수가 아니거나(커맨드·핸들러 포함) 재귀면 모름,
            /// 적지 않은 매개변수의 인자 타입을 아직 모르면(대입을 모으는 중) null이다.
            /// </summary>
            public StaticType? Of(string mod, string name, IReadOnlyList<Argument> arguments, List<StaticType?> types)
            {
                if (!_mods.TryGetValue(mod, out var code) || !code.Scope.TryGet(name, out var value, out _)) return StaticType.Unknown;
                if (!(value is FunctionValue fn) || fn.IsEntry) return StaticType.Unknown;
                if (fn.Definition?.Returns != null) return Annotation(fn.Definition.Returns, _schema);

                var given = new StaticType[fn.Parameters.Count];
                for (int i = 0, position = 0; i < arguments.Count && i < types.Count; i++)
                {
                    int index = arguments[i].Name == null ? position++ : IndexOf(fn, arguments[i].Name!);
                    if (index < 0 || index >= given.Length) continue;
                    var p = fn.Parameters[index];
                    if (p.Annotation != null) continue;
                    if (types[i] == null) return null;
                    given[index] = types[i]!;
                }
                for (int i = 0; i < given.Length; i++)
                {
                    var p = fn.Parameters[i];
                    given[i] ??= p.Annotation != null ? Annotation(p.Annotation, _schema) : StaticType.Unknown;
                }

                var node = Node(fn);
                if (!_known.TryGetValue(node, out var known)) _known[node] = known = new List<KeyValuePair<StaticType[], StaticType>>();
                foreach (var pair in known)
                    if (SameAll(pair.Key, given)) return pair.Value;
                if (!_active.Add(node)) return StaticType.Unknown;

                var parameters = new List<KeyValuePair<string, StaticType?>>();
                for (int i = 0; i < given.Length; i++)
                    parameters.Add(new KeyValuePair<string, StaticType?>(fn.Parameters[i].Name, given[i]));
                var walker = new Walker(_schema, _defs, Globals(fn.Mod), null, fn.Mod, returns: this);
                walker.Function(fn, parameters);
                _active.Remove(node);

                var result = walker.Returned ?? StaticType.Unknown;
                known.Add(new KeyValuePair<StaticType[], StaticType>(given, result));
                return result;
            }

            private static bool SameAll(StaticType[] a, StaticType[] b)
            {
                for (int i = 0; i < a.Length; i++)
                    if (!a[i].Same(b[i])) return false;
                return true;
            }

            private static int IndexOf(FunctionValue fn, string name)
            {
                for (int i = 0; i < fn.Parameters.Count; i++)
                    if (fn.Parameters[i].Name == name) return i;
                return -1;
            }
        }
    }
}
