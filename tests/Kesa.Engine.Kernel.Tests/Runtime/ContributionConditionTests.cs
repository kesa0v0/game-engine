using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 조건부 선언 보너스 <c>contrib(..., when=lambda src, dst: ...)</c> (spec-gaps G301): <c>Cond</c> 칸과 같은 함수와 검사,
    /// 거짓이면 그 줄은 없고, 조건의 읽기는 그 <c>derived</c>의 읽기다.
    /// </summary>
    public class ContributionConditionTests : LoaderTestBase
    {
        private const string Schema =
            "def melee(m):\n" +
            "    return m.base + floor(sum([c.value for c in contribs(m, \"dmg\")]))\n" +
            "component(\"Melee\", base=const(int, default=1), dmg=derived(int, melee))\n" +
            "component(\"Mood\", value=field(int, default=0))\n" +
            "component(\"Mark\", hit=field(int, default=0))\n" +
            "component(\"Held\", holder=ref(\"Pawn\", optional=True))\n" +
            "component(\"Blade\", grants=const(list[Contrib], default=[]), blessed=field(bool, default=False))\n" +
            "deftype(\"Pawn\", components=[\"Melee\", \"Mood\", \"Mark\"])\n" +
            "deftype(\"Sword\", components=[\"Held\", \"Blade\"])\n";

        private const string Pawns =
            "Pawn(\"Legionary\")\n" +
            "@when(lambda p: p.Melee.dmg >= 6)\ndef armed(p: Ref[Pawn]):\n    p.Mark.hit = 1\n";

        private static string Sword(string when) =>
            "Sword(\"Longsword\", Blade=Blade(grants=[contrib(\"Melee.dmg\", \"add\", 5, to=\"Held.holder\", when=" + when + ")]))\n";

        private const string Happy = "def happy(src, dst):\n    return dst.Mood.value >= 5\n";

        private static HostFunctions Host()
        {
            var host = new HostFunctions();
            var core = host.For("core");
            core.Pure("def pawns.calm(s: Ref[Sword], p: Ref[Pawn]) -> bool", "",
                (HostReader r, EntityId s, EntityId p) => ((IntValue)r.Get(p, r.Member("Mood", "value"))).Value >= 5);
            core.Pure("def pawns.one(p: Ref[Pawn]) -> bool", "", (EntityId p) => true);
            core.Pure("def pawns.score(s: Ref[Sword], p: Ref[Pawn]) -> int", "", (EntityId s, EntityId p) => 1L);
            core.Writes("def pawns.poke(s: Ref[Sword], p: Ref[Pawn]) -> bool", "", (HostAccess a, EntityId s, EntityId p) => true);
            return host;
        }

        private World _world = null!;
        private EntityId _pawn, _sword;

        private IReadOnlyList<Diagnostic> Load(string defs)
        {
            Mod("core", schema: Schema, defs: defs);
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var result = ContentLoader.Load(_root, order, bag, Host());
            var diags = bag.ToSortedList();
            if (diags.Count == 0)
            {
                _world = new World(result);
                _pawn = _world.Create("core:Legionary");
                _sword = _world.Create("core:Longsword");
                _world.Set(_sword, _world.Member("Held", "holder"), new EntityValue(_pawn));
            }
            return diags;
        }

        private void NewWorld(string defs) => Assert.That(Load(defs), Is.Empty);

        private long Dmg => ((IntValue)_world.Get(_pawn, _world.Member("Melee", "dmg"))).Value;

        private void Mood(int value) => _world.Set(_pawn, _world.Member("Mood", "value"), new IntValue(value));

        [Test]
        public void AConditionOnTheReceiverDecidesWhetherTheLineIsThere()
        {
            NewWorld(Happy + Sword("happy") + Pawns);
            Assert.That(Dmg, Is.EqualTo(1));
            Mood(5);
            Assert.That(Dmg, Is.EqualTo(6));
            Assert.That(_world.Explain(_pawn, _world.Member("Melee", "dmg")).Render(), Is.EqualTo(
                "Melee.dmg of " + _pawn + " = 6\n" +
                "  Melee.base of " + _pawn + " = 1\n" +
                "  Mood.value of " + _pawn + " = 5\n" +
                "  contribution add 5.0 from " + _sword + " (Blade)"));
        }

        [Test]
        public void AConditionOnTheGiverDecidesToo()
        {
            NewWorld(Sword("lambda src, dst: src.Blade.blessed") + Pawns);
            Assert.That(Dmg, Is.EqualTo(1));
            _world.Set(_sword, _world.Member("Blade", "blessed"), BoolValue.True);
            Assert.That(Dmg, Is.EqualTo(6));
        }

        [Test]
        public void WhatTheConditionReadsWakesAWhen()
        {
            NewWorld(Happy + Sword("happy") + Pawns);
            _world.Advance(1);
            Assert.That(Hit, Is.EqualTo(0));
            Mood(5); // 폰의 Melee는 건드리지 않았다
            _world.Advance(1);
            Assert.That(Hit, Is.EqualTo(1));
        }

        private long Hit => ((IntValue)_world.Get(_pawn, _world.Member("Mark", "hit"))).Value;

        [Test]
        public void AConditionThatIsNotABoolIsAnErrorOfTheRead()
        {
            NewWorld(Sword("lambda src, dst: 1") + Pawns);
            var e = Assert.Catch(() => _ = Dmg)!;
            Assert.That(e.Message, Does.Contain("returned int, not bool"));
        }

        [Test]
        public void AConditionThatReadsItsOwnStatIsACycle()
        {
            NewWorld(Sword("lambda src, dst: dst.Melee.dmg > 0") + Pawns);
            var e = Assert.Catch(() => _ = Dmg)!;
            Assert.That(e.Message, Does.Contain("reads itself"));
        }

        [Test]
        public void AReadingHostFunctionCanBeTheCondition()
        {
            NewWorld(Sword("core.pawns.calm") + Pawns);
            Assert.That(Dmg, Is.EqualTo(1));
            Mood(7);
            Assert.That(Dmg, Is.EqualTo(6));
        }

        [TestCase("lambda src: True", "called with two arguments")]
        [TestCase("3", "takes a function")]
        [TestCase("bump", "changes the world")]
        [TestCase("dice", "draws random numbers")]
        [TestCase("core.pawns.poke", "changes the world")]
        [TestCase("core.pawns.one", "cannot be called with two entities")]
        [TestCase("core.pawns.score", "needs a bool")]
        public void AConditionIsCheckedAtLoad(string when, string message)
        {
            const string fns =
                "def bump(src, dst):\n    dst.Mood.value = 1\n    return True\n" +
                "def dice(src, dst):\n    return random.randint(1, 6) > 3\n";
            var diags = Load(fns + Sword(when));
            Assert.That(diags.Select(d => d.Message), Has.Some.Contains(message), string.Join("\n", diags));
        }
    }
}
