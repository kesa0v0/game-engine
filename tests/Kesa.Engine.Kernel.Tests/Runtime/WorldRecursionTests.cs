using System.Collections.Generic;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>재귀가 커맨드·derived에서 돌고, 모드가 정한 깊이 한도가 월드까지 간다 (G452).</summary>
    public class WorldRecursionTests : LoaderTestBase
    {
        // derived 함수는 스키마 파일이 먼저 정해야 한다
        private const string Schema =
            "def depth_of(n) -> int:\n    if n == 0:\n        return 0\n    return 1 + depth_of(n - 1)\n" +
            "def shown(m) -> int:\n    return depth_of(m.n)\n" +
            "component(\"Mark\", n=field(int, default=0), shown=derived(int, shown))\n" +
            "deftype(\"Ship\", components=[\"Mark\"])\n";

        // 쓰기가 있는 상호 재귀: 로드 때 효과 분석이 고리를 돌다 멈추지 않아야 한다
        private const string Defs =
            "def down(s, n):\n    if n > 0:\n        s.Mark.n += 1\n        up(s, n - 1)\n" +
            "def up(s, n):\n    if n > 0:\n        s.Mark.n += 1\n        down(s, n - 1)\n" +
            "@command(free=True, acts_for=None)\ndef Climb(s: Ref[Ship], n: int):\n    down(s, n)\n" +
            "Ship(\"Sloop\")\n";

        private World _world = null!;
        private EntityId _sloop;

        private void Start(int depth)
        {
            Mod("banking", schema: Schema, defs: Rule("max_call_depth", depth) + Defs);
            _world = new World(LoadClean());
            _sloop = _world.Create("banking:Sloop");
        }

        private SubmitResult Climb(int n) =>
            _world.Submit("host", "banking.Climb", new Dictionary<string, Value> { ["s"] = new EntityValue(_sloop), ["n"] = new IntValue(n) });

        private long Get(string member) => ((IntValue)_world.Get(_sloop, _world.Member("Mark", member))).Value;

        [Test]
        public void ACommandAndADerivedRecurse()
        {
            Start(30);
            Assert.That(Climb(20).Succeeded, Is.True);
            Assert.That(Get("n"), Is.EqualTo(20));
            Assert.That(Get("shown"), Is.EqualTo(20));
        }

        [Test]
        public void TheModsLimitStopsACommand()
        {
            Start(30);
            var r = Climb(40);
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(Codes.RunDepth));
            Assert.That(Get("n"), Is.EqualTo(0), "a failed command changes nothing");
        }

        [Test]
        public void TheModsLimitStopsADerived()
        {
            Start(30);
            _world.Set(_sloop, _world.Member("Mark", "n"), new IntValue(40));
            var e = Assert.Throws<WorldException>(() => Get("shown"))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunDepth));
        }
    }
}
