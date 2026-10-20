using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// ③에서 연산이 대상에 허용되는지(spec-gaps G36·G44·G51)와 값의 모양(G39·G48)을 보는 검사.
    /// 값이 있는지·컴포넌트가 이미 붙어 있는지는 ④의 일이라 여기서는 보지 않는다.
    /// </summary>
    public class PatchOperationTests : LoaderTestBase
    {
        private const string CoreDep = ", dependencies=[\"core\"]";

        private void Core()
        {
            Mod("core",
                schema:
                "component(\"Health\", base_max=const(int, default=10), current=field(int, default=0))\n" +
                "component(\"Morale\", value=field(int, default=5))\n" +
                "deftype(\"Resource\")\n" +
                "component(\"Stock\", key=\"Resource\", amount=field(fixed, default=0))\n" +
                "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=field(int, default=0))\n" +
                "deftype(\"Unit\",\n" +
                "    components=[\"Health\", \"Stock\", \"Skill\"],\n" +
                "    move=const(int, default=1),\n" +
                "    speed=const(fixed, default=1.0),\n" +
                "    flag=const(bool, default=False),\n" +
                "    name=const(str, default=\"\"),\n" +
                "    label=const(TextKey, default=\"\"),\n" +
                "    tags=const(list[str]),\n" +
                "    cost=const(dict[DefId[\"Resource\"], int]),\n" +
                "    table=const(dict[int, str]),\n" +
                ")\n",
                defs: "Resource(\"Wood\")\nResource(\"Iron\")\n" +
                      "Unit(\"Scout\", tags=[\"slow\"], cost={\"core:Wood\": 2}, table={1: \"x\"})\n");
        }

        private ContentLoadResult Run(string call)
        {
            Core();
            Mod("compat", defs: call + "\n", extra: CoreDep);
            return Load(out _);
        }

        // ---- 허용되는 연산 ----

        [TestCase("patch.set(\"core:Scout\", \"move\", 4)")]
        [TestCase("patch.set(\"core:Scout\", \"speed\", 2)")]
        [TestCase("patch.set(\"core:Scout\", \"speed\", 1.5)")]
        [TestCase("patch.set(\"core:Scout\", \"flag\", True)")]
        [TestCase("patch.set(\"core:Scout\", \"name\", \"x\")")]
        [TestCase("patch.set(\"core:Scout\", \"label\", \"unit.scout\")")]
        [TestCase("patch.add(\"core:Scout\", \"move\", 2)")]
        [TestCase("patch.add(\"core:Scout\", \"move\", -1)")]
        [TestCase("patch.add(\"core:Scout\", \"speed\", 1)")]
        [TestCase("patch.add(\"core:Scout\", \"speed\", 0.5)")]
        [TestCase("patch.set(\"core:Scout\", \"tags\", [\"a\", \"b\"])")]
        [TestCase("patch.append(\"core:Scout\", \"tags\", [\"fast\"])")]
        [TestCase("patch.remove(\"core:Scout\", \"tags\", \"slow\")")]
        [TestCase("patch.set(\"core:Scout\", \"cost\", {\"core:Wood\": 1})")]
        [TestCase("patch.append(\"core:Scout\", \"cost\", {\"core:Iron\": 1})")]
        [TestCase("patch.remove(\"core:Scout\", \"cost\", \"core:Wood\")")]
        [TestCase("patch.set(\"core:Scout\", \"cost['core:Wood']\", 3)")]
        [TestCase("patch.add(\"core:Scout\", \"cost['core:Wood']\", 1)")]
        [TestCase("patch.set(\"core:Scout\", \"table[3]\", \"x\")")]
        [TestCase("patch.append(\"core:Scout\", \"table\", {2: \"a\"})")]
        [TestCase("patch.remove(\"core:Scout\", \"table\", 1)")]
        [TestCase("patch.set(\"core:Scout\", \"Health.base_max\", 5)")]
        [TestCase("patch.add(\"core:Scout\", \"Health.base_max\", 1)")]
        [TestCase("patch.add(\"core:Scout\", \"Health.current\", 1)")]
        [TestCase("patch.set(\"core:Scout\", \"Morale\", Morale(value=7))")]
        [TestCase("patch.set(\"core:Scout\", \"Skill['core:Wood']\", Skill(level=1))")]
        [TestCase("patch.add(\"core:Scout\", \"Stock['core:Iron'].amount\", 1)")]
        public void AllowedOperation(string call)
        {
            var result = Run(call);
            Assert.That(result.Resolved, Has.Count.EqualTo(1), string.Join("\n", result.Disabled));
            Assert.That(result.Disabled, Is.Empty);
        }

        [TestCase("patch.remove(\"core:Scout\", \"Morale\")")]
        [TestCase("patch.set(\"core:Scout\", \"Morale.value\", 1)")]
        [TestCase("patch.remove(\"core:Scout\", \"Skill['core:Wood']\")")]
        public void AllowedOperationOnAComponentAttachedBefore(string call)
        {
            // 떼거나 그 안의 값을 바꾸려면 먼저 붙어 있어야 한다 (④가 본다, G44)
            var result = Run("patch.set(\"core:Scout\", \"Morale\", Morale(value=3))\n" +
                             "patch.set(\"core:Scout\", \"Skill['core:Wood']\", Skill(level=1))\n" + call);
            Assert.That(result.Resolved, Has.Count.EqualTo(3), string.Join("\n", result.Disabled));
            Assert.That(result.Disabled, Is.Empty);
        }

        // ---- 허용되지 않는 연산 ----

        [TestCase("patch.add(\"core:Scout\", \"tags\", [\"a\"])", "patch.set, patch.append, patch.remove")]
        [TestCase("patch.append(\"core:Scout\", \"move\", [1])", "patch.set, patch.add")]
        [TestCase("patch.remove(\"core:Scout\", \"move\", 1)", "patch.set, patch.add")]
        [TestCase("patch.add(\"core:Scout\", \"name\", \"x\")", "patch.set")]
        [TestCase("patch.add(\"core:Scout\", \"flag\", 1)", "patch.set")]
        [TestCase("patch.append(\"core:Scout\", \"cost['core:Wood']\", 1)", "patch.set, patch.add")]
        [TestCase("patch.remove(\"core:Scout\", \"Health.base_max\", 1)", "patch.set, patch.add")]
        public void OperationThatTheValueDoesNotSupport(string call, string hint)
        {
            var result = Run(call);
            var d = Diagnostics(result);
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.PatBadOp, 1)));
            Assert.That(d.Hint, Is.EqualTo("use " + hint));
        }

        private Diagnostic Diagnostics(ContentLoadResult result)
        {
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
            Assert.That(result.Resolved, Is.Empty);
            // 마지막 Load의 진단을 다시 얻는다
            Load(out var diags);
            return diags.Single();
        }

        [Test]
        public void RemovingFromAListNeedsTheElement()
        {
            var d = Diagnostics(Run("patch.remove(\"core:Scout\", \"tags\")"));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadOp));
            Assert.That(d.Message, Does.Contain("element"));
        }

        [Test]
        public void RemovingFromAMapNeedsTheKey()
        {
            var d = Diagnostics(Run("patch.remove(\"core:Scout\", \"cost\")"));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadOp));
            Assert.That(d.Message, Does.Contain("key"));
        }

        [TestCase("cost['core:Wood']", "patch.remove(\"core:Scout\", \"cost\", \"core:Wood\")")]
        [TestCase("cost['core:Iron']", "patch.remove(\"core:Scout\", \"cost\", \"core:Iron\")")]
        [TestCase("table[3]", "patch.remove(\"core:Scout\", \"table\", 3)")]
        public void AMapEntryIsRemovedByKeyNotByPath(string path, string hint)
        {
            var d = Diagnostics(Run("patch.remove(\"core:Scout\", \"" + path + "\")"));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadOp));
            Assert.That(d.Hint, Does.Contain(hint));
        }

        [TestCase("patch.add(\"core:Scout\", \"Morale\", 1)")]
        [TestCase("patch.append(\"core:Scout\", \"Morale\", Morale(value=1))")]
        [TestCase("patch.add(\"core:Scout\", \"Skill['core:Wood']\", 1)")]
        public void ComponentsCanOnlyBeAttachedOrDetached(string call)
        {
            var d = Diagnostics(Run(call));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadOp));
            Assert.That(d.Hint, Does.Contain("patch.set"));
        }

        [TestCase("patch.set(\"core:Scout\", \"Health\", Health(base_max=1))")]
        [TestCase("patch.remove(\"core:Scout\", \"Health\")")]
        [TestCase("patch.set(\"core:Scout\", \"Stock['core:Wood']\", Stock(amount=1))")]
        [TestCase("patch.remove(\"core:Scout\", \"Stock['core:Wood']\")")]
        public void WhatEveryDefHasCannotBeAttachedOrDetached(string call)
        {
            var d = Diagnostics(Run(call));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadOp));
            Assert.That(d.Message, Does.Contain("every"));
        }

        [Test]
        public void DetachingTakesNoThirdArgument()
        {
            var d = Diagnostics(Run("patch.remove(\"core:Scout\", \"Morale\", 1)"));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadOp));
            Assert.That(d.Message, Does.Contain("third argument"));
        }

        // ---- 값의 모양 ----

        [TestCase("patch.set(\"core:Scout\", \"move\", 1.5)", "int field")]
        [TestCase("patch.add(\"core:Scout\", \"move\", 0.5)", "int field")]
        [TestCase("patch.set(\"core:Scout\", \"move\", \"x\")", null)]
        [TestCase("patch.set(\"core:Scout\", \"move\", True)", null)]
        [TestCase("patch.set(\"core:Scout\", \"speed\", \"fast\")", null)]
        [TestCase("patch.set(\"core:Scout\", \"flag\", 1)", null)]
        [TestCase("patch.set(\"core:Scout\", \"name\", 1)", null)]
        [TestCase("patch.set(\"core:Scout\", \"tags\", \"x\")", null)]
        [TestCase("patch.set(\"core:Scout\", \"tags\", [1])", null)]
        [TestCase("patch.append(\"core:Scout\", \"tags\", \"fast\")", "write [value]")]
        [TestCase("patch.append(\"core:Scout\", \"tags\", [1])", null)]
        [TestCase("patch.remove(\"core:Scout\", \"tags\", 1)", null)]
        [TestCase("patch.set(\"core:Scout\", \"cost\", {1: 2})", null)]
        [TestCase("patch.set(\"core:Scout\", \"cost\", {\"core:Wood\": \"x\"})", null)]
        [TestCase("patch.append(\"core:Scout\", \"cost\", [1])", null)]
        [TestCase("patch.remove(\"core:Scout\", \"cost\", 3)", null)]
        [TestCase("patch.set(\"core:Scout\", \"cost['core:Wood']\", 1.5)", null)]
        [TestCase("patch.set(\"core:Scout\", \"table[3]\", 5)", null)]
        [TestCase("patch.set(\"core:Scout\", \"Health.base_max\", \"x\")", null)]
        [TestCase("patch.set(\"core:Scout\", \"Morale\", 5)", "Morale(...)")]
        [TestCase("patch.set(\"core:Scout\", \"Morale\", Health(base_max=1))", "Health(...)")]
        [TestCase("patch.set(\"core:Scout\", \"Skill['core:Wood']\", 5)", "Skill(...)")]
        [TestCase("patch.set(\"core:Scout\", \"move\", Morale(value=1))", null)]
        public void ValueOfTheWrongShape(string call, string? contains)
        {
            var d = Diagnostics(Run(call));
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.PatBadValue, 1)));
            if (contains != null) Assert.That(d.Message + " " + d.Hint, Does.Contain(contains));
        }

        [Test]
        public void AnIntegerFitsAFixedValueButNotTheOtherWayAround()
        {
            // spec-gaps G48
            Assert.That(Run("patch.set(\"core:Scout\", \"speed\", 2)").Resolved, Has.Count.EqualTo(1));
            var d = Diagnostics(Run("patch.set(\"core:Scout\", \"move\", 2.0)"));
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadValue));
        }

        [Test]
        public void TheErrorPointsAtTheValue()
        {
            Core();
            Mod("compat", defs: "patch.set(\"core:Scout\", \"move\",\n    \"x\")\n", extra: CoreDep);
            Load(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.PatBadValue, 2)));
        }

        [Test]
        public void EveryCallIsCheckedAndOnlyTheBadOnesAreReported()
        {
            Core();
            Mod("compat",
                defs: "patch.set(\"core:Scout\", \"move\", 1)\npatch.add(\"core:Scout\", \"tags\", [])\npatch.set(\"core:Scout\", \"move\", \"x\")\n",
                extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Line)),
                Is.EqualTo(new[] { (Codes.PatBadOp, 2), (Codes.PatBadValue, 3) }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }
    }
}
