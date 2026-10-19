using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>입구 선언: 자유 커맨드의 <c>acts_for</c>, 없어진 <c>@submit_policy</c>, 정책 교체 규칙 (spec S6, spec-gaps G123·G130·G203·G237).</summary>
    public class SubmitGateDeclarationTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Moves\", left=field(int, default=1))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "deftype(\"Party\", components=[\"Moves\"])\n" +
            "deftype(\"Unit\", components=[\"Moves\", \"Owner\"])\n";

        // ---- acts_for (G123·G130) ----

        [Test]
        public void ActsForPicksCommandParametersByName()
        {
            Mod("core", schema: Schema,
                defs: "@command(free=True, acts_for=lambda unit: unit.Owner.party)\n" +
                      "def Move(party: Ref[Party], unit: Ref[Unit], n: int):\n    return 0\n" +
                      "@command(free=True, acts_for=None)\ndef Chat(msg: str):\n    return 0\n" +
                      "@command(free=True, acts_for=None)\ndef Answer(n: int):\n    return 0\n");
            var result = LoadClean();
            var move = result.Commands["core.Move"];
            Assert.That(move.ActsFor, Is.Not.Null);
            Assert.That(move.ActsFor!.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "unit" }));
            Assert.That(result.Commands["core.Chat"].Free, Is.True);
            Assert.That(result.Commands["core.Chat"].ActsFor, Is.Null);
            Assert.That(result.Commands["core.Answer"].ActsFor, Is.Null);
        }

        [TestCase("@command(free=True)\ndef Move(party: Ref[Party]):\n    return 0\n",
            "a free command must say whom it acts for")]
        [TestCase("@command\ndef Move(party: Ref[Party]):\n    return 0\n",
            "a response command must say which entity answers")]
        [TestCase("@command(free=False, acts_for=None)\ndef Move(party: Ref[Party]):\n    return 0\n",
            "a response command cannot use acts_for=None; an answer always comes from an entity")]
        [TestCase("@command(free=True, acts_for=\"party\")\ndef Move(party: Ref[Party]):\n    return 0\n",
            "acts_for= must be a lambda or None")]
        [TestCase("@command(free=True, acts_for=lambda partu: partu)\ndef Move(party: Ref[Party]):\n    return 0\n",
            "the acts_for lambda of command 'core.Move' takes 'partu', which is not a parameter of the command")]
        public void BadActsForIsALoadError(string defs, string message)
        {
            Mod("core", schema: Schema, defs: defs);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ActsForCannotChangeTheWorld()
        {
            Mod("core", schema: Schema,
                defs: "def spend(u):\n    u.Moves.left = 0\n" +
                      "@command(free=True, acts_for=lambda unit: spend(unit))\ndef Move(unit: Ref[Unit]):\n    return 0\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.CmdWriteEffect));
        }

        // ---- 없어진 @submit_policy (G237) ----

        [Test]
        public void SubmitPolicyWasRemoved()
        {
            Mod("core", schema: Schema, defs: "@submit_policy\ndef may_act(issuer, target):\n    return True\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdSubmitPolicy));
            Assert.That(d.Message, Is.EqualTo("@submit_policy was removed: the gate checks the kernel's seat table, not a policy function"));
            Assert.That(d.Hint, Does.Contain("World.Seat"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        // ---- 정책 교체: 모드 사이의 규칙 (G203, Def 교체와 같다) ----

        private const string Policy = "@view_policy\ndef may_see(viewer, entity, component):\n    return True\n";

        [Test]
        public void ADependentModMustSayOverride()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@view_policy\ndef gate(viewer, entity, component):\n    return False\n",
                extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Is.EqualTo("a view policy is already declared by mod 'core'"));
            Assert.That(d.Hint, Does.Contain("override=True"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
            Assert.That(result.ViewPolicy!.Mod, Is.EqualTo("core"));
        }

        [Test]
        public void OverrideReplacesThePolicy()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@view_policy(override=True)\ndef gate(viewer, entity, component):\n    return False\n",
                extra: ", dependencies=[\"core\"]");
            var policy = LoadClean().ViewPolicy!;
            Assert.That(policy.Mod, Is.EqualTo("banking"));
            Assert.That(policy.Function.Name, Is.EqualTo("gate"));
            Assert.That(policy.Replaced!.Mod, Is.EqualTo("core"));
        }

        [Test]
        public void TwoUnrelatedModsWithPoliciesBothFail()
        {
            Mod("banking", defs: Policy);
            Mod("scouts", defs: Policy);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { Codes.CmdViewPolicy }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking", "scouts" }));
            Assert.That(result.ViewPolicy, Is.Null);
        }

        [Test]
        public void AnOverrideOfAModNotDependedOnIsAnError()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@view_policy(override=True)\ndef gate(viewer, entity, component):\n    return False\n",
                extra: ", loadAfter=[\"core\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
        }

        [Test]
        public void TwoOverridesWithoutOrderConflict()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@view_policy(override=True)\ndef gate(viewer, entity, component):\n    return False\n",
                extra: ", dependencies=[\"core\"]");
            Mod("scouts", defs: "@view_policy(override=True)\ndef gate(viewer, entity, component):\n    return True\n",
                extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { Codes.CmdViewPolicy }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking", "scouts" }));
            Assert.That(result.ViewPolicy!.Mod, Is.EqualTo("core"));
        }

        [Test]
        public void LogicMayCallThePolicyFunction()
        {
            // 정책 함수는 커맨드가 아니라 평범한 순수 함수다 (G118의 금지는 커맨드에만)
            Mod("core", schema: Schema, defs: Policy + "def check(v, e):\n    return may_see(v, e, None)\n");
            LoadClean();
        }
    }
}
