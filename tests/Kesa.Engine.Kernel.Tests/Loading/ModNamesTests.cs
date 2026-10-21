using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 다른 모드의 최상위 이름 (spec-gaps G309): 필수 의존한 모드(와 자신)의 함수·상수를 <c>모드id.이름</c>으로 쓴다. 효과 규칙은 부르는
    /// 자리대로, 불린 함수는 제 모드의 이름으로 돈다.
    /// </summary>
    public class ModNamesTests : LoaderTestBase
    {
        private const string Schema = "component(\"Mark\", n=field(int, default=0))\ndeftype(\"Ship\", components=[\"Mark\"])\n";

        private const string Core =
            "LIMIT = 7\n" +
            "def helper():\n    return 10\n" +
            "def bonus(s):\n    return s.Mark.n + helper()\n" +
            "def bump(s):\n    s.Mark.n += 1\n" +
            "def roll():\n    return random.randint(1, 6)\n" +
            "def total(s):\n    return banking.helper() + banking.LIMIT\n" +
            "@command(free=True, acts_for=None)\ndef Move(s: Ref[Ship]):\n    s.Mark.n = total(s)\n" +
            "Ship(\"Sloop\")\n";

        private const string Depends = ", dependencies=[\"banking\"]";

        private static string Command(string body) => "@command(free=True, acts_for=None)\ndef Use(s: Ref[Ship]):\n" + body;

        private World _world = null!;
        private EntityId _sloop;

        private void Start(string scouts)
        {
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: scouts, extra: Depends);
            _world = new World(LoadClean());
            _sloop = _world.Create("banking:Sloop");
        }

        private SubmitResult Submit(string command) =>
            _world.Submit("host", command, new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop) });

        private long N() => ((IntValue)_world.Get(_sloop, _world.Member("Mark", "n"))).Value;

        private Diagnostic Single(string scouts, string extra = Depends, string scoutsSchema = "")
        {
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", schema: scoutsSchema, defs: scouts, extra: extra);
            Load(out var diags);
            return diags.Single();
        }

        [Test]
        public void ADependentModCallsFunctionsAndReadsConstants()
        {
            // bonus는 banking의 helper를 부른다: 불린 함수는 제 모드의 이름으로 돈다
            Start(Command("    banking.bump(s)\n    s.Mark.n = banking.bonus(s) * 100 + banking.LIMIT\n"));
            Assert.That(Submit("scouts.Use").Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(1107));
        }

        [Test]
        public void AModCanNameItself()
        {
            Start(Command("    pass\n"));
            Assert.That(Submit("banking.Move").Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(17));
        }

        [Test]
        public void AFunctionValueHeldAcrossAWaitSavesAndLoads()
        {
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: Command("    f = banking.bonus\n    wait(ticks=1)\n    s.Mark.n = f(s)\n"), extra: Depends);
            var content = LoadClean();
            _world = new World(content);
            _sloop = _world.Create("banking:Sloop");
            Assert.That(Submit("scouts.Use").Succeeded, Is.True);
            var loaded = World.Load(content, _world.Save());
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            _world.Advance(1);
            Assert.That(N(), Is.EqualTo(10));
        }

        [Test]
        public void AWritingFunctionOfAnotherModIsAStatement()
        {
            var d = Single(Command("    s.Mark.n = banking.bonus(s) + banking.bump(s)\n"));
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("'bump' changes the world, so it can only be called as a statement"));
        }

        [Test]
        public void ARandomFunctionOfAnotherModCannotBeUsedInADerived()
        {
            var d = Single("", scoutsSchema: "component(\"Luck\", v=derived(int, lambda h: banking.roll()))\n");
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Does.Contain("draws random numbers"));
        }

        [Test]
        public void ACommandOfAnotherModCannotBeCalled()
        {
            var d = Single(Command("    banking.Move(s)\n"));
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("'Move' is a @command, so logic cannot call it or use it as a value"));
        }

        [Test]
        public void AMisspelledNameIsCaughtAtLoad()
        {
            var d = Single(Command("    s.Mark.n = banking.bonuss(s)\n"));
            Assert.That(d.Code, Is.EqualTo(Codes.EvlUnknownName));
            Assert.That(d.Message, Is.EqualTo("mod 'banking' has no top-level name 'bonuss'"));
            Assert.That(d.Hint, Is.EqualTo("did you mean 'bonus'?"));
        }

        [Test]
        public void AModThatIsNotADependencyIsNotVisible()
        {
            var d = Single(Command("    s.Mark.n = banking.LIMIT\n").Replace("Ref[Ship]", "int"), extra: "");
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
            Assert.That(d.Message, Is.EqualTo("the names of mod 'banking' can only be used by mods that depend on it"));
        }

        [Test]
        public void AModIdAloneIsNotAValue()
        {
            var d = Single(Command("    m = banking\n"));
            Assert.That(d.Code, Is.EqualTo(Codes.EvlType));
            Assert.That(d.Message, Is.EqualTo("'banking' is a mod, not a value"));
        }

        [Test]
        public void ATopLevelNameCannotHideAMod()
        {
            var d = Single("banking = 1\n");
            Assert.That(d.Code, Is.EqualTo(Codes.EvlNameTaken));
            Assert.That(d.Message, Is.EqualTo("the top-level name 'banking' hides the names of mod 'banking' (banking.…)"));
        }

        [Test]
        public void ALocalNamedLikeTheModIsJustALocal()
        {
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: "def f(banking):\n    return banking.anything\n", extra: Depends);
            LoadClean();
        }

        [Test]
        public void TheStubOfADependentModDeclaresTheNames()
        {
            // 커맨드는 로직에서 부를 수 없으므로 빠진다 (G118). 의존하지 않는 모드는 보이지 않는다
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: "", extra: Depends);
            Mod("pirates", defs: "BOOTY = \"gold\"\n");
            var content = LoadClean();
            string stub = Kesa.Engine.Stubs.StubGenerator.Generate("scouts", content);
            Assert.That(stub, Does.Contain(
                "class banking:\n" +
                "    LIMIT: int\n" +
                "    @staticmethod\n    def helper() -> Any: ...\n" +
                "    @staticmethod\n    def bonus(s: Any) -> Any: ...\n" +
                "    @staticmethod\n    def bump(s: Any) -> Any: ...\n" +
                "    @staticmethod\n    def roll() -> Any: ...\n" +
                "    @staticmethod\n    def total(s: Any) -> Any: ...\n" +
                "\n"));
            Assert.That(stub, Does.Not.Contain("class pirates:").And.Not.Contain("class scouts:"));
        }

        [Test]
        public void AHostFunctionPathAndATopLevelNameCannotShareAName()
        {
            Mod("banking", schema: Schema, defs: Core);
            var host = new HostFunctions();
            host.For("banking").Pure("def helper.size() -> int", "", () => 3L);
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            ContentLoader.Load(_root, order, bag, host);
            var d = bag.ToSortedList().Single();
            Assert.That(d.Code, Is.EqualTo(Codes.HostName));
            Assert.That(d.Message, Is.EqualTo("'banking.helper' is both a host function group and this mod's top-level name 'helper'"));
        }

        [Test]
        public void HostFunctionsAndScriptNamesShareTheModId()
        {
            Mod("banking", schema: Schema, defs: Core);
            Mod("scouts", defs: Command("    s.Mark.n = banking.size() * 100 + banking.helper()\n"), extra: Depends);
            var host = new HostFunctions();
            host.For("banking").Pure("def size() -> int", "", () => 3L);
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var content = ContentLoader.Load(_root, order, bag, host);
            Assert.That(bag.ToSortedList(), Is.Empty);
            _world = new World(content);
            _sloop = _world.Create("banking:Sloop");
            Assert.That(Submit("scouts.Use").Succeeded, Is.True);
            Assert.That(N(), Is.EqualTo(310));
        }
    }
}
