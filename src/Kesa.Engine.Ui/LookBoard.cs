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
    }
}
