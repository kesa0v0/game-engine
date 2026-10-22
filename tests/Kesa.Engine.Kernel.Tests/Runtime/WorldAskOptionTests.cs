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
    /// 선택지에 보이는 모양을 싣는 <c>option(값, label=, desc=, style=, data=)</c> (spec-gaps G271): 맨 값도 감싸 질문의 선택지는 늘 option,
    /// 답은 안의 값, 저장·불러오기·거른 읽기가 칸 안의 값까지 다룬다.
    /// </summary>
    public class WorldAskOptionTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Score\", v=field(int, default=0))\n" +
            "component(\"Fog\", hidden=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Score\"])\n" +
            "deftype(\"Unit\", components=[\"Fog\"])\n";

        private const string Defs =
            "Party(\"Red\")\nParty(\"Blue\")\nUnit(\"Scout\")\n" +
            "@view_rule(Unit)\n" +
            "def fog(viewer, entity):\n" +
            "    return not entity.Fog.hidden\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Offer(p: Ref[Party], u: Ref[Unit]):\n" +
            "    opts = [option(\"accept\", label=\"cult-accept\", desc=text(\"cult-accept-desc\", unit=u), style=\"purple-fire\"," +
            " data={\"cost\": 3, \"unit\": u}), \"decline\", option(u)]\n" +
            "    r = ask(p, \"cult-offer\", choices=opts, deadline=5, on_timeout=\"decline\")\n" +
            "    p.Score.v = len(opts) * 10 + r.index\n" +
            "    if r.choice == \"accept\":\n" +
            "        p.Score.v += 100\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Bad(p: Ref[Party], n: int):\n" +
            "    if n == 0:\n" +
            "        o = option(\"a\", style=3)\n" +
            "    elif n == 1:\n" +
            "        o = option(option(\"a\"))\n" +
            "    elif n == 2:\n" +
            "        o = option(\"a\", labl=\"x\")\n" +
            "    elif n == 3:\n" +
            "        o = option(\"a\", label=5)\n" +
            "    else:\n" +
            "        o = \"a\"\n" +
            "        ask(p, \"q\", choices=[\"a\"], data={\"o\": option(\"a\")})\n" +
            "    ask(p, \"q\", choices=[o])\n";

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

        private Request Offer()
        {
            var r = _w.Submit("red", "banking.Offer",
                new Dictionary<string, Value> { ["p"] = new EntityValue(_red), ["u"] = new EntityValue(_scout) });
            Assert.That(r.Succeeded, Is.True, r.Error?.Message);
            return r.OpenedRequest!;
        }

        private long Score() => ((IntValue)_w.Get(_red, _w.Member("Score", "v"))).Value;

        [Test]
        public void EveryChoiceIsAnOptionAndOnlyTheWrittenFieldsAreSet()
        {
            var q = Offer();
            var accept = q.Choices[0];
            Assert.That(((StrValue)accept.Value).Value, Is.EqualTo("accept"));
            Assert.That(((StrValue)accept.Label!).Value, Is.EqualTo("cult-accept"));
            var desc = (TextValue)accept.Desc!;
            Assert.That((desc.Key, ((EntityValue)desc.Args["unit"]).Id), Is.EqualTo(("cult-accept-desc", _scout)));
            Assert.That(accept.Style, Is.EqualTo("purple-fire"));
            Assert.That(((EntityValue)((DictValue)accept.Data!).Items[1].Value).Id, Is.EqualTo(_scout)); // 저장 가능한 id로

            var decline = q.Choices[1]; // 맨 값은 감쌌다
            Assert.That((((StrValue)decline.Value).Value, decline.IsPlain), Is.EqualTo(("decline", true)));
            Assert.That(((EntityValue)q.Choices[2].Value).Id, Is.EqualTo(_scout));
            Assert.That(q.TimeoutChoice, Is.EqualTo(1), "on_timeout= names the value inside, not the option");
        }

        [Test]
        public void TheAnswerIsTheValueInsideTheOption()
        {
            var q = Offer();
            Assert.That(_w.Answer("red", q.Id, _red, 0).Succeeded, Is.True);
            Assert.That(Score(), Is.EqualTo(130)); // 선택지 3개, 0번, r.choice == "accept"
        }

        [Test]
        public void OptionsSurviveSaveAndLoadAndAPlainChoiceIsSavedAsItsValue()
        {
            var q = Offer();
            var saved = Encoding.UTF8.GetString(_w.Save());
            Assert.That(saved, Does.Contain("\"option\": \"accept\""));
            Assert.That(saved, Does.Contain("\"style\": \"purple-fire\""));
            Assert.That(saved, Does.Not.Contain("\"option\": \"decline\""), "a plain choice keeps the old save shape");

            var loaded = World.Load(LoadClean(), Encoding.UTF8.GetBytes(saved));
            Assert.That(loaded.Diagnostics.Where(d => d.Severity == Severity.Error), Is.Empty);
            var w = loaded.World!;
            Assert.That(Encoding.UTF8.GetString(w.Save()), Is.EqualTo(saved), "C4");
            var back = w.OpenRequests.Single();
            Assert.That(back.Choices.Select(c => c.Style), Is.EqualTo(new[] { "purple-fire", null, null }));
            Assert.That(PureOps.ValuesEqual(back.Choices[0], q.Choices[0]), Is.True);

            // 멈춘 흐름의 지역 변수 opts에 든 option도 저장되어 이어 간 본문이 그대로 읽는다
            Assert.That(w.Answer("red", back.Id, _red, 2).Succeeded, Is.True);
            Assert.That(((IntValue)w.Get(_red, w.Member("Score", "v"))).Value, Is.EqualTo(32));
        }

        [Test]
        public void AViewHidesUnseenEntitiesInsideOptionsButKeepsTheirPlaces()
        {
            _w.Set(_scout, _w.Member("Fog", "hidden"), BoolValue.True);
            var q = _w.ViewAs("red").Show(_w.Submit("red", "banking.Offer",
                new Dictionary<string, Value> { ["p"] = new EntityValue(_red), ["u"] = new EntityValue(_scout) })).OpenedRequest!;
            Assert.That(((TextValue)q.Choices[0].Desc!).Args["unit"], Is.SameAs(HiddenValue.Instance));
            Assert.That(((DictValue)q.Choices[0].Data!).Items[1].Value, Is.SameAs(HiddenValue.Instance));
            Assert.That(q.Choices[0].Style, Is.EqualTo("purple-fire"));
            Assert.That(q.Choices[2].Value, Is.SameAs(HiddenValue.Instance));
            Assert.That(q.Choices.Count, Is.EqualTo(3));
        }

        [TestCase(0, "RUN015", "style= of option() must be a str, not int")]
        [TestCase(1, "RUN015", "option() takes the value to choose, not another option")]
        [TestCase(2, "RUN015", "option() has no argument 'labl'")]
        [TestCase(3, "RUN015", "label= of option() must be a text key (str) or text(...), not int")]
        [TestCase(4, "RUN005", "data= of ask() cannot hold option")]
        public void WrongOptionsAreRuntimeErrors(int n, string code, string message)
        {
            var r = _w.Submit("red", "banking.Bad", new Dictionary<string, Value> { ["p"] = new EntityValue(_red), ["n"] = new IntValue(n) });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(r.Error!.Code, Is.EqualTo(code));
            Assert.That(r.Error.Message, Does.StartWith("computing the command banking.Bad: " + message + " (at"));
            if (n == 2) Assert.That(r.Error.Hint, Is.EqualTo("did you mean 'label'?"));
        }
    }
}
