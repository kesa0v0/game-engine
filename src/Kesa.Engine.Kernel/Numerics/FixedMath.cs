using System.Numerics;

namespace Kesa.Engine.Numerics
{
    /// <summary>
    /// <c>int</c>와 <c>fixed</c>의 결정적 산술 (spec P2): 오버플로는 오류(false), 곱셈은 128비트 이상의 중간값을 쓰고
    /// 반올림은 0에서 먼 쪽이다. 로드 시점의 <c>inherit</c> 식(spec-gaps G56)이 쓴다.
    /// </summary>
    public static class FixedMath
    {
        public static bool TryAdd(long a, long b, out long result)
        {
            try
            {
                result = checked(a + b);
                return true;
            }
            catch (System.OverflowException)
            {
                result = 0;
                return false;
            }
        }

        public static bool TrySubtract(long a, long b, out long result)
        {
            try
            {
                result = checked(a - b);
                return true;
            }
            catch (System.OverflowException)
            {
                result = 0;
                return false;
            }
        }

        public static bool TryMultiply(long a, long b, out long result)
        {
            try
            {
                result = checked(a * b);
                return true;
            }
            catch (System.OverflowException)
            {
                result = 0;
                return false;
            }
        }

        public static bool TryNegate(long a, out long result)
        {
            if (a == long.MinValue)
            {
                result = 0;
                return false;
            }
            result = -a;
            return true;
        }

        /// <summary><c>int</c>를 <c>fixed</c>의 원시값으로 올린다.</summary>
        public static bool TryFromInt(long value, out Fixed result)
        {
            if (!TryMultiply(value, Fixed.Scale, out long raw))
            {
                result = default;
                return false;
            }
            result = Fixed.FromRaw(raw);
            return true;
        }

        public static bool TryAdd(Fixed a, Fixed b, out Fixed result)
        {
            bool ok = TryAdd(a.Raw, b.Raw, out long raw);
            result = ok ? Fixed.FromRaw(raw) : default;
            return ok;
        }

        public static bool TrySubtract(Fixed a, Fixed b, out Fixed result)
        {
            bool ok = TrySubtract(a.Raw, b.Raw, out long raw);
            result = ok ? Fixed.FromRaw(raw) : default;
            return ok;
        }

        public static bool TryNegate(Fixed a, out Fixed result)
        {
            bool ok = TryNegate(a.Raw, out long raw);
            result = ok ? Fixed.FromRaw(raw) : default;
            return ok;
        }

        /// <summary>
        /// 두 <c>fixed</c>의 나눗셈 (spec-gaps G95의 <c>/</c>). 중간값은 128비트 이상이고 반올림은 0에서 먼 쪽이다.
        /// 0으로 나누거나 범위를 넘으면 false.
        /// </summary>
        public static bool TryDivide(Fixed a, Fixed b, out Fixed result)
        {
            result = default;
            if (b.Raw == 0) return false;
            var numerator = new BigInteger(a.Raw) * Fixed.Scale;
            var denominator = new BigInteger(b.Raw);
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (!remainder.IsZero && BigInteger.Abs(remainder) * 2 >= BigInteger.Abs(denominator))
                quotient += numerator.Sign * denominator.Sign;
            if (quotient < long.MinValue || quotient > long.MaxValue) return false;
            result = Fixed.FromRaw((long)quotient);
            return true;
        }

        /// <summary>정수의 <c>//</c> (floor). 0으로 나누거나 <c>long.MinValue // -1</c>이면 false.</summary>
        public static bool TryFloorDivide(long a, long b, out long result)
        {
            result = 0;
            if (b == 0 || (a == long.MinValue && b == -1)) return false;
            long q = a / b;
            if (a % b != 0 && ((a < 0) != (b < 0))) q--;
            result = q;
            return true;
        }

        /// <summary>정수의 <c>%</c> (floor 기준: 결과의 부호는 나누는 수를 따른다). 0으로 나누면 false.</summary>
        public static bool TryFloorModulo(long a, long b, out long result)
        {
            result = 0;
            if (b == 0) return false;
            if (b == -1) return true; // 나머지는 늘 0 (long.MinValue % -1이 예외를 내므로 따로 처리)
            long m = a % b;
            if (m != 0 && ((m < 0) != (b < 0))) m += b;
            result = m;
            return true;
        }

        /// <summary><c>fixed // fixed</c>: 몫을 floor한 정수 값을 <c>fixed</c>로 돌려준다 (7.5 // 2 = 3.0).</summary>
        public static bool TryFloorDivide(Fixed a, Fixed b, out Fixed result)
        {
            result = default;
            if (!TryFloorDivide(a.Raw, b.Raw, out long q)) return false;
            if (!TryFromInt(q, out result)) return false;
            return true;
        }

        /// <summary><c>fixed % fixed</c> (floor 기준).</summary>
        public static bool TryFloorModulo(Fixed a, Fixed b, out Fixed result)
        {
            result = default;
            if (!TryFloorModulo(a.Raw, b.Raw, out long m)) return false;
            result = Fixed.FromRaw(m);
            return true;
        }

        /// <summary>내림. 결과는 항상 <c>long</c>에 들어간다.</summary>
        public static long Floor(Fixed a)
        {
            long q = a.Raw / Fixed.Scale;
            if (a.Raw % Fixed.Scale < 0) q--;
            return q;
        }

        /// <summary>올림.</summary>
        public static long Ceiling(Fixed a)
        {
            long q = a.Raw / Fixed.Scale;
            if (a.Raw % Fixed.Scale > 0) q++;
            return q;
        }

        /// <summary>반올림: 0.5는 0에서 먼 쪽 (파이썬 <c>round()</c>의 은행가 반올림이 아님, spec P2).</summary>
        public static long Round(Fixed a)
        {
            long q = a.Raw / Fixed.Scale;
            long rem = a.Raw % Fixed.Scale;
            if (rem < 0 ? -rem * 2 >= Fixed.Scale : rem * 2 >= Fixed.Scale) q += a.Raw < 0 ? -1 : 1;
            return q;
        }

        public static int Compare(Fixed a, Fixed b) => a.Raw.CompareTo(b.Raw);

        /// <summary><c>int</c>와 <c>fixed</c>의 비교. 승격하면 넘칠 수 있으므로 큰 정수로 비교한다.</summary>
        public static int Compare(long a, Fixed b) => (new BigInteger(a) * Fixed.Scale).CompareTo(new BigInteger(b.Raw));

        /// <summary>두 <c>fixed</c>의 곱. 중간값은 <see cref="BigInteger"/>이고 반올림은 0에서 먼 쪽이다.</summary>
        public static bool TryMultiply(Fixed a, Fixed b, out Fixed result)
        {
            var product = new BigInteger(a.Raw) * new BigInteger(b.Raw);
            var scale = new BigInteger(Fixed.Scale);
            var quotient = BigInteger.DivRem(product, scale, out var remainder);
            if (!remainder.IsZero)
            {
                // |나머지| * 2 >= 스케일 이면 0에서 먼 쪽으로 올린다
                if (BigInteger.Abs(remainder) * 2 >= scale) quotient += product.Sign;
            }
            if (quotient < long.MinValue || quotient > long.MaxValue)
            {
                result = default;
                return false;
            }
            result = Fixed.FromRaw((long)quotient);
            return true;
        }
    }
}
