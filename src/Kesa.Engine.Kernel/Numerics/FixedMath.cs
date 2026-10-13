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
