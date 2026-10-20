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
                // 모드의 units 패널(ui/console.txt)을 읽는다. 자리마다 한 번씩 보이므로 같은 유닛이 두 번 나온다
                var units = Regex.Matches(block, @"^  \{unit: #(\d+) [\w:]+, x: (\d+), y: (\d+), .*party: #(\d+)", RegexOptions.Multiline)
                    .GroupBy(m => m.Groups[1].Value).Select(g => g.First())
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
            int code = Play.Run(new[] { "--lang", "ko", Samples(), "--seat", "red=skirmish:Red", "--seat", "blue=skirmish:Blue", "--seed", seed.ToString() },
                new Bot(screen, limit), screen, errors);
            Assert.That(code, Is.EqualTo(0), errors.ToString());
            return screen.ToString();
        }

        [Test]
        public void ASeatWithoutADefIsPickedFromTheChoices()
        {
            // --seat red: 앉을 후보(Seatable, G239)가 생기면 묻는다. 잘못된 번호는 다시 묻고, 남이 앉은 자리는 빠진다
            var screen = new StringWriter();
            var errors = new StringWriter();
            int code = Play.Run(new[] { "--lang", "ko", Samples(), "--seat", "red", "--seat", "blue" },
                new StringReader("1\n9\n2\n1\n:q\n"), screen, errors);
            Assert.That(code, Is.EqualTo(0), errors.ToString());
            string text = screen.ToString();
            Assert.That(text, Does.Contain("앉을 자리: 1) #1 빨강  2) #2 파랑"));
            Assert.That(text, Does.Contain("[red] #2 파랑에 앉음"));
            Assert.That(text, Does.Contain("앉을 자리: 1) #1 빨강\n").Or.Contain("앉을 자리: 1) #1 빨강\r\n"));
            Assert.That(text, Does.Contain("[blue] #1 빨강에 앉음"));
            Assert.That(text, Does.Contain("[blue] skirmish.EndTurn(party=#1 빨강)"));
        }

        [Test]
        public void TheBotsPlayAGameToTheEnd()
        {
            string screen = PlayOut(seed: 1);
            Assert.That(screen, Does.EndWith("낼 수 있는 것이 없다. 끝.\n").Or.EndWith("낼 수 있는 것이 없다. 끝.\r\n"));
            Assert.That(screen, Does.Match(@"winner: #\d [\w:]+"));
        }

        [Test]
        public void TheHitQuestionGoesToTheDefendersSeat()
        {
            // 빨강이 공격해 명중하면 질문은 파랑 진영에게 가고, 파랑에 앉은 연결이 답한다(배정 표, G236)
            string screen = PlayOut(seed: 1);
            var asked = Regex.Match(screen, @"질문 \d+: 명중! → #2 파랑[^\n]*\n(?:  \d+\)[^\n]*\n)+> (\[\w+\])");
            Assert.That(asked.Success, Is.True);
            Assert.That(asked.Groups[1].Value, Is.EqualTo("[blue]"));
        }

        [Test]
        public void EachChoiceIsListedOnceAsAPickMove()
        {
            // 선택지 답도 LegalResponses의 engine.pick 수라 한 번씩만 나온다 (G272)
            string screen = PlayOut(seed: 1);
            var asked = Regex.Match(screen, @"질문 (\d+): 명중! → #2 파랑[^\n]*\r?\n((?:  \d+\)[^\n]*\n)+)");
            Assert.That(asked.Success, Is.True);
            // 화면 동작(G285)은 질문과 상관없이 뒤에 붙는다
            var lines = Regex.Matches(asked.Groups[2].Value, @"\d+\) ([^\r\n]*)").Select(m => m.Groups[1].Value)
                .Where(l => !l.Contains("[화면]"));
            string q = asked.Groups[1].Value;
            Assert.That(lines, Is.EqualTo(new[]
            {
                "[blue] 질문 " + q + " #2 파랑: 버틴다",
                "[blue] 질문 " + q + " #2 파랑: 물러난다",
            }));
        }

        [Test]
        public void SkippingUnchangedPanelsByReceiptDrawsTheSameScreens()
        {
            // 스커미시는 ui/console.txt가 있어 패널의 영수증(CallResult.Changed, G246·G250)으로 건너뛴다
            string skipped = PlayOut(seed: 2);
            ConsoleGame.AlwaysRedraw = true;
            try
            {
                Assert.That(PlayOut(seed: 2), Is.EqualTo(skipped));
            }
            finally
            {
                ConsoleGame.AlwaysRedraw = false;
            }
        }

        /// <summary>적힌 글을 차례로 찾아 그 메뉴 번호를 고르는 입력. 다 고르면 끝낸다.</summary>
        private sealed class Script : TextReader
        {
            private readonly StringWriter _screen;
            private readonly string[] _wants;
            private int _next;

            public Script(StringWriter screen, params string[] wants)
            {
                _screen = screen;
                _wants = wants;
            }

            public override string? ReadLine()
            {
                if (_next == _wants.Length) return null;
                string text = _screen.ToString();
                string block = text.Substring(text.LastIndexOf("== 틱", StringComparison.Ordinal));
                string want = _wants[_next++];
                var hit = Regex.Matches(block, @"^  (\d+)\) (.*)$", RegexOptions.Multiline)
                    .FirstOrDefault(m => m.Groups[2].Value.Contains(want));
                Assert.That(hit, Is.Not.Null, "no move '" + want + "' in\n" + block);
                return hit!.Groups[1].Value;
            }
        }

        [Test]
        public void SelectingAUnitThenStrikingAnswersTheTurnQuestion()
        {
            // 화면 동작은 커맨드 뒤에 [화면]으로 나오고, me에는 앉은 진영이 들어간다 (G285·G286)
            var screen = new StringWriter();
            var errors = new StringWriter();
            int code = Play.Run(new[] { "--lang", "ko", Samples(), "--seat", "red=skirmish:Red", "--seat", "blue=skirmish:Blue", "--seed", "1" },
                new Script(screen,
                    "[red] skirmish.Start()",
                    "Move(dir=north, party=#1 빨강, unit=#5",
                    "[red] skirmish.EndTurn(",
                    "Move(dir=south, party=#2 파랑, unit=#8",
                    "[blue] skirmish.EndTurn(",
                    "[red] [화면] skirmish.select(unit=#5 궁수)",
                    "[red] [화면] skirmish.strike(target=#8 궁수)"),
                screen, errors);
            Assert.That(code, Is.EqualTo(0), errors.ToString());
            string text = screen.ToString();
            // 고르기 전에는 공격 동작이 없다(require가 막음), 고른 뒤에는 그 자리의 화면 함수가 고른 유닛을 보인다
            int selected = text.IndexOf("{selected: #5 궁수}", StringComparison.Ordinal);
            Assert.That(selected, Is.GreaterThan(0));
            Assert.That(text.IndexOf("skirmish.strike(", StringComparison.Ordinal), Is.GreaterThan(selected));
            // strike가 적은 Attack은 빨강이 답할 차례 질문의 답으로 들어간다 (G283)
            Assert.That(text, Does.Contain("[red] skirmish.Attack(party=#1 빨강, target=#8 궁수, unit=#5 궁수): ok"));
            Assert.That(text, Does.Contain("{unit: #5 궁수, x: 2, y: 3, hp: 6, acted: true"));
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
            Play.Run(new[] { "--lang", "ko", Samples(), "--seat", "red=skirmish:Red" }, new StringReader("9\nx\n:q\n"), screen, new StringWriter());
            Assert.That(Regex.Matches(screen.ToString(), "1부터 1 사이의 번호").Count, Is.EqualTo(2));
        }

        [Test]
        public void MissingSeatsOrFolderAreUsageErrors()
        {
            var errors = new StringWriter();
            Assert.That(Play.Run(new[] { "--lang", "ko", Samples() }, new StringReader(""), new StringWriter(), errors), Is.EqualTo(2));
            Assert.That(Play.Run(new[] { "--lang", "ko", Samples(), "--seat" }, new StringReader(""), new StringWriter(), errors), Is.EqualTo(2));
            Assert.That(Play.Run(new[] { "--lang", "ko", Samples(), "--seat", "=A" }, new StringReader(""), new StringWriter(), errors),
                Is.EqualTo(2));
            Assert.That(Play.Run(new[] { "--lang", "ko", "no/such/dir", "--seat", "a=A" }, new StringReader(""), new StringWriter(), errors),
                Is.EqualTo(2));
        }

        // 언어는 --lang으로, 태그가 아니면 사용법 오류 (G274)
        [Test]
        public void TheLanguageComesFromLang()
        {
            var screen = new StringWriter();
            var errors = new StringWriter();
            int code = Play.Run(new[] { Samples(), "--seat", "red", "--seat", "blue", "--lang", "fr,en" },
                new StringReader("1\n:q\n"), screen, errors);
            Assert.That(code, Is.EqualTo(0), errors.ToString());
            Assert.That(screen.ToString(), Does.Contain("== tick 0 ==").And.Contain("Seats: 1) #1 Red  2) #2 Blue"));
            Assert.That(errors.ToString(), Is.Empty, "the sample has every text it shows");

            Assert.That(Play.Run(new[] { Samples(), "--seat", "red", "--lang", "ko_KR" }, new StringReader(""), new StringWriter(), errors),
                Is.EqualTo(2));
            Assert.That(Play.DefaultLanguages()[^1], Is.EqualTo("en"));
        }
    }
}
