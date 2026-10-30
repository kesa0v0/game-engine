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
    /// <summary>
    /// 한 번 일어나는 효과 (G398·G426): 신호마다 <c>@world_fx</c>를 연결의 눈으로 돌려 커맨드 묶음별로 가져간다. 처음은 도장만, 늦으면 Missed.
    /// </summary>
    public class LocalWorldFxTests
    {
        private const string Schema =
            "component(\"Pos\", x=field(int, default=0))\n" +
            "deftype(\"Unit\", components=[\"Pos\"])\n" +
            "signal(\"Hit\", target=Ref[\"Unit\"], amount=int)\n" +
            "signal(\"Quiet\", n=int)\n";

        private const string Defs =
            "Unit(\"Foot\")\n" +
            "@command(free=True, acts_for=None)\ndef Strike(target: Ref[Unit], amount: int, times: int):\n" +
            "    for i in range(times):\n        emit(Hit(target=target, amount=amount))\n    emit(Quiet(n=1))\n" +
            "@world_fx(Hit)\ndef number(target, amount):\n" +
            "    return [fx(look(\"number\", amount=10 // amount), at=at(\"hex\", target.Pos.x))]\n" +
            "@world_fx(Hit)\ndef flash(target):\n    return [fx(\"flash\", at=at(0))]\n";

        private string _root = "";
        private World _world = null!;
        private IHostSession _alice = null!;
        private EntityId _foot;

        [SetUp]
        public async Task Open()
        {
            _root = Path.Combine(Path.GetTempPath(), "kesa-engine-tests", TestContext.CurrentContext.Test.ID);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
            var bag = new DiagnosticBag();
            var content = Load(Defs, bag);
            Assert.That(bag.ToSortedList(), Is.Empty, string.Join("\n", bag.ToSortedList()));
            _world = new World(content);
            _foot = _world.Create("arena:Foot");
            var host = new LocalHost(content, _world);
            _alice = await host.Open("alice");
        }

        [TearDown]
        public void Close()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private ContentLoadResult Load(string defs, DiagnosticBag bag)
        {
            Write("arena/schema/types.py", Schema);
            Write("arena/defs/defs.py", defs);
            Write("arena/mod.py", "Mod(id=\"arena\")\nuse_all(\"schema/\")\nuse_all(\"defs/\")\n");
            return ContentLoader.Load(_root, ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag), bag);
        }

        private void Write(string relative, string text)
        {
            var path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private void Strike(long amount, long times = 1)
        {
            var result = _world.Submit("host", "arena.Strike", new Dictionary<string, Value>
            {
                ["target"] = new EntityValue(_foot), ["amount"] = new IntValue(amount), ["times"] = new IntValue(times),
            });
            Assert.That(result.Succeeded, Is.True, result.ToString());
        }

        private static string Show(BridgeFx fx) =>
            fx.Error != null ? fx.Function + " failed" : fx.Function + ":" + fx.Look!.Name + string.Concat(fx.Look.Keys.Select(k => " " + k.Key + "=" + k.Value));

        [Test]
        public async Task TheFirstCallGivesOnlyTheStampAndLaterOnesTheNewEffectsInOrder()
        {
            Strike(5);
            var first = await _alice.WorldFx(0);
            Assert.That((first.Batches.Count, first.Missed), Is.EqualTo((0, false)), "effects from before the host asked are not news");

            Strike(5, times: 2);
            Strike(2);
            var next = await _alice.WorldFx(first.Stamp);
            Assert.That(next.Batches.Count, Is.EqualTo(2), "one batch per command");
            Assert.That(next.Batches[0].Effects.Select(Show), Is.EqualTo(new[]
            {
                "arena.number:number amount=2", "arena.flash:flash", "arena.number:number amount=2", "arena.flash:flash",
            }), "signal by signal, each signal's functions in load order");
            var signals = next.Batches[0].Effects.Select(e => e.Signal).ToList();
            Assert.That((signals[0] == signals[1], signals[1] == signals[2], signals[2] == signals[3]), Is.EqualTo((true, false, true)),
                "the two effects of one signal share its number (G430)");
            Assert.That(next.Batches[1].Effects.Select(Show), Is.EqualTo(new[] { "arena.number:number amount=5", "arena.flash:flash" }));
            Assert.That(next.Batches[1].Batch, Is.GreaterThan(next.Batches[0].Batch));
            Assert.That(next.Batches[0].Effects[0].Where, Is.EqualTo(BridgeWhere.At("hex", BridgeValue.Of(0))));

            var again = await _alice.WorldFx(next.Stamp);
            Assert.That((again.Batches.Count, again.Stamp), Is.EqualTo((0, next.Stamp)));
        }

        [Test]
        public async Task AFailingFunctionIsOneErrorAndTheOthersStillPlay()
        {
            var first = await _alice.WorldFx(0);
            Strike(0);
            var effects = (await _alice.WorldFx(first.Stamp)).Batches.Single().Effects;
            Assert.That(effects.Select(Show), Is.EqualTo(new[] { "arena.number failed", "arena.flash:flash" }));
            Assert.That(effects[0].Error, Does.Contain("arena.number"));
        }

        [Test]
        public async Task AHostThatFallsTooFarBehindMissesTheOldEffects()
        {
            var first = await _alice.WorldFx(0);
            Strike(5, times: 200);
            Strike(5, times: 100);
            var late = await _alice.WorldFx(first.Stamp);
            Assert.That(late.Missed, Is.True);
            Assert.That(late.Batches.Sum(b => b.Effects.Count), Is.EqualTo(256 * 2), "only what the kernel still holds");
            Assert.That((await _alice.WorldFx(late.Stamp)).Missed, Is.False);
        }

        [TestCase("@world_fx\ndef f(target):\n    return []\n", "@world_fx takes one signal")]
        [TestCase("@world_fx(Hit)\ndef f(who):\n    return []\n", "signal 'Hit' has no value 'who'")]
        [TestCase("@world_fx(Hit)\ndef f(target: Ref[Unit]):\n    return []\n", "a @world_fx parameter takes its type from the signal")]
        [TestCase("@world_fx(Hit)\ndef f(target):\n    target.Pos.x = 1\n    return []\n", "the @world_fx 'f' cannot run on a screen")]
        [TestCase("@command(free=True, acts_for=None)\ndef Go():\n    x = fx(\"a\", at=at(0))\n", "makes a look or a place")]
        public void AWrongFxFunctionIsALoadError(string defs, string message)
        {
            Directory.Delete(_root, true);
            var bag = new DiagnosticBag();
            Load("Unit(\"Foot\")\n" + defs, bag);
            Assert.That(bag.ToSortedList().Select(d => d.ToString()), Has.Some.Contain(message));
        }
    }
}
