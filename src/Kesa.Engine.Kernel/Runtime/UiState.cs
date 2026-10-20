using System;
using System.Collections.Generic;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 연결 하나의 UI 로컬 저장소 (spec S10 3항, spec-gaps G277·G279). 스키마가 선언한 <c>ui_state</c> 기록마다 칸 값을 든다. 엔진이 만들고
    /// 호스트가 연결마다 들고 <see cref="WorldView.Act"/>에 넘긴다. 저장(S8)에도 네트워크에도 들지 않고, 비우는 때(<see cref="Reset"/>)는
    /// 호스트가 정한다. 값은 적힌 그대로 두고 읽을 때 없어진·숨긴 엔티티를 거른다(G279·G284).
    /// </summary>
    public sealed class UiState
    {
        private readonly SchemaSet _schema;
        private SortedDictionary<string, SortedDictionary<string, Value>> _records;

        internal UiState(SchemaSet schema)
        {
            _schema = schema;
            _records = Defaults(schema);
        }

        private static SortedDictionary<string, SortedDictionary<string, Value>> Defaults(SchemaSet schema)
        {
            var records = new SortedDictionary<string, SortedDictionary<string, Value>>(StringComparer.Ordinal);
            foreach (var state in schema.UiStates)
            {
                var fields = new SortedDictionary<string, Value>(StringComparer.Ordinal);
                foreach (var m in state.Members) fields[m.Name] = m.Kind == MemberKind.Ref ? NoneValue.Instance : m.Spec.Default!;
                records[state.Name] = fields;
            }
            return records;
        }

        /// <summary>모든 기록을 선언한 기본값으로 되돌린다 (판이 바뀌었을 때 등, G279).</summary>
        public void Reset() => _records = Defaults(_schema);

        /// <summary>
        /// 적힌 그대로의 값 (없어진 엔티티도 그대로, 호스트의 디버그용). 화면에 쓸 값은 모드의 화면 함수가 읽는다.
        /// 없는 기록·칸은 <see cref="ArgumentException"/>이다.
        /// </summary>
        public Value Stored(string state, string field)
        {
            if (!_records.TryGetValue(state, out var fields) || !fields.TryGetValue(field, out var value))
                throw new ArgumentException("there is no UI state field '" + state + "." + field + "'");
            return value;
        }

        /// <summary>UI 동작 하나가 쓸 사본: 끝까지 성공하면 <see cref="Commit"/>으로 들인다 (G278: 실패하면 쓰기는 사라진다).</summary>
        internal SortedDictionary<string, SortedDictionary<string, Value>> Draft()
        {
            var copy = new SortedDictionary<string, SortedDictionary<string, Value>>(StringComparer.Ordinal);
            foreach (var pair in _records) copy[pair.Key] = new SortedDictionary<string, Value>(pair.Value, StringComparer.Ordinal);
            return copy;
        }

        internal void Commit(SortedDictionary<string, SortedDictionary<string, Value>> draft) => _records = draft;

        internal bool Belongs(SchemaSet schema) => ReferenceEquals(schema, _schema);
    }
}
