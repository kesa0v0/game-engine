using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Schema;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>호스트 함수의 등록과 서명 검사 (spec S7, spec-gaps G215·G218·G222).</summary>
    public class HostFunctionTests : LoaderTestBase
    {
        private const string Tiles = "component(\"Terrain\", cost=field(int, default=1))\ndeftype(\"Tile\", components=[\"Terrain\"])\n";

        private static List<EntityId> Find(EntityId start, EntityId goal, long maxSteps) => new List<EntityId> { start, goal };

        private ContentLoadResult Load(HostFunctions host, out IReadOnlyList<Diagnostic> diagnostics)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var result = ContentLoader.Load(_root, order, bag, host);
            diagnostics = bag.ToSortedList();
            return result;
        }

        private static string Fails(Action register) => Assert.Throws<ArgumentException>(() => register())!.Message;

        // ---- 등록 ----

        [Test]
        public void TheSpecExampleRegisters()
        {
            var host = new HostFunctions();
            var core = host.For("core");
            core.Pure("def path.find(start: Ref[Tile], goal: Ref[Tile], max_steps: int = 50) -> list[Ref[Tile]]",
                "가장 짧은 길을 찾는다.",
                (EntityId start, EntityId goal, long maxSteps) => Find(start, goal, maxSteps));
            core.Writes("def economy.run_phase(region: Ref[Tile]) -> None", "지역 경제 단계를 돌린다.",
                (HostAccess access, EntityId region) => { });
            core.Writes("def economy.tax(region: Ref[Tile]) -> int", "세금을 걷는다.",
                (HostAccess access, EntityId region) => 3L);

            Assert.That(host.All.Select(f => f.Name), Is.EqualTo(new[] { "core.economy.run_phase", "core.economy.tax", "core.path.find" }));
            var find = host.All.Last();
            Assert.That(find.Mod, Is.EqualTo("core"));
            Assert.That(find.Path, Is.EqualTo("path.find"));
            Assert.That(find.Effect, Is.EqualTo(HostEffect.Pure));
            Assert.That(find.Doc, Is.EqualTo("가장 짧은 길을 찾는다."));
            Assert.That(host.All[0].Effect, Is.EqualTo(HostEffect.Writes));
        }

        [Test]
        public void AMethodGroupRegistersWithItsParameterNames()
        {
            var host = new HostFunctions();
            host.For("core").Pure<EntityId, EntityId, long, List<EntityId>>(
                "def find(start: Ref[Tile], goal: Ref[Tile], max_steps: int) -> list[Ref[Tile]]", "", Find);
            Assert.That(host.All.Single().Name, Is.EqualTo("core.find"));
        }

        [Test]
        public void ForReturnsTheSameRegistrarForAMod()
        {
            var host = new HostFunctions();
            Assert.That(host.For("core"), Is.SameAs(host.For("core")));
        }

        [TestCase("Core")]
        [TestCase("core.path")]
        [TestCase("")]
        public void TheOwnerMustBeAModId(string mod)
        {
            Assert.That(Fails(() => new HostFunctions().For(mod)), Does.StartWith("'" + mod + "' is not a mod id"));
        }

        [TestCase("path.find(a: int) -> int", "a host function signature starts with 'def', found 'path'")]
        [TestCase("def path.if(a: int) -> int", "expected a name, found 'if'")]
        [TestCase("def path.(a: int) -> int", "expected a name, found '('")]
        [TestCase("def find(a: int)", "a host function signature needs a return type before end of line")]
        [TestCase("def find(a: int) -> int: return a", "a host function signature is one 'def' line without a body, found ':'")]
        [TestCase("def find(*a: int) -> int", "'*' in parameter lists is not supported")]
        public void ABadSignatureIsARegistrationError(string signature, string message)
        {
            var e = Fails(() => new HostFunctions().For("core").Pure(signature, "", (long a) => a));
            Assert.That(e, Does.Contain(": " + message));
        }

        [Test]
        public void TheSamePathTwiceInOneModIsAnError()
        {
            var core = new HostFunctions().For("core");
            core.Pure("def find(a: int) -> int", "", (long a) => a);
            Assert.That(Fails(() => core.Pure("def find(a: int) -> int", "", (long a) => a)),
                Does.StartWith("host function core.find is registered twice"));
        }

        [Test]
        public void TheSamePathInTwoModsIsTwoFunctions()
        {
            var host = new HostFunctions();
            host.For("core").Pure("def find(a: int) -> int", "", (long a) => a);
            host.For("naval").Pure("def find(a: int) -> int", "", (long a) => a);
            Assert.That(host.All.Select(f => f.Name), Is.EqualTo(new[] { "core.find", "naval.find" }));
        }

        [Test]
        public void TheArgumentCountMustMatch()
        {
            Assert.That(Fails(() => new HostFunctions().For("core").Pure("def f(a: int, b: int) -> int", "", (long a) => a)),
                Does.StartWith("host function core.f: the signature takes 2 arguments, but the C# function takes 1 argument"));
            Assert.That(Fails(() => new HostFunctions().For("core").Writes("def f() -> None", "", (HostAccess x, long a) => { })),
                Does.StartWith("host function core.f: the signature takes 0 arguments, but the C# function takes 1 argument after the HostAccess"));
        }

        [Test]
        public void SwappedNamesAreCaught()
        {
            var e = Fails(() => new HostFunctions().For("core").Pure(
                "def f(start: Ref[Tile], goal: Ref[Tile]) -> int", "", (EntityId goal, EntityId start) => 0L));
            Assert.That(e, Does.StartWith("host function core.f: parameter 1 is 'start' in the signature but 'goal' in the C# function"));
        }

        [Test]
        public void CamelCaseMatchesSnakeCase()
        {
            new HostFunctions().For("core").Pure("def f(max_steps: int, x: int) -> int", "", (long maxSteps, long x) => maxSteps);
            Assert.That(Fails(() => new HostFunctions().For("core").Pure("def f(max_steps: int) -> int", "", (long maxstep) => 0L)),
                Does.Contain("'max_steps' in the signature but 'maxstep'"));
        }

        [Test]
        public void AParameterNeedsAType()
        {
            Assert.That(Fails(() => new HostFunctions().For("core").Pure("def f(a) -> int", "", (long a) => a)),
                Does.Contain("parameter 'a' needs a type"));
        }

        [TestCase("int", typeof(int), "parameter 'a' is int, so the C# function takes long, not int")]
        [TestCase("fixed", typeof(long), "parameter 'a' is fixed, so the C# function takes Fixed, not long")]
        [TestCase("Ref[Tile]", typeof(long), "parameter 'a' is Ref[Tile], so the C# function takes EntityId, not long")]
        [TestCase("DefId[Tile]", typeof(EntityId), "parameter 'a' is DefId[Tile], so the C# function takes string, not EntityId")]
        [TestCase("int | None", typeof(long), "parameter 'a' is int | None, so the C# function takes long?, not long")]
        [TestCase("int", typeof(long?), "parameter 'a' is int, so the C# function takes long, not long?")]
        [TestCase("list[int]", typeof(List<long>), "parameter 'a' is list[int], so the C# function takes IReadOnlyList<long>, not List<long>")]
        [TestCase("list[int]", typeof(IReadOnlyList<int>), "parameter 'a' is list[int], so the C# function takes IReadOnlyList<long>, not IReadOnlyList<int>")]
        [TestCase("dict[str, int]", typeof(IReadOnlyDictionary<string, long>), "parameter 'a' is dict[str, int], so the C# function takes Value (a dict is passed as the engine value, spec-gaps G222), not IReadOnlyDictionary<string, long>")]
        [TestCase("Tile", typeof(long), "'Tile' is not a host function type")]
        [TestCase("None", typeof(long), "'None' is not a host function type")]
        [TestCase("Ref[Tile] | int", typeof(long), "'Ref[Tile] | int' is not a host function type")]
        public void AnArgumentTypeMustMatch(string annotation, Type type, string message)
        {
            var e = Fails(() => RegisterOne(annotation, type));
            Assert.That(e, Does.StartWith("host function core.f: " + message));
        }

        [TestCase("int", typeof(long))]
        [TestCase("fixed", typeof(Fixed))]
        [TestCase("bool", typeof(bool))]
        [TestCase("str", typeof(string))]
        [TestCase("Ref[Tile]", typeof(EntityId))]
        [TestCase("DefId[Tile]", typeof(string))]
        [TestCase("int | None", typeof(long?))]
        [TestCase("Ref[Tile] | None", typeof(EntityId?))]
        [TestCase("str | None", typeof(string))]
        [TestCase("list[Ref[Tile]]", typeof(IReadOnlyList<EntityId>))]
        [TestCase("list[int | None]", typeof(IReadOnlyList<long?>))]
        [TestCase("dict[str, int]", typeof(Value))]
        [TestCase("Ref[Tile]", typeof(Value))]
        public void TheseArgumentTypesMatch(string annotation, Type type)
        {
            RegisterOne(annotation, type);
        }

        /// <summary>인자 하나(<paramref name="type"/>)를 받는 함수를 <c>def f(a: annotation) -> bool</c>로 등록한다.</summary>
        private static void RegisterOne(string annotation, Type type)
        {
            var registrar = new HostFunctions().For("core");
            var method = typeof(HostFunctionTests).GetMethod(nameof(Take), System.Reflection.BindingFlags.NonPublic |
                                                                           System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type);
            var f = method.CreateDelegate(typeof(Func<,>).MakeGenericType(type, typeof(bool)));
            var pure = typeof(HostModFunctions).GetMethods()
                .Single(m => m.Name == "Pure" && m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(type, typeof(bool));
            try
            {
                pure.Invoke(registrar, new object[] { "def f(a: " + annotation + ") -> bool", "", f });
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                throw e.InnerException!;
            }
        }

        private static bool Take<T>(T a) => true;

        [Test]
        public void ReturnTypesConvertToTheSignature()
        {
            var core = new HostFunctions().For("core");
            core.Pure("def a() -> list[Ref[Tile]]", "", () => new List<EntityId>());
            core.Pure("def b() -> list[int]", "", () => new[] { 1L });
            core.Pure("def c() -> dict[str, int]", "", () => (Value)NoneValue.Instance);
            core.Pure("def d() -> str | None", "", () => (string?)null);
            Assert.That(Fails(() => core.Pure("def e() -> int", "", () => 1)),
                Does.StartWith("host function core.e: the signature returns int, so the C# function returns long, not int"));
            Assert.That(Fails(() => core.Pure("def g() -> list[int]", "", () => new List<int>())),
                Does.Contain("returns IReadOnlyList<long>, not List<int>"));
        }

        [Test]
        public void NoneReturnsMatchOnlyWritesWithoutAValue()
        {
            var core = new HostFunctions().For("core");
            Assert.That(Fails(() => core.Pure("def a() -> None", "", () => 1L)),
                Does.StartWith("host function core.a: a pure function that returns None does nothing"));
            Assert.That(Fails(() => core.Writes("def b() -> None", "", (HostAccess x) => 1L)),
                Does.StartWith("host function core.b: the signature returns None, but the C# function returns long"));
            Assert.That(Fails(() => core.Writes("def c() -> int", "", (HostAccess x) => { })),
                Does.StartWith("host function core.c: the signature returns int, but the C# function returns nothing"));
        }

        // ---- 로드 ----

        [Test]
        public void LoadingResolvesTheSignatureWithTheSchema()
        {
            Mod("core", schema: Tiles);
            var host = new HostFunctions();
            host.For("core").Pure("def path.find(start: Ref[Tile], goal: Ref[Tile], max_steps: int = 50) -> list[Ref[Tile]]",
                "", (EntityId start, EntityId goal, long maxSteps) => Find(start, goal, maxSteps));
            host.For("core").Writes("def economy.run(at: Ref[Tile] | None = None) -> None", "", (HostAccess a, EntityId? at) => { });
            var result = Load(host, out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));

            Assert.That(result.HostFunctions.Keys, Is.EqualTo(new[] { "core.economy.run", "core.path.find" }));
            var find = result.HostFunctions["core.path.find"];
            Assert.That(find.Parameters.Select(p => p.Name + ": " + p.Type.Display),
                Is.EqualTo(new[] { "start: Ref[\"Tile\"]", "goal: Ref[\"Tile\"]", "max_steps: int" }));
            Assert.That(((IntValue)find.Parameters[2].Default!).Value, Is.EqualTo(50));
            Assert.That(find.Returns!.Display, Is.EqualTo("list[Ref[\"Tile\"]]"));
            var run = result.HostFunctions["core.economy.run"];
            Assert.That(run.Returns, Is.Null);
            Assert.That(run.Parameters.Single().Default, Is.SameAs(NoneValue.Instance));
        }

        [Test]
        public void AnUnknownDefTypeIsALoadErrorOfTheOwner()
        {
            Mod("core", schema: Tiles);
            Mod("naval", defs: "x = 1\n", extra: ", dependencies=[\"core\"]");
            var host = new HostFunctions();
            host.For("core").Pure("def path.find(start: Ref[Tiles]) -> int", "", (EntityId start) => 0L);
            var result = Load(host, out var diags);

            var d = diags.First(x => x.Code == Codes.HostSignature);
            Assert.That(d.Message, Is.EqualTo("there is no Def type 'Tiles'"));
            Assert.That(d.Hint, Does.Contain("Tile"));
            Assert.That(d.Mod, Is.EqualTo("core"));
            Assert.That(d.Span.File, Is.EqualTo("<host core.path.find>"));
            Assert.That(d.Span.Line, Is.EqualTo(1));
            Assert.That(d.Span.Col, Is.EqualTo(26));
            // 소유 모드와 거기 의존한 모드가 함께 빠진다
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core", "naval" }));
            Assert.That(result.HostFunctions, Is.Empty);
        }

        [Test]
        public void ATypeTheOwnerDoesNotDependOnIsALoadError()
        {
            Mod("core", schema: "x = 1\n");
            Mod("naval", schema: "component(\"Hull\", hp=field(int, default=1))\ndeftype(\"Ship\", components=[\"Hull\"])\n");
            var host = new HostFunctions();
            host.For("core").Pure("def sink(ship: Ref[Ship]) -> bool", "", (EntityId ship) => true);
            var result = Load(host, out var diags);

            var d = diags.Single(x => x.Code == Codes.HostSignature);
            Assert.That(d.Message, Is.EqualTo("Def type 'Ship' belongs to mod 'naval', which mod 'core' does not depend on"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [TestCase("max_steps: int = \"far\"", "the default of 'max_steps' must be int, not str")]
        [TestCase("max_steps: int = 1 + 2", "a default parameter value must be a literal")]
        [TestCase("max_steps: int = None", "the default of 'max_steps' is None, but its type int does not allow None")]
        public void ADefaultThatDoesNotFitIsALoadError(string parameter, string message)
        {
            Mod("core", schema: Tiles);
            var host = new HostFunctions();
            host.For("core").Pure("def f(" + parameter + ") -> int", "", (long maxSteps) => maxSteps);
            var result = Load(host, out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.HostSignature));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void AFunctionOfAModThatIsNotLoadedIsNotUsed()
        {
            Mod("core", schema: Tiles);
            var host = new HostFunctions();
            host.For("core").Pure("def a() -> int", "", () => 1L);
            host.For("naval").Pure("def sink(ship: Ref[Ship]) -> bool", "", (EntityId ship) => true);
            var result = Load(host, out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
            Assert.That(result.HostFunctions.Keys, Is.EqualTo(new[] { "core.a" }));
        }

        [Test]
        public void LoadingWithoutHostFunctionsStillWorks()
        {
            Mod("core", schema: Tiles);
            var result = LoadClean();
            Assert.That(result.HostFunctions, Is.Empty);
        }

        [Test]
        public void APathCannotBeBothAFunctionAndAGroup()
        {
            var core = new HostFunctions().For("core");
            core.Pure("def path(a: int) -> int", "", (long a) => a);
            Assert.That(Fails(() => core.Pure("def path.find(a: int) -> int", "", (long a) => a)),
                Does.StartWith("host function core.path.find cannot be registered next to core.path: core.path would be both " +
                               "a function and a group of functions"));
            var other = new HostFunctions().For("core");
            other.Pure("def path.find(a: int) -> int", "", (long a) => a);
            Assert.That(Fails(() => other.Pure("def path(a: int) -> int", "", (long a) => a)),
                Does.StartWith("host function core.path cannot be registered next to core.path.find"));
        }

        [Test]
        public void ABuiltInNameCannotOwnHostFunctions()
        {
            Mod("len", schema: "x = 1\n");
            var host = new HostFunctions();
            host.For("len").Pure("def a() -> int", "", () => 1L);
            var result = Load(host, out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.HostSignature));
            Assert.That(d.Message, Is.EqualTo("mod id 'len' is a built-in name, so scripts cannot reach its host functions"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "len" }));
        }

        // ---- 스크립트에서 쓰는 이름 (G218) ----

        private HostFunctions PathHost()
        {
            var host = new HostFunctions();
            var core = host.For("core");
            core.Pure("def path.find(start: Ref[Tile], goal: Ref[Tile], max_steps: int = 50) -> list[Ref[Tile]]", "",
                (EntityId start, EntityId goal, long maxSteps) => Find(start, goal, maxSteps));
            core.Writes("def economy.tax(at: Ref[Tile]) -> int", "", (HostAccess a, EntityId at) => 1L);
            return host;
        }

        /// <summary>core에 의존하는 naval 모드의 Def 파일이 <paramref name="defs"/>이다.</summary>
        private IReadOnlyList<Diagnostic> LoadNaval(string defs, bool depends = true)
        {
            Mod("core", schema: Tiles);
            Mod("naval", defs: defs, extra: depends ? ", dependencies=[\"core\"]" : "");
            Load(PathHost(), out var diags);
            return diags;
        }

        [Test]
        public void AModThatDependsOnTheOwnerCallsItsFunctions()
        {
            var diags = LoadNaval("def go(a, b):\n    return core.path.find(a, b, max_steps=3)\n" +
                                  "def keep():\n    f = core.path.find\n    return f\n");
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }

        [Test]
        public void TheOwnerCallsItsOwnFunctions()
        {
            Mod("core", schema: Tiles, defs: "def go(a, b):\n    return core.path.find(a, b)\n");
            Load(PathHost(), out var diags);
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }

        [Test]
        public void AModThatDoesNotDependOnTheOwnerCannotUseThem()
        {
            var d = LoadNaval("def go(a, b):\n    return core.path.find(a, b)\n", depends: false).Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(d.Message, Is.EqualTo("the host functions of mod 'core' can only be used by mods that depend on it"));
            Assert.That(d.Hint, Is.EqualTo("add \"core\" to dependencies in mod.py (spec-gaps G218)"));
        }

        [TestCase("core.path.fnd(a, b)", Codes.EvlUnknownName, "core.path has no host function 'fnd'", "did you mean 'find'?")]
        [TestCase("core.pth.find(a, b)", Codes.EvlUnknownName, "core has no host function 'pth'", "did you mean 'path'?")]
        [TestCase("cor.path.find(a, b)", Codes.EvlUnknownName, "name 'cor' is not defined", "did you mean 'core'?")]
        [TestCase("core.path", Codes.HostName, "core.path is a group of host functions, not a value",
            "call one of them, such as core.path.find(...) (spec-gaps G218)")]
        [TestCase("core", Codes.HostName, "core is a group of host functions, not a value",
            "call one of them, such as core.economy.tax(...) (spec-gaps G218)")]
        [TestCase("core.path.find.name", Codes.HostName, "core.path.find is a host function, so it has no attribute 'name'", null)]
        public void HostNamesAreCheckedWhenTheModLoads(string expr, string code, string message, string? hint)
        {
            var d = LoadNaval("def go(a, b):\n    return " + expr + "\n").Single();
            Assert.That(d.Code, Is.EqualTo(code));
            Assert.That(d.Message, Is.EqualTo(message));
            Assert.That(d.Hint, Is.EqualTo(hint));
        }

        [Test]
        public void ALocalNamedLikeTheOwnerIsJustALocal()
        {
            var diags = LoadNaval("def go(core):\n    return core.anything\n");
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }

        [Test]
        public void ATopLevelNameThatHidesTheOwnerIsALoadError()
        {
            var diags = LoadNaval("core = 1\ndef go(a, b):\n    return core.path.find(a, b)\n");
            var d = diags.First();
            Assert.That(d.Code, Is.EqualTo(Codes.HostName));
            Assert.That(d.Message, Is.EqualTo("the top-level name 'core' hides the host functions of mod 'core'"));
            Assert.That(d.Span.Line, Is.EqualTo(1));
        }

        [Test]
        public void AWritingHostFunctionInsideAnExpressionIsALoadError()
        {
            var d = LoadNaval("@command(free=True, acts_for=None)\ndef Tax(t: Ref[Tile]):\n    n = core.economy.tax(t) + 1\n")
                .Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("core.economy.tax() changes the world, so it can only be called as a statement"));
        }

        [Test]
        public void AFunctionThatCallsAWritingHostFunctionWrites()
        {
            var d = LoadNaval("def levy(t):\n    core.economy.tax(t)\n    return 1\n" +
                              "@command(free=True, acts_for=None)\ndef Tax(t: Ref[Tile]):\n    n = levy(t) + 1\n").Single();
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("'levy' changes the world, so it can only be called as a statement"));
        }

        [Test]
        public void AWritingHostFunctionAsAStatementLoads()
        {
            var diags = LoadNaval("@command(free=True, acts_for=None)\ndef Tax(t: Ref[Tile]):\n    core.economy.tax(t)\n" +
                                  "    n = core.economy.tax(t)\n");
            Assert.That(diags, Is.Empty, string.Join("\n", diags));
        }
    }
}
