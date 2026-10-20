using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 커맨드 본문의 <c>match</c> (spec-gaps G253): <c>ask</c>의 답을 가르고, <c>case</c> 안에서 멈춘 흐름도 저장·불러오기 뒤에
    /// 이어 간다. 대상은 한 번만 평가한다.
    /// </summary>
    public class WorldMatchTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n";

        private const string Defs =
            "Party(\"Red\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Turn(p: Ref[Party]):\n" +
            "    r = ask(p, \"turn\", choices=[\"attack\", \"wait\", \"flee\"])\n" +
            "    match r.choice:\n" +
            "        case \"attack\":\n" +
            "            p.Score.v += 10\n" +
            "            again = ask(p, \"again?\", choices=[\"yes\", \"no\"])\n" +
            "            match again.choice:\n" +
            "                case \"yes\":\n" +
            "                    p.Score.v += 100\n" +
            "        case \"wait\" | \"flee\" if p.Score.v == 0:\n" +
            "            p.Score.v = -1\n" +
            "    p.Score.v += 1000\n" +
            "def bump(p):\n" +
            "    p.Score.v += 1\n" +
            "    return p.Score.v\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Once(p: Ref[Party]):\n" +
            "    match bump(p):\n" +
            "        case 5:\n" +
            "            pass\n" +
            "        case 6:\n" +
            "            pass\n" +
            "        case 7:\n" +
            "            pass\n";

        private World _world = null!;
        private ContentLoadResult _content = null!;
        private EntityId _red;

        private void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _content = LoadClean();
            _world = new World(_content);
            _red = _world.Create("Red");
            _world.Seat(_red, "host");
        }

        private long V(World w) => ((IntValue)w.Get(_red, w.Member("Score", "v"))).Value;

        private SubmitResult Submit(World w, string command) =>
            w.Submit("host", "banking." + command, new Dictionary<string, Value> { ["p"] = new EntityValue(_red) });

        [TestCase(0, 0, 10 + 100 + 1000)]
        [TestCase(0, 1, 10 + 1000)]
        [TestCase(1, -1, -1 + 1000)]
        [TestCase(2, -1, -1 + 1000)]
        public void TheAnswerPicksTheCase(int first, int second, long expected)
        {
            NewWorld();
            var a = _world.Answer("host", Submit(_world, "Turn").OpenedRequest!.Id, _red, first);
            Assert.That(a.Succeeded, Is.True, a.Message);
            if (second >= 0)
            {
                var b = _world.Answer("host", a.OpenedRequest!.Id, _red, second);
                Assert.That(b.Succeeded, Is.True, b.Message);
            }
            Assert.That(V(_world), Is.EqualTo(expected));
            Assert.That(_world.OpenRequests, Is.Empty);
        }

        // C1: case 안에서 멈춘 흐름을 저장하고 불러와 이어도 저장 없이 한 것과 같다
        [Test]
        public void SavingInsideACaseGoesOnAfterLoading()
        {
            NewWorld();
            var picks = new[] { 0, 0 };
            var straight = Play(Fresh(), picks, stopAt: -1);
            Assert.That(V(straight), Is.EqualTo(1110));
            for (int stop = 0; stop < picks.Length; stop++)
                Assert.That(Play(Fresh(), picks, stop).Save(), Is.EqualTo(straight.Save()), "saved before answer " + stop);
        }

        private World Fresh()
        {
            var w = new World(_content);
            w.Seat(w.Create("Red"), "host");
            return w;
        }

        private World Play(World w, int[] picks, int stopAt)
        {
            var q = Submit(w, "Turn").OpenedRequest!;
            for (int i = 0; i < picks.Length; i++)
            {
                if (i == stopAt)
                {
                    var bytes = w.Save();
                    var loaded = World.Load(_content, bytes);
                    Assert.That(loaded.Diagnostics, Is.Empty);
                    w = loaded.World!;
                    Assert.That(w.Save(), Is.EqualTo(bytes), "C4");
                    q = w.OpenRequests.Single();
                }
                var a = w.Answer("host", q.Id, _red, picks[i]);
                Assert.That(a.Succeeded, Is.True, a.Message);
                q = a.OpenedRequest!;
            }
            Assert.That(q, Is.Null);
            return w;
        }

        [Test]
        public void TheSubjectIsEvaluatedOnce()
        {
            NewWorld();
            var r = Submit(_world, "Once");
            Assert.That(r.Succeeded, Is.True, r.Error?.Message);
            Assert.That(V(_world), Is.EqualTo(1));
        }

        private List<Diagnostic> Diags(string defs)
        {
            Mod("banking", schema: Schema, defs: "Party(\"Red\")\n" + defs);
            Load(out var diags);
            return diags.ToList();
        }

        [Test]
        public void ATopLevelMatchIsALoadError()
        {
            var diags = Diags("match 1:\n    case 1:\n        pass\n");
            Assert.That(diags.Select(d => d.Code), Has.Some.EqualTo(Codes.EvlUnsupported), string.Join("\n", diags));
        }

        [Test]
        public void ANameTypoInAGuardIsALoadErrorAndTheHiddenNameIsNotSuggested()
        {
            var diags = Diags("def f(p):\n    m0 = 1\n    match p:\n        case 1 if m00 > 0:\n            return 1\n    return 0\n");
            var d = diags.Single(x => x.Code == Codes.EvlUnknownName);
            Assert.That(d.Message, Is.EqualTo("name 'm00' is not defined"));
            Assert.That(d.Hint, Does.Contain("m0").And.Not.Contain("$"));
        }
    }
}
