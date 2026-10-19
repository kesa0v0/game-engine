using System.Globalization;
using System.Linq;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 값을 터미널 글자로. 구멍: 번역(X4)이 없어 텍스트 값은 키와 인자를, 엔티티는 번호와 Def id를 그대로 낸다.
    /// </summary>
    internal sealed class ValueText
    {
        private readonly World _world;

        public ValueText(World world) => _world = world;

        public string Entity(EntityId id) => _world.IsAlive(id) ? id + " " + _world.DefOf(id) : id + " (없음)";

        public string Of(Value value)
        {
            switch (value)
            {
                case NoneValue _: return "None";
                case BoolValue b: return b.Value ? "True" : "False";
                case IntValue i: return i.Value.ToString(CultureInfo.InvariantCulture);
                case FixedValue f: return f.Value.ToString();
                case StrValue s: return s.Value;
                case EntityValue e: return Entity(e.Id);
                case ListValue list: return "[" + string.Join(", ", list.Items.Select(Of)) + "]";
                case DictValue dict: return "{" + string.Join(", ", dict.Items.Select(p => Of(p.Key) + ": " + Of(p.Value))) + "}";
                case TextValue t:
                    return t.Args.Count == 0 ? t.Key : t.Key + "(" + string.Join(", ", t.Args.Select(p => p.Key + "=" + Of(p.Value))) + ")";
                default: return value.KindName;
            }
        }
    }
}
