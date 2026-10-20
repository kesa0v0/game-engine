using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Host;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>호스트 함수와 저장 (spec S7·S8, spec-gaps G221).</summary>
    public class HostSaveTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Terrain\", cost=field(int, default=1))\n" +
            "deftype(\"Tile\", components=[\"Terrain\"])\n";

        private const string Defs =
            "Tile(\"Grass\")\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Later(at: Ref[Tile]):\n" +
            "    f = pick()\n" +
            "    wait(ticks=2)\n" +
            "    at.Terrain.cost = f(at.Terrain.cost + 4)\n";

        /// <summary>
        /// 함수 값을 골라 주는 곳. 나중 판에서 모드가 더는 그 호스트 함수를 쓰지 않게 바꿔도 멈춘 Flow의 프레임(Later)은 그대로다.
        /// </summary>
        private const string Pick = "def pick():\n    return core.math.double\n";

        private const string PickNothing = "def pick():\n    return 0\n";

        private readonly List<long> _doubled = new List<long>();

        private HostFunctions Host(bool withDouble = true, string doubleSignature = "def math.double(n: int) -> int",
            bool withHalf = false)
        {
            var host = new HostFunctions();
            var core = host.For("core");
            if (withDouble)
            {
                if (doubleSignature.Contains("m: int"))
                    core.Pure(doubleSignature, "", (long n, long m) => n * 2);
                else
                    core.Pure(doubleSignature, "", (long n) =>
                    {
                        _doubled.Add(n);
                        return n * 2;
                    });
            }
            if (withHalf) core.Pure("def math.half(n: int) -> int", "", (long n) => n / 2);
            return host;
        }

        private ContentLoadResult Content(HostFunctions host)
        {
            var bag = new DiagnosticBag();
            var order = ModSorter.Sort(ModDiscovery.Discover(_root, bag), bag);
            var loaded = ContentLoader.Load(_root, order, bag, host);
            Assert.That(bag.ToSortedList().Where(d => d.Severity == Severity.Error), Is.Empty,
                string.Join("\n", bag.ToSortedList()));
            return loaded;
        }

        /// <summary>Later를 내고 기다리는 중에 저장한다.</summary>
        private byte[] PausedSave(out EntityId tile)
        {
            Mod("core", schema: Schema, defs: Defs + Pick);
            var world = new World(Content(Host()));
            tile = world.Create("core:Grass");
            var r = world.Submit("host", "core.Later", new Dictionary<string, Value> { ["at"] = new EntityValue(tile) });
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            return world.Save();
        }

        [Test]
        public void AHostFunctionValueInAPausedFlowIsSavedByName()
        {
            var save = PausedSave(out var tile);
            string text = Compact(save);
            Assert.That(text, Does.Contain("\"f\": {\"host\": \"core.math.double\"}"));
            Assert.That(text, Does.Not.Contain("Kesa"));

            var loaded = World.Load(Content(Host()), save);
            Assert.That(loaded.Diagnostics, Is.Empty, string.Join("\n", loaded.Diagnostics));
            var world = loaded.World!;
            world.Advance(2);
            Assert.That(_doubled, Is.EqualTo(new[] { 5L }));
            Assert.That(((IntValue)world.Get(tile, world.Member("Terrain", "cost"))).Value, Is.EqualTo(10));
        }

        [Test]
        public void TheSaveListsTheHostFunctions()
        {
            var save = PausedSave(out _);
            Assert.That(Compact(save), Does.Contain(
                "\"host\": [{\"name\": \"core.math.double\",\"signature\": \"(n: int) -> int\",\"effect\": \"pure\"}]"));
        }

        /// <summary>들여쓴 저장 JSON을 줄바꿈과 들여쓰기 없이.</summary>
        private static string Compact(byte[] save) =>
            System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(save), "\r?\n *", "");

        [Test]
        public void AWorldWithoutHostFunctionsSavesNoHostList()
        {
            Mod("core", schema: Schema, defs: "Tile(\"Grass\")\n");
            var world = new World(Content(new HostFunctions()));
            Assert.That(Encoding.UTF8.GetString(world.Save()), Does.Not.Contain("\"host\""));
        }

        [Test]
        public void AMissingHostFunctionInAPausedFlowStopsTheLoad()
        {
            var save = PausedSave(out _);
            Mod("core", schema: Schema, defs: Defs + PickNothing);
            var loaded = World.Load(Content(Host(withDouble: false)), save);
            Assert.That(loaded.World, Is.Null);
            var error = loaded.Diagnostics.Single(d => d.Code == Codes.SaveMismatch);
            Assert.That(error.Message, Does.Contain("the host function 'core.math.double' is not registered"));
            var warning = loaded.Diagnostics.Single(d => d.Code == Codes.SaveModsChanged && d.Message.StartsWith("the host"));
            Assert.That(warning.Message, Is.EqualTo("the host functions differ from the save: removed core.math.double"));
        }

        [Test]
        public void TheFlowCanBeCanceledInstead()
        {
            var save = PausedSave(out _);
            Mod("core", schema: Schema, defs: Defs + PickNothing);
            var loaded = World.Load(Content(Host(withDouble: false)), save, cancelBrokenFlows: true);
            Assert.That(loaded.World, Is.Not.Null, string.Join("\n", loaded.Diagnostics));
            var canceled = loaded.Diagnostics.Single(d => d.Code == Codes.SaveFlowCanceled);
            Assert.That(canceled.Message, Does.Contain("the host function 'core.math.double' is not registered"));
        }

        [Test]
        public void AChangedSignatureOrANewFunctionIsReported()
        {
            var save = PausedSave(out _);
            var loaded = World.Load(Content(Host(doubleSignature: "def math.double(n: int, m: int = 1) -> int", withHalf: true)), save);
            Assert.That(loaded.World, Is.Not.Null, string.Join("\n", loaded.Diagnostics));
            var warning = loaded.Diagnostics.Single(d => d.Code == Codes.SaveModsChanged && d.Message.StartsWith("the host"));
            Assert.That(warning.Message, Is.EqualTo(
                "the host functions differ from the save: changed core.math.double (n: int) -> int -> (n: int, m: int = 1) -> int, " +
                "added core.math.half"));
        }

        [Test]
        public void TheSameSignatureWrittenDifferentlyIsNotAChange()
        {
            var save = PausedSave(out _);
            var loaded = World.Load(Content(Host(doubleSignature: "def   math.double( n :int )->int")), save);
            Assert.That(loaded.Diagnostics, Is.Empty, string.Join("\n", loaded.Diagnostics));
        }
    }
}
