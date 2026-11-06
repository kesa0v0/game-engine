using System;
using System.Numerics;

namespace Kesa.Engine.Numerics
{
    /// <summary>
    /// 제곱근, 삼각함수, 거듭제곱 (G472). 결과는 참값에 가장 가까운 <see cref="Fixed"/>로 정의한다.
    /// 그래서 구현을 바꿔도 값은 그대로이고 리플레이가 깨지지 않는다.
    /// </summary>
    public static class FixedFunctions
    {
        /// <summary>반올림된 원주율. 라디안 함수의 입력에 쓰는 값이다.</summary>
        public static readonly Fixed Pi = Fixed.FromRaw(3_141_593);

        /// <summary><c>fixed ** n</c>의 지수 상한: 정확한 곱의 크기가 지수만큼 커진다.</summary>
        public const long MaxFixedExponent = 1024;

        private const int StartBits = 128;
        private const int MaxBits = 4096;

        // 계산 오차의 상한(2의 지수, 마지막 자리 기준): 큰 라디안을 줄일 때 약 2^43 × 원주율의 오차에 급수와 변환의 오차를 더한 값
        private const int ErrorBits = 48;

        private const long DegreesRaw90 = 90 * Fixed.Scale;
        private const long DegreesRaw360 = 360 * Fixed.Scale;
        private static readonly BigInteger DegreesRaw180 = 180 * Fixed.Scale;
        private static readonly BigInteger Scale = Fixed.Scale;

        // ---- 제곱근 ----

        /// <summary>음수면 false.</summary>
        public static bool TrySqrt(Fixed a, out Fixed result)
        {
            result = default;
            if (a.Raw < 0) return false;
            if (a.Raw <= long.MaxValue / Fixed.Scale)
            {
                ulong small = (ulong)a.Raw * (ulong)Fixed.Scale;
                ulong root = SqrtFloor(small);
                // n > r² + r 이면 r + 0.5보다 크다. 정수의 제곱근은 r + 0.5일 수 없다
                result = Fixed.FromRaw((long)(small > root * root + root ? root + 1 : root));
                return true;
            }
            var n = new BigInteger(a.Raw) * Scale;
            var big = SqrtFloor(n);
            result = Fixed.FromRaw((long)(n > big * big + big ? big + 1 : big));
            return true;
        }

        private static ulong SqrtFloor(ulong n)
        {
            ulong root = 0;
            for (ulong bit = 1UL << 62; bit != 0; bit >>= 2)
            {
                if (n >= root + bit)
                {
                    n -= root + bit;
                    root = (root >> 1) + bit;
                }
                else root >>= 1;
            }
            return root;
        }

        private static BigInteger SqrtFloor(BigInteger n)
        {
            if (n.IsZero) return n;
            var x = BigInteger.One << (n.ToByteArray().Length * 4); // 제곱근보다 크게 시작해야 줄어들며 멈춘다
            while (true)
            {
                var next = (x + n / x) >> 1;
                if (next >= x) return x;
                x = next;
            }
        }

        // ---- 거듭제곱 ----

        /// <summary>범위를 넘으면 false. <paramref name="n"/>은 0 이상이다. 0의 0제곱은 1이다.</summary>
        public static bool TryPower(long a, long n, out long result)
        {
            result = 1;
            if (n == 0 || a == 1) return true;
            if (a == 0)
            {
                result = 0;
                return true;
            }
            if (a == -1)
            {
                result = n % 2 == 0 ? 1 : -1;
                return true;
            }
            if (n > 63) return false;
            for (long i = 0; i < n; i++)
                if (!FixedMath.TryMultiply(result, a, out result)) return false;
            return true;
        }

        /// <summary>범위를 넘으면 false. <paramref name="n"/>은 0 이상 <see cref="MaxFixedExponent"/> 이하다. 곱할 때마다 반올림하지 않는다.</summary>
        public static bool TryPower(Fixed a, long n, out Fixed result)
        {
            result = Fixed.FromRaw(Fixed.Scale);
            if (n == 0) return true;
            int exponent = (int)n;
            var numerator = BigInteger.Pow(BigInteger.Abs(a.Raw), exponent);
            var denominator = BigInteger.Pow(Scale, exponent - 1);
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder * 2 >= denominator) quotient += 1;
            if (a.Raw < 0 && n % 2 != 0) quotient = -quotient;
            if (quotient < long.MinValue || quotient > long.MaxValue) return false;
            result = Fixed.FromRaw((long)quotient);
            return true;
        }

        // ---- 삼각함수 ----

        public static Fixed Sin(Fixed radians) => Nearest(bits => SinRadians(radians, bits, 0));

        public static Fixed Cos(Fixed radians) => Nearest(bits => SinRadians(radians, bits, 1));

        public static Fixed SinDegrees(Fixed degrees) => Nearest(bits => SinOfDegrees(degrees, bits, 0));

        public static Fixed CosDegrees(Fixed degrees) => Nearest(bits => SinOfDegrees(degrees, bits, 1));

        /// <summary>라디안, (-π, π]. 둘 다 0이면 0이다.</summary>
        public static Fixed Atan2(Fixed y, Fixed x) => Nearest(bits => Angle(y, x, bits));

        /// <summary>도, (-180, 180]. 둘 다 0이면 0이다.</summary>
        public static Fixed Atan2Degrees(Fixed y, Fixed x) => Nearest(bits => (Angle(y, x, bits) * DegreesRaw180 / Scale << bits) / PiAt(bits));

