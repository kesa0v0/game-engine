using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 공간 종류 <c>grid</c> (G414): 자리 값은 정수 <c>[x, y]</c>나 <c>[x, y, z]</c>(판 위 가로·세로, 높이). 설정 <c>cell</c>·<c>origin</c>·<c>plane</c>·<c>flip-y</c>.
    /// </summary>
    public sealed class GridSpace : SpaceKind
    {
        private static readonly string[] Names = { "cell", "origin", "plane", "flip-y" };
        private static readonly string[] Planes = { "xy", "xz" };

        public override string Name => "grid";

        public override IReadOnlyCollection<string> Settings => Names;

        public override SpaceLayout? Configure(IReadOnlyDictionary<string, string> settings, List<string> problems)
        {
            int before = problems.Count;
            long cell = BridgeFixed.Scale;
            var origin = new long[3];
            bool xz = false, flip = false;
            foreach (var setting in settings)
            {
                string error;
                switch (setting.Key)
                {
                    case "cell":
                        var c = LookAttributes.FromText(LookAttributeType.Number, setting.Value, out error);
                        if (c == null) problems.Add("cell=\"" + setting.Value + "\": " + error);
                        else if (c.Numbers[0] <= 0) problems.Add("cell=\"" + setting.Value + "\": a cell is larger than 0");
                        else cell = c.Numbers[0];
                        break;
                    case "origin":
                        var o = LookAttributes.FromText(LookAttributeType.Vector, setting.Value, out error);
                        if (o == null) problems.Add("origin=\"" + setting.Value + "\": " + error);
                        else
                            for (int i = 0; i < o.Numbers.Count; i++) origin[i] = o.Numbers[i];
                        break;
                    case "plane":
                        if (setting.Value == "xy") xz = false;
                        else if (setting.Value == "xz") xz = true;
                        else problems.Add("plane=\"" + setting.Value + "\": a plane is xy or xz" + UiSuggest.Hint(setting.Value, Planes));
                        break;
                    case "flip-y":
                        var f = LookAttributes.FromText(LookAttributeType.Flag, setting.Value, out error);
                        if (f == null) problems.Add("flip-y=\"" + setting.Value + "\": " + error);
                        else flip = f.Flag;
                        break;
                }
            }
            return problems.Count > before ? null : new Layout(cell, origin, xz, flip);
        }

        private sealed class Layout : SpaceLayout
        {
            private readonly long _cell;
            private readonly long[] _origin;
            private readonly bool _xz;
            private readonly bool _flip;

            public Layout(long cell, long[] origin, bool xz, bool flip)
            {
                _cell = cell;
                _origin = origin;
                _xz = xz;
                _flip = flip;
            }

            public override long Unit => _cell;

            public override bool TryPlace(BridgeValue place, out LookPosition position, out string error)
            {
                position = default;
                error = "";
                if (!(place is BridgeList list) || list.Items.Count < 2 || list.Items.Count > 3)
                {
                    error = "a grid place is a list of 2 or 3 whole numbers like [2, 3], got " + place;
                    return false;
                }
                var cells = new long[3];
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (!(list.Items[i] is BridgeInt n))
                    {
                        error = "a grid place is a list of whole numbers, got " + place;
                        return false;
                    }
                    cells[i] = n.Value;
                }
                if (!_xz && cells[2] != 0)
                {
                    error = "a height (z) on plane=\"xy\" is not supported yet, got " + place;
                    return false;
                }
                try
                {
                    long x = checked(cells[0] * _cell + _origin[0]);
                    long y = checked((_flip ? -cells[1] : cells[1]) * _cell + _origin[1]);
                    long z = checked(cells[2] * _cell + _origin[2]);
                    position = _xz ? new LookPosition(x, z, y) : new LookPosition(x, y, 0);
                    return true;
                }
                catch (OverflowException)
                {
                    error = "the place " + place + " is too far away";
                    return false;
                }
            }

            public override bool TryPlaceOnRay(LookPosition from, LookPosition direction, out BridgeValue? place)
            {
                place = null;
                // 바닥: xy는 Z = 0, xz는 높이(Y) = origin의 z
                long level = _xz ? _origin[2] : 0;
                long start = _xz ? from.Y : from.Z;
                long step = _xz ? direction.Y : direction.Z;
                if (step == 0 || (level - start) != 0 && (level - start > 0) != (step > 0)) return false;
                try
                {
                    long a = checked(from.X + direction.X * (level - start) / step);
                    long b = checked((_xz ? from.Z : from.Y) + (_xz ? direction.Z : direction.Y) * (level - start) / step);
                    long x = Nearest(checked(a - _origin[0]), _cell);
                    long y = Nearest(checked(b - _origin[1]), _cell);
                    place = new BridgeList(new BridgeValue[] { new BridgeInt(x), new BridgeInt(_flip ? -y : y) });
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            /// <summary>가장 가까운 칸: <paramref name="d"/>/<paramref name="cell"/>을 반올림(음수도 아래로 내림한 뒤).</summary>
            private static long Nearest(long d, long cell)
            {
                long twice = checked(2 * d + cell);
                long q = twice / (2 * cell);
                return twice % (2 * cell) < 0 ? q - 1 : q;
            }
        }
    }
}
