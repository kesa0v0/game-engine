using System.Collections.Generic;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>바뀜 도장 (spec S10 4항, spec-gaps G235): 쓰기마다 (엔티티, 칸 이름)에 전역 시계를 적고, 실패한 커맨드는 도장도 되감는다.</summary>
    public class WorldStampTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Out\", n=field(int, default=0))\n" +
            "component(\"Home\", city=ref(\"City\", optional=True, inverse=\"units\"))\n" +
            "component(\"Pop\", size=field(int, default=0), bonus=derived(int, lambda p: p.size + len(contribs(p, \"bonus\"))))\n" +
            "component(\"Tag\", x=field(int, default=0))\n" +
            "component(\"Opinion\", key=Ref[\"City\"], score=field(int, default=0))\n" +
            "deftype(\"City\", components=[\"Pop\"])\n" +
            "deftype(\"Unit\", components=[\"Out\", \"Home\", \"Opinion\"])\n";

        private const string Defs =
            "City(\"Town\")\nUnit(\"Scout\")\n" +
            "@command(free=True, acts_for=None)\ndef Bump(u: Ref[Unit]):\n    u.Out.n = u.Out.n + 1\n" +
            // 쓰고, 만들고, 붙인 뒤 실패한다: 모두 되감긴다
            "@command(free=True, acts_for=None)\ndef Boom(u: Ref[Unit], c: Ref[City]):\n" +
            "    u.Out.n = 9\n    u.Home.city = c\n    spawn(\"Town\")\n    attach(u, Tag(x=1))\n    u.Out.n = 10\n    destroy(c)\n" +
            "    u.Tag.x = [1][5]\n";

        private World _w = null!;
        private EntityId _u1, _u2, _city;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _w = new World(LoadClean());
            _u1 = _w.Create("Scout");
            _u2 = _w.Create("Scout");
            _city = _w.Create("Town");
        }

        private MemberHandle M(string owner, string member) => _w.Member(owner, member);

        private static Dictionary<string, Value> Args(params (string, EntityId)[] pairs)
        {
            var args = new Dictionary<string, Value>();
            foreach (var (name, id) in pairs) args[name] = new EntityValue(id);
            return args;
        }

        [Test]
        public void AWriteStampsOnlyItsEntityAndComponent()
        {
            long clock = _w.Version();
            long out1 = _w.Version(_u1, "Out"), home1 = _w.Version(_u1, "Home"), u2 = _w.Version(_u2);
            Assert.That(out1, Is.GreaterThan(0));
            _w.Set(_u1, M("Out", "n"), new IntValue(3));
            Assert.That(_w.Version(), Is.GreaterThan(clock));
            Assert.That(_w.Version(_u1, "Out"), Is.EqualTo(_w.Version()));
            Assert.That(_w.Version(_u1), Is.EqualTo(_w.Version()));
            Assert.That(_w.Version(_u1, "Home"), Is.EqualTo(home1));
            Assert.That(_w.Version(_u2), Is.EqualTo(u2));
        }

        [Test]
        public void ARefWriteStampsTheTargetsInverseAndContributionsStampTheirComponent()
        {
            long pop = _w.Version(_city, "Pop"), units = _w.Version(_city, "units");
            _w.Set(_u1, M("Home", "city"), new EntityValue(_city));
            Assert.That(_w.Version(_city, "units"), Is.GreaterThan(units));
            Assert.That(_w.Version(_city, "Pop"), Is.EqualTo(pop));

            units = _w.Version(_city, "units");
            _w.AddContribution(_city, M("Pop", "bonus"), "add", new IntValue(1));
            Assert.That(_w.Version(_city, "Pop"), Is.GreaterThan(pop));
            Assert.That(_w.Version(_city, "units"), Is.EqualTo(units));
        }

        [Test]
        public void AttachingRowsAndDestroyingStamp()
        {
            long tag = _w.Version(_u1, "Tag");
            _w.Add(_u1, "Tag");
            Assert.That(_w.Version(_u1, "Tag"), Is.GreaterThan(tag));

            long opinion = _w.Version(_u1, "Opinion");
            _w.AddRow(_u1, "Opinion", _city);
            Assert.That(_w.Version(_u1, "Opinion"), Is.GreaterThan(opinion));

            // 줄의 키 엔티티가 죽으면 그 줄이 지워진다: 줄을 가진 엔티티의 칸도 바뀐 것이다
            opinion = _w.Version(_u1, "Opinion");
            long clock = _w.Version();
            _w.Destroy(_city);
            Assert.That(_w.Version(), Is.GreaterThan(clock));
            Assert.That(_w.Version(_u1, "Opinion"), Is.GreaterThan(opinion));
            Assert.That(_w.Version(_city), Is.EqualTo(0));
            Assert.That(_w.Version(_city, "Pop"), Is.EqualTo(0));
            Assert.That(_w.Version(new EntityId(999)), Is.EqualTo(0));
        }

        [Test]
        public void ACommandStampsOnSuccessAndRewindsItsStampsOnFailure()
        {
            Assert.That(_w.Submit("x", "banking.Bump", Args(("u", _u1))).Succeeded, Is.True);
            long clock = _w.Version();
            Assert.That(_w.Version(_u1, "Out"), Is.EqualTo(clock));

            long u1 = _w.Version(_u1), out1 = _w.Version(_u1, "Out"), home = _w.Version(_u1, "Home"),
                tag = _w.Version(_u1, "Tag"), city = _w.Version(_city), units = _w.Version(_city, "units");
            var failed = _w.Submit("x", "banking.Boom", Args(("u", _u1), ("c", _city)));
            Assert.That(failed.Error!.Message, Does.Contain("index"), "it fails on the last line, after every write");
            Assert.That(_w.Version(), Is.EqualTo(clock));
            Assert.That((_w.Version(_u1), _w.Version(_u1, "Out"), _w.Version(_u1, "Home"), _w.Version(_u1, "Tag")),
                Is.EqualTo((u1, out1, home, tag)));
            Assert.That((_w.Version(_city), _w.Version(_city, "units")), Is.EqualTo((city, units)));

            // 되감긴 뒤의 다음 쓰기는 되감긴 시계 위에서 이어 간다
            Assert.That(_w.Submit("x", "banking.Bump", Args(("u", _u2))).Succeeded, Is.True);
            Assert.That(_w.Version(_u2, "Out"), Is.EqualTo(clock + 1));
            Assert.That(_w.Version(_u1), Is.EqualTo(u1));
        }

        [Test]
        public void ANameThatIsNeitherAComponentNorAnInverseIsAnError()
        {
            var e = Assert.Throws<WorldException>(() => _w.Version(_u1, "Nope"))!;
            Assert.That(e.Message, Does.Contain("'Nope' is neither a component nor an inverse of Unit"));
            Assert.Throws<WorldException>(() => _w.Version(_u1, "units"));
        }

        [Test]
        public void ALoadedWorldStampsEveryEntityAsNew()
        {
            _w.Set(_u1, M("Out", "n"), new IntValue(3));
            var loaded = World.Load(LoadClean(), _w.Save());
            Assert.That(loaded.Succeeded, Is.True);
            var w = loaded.World!;
            Assert.That(w.Version(_u1), Is.GreaterThan(0));
            Assert.That(w.Version(_u1, "Out"), Is.EqualTo(w.Version(_u1)));
            Assert.That(w.Version(), Is.GreaterThanOrEqualTo(w.Version(_city)));
        }
    }
}
