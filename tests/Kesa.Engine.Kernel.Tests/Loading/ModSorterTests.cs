using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    public class ModSorterTests
    {
        /// <summary>"id: dep1 dep2 | after1 | opt1" 형식으로 매니페스트를 만든다.</summary>
        private static ModManifest M(string spec, string? folder = null)
        {
            var parts = spec.Split(':');
            string id = parts[0].Trim();
            var groups = (parts.Length > 1 ? parts[1] : "").Split('|');
            string List(int i) => i < groups.Length
                ? "[" + string.Join(", ", groups[i].Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Select(x => "\"" + x + "\"")) + "]"
                : "[]";

            var text = "Mod(id=\"" + id + "\", dependencies=" + List(0) + ", loadAfter=" + List(1) + ", optional=" + List(2) + ")\n";
            var bag = new DiagnosticBag();
            var m = ManifestReader.Read(text, folder ?? id, bag);
            Assert.That(bag.Count, Is.Zero, string.Join("\n", bag.ToSortedList()));
            return m!;
        }

        private static (LoadOrder Order, IReadOnlyList<Diagnostic> Diagnostics) Sort(params ModManifest[] mods)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(mods, bag);
            return (order, bag.ToSortedList());
        }

        private static string Ids(LoadOrder order) => string.Join(" ", order.Mods.Select(m => m.Id));

        [Test]
        public void DependenciesLoadFirst()
        {
            var (order, diags) = Sort(M("a: core"), M("core"));
            Assert.That(diags, Is.Empty);
            Assert.That(Ids(order), Is.EqualTo("core a"));
        }

        [Test]
        public void UnrelatedModsLoadInIdOrderRegardlessOfInputOrder()
        {
            var (order1, _) = Sort(M("c"), M("a"), M("b"));
            var (order2, _) = Sort(M("b"), M("c"), M("a"));
            Assert.That(Ids(order1), Is.EqualTo("a b c"));
            Assert.That(Ids(order2), Is.EqualTo("a b c"));
        }

        // spec-gaps G1: "관계없는 모드는 id 순"을 문자 그대로는 만족할 수 없는 경우
        [Test]
        public void TieBreakIsSmallestReadyIdFirst()
        {
            var (order, diags) = Sort(M("a: z"), M("b"), M("z"));
            Assert.That(diags, Is.Empty);
            Assert.That(Ids(order), Is.EqualTo("b z a"));
        }

        [Test]
        public void LoadAfterAndOptionalOrderWhenPresentAndAreIgnoredWhenAbsent()
        {
            var (order, diags) = Sort(M("a: | z | y"), M("z"), M("y"));
            Assert.That(diags, Is.Empty);
            Assert.That(Ids(order), Is.EqualTo("y z a"));

            var (order2, diags2) = Sort(M("a: | nothing_here | also_absent"));
            Assert.That(diags2, Is.Empty);
            Assert.That(Ids(order2), Is.EqualTo("a"));
        }

        [Test]
        public void AbsentLoadAfterThatLooksLikeATypoWarns()
        {
            var (order, diags) = Sort(M("a: | base_gme"), M("base_game"));
            Assert.That(Ids(order), Is.EqualTo("a base_game"));
            var d = diags.Single();
            Assert.That(d.Severity, Is.EqualTo(Severity.Warning));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'base_game'?"));
        }

        [Test]
        public void MissingDependencyDisablesModWithSuggestion()
        {
            var (order, diags) = Sort(M("a: cor"), M("core"));
            Assert.That(Ids(order), Is.EqualTo("core"));
            Assert.That(order.Disabled, Is.EqualTo(new[] { "a" }));
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.ModMissingDependency));
            Assert.That(d.Mod, Is.EqualTo("a"));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'core'?"));
        }

        [Test]
        public void FailurePropagatesThroughRequiredDependencies()
        {
            var (order, diags) = Sort(M("a: missing"), M("b: a"), M("c: b"), M("d"));
            Assert.That(Ids(order), Is.EqualTo("d"));
            Assert.That(order.Disabled, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(diags.Count(x => x.Code == Codes.ModDependencyFailed), Is.EqualTo(2));
        }

        [Test]
        public void CycleIsReportedWithPathAndDisablesMembers()
        {
            var (order, diags) = Sort(M("b: c"), M("c: a"), M("a: b"), M("d"));
            Assert.That(Ids(order), Is.EqualTo("d"));
            Assert.That(order.Disabled, Is.EqualTo(new[] { "a", "b", "c" }));
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.ModCycle));
            Assert.That(d.Message, Is.EqualTo("load order cycle: a -> c -> b -> a"));
            Assert.That(d.File, Is.EqualTo("a/mod.py"));
            Assert.That(d.Related.Select(s => s.File), Is.EqualTo(new[] { "c/mod.py", "b/mod.py" }));
        }

        [Test]
        public void SoftOrderingOnACycleMemberDoesNotDisable()
        {
            var (order, diags) = Sort(M("a: | b"), M("b: c"), M("c: b"));
            Assert.That(Ids(order), Is.EqualTo("a"));
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.ModCycle));
        }

        [Test]
        public void HardDependencyOnACycleMemberDisables()
        {
            var (order, diags) = Sort(M("a: b"), M("b: c"), M("c: b"));
            Assert.That(Ids(order), Is.Empty);
            Assert.That(diags.Select(x => x.Code), Is.EquivalentTo(new[] { Codes.ModCycle, Codes.ModDependencyFailed }));
        }

        [Test]
        public void DuplicateIdsDisableBothWithRelatedLocations()
        {
            var (order, diags) = Sort(M("a", "folder1"), M("a", "folder2"), M("b: a"));
            Assert.That(Ids(order), Is.Empty);
            var dup = diags.Where(x => x.Code == Codes.ModDuplicateId).ToList();
            Assert.That(dup, Has.Count.EqualTo(2));
            Assert.That(dup[0].Related.Single().File, Is.EqualTo("folder2/mod.py"));
            Assert.That(diags.Select(x => x.Code), Does.Contain(Codes.ModDependencyFailed));
        }

        [Test]
        public void OrderedRelationIsTransitiveAndExcludesTieBreak()
        {
            var (order, _) = Sort(M("a"), M("b: a"), M("c: | b"), M("x"));
            Assert.That(order.IsOrdered("a", "c"), Is.True);
            Assert.That(order.IsOrdered("c", "a"), Is.True);
            Assert.That(order.IsOrdered("a", "x"), Is.False);
            Assert.That(order.IsOrdered("b", "x"), Is.False);
        }
    }
}
