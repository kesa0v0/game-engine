using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Ui.Tests
{
    /// <summary>
    /// 아무도 그리지 않은 질문의 엔진 기본 창 (spec-gaps G386): 화면이 <c>questions()</c>로 읽지도, 응답 커맨드 버튼을 달지도 않은 열린 질문은
    /// 해석기가 제목과 답 버튼이 든 창으로 보이고 개발 경고를 남긴다.
    /// </summary>
    public class QuestionFallbackTests
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\ndeftype(\"Party\", components=[\"Hp\", \"Seatable\"])\n";

        private const string Defs =
            "Party(\"Red\")\n" +
            "@command(free=True, acts_for=None)\ndef Strike(p: Ref[Party]):\n" +
            "    r = ask(p, \"hit-title\", name=\"hit\", choices=[option(\"hold\", desc=\"hold-desc\"), \"retreat\"])\n    p.Hp.v += r.index\n" +
            "@command(acts_for=lambda p: p)\ndef Move(p: Ref[Party]):\n    p.Hp.v += 10\n" +
            "@command(free=True, acts_for=None)\ndef Turn(p: Ref[Party]):\n    ask(p, \"turn\", name=\"turn\", responses=[\"Move\"])\n" +
            "def hp(p):\n    return p.Hp.v\n" +
            "def hits():\n    return len(questions(\"hit\"))\n";

        private string _root = "";
        private LocalHost _host = null!;
        private MemoryRenderer _renderer = null!;
        private UiInterpreter _ui = null!;
        private long _red;

        [SetUp]
        public async Task OpenWorld()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-ui-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("quiz/schema/types.py", Schema);
            Write("quiz/defs/defs.py", Defs);
            Write("quiz/mod.py", "Mod(id=\"quiz\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            var world = new World(content);
            _red = world.Create("quiz:Red").Value;
            _host = new LocalHost(content, world);
            await _host.Seat(_red, "alice");
            _renderer = new MemoryRenderer();
            _ui = new UiInterpreter(await _host.Open("alice"), _renderer, v => v is BridgeStr s ? s.Value : v.ToString(), _ => null);
        }

        [TearDown]
        public void CloseWorld()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            string path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private async Task Mount(string body)
        {
            var read = UxmlReader.Read("test.uxml", "<ui:VisualElement params=\"p\">" + body + "</ui:VisualElement>");
            Assert.That(read.Problems, Is.Empty, string.Join("\n", read.Problems));
            var result = await _ui.MountAsync(read.Document!, new Dictionary<string, BridgeValue> { ["p"] = new BridgeEntity(_red, null) });
            Assert.That(result.Problems, Is.Empty, string.Join("\n", result.Problems));
        }

        private void Open(string command) =>
            Assert.That(_host.World.Submit("host", command, new Dictionary<string, Value> { ["p"] = new EntityValue(new EntityId(_red)) }).Succeeded,
                Is.True);

        private long Hp => ((IntValue)_host.World.Get(new EntityId(_red), _host.World.Member("Hp", "v"))).Value;

        private List<MemoryRenderer.El> Fallbacks => _renderer.Tops.Where(t => !t.Removed && t.Classes.Contains("kesa-question")).ToList();

        [Test]
        public async Task AChoiceQuestionNobodyDrawsGetsTheEngineWindowAndAnsweringClosesIt()
        {
            await Mount("<ui:Label bind-text=\"quiz.hp(p)\" />");
            Open("quiz.Strike");
            await _ui.RefreshAsync();

            var window = Fallbacks.Single();
            var answers = window.Children.Where(c => c.Kind == "Button").ToList();
            Assert.That(answers.Select(b => b.Text), Is.EqualTo(new[] { "hold", "retreat" }));
            Assert.That(answers.Select(b => string.Join(",", b.Children.Select(c => c.Text + "/" + string.Join(" ", c.Classes)))),
                Is.EqualTo(new[] { "hold-desc/kesa-question-desc", "" }), "a choice's desc is a second line in its button (G448)");
            Assert.That(_ui.Errors.Single().ToString(), Does.Contain("question 'hit'").And.Contain("G386"));

            await answers[1].Press();
            Assert.That(Hp, Is.EqualTo(4));
            Assert.That(Fallbacks, Is.Empty);
        }

        [Test]
        public async Task AScreenThatReadsTheQuestionsKindGetsNoEngineWindow()
        {
            await Mount("<ui:Label bind-text=\"quiz.hits()\" />");
            Open("quiz.Strike");
            await _ui.RefreshAsync();
            Assert.That(Fallbacks, Is.Empty);
            Assert.That(_ui.Errors, Is.Empty);
        }

        [Test]
        public async Task AButtonForTheResponseCommandCountsAsDrawingIt()
        {
            await Mount("<ui:Button text=\"move\" on-click=\"quiz.Move(p)\" />");
            Open("quiz.Turn");
            await _ui.RefreshAsync();
            Assert.That(Fallbacks, Is.Empty);
            Assert.That(_ui.Errors, Is.Empty);
        }

        [Test]
        public async Task AResponseQuestionWithoutAButtonListsItsLegalResponses()
        {
            await Mount("<ui:Label bind-text=\"quiz.hp(p)\" />");
            Open("quiz.Turn");
            await _ui.RefreshAsync();

            var answer = Fallbacks.Single().Children.Single(c => c.Kind == "Button");
            Assert.That(answer.Text, Does.StartWith("quiz.Move"));
            await answer.Press();
            Assert.That(Hp, Is.EqualTo(13));
            Assert.That(Fallbacks, Is.Empty);
        }

        [Test]
        public async Task TheWarningIsOncePerQuestionNotPerRefresh()
        {
            await Mount("<ui:Label bind-text=\"quiz.hp(p)\" />");
            Open("quiz.Strike");
            await _ui.RefreshAsync();
            await _ui.RefreshAsync();
            Assert.That(Fallbacks.Count, Is.EqualTo(1));
            Assert.That(_ui.Errors.Count, Is.EqualTo(1));
        }
    }
}
