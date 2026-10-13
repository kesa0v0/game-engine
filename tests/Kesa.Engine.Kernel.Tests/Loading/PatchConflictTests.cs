using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 같은 Def를 패치하는 호출끼리의 충돌 (spec-gaps G37·G41·G50)과 변경 이력 (G45).
    /// alpha와 omega는 둘 다 core에만 의존하고 서로의 순서는 선언하지 않는다 (omega가 loadAfter를 쓰면 선언한 경우).
    /// </summary>
    public class PatchConflictTests : LoaderTestBase
    {
        private const string CoreDep = "dependencies=[\"core\"]";

        private void Core()
        {
            Mod("core",
                schema:
                "component(\"Health\", base_max=const(int, default=10))\n" +
                "component(\"Morale\", value=field(int, default=5))\n" +
                "deftype(\"Resource\")\n" +
                "deftype(\"Unit\",\n" +
                "    components=[\"Health\"],\n" +
                "    move=const(int, default=1),\n" +
                "    tags=const(list[str]),\n" +
                "    cost=const(dict[DefId[\"Resource\"], int]),\n" +
                ")\n",
                defs: "Resource(\"Wood\")\nResource(\"Iron\")\nUnit(\"Scout\")\nUnit(\"Archer\")\n");
        }

        private void Alpha(string defs) => Mod("alpha", defs: defs, extra: ", " + CoreDep);

        private void Omega(string defs, bool afterAlpha = false) =>
            Mod("omega", defs: defs, extra: ", " + CoreDep + (afterAlpha ? ", loadAfter=[\"alpha\"]" : ""));

        private static string Call(string op, string path, string value, string target = "Scout") =>
            "patch." + op + "(\"" + target + "\", \"" + path + "\", " + value + ")\n";

        private ContentLoadResult LoadWith(out System.Collections.Generic.IReadOnlyList<Diagnostic> diags)
        {
            return Load(out diags);
        }

        // ---- 순서가 선언되지 않은 두 모드 ----

        [TestCase("set", "move", "1", "set", "move", "2")]
        [TestCase("set", "move", "1", "add", "move", "2")]
        [TestCase("add", "move", "1", "set", "move", "2")]
        [TestCase("remove", "tags", "'a'", "remove", "tags", "'b'")]
        [TestCase("append", "tags", "['a']", "remove", "tags", "'a'")]
        [TestCase("append", "cost", "{'Wood': 1}", "add", "cost.Wood", "1")]
        [TestCase("set", "cost", "{}", "add", "cost.Wood", "1")]
        [TestCase("add", "Health.base_max", "1", "set", "Health.base_max", "5")]
        [TestCase("set", "Morale", "Morale(value=1)", "set", "Morale.value", "2")]
        public void UnorderedModsThatOverlapAreBothErrors(string op1, string path1, string v1, string op2, string path2, string v2)
        {
            Core();
            Alpha(Call(op1, path1, v1));
            Omega(Call(op2, path2, v2));
            var result = LoadWith(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Mod)), Is.EquivalentTo(new[] { (Codes.PatConflict, "alpha"), (Codes.PatConflict, "omega") }));
            Assert.That(diags.All(d => d.Hint!.Contains("loadAfter")), Is.True);
            Assert.That(diags.All(d => d.Related.Count == 1), Is.True);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "alpha", "omega" }));
        }

        [TestCase("add", "move", "1", "add", "move", "2")]
        [TestCase("append", "tags", "['a']", "append", "tags", "['b']")]
        [TestCase("add", "cost.Wood", "1", "add", "cost.Iron", "1")]
        [TestCase("set", "move", "1", "set", "Health.base_max", "2")]
        [TestCase("set", "cost.Wood", "1", "set", "cost.Iron", "2")]
        public void UnorderedModsThatCommuteAreFine(string op1, string path1, string v1, string op2, string path2, string v2)
        {
            Core();
            Alpha(Call(op1, path1, v1));
            Omega(Call(op2, path2, v2));
            var result = LoadWith(out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.Resolved, Has.Count.EqualTo(2));
        }

        [Test]
        public void ModsPatchingDifferentDefsDoNotConflict()
        {
            Core();
            Alpha(Call("set", "move", "1", "Scout"));
            Omega(Call("set", "move", "2", "Archer"));
            Assert.That(LoadWith(out var diags).Resolved, Has.Count.EqualTo(2));
            Assert.That(diags, Is.Empty);
        }

        [Test]
        public void AConflictIsReportedOncePerPairAndDef()
        {
            Core();
            Alpha(Call("set", "move", "1") + Call("set", "Health.base_max", "1"));
            Omega(Call("set", "move", "2") + Call("set", "Health.base_max", "2"));
            LoadWith(out var diags);
            Assert.That(diags.Select(d => d.Mod), Is.EquivalentTo(new[] { "alpha", "omega" }));
        }

        [Test]
        public void ConflictsWaitForModsWithTheirOwnErrors()
        {
            // 자기 오류가 있는 omega가 먼저 빠지고, 그러면 alpha의 충돌은 사라진다 (G28·G49)
            Core();
            Alpha(Call("set", "move", "1"));
            Omega(Call("set", "move", "2") + Call("set", "nope", "1"));
            var result = LoadWith(out var diags);
            Assert.That(result.Disabled, Is.EqualTo(new[] { "omega" }));
            Assert.That(result.Mods.Select(m => m.Id), Is.EqualTo(new[] { "core", "alpha" }));
            // 빠지는 omega는 그 회차의 자기 오류와 충돌 오류를 함께 남기고, 살아남는 alpha에는 진단이 없다
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EquivalentTo(new[] { (Codes.PatBadField, "omega"), (Codes.PatConflict, "omega") }));
            Assert.That(result.Resolved.Select(p => p.Call.Mod), Is.EqualTo(new[] { "alpha" }));
        }

        // ---- 순서가 선언된 두 모드 ----

        [Test]
        public void OrderedModsDoNotConflictAndTheLaterOneWins()
        {
            Core();
            Alpha(Call("set", "move", "1"));
            Omega(Call("set", "move", "2"), afterAlpha: true);
            var result = LoadWith(out var diags);
            Assert.That(result.Disabled, Is.Empty);
            var d = diags.Single();
            Assert.That((d.Code, d.Severity, d.Mod), Is.EqualTo((Codes.PatOverridden, Severity.Warning, "omega")));
            Assert.That(d.Related, Has.Count.EqualTo(1));
            Assert.That(result.Resolved.Select(p => p.Call.Mod), Is.EqualTo(new[] { "alpha", "omega" }));
        }

        [TestCase("add", "move", "1", "set", "move", "2", true)]
        [TestCase("append", "tags", "['a']", "set", "tags", "[]", true)]
        [TestCase("set", "cost.Wood", "1", "set", "cost", "{}", true)]
        [TestCase("add", "cost.Wood", "1", "set", "cost", "{}", true)]
        [TestCase("set", "move", "1", "add", "move", "2", false)]
        [TestCase("set", "cost", "{}", "set", "cost.Wood", "1", false)]
        [TestCase("remove", "tags", "'a'", "set", "tags", "[]", false)]
        [TestCase("set", "tags", "[]", "remove", "tags", "'a'", false)]
        [TestCase("add", "move", "1", "add", "move", "2", false)]
        public void OnlyASetThatReplacesEarlierWorkIsWarned(string op1, string path1, string v1, string op2, string path2, string v2,
            bool warns)
        {
            Core();
            Alpha(Call(op1, path1, v1));
            Omega(Call(op2, path2, v2), afterAlpha: true);
            var result = LoadWith(out var diags);
            Assert.That(result.Disabled, Is.Empty);
            Assert.That(diags.Select(d => d.Code), Is.EqualTo(warns ? new[] { Codes.PatOverridden } : new string[0]));
        }

        [Test]
        public void TransitiveOrderCounts()
        {
            Core();
            Mod("alpha", defs: Call("set", "move", "1"), extra: ", " + CoreDep);
            Mod("mid", defs: "\n", extra: ", " + CoreDep + ", loadAfter=[\"alpha\"]");
            Mod("omega", defs: Call("set", "move", "2"), extra: ", " + CoreDep + ", loadAfter=[\"mid\"]");
            var result = LoadWith(out var diags);
            Assert.That(result.Disabled, Is.Empty);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.PatOverridden));
        }

        // ---- 같은 모드 ----

        [Test]
        public void ASecondSetOfTheSameFieldInOneModIsWarned()
        {
            Core();
            Alpha(Call("set", "move", "1") + Call("set", "move", "2"));
            var result = LoadWith(out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.PatOverridden, 2)));
            Assert.That(result.Resolved, Has.Count.EqualTo(2));
        }

        [Test]
        public void AnOwnAddAfterAnOwnSetIsFine()
        {
            Core();
            Alpha(Call("set", "move", "1") + Call("add", "move", "2"));
            Assert.That(LoadWith(out var diags).Resolved, Has.Count.EqualTo(2));
            Assert.That(diags, Is.Empty);
        }

        // ---- 변경 이력 (G45) ----

        [Test]
        public void TheLogListsEveryCallInApplicationOrder()
        {
            Core();
            Alpha(Call("set", "move", "1") + Call("add", "Health.base_max", "2"));
            Omega(Call("add", "move", "3") + Call("add", "move", "4", "Archer"), afterAlpha: true);
            var result = LoadWith(out _);
            var log = result.PatchLog;
            Assert.That(log.Entries.Select(e => e.Sequence), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(log.Entries.Select(e => e.Mod + ":" + e.Path.Text),
                Is.EqualTo(new[] { "alpha:move", "alpha:Health.base_max", "omega:move", "omega:move" }));
            Assert.That(log.Entries.Select(e => e.Status), Has.All.EqualTo(PatchStatus.Pending));
            Assert.That(log.ForDef("Scout").Select(e => e.Sequence), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(log.ForDef("Archer").Select(e => e.Mod), Is.EqualTo(new[] { "omega" }));
            Assert.That(log.ForDef("Nothing"), Is.Empty);
        }

        [Test]
        public void TheLogFindsEntriesWhosePathsOverlap()
        {
            Core();
            Alpha(Call("set", "cost.Wood", "1") + Call("set", "cost.Iron", "1") + Call("set", "move", "1"));
            Omega(Call("set", "cost", "{}"), afterAlpha: true);
            var log = LoadWith(out _).PatchLog;
            Assert.That(PatchPath.TryParse("cost['Wood']", out var wood, out _), Is.True);
            Assert.That(log.ForField("Scout", wood).Select(e => e.Mod + ":" + e.Path.Text),
                Is.EqualTo(new[] { "alpha:cost.Wood", "omega:cost" }));
            Assert.That(PatchPath.TryParse("move", out var move, out _), Is.True);
            Assert.That(log.ForField("Scout", move).Count(), Is.EqualTo(1));
        }

        [Test]
        public void ADisabledModLeavesNothingInTheLog()
        {
            Core();
            Alpha(Call("set", "move", "1"));
            Omega(Call("set", "nope", "1"));
            var result = LoadWith(out _);
            Assert.That(result.PatchLog.Entries.Select(e => e.Mod), Is.EqualTo(new[] { "alpha" }));
        }
    }
}
