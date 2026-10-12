namespace Kesa.Engine.Numerics
{
    /// <summary>
    /// 십진 고정소수점 (spec P2). 값 = Raw / 10^6.
    /// 연산은 이후 단계에서 추가한다. 지금은 리터럴 표현만 담당한다.
    /// </summary>
    public readonly struct Fixed : System.IEquatable<Fixed>
    {
        public const int Decimals = 6;
        public const long Scale = 1_000_000;

        private Fixed(long raw) => Raw = raw;

        public long Raw { get; }

        public static Fixed FromRaw(long raw) => new Fixed(raw);

        public bool Equals(Fixed other) => Raw == other.Raw;
        public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();

        public override string ToString()
        {
            long whole = Raw / Scale;
            long frac = Raw % Scale;
            if (frac < 0) frac = -frac;
            string sign = Raw < 0 && whole == 0 ? "-" : "";
            string fracText = frac.ToString("D6", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
            if (fracText.Length == 0) fracText = "0";
            return sign + whole.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + fracText;
        }
    }
}
