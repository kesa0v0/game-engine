using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>컴프리헨션과 제너레이터 식의 로드 검사 (spec S2, spec-gaps G189).</summary>
    public class ComprehensionLoadTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Moves\", left=field(int, default=1))\ndeftype(\"Unit\", components=[\"Moves\"])\n";

        private List<Diagnostic> Diags(string defs)
        {
            Mod("core", schema: Schema, defs: defs);
            Load(out var diags);
            return diags.ToList();
        }

        private static string Text(Diagnostic d) => d.Code + " " + d.Message;

        [TestCase("def f(xs):\n    g = (x for x in xs)\n    return 0\n",
            "EVL002 a generator expression can only be an argument of a call")]
        [TestCase("def f(xs):\n    return [(x for x in xs)]\n",
            "EVL002 a generator expression can only be an argument of a call")]
        [TestCase("def f(xs):\n    return sum(x for x in xs, 1)\n",
            "PAR001 a generator expression must be parenthesized unless it is the only argument")]
        [TestCase("X = [i for i in [1]]\n",
            "EVL002 comprehensions can only be used inside a function")]
        [TestCase("def f():\n    y = [i for i in [1]]\n    return i\n",
            "EVL001 name 'i' is not defined")]
        public void MisplacedFormsAreLoadErrors(string defs, string expected)
        {
            Assert.That(Diags(defs).Select(Text), Has.Some.EqualTo(expected), string.Join("\n", Diags(defs).Select(Text)));
        }

        [Test]
        public void TheWellPlacedFormsLoad()
        {
            Mod("core", schema: Schema, defs:
                "def f(xs):\n" +
                "    a = [x for x in xs if x > 0]\n" +
                "    b = {x: x for x in a}\n" +
                "    c = sum(x for x in a)\n" +
                "    d = max((x for x in a), default=0) if False else 0\n" +
                "    return [y for x in [a] for y in x] if any(x > 1 for x in xs) else []\n");
            LoadClean();
        }

        [Test]
        public void AWriterCalledInsideAComprehensionIsAnExpressionCall()
        {
            var diags = Diags("def spend(u):\n    u.Moves.left = 0\n" +
                              "@command(free=True, acts_for=None)\ndef Rest(u: Ref[Unit]):\n    done = [spend(x) for x in [u]]\n");
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.CmdWriteEffect));
        }

        [Test]
        public void ALambdaMadeInAComprehensionSurvivesSavingAcrossAsk()
        {
            // 컴프리헨션의 이름 공간을 잡은 람다를 든 채 멈춘다: 저장은 그것을 끝난 함수의 묶음으로 적는다
            Mod("core", schema: Schema, defs:
                "Unit(\"Scout\")\n" +
                "@command(free=True, acts_for=None)\ndef Plan(u: Ref[Unit]):\n" +
                "    base = 1\n" +
                "    fs = [lambda: i * 10 + base for i in range(3)]\n" +
                "    r = ask(u, \"Go\", choices=[\"a\", \"b\"])\n" +
                "    u.Moves.left = fs[0]() + fs[1]() + r.index\n");
            var content = LoadClean();

            long Play(bool reload)
            {
                var world = new World(content);
                var u = world.Create("Scout");
                world.Seat(u, "host");
                Assert.That(world.Submit("host", "core.Plan", new Dictionary<string, Value> { ["u"] = new EntityValue(u) }).Succeeded,
                    Is.True);
                if (reload)
                {
                    var bytes = world.Save();
                    var loaded = World.Load(content, bytes);
                    Assert.That(loaded.Succeeded, Is.True, string.Join("\n", loaded.Diagnostics));
                    world = loaded.World!;
                    Assert.That(world.Save(), Is.EqualTo(bytes), "C4");
                }
                var open = world.OpenRequests.Single();
                Assert.That(world.Answer("host", open.Id, open.Who[0], 1).Error, Is.Null);
                return ((IntValue)world.Get(u, world.Member("Moves", "left"))).Value;
            }

            // 파이썬처럼 늦게 묶여 둘 다 i = 2: (20 + 1) * 2 + 1
            Assert.That(Play(reload: false), Is.EqualTo(43));
            Assert.That(Play(reload: true), Is.EqualTo(43));
        }

        [Test]
        public void ACommandBodyCanUseComprehensionsAndRandomInsideThem()
        {
            Mod("core", schema: Schema, defs:
                "Unit(\"Scout\")\n" +
                "@command(free=True, acts_for=None)\ndef Roll(u: Ref[Unit]):\n" +
                "    rolls = [random.randint(1, 6) for i in range(3)]\n" +
                "    u.Moves.left = sum(r for r in rolls if r > 0) + len({r: 1 for r in rolls}) * 0\n");
            var world = new World(LoadClean(), seed: 3);
            var u = world.Create("Scout");
            world.Seat(u, "host");
            Assert.That(world.Submit("host", "core.Roll", new Dictionary<string, Value> { ["u"] = new EntityValue(u) }).Succeeded,
                Is.True);
            Assert.That(((IntValue)world.Get(u, world.Member("Moves", "left"))).Value, Is.InRange(3, 18));
        }
    }
}
