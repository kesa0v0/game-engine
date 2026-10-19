using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>스크립트가 Def 타입 바로 아래의 const를 타입 이름으로 읽는다 (spec-gaps G107).</summary>
    public class WorldDefConstTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Progress\", value=field(int, default=0), target=const(int, default=10),\n" +
            "    upkeep=derived(int, lambda p: p.entity.Unit.cost // 10),\n" +
            "    done=derived(bool, lambda p: p.Unit.ok(p)),\n" +
            "    other=derived(int, lambda p: 1 if p.Banner else 0),\n" +
            // 리스트로 감싸 타입을 모르게 한다: 실행 중 안전망을 본다 (아니면 G228의 로드 오류)
            "    typo=derived(int, lambda p: [p.entity.Unit][0].cosst),\n" +
            "    fx=derived(fixed, lambda p: p.entity.Unit.rate))\n" +
            "deftype(\"Unit\", cost=const(int), rate=const(fixed, default=2), ok=const(Cond[\"Unit\"]),\n" +
            "    components=[\"Progress\"])\n" +
            "deftype(\"Banner\", components=[\"Progress\"])\n";

        private const string Defs =
            "Unit(\"Scout\", cost=30, ok=lambda u: u.Progress.value >= u.Progress.target)\n" +
            "Banner(\"Flag\")\n";

        private World _world = null!;

        private EntityId NewScout(string def = "Scout")
        {
            Mod("banking", schema: Schema, defs: Defs);
            _world = new World(LoadClean());
            return _world.Create(def);
        }

        private MemberHandle M(string owner, string member) => _world.Member(owner, member);

        [Test]
        public void AFunctionReadsTheDefTypeConstByTypeName()
        {
            var scout = NewScout();
            Assert.That(((IntValue)_world.Get(scout, M("Progress", "upkeep"))).Value, Is.EqualTo(3));
            Assert.That(((FixedValue)_world.Get(scout, M("Progress", "fx"))).Value, Is.EqualTo(Kesa.Engine.Numerics.Fixed.FromRaw(2_000_000)));
        }

        [Test]
        public void AFunctionValuedConstCanBeCalledFromAFunction()
        {
            var scout = NewScout();
            Assert.That(((BoolValue)_world.Get(scout, M("Progress", "done"))).Value, Is.False);
            _world.Set(scout, M("Progress", "value"), new IntValue(10));
            Assert.That(((BoolValue)_world.Get(scout, M("Progress", "done"))).Value, Is.True);
        }

        [Test]
        public void AnotherDefTypeNameReadsAsNone()
        {
            var scout = NewScout();
            Assert.That(((IntValue)_world.Get(scout, M("Progress", "other"))).Value, Is.EqualTo(0));
        }

        [Test]
        public void AMisspelledConstIsAnErrorWithASuggestion()
        {
            var scout = NewScout();
            var e = Assert.Throws<WorldException>(() => _world.Get(scout, M("Progress", "typo")))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunScript));
            Assert.That(e.Hint, Does.Contain("cost"));
        }

        [Test]
        public void ExplainRecordsTheDefConstRead()
        {
            var scout = NewScout();
            var node = _world.Explain(scout, M("Progress", "upkeep"));
            Assert.That(node.Reads, Has.Count.EqualTo(1));
            Assert.That(node.Reads[0].Component, Is.EqualTo("Unit"));
            Assert.That(node.Reads[0].Member, Is.EqualTo("cost"));
        }
    }
}
