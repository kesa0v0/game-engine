using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Text;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 값을 터미널 글자로 (spec-gaps G269·G270). 엔티티는 번호와 그 Def의 번역된 이름(없으면 Def id, G275), 제목·선택지 자리는
    /// <see cref="Translator.Label(Value, World)"/>, 화면 값의 맨 값은 <see cref="Translator.Value(Value, World)"/>, list·dict는 콘솔이 늘어놓는다.
    /// </summary>
    internal sealed class ValueText
    {
        private readonly World _world;

        public ValueText(World world, Translator translator)
        {
            _world = world;
            Tr = translator;
        }

        public Translator Tr { get; }

        /// <summary>콘솔 자신의 글 <c>engine-console-…</c> (G274).</summary>
        public string Say(string name, params (string Name, Value Value)[] args) => Tr.Text("engine-console-" + name, args);

        public string Entity(EntityId id) =>
            id + " " + (_world.IsAlive(id) ? Tr.Label(new EntityValue(id), _world) : Say("gone"));

        /// <summary>제목·선택지 자리: 문자열은 키, 엔티티는 <see cref="Entity"/>.</summary>
        public string Label(Value value) => value switch
        {
            EntityValue e => Entity(e.Id),
            OptionValue o when o.Label == null => Label(o.Value),
            _ => Tr.Label(value, _world),
        };

        /// <summary>화면 값: 문자열은 글자 그대로(Def id면 그 이름).</summary>
        public string Of(Value value)
        {
            switch (value)
            {
                case EntityValue e: return Entity(e.Id);
                case ListValue list: return "[" + string.Join(", ", list.Items.Select(Of)) + "]";
                case DictValue dict: return "{" + string.Join(", ", dict.Items.Select(p => Of(p.Key) + ": " + Of(p.Value))) + "}";
                default: return Tr.Value(value, _world);
            }
        }

        /// <summary><c>require</c>의 메시지: 엔티티 인자는 지금 살아 있으면 그 Def로 (G267).</summary>
        public string Message(string key, IReadOnlyDictionary<string, Value> args)
        {
            var defs = new Dictionary<string, string>();
            foreach (var pair in args)
                if (pair.Value is EntityValue e && _world.IsAlive(e.Id)) defs[pair.Key] = _world.DefOf(e.Id);
            return Tr.Format(new TextValue(key, args, defs));
        }
    }
}
