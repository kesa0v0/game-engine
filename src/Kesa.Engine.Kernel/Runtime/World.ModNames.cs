using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// <c>모드id.이름</c> (spec-gaps G309): 모드 id마다 그 모드의 최상위 이름(함수·상수)을 속성으로 주는 값을 런타임 내장에 넣는다.
    /// 그 모드의 호스트 함수 이름 공간이 있으면 최상위 이름에 없는 속성은 거기서 찾는다(둘이 겹치는 이름은 로드가 막았다). 어느 모드가
    /// 쓸 수 있는지는 로드가 막았다 (자신과 필수 의존, G218과 같음). 불린 함수는 제 모드의 이름으로 돈다.
    /// </summary>
    public sealed partial class World
    {
        private void AddModNamespaces(SortedDictionary<string, Value> table)
        {
            foreach (var pair in _scopes)
            {
                table.TryGetValue(pair.Key, out var existing);
                // 같은 이름의 엔진 내장이 이긴다 (로드의 이름 검사도 그렇게 본다)
                if (existing != null && !(existing is HostNamespace)) continue;
                table[pair.Key] = new ModNamespace(pair.Key, pair.Value, existing as HostNamespace);
            }
        }

        private sealed class ModNamespace : Value, IRuntimeAttributes
        {
            private readonly string _mod;
            private readonly ModScope _scope;
            private readonly HostNamespace? _hosts;

            public ModNamespace(string mod, ModScope scope, HostNamespace? hosts)
            {
                _mod = mod;
                _scope = scope;
                _hosts = hosts;
            }

            public override string KindName => "mod";

            public Value GetAttribute(string name)
            {
                if (_scope.TryGet(name, out var value, out _)) return value;
                if (_hosts != null && _hosts.Members.ContainsKey(name)) return _hosts.GetAttribute(name);
                var names = new List<string>(_scope.Names);
                if (_hosts != null) names.AddRange(_hosts.Members.Keys);
                throw new ScriptRuntimeException(Codes.RunScript, "mod '" + _mod + "' has no top-level name '" + name + "'",
                    Suggest.Hint(name, names));
            }
        }
    }
}
