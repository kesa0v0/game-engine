using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 함수 본문의 속성 이름 검사 (spec-gaps G228): 타입을 아는 값에 없는 이름은 로드 오류(<c>EVL001</c> + "혹시 …?")이고,
    /// 타입을 모르면 넘어간다. 이름이 맞는지는 실행과 같은 규칙이다.
    /// </summary>
    public class AttributeCheckerTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Food\", amount=field(int, default=0))\n" +
            "component(\"Life\", hp=field(int, default=5), max=derived(int, lambda h: h.hp + 1))\n" +
            "component(\"Wounded\", turns=field(int, default=1))\n" +
            "component(\"Skill\", key=\"SkillDef\", level=field(int, default=0))\n" +
            "component(\"Location\", city=ref(\"City\", optional=True, inverse=\"units\"))\n" +
            "component(\"Pending\", by=ref(\"Unit\", optional=True))\n" +
            "deftype(\"SkillDef\")\n" +
            "deftype(\"City\", components=[\"Food\"])\n" +
            "deftype(\"Unit\", cost=const(int, default=1), components=[\"Food\", \"Life\", \"Skill\", \"Location\"])\n" +
            "deftype(\"Spell\", effect=const(Action[\"Spell\"]), components=[\"Pending\"])\n" +
            "signal(\"Hit\", target=Ref[\"Unit\"], n=int)\n";

        private const string Data =
            "SkillDef(\"Medicine\")\n" +
            "City(\"Town\")\n" +
            "Unit(\"Grunt\")\n";

        private const string Free = "@command(free=True, acts_for=None)\n";

        /// <summary>속성 이름 오류(EVL001)의 메시지들.</summary>
        private string[] Errors(string defs, string? schema = null)
        {
            Mod("core", schema: schema ?? Schema, defs: Data + defs);
            Load(out var diags);
            return diags.Where(d => d.Severity == Severity.Error).Select(d => d.Code + " " + d.Span.Line + ": " + d.Message +
                                                                              (d.Hint == null ? "" : " | " + d.Hint)).ToArray();
        }

        private void Clean(string defs, string? schema = null)
        {
            Mod("core", schema: schema ?? Schema, defs: Data + defs);
            LoadClean();
        }

        [Test]
        public void ATypoAfterAQueryLoopIsALoadError()
        {
            Assert.That(Errors("def feed():\n    for u in query(\"Unit\"):\n        u.Fod.amount += 1\n"), Is.EqualTo(new[]
            {
                "EVL001 6: a Unit entity has no component or collection 'Fod' | did you mean 'Food'?",
            }));
        }

        [Test]
        public void ATypoOnAnAnnotatedParameterIsALoadError()
        {
            Assert.That(Errors(Free + "def Feed(u: Ref[Unit]):\n    u.Food.amout += 1\n"), Is.EqualTo(new[]
            {
                "EVL001 6: 'Food' has no member, sibling component or property 'amout' | did you mean 'amount'?",
            }));
        }

        [Test]
        public void ATypoInADerivedFunctionIsALoadError()
        {
            var schema = Schema.Replace("lambda h: h.hp + 1", "lambda h: h.Lfe.hp");
            Assert.That(Errors("", schema), Is.EqualTo(new[]
            {
                "EVL001 2: 'Life' has no member, sibling component or property 'Lfe' | did you mean 'Life'?",
            }));
        }

        [Test]
        public void AFunctionInAnActionSlotTakesThatTypesEntity()
        {
            // 타입을 적지 않은 s도 Action["Spell"] 칸에 들어가므로 Spell이다
            Assert.That(Errors("def bolt(s):\n    s.Pendng.by.Life.hp -= 3\nSpell(\"Bolt\", effect=bolt)\n"), Is.EqualTo(new[]
            {
                "EVL001 5: a Spell entity has no component or collection 'Pendng' | did you mean 'Pending'?",
            }));
        }

        [Test]
        public void ADefConstAfterTheTypeNameIsChecked()
        {
            Assert.That(Errors(Free + "def Buy(u: Ref[Unit]):\n    return u.Unit.cst\n"), Is.EqualTo(new[]
            {
                "EVL001 6: Def type 'Unit' has no const 'cst' | did you mean 'cost'?",
            }));
        }

        [Test]
        public void AWriteTargetIsCheckedLikeARead()
        {
            Assert.That(Errors(Free + "def Set(u: Ref[Unit]):\n    u.Life.hpp = 1\n"), Is.EqualTo(new[]
            {
                "EVL001 6: 'Life' has no member, sibling component or property 'hpp' | did you mean 'hp'?",
            }));
        }

        [Test]
        public void ALocalTakesTheTypeOfWhatIsAssignedAndNoneDoesNotHideIt()
        {
            // t는 None 또는 Spell: Spell로 본다. c = u.Food처럼 다른 지역 변수에서 온 값도 따라간다
            Assert.That(Errors(
                "def top():\n    t = None\n    for s in query(\"Spell\"):\n        t = s\n    if t is not None:\n        t.Spel.effect(t)\n" +
                "def feed():\n    for u in query(\"Unit\"):\n        c = u.Food\n        c.amont = 1\n"), Is.EqualTo(new[]
            {
                "EVL001 9: a Spell entity has no component or collection 'Spel' | did you mean 'Spell'?",
                "EVL001 13: 'Food' has no member, sibling component or property 'amont' | did you mean 'amount'?",
            }));
        }

        [Test]
        public void EntryPointsGiveTheirParametersTypes()
        {
            Assert.That(Errors(
                "@on(Hit)\ndef hurt(target, n):\n    target.Life.hp -= n\n    target.Lif.hp = 0\n" +
                "@when(lambda u: u.Food.amout > 3)\ndef full(u: Ref[Unit]):\n    pass\n" +
                "@command(acts_for=lambda u: u.Locaton.city)\ndef Go(u: Ref[Unit]):\n    pass\n"), Is.EqualTo(new[]
            {
                "EVL001 7: a Unit entity has no component or collection 'Lif' | did you mean 'Life'?",
                "EVL001 8: 'Food' has no member, sibling component or property 'amout' | did you mean 'amount'?",
                "EVL001 11: a Unit entity has no component or collection 'Locaton' | did you mean 'Location'?",
            }));
        }

        [Test]
        public void ChooseWhereTakesTheCandidatesType()
        {
            Assert.That(Errors(Free + "def Pick(u: Ref[Unit]):\n    with preconditions:\n        choose(u, where=lambda x: x.Fod.amount > 0)\n"),
                Is.EqualTo(new[] { "EVL001 7: a Unit entity has no component or collection 'Fod' | did you mean 'Food'?" }));
        }

        [Test]
        public void SpawnComprehensionsAndInversesKeepTheType()
        {
            Assert.That(Errors(Free +
                "def Make(c: Ref[City]):\n    u = spawn(\"Grunt\")\n    u.Lfe.hp = 1\n" +
                "    rich = [x for x in query(\"Unit\") if x.Food.amont > 1]\n    for r in rich:\n        r.Foodd.amount = 0\n" +
                "    for v in c.units:\n        v.Wonded.turns = 1\n    n = len(c.unts)\n"), Is.EqualTo(new[]
            {
                "EVL001 7: a Unit entity has no component or collection 'Lfe' | did you mean 'Life'?",
                "EVL001 8: 'Food' has no member, sibling component or property 'amont' | did you mean 'amount'?",
                "EVL001 10: a Unit entity has no component or collection 'Foodd' | did you mean 'Food'?",
                "EVL001 12: an entity has no component or collection 'Wonded' | did you mean 'Wounded'?",
                "EVL001 13: a City entity has no component or collection 'unts' | did you mean 'units'?",
            }));
        }

        [Test]
        public void KeyedRowsAreRowsAndTheirMembersAreChecked()
        {
            Assert.That(Errors(Free + "def Train(u: Ref[Unit], s: DefId[SkillDef]):\n    u.Skill.Medicine.level += 1\n" +
                               "    u.Skill[s].levl += 1\n    for k in u.Skill:\n        pass\n"), Is.EqualTo(new[]
            {
                "EVL001 7: 'Skill' has no member, sibling component or property 'levl' | did you mean 'level'?",
            }));
        }

        [Test]
        public void WhatTheRuntimeAcceptsIsAccepted()
        {
            // 붙지 않았을 수 있는 컴포넌트(None), 다른 Def 타입 이름(None), entity·key·형제, Def 상수, 역방향 컬렉션
            Clean(Free + "def Look(u: Ref[Unit], c: Ref[City]):\n" +
                  "    a = u.Wounded is not None and u.Spell is None\n" +
                  "    b = u.Life.entity.Food.amount + u.Food.Life.max + u.Life.Unit.cost + len(c.units)\n" +
                  "    for k in u.Skill:\n        r = u.Skill[k]\n        x = r.key\n        y = r.entity.Unit.cost\n" +
                  "    return 0\n");
        }

        [Test]
        public void UnknownTypesAreLeftAlone()
        {
            // 적지 않은 매개변수, 함수의 결과, 리스트 원소, 타입이 다른 문맥 둘에서 쓰인 함수는 모른다
            Clean("def f(x):\n    return x.anything\n" +
                  "def g():\n    y = f(1)\n    return y.whatever\n" +
                  Free + "def H(u: Ref[Unit]):\n    return [u.Food][0].nope\n",
                Schema + "def both(h):\n    return h.amount\n" + "component(\"Stock\", amount=field(int, default=0), twice=derived(int, both))\n" +
                "component(\"Mana\", now=field(int, default=0), twice=derived(int, both))\n");
        }

        [Test]
        public void AnAnnotationBeatsTheContextAndANonEntityAnnotationIsUnknown()
        {
            Assert.That(Errors("def bolt(s: Ref[Unit]):\n    s.Pendng.by = None\nSpell(\"Bolt\", effect=bolt)\n" +
                               "def count(n: int):\n    return n.whatever\n"), Is.EqualTo(new[]
            {
                "EVL001 5: a Unit entity has no component or collection 'Pendng' | did you mean 'Pending'?",
            }));
        }

        [Test]
        public void TheErrorDisablesTheModThatWroteTheFunction()
        {
            Mod("core", schema: Schema, defs: Data);
            Mod("addon", defs: "def feed():\n    for u in query(\"Unit\"):\n        u.Fod.amount += 1\n", extra: ", dependencies=[\"core\"]");
            var result = Load(out var diags);
            Assert.That(diags.Single(d => d.Severity == Severity.Error).Mod, Is.EqualTo("addon"));
            Assert.That(result.Disabled, Is.EqualTo(new[] { "addon" }));
        }
    }
}
