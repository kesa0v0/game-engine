using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>판 주인의 문제 통로 (G470): 어느 실행에서 나든 실패와 경고가 한곳에 쌓이고, 연결에게 가는 결과는 그대로다.</summary>
    public class LocalProblemTests
    {
        private const string Schema =
            "component(\"Hp\", v=field(int, default=3))\n" +
            "deftype(\"Hero\", components=[\"Hp\", \"Seatable\"])\n" +
            "signal(\"Hurt\", target=Ref[\"Hero\"])\n";

        private const string Command = "@command(free=True, acts_for=None)\n";

        private const string Defs =
            "patch.set(\"engine:Rules\", \"step_budget\", 200)\n" +
            "Hero(\"Red\")\n" +
            Command + "def Poke(h: Ref[Hero]):\n    emit(Hurt(target=h))\n" +
            Command + "def Crash():\n    h = 1 // 0\n" +
            Command + "def Heavy():\n    n = 0\n    for i in range(500):\n        n += 1\n" +
            Command + "def Picky():\n    with preconditions:\n        require(False, text(\"msg.no\"))\n    pass\n" +
            "@on(Hurt)\ndef hurt_fails(target):\n    target.Hp.v = 1 // 0\n" +
            "@on(Tick)\ndef tick_fails():\n    for h in query(Hero):\n        if h.Hp.v == 99:\n            h.Hp.v = 1 // 0\n" +
            "@ui_action\ndef oops():\n    n = 1 // 0\n" +
            "@ui_action\ndef poke_it(h: Ref[Hero]):\n    submit(\"Poke\", h=h)\n" +
            "@bot\ndef broken(me: Ref[Hero]):\n    n = 1 // 0\n" +
            "@bot\ndef poker(me: Ref[Hero]):\n    submit(\"Poke\", h=me)\n";

        private string _root = "";
        private World _world = null!;
        private LocalHost _host = null!;
        private IHostSession _alice = null!;
        private EntityId _red;

        [SetUp]
        public async Task Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            Write("arena/schema/types.py", Schema);
            Write("arena/defs/defs.py", Defs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            var bag = new DiagnosticBag();
            var content = ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
            Assert.That(bag.ToSortedList().Where(d => d.Severity == Severity.Error), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _world = new World(content);
            _red = _world.Create("arena:Red");
            _host = new LocalHost(content, _world);
            await _host.Seat(_red.Value, "alice");
            _alice = await _host.Open("alice");
        }

        [TearDown]
        public void Close()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Write(string relative, string text)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static IReadOnlyDictionary<string, BridgeValue> Args(params (string name, BridgeValue value)[] args) =>
            args.ToDictionary(a => a.name, a => a.value);

        private IReadOnlyDictionary<string, BridgeValue> Red => Args(("h", new BridgeEntity(_red.Value, null)));

        private BridgeProblems Read(long since = 0) => Wait(_host.Problems(since));

        private void BreakTick() => _world.Set(_red, _world.Member("Hp", "v"), new IntValue(99));

        [Test]
        public void AFailingTickHandlerIsReadOnceFromItsStamp()
        {
            Assert.That(Read().Items, Is.Empty);
            BreakTick();
            Wait(_host.Advance(1));
            var first = Read();
            var problem = first.Items.Single();
            Assert.That((problem.Source, problem.IsWarning, problem.Tick, first.Missed), Is.EqualTo(("arena.tick_fails", false, 1L, false)));
            Assert.That(problem.Code, Is.Not.Null);
            Assert.That(Read(first.Stamp).Items, Is.Empty);
            Assert.That(problem.ToString(), Does.Contain("arena.tick_fails"));
        }

        [Test]
        public void AHandlerACommandStartedFailsWithoutFailingTheCommand()
        {
            var result = Wait(_alice.Submit("arena.Poke", Red));
            Assert.That((result.Succeeded, result.Reasons.Count), Is.EqualTo((true, 0)), "the connection is not told");
            Assert.That(Read().Items.Single().Source, Is.EqualTo("arena.hurt_fails"));
        }

        [Test]
        public void AFailingCommandAndScreenActionAreNamed()
        {
            Assert.That(Wait(_alice.Submit("arena.Crash", Args())).Succeeded, Is.False);
            Assert.That(Wait(_alice.Act("arena.oops", Args())).Succeeded, Is.False);
            Assert.That(Read().Items.Select(p => p.Source), Is.EqualTo(new[] { "arena.Crash", "arena.oops" }));
        }

        [Test]
        public void ASubmitFromAScreenActionReachesTheLog()
        {
            Assert.That(Wait(_alice.Act("arena.poke_it", Red)).Succeeded, Is.True);
            Assert.That(Read().Items.Single().Source, Is.EqualTo("arena.hurt_fails"));
        }

        [Test]
        public void ABotsThoughtAndMovesReachTheLog()
        {
            var broken = Wait(_alice.Bot("arena.broken", _red.Value));
            Wait(broken.Act(_alice));
            Assert.That(Read().Items.Single().Source, Is.EqualTo("arena.broken"));

            var seen = Read().Stamp;
            var poker = Wait(_alice.Bot("arena.poker", _red.Value));
            Wait(poker.Act(_alice));
            Assert.That(Read(seen).Items.Single().Source, Is.EqualTo("arena.hurt_fails"));
        }

        [Test]
        public void AHeavyCommandIsAWarning()
        {
            Assert.That(Wait(_alice.Submit("arena.Heavy", Args())).Succeeded, Is.True);
            var problem = Read().Items.Single();
            Assert.That((problem.Code, problem.IsWarning, problem.Source), Is.EqualTo((Codes.RunHeavy, true, "arena.Heavy")));
        }

        [Test]
        public void ARefusedCommandIsNotAProblem()
        {
            Assert.That(Wait(_alice.Submit("arena.Picky", Args())).Succeeded, Is.False);
            Assert.That(Read().Items, Is.Empty);
        }

        [Test]
        public void OldProblemsAreDroppedAndThatIsSaid()
        {
            BreakTick();
            for (int i = 0; i < LocalProblems.Kept + 5; i++) Wait(_host.Advance(1));
            var old = Read();
            Assert.That((old.Missed, old.Items.Count, old.Stamp), Is.EqualTo((true, LocalProblems.Kept, (long)LocalProblems.Kept + 5)));
            Assert.That(Read(old.Stamp).Missed, Is.False);
        }

        [Test]
        public void TheLogOutlivesALoad()
        {
            Wait(_alice.Submit("arena.Crash", Args()));
            var save = Wait(_host.Save());
            Assert.That(Wait(_host.Load(save)).Succeeded, Is.True);
            Assert.That(Read().Items.Single().Source, Is.EqualTo("arena.Crash"));
        }

        private static T Wait<T>(ValueTask<T> task) => task.AsTask().GetAwaiter().GetResult();
    }
}
