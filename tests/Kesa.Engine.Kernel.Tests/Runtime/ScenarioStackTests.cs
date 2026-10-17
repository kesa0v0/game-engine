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
    /// 검증 시나리오 ① "끼어들기 스택" (spec 7절): 한 참가자가 낸 효과가 대기열에 올라가고, 참가자들이 순서를 돌며 통과하거나
    /// 반응(새 효과를 올림)한다. 모두 연속으로 통과하면 위에서부터 해소되며 효과가 다른 대기 효과를 취소할 수 있다. 해소 중에도
    /// 질문이 열릴 수 있고 그 시점에 저장해도 이어서 진행된다. 코어를 고치지 않고 스크립트와 호스트 코드만으로 표현한다.
    /// 노드는 데이터 방식(G128): 대기 효과는 <c>Spell</c> 엔티티이고 해소 때 그 Def의 <c>Action</c> 함수를 부른다.
    /// 스택의 정책은 이 테스트가 박아 둔 것이다(<c>std.stack</c>이 아님): 우선권은 낸 사람의 다음 자리부터, 반응한 뒤에도
    /// 다음 자리로, 해소 뒤에는 처음 낸 사람부터, 참가자 수만큼 연속으로 통과하면 맨 위를 해소한다.
    /// </summary>
    public class ScenarioStackTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Life\", hp=field(int, default=20))\n" +
            "component(\"Seat\", next=ref(\"Party\", optional=True))\n" +
            "component(\"Pending\", by=ref(\"Party\", optional=True), target=ref(\"Party\", optional=True),\n" +
            "    victim=ref(\"Spell\", optional=True))\n" +
            "deftype(\"Party\", components=[\"Life\", \"Seat\"])\n" +
            "deftype(\"Spell\", effect=const(Action[\"Spell\"]), components=[\"Pending\"])\n";

        private const string Defs =
            "Party(\"Player\")\n" +
            "def bolt(s):\n" +
            "    s.Pending.target.Life.hp -= 3\n" +
            "def counter(s):\n" +
            "    if s.Pending.victim is not None:\n" +
            "        destroy(s.Pending.victim)\n" +
            "def charm(s):\n" +
            "    r = ask(s.Pending.by, \"charm\", choices=[\"heal\", \"drain\"])\n" +
            "    if r.index == 0:\n" +
            "        s.Pending.by.Life.hp += 2\n" +
            "    else:\n" +
            "        s.Pending.target.Life.hp -= 2\n" +
            "Spell(\"Bolt\", effect=bolt)\n" +
            "Spell(\"Counter\", effect=counter)\n" +
            "Spell(\"Charm\", effect=charm)\n" +
            "def top():\n" +
            "    t = None\n" +
            "    for s in query(\"Spell\"):\n" +
            "        t = s\n" +
            "    return t\n" +
            "def seats():\n" +
            "    return len(query(\"Party\"))\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Cast(by: Ref[Party], spell: DefId[Spell], target: Ref[Party]):\n" +
            "    spawn(spell, Pending=Pending(by=by, target=target))\n" +
            "    p = by.Seat.next\n" +
            "    passes = 0\n" +
            // 끝이 참가자들에게 달린 반복: 상한은 단계 예산이고, 바퀴마다 묻기 때문에 걸리지 않는다 (G195)
            "    while top() is not None:\n" +
            "        r = ask(p, \"priority\", responses=[\"Pass\", \"Zap\", \"Negate\"], data={\"top\": top()})\n" +
            "        if r.response == \"Pass\":\n" +
            "            passes += 1\n" +
            "            if passes == seats():\n" +
            "                s = top()\n" +
            "                s.Spell.effect(s)\n" +
            "                destroy(s)\n" +
            "                passes = 0\n" +
            "                p = by\n" +
            "            else:\n" +
            "                p = p.Seat.next\n" +
            "        else:\n" +
            "            passes = 0\n" +
            "            p = p.Seat.next\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Pass(party: Ref[Party]):\n" +
            "    pass\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Zap(party: Ref[Party], target: Ref[Party]):\n" +
            "    spawn(\"Bolt\", Pending=Pending(by=party, target=target))\n" +
            "@command(acts_for=lambda party: party)\n" +
            "def Negate(party: Ref[Party], victim: Ref[Spell]):\n" +
            "    spawn(\"Counter\", Pending=Pending(by=party, victim=victim))\n";

        private ContentLoadResult _content = null!;
        private EntityId _a, _b, _c;

        [SetUp]
        public void Content()
        {
            Mod("core", schema: Schema, defs: Defs);
            _content = LoadClean();
        }

        private World NewGame()
        {
            var w = new World(_content, seed: 1);
            _a = w.Create("Player");
            _b = w.Create("Player");
            _c = w.Create("Player");
            Seat(w, _a, _b);
            Seat(w, _b, _c);
            Seat(w, _c, _a);
            return w;
        }

        private static void Seat(World w, EntityId p, EntityId next) => w.Set(p, w.Member("Seat", "next"), new EntityValue(next));

        private long Hp(World w, EntityId p) => ((IntValue)w.Get(p, w.Member("Life", "hp"))).Value;

        private static Request Open(World w) => w.OpenRequests.Single();

        private static EntityId Asked(World w) => Open(w).Who.Single();

        private static IReadOnlyList<string> Stack(World w) =>
            w.Query("Spell").Select(s => w.DefOf(s)).ToList();

        private Request Cast(World w, EntityId by, string spell, EntityId target)
        {
            var r = w.Submit("host", "core.Cast", new Dictionary<string, Value>
            {
                ["by"] = new EntityValue(by), ["spell"] = new StrValue(spell), ["target"] = new EntityValue(target),
            });
            Assert.That(r.Succeeded, Is.True, r.Error?.ToString());
            return r.OpenedRequest!;
        }

        /// <summary>지금 질문받은 참가자가 응답 커맨드로 답한다.</summary>
        private static void Reply(World w, string command, params (string Name, EntityId Id)[] more)
        {
            var q = Open(w);
            var args = new Dictionary<string, Value> { ["party"] = new EntityValue(q.Who.Single()) };
            foreach (var (name, id) in more) args[name] = new EntityValue(id);
            var a = w.Respond("host", q.Id, "core." + command, args);
            Assert.That(a.Succeeded, Is.True, a.Message);
        }

        [Test]
        public void EveryonePassingInTurnResolvesTheTop()
        {
            var w = NewGame();
            Cast(w, _a, "Bolt", _b);
            Assert.That(Stack(w), Is.EqualTo(new[] { "Bolt" }));
            Assert.That(Asked(w), Is.EqualTo(_b), "priority starts at the next seat");
            Reply(w, "Pass");
            Assert.That(Asked(w), Is.EqualTo(_c));
            Reply(w, "Pass");
            Assert.That(Asked(w), Is.EqualTo(_a));
            Assert.That(Hp(w, _b), Is.EqualTo(20), "two passes are not enough");
            Reply(w, "Pass");
            Assert.That(Hp(w, _b), Is.EqualTo(17));
            Assert.That(Stack(w), Is.Empty);
            Assert.That(w.OpenRequests, Is.Empty);
        }

        [Test]
        public void AResponseGoesOnTopAndResolvesFirst()
        {
            var w = NewGame();
            Cast(w, _a, "Bolt", _b);
            Reply(w, "Zap", ("target", _a)); // b가 반응: a에게 3
            Assert.That(Stack(w), Is.EqualTo(new[] { "Bolt", "Bolt" }));
            Assert.That(Asked(w), Is.EqualTo(_c), "a response resets the passes and moves on");
            Reply(w, "Pass");
            Reply(w, "Pass");
            Assert.That(Hp(w, _a), Is.EqualTo(20));
            Reply(w, "Pass"); // b: 세 번 연속 통과, 맨 위(b의 Zap)부터
            Assert.That(Hp(w, _a), Is.EqualTo(17));
            Assert.That(Hp(w, _b), Is.EqualTo(20), "the first bolt still waits");
            Assert.That(Asked(w), Is.EqualTo(_a), "after resolving, priority goes back to the caster");
            Reply(w, "Pass");
            Reply(w, "Pass");
            Reply(w, "Pass");
            Assert.That(Hp(w, _b), Is.EqualTo(17));
            Assert.That(w.OpenRequests, Is.Empty);
        }

        [Test]
        public void AnEffectCanCancelAnotherPendingEffect()
        {
            var w = NewGame();
            Cast(w, _a, "Bolt", _b);
            var bolt = w.Query("Spell").Single();
            Reply(w, "Negate", ("victim", bolt)); // b가 a의 Bolt를 취소
            Reply(w, "Pass");
            Reply(w, "Pass");
            Reply(w, "Pass");
            Assert.That(Stack(w), Is.Empty, "the counter took the bolt with it");
            Assert.That(Hp(w, _b), Is.EqualTo(20));
            Assert.That(w.OpenRequests, Is.Empty);
        }

        /// <summary>해소 중에 Charm이 묻는다. 그 질문이 열린 채로 저장·불러와 이어도 저장 없이 한 것과 같다 (C1).</summary>
        [Test]
        public void AQuestionOpensWhileResolvingAndSavingThereGoesOn()
        {
            var straight = NewGame();
            PlayCharm(straight, stopAt: -1);

            for (int stop = 0; stop < 6; stop++)
            {
                var w = NewGame();
                var loaded = PlayCharm(w, stop);
                Assert.That(loaded.Save(), Is.EqualTo(straight.Save()), "saved at step " + stop);
                Assert.That(Hp(loaded, _a), Is.EqualTo(Hp(straight, _a)));
            }
            Assert.That(Hp(straight, _a), Is.EqualTo(17));
            Assert.That(Hp(straight, _c), Is.EqualTo(18));
        }

        /// <summary>a가 Charm(c), c가 Zap(a)로 반응. 맨 위 Zap부터 해소, 이어 Charm 해소 중 a에게 묻는다. stopAt 걸음에서 저장·불러온다.</summary>
        private World PlayCharm(World w, int stopAt)
        {
            int step = 0;
            World Maybe(World cur)
            {
                if (step++ != stopAt) return cur;
                var r = World.Load(_content, cur.Save());
                Assert.That(r.Diagnostics, Is.Empty);
                return r.World!;
            }

            Cast(w, _a, "Charm", _c);
            w = Maybe(w);
            Reply(w, "Pass");                 // b
            w = Maybe(w);
            Reply(w, "Zap", ("target", _a));  // c가 반응
            w = Maybe(w);
            Reply(w, "Pass");                 // a
            Reply(w, "Pass");                 // b
            Reply(w, "Pass");                 // c: Zap 해소, a 17
            w = Maybe(w);
            Reply(w, "Pass");                 // a
            Reply(w, "Pass");                 // b
            Reply(w, "Pass");                 // c: Charm 해소 시작, a에게 묻는다
            var q = Open(w);
            Assert.That(((StrValue)q.Title).Value, Is.EqualTo("charm"));
            Assert.That(q.Who.Single(), Is.EqualTo(_a));
            Assert.That(Stack(w), Is.EqualTo(new[] { "Charm" }), "the charm is still on the stack while it asks");
            // 멈춘 자리는 Def의 Action 함수 안이다: 저장에 커맨드 프레임 위로 charm 프레임이 있다
            Assert.That(System.Text.Encoding.UTF8.GetString(w.Save()), Does.Contain("\"function\": \"charm\""));
            w = Maybe(w);
            var a = w.Answer("host", Open(w).Id, _a, 1); // drain: c에게 2
            Assert.That(a.Succeeded, Is.True, a.Message);
            w = Maybe(w);
            Assert.That(Stack(w), Is.Empty);
            Assert.That(w.OpenRequests, Is.Empty);
            return w;
        }
    }
}
