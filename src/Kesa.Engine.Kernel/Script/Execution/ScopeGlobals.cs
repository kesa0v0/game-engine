using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

namespace Kesa.Engine.Script
{
    /// <summary>모드 이름 공간(<see cref="ModScope"/>)들을 함수 본문의 전역 이름 조회로 보여 주는 어댑터.</summary>
    public sealed class ScopeGlobals : IScriptGlobals
    {
        private readonly IReadOnlyDictionary<string, ModScope> _scopes;

        public ScopeGlobals(IReadOnlyDictionary<string, ModScope> scopes) => _scopes = scopes;

        public Value? Lookup(string mod, string name) =>
            _scopes.TryGetValue(mod, out var scope) && scope.TryGet(name, out var value, out _) ? value : null;
    }
}
