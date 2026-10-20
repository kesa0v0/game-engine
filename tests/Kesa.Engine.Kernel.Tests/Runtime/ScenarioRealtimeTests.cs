using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 검증 시나리오 ④ "실시간" (spec 7절): 한 참가자에게 기한이 있는 선택이 뜨고(시간은 멈추지 않음) 그 사이 다른 참가자가 자유
    /// 커맨드를 여러 개 동시에 진행시키며, 선택 뒤 같은 스크립트가 이어진다. 기한이 되면 기본 응답으로 진행한다.
    /// A의 습격 커맨드가 <c>ask(..., deadline=3, on_timeout="retreat")</c>로 묻는 동안(G159~G162) 틱마다 수입이 들어오고,
    /// B는 여러 틱에 걸쳐 <c>wait</c>하는 행군 둘과 금화를 내는 건설을 진행한다. 코어를 고치지 않고 스크립트와 호스트 코드만으로 표현한다.
    /// </summary>
    public class ScenarioRealtimeTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Gold\", n=field(int, default=0))\n" +
            "component(\"Army\", at=field(str, default=\"home\"), decided=field(int, default=-1), marched=field(int, default=0),\n" +
            "    walls=field(int, default=0))\n" +
            "deftype(\"Party\", components=[\"Gold\", \"Army\"])\n";

        private const string Defs =
            "Party(\"Player\")\n" +
            "@on(Tick)\n" +
            "def income():\n" +
            "    for p in query(\"Party\"):\n" +
            "        p.Gold.n += 1\n" +
            // 기한 3틱의 선택. 시간은 흐르고, 답이나 기본 답이 오면 같은 스크립트가 이어진다
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Raid(party: Ref[Party]):\n" +
            "    r = ask(party, \"raid.where\", choices=[\"north\", \"south\", \"retreat\"], deadline=3, on_timeout=\"retreat\")\n" +
            "    party.Army.decided = now()\n" +
            "    if r.choice != \"retreat\":\n" +
            "        party.Army.at = r.choice\n" +
            "        party.Gold.n += 10\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def March(party: Ref[Party]):\n" +
            "    for i in range(3):\n" +
            "        wait(ticks=1)\n" +
            "        party.Army.marched += 1\n" +
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Build(party: Ref[Party]):\n" +
            "    with preconditions:\n" +
            "        cost(party.Gold.n, 2)\n" +
            "    party.Army.walls += 1\n" +
            // 기한 없는 질문은 시간을 막는다 (G159)
            "@command(free=True, acts_for=lambda party: party)\n" +
            "def Ambush(party: Ref[Party]):\n" +
            "    r = ask(party, text(\"ambush.where\", by=party), choices=[\"pass\", \"woods\"])\n" +
            "    party.Army.at = r.choice\n";

        private ContentLoadResult _content = null!;

        [SetUp]
        public void Content()
        {
            Mod("core", schema: Schema, defs: Defs);
            _content = LoadClean();
        }

        private World Start()
        {
            var w = new World(_content, seed: 4);
            foreach (var conn in new[] { "A", "B" })
                w.Seat(w.Create("core:Player"), conn);
            return w;
        }

        private static EntityId PartyOf(World w, string conn) =>
            w.SeatsOf(conn).Single();

        private static SubmitResult Do(World w, string conn, string command)
        {
            var r = w.Submit(conn, "core." + command, new Dictionary<string, Value> { ["party"] = new EntityValue(PartyOf(w, conn)) });
            return r;
        }

        private static long Int(World w, string conn, string component, string member) =>
            ((IntValue)w.Get(PartyOf(w, conn), w.Member(component, member))).Value;

        private static string Str(World w, string conn, string member) =>
            ((StrValue)w.Get(PartyOf(w, conn), w.Member("Army", member))).Value;

        private static void Answer(World w, string conn, string choice)
        {
            var q = w.ViewAs(conn).OpenRequests.Single();
            var a = w.Answer(conn, q.Id, PartyOf(w, conn), q.Choices.ToList().FindIndex(c => ((StrValue)c.Value).Value == choice));
            Assert.That(a.Succeeded, Is.True, a.Message);
        }

        private static void Tick(World w)
        {
            var r = w.Advance();
            Assert.That((r.Advanced, r.Blocked), Is.EqualTo((1, false)), "a question with a deadline does not stop time (G159)");
        }

        /// <summary>
        /// 경기 진행을 단계로: 단계 사이마다 저장하고 불러와도 같아야 한다 (C1). <paramref name="answer"/>가 null이면 A는 답하지 않는다.
        /// </summary>
        private static IReadOnlyList<Action<World>> Steps(string? answer)
        {
            var steps = new List<Action<World>>
            {
                w => Assert.That(Do(w, "A", "Raid").OpenedRequest, Is.Not.Null),
                w =>
                {
                    Assert.That(Do(w, "B", "March").SleepsUntil, Is.EqualTo(1));
                    Assert.That(Do(w, "B", "March").SleepsUntil, Is.EqualTo(1));
                },
                Tick, // 틱 1: 수입 +1, 행군 둘 1걸음
                w => Assert.That(Do(w, "B", "Build").Succeeded, Is.False, "B has only 1 gold"),
                Tick, // 틱 2: 금화 2
                w => Assert.That(Do(w, "B", "Build").Succeeded, Is.True),
            };
            if (answer != null) steps.Add(w => Answer(w, "A", answer)); // 틱 2에 답한다
            steps.Add(Tick); // 틱 3: 행군 끝, 답하지 않았으면 마감
            steps.Add(Tick); // 틱 4
            return steps;
        }

        private static World Play(World w, IReadOnlyList<Action<World>> steps)
        {
            foreach (var step in steps) step(w);
            return w;
        }

        [Test]
        public void TimeGoesOnWhileAIsAskedAndTheSameScriptContinuesAfterTheAnswer()
        {
            var w = Start();
            var steps = Steps("north");
            for (int i = 0; i < steps.Count; i++)
            {
                steps[i](w);
                if (i >= 1 && i <= 5)
                {
                    // A가 묻는 동안: A만 그 질문을 보고, B는 자유 커맨드를 진행한다
                    Assert.That(w.ViewAs("A").OpenRequests, Has.Count.EqualTo(1));
                    Assert.That(w.ViewAs("B").OpenRequests, Is.Empty);
                }
            }
            Assert.That(w.Tick, Is.EqualTo(4));
            Assert.That((Str(w, "A", "at"), Int(w, "A", "Army", "decided")), Is.EqualTo(("north", 2L)), "the script went on at tick 2");
            Assert.That(Int(w, "A", "Gold", "n"), Is.EqualTo(4 + 10));
            Assert.That(Int(w, "B", "Army", "marched"), Is.EqualTo(6), "both marches walked three steps");
            Assert.That((Int(w, "B", "Army", "walls"), Int(w, "B", "Gold", "n")), Is.EqualTo((1L, 4L - 2)));
            Assert.That(w.OpenRequests, Is.Empty);
        }

        [Test]
        public void WhenTheDeadlinePassesTheDefaultAnswerGoesOn()
        {
            var w = Start();
            var deadline = Do(w, "A", "Raid").OpenedRequest!.Deadline;
            Assert.That(deadline, Is.EqualTo(3));
            var steps = Steps(null);
            foreach (var step in steps.Skip(1).Take(5)) step(w);
            Assert.That(w.OpenRequests, Has.Count.EqualTo(1), "still open at tick 2");
            var r = w.Advance();
            Assert.That(r.Reactions.Select(x => x.Signal), Does.Contain(Reaction.TimeoutSignal));
            Assert.That((Str(w, "A", "at"), Int(w, "A", "Army", "decided")), Is.EqualTo(("home", 3L)), "retreat at the deadline");
            Assert.That(Int(w, "A", "Gold", "n"), Is.EqualTo(3));
            Assert.That(w.OpenRequests, Is.Empty);
            Assert.That(Int(w, "B", "Army", "marched"), Is.EqualTo(6));
        }

        /// <summary>시간이 멈춘 것은 모두가 알지만, 멈춘 이유의 질문은 답할 수 있는 연결에게만 보인다 (G206).</summary>
        [Test]
        public void EveryoneSeesThatTimeStoppedButOnlyTheOneAskedSeesWhy()
        {
            var w = Start();
            Do(w, "A", "Raid");
            var ambush = Do(w, "B", "Ambush").OpenedRequest!;
            var r = w.Advance();
            Assert.That((r.Advanced, r.Blocked, r.BlockedBy.Select(q => q.Id)), Is.EqualTo((0, true, new[] { ambush.Id })),
                "A's raid has a deadline and does not block; B's ambush does");

            var a = w.ViewAs("A").Show(r);
            Assert.That((a.Advanced, a.Tick, a.Blocked, a.BlockedBy.Count, a.Reactions.Count), Is.EqualTo((0, 0L, true, 0, 0)));
            var b = w.ViewAs("B").Show(r);
            Assert.That((b.Blocked, b.BlockedBy.Single().Id), Is.EqualTo((true, ambush.Id)));
            Assert.That(((TextValue)b.BlockedBy[0].Title).Key, Is.EqualTo("ambush.where"));

            Answer(w, "B", "woods");
            Assert.That(w.ViewAs("A").OpenRequests.Single().Id, Is.Not.EqualTo(ambush.Id), "A still has its raid question");
            var on = w.ViewAs("A").Show(w.Advance());
            Assert.That((on.Advanced, on.Blocked, on.BlockedBy.Count), Is.EqualTo((1, false, 0)));
        }

        // C1: 어느 단계 사이에서 저장하고 불러와 이어도 같은 결과다 (답한 경기와 마감된 경기 둘 다)
        [TestCase("north")]
        [TestCase(null)]
        public void SavingBetweenAnyStepsGoesOn(string? answer)
        {
            var steps = Steps(answer);
            var straight = Play(Start(), steps).Save();
            for (int cut = 0; cut <= steps.Count; cut++)
            {
                var w = Start();
                foreach (var step in steps.Take(cut)) step(w);
                var loaded = World.Load(_content, w.Save());
                Assert.That(loaded.Diagnostics, Is.Empty);
                w = loaded.World!;
                foreach (var step in steps.Skip(cut)) step(w);
                Assert.That(w.Save(), Is.EqualTo(straight), "saved after step " + cut);
            }
        }
    }
}
