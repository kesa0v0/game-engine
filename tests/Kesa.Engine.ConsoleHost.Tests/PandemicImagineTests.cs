using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.ConsoleHost.Tests
{
    /// <summary>
    /// 숨긴 정보가 있는 실제 예시(판데믹 + brink)에서 봇의 시험 두기 (spec-gaps G462·G464): 뒷면 카드의 앞면은 시험 판에서 가려지고, 봇이 추측을
    /// 붙이면 차례를 끝까지 돌려 볼 수 있다. 진짜 판은 그대로다.
    /// </summary>
    public class PandemicImagineTests
    {
        private string _root = "";

        [SetUp]
        public void CopySample()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-pandemic-imagine-" + TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            foreach (var mod in new[] { "core", "brink" }) Copy(Path.Combine(PandemicTests.Root(), mod), Path.Combine(_root, mod));
            Write("probe/mod.py", "Mod(id=\"probe\", dependencies=[\"pandemic\", \"brink\"])\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            Write("probe/schema/bot.py", "bot_state(\"Probe\", notes=field(list[str], default=[]), filled=field(int, default=-1), second=field(str, default=\"unset\"))\n");
            Write("probe/defs/bot.py",
                "@bot\ndef peeker(me: Ref[Player]):\n" +
                "    with imagine():\n" +
                "        r = play(\"pandemic.EndTurn\", player=me)\n" +
                "        bot.Probe.notes = bot.Probe.notes + [r.outcome, r.error]\n" +
                "    with imagine():\n" +
                // 뒷면 카드는 앞면이 None(모름)이다. 도시 없는 카드라고 추측해 채우면 감염 단계까지 돈다
                "        hidden = [c for c in query(Card) if c.Face is None]\n" +
                "        for c in hidden:\n" +
                "            attach(c, Face(epidemic=False))\n" +
                "        r = play(\"pandemic.EndTurn\", player=me)\n" +
                "        bot.Probe.filled = len(hidden)\n" +
                "        bot.Probe.second = r.outcome if r.error is None else r.outcome + \": \" + r.error\n");
        }

        [TearDown]
        public void Clean()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(from)) Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        [Test]
        public void ABotImaginesEndingItsTurnWithoutSeeingTheDeck()
        {
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), new[] { "pandemic", "brink", "probe" }, bag), bag);
            Assert.That(bag.ToSortedList().Where(d => d.Severity == Severity.Error), Is.Empty);
            var world = new World(content, seed: 3);
            Assert.That(world.Submit("host", "pandemic.Start", new Dictionary<string, Value>()).Succeeded, Is.True);
            var alice = world.All("Player").Single(p => world.DefOf(p) == "pandemic:Alice");
            world.Seat(alice, "alice", SeatKind.Ai);
            byte[] before = world.Save();

            var bot = world.NewBot("probe.peeker", alice);
            var result = world.ViewAs("alice").Think(bot);

            Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), result.Error?.Message);
            // 추측 없이 차례를 끝내면 뒷면 카드의 앞면(None)을 읽다 멈춘다
            var notes = ((ListValue)bot.Stored("Probe", "notes")).Items.Select(v => ((StrValue)v).Value).ToList();
            Assert.That(notes[0], Is.EqualTo("script_error"));
            Assert.That(notes[1], Does.Contain("'None' has no attribute 'epidemic'"));
            // 가려진 카드는 여럿이고, 추측을 붙이면 그 카드는 지나 다음에 모르는 곳(도시)에서 멈춘다
            Assert.That(((IntValue)bot.Stored("Probe", "filled")).Value, Is.GreaterThan(10));
            Assert.That(((StrValue)bot.Stored("Probe", "second")).Value, Does.Not.Contain("'epidemic'"));
            Assert.That(world.Save(), Is.EqualTo(before));
        }
    }
}
