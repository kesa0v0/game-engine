using System;
using System.Collections.Generic;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;

namespace Kesa.Engine.Runtime
{
    /// <summary>
    /// 붙인 봇 하나 (spec-gaps G368~G375): 어느 <c>@bot</c>이 어느 엔티티를 맡나, 호스트가 넘긴 설정, 기억(<c>bot_state</c> 기록들), 봇
    /// 난수열의 시드와 카운터. 엔진이 만들고 호스트가 들고 <see cref="WorldView.Think"/>에 넘긴다. 게임 상태가 아니라 월드 저장에 들지 않고
    /// 호스트가 <see cref="Save"/>로 따로 남긴다(G372).
    /// </summary>
    public sealed class BotState
    {
        private readonly SchemaSet _schema;
        private SortedDictionary<string, SortedDictionary<string, Value>> _records;

        internal BotState(SchemaSet schema, string bot, EntityId me, SortedDictionary<string, Value> settings, long seed, int stepBudget)
        {
            _schema = schema;
            _records = UiState.Defaults(schema.BotStates);
            Bot = bot;
            Me = me;
            SortedSettings = settings;
            Seed = seed;
            StepBudget = stepBudget;
        }

        /// <summary><c>@bot</c> id (<c>모드id.함수이름</c>).</summary>
        public string Bot { get; }

        /// <summary>맡은 엔티티: 첫 매개변수로 넘어간다 (G368).</summary>
        public EntityId Me { get; }

        /// <summary>호스트가 넘긴 설정 (첫 매개변수 뒤의 매개변수 이름 → 값). 빠진 것은 선언한 기본값이다 (G370).</summary>
        public IReadOnlyDictionary<string, Value> Settings => SortedSettings;

        internal SortedDictionary<string, Value> SortedSettings { get; }

        /// <summary>봇 난수열의 시드 (G373).</summary>
        public long Seed { get; }

        /// <summary>봇 난수열에서 난수 함수를 부른 횟수. 다음 호출의 n이다 (G373).</summary>
        public long RandomCalls { get; internal set; }

        /// <summary>한 번 생각할 때의 단계 예산 (G375). 저장하지 않는다: 호스트가 붙일 때마다 정한다.</summary>
        public int StepBudget { get; set; }

        /// <summary>기억이 바뀐 횟수.</summary>
        public long Version { get; private set; }

        /// <summary>적힌 그대로의 값 (호스트의 디버그용). 없는 기록·칸은 <see cref="ArgumentException"/>이다.</summary>
        public Value Stored(string state, string field)
        {
            if (!_records.TryGetValue(state, out var fields) || !fields.TryGetValue(field, out var value))
                throw new ArgumentException("there is no bot state field '" + state + "." + field + "'");
            return value;
        }

        /// <summary>호스트가 저장 파일에 넣을 바이트 (G372). <see cref="World.LoadBot"/>로 되살린다.</summary>
        public byte[] Save() => World.SaveBot(this);

        internal SortedDictionary<string, SortedDictionary<string, Value>> Records => _records;

        internal SortedDictionary<string, SortedDictionary<string, Value>> Draft()
        {
            var copy = new SortedDictionary<string, SortedDictionary<string, Value>>(StringComparer.Ordinal);
            foreach (var pair in _records) copy[pair.Key] = new SortedDictionary<string, Value>(pair.Value, StringComparer.Ordinal);
            return copy;
        }

        internal void Commit(SortedDictionary<string, SortedDictionary<string, Value>> draft, long randomCalls)
        {
            _records = draft;
            RandomCalls = randomCalls;
            Version++;
        }

        internal bool Belongs(SchemaSet schema) => ReferenceEquals(schema, _schema);
    }
}
