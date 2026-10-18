using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;

namespace Kesa.Engine.Script
{
    /// <summary>
    /// 모드 하나의 최상위 이름들. 한 모드의 파일들은 실행 순서대로 같은 이름 공간을 쓰고,
    /// 이름은 한 번만 묶을 수 있다 (spec-gaps G20).
    /// </summary>
    public sealed class ModScope
    {
        private readonly Dictionary<string, (Value Value, SourceSpan Span)> _bindings =
            new Dictionary<string, (Value, SourceSpan)>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public bool TryGet(string name, out Value value, out SourceSpan span)
        {
            if (_bindings.TryGetValue(name, out var b))
            {
                value = b.Value;
                span = b.Span;
                return true;
            }
            value = NoneValue.Instance;
            span = SourceSpan.None;
            return false;
        }

        public void Bind(string name, Value value, SourceSpan span)
        {
            _bindings.Add(name, (value, span));
            _order.Add(name);
        }

        /// <summary>묶인 순서대로의 이름.</summary>
        public IReadOnlyList<string> Names => _order;
    }
}
