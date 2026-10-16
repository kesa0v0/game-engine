using System.Linq;
using Kesa.Engine.Numerics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Numerics
{
    /// <summary>RFC 8439의 테스트 값 (spec-gaps G181, C6). 난수 해시가 표준 ChaCha20 그대로인지 묶는다.</summary>
    public class ChaCha20Tests
    {
        [Test]
        public void QuarterRoundMatchesRfc8439Section2_1_1()
        {
            uint a = 0x11111111, b = 0x01020304, c = 0x9b8d6f43, d = 0x01234567;
            ChaCha20.QuarterRound(ref a, ref b, ref c, ref d);
            Assert.That(new[] { a, b, c, d }, Is.EqualTo(new uint[] { 0xea2a92f4, 0xcb1cf8ce, 0x4581472e, 0x5881c4bb }));
        }

        [Test]
        public void BlockMatchesRfc8439Section2_3_2()
        {
            // 키 00 01 .. 1f, nonce 00:00:00:09:00:00:00:4a:00:00:00:00, 블록 카운터 1
            var key = new uint[8];
            for (int i = 0; i < 8; i++)
                key[i] = (uint)(4 * i) | (uint)(4 * i + 1) << 8 | (uint)(4 * i + 2) << 16 | (uint)(4 * i + 3) << 24;
            var nonce = new uint[] { 0x09000000, 0x4a000000, 0x00000000 };
            var output = new uint[16];
            ChaCha20.Block(key, 1, nonce, output);
            var bytes = output.SelectMany(w => new[] { (byte)w, (byte)(w >> 8), (byte)(w >> 16), (byte)(w >> 24) }).ToArray();
            Assert.That(string.Concat(bytes.Select(x => x.ToString("x2"))), Is.EqualTo(
                "10f1e7e4d13b5915500fdd1fa32071c4c7d1f4c733c068030422aa9ac3d46c4e" +
                "d2826446079faa0914c2d705d98b02a2b5129cd1de164eb9cbd083e8a2503c4e"));
        }
    }
}
