using System;
using System.Collections.Generic;
using System.Numerics;
using Kesa.Engine.Numerics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Numerics
{
    /// <summary>할당 없는 곱·나눗셈·비교가 큰 정수로 계산한 정의와 비트까지 같다 (spec P2: 반올림은 0에서 먼 쪽, 넘치면 false).</summary>
    public class FixedMathTests
    {
        private static IEnumerable<long> Values()
        {
            var edges = new long[]
            {
                0, 1, -1, 2, -2, 499_999, 500_000, 500_001, -500_000, -500_001, 999_999, 1_000_000, -1_000_000, 1_500_000, -1_500_000,
                3_037_000_499, 3_037_000_500, -3_037_000_500, long.MaxValue / Fixed.Scale, long.MaxValue / Fixed.Scale + 1,
                -(long.MaxValue / Fixed.Scale), -(long.MaxValue / Fixed.Scale) - 1, long.MinValue / Fixed.Scale, long.MinValue / Fixed.Scale - 1,
                int.MaxValue, int.MinValue, (long)uint.MaxValue, 1L << 32, 1L << 62, -(1L << 62), long.MaxValue, long.MinValue, long.MaxValue - 1,
                long.MinValue + 1, 9_223_372_036_854_775, -9_223_372_036_854_776,
            };
            foreach (long e in edges) yield return e;
            var random = new Random(7);
            var buffer = new byte[8];
            for (int i = 0; i < 400; i++)
            {
                random.NextBytes(buffer);
                long raw = BitConverter.ToInt64(buffer, 0);
                // 여러 크기가 고루 나오게 오른쪽으로 민다
                yield return raw >> random.Next(0, 63);
            }
        }

        private static bool Multiply(long a, long b, out long result)
        {
            var product = new BigInteger(a) * b;
            var quotient = BigInteger.DivRem(product, Fixed.Scale, out var remainder);
            if (BigInteger.Abs(remainder) * 2 >= Fixed.Scale) quotient += product.Sign;
            result = quotient >= long.MinValue && quotient <= long.MaxValue ? (long)quotient : 0;
            return quotient >= long.MinValue && quotient <= long.MaxValue;
        }

        private static bool Divide(long a, long b, out long result)
        {
            result = 0;
            if (b == 0) return false;
            var numerator = new BigInteger(a) * Fixed.Scale;
            var quotient = BigInteger.DivRem(numerator, b, out var remainder);
            if (!remainder.IsZero && BigInteger.Abs(remainder) * 2 >= BigInteger.Abs(b)) quotient += numerator.Sign * Math.Sign(b);
            if (quotient < long.MinValue || quotient > long.MaxValue) return false;
            result = (long)quotient;
            return true;
        }

        [Test]
        public void MultiplyDivideAndCompareMatchTheBigIntegerDefinition()
        {
            var values = new List<long>(Values());
            int checkedPairs = 0;
            foreach (long a in values)
            {
                foreach (long b in values)
                {
                    bool ok = Multiply(a, b, out long expected);
                    Assert.That(FixedMath.TryMultiply(Fixed.FromRaw(a), Fixed.FromRaw(b), out var product), Is.EqualTo(ok), a + " * " + b);
                    if (ok) Assert.That(product.Raw, Is.EqualTo(expected), a + " * " + b);

                    ok = Divide(a, b, out expected);
                    Assert.That(FixedMath.TryDivide(Fixed.FromRaw(a), Fixed.FromRaw(b), out var quotient), Is.EqualTo(ok), a + " / " + b);
                    if (ok) Assert.That(quotient.Raw, Is.EqualTo(expected), a + " / " + b);

                    int compared = (new BigInteger(a) * Fixed.Scale).CompareTo(new BigInteger(b));
                    Assert.That(Math.Sign(FixedMath.Compare(a, Fixed.FromRaw(b))), Is.EqualTo(compared), a + " <> " + b);
                    checkedPairs++;
                }
            }
            Assert.That(checkedPairs, Is.GreaterThan(190_000));
        }
    }
}
