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
    /// 검증 시나리오 ③ "단일 플레이어 + AI + 틱 이벤트" (spec 7절): 엔진이 합법 수를 나열하고(<c>LegalMoves</c>, G184) AI가 고르며,
    /// 틱마다 반응 규칙이 발동한다(<c>@when(repeat=True)</c>, G173). 코어를 고치지 않고 스크립트와 호스트 코드만으로 표현한다.
    /// </summary>
    public class ScenarioAiTickTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Treasury\", gold=field(int, default=0, min=0))\n" +
            "component(\"Map\", east=ref(\"City\", optional=True), west=ref(\"City\", optional=True))\n" +
            "component(\"Loot\", gold=field(int, default=0, min=0))\n" +
            "component(\"Owner\", party=ref(\"Party\", optional=True))\n" +
            "component(\"Place\", city=ref(\"City\", optional=True))\n" +
            "component(\"Moves\", left=field(int, default=1, min=0))\n" +
            "deftype(\"Party\", components=[\"Treasury\"])\n" +
            "deftype(\"City\", components=[\"Map\", \"Loot\"])\n" +
            "deftype(\"Unit\", components=[\"Owner\", \"Place\", \"Moves\"])\n";

        private const string Defs =
            "Party(\"Side\")\nCity(\"Town\")\nUnit(\"Soldier\")\n" +
            "def exits(c):\n" +
            "    out = []\n" +
            "    if c.Map.west is not None:\n" +
            "        out = out + [c.Map.west]\n" +
            "    if c.Map.east is not None:\n" +
            "        out = out + [c.Map.east]\n" +
            "    return out\n" +
            "@command(free=True, acts_for=None)\n" +
            "def Move(party: Ref[Party], unit: Ref[Unit], dest: Ref[City]):\n" +
            "    with preconditions:\n" +
            "        choose(unit, where=lambda u: u.Owner.party == party)\n" +
            "        here = unit.Place.city\n" +
            "        choose(dest, from_=exits(here))\n" +
            "        require(unit.Moves.left > 0, text(\"msg.tired\"))\n" +
            "        cost(party.Treasury.gold, 1)\n" +
            "    unit.Place.city = dest\n" +
            "    unit.Moves.left -= 1\n" +
            "    party.Treasury.gold += dest.Loot.gold\n" +
            "    dest.Loot.gold = 0\n" +
            // 틱 끝마다: 지친 유닛은 쉬고, 비어 가는 도시는 약탈물이 다시 자란다 (난수, G178)
            "@when(lambda u: u.Moves.left < 1, repeat=True)\n" +
            "def rest(u: Ref[Unit]):\n" +
            "    u.Moves.left = 1\n" +
            "@when(lambda c: c.Loot.gold < 3, repeat=True)\n" +
            "def regrow(c: Ref[City]):\n" +
            "    c.Loot.gold += random.randint(0, 2)\n";

        private ContentLoadResult _content = null!;
        private EntityId _player, _ai, _a, _b, _c, _hero, _raider, _guard;

        [SetUp]
        public void Content()
        {
            Mod("core", schema: Schema, defs: Defs);
            _content = LoadClean();
        }

        private World NewGame()
        {
            var w = new World(_content, seed: 7);
            _player = w.Create("core:Side");
            _ai = w.Create("core:Side");
            _a = w.Create("core:Town");
            _b = w.Create("core:Town");
            _c = w.Create("core:Town");
            Link(w, _a, "east", _b);
            Link(w, _b, "west", _a);
            Link(w, _b, "east", _c);
            Link(w, _c, "west", _b);
            w.Set(_b, w.Member("Loot", "gold"), new IntValue(2));
            w.Set(_c, w.Member("Loot", "gold"), new IntValue(1));
            _hero = Unit(w, _player, _a);
            _raider = Unit(w, _ai, _c);
            _guard = Unit(w, _ai, _b);
            foreach (var p in new[] { _player, _ai }) w.Set(p, w.Member("Treasury", "gold"), new IntValue(2));
            return w;
        }

        private static void Link(World w, EntityId from, string side, EntityId to) =>
            w.Set(from, w.Member("Map", side), new EntityValue(to));

        private static EntityId Unit(World w, EntityId party, EntityId city)
        {
            var u = w.Create("core:Soldier");
            w.Set(u, w.Member("Owner", "party"), new EntityValue(party));
            w.Set(u, w.Member("Place", "city"), new EntityValue(city));
            return u;
        }

        private static IReadOnlyList<IReadOnlyDictionary<string, Value>> Legal(World w, EntityId party) =>
            w.LegalMoves("core.Move", new Dictionary<string, Value> { ["party"] = new EntityValue(party) }, 100).Moves;

        private static EntityId Id(IReadOnlyDictionary<string, Value> move, string name) => ((EntityValue)move[name]).Id;

        private static long Gold(World w, EntityId e, string component) => ((IntValue)w.Get(e, w.Member(component, "gold"))).Value;

        /// <summary>AI: 약탈물이 가장 많은 도시로 가는 수 (같으면 엔진이 준 순서의 처음). 낼 수가 없으면 쉰다.</summary>
        private static IReadOnlyDictionary<string, Value>? AiPick(World w, EntityId party)
        {
            var moves = Legal(w, party);
            if (moves.Count == 0) return null;
            var best = moves[0];
            foreach (var m in moves)
                if (Gold(w, Id(m, "dest"), "Loot") > Gold(w, Id(best, "dest"), "Loot")) best = m;
            return best;
        }

        /// <summary>한 틱: 플레이어는 첫 합법 수, AI는 낼 수 있는 동안 계속 고른다. 그다음 틱을 넘긴다.</summary>
        private static string PlayTick(World w, EntityId player, EntityId ai)
        {
            int moves = 0;
            var mine = Legal(w, player);
            if (mine.Count > 0)
            {
                Assert.That(w.Submit("host", "core.Move", mine[0]).Succeeded, Is.True);
                moves++;
            }
            for (var pick = AiPick(w, ai); pick != null; pick = AiPick(w, ai))
            {
                Assert.That(w.Submit("host", "core.Move", pick).Succeeded, Is.True);
                moves++;
            }
            var reactions = w.Advance().Reactions.Select(r => r.Handler).Distinct().OrderBy(h => h, System.StringComparer.Ordinal);
            return moves + " " + string.Join(",", reactions) + " gold " + Gold(w, player, "Treasury") + "/" + Gold(w, ai, "Treasury");
        }

        [Test]
        public void TheEngineListsOnlyTheMovesThePreconditionsAllow()
        {
            var w = NewGame();
            var ai = Legal(w, _ai);
            // AI의 유닛만(남의 유닛 hero는 없음), 지금 도시의 출구로만: raider(C) → B, guard(B) → A·C
            Assert.That(ai.Select(m => (Id(m, "unit"), Id(m, "dest"))), Is.EqualTo(new[]
            {
                (_raider, _b), (_guard, _a), (_guard, _c),
            }));
            Assert.That(Legal(w, _player).Select(m => Id(m, "dest")), Is.EqualTo(new[] { _b }));
        }

        [Test]
        public void TheAiPicksAndTheTickReactionsGiveTheMovesBack()
        {
            var w = NewGame();
            // guard(B) → C(약탈 1)가 A(0)보다 낫고, raider(C) → B(2)가 가장 낫다
            var first = AiPick(w, _ai)!;
            Assert.That((Id(first, "unit"), Id(first, "dest")), Is.EqualTo((_raider, _b)));
            Assert.That(w.Submit("host", "core.Move", first).Succeeded, Is.True);
            Assert.That(Gold(w, _ai, "Treasury"), Is.EqualTo(2 - 1 + 2));

            var second = AiPick(w, _ai)!;
            Assert.That(w.Submit("host", "core.Move", second).Succeeded, Is.True);
            Assert.That(Legal(w, _ai), Is.Empty, "both units are tired until the tick ends");

            var advanced = w.Advance();
            Assert.That(advanced.Tick, Is.EqualTo(1));
            Assert.That(Legal(w, _ai), Is.Not.Empty, "rest ran at the end of the tick");
        }

        [Test]
        public void AnEmptyTreasuryLeavesNoLegalMove()
        {
            var w = NewGame();
            w.Set(_player, w.Member("Treasury", "gold"), new IntValue(0));
            Assert.That(Legal(w, _player), Is.Empty);
            var report = w.CheckPreconditions("core.Move", new Dictionary<string, Value>
            {
                ["party"] = new EntityValue(_player), ["unit"] = new EntityValue(_hero), ["dest"] = new EntityValue(_b),
            });
            Assert.That(report.Failures.Single().Kind, Is.EqualTo(PreconditionKind.Cost));
        }

        [Test]
        public void TheSameSeedPlaysTheSameGame()
        {
            var one = NewGame();
            var log = new List<string>();
            for (int t = 0; t < 6; t++) log.Add(PlayTick(one, _player, _ai));
            var two = NewGame();
            for (int t = 0; t < 6; t++) PlayTick(two, _player, _ai);
            Assert.That(two.Save(), Is.EqualTo(one.Save()));
            Assert.That(one.Tick, Is.EqualTo(6));
            // 틱마다 낸 수, 틱 끝에 돈 반응, 플레이어/AI의 금 (시드 7). 이동마다 1을 내므로 금이 바닥난 AI는 수를 덜 낸다
            Assert.That(log, Is.EqualTo(new[]
            {
                "3 core.regrow,core.rest gold 3/1",
                "3 core.regrow,core.rest gold 2/2",
                "3 core.regrow,core.rest gold 1/2",
                "3 core.regrow,core.rest gold 2/1",
                "2 core.regrow,core.rest gold 1/0",
                "1 core.regrow,core.rest gold 2/0",
            }));
        }

        [Test]
        public void SavingMidGameAndLoadingPlaysOnTheSame()
        {
            var straight = NewGame();
            for (int t = 0; t < 6; t++) PlayTick(straight, _player, _ai);

            var first = NewGame();
            for (int t = 0; t < 3; t++) PlayTick(first, _player, _ai);
            var loaded = World.Load(_content, first.Save());
            Assert.That(loaded.Diagnostics, Is.Empty);
            var resumed = loaded.World!;
            for (int t = 3; t < 6; t++) PlayTick(resumed, _player, _ai);
            Assert.That(resumed.Save(), Is.EqualTo(straight.Save()));
        }
    }
}
