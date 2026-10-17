using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Stubs;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>호스트 함수의 선언 파일과 스텁 (spec S7, spec-gaps G221).</summary>
    public class HostDeclarationTests : LoaderTestBase
    {
        private const string Tiles = "component(\"Terrain\", cost=field(int, default=1))\ndeftype(\"Tile\", components=[\"Terrain\"])\n";

        private const string Uses = "def go(a, b):\n    return core.path.find(a, b, max_steps=3)\n";

        private static HostFunctions Registered()
        {
            var host = new HostFunctions();
            var core = host.For("core");
            core.Pure("def path.find(start: Ref[Tile], goal: Ref[Tile], max_steps: int = 50) -> list[Ref[Tile]]",
                "가장 짧은 길을 찾는다.",
                (EntityId start, EntityId goal, long maxSteps) => new List<EntityId> { start, goal });
            core.Pure("def path.cost(tile: Ref[Tile]) -> int", "", (EntityId tile) => 1L);
            core.Writes("def units.heal(tile: Ref[Tile], amount: int | None = None) -> None", "체력을 채운다.",
                (HostAccess access, EntityId tile, long? amount) => { });
            return host;
        }

        private ContentLoadResult Load(HostFunctions host, out IReadOnlyList<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var result = ContentLoader.Load(_root, order, bag, host);
            diagnostics = bag.ToSortedList();
            return result;
        }

        // ---- 내보내기와 읽기 ----

        [Test]
        public void TheExportedFileListsNameSignatureEffectAndDoc()
        {
            var host = new HostFunctions();
            host.For("core").Pure("def math.double(n: int) -> int", "두 배.", (long n) => n * 2);
            Assert.That(host.ExportDeclarations(), Is.EqualTo(
                "{\n" +
                "  \"format\": \"kesa-engine-host-functions\",\n" +
                "  \"version\": 1,\n" +
                "  \"functions\": [\n" +
                "    {\n" +
                "      \"name\": \"core.math.double\",\n" +
                "      \"signature\": \"def math.double(n: int) -> int\",\n" +
                "      \"effect\": \"pure\",\n" +
                "      \"doc\": \"두 배.\"\n" +
                "    }\n" +
                "  ]\n" +
                "}\n"));
        }

        [Test]
        public void ReadingTheExportGivesTheSameFunctionsWithoutImplementations()
        {
            var original = Registered();
            var read = HostFunctions.ImportDeclarations(original.ExportDeclarations());
            Assert.That(read.All.Select(f => (f.Name, f.Signature, f.Effect, f.Doc)),
                Is.EqualTo(original.All.Select(f => (f.Name, f.Signature, f.Effect, f.Doc))));
            Assert.That(read.All.All(f => f.IsDeclarationOnly), Is.True);
            Assert.That(original.All.Any(f => f.IsDeclarationOnly), Is.False);
            // 읽은 것을 다시 내보내면 같은 글이다
            Assert.That(read.ExportDeclarations(), Is.EqualTo(original.ExportDeclarations()));
        }

        [Test]
        public void DeclarationsLoadLikeTheRegistration()
        {
            Mod("core", schema: Tiles);
            Mod("naval", defs: Uses, extra: ", dependencies=[\"core\"]");
            var real = Load(Registered(), out var realDiagnostics);
            Assert.That(realDiagnostics, Is.Empty, string.Join("\n", realDiagnostics));
            var declared = Load(HostFunctions.ImportDeclarations(Registered().ExportDeclarations()), out var diagnostics);
            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            Assert.That(declared.HostFunctions.Values.Select(s => s.Function.Name + s.Canonical),
                Is.EqualTo(real.HostFunctions.Values.Select(s => s.Function.Name + s.Canonical)));
        }

        [Test]
        public void ADeclarationFileIsCheckedLikeTheRegistration()
        {
            // 서명이 Def 타입과 어긋나면 선언 파일로 읽어도 같은 HST001이다
            Mod("core", schema: "deftype(\"Unit\")\n");
            Load(HostFunctions.ImportDeclarations(Registered().ExportDeclarations()), out var diagnostics);
            Assert.That(diagnostics.Select(d => d.Code).Distinct(), Is.EqualTo(new[] { Codes.HostSignature }));
        }

        [Test]
        public void AWorldCannotRunOnDeclarations()
        {
            Mod("core", schema: Tiles);
            var loaded = Load(HostFunctions.ImportDeclarations(Registered().ExportDeclarations()), out _);
            var e = Assert.Throws<ArgumentException>(() => new World(loaded))!;
            Assert.That(e.Message, Does.StartWith(
                "host function core.path.cost has no implementation: it was read from a declarations file, which is for " +
                "engine check and stubs only; register the C# function to run a world (spec-gaps G221)"));
        }

        [TestCase("{", "the host declarations file is not valid JSON (line 1)")]
        [TestCase("[]", "this is not a host declarations file: it needs \"format\": \"kesa-engine-host-functions\"")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 2, \"functions\": []}",
            "the host declarations file has a version this engine does not read: expected 1")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1}",
            "the host declarations file has no \"functions\" list")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [{\"name\": \"core.f\"}]}",
            "the host declarations file: functions[0] needs a string \"signature\"")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [" +
                  "{\"name\": \"core.f\", \"signature\": \"def f() -> int\", \"effect\": \"reads\", \"doc\": \"\"}]}",
            "the host declarations file: functions[0] has effect 'reads'; use \"pure\" or \"writes\"")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [" +
                  "{\"name\": \"core.g\", \"signature\": \"def f() -> int\", \"effect\": \"pure\", \"doc\": \"\"}]}",
            "the host declarations file: functions[0] is named 'core.g', but its signature declares another path: def f() -> int")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [" +
                  "{\"name\": \"core.f\", \"signature\": \"def f(a) -> int\", \"effect\": \"pure\", \"doc\": \"\"}]}",
            "the host declarations file: functions[0]: host function core.f: parameter 'a' needs a type")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [" +
                  "{\"name\": \"core.f\", \"signature\": \"def f() -> None\", \"effect\": \"pure\", \"doc\": \"\"}]}",
            "the host declarations file: functions[0]: host function core.f: a pure function that returns None does nothing")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [" +
                  "{\"name\": \"core.f\", \"signature\": \"def f(a: float) -> int\", \"effect\": \"pure\", \"doc\": \"\"}]}",
            "the host declarations file: functions[0]: host function core.f: 'float' is not a host function type")]
        [TestCase("{\"format\": \"kesa-engine-host-functions\", \"version\": 1, \"functions\": [" +
                  "{\"name\": \"Core.f\", \"signature\": \"def f() -> int\", \"effect\": \"pure\", \"doc\": \"\"}]}",
            "the host declarations file: functions[0]: 'Core' is not a mod id")]
        public void ABrokenFileIsAFormatError(string text, string expected)
        {
            var e = Assert.Throws<FormatException>(() => HostFunctions.ImportDeclarations(text))!;
            Assert.That(e.Message, Does.StartWith(expected));
        }

        // ---- 스텁 ----

        private const string ExpectedClasses =
            "class core:\n" +
            "    class path:\n" +
            "        @staticmethod\n" +
            "        def cost(tile: Tile) -> int: ...\n" +
            "        @staticmethod\n" +
            "        def find(start: Tile, goal: Tile, max_steps: int = ...) -> list[Tile]:\n" +
            "            \"\"\"가장 짧은 길을 찾는다.\"\"\"\n" +
            "            ...\n" +
            "    class units:\n" +
            "        @staticmethod\n" +
            "        def heal(tile: Tile, amount: int | None = ...) -> None:\n" +
            "            \"\"\"체력을 채운다.\"\"\"\n" +
            "            ...\n" +
            "\n";

        private ContentLoadResult LoadThree()
        {
            Mod("core", schema: Tiles);
            Mod("naval", schema: "", defs: Uses, extra: ", dependencies=[\"core\"]");
            Mod("music", defs: "x = 1\n");
            var result = Load(HostFunctions.ImportDeclarations(Registered().ExportDeclarations()), out var diagnostics);
            Assert.That(diagnostics, Is.Empty, string.Join("\n", diagnostics));
            return result;
        }

        [Test]
        public void TheStubNestsTheFunctionsAlongTheirNames()
        {
            var result = LoadThree();
            Assert.That(StubGenerator.Generate("naval", result), Does.Contain(ExpectedClasses));
            Assert.That(StubGenerator.Generate("core", result), Does.Contain(ExpectedClasses));
        }

        [Test]
        public void OnlyModsThatSeeTheOwnerGetTheClasses()
        {
            var result = LoadThree();
            Assert.That(StubGenerator.Generate("music", result), Does.Not.Contain("class core:"));
        }

        [Test]
        public void TheSchemaStubHasThemToo()
        {
            var result = LoadThree();
            Assert.That(StubGenerator.GenerateSchema("naval", result), Does.Contain(ExpectedClasses));
            Assert.That(StubGenerator.GenerateSchema("music", result), Does.Not.Contain("class core:"));
        }

        [Test]
        public void AQuoteInTheDocDoesNotEndTheDocstring()
        {
            Mod("core", schema: Tiles);
            var host = new HostFunctions();
            host.For("core").Pure("def f() -> int", "say \"\"\"hi\"\"\" \\ or \"end\"", () => 1L);
            var result = Load(host, out _);
            Assert.That(StubGenerator.Generate("core", result), Does.Contain(
                "        \"\"\"say \\\"\\\"\\\"hi\\\"\\\"\\\" \\\\ or \"end\\\"\"\"\"\n"));
        }
    }
}
