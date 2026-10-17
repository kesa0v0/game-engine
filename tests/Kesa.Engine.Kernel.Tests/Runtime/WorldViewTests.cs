using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 거른 읽기 <c>ViewAs(issuer)</c>와 보기 정책 <c>@view_policy</c> (spec-gaps G196~G203). 진영마다 연결 꼬리표가 있고
    /// 남의 <c>Hand</c>와 안개 속(<c>Fog.hidden</c>) 유닛은 못 본다.
    /// </summary>
    public class WorldViewTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Seat\", conn=field(str, default=\"\"))\n" +
            "component(\"Hand\", cards=field(int, default=0))\n" +
            "component(\"Owner\", party=ref(\"Party\"))\n" +
            "component(\"Fog\", hidden=field(bool, default=False))\n" +
            "component(\"Battle\", attacker=ref(\"Unit\"))\n" +
            "component(\"Spy\", on=ref(\"Unit\", optional=True))\n" +
            "component(\"Known\", key=Ref[\"Unit\"], note=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Seat\", \"Hand\", \"Known\"])\n" +
            "deftype(\"Unit\", components=[\"Owner\", \"Fog\", \"Spy\"])\n" +
            "deftype(\"Field\", components=[\"Battle\"])\n";

        private const string Policies =
            "def mine(issuer, party):\n" +
            "    return party.Seat.conn == issuer\n" +
            "@submit_policy\n" +
            "def may_act(issuer, target):\n" +
            "    return target is None or mine(issuer, target)\n" +
            "@view_policy\n" +
            "def may_see(issuer, entity, component):\n" +
            "    if entity.Seat is not None:\n" +
            "        return component != \"Hand\" or mine(issuer, entity)\n" +
            "    if entity.Fog is not None:\n" +
            "        return not entity.Fog.hidden or mine(issuer, entity.Owner.party)\n" +
            "    return True\n";

        private const string Defs =
            "Party(\"Side\")\nUnit(\"Scout\")\nField(\"Plain\")\n" + Policies +
            "@command(acts_for=lambda party: party)\n" +
            "def Pick(party: Ref[Party]):\n" +
            "    pass\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Target(party: Ref[Party], unit: Ref[Unit]):\n" +
            "    r = ask(party, text(\"q.target\", who=unit), choices=[unit, \"skip\"], data={\"u\": unit})\n" +
            "    party.Hand.cards = r.index\n";

        private World _w = null!;
        private EntityId _a, _b, _scout, _field;

        private void NewWorld(string defs = Defs, bool requireView = false)
        {
            Mod("banking", schema: Schema, defs: defs);
            _w = new World(LoadClean(), requireViewPolicy: requireView);
            _a = Party("A", 3);
            _b = Party("B", 5);
            _scout = _w.Create("Scout", new Init(M("Owner", "party"), new EntityValue(_a)));
            _w.Set(_scout, M("Owner", "party"), new EntityValue(_a));
            _w.Set(_scout, M("Fog", "hidden"), BoolValue.True);
            _field = _w.Create("Plain", new Init(M("Battle", "attacker"), new EntityValue(_scout)));
            _w.Set(_field, M("Battle", "attacker"), new EntityValue(_scout));
        }

        private EntityId Party(string conn, int cards)
        {
            var p = _w.Create("Side");
            _w.Set(p, M("Seat", "conn"), new StrValue(conn));
            _w.Set(p, M("Hand", "cards"), new IntValue(cards));
            return p;
        }

        private MemberHandle M(string owner, string member) => _w.Member(owner, member);

        [Test]
        public void MyOwnHandIsVisibleAndTheOtherHandIsAsIfNotAttached()
        {
            NewWorld();
            var b = _w.ViewAs("B");
            Assert.That(((IntValue)b.Get(_b, M("Hand", "cards"))).Value, Is.EqualTo(5));
            Assert.That(b.Has(_a, "Hand"), Is.False);
            Assert.That(b.Has(_a, "Seat"), Is.True);
            var e = Assert.Throws<WorldException>(() => b.Get(_a, M("Hand", "cards")))!;
            Assert.That(e.Code, Is.EqualTo(Codes.RunNoComponent));
            Assert.That(e.Message, Is.EqualTo(_a + " (Side) has no 'Hand'"));
            Assert.That(b.Query("Hand"), Is.EqualTo(new[] { _b }), "attachment does not leak through a query");
            Assert.That(b.Query("Party"), Is.EqualTo(new[] { _a, _b }));
            Assert.That(b.Diagnostics, Is.Empty);
        }

        [Test]
        public void AHiddenEntityAnswersExactlyLikeADestroyedOne()
        {
            NewWorld();
            var dead = _w.Create("Scout", new Init(M("Owner", "party"), new EntityValue(_a)));
            _w.Set(dead, M("Owner", "party"), new EntityValue(_a));
            _w.Destroy(dead);
            var b = _w.ViewAs("B");
            Assert.That(b.IsAlive(_scout), Is.False);
            Assert.That(b.All("Unit"), Is.Empty);
            var hidden = Assert.Throws<WorldException>(() => b.Get(_scout, M("Fog", "hidden")))!;
            var gone = Assert.Throws<WorldException>(() => _w.Get(dead, M("Fog", "hidden")))!;
            Assert.That(hidden.Code, Is.EqualTo(gone.Code));
            Assert.That(hidden.Message, Is.EqualTo(gone.Message.Replace(dead.ToString(), _scout.ToString())));
            Assert.That(Assert.Throws<WorldException>(() => b.DefOf(_scout))!.Code, Is.EqualTo(Codes.RunDeadEntity));

            var a = _w.ViewAs("A");
            Assert.That(a.All("Unit"), Is.EqualTo(new[] { _scout }), "the owner sees its own scout");
            Assert.That(a.DefOf(_scout), Is.EqualTo("Scout"));
        }

        [Test]
        public void ARefToAHiddenEntityIsHiddenWithoutAnId()
        {
            NewWorld();
            Assert.That(_w.ViewAs("B").Get(_field, M("Battle", "attacker")), Is.SameAs(HiddenValue.Instance));
            Assert.That(((EntityValue)_w.ViewAs("A").Get(_field, M("Battle", "attacker"))).Id, Is.EqualTo(_scout));
        }

        [Test]
        public void InverseAndEntityKeyedRowsDropWhatIsHidden()
        {
            NewWorld();
            var b = _w.ViewAs("B");
            Assert.That(_w.Inverse(_a, M("Owner", "party")), Is.EqualTo(new[] { _scout }));
            Assert.That(b.Inverse(_a, M("Owner", "party")), Is.Empty, "the scout owned by A is in the fog");

            _w.AddRow(_b, "Known", _scout);
            Assert.That(_w.RowEntities(_b, "Known"), Is.EqualTo(new[] { _scout }));
            Assert.That(b.RowEntities(_b, "Known"), Is.Empty);
            // 못 보는 키는 죽은 키처럼 묻는 것 자체가 오류다
            Assert.That(Assert.Throws<WorldException>(() => b.HasRow(_b, "Known", _scout))!.Code,
                Is.EqualTo(Codes.RunDeadEntity));
            Assert.That(Assert.Throws<WorldException>(() => b.GetRow(_b, M("Known", "note"), _scout))!.Code,
                Is.EqualTo(Codes.RunDeadEntity));

            _w.Set(_scout, M("Fog", "hidden"), BoolValue.False);
            Assert.That(b.RowEntities(_b, "Known"), Is.EqualTo(new[] { _scout }), "out of the fog it is seen again");
            Assert.That(b.Inverse(_a, M("Owner", "party")), Is.EqualTo(new[] { _scout }));
        }

        /// <summary>못 보는 키 컴포넌트의 줄과 못 보는 엔티티 키는 붙지 않은 컴포넌트·죽은 키와 똑같이 응답한다 (G198).</summary>
        [Test]
        public void HiddenRowsAnswerLikeUnattachedComponentsAndDeadKeys()
        {
            Mod("banking", schema:
                    "component(\"Seat\", conn=field(str, default=\"\"))\n" +
                    "component(\"Tricks\", key=\"Card\", keys=\"listed\", n=field(int, default=0))\n" +
                    "component(\"Moods\", key=\"Card\", keys=\"all\", n=field(int, default=0))\n" +
                    "component(\"Known\", key=Ref[\"Party\"], n=field(int, default=0))\n" +
                    "deftype(\"Card\")\ndeftype(\"Party\", components=[\"Seat\", \"Known\"])\n",
                defs: "Card(\"Ace\")\nParty(\"Side\")\n" +
                      "@view_policy\ndef may_see(issuer, entity, component):\n" +
                      "    if component == \"Tricks\" or component == \"Moods\":\n" +
                      "        return entity.Seat.conn == issuer\n" +
                      "    return entity.Seat.conn != \"ghost\"\n");
            var w = new World(LoadClean());
            EntityId P(string conn)
            {
                var p = w.Create("Side");
                w.Set(p, w.Member("Seat", "conn"), new StrValue(conn));
                return p;
            }
            var a = P("A");
            var b = P("B");
            w.Add(a, "Tricks");
            w.AddRow(a, "Tricks", "Ace");
            w.Add(a, "Moods");
            var ghost = P("ghost");
            var dead = P("x");
            w.AddRow(b, "Known", ghost);
            w.AddRow(b, "Known", dead);
            w.Destroy(dead);

            var view = w.ViewAs("B");
            // a의 숨긴 Tricks·Moods를 B가 읽기 ↔ 그 컴포넌트가 정말 없는 b를 월드가 읽기
            void Same(System.Func<object> hidden, System.Func<object> real, EntityId hiddenId, EntityId realId)
            {
                var h = Assert.Throws<WorldException>(() => hidden())!;
                var r = Assert.Throws<WorldException>(() => real())!;
                Assert.That(h.Code, Is.EqualTo(r.Code));
                Assert.That(h.Message.Replace(hiddenId.ToString(), "#"), Is.EqualTo(r.Message.Replace(realId.ToString(), "#")));
            }
            Same(() => view.GetRow(a, w.Member("Tricks", "n"), "Ace"), () => w.GetRow(b, w.Member("Tricks", "n"), "Ace"), a, b);
            Same(() => view.GetRow(a, w.Member("Moods", "n"), "Ace"), () => w.GetRow(b, w.Member("Moods", "n"), "Ace"), a, b);
            Same(() => view.RowKeys(a, "Tricks"), () => w.RowKeys(b, "Tricks"), a, b);
            Same(() => view.RowKeys(a, "Moods"), () => w.RowKeys(b, "Moods"), a, b);
            Assert.That(view.HasRow(a, "Tricks", "Ace"), Is.EqualTo(w.HasRow(b, "Tricks", "Ace")));
            Assert.That(view.HasRow(a, "Moods", "Ace"), Is.EqualTo(w.HasRow(b, "Moods", "Ace")));

            // 못 보는 ghost가 키인 줄 ↔ 죽은 dead가 키인 줄
            Same(() => view.GetRow(b, w.Member("Known", "n"), ghost), () => w.GetRow(b, w.Member("Known", "n"), dead), ghost, dead);
            Same(() => view.HasRow(b, "Known", ghost), () => w.HasRow(b, "Known", dead), ghost, dead);
            Assert.That(view.RowEntities(b, "Known"), Is.Empty);

            // 자기 것은 그대로 보인다
            var own = w.ViewAs("A");
            Assert.That(((IntValue)own.GetRow(a, w.Member("Moods", "n"), "Ace")).Value, Is.EqualTo(0));
            Assert.That(own.RowKeys(a, "Tricks"), Is.EqualTo(new[] { "Ace" }));
        }

        [Test]
        public void OnlyTheQuestionsThisConnectionMayAnswerAreShownAndHiddenChoicesKeepTheirPlace()
        {
            NewWorld();
            var r = _w.Submit("A", "banking.Target", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_b), ["unit"] = new EntityValue(_scout),
            });
            Assert.That(r.Outcome, Is.EqualTo(SubmitOutcome.GateDenied), "A may not act for B");

            r = _w.Submit("B", "banking.Target", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_b), ["unit"] = new EntityValue(_scout),
            });
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            Assert.That(_w.ViewAs("A").OpenRequests, Is.Empty, "the question to B is not shown to A");

            var q = _w.ViewAs("B").OpenRequests.Single();
            Assert.That(q.Id, Is.EqualTo(r.OpenedRequest!.Id));
            Assert.That(q.Choices[0], Is.SameAs(HiddenValue.Instance), "the scout in the fog is a face-down choice");
            Assert.That(((StrValue)q.Choices[1]).Value, Is.EqualTo("skip"));
            Assert.That(((TextValue)q.Title).Args["who"], Is.SameAs(HiddenValue.Instance));
            Assert.That(((DictValue)q.Data).Items[0].Value, Is.SameAs(HiddenValue.Instance));
            Assert.That(_w.Answer("B", q.Id, _b, 0).Succeeded, Is.True, "answering by number works with a hidden choice");
            Assert.That(((IntValue)_w.Get(_b, M("Hand", "cards"))).Value, Is.EqualTo(0));
        }

        [Test]
        public void AHiddenValueCannotBeSubmitted()
        {
            NewWorld();
            var r = _w.Submit("B", "banking.Target", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_b), ["unit"] = HiddenValue.Instance,
            });
            Assert.That(r.Succeeded, Is.False);
            Assert.That(r.ArgumentRejections.Single().Parameter, Is.EqualTo("unit"));
        }

        [Test]
        public void ViewingChangesNothing()
        {
            NewWorld();
            _w.Submit("B", "banking.Target", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_b), ["unit"] = new EntityValue(_scout),
            });
            var before = _w.Save();
            foreach (var issuer in new[] { "A", "B", "nobody" })
            {
                var v = _w.ViewAs(issuer);
                v.All("Party");
                v.All("Unit");
                v.Query("Hand");
                v.Inverse(_a, M("Owner", "party"));
                _ = v.OpenRequests;
                v.Get(_field, M("Battle", "attacker"));
            }
            Assert.That(_w.Save(), Is.EqualTo(before));
        }

        [Test]
        public void WithoutAViewPolicyEverythingIsSeen()
        {
            NewWorld(Defs.Replace("@view_policy\n", ""));
            var b = _w.ViewAs("B");
            Assert.That(((IntValue)b.Get(_a, M("Hand", "cards"))).Value, Is.EqualTo(3));
            Assert.That(b.All("Unit"), Is.EqualTo(new[] { _scout }));
        }

        // ---- 닫는 쪽 (G203) ----

        [TestCase("    return 1\n", "it must return True or False, not int; treated as not seen")]
        [TestCase("    return entity.Nope.x\n", "treated as not seen")]
        public void ABrokenPolicyHidesAndLeavesADiagnostic(string body, string expected)
        {
            NewWorld(Defs.Replace(Policies, Policies.Substring(0, Policies.IndexOf("@view_policy")) +
                                            "@view_policy\ndef may_see(issuer, entity, component):\n" + body));
            var b = _w.ViewAs("B");
            Assert.That(b.All("Party"), Is.Empty);
            Assert.That(b.Diagnostics.Select(d => d.Code), Has.All.EqualTo(Codes.RunViewPolicy));
            Assert.That(b.Diagnostics.First().Message, Does.EndWith(expected));
            Assert.That(b.Diagnostics.First().Span.Line, Is.GreaterThan(0), "points at the policy");
        }

        // ---- 로드 검사와 호스트 설정 ----

        private List<Diagnostic> Diags(string defs)
        {
            Mod("banking", schema: Schema, defs: "Party(\"Side\")\n" + defs);
            Load(out var diags);
            return diags.ToList();
        }

        [TestCase("@view_policy\ndef may_see(issuer, entity):\n    return True\n",
            "CMD006 a view policy takes exactly three parameters without defaults: (issuer, entity, component)")]
        [TestCase("@view_policy\ndef may_see(issuer, entity, component=None):\n    return True\n",
            "CMD006 a view policy takes exactly three parameters without defaults: (issuer, entity, component)")]
        [TestCase("@view_policy\ndef a(i, e, c):\n    return True\n@view_policy\ndef b(i, e, c):\n    return True\n",
            "CMD006 a mod can declare only one view policy")]
        [TestCase("@view_policy(override=True)\ndef a(i, e, c):\n    return True\n",
            "CMD006 override=True but there is no view policy to replace")]
        [TestCase("@view_policy\ndef a(i, e, c):\n    e.Hand.cards = 1\n    return True\n",
            "CMD004 the view policy 'a' changes the world; it can only read")]
        [TestCase("@view_policy(sneaky=True)\ndef a(i, e, c):\n    return True\n",
            "CMD001 @view_policy has no argument 'sneaky'")]
        public void PolicyDeclarationsAreChecked(string defs, string expected)
        {
            var diags = Diags(defs);
            Assert.That(diags.Select(d => d.Code + " " + d.Message), Has.Some.EqualTo(expected),
                string.Join("\n", diags.Select(d => d.Code + " " + d.Message)));
        }

        [Test]
        public void ASubmitAndAViewPolicyCanLiveSideBySide()
        {
            Mod("banking", schema: Schema, defs: Defs);
            var loaded = LoadClean();
            Assert.That(loaded.SubmitPolicy!.Function.Name, Is.EqualTo("may_act"));
            Assert.That(loaded.ViewPolicy!.Function.Name, Is.EqualTo("may_see"));
            Assert.That(loaded.ViewPolicy.Function.IsViewPolicy, Is.True);
            Assert.That(loaded.ViewPolicy.Function.IsCommand, Is.False);
            Assert.That(loaded.Commands.Keys, Has.None.EqualTo("banking.may_see"));
        }

        [Test]
        public void RequiringAViewPolicyAlsoRequiresASubmitPolicy()
        {
            Mod("banking", schema: Schema, defs: "Party(\"Side\")\n");
            var none = Assert.Throws<WorldException>(() => new World(LoadClean(), requireViewPolicy: true))!;
            Assert.That(none.Code, Is.EqualTo(Codes.RunNoViewPolicy));
            Assert.That(none.Message, Is.EqualTo("the host requires a view policy, but no active mod declares one"));

            Mod("banking", schema: Schema, defs: "Party(\"Side\")\n@view_policy\ndef may_see(i, e, c):\n    return True\n");
            var noGate = Assert.Throws<WorldException>(() => new World(LoadClean(), requireViewPolicy: true))!;
            Assert.That(noGate.Code, Is.EqualTo(Codes.RunNoViewPolicy));
            Assert.That(noGate.Message, Does.Contain("also needs a submit policy"));

            NewWorld(requireView: true); // 둘 다 있으면 된다
        }
    }
}
