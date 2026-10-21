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
    /// 다른 모드의 함수를 감싸기 <c>@wrap(대상)</c> (spec-gaps G311~G314): 대상을 부르는 모든 자리가 감싼 것을 부르고, 감싼 함수는 첫
    /// 매개변수로 받은 안쪽 함수를 부른다.
    /// </summary>
    public class WrapTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Mark\", n=field(int, default=0), shown=derived(int, shown))\n" +
            "deftype(\"Ship\", components=[\"Mark\"])\n";

        private const string Core =
            "LIMIT = 7\n" +
            "def base(s):\n    return 10\n" +
            "def bump(s):\n    s.Mark.n += 1\n" +
            "def roll():\n    return random.randint(1, 6)\n" +
            "def step(s):\n    r = ask(s, \"Go\", choices=[1, 2])\n    s.Mark.n += r.choice\n" +
            "@command(free=True, acts_for=None)\ndef Use(s: Ref[Ship]):\n    s.Mark.n = base(s)\n" +
            "@command(free=True, acts_for=None)\ndef Bump(s: Ref[Ship]):\n    bump(s)\n" +
            "@command(free=True, acts_for=None)\ndef Step(s: Ref[Ship]):\n    step(s)\n    s.Mark.n *= 10\n" +
            "Ship(\"Sloop\")\n";

        // derived 함수는 스키마 파일이 먼저 정해야 한다
        private const string CoreSchema = "def shown(m):\n    return m.n + 1\n" + Schema;

        private const string Depends = ", dependencies=[\"banking\"]";

        private World _world = null!;
        private EntityId _sloop;

        private void Start(params (string Id, string Defs, string Extra)[] mods)
        {
            Mod("banking", schema: CoreSchema, defs: Core);
            foreach (var m in mods) Mod(m.Id, defs: m.Defs, extra: m.Extra);
            _world = new World(LoadClean());
            _sloop = _world.Create("banking:Sloop");
        }

        private SubmitResult Submit(string command) =>
            _world.Submit("host", command, new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop) });

        private long Get(string member) => ((IntValue)_world.Get(_sloop, _world.Member("Mark", member))).Value;

        private Diagnostic Single(string scouts, string extra = Depends)
        {
            Mod("banking", schema: CoreSchema, defs: Core);
            Mod("scouts", defs: scouts, extra: extra);
            Load(out var diags);
            return diags.Single();
        }

        [Test]
        public void TheCoresOwnCallRunsTheWrapper()
        {
            Start(("scouts", "@wrap(banking.base)\ndef more(original, s):\n    return original(s) + 5\n", Depends));
            Assert.That(Submit("banking.Use").Succeeded, Is.True);
            Assert.That(Get("n"), Is.EqualTo(15));
        }

        [Test]
        public void WrappersStackInLoadOrder()
        {
            // pirates는 scouts 뒤에 로드되어 바깥이다: (10 + 5) * 2
            Start(("scouts", "@wrap(banking.base)\ndef more(original, s):\n    return original(s) + 5\n", Depends),
                ("pirates", "@wrap(banking.base)\ndef twice(inner, s):\n    return inner(s=s) * 2\n",
                    ", dependencies=[\"banking\", \"scouts\"]"));
            Assert.That(Submit("banking.Use").Succeeded, Is.True);
            Assert.That(Get("n"), Is.EqualTo(30));
        }

        [Test]
        public void AWrapperNeedNotCallTheInnerFunction()
        {
            Start(("scouts", "@wrap(banking.base)\ndef fixed(original, s):\n    return 3\n", Depends));
            Assert.That(Submit("banking.Use").Succeeded, Is.True);
            Assert.That(Get("n"), Is.EqualTo(3));
        }

        [Test]
        public void ADerivedFunctionIsWrappedToo()
        {
            Start(("banking2", "", ""));
            Assert.That(Get("shown"), Is.EqualTo(1));
            Start(("scouts", "@wrap(banking.shown)\ndef more(original, m):\n    return original(m) + 100\n", Depends));
            Assert.That(Get("shown"), Is.EqualTo(101));
        }

        [Test]
        public void AWritingFunctionCanBeWrappedWithWrites()
        {
            Start(("scouts", "@wrap(banking.bump)\ndef more(original, s):\n    original(s)\n    s.Mark.n += 10\n", Depends));
            Assert.That(Submit("banking.Bump").Succeeded, Is.True);
            Assert.That(Get("n"), Is.EqualTo(11));
        }

        [Test]
        public void AModCanWrapItsOwnFunction()
        {
            Mod("banking", schema: CoreSchema, defs: Core + "@wrap(base)\ndef more(original, s):\n    return original(s) + 1\n");
            _world = new World(LoadClean());
            _sloop = _world.Create("banking:Sloop");
            Assert.That(Submit("banking.Use").Succeeded, Is.True);
            Assert.That(Get("n"), Is.EqualTo(11));
        }

        [Test]
        public void CallingTheWrappedFunctionByNameInsideTheWrapperIsRecursion()
        {
            Start(("scouts", "@wrap(banking.base)\ndef more(original, s):\n    return banking.base(s) + 1\n", Depends));
            var r = Submit("banking.Use");
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunCycle));
        }

        [Test]
        public void APausedWrapperSavesAndLoads()
        {
            Mod("banking", schema: CoreSchema, defs: Core);
            Mod("scouts", defs: "@wrap(banking.step)\ndef twice(original, s):\n    original(s)\n    s.Mark.n += 100\n", extra: Depends);
            var content = LoadClean();
            _world = new World(content);
            _sloop = _world.Create("banking:Sloop");
            Assert.That(Submit("banking.Step").Succeeded, Is.True);
            var save = _world.Save();
            Assert.That(System.Text.Encoding.UTF8.GetString(save), Does.Not.Contain("\"original\""), "the inner function is not saved");
            var loaded = World.Load(content, save);
            Assert.That(loaded.Diagnostics, Is.Empty);
            _world = loaded.World!;
            Assert.That(_world.Save(), Is.EqualTo(save));
            _world.Seat(_sloop, "alice");
            var answer = _world.Answer("alice", _world.OpenRequests.Single().Id, _sloop, 1);
            Assert.That(answer.Succeeded, Is.True, answer.Outcome + " " + answer.Message + " " + answer.Error);
            Assert.That(Get("n"), Is.EqualTo(1020));
        }

        // ---- 로드 오류 ----

        [TestCase("@wrap(banking.base)\ndef more(original, s):\n    s.Mark.n = 1\n    return 1\n",
            Codes.CmdWriteEffect, "'more' changes the world but 'base' does not, so it cannot wrap it")]
        [TestCase("@wrap(banking.base)\ndef more(original, s):\n    return original(s) + random.randint(1, 2)\n",
            Codes.CmdWriteEffect, "'more' draws random numbers but 'base' does not, so it cannot wrap it")]
        [TestCase("@wrap(banking.base)\ndef more(original, ship):\n    return 1\n",
            Codes.CmdBadDecorator, "after the inner function, a @wrap function takes the same parameters as 'base'")]
        [TestCase("@wrap(banking.base)\ndef more(original):\n    return 1\n",
            Codes.CmdBadDecorator, "after the inner function, a @wrap function takes the same parameters as 'base'")]
        [TestCase("@wrap(banking.Use)\ndef more(original, s):\n    pass\n",
            Codes.CmdBadDecorator, "@wrap: 'banking.Use' is a @command, so it cannot be wrapped")]
        [TestCase("@wrap(banking.LIMIT)\ndef more(original):\n    return 1\n",
            Codes.CmdBadDecorator, "@wrap needs a function defined with def, but 'banking.LIMIT' is a int")]
        [TestCase("@wrap(banking.bse)\ndef more(original, s):\n    return 1\n",
            Codes.EvlUnknownName, "mod 'banking' has no top-level name 'bse'")]
        [TestCase("@wrap(bankng.base)\ndef more(original, s):\n    return 1\n",
            Codes.EvlUnknownName, "@wrap: there is no mod 'bankng'")]
        [TestCase("@wrap(banking.base)\ndef more(original, s):\n    f = original\n    return f(s)\n",
            Codes.CmdBadDecorator, "'original' is the inner function of @wrap, so it can only be called")]
        [TestCase("@wrap(banking.base)\ndef more(original, s):\n    return original\n",
            Codes.CmdBadDecorator, "'original' is the inner function of @wrap, so it can only be called")]
        [TestCase("@wrap(banking.base)\ndef more(original, s):\n    return max([original(s)], key=lambda x: original(x))\n",
            Codes.CmdBadDecorator, "'original' is the inner function of @wrap, so it can only be called")]
        [TestCase("@wrap(banking.bump)\ndef more(original, s):\n    x = [original(s)]\n",
            Codes.CmdWriteEffect, "'bump' changes the world, so the inner function can only be called as a statement")]
        [TestCase("@wrap(banking.base)\ndef more(original, s=1):\n    return 1\n",
            Codes.CmdBadDecorator, "a @wrap function's parameter follows the wrapped function, so it has no default")]
        [TestCase("@wrap\ndef more(original, s):\n    return 1\n", Codes.CmdBadDecorator, "@wrap takes the function it wraps")]
        [TestCase("@wrap(\"banking.base\")\ndef more(original, s):\n    return 1\n",
            Codes.CmdBadDecorator, "@wrap needs a function name, like @wrap(pandemic.cure_cost)")]
        public void BadWrapsAreLoadErrors(string scouts, string code, string message)
        {
            var d = Single(scouts);
            Assert.That(d.Code, Is.EqualTo(code), d.Message);
            Assert.That(d.Message, Is.EqualTo(message));
        }

        [Test]
        public void AModThatIsNotADependencyCannotBeWrapped()
        {
            var d = Single("@wrap(banking.base)\ndef more(original, s):\n    return 1\n", extra: "");
            Assert.That(d.Code, Is.EqualTo(Codes.SchNotVisible));
        }

        [Test]
        public void AWrapperCannotBeCalledFromLogic()
        {
            var d = Single("@wrap(banking.base)\ndef more(original, s):\n    return 1\n" +
                           "def f(s):\n    return more(None, s)\n");
            Assert.That(d.Code, Is.EqualTo(Codes.CmdWriteEffect));
            Assert.That(d.Message, Is.EqualTo("'more' is a @wrap function, so logic cannot call it or use it as a value"));
        }

        [Test]
        public void AHostFunctionCannotBeWrapped()
        {
            Mod("banking", schema: CoreSchema, defs: Core);
            Mod("scouts", defs: "@wrap(banking.size)\ndef more(original):\n    return 1\n", extra: Depends);
            var host = new HostFunctions();
            host.For("banking").Pure("def size() -> int", "", () => 3L);
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            ContentLoader.Load(_root, order, bag, host);
            var d = bag.ToSortedList().Single();
            Assert.That(d.Message, Is.EqualTo("@wrap: 'banking.size' is a host function, so it cannot be wrapped"));
        }
    }
}
