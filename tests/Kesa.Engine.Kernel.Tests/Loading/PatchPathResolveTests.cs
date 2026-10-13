using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>③에서 패치 경로를 Def 타입의 스키마에 대해 따라가는 검사 (spec-gaps G34·G47, G38의 이름 범위).</summary>
    public class PatchPathResolveTests : LoaderTestBase
    {
        private const string CoreDep = ", dependencies=[\"core\"]";

        private void Core()
        {
            Mod("core",
                schema:
                "def health_max(h: Health) -> int:\n" +
                "    return h.base_max\n" +
                "component(\"Health\",\n" +
                "    base_max=const(int, default=10),\n" +
                "    max=derived(int, health_max),\n" +
                "    current=field(int, default_from=\"max\", min=0),\n" +
                ")\n" +
                "component(\"Morale\", value=field(int, default=5))\n" +
                "deftype(\"Resource\")\n" +
                "component(\"Stock\", key=\"Resource\", amount=field(fixed, default=0))\n" +
                "component(\"Skill\", key=\"Resource\", keys=\"listed\", level=field(int, default=0))\n" +
                "deftype(\"Unit\",\n" +
                "    components=[\"Health\", \"Stock\", \"Skill\"],\n" +
                "    move=const(int, default=1),\n" +
                "    tags=const(list[str]),\n" +
                "    cost=const(dict[DefId[\"Resource\"], int]),\n" +
                "    names=const(dict[str, int]),\n" +
                "    table=const(dict[int, str]),\n" +
                ")\n",
                defs: "Resource(\"Wood\")\nResource(\"core.Iron\")\nUnit(\"Scout\")\n");
        }

        private ContentLoadResult Patch(string path, string value = "1")
        {
            Mod("compat", defs: "patch.set(\"Scout\", \"" + path + "\", " + value + ")\n", extra: CoreDep);
            return Load(out _);
        }

        private PatchTarget Resolved(string path, string value = "1")
        {
            Core();
            var result = Patch(path, value);
            var resolved = result.Resolved.SingleOrDefault();
            Assert.That(resolved, Is.Not.Null, "path " + path + " was not resolved");
            return resolved!.Target;
        }

        private Diagnostic Rejected(string path)
        {
            Core();
            Mod("compat", defs: "patch.set(\"Scout\", \"" + path + "\", 1)\n", extra: CoreDep);
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Mod, Is.EqualTo("compat"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
            Assert.That(result.Resolved, Is.Empty);
            return d;
        }

        // ---- 통과하는 경로 ----

        [Test]
        public void ATypeConstIsTheTarget()
        {
            var t = Resolved("move");
            Assert.That(t.Kind, Is.EqualTo(PatchTargetKind.Const));
            Assert.That(t.Member!.Name, Is.EqualTo("move"));
            Assert.That(t.ValueType!.Display, Is.EqualTo("int"));
            Assert.That(t.Entry, Is.Null);
        }

        [Test]
        public void AListConstIsPatchedAsAWhole()
        {
            Assert.That(Resolved("tags", "[]").ValueType!.Display, Is.EqualTo("list[str]"));
        }

        [TestCase("cost.Wood", "Wood")]
        [TestCase("cost['Wood']", "Wood")]
        [TestCase("cost['core.Iron']", "core.Iron")]
        public void AMapEntryKeepsItsMapAndKey(string path, string key)
        {
            var t = Resolved(path);
            Assert.That(t.Kind, Is.EqualTo(PatchTargetKind.Const));
            Assert.That(t.ValueType!.Display, Is.EqualTo("int"));
            Assert.That(t.Entry!.Display, Is.EqualTo("dict[DefId[\"Resource\"], int]"));
            Assert.That(t.EntryKey!.Value.Text, Is.EqualTo(key));
        }

        [Test]
        public void TheWholeMapHasNoEntry()
        {
            var t = Resolved("cost", "{}");
            Assert.That(t.Entry, Is.Null);
            Assert.That(t.ValueType!.Display, Is.EqualTo("dict[DefId[\"Resource\"], int]"));
        }

        [Test]
        public void IntegerKeysFitIntegerMaps()
        {
            var t = Resolved("table[3]", "'x'");
            Assert.That(t.ValueType!.Display, Is.EqualTo("str"));
            Assert.That(t.EntryKey!.Value.Number, Is.EqualTo(3));
        }

        [Test]
        public void ANonSlotComponentCanBeAttached()
        {
            var t = Resolved("Morale", "Morale(value=1)");
            Assert.That((t.Kind, t.IsTypeSlot, t.Component!.Name), Is.EqualTo((PatchTargetKind.Component, false, "Morale")));
            Assert.That(t.ValueType, Is.Null);
        }

        [TestCase("Health.base_max")]
        [TestCase("Health.current")]
        public void AComponentValueIsATarget(string path)
        {
            var t = Resolved(path);
            Assert.That((t.Kind, t.IsTypeSlot), Is.EqualTo((PatchTargetKind.Member, true)));
            Assert.That(t.ValueType!.Display, Is.EqualTo("int"));
        }

        [Test]
        public void AValueOfANonSlotComponentIsResolvedByType()
        {
            // 붙어 있는지는 ④가 판정한다. ③은 컴포넌트와 값 이름이 스키마에 있는지만 본다
            var t = Resolved("Morale.value");
            Assert.That((t.Kind, t.IsTypeSlot), Is.EqualTo((PatchTargetKind.Member, false)));
        }

        [Test]
        public void ARowOfAKeyedComponentNamesTheKeyDef()
        {
            var t = Resolved("Skill.Wood", "Skill(level=1)");
            Assert.That((t.Kind, t.RowKey, t.IsTypeSlot), Is.EqualTo((PatchTargetKind.Row, "Wood", true)));
            Assert.That(t.RowDef!.Type.Name, Is.EqualTo("Resource"));
        }

        [Test]
        public void KeysWithDotsNeedBrackets()
        {
            var t = Resolved("Stock['core.Iron'].amount");
            Assert.That((t.Kind, t.RowKey), Is.EqualTo((PatchTargetKind.RowMember, "core.Iron")));
            Assert.That(t.Member!.Name, Is.EqualTo("amount"));
        }

        // ---- 거부하는 경로 ----

        [TestCase("mov", "move")]
        [TestCase("Moral", "Morale")]
        public void UnknownNameSuggestsAConstOrComponent(string path, string suggestion)
        {
            var d = Rejected(path);
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadField));
            Assert.That(d.Hint, Does.Contain(suggestion));
        }

        [Test]
        public void ADerivedValueCannotBePatched()
        {
            var d = Rejected("Health.max");
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadField));
            Assert.That(d.Message, Does.Contain("derived"));
            Assert.That(d.Related, Has.Count.EqualTo(1));
        }

        [Test]
        public void UnknownComponentValueSuggestsAName()
        {
            var d = Rejected("Health.bse_max");
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadField));
            Assert.That(d.Hint, Does.Contain("base_max"));
        }

        [TestCase("Health[3]")]
        [TestCase("Health['x']")]
        public void AComponentWithoutKeysHasNoRows(string path) =>
            Assert.That(Rejected(path).Code, Is.EqualTo(Codes.PatBadPathStep));

        [Test]
        public void AKeyedComponentIsPatchedRowByRow()
        {
            var d = Rejected("Stock");
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadPathStep));
            Assert.That(d.Message, Does.Contain("keyed"));
        }

        [Test]
        public void ARowKeyMustBeAnExistingDefOfTheKeyType()
        {
            var d = Rejected("Stock.Wod");
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadField));
            Assert.That(d.Hint, Does.Contain("Wood"));
        }

        [Test]
        public void ARowKeyIsNotAnInteger() =>
            Assert.That(Rejected("Stock[3]").Code, Is.EqualTo(Codes.PatBadPathStep));

        [TestCase("move.x")]
        [TestCase("Health.base_max.x")]
        [TestCase("tags[0]")]
        [TestCase("cost[3]")]
        [TestCase("table.x")]
        public void CannotGoInsideAValueThatIsNotAMap(string path) =>
            Assert.That(Rejected(path).Code, Is.EqualTo(Codes.PatBadPathStep));

        [Test]
        public void ADottedKeyInDotNotationGetsABracketHint()
        {
            var d = Rejected("names.core.Wood");
            Assert.That(d.Code, Is.EqualTo(Codes.PatBadPathStep));
            Assert.That(d.Hint, Does.Contain("names['core.Wood']"));
        }

        [TestCase("parent")]
        [TestCase("abstract")]
        [TestCase("override")]
        public void DefMetaInformationCannotBePatched(string path) =>
            Assert.That(Rejected(path).Code, Is.EqualTo(Codes.PatBadField));

        // ---- 이름의 범위 (G38) ----

        private void WithExtraMod(string compatExtra, string compatDefs)
        {
            Core();
            Mod("extra", schema: "extend(\"Unit\", bonus=const(int, default=0))\n", extra: CoreDep);
            Mod("compat", defs: compatDefs, extra: compatExtra);
        }

        [Test]
        public void ANameFromAModThatIsNotADependencyIsNotVisible()
        {
            WithExtraMod(CoreDep, "patch.set(\"Scout\", \"bonus\", 1)\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(d.Hint, Does.Contain("\"extra\""));
        }

        [Test]
        public void ANameFromAnOptionalModNeedsItsGuard()
        {
            WithExtraMod(CoreDep + ", optional=[\"extra\"]", "patch.set(\"Scout\", \"bonus\", 1)\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.PatNeedsGuard));
            Assert.That(d.Hint, Does.Contain("mod_loaded(\"extra\")"));
        }

        [Test]
        public void TheGuardOpensTheNamesOfTheOptionalMod()
        {
            WithExtraMod(CoreDep + ", optional=[\"extra\"]",
                "if mod_loaded(\"extra\"):\n    patch.set(\"Scout\", \"bonus\", 1)\n");
            var result = LoadClean();
            Assert.That(result.Resolved.Single().Target.Member!.Mod, Is.EqualTo("extra"));
        }

        [Test]
        public void ThePathIsCheckedForEveryCallInOrder()
        {
            Core();
            Mod("compat",
                defs: "patch.set(\"Scout\", \"move\", 1)\npatch.set(\"Scout\", \"nope\", 1)\npatch.set(\"Scout\", \"tags\", [])\n",
                extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Select(d => (d.Code, d.Line)), Is.EqualTo(new[] { (Codes.PatBadField, 2) }));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }
    }
}
