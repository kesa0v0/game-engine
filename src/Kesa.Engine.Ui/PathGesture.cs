using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 모양 종류 <c>path</c> (G482·G483): 지나간 자리의 순서 목록. 바로 앞과 같은 자리는 넣지 않고, 칸 공간(<see cref="SpaceLayout.TryCell"/>)이면
    /// 프레임 사이에 건너뛴 칸을 이웃 칸(8방향)으로 메운다. 같은 자리를 다시 지나면 다시 들어간다. 설정은 없다.
    /// </summary>
    public sealed class PathGesture : GestureKind
    {
        public override string Name => "path";

        public override IReadOnlyCollection<string> Settings => Array.Empty<string>();

        public override GestureShape? Configure(IReadOnlyDictionary<string, string> settings, List<string> problems) => new Shape();

        private sealed class Shape : GestureShape
        {
            public override GestureStroke? Begin(SpaceLayout space, BridgeValue start, out string error)
            {
                error = "";
                var stroke = new CellPath(space);
                stroke.Add(start);
                return stroke;
            }
        }
    }

    /// <summary>지나간 자리의 순서와, 칸 공간이면 그 칸 좌표. <see cref="PathGesture"/>와 <see cref="LassoGesture"/>가 쓴다.</summary>
    internal sealed class CellPath : GestureStroke
    {
        private readonly SpaceLayout _space;
        private readonly List<BridgeValue> _places = new List<BridgeValue>();
        private BridgeValue? _value;

        public CellPath(SpaceLayout space) => _space = space;

        /// <summary>칸 좌표(칸 공간일 때만, <see cref="_places"/>와 같은 순서).</summary>
        public List<(long X, long Y)> Cells { get; } = new List<(long X, long Y)>();

        public bool IsCells { get; private set; } = true;

        public override bool Add(BridgeValue place)
        {
            if (_places.Count > 0 && Equals(_places[_places.Count - 1], place)) return false;
            bool cell = _space.TryCell(place, out long x, out long y);
            if (_places.Count == 0) IsCells = cell;
            if (IsCells && cell && Cells.Count > 0)
            {
                var (fromX, fromY) = Cells[Cells.Count - 1];
                foreach (var (gapX, gapY) in Line(fromX, fromY, x, y)) Append(_space.Cell(gapX, gapY), (gapX, gapY));
            }
            Append(place, cell ? (x, y) : ((long, long)?)null);
            return true;
        }

        private void Append(BridgeValue place, (long X, long Y)? cell)
        {
            _places.Add(place);
            if (cell == null) IsCells = false;
            else Cells.Add(cell.Value);
            _value = null;
        }

        public override BridgeValue Value => _value ??= new BridgeList(_places.ToArray());

        /// <summary>두 칸 사이(끝 둘은 빼고)의 칸들: 브레젠험 선, 이웃끼리 8방향으로 붙는다.</summary>
        internal static IEnumerable<(long X, long Y)> Line(long x0, long y0, long x1, long y1)
        {
            long dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            long sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            long error = dx + dy;
            long x = x0, y = y0;
            if (x == x1 && y == y1) yield break;
            while (true)
            {
                long twice = 2 * error;
                if (twice >= dy)
                {
                    error += dy;
                    x += sx;
                }
                if (twice <= dx)
                {
                    error += dx;
                    y += sy;
                }
                if (x == x1 && y == y1) yield break;
                yield return (x, y);
            }
        }
    }
}
