using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 모양 종류 <c>lasso</c> (G482): 지나간 칸들을 시작 칸까지 곧게 닫은 테두리와 그 안의 칸 목록, y 다음 x 순. 안은 감은 수가 0이 아닌 칸이다
    /// (자기와 겹친 고리도 둘러싼 곳은 모두 든다). 칸 공간(<see cref="SpaceLayout.TryCell"/>)에서만 쓴다. 설정은 없다.
    /// </summary>
    public sealed class LassoGesture : GestureKind
    {
        public override string Name => "lasso";

        public override IReadOnlyCollection<string> Settings => Array.Empty<string>();

        public override GestureShape? Configure(IReadOnlyDictionary<string, string> settings, List<string> problems) => new Shape();

        private sealed class Shape : GestureShape
        {
            public override GestureStroke? Begin(SpaceLayout space, BridgeValue start, out string error)
            {
                error = "";
                if (!space.TryCell(start, out _, out _))
                {
                    error = "kind=\"lasso\" needs a space of cells (like kind=\"grid\"), got the place " + start;
                    return null;
                }
                var stroke = new Stroke(space);
                stroke.Add(start);
                return stroke;
            }
        }

        private sealed class Stroke : GestureStroke
        {
            private readonly SpaceLayout _space;
            private readonly CellPath _path;
            private BridgeValue? _value;

            public Stroke(SpaceLayout space)
            {
                _space = space;
                _path = new CellPath(space);
            }

            public override bool Add(BridgeValue place)
            {
                if (!_space.TryCell(place, out _, out _) || !_path.Add(place)) return false;
                _value = null;
                return true;
            }

            public override BridgeValue Value => _value ??= Fill();

            private BridgeValue Fill()
            {
                var ring = _path.Cells;
                var edge = new HashSet<(long X, long Y)>(ring);
                // 끝 칸에서 시작 칸으로 닫는 선도 테두리다
                var (lastX, lastY) = ring[ring.Count - 1];
                foreach (var cell in CellPath.Line(lastX, lastY, ring[0].X, ring[0].Y)) edge.Add(cell);
                long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
                foreach (var (x, y) in ring)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
                var cells = new List<BridgeValue>();
                for (long y = minY; y <= maxY; y++)
                for (long x = minX; x <= maxX; x++)
                    if (edge.Contains((x, y)) || Winding(ring, x, y) != 0)
                        cells.Add(_space.Cell(x, y));
                return new BridgeList(cells.ToArray());
            }

            /// <summary>닫힌 다각형 <paramref name="ring"/>이 점을 감은 수. 점이 변 위에 있으면 테두리 칸이라 앞에서 이미 든다.</summary>
            private static int Winding(List<(long X, long Y)> ring, long px, long py)
            {
                int winding = 0;
                for (int i = 0; i < ring.Count; i++)
                {
                    var (ax, ay) = ring[i];
                    var (bx, by) = ring[(i + 1) % ring.Count];
                    long side = (bx - ax) * (py - ay) - (px - ax) * (by - ay);
                    if (ay <= py)
                    {
                        if (by > py && side > 0) winding++;
                    }
                    else if (by <= py && side < 0) winding--;
                }
                return winding;
            }
        }
    }
}
