using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Schema;
using Kesa.Engine.Stubs;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary><c>ui_state(...)</c> 선언: UI 로컬 기록의 칸과 이름 공간 (spec-gaps G277·G279·G281).</summary>
    public class UiStateDeclarationTests : LoaderTestBase
    {
        private const string Units =
            "component(\"Pos\", x=field(int, default=0))\ndeftype(\"Unit\", components=[\"Pos\"])\n";

        [Test]
        public void AUiStateHoldsFieldsListsMapsAndOptionalRefs()
        {
            Mod("core", schema: Units +
                                "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True), panel=field(bool, default=False),\n" +
                                "    marks=field(dict[Ref[\"Unit\"], bool], default={}), seen=field(list[int], default=[]))\n");
            var result = LoadClean();
            Assert.That(result.Schema.TryGetUiState("Selection", out var state), Is.True);
            Assert.That(state.Members.Select(m => m.Name), Is.EqualTo(new[] { "unit", "panel", "marks", "seen" }));
            Assert.That(state.TryGetMember("marks", out var marks), Is.True);
            Assert.That(marks.Spec.Type.Display, Is.EqualTo("dict[Ref[\"Unit\"], bool]"));
            // 컴포넌트가 아니다: 엔티티에 붙지 않고 Def 파일의 생성자도 아니다
            Assert.That(result.Schema.TryGetComponent("Selection", out _), Is.False);
        }

        [Test]
        public void EveryFieldNeedsADefault()
        {
            Mod("core", schema: "ui_state(\"Tabs\", open=field(int))\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchBadDeclaration));
            Assert.That(d.Message, Is.EqualTo("'open' needs a default"));
        }

        [Test]
        public void ARefMustBeOptional()
        {
            Mod("core", schema: Units + "ui_state(\"Selection\", unit=ref(\"Unit\"))\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'unit' must be ref(..., optional=True)"));
        }

        [Test]
        public void ConstAndDerivedValuesAreNotUiState()
        {
            Mod("core", schema: "ui_state(\"Tabs\", n=const(int, default=1))\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'n': a UI state holds field(...) and ref(...) only, not const values"));
        }

        [Test]
        public void TheDefaultMustFitTheType()
        {
            Mod("core", schema: "ui_state(\"Tabs\", open=field(int, default=\"x\"))\n");
            Load(out var diags);
            Assert.That(diags.Any(d => d.Severity == Severity.Error), Is.True);
        }

        [Test]
        public void TheRefTargetMustBeADefType()
        {
            Mod("core", schema: Units + "ui_state(\"Selection\", unit=ref(\"Unitt\", optional=True))\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchUnknownName));
            Assert.That(d.Hint, Does.Contain("Unit"));
        }

        [Test]
        public void AUiStateIsNotAComponentOfADefType()
        {
            Mod("core", schema: "ui_state(\"Selection\", n=field(int, default=0))\ndeftype(\"Unit\", components=[\"Selection\"])\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("'Selection' is a UI state, not a component"));
        }

        [Test]
        public void UiStatesShareTheNamespaceOfComponents()
        {
            // G281: 스텁에서 둘 다 class Selection이 된다
            Mod("core", schema: "component(\"Selection\", n=field(int, default=0))\nui_state(\"Selection\", m=field(int, default=0))\n");
            Load(out var diags);
            var d = diags.First(x => x.Code == Codes.SchDuplicateName);
            Assert.That(d.Message, Is.EqualTo("'Selection' is declared more than once"));
            Assert.That(d.Hint, Does.Contain("UI states"));
        }

        [Test]
        public void ComponentsStillCannotHoldListsOrEntityKeys()
        {
            // field(...)의 리스트 검사가 component()로 옮겨 갔어도 같다
            Mod("core", schema: "component(\"Bag\", items=field(list[int], default=[]))\n");
            Load(out var diags);
            Assert.That(diags.Single().Message, Is.EqualTo("runtime fields cannot hold lists or maps"));

            TearDown();
            SetUp();
            Mod("core", schema: Units + "signal(\"Marked\", marks=dict[Ref[\"Unit\"], bool])\n");
            Load(out var signalDiags);
            Assert.That(signalDiags.Single().Message, Is.EqualTo("map keys must be str, int or DefId[...], not Ref[\"Unit\"]"));
        }

        [Test]
        public void TheStubHasAUiObject()
        {
            Mod("core", schema: Units + "ui_state(\"Selection\", unit=ref(\"Unit\", optional=True), panel=field(bool, default=False))\n");
            string stub = StubGenerator.Generate("core", LoadClean());
            Assert.That(stub, Does.Contain("class Selection:\n    unit: Unit | None\n    panel: bool\n"));
            Assert.That(stub, Does.Contain("class _ui:\n    Selection: Selection\n\nui: _ui\n"));
            Assert.That(StubGenerator.GenerateSchema(), Does.Contain("def ui_state(name: str, /, **members: MemberDecl) -> None: ...\n"));
        }
    }
}
