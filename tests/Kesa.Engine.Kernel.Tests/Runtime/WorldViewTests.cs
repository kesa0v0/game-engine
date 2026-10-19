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

        // ---- 제출·답의 결과 (G201) ----

        private const string ShowDefs =
            Defs +
            // 거부 사유의 문구 인자: 숨긴 유닛은 가리고, 모드가 넣은 숫자(남의 손패 수)는 모드가 알려 준 것이라 그대로
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Probe(party: Ref[Party], unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        cost(unit.Owner.party.Hand.cards, 100)\n" +
            "        cost(party.Hand.cards, 100)\n" +
            "        require(False, text(\"e.probe\", who=unit, cards=unit.Owner.party.Hand.cards, me=party))\n" +
            "    pass\n" +
            // 스크립트 오류의 메시지에는 숨긴 유닛의 id가 든다
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Boom(party: Ref[Party], unit: Ref[Unit]):\n" +
            "    destroy(unit)\n" +
            "    unit.Fog.hidden = False\n" +
            // 남(A)에게 묻는 질문을 연다
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Poke(party: Ref[Party], other: Ref[Party]):\n" +
            "    r = ask(other, \"q.poke\", choices=[\"ok\"])\n" +
            "    pass\n";

        private SubmitResult SubmitAs(string issuer, string command, params (string, EntityId)[] args)
        {
            var values = new Dictionary<string, Value>();
            foreach (var (name, id) in args) values[name] = new EntityValue(id);
            return _w.Submit(issuer, "banking." + command, values);
        }

        [Test]
        public void ARejectionShowsHiddenEntitiesAsHiddenButWhatTheModWroteAsIs()
        {
            NewWorld(ShowDefs);
            var full = SubmitAs("B", "Probe", ("party", _b), ("unit", _scout));
            Assert.That(full.Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            var shown = _w.ViewAs("B").Show(full);
            Assert.That(shown.Outcome, Is.EqualTo(SubmitOutcome.PreconditionsFailed));
            Assert.That(shown.Arguments!["unit"], Is.SameAs(HiddenValue.Instance), "B named the scout but must not learn its id back");
            Assert.That(((EntityValue)shown.Arguments["party"]).Id, Is.EqualTo(_b));
            Assert.That(((EntityValue)shown.Target!).Id, Is.EqualTo(_b));

            var requires = shown.Preconditions!.Requires;
            // A의 손패 칸(B에게 숨김): 칸과 가진 양은 없고, 필요한 양은 모드가 쓴 식
            Assert.That(requires[0].Slot, Is.Null);
            Assert.That(requires[0].Have, Is.SameAs(HiddenValue.Instance));
            Assert.That(((IntValue)requires[0].Need!).Value, Is.EqualTo(100));
            Assert.That(full.Preconditions!.Requires[0].Slot, Does.StartWith(_a + ".Hand"), "the server still sees everything");
            // 자기 손패 칸은 그대로
            Assert.That(requires[1].Slot, Is.EqualTo(_b + ".Hand.cards"));
            Assert.That(((IntValue)requires[1].Have!).Value, Is.EqualTo(5));

            var text = requires[2];
            Assert.That(text.MessageKey, Is.EqualTo("e.probe"));
            Assert.That(text.Args["who"], Is.SameAs(HiddenValue.Instance));
            Assert.That(((IntValue)text.Args["cards"]).Value, Is.EqualTo(3), "a number the mod put in the text is the mod's choice");
            Assert.That(((EntityValue)text.Args["me"]).Id, Is.EqualTo(_b));
            Assert.That(shown.Preconditions.Failures.Select(f => f.Span), Is.EqualTo(full.Preconditions.Failures.Select(f => f.Span)));

            // A는 다 본다
            var a = _w.ViewAs("A").Show(full);
            Assert.That(((EntityValue)a.Arguments!["unit"]).Id, Is.EqualTo(_scout));
            Assert.That(a.Preconditions!.Requires[0].Slot, Is.EqualTo(full.Preconditions.Requires[0].Slot));
        }

        [Test]
        public void AScriptErrorKeepsOnlyItsCodeAndArgumentRejectionsOnlyTheirKind()
        {
            NewWorld(ShowDefs);
            var boom = SubmitAs("B", "Boom", ("party", _b), ("unit", _scout));
            Assert.That(boom.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(boom.Error!.Message, Does.Contain(_scout.ToString()), "the server log names the scout");
            var shown = _w.ViewAs("B").Show(boom);
            Assert.That(shown.Outcome, Is.EqualTo(SubmitOutcome.ScriptError));
            Assert.That(shown.Error!.Code, Is.EqualTo(boom.Error.Code));
            Assert.That(shown.Error.Message, Is.EqualTo("script error"));
            Assert.That(shown.Error.Hint, Is.Null);

            var bad = _w.Submit("B", "banking.Boom", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_b), ["unit"] = new EntityValue(_a),
            });
            Assert.That(bad.Outcome, Is.EqualTo(SubmitOutcome.ArgumentsRejected));
            var r = _w.ViewAs("B").Show(bad).ArgumentRejections.Single();
            Assert.That((r.Parameter, r.Path, r.Problem, r.Expected, r.Message),
                Is.EqualTo(("unit", "unit", bad.ArgumentRejections[0].Problem, bad.ArgumentRejections[0].Expected, "")));
        }

        [Test]
        public void AnOpenedQuestionIsShownOnlyToWhoMayAnswerAndReactionsAreLeftOut()
        {
            NewWorld(ShowDefs);
            var mine = SubmitAs("B", "Target", ("party", _b), ("unit", _scout));
            var q = _w.ViewAs("B").Show(mine).OpenedRequest!;
            Assert.That(q.Id, Is.EqualTo(mine.OpenedRequest!.Id));
            Assert.That(((TextValue)q.Title).Args["who"], Is.SameAs(HiddenValue.Instance));
            Assert.That(q.Choices[0], Is.SameAs(HiddenValue.Instance));

            var poke = SubmitAs("B", "Poke", ("party", _b), ("other", _a));
            Assert.That(poke.OpenedRequest, Is.Not.Null);
            Assert.That(_w.ViewAs("B").Show(poke).OpenedRequest, Is.Null, "the question is A's to answer");
            Assert.That(_w.ViewAs("A").Show(poke).OpenedRequest!.Id, Is.EqualTo(poke.OpenedRequest!.Id));

            // 답의 거절 이유 본문(엔티티 id가 든다)은 빠지고 결과 종류만 남는다
            var wrong = _w.Answer("B", q.Id, _a, 1);
            Assert.That(wrong.Message, Is.Not.Null);
            var shown = _w.ViewAs("B").Show(wrong);
            Assert.That((shown.Request, shown.Outcome, shown.Message), Is.EqualTo((wrong.Request, wrong.Outcome, (string?)null)));
            var ok = _w.ViewAs("B").Show(_w.Answer("B", q.Id, _b, 1));
            Assert.That(ok.Succeeded, Is.True);
            Assert.That(ok.Reactions, Is.Empty);
        }

        // ---- 근거 (G199·G205) ----

        private World ThreatWorld(params (int atk, bool hidden)[] units)
        {
            Mod("scouts", schema:
                    "component(\"Side\", conn=field(str, default=\"\"))\n" +
                    "component(\"Combat\", atk=field(int, default=0))\n" +
                    "component(\"Fog\", hidden=field(bool, default=False))\n" +
                    "def threat(d):\n" +
                    "    total = 0\n" +
                    "    for u in query(\"Combat\"):\n" +
                    "        total += u.Combat.atk\n" +
                    "    for r in contribs(d, \"threat\"):\n" +
                    "        total += r.value\n" +
                    "    return floor(total)\n" +
                    "component(\"Defense\", threat=derived(int, threat))\n" +
                    "deftype(\"Unit\", components=[\"Side\", \"Combat\", \"Fog\"])\n" +
                    "deftype(\"Castle\", components=[\"Side\", \"Defense\"])\n",
                defs: "Unit(\"Raider\")\nCastle(\"Keep\")\n" +
                      "@view_policy\ndef may_see(issuer, entity, component):\n" +
                      "    return entity.Fog is None or not entity.Fog.hidden or entity.Side.conn == issuer\n");
            var w = new World(LoadClean());
            var castle = w.Create("Keep");
            w.Set(castle, w.Member("Side", "conn"), new StrValue("B"));
            foreach (var (atk, hidden) in units)
            {
                var u = w.Create("Raider");
                w.Set(u, w.Member("Side", "conn"), new StrValue("A"));
                w.Set(u, w.Member("Combat", "atk"), new IntValue(atk));
                w.Set(u, w.Member("Fog", "hidden"), hidden ? BoolValue.True : BoolValue.False);
            }
            return w;
        }

        [Test]
        public void TheExplanationKeepsTheValueAndFoldsHiddenReadsIntoOneLineAtTheEnd()
        {
            var w = ThreatWorld((4, true), (3, false), (5, true), (0, false));
            var castle = new EntityId(1);
            var threat = w.Member("Defense", "threat");
            w.AddContribution(castle, threat, "add", new IntValue(2), new EntityId(2)); // 숨긴 #2에서
            w.AddContribution(castle, threat, "add", new IntValue(1), new EntityId(3)); // 보이는 #3에서
            Assert.That(w.Explain(castle, threat).Render(), Is.EqualTo(
                "Defense.threat of #1 = 15\n" +
                "  query(\"Combat\") = [#2, #3, #4, #5]\n" +
                "  Combat.atk of #2 = 4\n  Combat.atk of #3 = 3\n  Combat.atk of #4 = 5\n  Combat.atk of #5 = 0\n" +
                "  contribution add 2.0 from #2\n  contribution add 1.0 from #3"));

            var b = w.ViewAs("B");
            Assert.That(b.Explain(castle, threat).Render(), Is.EqualTo(
                "Defense.threat of #1 = 15\n" +
                "  query(\"Combat\") = [#3, #5]\n" +
                "  Combat.atk of #3 = 3\n  Combat.atk of #5 = 0\n" +
                "  contribution add 1.0 from #3\n" +
                "  (hidden)"));
            Assert.That(b.Diagnostics, Is.Empty);
            // A는 자기 유닛이라 전부 본다
            Assert.That(w.ViewAs("A").Explain(castle, threat).Render(), Is.EqualTo(w.Explain(castle, threat).Render()));
            // 못 보는 유닛의 값은 설명할 수도 없다(죽은 id와 같다)
            Assert.That(Assert.Throws<WorldException>(() => b.Explain(new EntityId(2), w.Member("Combat", "atk")))!.Code,
                Is.EqualTo(Codes.RunDeadEntity));
        }

        [Test]
        public void HowManyHiddenReadsThereWereDoesNotShow()
        {
            var one = ThreatWorld((4, true), (3, false));
            var two = ThreatWorld((4, true), (3, false), (0, true));
            var threat = one.Member("Defense", "threat");
            Assert.That(two.Explain(new EntityId(1), threat).Render(), Is.Not.EqualTo(one.Explain(new EntityId(1), threat).Render()));
            Assert.That(two.ViewAs("B").Explain(new EntityId(1), threat).Render(),
                Is.EqualTo(one.ViewAs("B").Explain(new EntityId(1), threat).Render()));
            var nothing = ThreatWorld((3, false));
            Assert.That(nothing.ViewAs("B").Explain(new EntityId(1), threat).Render(), Does.Not.Contain("hidden"));
        }

        // ---- 합법 수와 후보 (G202) ----

        private const string MoveDefs =
            Defs +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Attack(party: Ref[Party], unit: Ref[Unit]):\n" +
            "    with preconditions:\n" +
            "        choose(unit, where=lambda u: u.Owner.party != party)\n" +
            "    pass\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Group(party: Ref[Party], units: list[Ref[Unit]]):\n" +
            "    with preconditions:\n" +
            "        choose(units, from_=[[u] for u in query(\"Unit\")])\n" +
            "    pass\n";

        /// <summary>id 순으로 scout(숨김) · u1 · h(숨김) · u2. 모두 A의 유닛이다.</summary>
        private (EntityId u1, EntityId h, EntityId u2) MoveWorld()
        {
            NewWorld(MoveDefs);
            EntityId Unit(bool hidden)
            {
                var u = _w.Create("Scout", new Init(M("Owner", "party"), new EntityValue(_a)));
                _w.Set(u, M("Fog", "hidden"), hidden ? BoolValue.True : BoolValue.False);
                return u;
            }
            return (Unit(false), Unit(true), Unit(false));
        }

        private static IEnumerable<EntityId> Units(LegalMoveList list, string parameter) =>
            list.Moves.Select(m => ((EntityValue)m[parameter]).Id);

        [Test]
        public void LegalMovesLeaveOutHiddenTargetsAndTheLimitCountsOnlyWhatIsSeen()
        {
            var (u1, h, u2) = MoveWorld();
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(_b) };
            var world = _w.LegalMoves("banking.Attack", args, 10);
            Assert.That(Units(world, "unit"), Is.EqualTo(new[] { _scout, u1, h, u2 }));

            var b = _w.ViewAs("B");
            Assert.That(Units(b.LegalMoves("banking.Attack", args, 10), "unit"), Is.EqualTo(new[] { u1, u2 }));
            // 상한 2: 월드는 숨긴 scout가 한 자리를 차지해 잘리지만, B에게는 보이는 둘이 다이고 잘리지 않았다
            Assert.That(_w.LegalMoves("banking.Attack", args, 2).Truncated, Is.True);
            var two = b.LegalMoves("banking.Attack", args, 2);
            Assert.That((Units(two, "unit"), two.Truncated), Is.EqualTo((new[] { u1, u2 }, false)));
            var one = b.LegalMoves("banking.Attack", args, 1);
            Assert.That((Units(one, "unit"), one.Truncated), Is.EqualTo((new[] { u1 }, true)));

            // A는 B로서 낼 수 없으니(acts_for → may_act) B 쪽 인자로 물으면 수가 없다 (G233)
            Assert.That(_w.ViewAs("A").LegalMoves("banking.Attack", args, 10).Moves, Is.Empty);
            Assert.That(b.Diagnostics, Is.Empty);
        }

        /// <summary>거른 합법 수는 제출 때처럼 acts_for → may_act를 통과한 수만 담는다 (G233). 담긴 수는 그 연결로 내면 입구를 지난다.</summary>
        [Test]
        public void LegalMovesListOnlyMovesTheConnectionMayIssue()
        {
            NewWorld(Defs +
                     "@command(free=True, acts_for=lambda party: party)\n" +
                     "def Concede(party: Ref[Party]):\n" +
                     "    with preconditions:\n" +
                     "        choose(party, from_=query(\"Party\"))\n" +
                     "    pass\n");
            var none = new Dictionary<string, Value>();
            IEnumerable<EntityId> Parties(LegalMoveList list) => Units(list, "party");
            Assert.That(Parties(_w.LegalMoves("banking.Concede", none, 10)), Is.EqualTo(new[] { _a, _b }));
            var a = _w.ViewAs("A");
            Assert.That(Parties(a.LegalMoves("banking.Concede", none, 10)), Is.EqualTo(new[] { _a }));
            Assert.That(Parties(_w.ViewAs("B").LegalMoves("banking.Concede", none, 10)), Is.EqualTo(new[] { _b }));
            Assert.That(_w.ViewAs("C").LegalMoves("banking.Concede", none, 10).Moves, Is.Empty);
            // 상한은 걸러진 뒤의 수만 센다
            var one = a.LegalMoves("banking.Concede", none, 1);
            Assert.That((Parties(one), one.Truncated), Is.EqualTo((new[] { _a }, false)));
            Assert.That(_w.Submit("A", "banking.Concede", a.LegalMoves("banking.Concede", none, 10).Moves.Single()).Succeeded, Is.True);
            Assert.That(a.Diagnostics, Is.Empty);
        }

        [Test]
        public void CandidatesLeaveOutHiddenEntitiesEvenInsideLists()
        {
            var (u1, h, u2) = MoveWorld();
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(_b) };
            var b = _w.ViewAs("B");
            Assert.That(b.Candidates("banking.Attack", args, "unit").Values.Select(v => ((EntityValue)v).Id), Is.EqualTo(new[] { u1, u2 }));

            var groups = b.Candidates("banking.Group", args, "units").Values;
            Assert.That(groups.Select(g => ((EntityValue)((ListValue)g).Items.Single()).Id), Is.EqualTo(new[] { u1, u2 }));
            Assert.That(b.LegalMoves("banking.Group", args, 10).Moves.Select(m => ((EntityValue)((ListValue)m["units"]).Items.Single()).Id),
                Is.EqualTo(new[] { u1, u2 }));
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
