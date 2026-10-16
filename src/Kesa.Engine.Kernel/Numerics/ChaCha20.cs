namespace Kesa.Engine.Numerics
{
    /// <summary>
    /// ChaCha20 블록 함수 (RFC 8439 2.3, 20라운드). 난수의 해시 H다 (spec S2 난수, spec-gaps G181). 덧셈·회전·XOR만 쓰므로
    /// 플랫폼과 프로세스에 따라 값이 달라지지 않는다. 바꾸면 예전 세이브가 다른 난수를 내므로 고정이다 (C6).
    /// </summary>
    internal static class ChaCha20
    {
        /// <summary>RFC 8439의 상수 "expand 32-byte k".</summary>
        private const uint C0 = 0x61707865, C1 = 0x3320646e, C2 = 0x79622d32, C3 = 0x6b206574;

        private static uint Rotate(uint v, int n) => (v << n) | (v >> (32 - n));

        /// <summary>쿼터 라운드 (RFC 8439 2.1).</summary>
        internal static void QuarterRound(ref uint a, ref uint b, ref uint c, ref uint d)
        {
            unchecked
            {
                a += b; d ^= a; d = Rotate(d, 16);
                c += d; b ^= c; b = Rotate(b, 12);
                a += b; d ^= a; d = Rotate(d, 8);
                c += d; b ^= c; b = Rotate(b, 7);
            }
        }

        /// <summary>
        /// 블록 하나: 키 32바이트(<paramref name="key"/> 8워드), 블록 카운터, nonce 12바이트(<paramref name="nonce"/> 3워드)로
        /// 64바이트(<paramref name="output"/> 16워드, 리틀 엔디언 워드)를 낸다.
        /// </summary>
        internal static void Block(uint[] key, uint counter, uint[] nonce, uint[] output)
        {
            var s = new uint[16];
            s[0] = C0; s[1] = C1; s[2] = C2; s[3] = C3;
            for (int i = 0; i < 8; i++) s[4 + i] = key[i];
            s[12] = counter;
            s[13] = nonce[0]; s[14] = nonce[1]; s[15] = nonce[2];
            var x = (uint[])s.Clone();
            for (int round = 0; round < 10; round++)
            {
                // 열 라운드
                QuarterRound(ref x[0], ref x[4], ref x[8], ref x[12]);
                QuarterRound(ref x[1], ref x[5], ref x[9], ref x[13]);
                QuarterRound(ref x[2], ref x[6], ref x[10], ref x[14]);
                QuarterRound(ref x[3], ref x[7], ref x[11], ref x[15]);
                // 대각선 라운드
                QuarterRound(ref x[0], ref x[5], ref x[10], ref x[15]);
                QuarterRound(ref x[1], ref x[6], ref x[11], ref x[12]);
                QuarterRound(ref x[2], ref x[7], ref x[8], ref x[13]);
                QuarterRound(ref x[3], ref x[4], ref x[9], ref x[14]);
            }
            unchecked
            {
                for (int i = 0; i < 16; i++) output[i] = x[i] + s[i];
            }
        }
    }
}