        /// <summary>
        /// 어림값이 두 격자의 가운데에 너무 가까우면 어느 쪽인지 모르므로 정밀도를 올려 다시 계산한다. 원주율이 무리수라 참값이 정확히 가운데인 입력은 없다.
        /// </summary>
        private static Fixed Nearest(Func<int, BigInteger> value)
        {
            var doubt = (BigInteger.One << ErrorBits) * Scale;
            for (int bits = StartBits; ; bits *= 2)
            {
                var v = value(bits);
                var scaled = BigInteger.Abs(v) * Scale;
                var whole = scaled >> bits;
                var fraction = scaled - (whole << bits);
                var half = BigInteger.One << (bits - 1);
                if (BigInteger.Abs(fraction - half) > doubt || bits >= MaxBits)
                {
                    if (fraction >= half) whole += 1;
                    return Fixed.FromRaw((long)(v.Sign < 0 ? -whole : whole));
                }
            }
        }

        private static BigInteger SinRadians(Fixed radians, int bits, int quarterTurns)
        {
            var whole = (new BigInteger(radians.Raw) << bits) / Scale;
            var halfPi = PiAt(bits) >> 1;
            var k = FloorDivide(whole + (halfPi >> 1), halfPi);
            int quadrant = (int)(((k % 4) + 4) % 4);
            return SinByQuadrant(whole - k * halfPi, quadrant + quarterTurns, bits);
        }

        private static BigInteger SinOfDegrees(Fixed degrees, int bits, int quarterTurns)
        {
            long turn = degrees.Raw % DegreesRaw360;
            if (turn < 0) turn += DegreesRaw360;
            long k = (turn + DegreesRaw90 / 2) / DegreesRaw90;
            long rest = turn - k * DegreesRaw90;
            return SinByQuadrant(new BigInteger(rest) * PiAt(bits) / DegreesRaw180, (int)(k % 4) + quarterTurns, bits);
        }

        // angle은 |angle| ≤ π/4
        private static BigInteger SinByQuadrant(BigInteger angle, int quadrant, int bits)
        {
            switch (quadrant % 4)
            {
                case 0: return SinSeries(angle, bits);
                case 1: return CosSeries(angle, bits);
                case 2: return -SinSeries(angle, bits);
                default: return -CosSeries(angle, bits);
            }
        }

        private static BigInteger SinSeries(BigInteger x, int bits)
        {
            var square = (x * x) >> bits;
            var term = x;
            var sum = x;
            for (int n = 1; !term.IsZero; n++)
            {
                term = -((term * square) >> bits) / (2 * n * (2 * n + 1));
                sum += term;
            }
            return sum;
        }

        private static BigInteger CosSeries(BigInteger x, int bits)
        {
            var square = (x * x) >> bits;
            var term = BigInteger.One << bits;
            var sum = term;
            for (int n = 1; !term.IsZero; n++)
            {
                term = -((term * square) >> bits) / ((2 * n - 1) * (2 * n));
                sum += term;
            }
            return sum;
        }

        // y와 x의 각도 (라디안, 2^bits 배)
        private static BigInteger Angle(Fixed y, Fixed x, int bits)
        {
            if (y.Raw == 0 && x.Raw == 0) return BigInteger.Zero;
            var a = BigInteger.Abs(y.Raw);
            var b = BigInteger.Abs(x.Raw);
            var pi = PiAt(bits);
            // 대각선 아래로 접으면 급수의 인자가 1 이하다
            var angle = a <= b ? Atan((a << bits) / b, bits) : (pi >> 1) - Atan((b << bits) / a, bits);
            if (x.Raw < 0) angle = pi - angle;
            return y.Raw < 0 ? -angle : angle;
        }

        private static BigInteger Atan(BigInteger t, int bits)
        {
            // atan(t) = 2 atan(t / (1 + √(1 + t²))): 세 번 접으면 t가 0.1 아래라 급수가 빠르다
            var one = BigInteger.One << bits;
            for (int i = 0; i < 3; i++)
                t = (t << bits) / (one + SqrtFloor((one << bits) + t * t));
            var square = (t * t) >> bits;
            var power = t;
            var sum = t;
            for (int n = 3; !power.IsZero; n += 2)
            {
                power = -((power * square) >> bits);
                sum += power / n;
            }
            return sum << 3;
        }

        // ---- 원주율 ----

        private static readonly BigInteger PiStart = ComputePi(StartBits);

        private static BigInteger PiAt(int bits) => bits == StartBits ? PiStart : ComputePi(bits);

        // 16 atan(1/5) - 4 atan(1/239)
        private static BigInteger ComputePi(int bits)
        {
            int guarded = bits + 16;
            return ((16 * AtanOfInverse(5, guarded) - 4 * AtanOfInverse(239, guarded)) >> 16);
        }

        private static BigInteger AtanOfInverse(int n, int bits)
        {
            var power = (BigInteger.One << bits) / n;
            var sum = power;
            var square = n * n;
            bool minus = true;
            for (int k = 3; !power.IsZero; k += 2)
            {
                power /= square;
                var term = power / k;
                sum += minus ? -term : term;
                minus = !minus;
            }
            return sum;
        }

        private static BigInteger FloorDivide(BigInteger a, BigInteger b)
        {
            var q = BigInteger.DivRem(a, b, out var r);
            return r.Sign < 0 ? q - 1 : q;
        }
    }
}
