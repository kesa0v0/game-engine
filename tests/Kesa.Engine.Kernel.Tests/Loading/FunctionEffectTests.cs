using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>함수 효과 판정: 쓰기 함수와 그것을 부를 수 있는 자리 (spec S2 함수 효과 표, spec-gaps G117).</summary>
    public class FunctionEffectTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Health\", current=field(int, default=10))\n" +
            "deftype(\"Unit\", components=[\"Health\"])\n";

        [Test]
        public void CommandsAndHelpersCanWriteAndCallWritersAsStatements()
        {
            Mod("core", schema: Schema,
                defs: "def damage(u, n):\n    u.Health.current -= n\n" +
                      "def strike(u):\n    if u:\n        damage(u, 3)\n" +
                      "@command\ndef Attack(u: Ref[Unit]):\n    strike(u)\n    u.Health.current = 0\n    return 0\n");
            LoadClean();
        }

        [Test]
        public void ItemAssignmentStaysForbidden()
        {
            // 지역 값은 불변이다 (G104): 엔티티 쓰기만 열린다
            Mod("core", defs: "def f(h):\n    h[0] = 1\n    return 1\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnsupported));
            Assert.That(d.Message, Is.EqualTo("a logic function can only assign to local variables and entity values"));
        }

        [TestCase("def f(u):\n    x = damage(u, 1)\n    return x\n", 4)]
        [TestCase("def f(u):\n    if damage(u, 1):\n        return 1\n    return 0\n", 4)]
        [TestCase("def f(u):\n    return damage(u, 1)\n", 4)]
        [TestCase("g = lambda u: damage(u, 1)\n", 3)]
        [TestCase("@command\ndef F(u: Ref[Unit]):\n    with preconditions:\n        require(damage(u, 1), text(\"m\"))\n    return 0\n", 6)]
        [TestCase("def f(u):\n    [damage(u, 1)]\n", 4)]
        public void AWriterCalledInsideAnExpressionIsALoadError(string defs, int line)
        {
            Mod("core", schema: Schema, defs: "def damage(u, n):\n    u.Health.current -= n\n" + defs);
            var result = Load(out var diags);
            var d = diags.Single(x => x.Code == Codes.CmdWriteEffect);
            Assert.That(d.Message, Is.EqualTo("'damage' changes the world, so it can only be called as a statement"));
            Assert.That(d.Span.Line, Is.EqualTo(line));
            Assert.That(d.Related.Single().Line, Is.EqualTo(2)); // 쓰기 함수로 만든 대입
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void WritingSpreadsThroughStatementCallsInAnyOrder()
        {
            // outer가 inner보다 먼저 정의돼도 고정점으로 퍼진다
            Mod("core", schema: Schema,
                defs: "def outer(u):\n    inner(u)\n" +
                      "def inner(u):\n    u.Health.current = 1\n" +
                      "def f(u):\n    return outer(u)\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Is.EqualTo("'outer' changes the world, so it can only be called as a statement"));
            Assert.That(d.Related.Single().Line, Is.EqualTo(2)); // outer를 쓰기 함수로 만든 inner(u) 호출
        }

        [Test]
        public void AShadowedNameIsNotTheWriter()
        {
            // 지역 변수 damage는 모드의 쓰기 함수가 아니다 (G112의 규칙)
            Mod("core", schema: Schema,
                defs: "def damage(u, n):\n    u.Health.current -= n\n" +
                      "def f(u, damage):\n    return damage(u, 1)\n");
            LoadClean();
        }

        [Test]
        public void AWriterCannotBeADerivedFunction()
        {
            Mod("core", schema: "def heal(h):\n    h.current = 1\n    return 1\n" +
                                "component(\"Health\", current=field(int, default=10), max=derived(int, heal))\n" +
                                "deftype(\"Unit\", components=[\"Health\"])\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("derived 'Health.max' uses 'heal', which changes the world; it can only read"));
            Assert.That(d.Related.Single().Line, Is.EqualTo(2));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void AWriterCannotBeAFieldDefault()
        {
            Mod("core", schema: "def start(h):\n    h.Health.current = 1\n    return 1\n" +
                                "component(\"Health\", current=field(int, default=10), mark=field(int, default=start))\n" +
                                "deftype(\"Unit\", components=[\"Health\"])\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("the default of 'Health.mark' uses 'start', which changes the world; it can only read"));
        }

        // ---- 커맨드는 로직에서 쓸 수 없다 (G118) ----

        private const string Heal =
            "@command\ndef Heal(u: Ref[Unit]):\n    with preconditions:\n        require(u.Health.current < 10, text(\"m\"))\n" +
            "    u.Health.current = 10\n    return 0\n";

        [TestCase("def helper(u):\n    Heal(u)\n", 8)]
        [TestCase("@command\ndef Rally(u: Ref[Unit]):\n    Heal(u)\n    return 0\n", 9)]
        [TestCase("def helper(u):\n    x = Heal(u)\n    return x\n", 8)]
        [TestCase("def helper(u):\n    f = Heal\n    return 0\n", 8)]
        [TestCase("def helper(u, g):\n    return g(Heal)\n", 8)]
        [TestCase("g = lambda u: Heal(u)\n", 7)]
        public void LogicCannotCallOrPassACommand(string defs, int line)
        {
            Mod("core", schema: Schema, defs: Heal + defs);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("'Heal' is a @command, so logic cannot call it or use it as a value"));
            Assert.That(d.Span.Line, Is.EqualTo(line));
            Assert.That(d.Hint, Is.EqualTo("move the shared part into a plain function that the commands call (spec-gaps G118)"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void SharedEffectsGoInAPlainFunction()
        {
            Mod("core", schema: Schema,
                defs: "def heal(u):\n    u.Health.current = 10\n" +
                      "@command\ndef Heal(u: Ref[Unit]):\n    heal(u)\n    return 0\n" +
                      "@command\ndef Rally(u: Ref[Unit]):\n    heal(u)\n    return 0\n");
            LoadClean();
        }

        [Test]
        public void ACommandCannotBeADefFunctionValue()
        {
            Mod("core", schema: Schema + "deftype(\"Goal\", effect=const(Action[\"Goal\"]))\n",
                defs: Heal + "Goal(\"A\", effect=Heal)\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Is.EqualTo("'effect': 'Heal' is a @command, so it cannot be a value of Action[\"Goal\"]; use a plain function"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ACommandCannotBeADerivedFunction()
        {
            Mod("core", schema: "@command\ndef Peek(h: int):\n    return 1\n" +
                                "component(\"Health\", current=field(int, default=10), max=derived(int, Peek))\n" +
                                "deftype(\"Unit\", components=[\"Health\"])\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("derived 'Health.max' uses 'Peek', which is a @command"));
        }

        [Test]
        public void AWriterCannotFillACondSlotButCanFillAnAction()
        {
            Mod("core", schema: "deftype(\"Goal\", check=const(Cond[\"Goal\"]), effect=const(Action[\"Goal\"]))\n",
                defs: "def win(g):\n    g.Score.points = 1\n" +
                      "def done(g):\n    return True\n" +
                      "Goal(\"A\", check=done, effect=win)\n" +
                      "Goal(\"B\", check=win, effect=win)\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Does.StartWith("'check': 'win' changes the world (at core/defs/defs.py:2:"));
            Assert.That(d.Message, Does.EndWith("), but Cond[\"Goal\"] only holds functions that read"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }
    }
}
