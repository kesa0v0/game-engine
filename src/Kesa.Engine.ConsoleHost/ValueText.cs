using System.Linq;
using Kesa.Engine.Bridge;
using Kesa.Engine.Text;

namespace Kesa.Engine.ConsoleHost
{
    /// <summary>
    /// 경계 값을 터미널 글자로 (spec-gaps G269·G270). 엔티티는 번호와 그 Def의 번역된 이름(없으면 Def id, G275), 제목·선택지 자리는
    /// <see cref="Translator.Label(BridgeValue)"/>, 화면 값의 맨 값은 <see cref="Translator.Value(BridgeValue)"/>, list·dict는 콘솔이 늘어놓는다.
    /// </summary>
    internal sealed class ValueText
    {
        public ValueText(Translator translator)
        {
            Tr = translator;
        }

        public Translator Tr { get; }

        /// <summary>콘솔 자신의 글 <c>engine-console-…</c> (G274).</summary>
        public string Say(string name, params (string Name, BridgeValue Value)[] args) => Tr.Text("engine-console-" + name, args);

        /// <summary>Def를 모르는 엔티티(이미 없어졌다)는 "gone".</summary>
        public string Entity(BridgeEntity e) => "#" + e.Id + " " + (e.Def != null ? Tr.Label(e) : Say("gone"));

        /// <summary>제목·선택지 자리: 문자열은 키, 엔티티는 <see cref="Entity"/>.</summary>
        public string Label(BridgeValue value) => value switch
        {
            BridgeEntity e => Entity(e),
            BridgeOption o when o.Label == null => Label(o.Value),
            _ => Tr.Label(value),
        };

        /// <summary>화면 값: 문자열은 글자 그대로(Def id면 그 이름).</summary>
        public string Of(BridgeValue value)
        {
            switch (value)
            {
                case BridgeEntity e: return Entity(e);
                case BridgeList list: return "[" + string.Join(", ", list.Items.Select(Of)) + "]";
                case BridgeDict dict: return "{" + string.Join(", ", dict.Items.Select(p => Of(p.Key) + ": " + Of(p.Value))) + "}";
                default: return Tr.Value(value);
            }
        }
    }
}
