using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kesa.Engine.Loading;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using Kesa.Engine.Tests.Loading;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Runtime
{
    /// <summary>
    /// 검증 시나리오 ② "동시 비공개 선택" (spec 7절): 모든 참가자가 서로의 선택을 보지 못한 채 동시에 선택하고, 모두 끝나면
    /// 공개된다. 가위바위보 셋. 핸들러가 참가자마다 질문을 하나씩 열고(동시에 열림, 아무 순서로 답함), 고른 값은 남에게 숨긴
    /// <c>Pick</c>에, "골랐음"은 공개 <c>Ready</c>에 둔다(G198). 모두 고르면 <c>@when</c>이 공개하고 점수를 매긴다.
    /// 숨김은 보기 정책과 거른 읽기 <c>ViewAs</c>(G196~G203)로 한다. 코어를 고치지 않고 스크립트와 호스트 코드만으로 표현한다.
    /// </summary>
    public class ScenarioSimultaneousTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Pick\", choice=field(int, default=-1))\n" +
            "component(\"Ready\", done=field(bool, default=False))\n" +
            "component(\"Score\", wins=field(int, default=0))\n" +
            "component(\"Round\", revealed=field(bool, default=False))\n" +
            "deftype(\"Party\", components=[\"Pick\", \"Ready\", \"Score\"])\n" +
            "deftype(\"Game\", components=[\"Round\"])\n" +
            "signal(\"AskPick\", who=Ref[\"Party\"])\n";

        private const string Defs =
            "Party(\"Player\")\nGame(\"Match\")\n" +
            // 남의 선택은 공개될 때까지 못 본다. 그 밖에는 다 보인다
            "@view_policy\n" +
            "def may_see(viewer, entity, component):\n" +
            "    if component != \"Pick\":\n" +
            "        return True\n" +
            "    return viewer == entity or any(g.Round.revealed for g in query(\"Game\"))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Start():\n" +
            "    for p in query(\"Party\"):\n" +
            "        emit(AskPick(who=p))\n" +
            "@on(AskPick)\n" +
            "def ask_pick(who):\n" +
            "    r = ask(who, \"rps.pick\", choices=[\"rock\", \"paper\", \"scissors\"])\n" +
            "    who.Pick.choice = r.index\n" +
            "    who.Ready.done = True\n" +
            // 모두 골랐으면 공개하고, 각자 이긴 상대 수만큼 점수
            "@when(lambda g: not g.Round.revealed and all(p.Ready.done for p in query(\"Party\")))\n" +
            "def reveal(g: Ref[Game]):\n" +
            "    g.Round.revealed = True\n" +
            "    for p in query(\"Party\"):\n" +
            "        for q in query(\"Party\"):\n" +
            "            if (p.Pick.choice - q.Pick.choice) % 3 == 1:\n" +
            "                p.Score.wins += 1\n";

        private const int Rock = 0, Paper = 1, Scissors = 2;

        private ContentLoadResult _content = null!;

        [SetUp]
        public void Content()
        {
            Mod("core", schema: Schema, defs: Defs);
            _content = LoadClean();
        }

        private static readonly string[] Conns = { "A", "B", "C" };

        private World NewGame()
        {
            var w = new World(_content, seed: 3, requireViewPolicy: true);
            foreach (var conn in Conns)
                w.Seat(w.Create("core:Player"), conn);
            w.Create("core:Match");
            var s = w.Submit("host", "core.Start", new Dictionary<string, Value>());
            Assert.That(s.Succeeded, Is.True, s.Error?.ToString());
            return w;
        }

        private static EntityId PartyOf(World w, string conn) =>
            w.SeatsOf(conn).Single();

        /// <summary>그 연결이 자기 질문에 답한다.</summary>
        private static void Choose(World w, string conn, int pick)
        {
            var q = w.ViewAs(conn).OpenRequests.Single();
            var a = w.Answer(conn, q.Id, PartyOf(w, conn), pick);
            Assert.That(a.Succeeded, Is.True, a.Message);
        }

        /// <summary>거른 읽기가 내보내는 모든 것을 한 문자열로: 엔티티, 붙은 컴포넌트의 모든 칸, 열린 질문.</summary>
        private static string Render(World w, string conn)
        {
            var v = w.ViewAs(conn);
            var sb = new StringBuilder();
            foreach (var type in new[] { "Party", "Game" })
            foreach (var e in v.All(type))
            {
                sb.Append(e).Append(' ').Append(v.DefOf(e)).Append('\n');
                foreach (var (component, members) in new[]
                         {
                             ("Pick", new[] { "choice" }), ("Ready", new[] { "done" }),
                             ("Score", new[] { "wins" }), ("Round", new[] { "revealed" }),
                         })
                {
                    if (!v.Has(e, component)) continue;
                    foreach (var m in members) sb.Append("  ").Append(component).Append('.').Append(m).Append('=')
                        .Append(Show(v.Get(e, w.Member(component, m)))).Append('\n');
                }
            }
            foreach (var q in v.OpenRequests)
                sb.Append("Q").Append(q.Id).Append(' ').Append(string.Join(",", q.Who)).Append(' ').Append(Show(q.Title))
                    .Append(' ').Append(string.Join(",", q.Choices.Select(c => Show(c.Value)))).Append('\n');
            Assert.That(v.Diagnostics, Is.Empty);
            return sb.ToString();
        }

        private static string Show(Value v)
        {
            switch (v)
            {
                case IntValue i: return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case BoolValue b: return b.Value ? "True" : "False";
                case StrValue s: return s.Value;
                case EntityValue e: return e.Id.ToString();
            }
            return v.KindName;
        }

        [Test]
        public void EveryoneIsAskedAtOnceAndSeesOnlyTheirOwnQuestion()
        {
            var w = NewGame();
            Assert.That(w.OpenRequests, Has.Count.EqualTo(3), "three questions are open at the same time");
            foreach (var conn in Conns)
                Assert.That(w.ViewAs(conn).OpenRequests.Single().Who, Is.EqualTo(new[] { PartyOf(w, conn) }));
            // 아무 순서로 답한다
            Choose(w, "C", Scissors);
            Choose(w, "A", Rock);
            Assert.That(w.ViewAs("B").OpenRequests, Has.Count.EqualTo(1));
            Choose(w, "B", Paper);
            Assert.That(w.OpenRequests, Is.Empty);
        }

        /// <summary>
        /// 공개 전에는 A가 무엇을 냈든 B의 화면이 같다(A가 "골랐음"은 보인다). 모두 고르면 공개되어 화면이 달라진다.
        /// </summary>
        [Test]
        public void BeforeTheRevealBCannotTellWhatAPicked()
        {
            var rock = NewGame();
            var paper = NewGame();
            Choose(rock, "A", Rock);
            Choose(paper, "A", Paper);
            Assert.That(Render(rock, "B"), Is.EqualTo(Render(paper, "B")));
            Assert.That(Render(rock, "C"), Is.EqualTo(Render(paper, "C")));
            Assert.That(Render(rock, "A"), Is.Not.EqualTo(Render(paper, "A")), "A sees its own pick");
            Assert.That(Render(rock, "B"), Does.Contain("Ready.done=True"), "that A has picked is public");

            Choose(rock, "B", Scissors);
            Choose(paper, "B", Scissors);
            Assert.That(Render(rock, "B"), Is.EqualTo(Render(paper, "B")), "C has not picked yet");

            Choose(rock, "C", Scissors);
            Choose(paper, "C", Scissors);
            Assert.That(Render(rock, "B"), Is.Not.EqualTo(Render(paper, "B")), "after everyone picked the choices are revealed");
        }

        [Test]
        public void WhenEveryoneHasPickedTheRoundIsRevealedAndScored()
        {
            var w = NewGame();
            Choose(w, "A", Rock);
            Choose(w, "B", Paper);
            var b = w.ViewAs("B");
            Assert.That(b.Has(PartyOf(w, "A"), "Pick"), Is.False);
            Choose(w, "C", Paper);

            var c = w.ViewAs("C");
            Assert.That(((IntValue)c.Get(PartyOf(w, "A"), w.Member("Pick", "choice"))).Value, Is.EqualTo(Rock));
            Assert.That(((BoolValue)c.Get(w.Query("Game").Single(), w.Member("Round", "revealed"))).Value, Is.True);
            // 보는 바위를 이긴다: B·C가 A를 한 번씩 이긴다
            Assert.That(Conns.Select(conn => ((IntValue)w.Get(PartyOf(w, conn), w.Member("Score", "wins"))).Value),
                Is.EqualTo(new long[] { 0, 1, 1 }));
        }

        [Test]
        public void NobodyMayAnswerSomeoneElsesQuestion()
        {
            var w = NewGame();
            var aq = w.ViewAs("A").OpenRequests.Single();
            var a = w.Answer("B", aq.Id, PartyOf(w, "A"), Rock);
            Assert.That(a.Outcome, Is.EqualTo(AnswerOutcome.GateDenied));
        }

        // C1: 일부가 고른 채 저장하고 불러와 이어도 같은 결과이고, 불러온 뒤에도 숨김이 그대로다
        [Test]
        public void SavingWhileSomeHavePickedGoesOn()
        {
            var straight = NewGame();
            Choose(straight, "A", Rock);
            Choose(straight, "B", Paper);
            Choose(straight, "C", Scissors);

            var w = NewGame();
            Choose(w, "A", Rock);
            var before = Render(w, "B");
            var loaded = World.Load(_content, w.Save(), requireViewPolicy: true);
            Assert.That(loaded.Diagnostics, Is.Empty);
            w = loaded.World!;
            Assert.That(Render(w, "B"), Is.EqualTo(before));
            Choose(w, "B", Paper);
            Choose(w, "C", Scissors);
            Assert.That(w.Save(), Is.EqualTo(straight.Save()));
        }
    }
}
