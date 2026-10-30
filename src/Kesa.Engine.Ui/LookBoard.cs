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

        /// <summary>
        /// 누른 곳의 물체들(위에 그린 것부터)에서 받을 것을 찾아 낸다 (G395: 표시의 <c>click</c>이 엔티티보다 먼저). 받을 것이 없으면 null.
        /// 엔티티 누르기(<c>@world_click</c>)는 아직 없다.
        /// </summary>
        public async ValueTask<BridgeResult?> PressAsync(IEnumerable<string> keysUnderPointer)
        {
            foreach (var key in keysUnderPointer)
            {
                var click = Stage.ClickOf(key);
                if (click != null) return await _session.Act(click.Action, click.Arguments);
            }
            return null;
        }
    }
}
