using System;
using System.Collections.Generic;
using Kesa.Engine.Diagnostics;

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

        /// <summary>
        /// 이 모드에 보이는 스키마 이름(컴포넌트·신호)의 값. 모드가 묶은 이름보다 약하고 엔진 내장보다 강하다 (spec-gaps G230):
        /// 엔진에 같은 이름의 내장이 새로 생겨도 그 컴포넌트를 쓰던 모드가 깨지지 않는다(G112의 이유).
        /// </summary>
        private Func<string, Value?>? _schemaNames;

        public void SetSchemaNames(Func<string, Value?> lookup) => _schemaNames = lookup;

        /// <summary>함수 본문과 콘텐츠 파일이 이름을 찾는 차례: 모드가 묶은 이름, 그다음 보이는 스키마 이름 (내장은 그 뒤).</summary>
        public bool TryLookup(string name, out Value value)
        {
            if (TryGet(name, out value, out _)) return true;
            var schema = _schemaNames?.Invoke(name);
            if (schema == null) return false;
            value = schema;
            return true;
        }

        /// <summary>모드가 묶지 않았고 보이는 스키마 이름인가 (그래서 같은 이름의 내장을 가린다).</summary>
        public bool IsSchemaName(string name) => !_bindings.ContainsKey(name) && _schemaNames?.Invoke(name) != null;

        public void Bind(string name, Value value, SourceSpan span)
        {
            _bindings.Add(name, (value, span));
            _order.Add(name);
        }

        /// <summary>묶인 순서대로의 이름.</summary>
        public IReadOnlyList<string> Names => _order;
    }
}
