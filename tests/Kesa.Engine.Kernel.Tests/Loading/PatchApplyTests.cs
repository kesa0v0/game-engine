using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>④에서 패치를 Def에 적용하는 단계와 값에 의존하는 검사 (spec-gaps G42·G44·G50·G51·G53·G54·G60).</summary>
    public class PatchApplyTests : LoaderTestBase
    {
        private const string CoreDep = ", dependencies=[\"core\"]";

        private const string Schema =
            "component(\"Health\", base_max=const(int, default=10), regen=const(int, default=0))\n" +
            "component(\"Morale\", value=field(int, default=5))\n" +
            "deftype(\"Resource\")\n" +
            "component(\"Stock\", key=\"Resource\", amount=field(fixed, default=0))\n" +
            "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=field(int, default=0))\n" +
            "deftype(\"Unit\",\n" +
            "    components=[\"Health\", \"Stock\", \"Skill\"],\n" +
            "    move=const(int, default=1),\n" +
            "    ratio=const(fixed, default=0),\n" +
            "    speed=const(fixed, default=1.5),\n" +
            "    label=const(str, default=\"\"),\n" +
            "    tags=const(list[str]),\n" +
            "    cost=const(dict[DefId[\"Resource\"], int]),\n" +
            "    table=const(dict[int, str]),\n" +
            ")\n";

        private const string DefaultDefs =
            "Resource(\"Wood\")\nResource(\"Iron\")\n" +
            "Unit(\"Scout\", move=4, tags=[\"a\", \"b\"], cost={\"Wood\": 2}, table={1: \"x\"}, Health=Health(base_max=12))\n";

        // ratio에 default가 없는 스키마: add의 기준값이 없는 경우를 만든다. 필수 값이 비어도 되도록 Def는 abstract
        private static readonly string NoRatioDefault = Schema.Replace("ratio=const(fixed, default=0)", "ratio=const(fixed)");

        private const string TemplateDefs =
            "Resource(\"Wood\")\nResource(\"Iron\")\n" +
            "Unit(\"Scout\", abstract=True, move=4, tags=[\"a\", \"b\"], cost={\"Wood\": 2}, table={1: \"x\"})\n";

        private ContentLoadResult Run(string patches, out IReadOnlyList<Diagnostic> diags, string defs = DefaultDefs,
            string? schema = null)
        {
            Mod("core", schema: schema ?? Schema, defs: defs);
            Mod("compat", defs: patches, extra: CoreDep);
            return Load(out diags);
        }

        private ContentLoadResult RunClean(string patches, string defs = DefaultDefs)
        {
            var result = Run(patches, out var diags, defs);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            return result;
        }

        private static string P(string op, string path, string value = "", string target = "Scout") =>
            "patch." + op + "(\"" + target + "\", \"" + path + "\"" + (value.Length == 0 ? "" : ", " + value) + ")\n";

        private static ResolvedValue Const(ContentLoadResult r, string id, string name, string target = "Scout")
        {
            Assert.That(r.Expanded.TryGet(id, out var def), Is.True);
            Assert.That(def.Consts.TryGet(name, out var v), Is.True, id + "." + name);
            return v;
        }

        private static long Int(ResolvedValue v) => ((IntValue)v.Value).Value;

        private static string Strings(ResolvedValue v) =>
            string.Join(",", ((ListValue)v.Value).Items.Select(i => ((StrValue)i).Value));

        private static ResolvedValue Member(ContentLoadResult r, string id, string component, string member)
        {
            Assert.That(r.Expanded.TryGet(id, out var def), Is.True);
            Assert.That(def.Components.TryGet(component, out var c), Is.True, component);
            Assert.That(c.Members.TryGet(member, out var v), Is.True, component + "." + member);
            return v;
        }

        // ---- set, add ----

        [Test]
        public void SetReplacesTheValueAndRecordsThePatch()
        {
            var r = RunClean(P("set", "move", "9"));
            var move = Const(r, "Scout", "move");
            Assert.That(Int(move), Is.EqualTo(9));
            Assert.That(move.Origin, Is.EqualTo(ValueOrigin.Patched));
            Assert.That(move.Patch!.Mod, Is.EqualTo("compat"));
            Assert.That(r.PatchLog.Entries.Single().Status, Is.EqualTo(PatchStatus.Applied));
        }

        [Test]
        public void AddStartsFromTheCurrentValueIncludingTheSchemaDefault()
        {
            // G53: 적힌 값이 있으면 그 값, 없으면 default
            var r = RunClean(
                P("add", "move", "2") + P("add", "Health.base_max", "3") + P("add", "Health.regen", "1") +
                P("add", "speed", "0.5"), "Resource(\"Wood\")\nUnit(\"Scout\", move=4, Health=Health(base_max=12))\n");
            Assert.That(Int(Const(r, "Scout", "move")), Is.EqualTo(6));
            Assert.That(Int(Member(r, "Scout", "Health", "base_max")), Is.EqualTo(15));
            Assert.That(Int(Member(r, "Scout", "Health", "regen")), Is.EqualTo(1));
            Assert.That(((FixedValue)Const(r, "Scout", "speed").Value).Value.Raw, Is.EqualTo(2_000_000L));
        }

        [Test]
        public void AddToAFixedFieldAcceptsAnIntAndKeepsFixed()
        {
            var r = RunClean(P("add", "speed", "1"), "Unit(\"Scout\")\n");
            Assert.That(((FixedValue)Const(r, "Scout", "speed").Value).Value.Raw, Is.EqualTo(2_500_000L));
        }

        [Test]
        public void ManyAddsAccumulateInLoadOrder()
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\", move=1)\n");
            Mod("alpha", defs: P("set", "move", "10") + P("add", "move", "1"), extra: CoreDep);
            Mod("omega", defs: P("add", "move", "5") + P("set", "label", "\"x\""), extra: CoreDep + ", loadAfter=[\"alpha\"]");
            var r = Load(out var diags);
            Assert.That(diags.Select(d => d.Severity), Has.All.EqualTo(Severity.Warning).Or.Empty);
            Assert.That(Int(Const(r, "Scout", "move")), Is.EqualTo(16));
            Assert.That(r.PatchLog.Entries.Select(e => e.Status), Has.All.EqualTo(PatchStatus.Applied));
        }

        [Test]
        public void AddWithoutAnyValueIsAnErrorSuggestingSet()
        {
            var result = Run(P("add", "ratio", "1"), out var diags, TemplateDefs, NoRatioDefault);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.PatNoBase, "compat", 1)));
            Assert.That(d.Hint, Does.Contain("patch.set"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        [Test]
        public void AddToAMissingMapEntryIsAnError()
        {
            Run(P("add", "cost.Iron", "1"), out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.PatNoBase));
            Assert.That(d.Message, Does.Contain("no such map entry"));
        }

        [Test]
        public void AnIntegerOverflowIsAnError()
        {
            Run(P("add", "move", "9223372036854775807"), out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.PatOutOfRange));
        }

        // ---- 맵 항목 (G54) ----

        [Test]
        public void SetCreatesAMissingMapKeyAndChangesAnExistingOne()
        {
            var r = RunClean(P("set", "cost.Iron", "3") + P("set", "cost.Wood", "7") + P("set", "table[2]", "\"y\""));
            var cost = (DictValue)Const(r, "Scout", "cost").Value;
            Assert.That(cost.Items.Select(i => ((StrValue)i.Key).Value + "=" + ((IntValue)i.Value).Value),
                Is.EqualTo(new[] { "Wood=7", "Iron=3" }));
            var table = (DictValue)Const(r, "Scout", "table").Value;
            Assert.That(table.Items.Select(i => ((IntValue)i.Key).Value), Is.EqualTo(new long[] { 1, 2 }));
        }

        [Test]
        public void SetOfAMapEntryWorksWhenTheMapHasNoValueYet()
        {
            var r = RunClean(P("set", "cost.Wood", "5"), "Resource(\"Wood\")\nUnit(\"Scout\")\n");
            Assert.That(((DictValue)Const(r, "Scout", "cost").Value).Items, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddToAnExistingMapEntry()
        {
            var r = RunClean(P("add", "cost.Wood", "3"));
            var cost = (DictValue)Const(r, "Scout", "cost").Value;
            Assert.That(((IntValue)cost.Items.Single().Value).Value, Is.EqualTo(5));
        }

        // ---- append, remove ----

        [Test]
        public void AppendJoinsListsAndCreatesAMissingOne()
        {
            var r = RunClean(P("append", "tags", "[\"c\"]"), DefaultDefs);
            Assert.That(Strings(Const(r, "Scout", "tags")), Is.EqualTo("a,b,c"));
            var fresh = RunClean(P("append", "tags", "[\"c\"]"), "Unit(\"Scout\")\n");
            Assert.That(Strings(Const(fresh, "Scout", "tags")), Is.EqualTo("c"));
        }

        [Test]
        public void AppendToAMapRejectsAnExistingKey()
        {
            var ok = RunClean(P("append", "cost", "{\"Iron\": 1}"));
            Assert.That(((DictValue)Const(ok, "Scout", "cost").Value).Items, Has.Count.EqualTo(2));

            Run(P("append", "cost", "{\"Wood\": 1}"), out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod), Is.EqualTo((Codes.PatKeyExists, "compat")));
            Assert.That(d.Hint, Does.Contain("patch.set"));
        }

        [Test]
        public void RemoveTakesTheFirstEqualElementOrTheKey()
        {
            var r = RunClean(P("remove", "tags", "\"a\"") + P("remove", "cost", "\"Wood\"") + P("remove", "table", "1"));
            Assert.That(Strings(Const(r, "Scout", "tags")), Is.EqualTo("b"));
            Assert.That(((DictValue)Const(r, "Scout", "cost").Value).Items, Is.Empty);
            Assert.That(((DictValue)Const(r, "Scout", "table").Value).Items, Is.Empty);
        }

        [TestCase("remove", "tags", "\"zzz\"")]
        [TestCase("remove", "cost", "\"Iron\"")]
        [TestCase("remove", "table", "7")]
        public void RemovingSomethingThatIsNotThereIsAnError(string op, string path, string value)
        {
            var result = Run(P(op, path, value), out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.PatNoTarget, "compat", 1)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        [Test]
        public void RemoveFromAFieldWithNoValueIsAnError()
        {
            Run(P("remove", "tags", "\"a\""), out var diags, "Unit(\"Scout\")\n");
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.PatNoTarget));
        }

        // ---- 컴포넌트 붙이기·떼기 (G44) ----

        [Test]
        public void AComponentIsAttachedChangedAndDetached()
        {
            var attached = RunClean(P("set", "Morale", "Morale(value=7)"));
            Assert.That(Int(Member(attached, "Scout", "Morale", "value")), Is.EqualTo(7));
            Assert.That(Member(attached, "Scout", "Morale", "value").Origin, Is.EqualTo(ValueOrigin.Patched));

            var changed = RunClean(P("set", "Morale", "Morale(value=7)") + P("add", "Morale.value", "1"));
            Assert.That(Int(Member(changed, "Scout", "Morale", "value")), Is.EqualTo(8));

            var detached = RunClean(P("set", "Morale", "Morale(value=7)") + P("remove", "Morale"));
            Assert.That(detached.Expanded.TryGet("Scout", out var def), Is.True);
            Assert.That(def.Components.Contains("Morale"), Is.False);
        }

        [Test]
        public void AttachingTwiceIsAnError()
        {
            var result = Run(P("set", "Morale", "Morale(value=7)") + P("set", "Morale", "Morale(value=8)"), out var diags);
            // 같은 모드의 두 번째 set은 경고(G37)도 함께 낸다
            var d = diags.Single(x => x.Severity == Severity.Error);
            Assert.That((d.Code, d.Line), Is.EqualTo((Codes.PatAlreadyThere, 2)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        [Test]
        public void ADetachOrValuePatchNeedsTheComponentToBeAttached()
        {
            Run(P("remove", "Morale"), out var removed);
            Assert.That(removed.Single().Code, Is.EqualTo(Codes.PatNoComponent));

            Run(P("set", "Morale.value", "3"), out var changed);
            var d = changed.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.PatNoComponent));
            Assert.That(d.Hint, Does.Contain("attach it first"));
        }

        [Test]
        public void ValuesOfATypeSlotComponentCanAlwaysBePatched()
        {
            // Health은 Unit의 슬롯이라 모든 Def에 있다: 적지 않았어도 값을 패치할 수 있다
            var r = RunClean(P("set", "Health.regen", "2"), "Unit(\"Scout\")\n");
            Assert.That(Int(Member(r, "Scout", "Health", "regen")), Is.EqualTo(2));
        }

        // ---- 키 컴포넌트의 줄 ----

        [Test]
        public void RowsOfAListedKeyedComponentAreAttachedChangedAndDetached()
        {
            var r = RunClean(P("set", "Skill.Wood", "Skill(level=1)") + P("add", "Skill.Wood.level", "2"));
            Assert.That(r.Expanded.TryGet("Scout", out var def), Is.True);
            def.Components.TryGet("Skill", out var skill);
            skill.Rows.TryGet("Wood", out var row);
            row.Members.TryGet("level", out var level);
            Assert.That(Int(level), Is.EqualTo(3));

            var gone = RunClean(P("set", "Skill.Wood", "Skill(level=1)") + P("remove", "Skill.Wood"));
            gone.Expanded.TryGet("Scout", out var def2);
            def2.Components.TryGet("Skill", out var skill2);
            Assert.That(skill2.Rows.Contains("Wood"), Is.False);
        }

        [Test]
        public void ARowThatIsNotThereCannotBePatchedOrDetachedOrAttachedTwice()
        {
            Run(P("set", "Skill.Wood.level", "3"), out var d1);
            Assert.That(d1.Single().Code, Is.EqualTo(Codes.PatNoComponent));
            Run(P("remove", "Skill.Wood"), out var d2);
            Assert.That(d2.Single().Code, Is.EqualTo(Codes.PatNoComponent));
            Run(P("set", "Skill.Wood", "Skill(level=1)") + P("set", "Skill.Wood", "Skill(level=2)"), out var d3);
            Assert.That(d3.Single(x => x.Severity == Severity.Error).Code, Is.EqualTo(Codes.PatAlreadyThere));
        }

        [Test]
        public void EveryRowOfAKeysAllComponentExistsImplicitly()
        {
            var r = RunClean(P("set", "Stock.Wood.amount", "2.5"));
            r.Expanded.TryGet("Scout", out var def);
            def.Components.TryGet("Stock", out var stock);
            stock.Rows.TryGet("Wood", out var row);
            row.Members.TryGet("amount", out var amount);
            Assert.That(((FixedValue)amount.Value).Value.Raw, Is.EqualTo(2_500_000L));
        }

        // ---- 상속 위의 패치 (G50·G55) ----

        private const string Family =
            "Resource(\"Wood\")\n" +
            "Unit(\"Base\", move=2, tags=[\"a\"], abstract=True)\n" +
            "Unit(\"Scout\", parent=\"Base\")\n" +
            "Unit(\"Archer\", parent=\"Base\", move=inherit.move + 10)\n" +
            "Unit(\"Rogue\", parent=\"Scout\", move=inherit.move * 2)\n";

        [Test]
        public void AParentsPatchIsInheritedByItsChildren()
        {
            var r = RunClean(P("set", "move", "5", "Base") + P("append", "tags", "[\"b\"]", "Base"), Family);
            Assert.That(Int(Const(r, "Scout", "move")), Is.EqualTo(5));
            Assert.That(Const(r, "Scout", "move").Origin, Is.EqualTo(ValueOrigin.Inherited));
            Assert.That(Const(r, "Scout", "move").Patch!.Mod, Is.EqualTo("compat"));
            Assert.That(Strings(Const(r, "Scout", "tags")), Is.EqualTo("a,b"));
        }

        [Test]
        public void InheritExpressionsReadThePatchedParentValue()
        {
            var r = RunClean(P("set", "move", "5", "Base"), Family);
            Assert.That(Int(Const(r, "Archer", "move")), Is.EqualTo(15));
            Assert.That(Int(Const(r, "Rogue", "move")), Is.EqualTo(10));
        }

        [Test]
        public void ADefsOwnPatchesComeAfterItsInheritExpression()
        {
            // 상속 식을 먼저 평가하고 그 위에 패치를 쌓는다 (G55)
            var r = RunClean(P("add", "move", "1", "Archer") + P("set", "move", "100", "Rogue"), Family);
            Assert.That(Int(Const(r, "Archer", "move")), Is.EqualTo(13));
            Assert.That(Int(Const(r, "Rogue", "move")), Is.EqualTo(100));
            Assert.That(Int(Const(r, "Scout", "move")), Is.EqualTo(2));
        }

        [Test]
        public void ChildPatchesLandOnTheInheritedValue()
        {
            var r = RunClean(P("set", "move", "5", "Base") + P("add", "move", "1", "Scout"), Family);
            Assert.That(Int(Const(r, "Scout", "move")), Is.EqualTo(6));
            Assert.That(Int(Const(r, "Base", "move")), Is.EqualTo(5));
        }

        [TestCase("patch.set(\"Scout\", \"Morale\", Morale(value=inherit.move))\n")]
        [TestCase("patch.set(\"Scout\", \"Skill.Wood\", Skill(level=inherit.move + 1))\n")]
        [TestCase("patch.append(\"Scout\", \"tags\", [inherit.tags])\n")]
        [TestCase("patch.set(\"Scout\", \"move\", inherit.move)\n")]
        public void InheritIsRejectedInAPatchValue(string call)
        {
            var result = Run(call, out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.EvlUnsupported, "compat", 1)));
            // 컴포넌트 생성자 안의 inherit는 G62가, 그 밖의 자리는 평가기 자체가 거부한다
            if (call.Contains("Morale(") || call.Contains("Skill(")) Assert.That(d.Hint, Does.Contain("patch.add"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        // ---- 실패한 패치 (G60) ----

        [Test]
        public void AFailedPatchIsSkippedAndTheRestOfTheDefKeepsGoing()
        {
            var result = Run(P("set", "move", "9") + P("remove", "tags", "\"zzz\"") + P("set", "label", "\"x\""), out var diags);
            var d = diags.Single();
            Assert.That((d.Code, d.Mod, d.Line), Is.EqualTo((Codes.PatNoTarget, "compat", 2)));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        [Test]
        public void SeveralFailuresInOneDefAreAllReported()
        {
            Run(P("remove", "tags", "\"zzz\"") + P("remove", "cost", "\"Iron\"") + P("add", "ratio", "1"), out var diags, TemplateDefs,
                NoRatioDefault);
            Assert.That(diags.Select(d => d.Code), Is.EquivalentTo(new[] { Codes.PatNoTarget, Codes.PatNoTarget, Codes.PatNoBase }));
        }

        [Test]
        public void TheFailingModIsDisabledSoNothingOfItIsApplied()
        {
            var result = Run(P("set", "move", "9") + P("remove", "tags", "\"zzz\""), out _);
            Assert.That(Int(Const(result, "Scout", "move")), Is.EqualTo(4));
            Assert.That(result.PatchLog.Entries, Is.Empty);
        }

        [Test]
        public void StatusesShowWhichPatchesWereApplied()
        {
            // 실패한 모드는 비활성화되어 그 호출이 이력에서 빠지므로, 둘 다 살아남는 경우로 상태를 본다
            var r = RunClean(P("set", "move", "9") + P("add", "move", "1"));
            Assert.That(r.PatchLog.Entries.Select(e => e.Status), Is.EqualTo(new[] { PatchStatus.Applied, PatchStatus.Applied }));
        }

        [Test]
        public void AParentErrorDisablesTheDefsModAndPatchesOfThatDefDoNotRun()
        {
            Mod("core", schema: Schema, defs: "Unit(\"Scout\", parent=\"Nope\")\n");
            Mod("compat", defs: P("set", "move", "9"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => d.Code), Does.Contain(Codes.InhParentNotFound));
            Assert.That(result.Disabled, Does.Contain("core"));
        }

        [Test]
        public void AnOwnApplicationErrorOfAConflictingModIsReportedAsTheConflictOnly()
        {
            // alpha가 cost를 통째로 비우고 omega가 그 위에 add: omega의 add 실패는 충돌 탓이므로 PAT010 하나만 알린다
            Mod("core", schema: Schema, defs: DefaultDefs);
            Mod("alpha", defs: P("set", "cost", "{}"), extra: CoreDep);
            Mod("omega", defs: P("add", "cost.Wood", "1"), extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Mod)),
                Is.EquivalentTo(new[] { (Codes.PatConflict, "alpha"), (Codes.PatConflict, "omega") }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "alpha", "omega" }));
        }
    }
}
