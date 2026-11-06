using System;
using Kesa.Engine.Numerics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Numerics
{
    /// <summary>제곱근, 삼각함수, 거듭제곱 (G472): 결과는 참값에 가장 가까운 6자리다.</summary>
    public class FixedFunctionsTests
    {
        private static Fixed F(double x) => Fixed.FromRaw((long)Math.Round(x * Fixed.Scale));

        private static double D(Fixed x) => x.Raw / (double)Fixed.Scale;

        [TestCase(30, 500_000)]
        [TestCase(150, 500_000)]
        [TestCase(-30, -500_000)]
        [TestCase(90, 1_000_000)]
        [TestCase(180, 0)]
        [TestCase(270, -1_000_000)]
        [TestCase(360, 0)]
        [TestCase(45, 707_107)]
        [TestCase(1, 17_452)]
        public void SinInDegreesHitsTheGrid(int degrees, long raw) =>
            Assert.That(FixedFunctions.SinDegrees(F(degrees)).Raw, Is.EqualTo(raw));

        [TestCase(60, 500_000)]
        [TestCase(0, 1_000_000)]
        [TestCase(90, 0)]
        [TestCase(180, -1_000_000)]
        public void CosInDegreesHitsTheGrid(int degrees, long raw) =>
            Assert.That(FixedFunctions.CosDegrees(F(degrees)).Raw, Is.EqualTo(raw));

        [Test]
        public void RadiansAreNearestToo()
        {
            Assert.That(FixedFunctions.Sin(F(1)).Raw, Is.EqualTo(841_471));
            Assert.That(FixedFunctions.Cos(F(1)).Raw, Is.EqualTo(540_302));
            Assert.That(FixedFunctions.Sin(FixedFunctions.Pi).Raw, Is.EqualTo(0), "the rounded pi still gives 0");
            Assert.That(FixedFunctions.Sin(F(1_000_000)).Raw, Is.EqualTo(-349_994), "a large angle reduces exactly");
        }

        [TestCase(1, 1, 45_000_000)]
        [TestCase(1, 0, 90_000_000)]
        [TestCase(-1, 0, -90_000_000)]
        [TestCase(0, -1, 180_000_000)]
        [TestCase(0, 1, 0)]
        [TestCase(0, 0, 0)]
        [TestCase(1, -1, 135_000_000)]
        [TestCase(-1, -1, -135_000_000)]
        public void Atan2InDegreesHitsTheGrid(int y, int x, long raw) =>
            Assert.That(FixedFunctions.Atan2Degrees(F(y), F(x)).Raw, Is.EqualTo(raw));

        [Test]
        public void Atan2SwappedIsACompassBearing()
        {
            Assert.That(FixedFunctions.Atan2Degrees(F(1), F(0)).Raw, Is.EqualTo(90_000_000), "east of north");
            Assert.That(FixedFunctions.Atan2Degrees(F(0), F(1)).Raw, Is.EqualTo(0), "north");
        }

        /// <summary>double로 잰 참값과 0.5 자리 안쪽인지 본다. 입력은 시드 고정이다.</summary>
        [Test]
        public void ManyAnglesAreWithinHalfAUnitOfTheTrueValue()
        {
            var random = new Random(12345);
            const double limit = 0.5 + 1e-5;
            for (int i = 0; i < 3000; i++)
            {
                var x = Fixed.FromRaw(random.Next(-2_000_000_000, 2_000_000_000));
                var y = Fixed.FromRaw(random.Next(-2_000_000_000, 2_000_000_000));
                double radians = D(x);
                double degrees = radians * Math.PI / 180;
                Near(FixedFunctions.Sin(x), Math.Sin(radians), limit, "sin " + radians);
                Near(FixedFunctions.Cos(x), Math.Cos(radians), limit, "cos " + radians);
                Near(FixedFunctions.SinDegrees(x), Math.Sin(degrees), limit, "sin_deg " + radians);
                Near(FixedFunctions.CosDegrees(x), Math.Cos(degrees), limit, "cos_deg " + radians);
                Near(FixedFunctions.Atan2(y, x), Math.Atan2(D(y), D(x)), limit, "atan2 " + y + " " + x);
                Near(FixedFunctions.Atan2Degrees(y, x), Math.Atan2(D(y), D(x)) * 180 / Math.PI, limit, "atan2_deg " + y + " " + x);
                if (x.Raw >= 0)
                {
                    Assert.That(FixedFunctions.TrySqrt(x, out var root), Is.True);
                    Near(root, Math.Sqrt(radians), limit, "sqrt " + radians);
                }
            }
        }

        private static void Near(Fixed actual, double expected, double limit, string what) =>
            Assert.That(Math.Abs(actual.Raw - expected * Fixed.Scale), Is.LessThanOrEqualTo(limit), what + " = " + actual.Raw);

        [TestCase(2_000_000, 1_414_214)]
        [TestCase(250_000, 500_000)]
        [TestCase(1, 1_000)]
        [TestCase(0, 0)]
        [TestCase(4_000_000_000_000, 2_000_000_000)]
        public void SqrtHitsTheGrid(long raw, long root)
        {
            Assert.That(FixedFunctions.TrySqrt(Fixed.FromRaw(raw), out var result), Is.True);
            Assert.That(result.Raw, Is.EqualTo(root));
        }

        [Test]
        public void SqrtWorksAtTheTopOfTheRangeAndRefusesNegatives()
        {
            Assert.That(FixedFunctions.TrySqrt(Fixed.FromRaw(long.MaxValue), out var top), Is.True);
            Assert.That(top.Raw, Is.EqualTo(3_037_000_499_976L));
            Assert.That(FixedFunctions.TrySqrt(Fixed.FromRaw(-1), out _), Is.False);
        }

        [TestCase(2, 10, 1024)]
        [TestCase(0, 0, 1)]
        [TestCase(0, 5, 0)]
        [TestCase(-2, 3, -8)]
        [TestCase(-1, 1_000_000_000_000_000_001, -1)]
        [TestCase(1, long.MaxValue, 1)]
        public void IntPowers(long a, long n, long expected)
        {
            Assert.That(FixedFunctions.TryPower(a, n, out long result), Is.True);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void IntPowersOverflowQuickly()
        {
            Assert.That(FixedFunctions.TryPower(2L, 63L, out long _), Is.False);
            Assert.That(FixedFunctions.TryPower(-2L, 63L, out long lowest), Is.True);
            Assert.That(lowest, Is.EqualTo(long.MinValue));
            Assert.That(FixedFunctions.TryPower(2L, long.MaxValue, out long _), Is.False);
        }

        [Test]
        public void FixedPowersRoundOnlyOnce()
        {
            Assert.That(FixedFunctions.TryPower(F(1.05), 10, out var compound), Is.True);
            Assert.That(compound.Raw, Is.EqualTo(1_628_895));
            Assert.That(FixedFunctions.TryPower(F(-0.5), 3, out var negative), Is.True);
            Assert.That(negative.Raw, Is.EqualTo(-125_000));
            Assert.That(FixedFunctions.TryPower(F(7), 0, out var one), Is.True);
            Assert.That(one.Raw, Is.EqualTo(1_000_000));
            Assert.That(FixedFunctions.TryPower(F(0.5), FixedFunctions.MaxFixedExponent, out var tiny), Is.True);
            Assert.That(tiny.Raw, Is.EqualTo(0));
            Assert.That(FixedFunctions.TryPower(F(1000), 10, out _), Is.False);
        }
    }
}
