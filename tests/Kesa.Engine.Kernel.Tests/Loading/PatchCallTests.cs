using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// ②b에서 <c>patch.*</c> 호출을 기록하는 단계 (spec-gaps G34·G35). 여기서는 호출 모양만 검사하고
    /// 대상 Def·경로·값의 모양은 ③에서 본다 (G42).
    /// </summary>
    public class PatchCallTests : LoaderTestBase
    {
        // 패치 대상 Def를 가진 모드. 패치하는 모드는 이 모드에 의존한다 (③이 대상을 검사한다)
        private const string CoreDep = ", dependencies=[\"core\"]";

        // 이 파일의 패치 경로(move, tags, Health.base_max, Morale, x, y, z, b, c)를 가진 타입
        private const string UnitSchema =
            "component(\"Health\", base_max=const(int, default=10))\n" +
            "component(\"Morale\", value=field(int, default=5))\n" +
            "deftype(\"Unit\", components=[\"Health\"], move=const(int, default=1), tags=const(list[str]),\n" +
            "    x=const(int, default=0), y=const(int, default=0), z=const(int, default=0),\n" +
            "    b=const(int, default=0), c=const(int, default=0))\n";

        private void Core(string id = "Scout") =>
            Mod("core", schema: UnitSchema, defs: "Unit(\"" + id + "\", tags=[\"slow\"])\n");

        [Test]
        public void RecordsEveryOperationInCallOrder()
        {
            Core();
            Mod("compat",
                defs: "patch.set(\"Scout\", \"move\", 4)\n" +
                      "patch.add(\"Scout\", \"Health.base_max\", 2)\n" +
                      "patch.append(\"Scout\", \"tags\", [\"fast\"])\n" +
                      "patch.remove(\"Scout\", \"tags\", \"slow\")\n" +
                      "patch.set(\"Scout\", \"Morale\", Morale(value=1))\n" +
                      "patch.remove(\"Scout\", \"Morale\")\n",
                extra: CoreDep);
            var result = LoadClean();
            var calls = result.Patches;
            Assert.That(calls.Select(c => c.Op),
                Is.EqualTo(new[] { PatchOp.Set, PatchOp.Add, PatchOp.Append, PatchOp.Remove, PatchOp.Set, PatchOp.Remove }));
            Assert.That(calls.Select(c => c.Index), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(calls.Select(c => c.Span.Line), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(calls.Select(c => c.Path.Text),
                Is.EqualTo(new[] { "move", "Health.base_max", "tags", "tags", "Morale", "Morale" }));
            Assert.That(calls.Select(c => c.TargetId), Has.All.EqualTo("Scout"));
            Assert.That(calls.Select(c => c.Mod), Has.All.EqualTo("compat"));
            Assert.That(((IntValue)calls[0].Value!).Value, Is.EqualTo(4));
            Assert.That(calls[2].Value, Is.InstanceOf<ListValue>());
            Assert.That(((StrValue)calls[3].Value!).Value, Is.EqualTo("slow"));
            Assert.That(calls[5].Value, Is.Null);
            Assert.That(calls.Select(c => c.Guards), Has.All.Empty);
        }

        [Test]
        public void CallsAreInModLoadOrderThenCallOrder()
        {
            Core("A");
            Mod("omega", defs: "patch.set(\"A\", \"x\", 1)\npatch.set(\"A\", \"y\", 1)\n",
                extra: ", loadAfter=[\"alpha\"]" + CoreDep);
            Mod("alpha", defs: "patch.set(\"A\", \"z\", 1)\n", extra: CoreDep);
            var result = LoadClean();
            Assert.That(result.Patches.Select(c => c.Mod + ":" + c.Path.Text), Is.EqualTo(new[] { "alpha:z", "omega:x", "omega:y" }));
        }

        [Test]
        public void ADisabledModLeavesNoPatchCalls()
        {
            Core("A");
            Mod("bad", defs: "patch.set(\"A\", \"b\", 1)\nNope()\n", extra: CoreDep);
            Mod("good", defs: "patch.set(\"A\", \"c\", 1)\n", extra: CoreDep);
            var result = Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(result.Patches.Select(c => c.Path.Text), Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public void ComponentValueIsAcceptedAsTheValueOfAttach()
        {
            Mod("core", schema: "component(\"Morale\", value=field(int, default=5))\ndeftype(\"Unit\")\n",
                defs: "Unit(\"Scout\")\n");
            Mod("compat", defs: "patch.set(\"Scout\", \"Morale\", Morale(value=7))\n", extra: ", dependencies=[\"core\"]");
            var result = LoadClean();
            var value = (ComponentValue)result.Patches.Single().Value!;
            Assert.That(value.Component.Name, Is.EqualTo("Morale"));
        }

        [TestCase("patch.set(\"A\", \"b\")", Codes.EvlArguments)]
        [TestCase("patch.set(\"A\", \"b\", 1, 2)", Codes.EvlArguments)]
        [TestCase("patch.set(\"A\", \"b\", value=1)", Codes.EvlArguments)]
        [TestCase("patch.add(\"A\")", Codes.EvlArguments)]
        [TestCase("patch.remove(\"A\")", Codes.EvlArguments)]
        [TestCase("patch.remove(\"A\", \"b\", 1, 2)", Codes.EvlArguments)]
        [TestCase("patch.set(1, \"b\", 1)", Codes.EvlType)]
        [TestCase("patch.set(\"\", \"b\", 1)", Codes.EvlArguments)]
        [TestCase("patch.set(\"A\", 1, 1)", Codes.EvlType)]
        [TestCase("patch.set(\"A\", \"a..b\", 1)", Codes.PatBadPath)]
        [TestCase("patch.set(\"A\", \"\", 1)", Codes.PatBadPath)]
        [TestCase("patch.set(\"A\", \"b\", patch.add)", Codes.DefBadValue)]
        public void BadPatchCalls(string call, string code)
        {
            Mod("compat", defs: call + "\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(code));
            Assert.That(d.Line, Is.EqualTo(1));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "compat" }));
        }

        [Test]
        public void UnknownPatchFunctionSuggestsAName()
        {
            Mod("compat", defs: "patch.sett(\"A\", \"b\", 1)\n");
            Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Hint, Does.Contain("set"));
        }

        [Test]
        public void PatchCallsAreNotAllowedInSchemaFiles()
        {
            Mod("core", schema: "patch.set(\"A\", \"b\", 1)\n");
            Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.SchDefInSchemaFile));
        }

        [Test]
        public void PatchCanBeShadowed()
        {
            // 엔진이 주는 이름은 덮어쓸 수 있는 내장이다 (G112). 덮어쓴 뒤의 patch.set은 모드의 값에 대한 속성 접근이다
            Mod("core", defs: "patch = 1\npatch.set(\"X\", \"n\", 1)\n");
            var result = Load(out var diags);
            Assert.That(diags.Single().Code, Is.EqualTo(Codes.EvlUnsupported), string.Join("\n", diags));
            Assert.That(result.Patches, Is.Empty);
        }
    }
}
