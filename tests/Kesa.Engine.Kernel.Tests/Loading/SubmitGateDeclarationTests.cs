using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>입구 선언: 자유 커맨드의 <c>acts_for</c>와 <c>@submit_policy</c> (spec S6, spec-gaps G122~G124·G130).</summary>
    public class SubmitGateDeclarationTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Moves\", left=field(int, default=1))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "deftype(\"Party\", components=[\"Moves\"])\n" +
            "deftype(\"Unit\", components=[\"Moves\", \"Owner\"])\n";

        private const string Policy = "@submit_policy\ndef may_act(issuer, target):\n    return True\n";

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

        // ---- @submit_policy (G122·G124) ----

        [Test]
        public void APolicyIsOptional()
        {
            Mod("core", schema: Schema);
            Assert.That(LoadClean().SubmitPolicy, Is.Null);
        }

        [Test]
        public void TheDeclaredPolicyIsInTheResult()
        {
            Mod("core", schema: Schema, defs: Policy);
            var policy = LoadClean().SubmitPolicy!;
            Assert.That(policy.Mod, Is.EqualTo("core"));
            Assert.That(policy.Function.Name, Is.EqualTo("may_act"));
            Assert.That(policy.Function.IsCommand, Is.False);
            Assert.That(policy.Function.IsSubmitPolicy, Is.True);
        }

        [Test]
        public void LogicMayCallThePolicyFunction()
        {
            // 정책 함수는 커맨드가 아니라 평범한 순수 함수다 (G118의 금지는 커맨드에만)
            Mod("core", schema: Schema, defs: Policy + "def check(i, t):\n    return may_act(i, t)\n");
            LoadClean();
        }

        [TestCase("@submit_policy\ndef may_act(issuer):\n    return True\n")]
        [TestCase("@submit_policy\ndef may_act(issuer, target, extra):\n    return True\n")]
        [TestCase("@submit_policy\ndef may_act(issuer, target=None):\n    return True\n")]
        public void APolicyTakesIssuerAndTarget(string defs)
        {
            Mod("core", schema: Schema, defs: defs);
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdSubmitPolicy));
            Assert.That(d.Message, Is.EqualTo("a submit policy takes exactly two parameters without defaults: (issuer, target)"));
        }

        [TestCase("@submit_policy(True)\ndef may_act(i, t):\n    return True\n", "@submit_policy takes no positional arguments")]
        [TestCase("@submit_policy(free=True)\ndef may_act(i, t):\n    return True\n", "@submit_policy has no argument 'free'")]
        [TestCase("@submit_policy(override=1)\ndef may_act(i, t):\n    return True\n", "override= must be True or False")]
        [TestCase("submit_policy = 1\n@submit_policy\ndef may_act(i, t):\n    return True\n",
            "this mod defines its own 'submit_policy', so the built-in @submit_policy cannot be used here")]
        public void BadPolicyDecoratorsAreLoadErrors(string defs, string message)
        {
            Mod("core", schema: Schema, defs: defs);
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdBadDecorator));
            Assert.That(d.Message, Is.EqualTo(message));
        }

        [Test]
        public void APolicyCannotChangeTheWorld()
        {
            Mod("core", schema: Schema,
                defs: "@submit_policy\ndef may_act(issuer, target):\n    target.Moves.left = 0\n    return True\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("the submit policy 'may_act' changes the world; it can only read"));
        }

        [Test]
        public void AModCanDeclareOnlyOnePolicy()
        {
            Mod("core", schema: Schema, defs: Policy + "@submit_policy\ndef other(issuer, target):\n    return False\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdSubmitPolicy));
            Assert.That(d.Message, Is.EqualTo("a mod can declare only one submit policy"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ADependentModMustSayOverride()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@submit_policy\ndef gate(issuer, target):\n    return False\n",
                extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Message, Is.EqualTo("a submit policy is already declared by mod 'core'"));
            Assert.That(d.Hint, Does.Contain("override=True"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking" }));
            Assert.That(result.SubmitPolicy!.Mod, Is.EqualTo("core"));
        }

        [Test]
        public void OverrideReplacesThePolicy()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@submit_policy(override=True)\ndef gate(issuer, target):\n    return False\n",
                extra: ", dependencies=[\"core\"]");
            var policy = LoadClean().SubmitPolicy!;
            Assert.That(policy.Mod, Is.EqualTo("banking"));
            Assert.That(policy.Function.Name, Is.EqualTo("gate"));
            Assert.That(policy.Replaced!.Mod, Is.EqualTo("core"));
        }

        [Test]
        public void OverrideWithNothingToReplaceIsAnError()
        {
            Mod("core", schema: Schema, defs: "@submit_policy(override=True)\ndef may_act(issuer, target):\n    return True\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdSubmitPolicy));
            Assert.That(d.Message, Is.EqualTo("override=True but there is no submit policy to replace"));
        }

        [Test]
        public void TwoUnrelatedModsWithPoliciesBothFail()
        {
            Mod("banking", defs: Policy);
            Mod("scouts", defs: Policy);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { Codes.CmdSubmitPolicy }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking", "scouts" }));
            Assert.That(result.SubmitPolicy, Is.Null);
        }

        [Test]
        public void AnOverrideOfAModNotDependedOnIsAnError()
        {
            Mod("core", schema: Schema, defs: Policy);
            Mod("banking", defs: "@submit_policy(override=True)\ndef gate(issuer, target):\n    return False\n",
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
            Mod("banking", defs: "@submit_policy(override=True)\ndef gate(issuer, target):\n    return False\n",
                extra: ", dependencies=[\"core\"]");
            Mod("scouts", defs: "@submit_policy(override=True)\ndef gate(issuer, target):\n    return True\n",
                extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { Codes.CmdSubmitPolicy }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "banking", "scouts" }));
            Assert.That(result.SubmitPolicy!.Mod, Is.EqualTo("core"));
        }
    }
}
