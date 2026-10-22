using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// <c>text()</c>의 인자 (spec-gaps G267): 숫자·str·bool·None·엔티티·text만 받고, 엔티티는 부르는 때의 Def를 적어 둔다.
    /// 적은 Def는 엔티티가 없어져도, 저장·불러오기 뒤에도 남고, 못 보는 엔티티의 Def는 거른 읽기에서 빠진다.
    /// </summary>
    public class TextArgumentTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0))\n" +
            "component(\"Fog\", hidden=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n" +
            "deftype(\"Unit\", components=[\"Fog\"])\n";

        private const string Defs =
            "Party(\"Red\")\nUnit(\"Scout\")\n" +
            "@view_rule(Unit)\n" +
            "def fog(viewer, entity):\n" +
            "    return not entity.Fog.hidden\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Show(p: Ref[Party], u: Ref[Unit]):\n" +
            "    t = text(\"scout-seen\", unit=u, n=3, f=1.5, s=\"banking:Red\", b=True, z=None, inner=text(\"k\"))\n" +
            "    ask(p, t, choices=[\"a\"])\n" +
            "    p.Score.v = 1 if t == text(\"scout-seen\", unit=u, n=3, f=1.5, s=\"banking:Red\", b=True, z=None, inner=text(\"k\")) else 2\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Gone(p: Ref[Party]):\n" +
            "    x = spawn(\"Scout\")\n" +
            "    t = text(\"scout-lost\", unit=x)\n" +
            "    destroy(x)\n" +
            "    ask(p, t, choices=[\"a\"])\n" +
            "    p.Score.v = 1 if t == text(\"scout-lost\", unit=x) else 2\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Plain(p: Ref[Party]):\n" +
            "    ask(p, text(\"plain\", n=3), choices=[\"a\"])\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Bad(p: Ref[Party], n: int):\n" +
            "    if n == 0:\n" +
            "        t = text(\"k\", xs=[1, 2])\n" +
            "    elif n == 1:\n" +
            "        t = text(\"k\", d={\"a\": 1})\n" +
            "    else:\n" +
            "        t = text(\"k\", f=lambda: 1)\n";

        private World _w = null!;
        private EntityId _red, _scout;

        [SetUp]
        public void NewWorld()
        {
            Mod("banking", schema: Schema, defs: Defs);
            _w = new World(LoadClean());
            _red = _w.Create("banking:Red");
            _scout = _w.Create("banking:Scout");
            _w.Seat(_red, "red");
        }

        private SubmitResult Show() => _w.Submit("red", "banking.Show",
            new Dictionary<string, Value> { ["p"] = new EntityValue(_red), ["u"] = new EntityValue(_scout) });

        [Test]
        public void AnEntityArgumentRecordsItsDefAndOtherKindsRecordNothing()
        {
            var s = Show();
            Assert.That(s.Succeeded, Is.True, s.Error?.Message);
            var t = (TextValue)s.OpenedRequest!.Title;
            Assert.That(t.EntityDefs, Is.EqualTo(new Dictionary<string, string> { ["unit"] = "banking:Scout" }));
            Assert.That(t.Args.Keys, Is.EquivalentTo(new[] { "unit", "n", "f", "s", "b", "z", "inner" }));
        }

        [Test]
        public void TheRecordedDefOutlivesTheEntityAndSurvivesSaveAndLoad()
        {
            var s = _w.Submit("red", "banking.Gone", new Dictionary<string, Value> { ["p"] = new EntityValue(_red) });
            Assert.That(s.Succeeded, Is.True, s.Error?.Message);
            Assert.That(((TextValue)s.OpenedRequest!.Title).EntityDefs["unit"], Is.EqualTo("banking:Scout"));
            var saved = Encoding.UTF8.GetString(_w.Save());
            Assert.That(saved, Does.Contain("\"defs\":"));

            var loaded = World.Load(LoadClean(), Encoding.UTF8.GetBytes(saved));
            Assert.That(loaded.Diagnostics.Where(d => d.Severity == Severity.Error), Is.Empty);
            var w = loaded.World!;
            Assert.That(Encoding.UTF8.GetString(w.Save()), Is.EqualTo(saved), "C4");
            var q = w.OpenRequests.Single();
            Assert.That(((TextValue)q.Title).EntityDefs["unit"], Is.EqualTo("banking:Scout"));

            // 멈춘 흐름의 지역 변수 t도 저장되어 이어 간 본문의 비교가 맞는다 (Def는 같음에 들지 않는다)
            Assert.That(w.Answer("red", q.Id, _red, 0).Succeeded, Is.True);
            Assert.That(((IntValue)w.Get(_red, w.Member("Score", "v"))).Value, Is.EqualTo(1));
        }

        [Test]
        public void ATextWithoutEntitiesKeepsTheOldSaveShape()
        {
            var s = _w.Submit("red", "banking.Plain", new Dictionary<string, Value> { ["p"] = new EntityValue(_red) });
            Assert.That(s.Succeeded, Is.True, s.Error?.Message);
            var saved = Encoding.UTF8.GetString(_w.Save());
            Assert.That(saved, Does.Contain("\"plain\""));
            Assert.That(saved, Does.Not.Contain("\"defs\":"), "a mod file kind \"defs\" is a value, not this member");
        }

        [Test]
        public void AViewDropsTheDefOfAnUnseenEntity()
        {
            _w.Set(_scout, _w.Member("Fog", "hidden"), BoolValue.True);
            var t = (TextValue)_w.ViewAs("red").Show(Show()).OpenedRequest!.Title;
            Assert.That(t.Args["unit"], Is.SameAs(HiddenValue.Instance));
            Assert.That(t.EntityDefs, Is.Empty, "the def would tell what the hidden entity is");
        }

        [TestCase(0, "list")]
        [TestCase(1, "dict")]
        [TestCase(2, "function")]
        public void OtherArgumentsAreRuntimeErrors(int n, string kind)
        {
            var r = _w.Submit("red", "banking.Bad", new Dictionary<string, Value> { ["p"] = new EntityValue(_red), ["n"] = new IntValue(n) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo("RUN015"));
            Assert.That(r.Error.Message, Does.Contain("text() cannot show "));
            Assert.That(r.Error.Message, Does.Contain(", which is a " + kind));
            Assert.That(r.Error.Hint, Does.Contain("G267"));
        }
    }
}
