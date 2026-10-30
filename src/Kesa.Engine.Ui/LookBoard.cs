using System.Collections.Generic;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 한 연결의 판 위 그림을 경계와 맞춘다 (G405·G408): 프레임마다 <see cref="RefreshAsync"/>가 엔티티 물체와 표시의 바뀐 것만 받아
    /// <see cref="LookStage"/>에 넣는다. 도장은 둘이 따로 센다.
    /// </summary>
    public sealed class LookBoard
    {
        private readonly IHostSession _session;
        private long _objects;
        private long _marks;

        public LookBoard(IHostSession session, LookStage stage)
        {
            _session = session;
            Stage = stage;
        }

        public LookStage Stage { get; }

        public async ValueTask RefreshAsync()
        {
            var objects = await _session.WorldObjects(_objects);
            Stage.Apply(objects);
            _objects = objects.Stamp;
            var marks = await _session.WorldMarks(_marks);
            Stage.Apply(marks);
            _marks = marks.Stamp;
        }

        /// <summary>내장 입력: 주 누름(왼쪽 클릭·탭)과 보조 누름 (G416).</summary>
        public const string Primary = "primary";

        public const string Secondary = "secondary";

        /// <summary>
        /// 커서 아래 물체들(위에 그린 것부터)과 자리(<see cref="LookStage.PlaceOnRay"/>)에 들어온 입력을 받을 것에 준다 (G395·G419·G422): 그 입력을
        /// 받는 표시가 먼저, 없으면 엔티티들과 자리를 경계의 <c>WorldInput</c>에 넘긴다. 받을 것이 없으면 null.
        /// </summary>
        public async ValueTask<BridgeResult?> InputAsync(string input, IEnumerable<string> keysUnderPointer, BridgeWhere? place = null)
        {
            var entities = new List<long>();
            foreach (var key in keysUnderPointer)
            {
                var click = input == Primary ? Stage.ClickOf(key) : null;
                if (click != null) return await _session.Act(click.Action, click.Arguments);
                if (key.StartsWith(LookStage.EntityPrefix, System.StringComparison.Ordinal) &&
                    long.TryParse(key.Substring(LookStage.EntityPrefix.Length), System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var id))
                    entities.Add(id);
            }
            return entities.Count == 0 && place == null ? null : await _session.WorldInput(input, entities, place);
        }

        /// <summary>주 누름: <c>InputAsync(Primary, …)</c>.</summary>
        public ValueTask<BridgeResult?> PressAsync(IEnumerable<string> keysUnderPointer) => InputAsync(Primary, keysUnderPointer);
    }
}
