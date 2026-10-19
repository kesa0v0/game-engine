using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Kesa.Engine.ConsoleHost.Tests
{
    /// <summary>예시 게임 <c>samples/skirmish</c>를 콘솔 호스트로 끝까지 돌린다.</summary>
    public class SkirmishTests
    {
        private static string Samples()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "samples", "skirmish");
                if (Directory.Exists(path)) return path;
            }
            throw new DirectoryNotFoundException("samples/skirmish");
        }

        /// <summary>
        /// 화면을 읽고 고르는 입력: 맞으면 버티고, 공격할 수 있으면 공격하고, 아니면 상대 쪽으로 걷고, 그다음 차례를 넘긴다.
        /// </summary>
        private sealed class Bot : TextReader
        {
            private readonly StringWriter _screen;
            private readonly int _limit;
            private int _reads;

            public Bot(StringWriter screen, int limit)
            {
                _screen = screen;
                _limit = limit;
            }

            public override string? ReadLine()
            {
                if (++_reads > _limit) return null;
                string text = _screen.ToString();
                string block = text.Substring(text.LastIndexOf("== 틱", StringComparison.Ordinal));
                var options = Regex.Matches(block, @"^  (\d+)\) (.*)$", RegexOptions.Multiline)
                    .Select(m => (number: m.Groups[1].Value, label: m.Groups[2].Value)).ToList();
                foreach (var want in new[] { ": hold", "Attack(" })
                {
                    var hit = options.FirstOrDefault(o => o.label.Contains(want));
                    if (hit.label != null) return hit.number;
                }
                var closer = options.Where(o => o.label.Contains("Move(")).OrderBy(o => Gain(block, o.label)).FirstOrDefault();
                if (closer.label != null && Gain(block, closer.label) < 0) return closer.number;
                var end = options.FirstOrDefault(o => o.label.Contains("EndTurn("));
                return end.label != null ? end.number : "1";
            }

            /// <summary>그 이동으로 가장 가까운 적까지의 거리가 얼마나 바뀌나 (음수면 가까워짐).</summary>
            private static int Gain(string block, string move)
            {
                var units = Regex.Matches(block, @"^#(\d+) \w+  Pos\(x=(\d+), y=(\d+)\).*party=#(\d+)", RegexOptions.Multiline)
                    .ToDictionary(m => m.Groups[1].Value,
                        m => (x: int.Parse(m.Groups[2].Value), y: int.Parse(m.Groups[3].Value), party: m.Groups[4].Value));
                var m = Regex.Match(move, @"dir=(\w+).*unit=#(\d+)");
                var me = units[m.Groups[2].Value];
                int dx = m.Groups[1].Value == "west" ? -1 : m.Groups[1].Value == "east" ? 1 : 0;
                int dy = m.Groups[1].Value == "north" ? -1 : m.Groups[1].Value == "south" ? 1 : 0;
                int Nearest(int x, int y) => units.Values.Where(u => u.party != me.party)
                    .Min(u => Math.Abs(u.x - x) + Math.Abs(u.y - y));
                return Nearest(me.x + dx, me.y + dy) - Nearest(me.x, me.y);
            }
        }

        private static string PlayOut(int seed, int limit = 500)
        {
            var screen = new StringWriter();
            var errors = new StringWriter();
            int code = Play.Run(new[] { Samples(), "--seat", "red", "--seat", "blue", "--seed", seed.ToString() },
                new Bot(screen, limit), screen, errors);
            Assert.That(code, Is.EqualTo(0), errors.ToString());
            return screen.ToString();
        }

        [Test]
        public void TheBotsPlayAGameToTheEnd()
        {
            string screen = PlayOut(seed: 1);
            Assert.That(screen, Does.EndWith("낼 수 있는 것이 없다. 끝.\n").Or.EndWith("낼 수 있는 것이 없다. 끝.\r\n"));
            Assert.That(screen, Does.Match(@"winner=#\d \w+"));
        }

        [Test]
        public void TheHitQuestionGoesToTheDefendersSeat()
        {
            // 빨강이 공격해 명중하면 질문은 파랑 진영에게 가고, 파랑 자리가 답한다(입구 정책)
            string screen = PlayOut(seed: 1);
            var asked = Regex.Match(screen, @"질문 \d+: hit → #2 Blue[^\n]*\n(?:  \d+\)[^\n]*\n)+> (\[\w+\])");
            Assert.That(asked.Success, Is.True);
            Assert.That(asked.Groups[1].Value, Is.EqualTo("[blue]"));
        }

        [Test]
        public void TheSameSeedPlaysTheSameGame()
        {
            Assert.That(PlayOut(seed: 3), Is.EqualTo(PlayOut(seed: 3)));
            Assert.That(PlayOut(seed: 3), Is.Not.EqualTo(PlayOut(seed: 4)));
        }

        [Test]
        public void AWrongNumberAsksAgain()
        {
            var screen = new StringWriter();
            Play.Run(new[] { Samples(), "--seat", "red" }, new StringReader("9\nx\n:q\n"), screen, new StringWriter());
            Assert.That(Regex.Matches(screen.ToString(), "1부터 1 사이의 번호").Count, Is.EqualTo(2));
        }

        [Test]
        public void MissingSeatsOrFolderAreUsageErrors()
        {
            var errors = new StringWriter();
            Assert.That(Play.Run(new[] { Samples() }, new StringReader(""), new StringWriter(), errors), Is.EqualTo(2));
            Assert.That(Play.Run(new[] { "no/such/dir", "--seat", "a" }, new StringReader(""), new StringWriter(), errors),
                Is.EqualTo(2));
        }
    }
}
