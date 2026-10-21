using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 같은 엔티티 안의 <c>derived</c> 순환은 로드 오류다 (spec-gaps G229). 다른 엔티티를 거친 읽기는 데이터에 따라 순환이 되므로
    /// 로드 때 보지 않고 실행 중 재진입 감지에 맡긴다(G100).
    /// </summary>
    public class DerivedCycleTests : LoaderTestBase
    {
        private const string Types =
            "deftype(\"Region\", components=[\"Loop\", \"Tally\"])\n";

        private string[] Errors(string schema, string defs = "")
        {
            Mod("core", schema: schema, defs: defs);
            Load(out var diags);
            return diags.Where(d => d.Severity == Severity.Error)
                .Select(d => d.Code + " " + d.Span.Line + ": " + d.Message).ToArray();
        }

        private void Clean(string schema, string defs = "")
        {
            Mod("core", schema: schema, defs: defs);
            LoadClean();
        }

        [Test]
        public void TwoDerivedValuesThatReadEachOtherAreALoadError()
        {
            Mod("core", schema: "component(\"Loop\", a=derived(int, lambda h: h.b + 1), b=derived(int, lambda h: h.a))\n" +
                                "deftype(\"Unit\", components=[\"Loop\"])\n");
            var result = Load(out var diags);
            var d = diags.Single();
            Assert.That(d.Code, Is.EqualTo(Codes.SchDerivedCycle));
            Assert.That(d.Message, Is.EqualTo("derived 'Loop.a' reads itself on the same entity: Loop.a -> Loop.b -> Loop.a"));
            Assert.That(d.Hint, Is.EqualTo("this fails whenever it is read; split the formula in two (schema-design 3.3.1)"));
            Assert.That(d.Related.Single().Col, Is.EqualTo(81)); // b의 h.a
            Assert.That(result.Disabled, Is.EqualTo(new[] { "core" }));
        }

        [Test]
        public void ADerivedThatReadsItselfIsALoadError()
        {
            Assert.That(Errors("component(\"Loop\", a=derived(int, lambda h: h.a))\n" + "deftype(\"Unit\", components=[\"Loop\"])\n"),
                Is.EqualTo(new[] { "SCH009 1: derived 'Loop.a' reads itself on the same entity: Loop.a -> Loop.a" }));
        }

        [Test]
        public void SiblingsTheEntityAndEveryBranchCount()
        {
            // Loop.a → (조건식의 다른 갈래) Tally.sum → (h.entity를 거쳐) Loop.a
            Assert.That(Errors(
                "component(\"Loop\", on=field(int, default=0), a=derived(int, lambda h: 0 if h.on > 0 else h.Tally.sum))\n" +
                "component(\"Tally\", sum=derived(int, lambda t: t.entity.Loop.a + 1))\n" + Types), Is.EqualTo(new[]
            {
                "SCH009 1: derived 'Loop.a' reads itself on the same entity: Loop.a -> Tally.sum -> Loop.a",
            }));
        }

        [Test]
        public void AHelperThatGetsTheComponentIsFollowed()
        {
            // 함수 안에서 읽어도 오류 자리는 derived 함수 안의 그 호출이다
            Assert.That(Errors(
                "def total(h):\n    return h.Tally.sum\n" +
                "def count(h):\n    return total(h) + 1\n" +
                "component(\"Loop\", a=derived(int, count))\n" +
                "component(\"Tally\", sum=derived(int, lambda t: t.Loop.a))\n" + Types), Is.EqualTo(new[]
            {
                "SCH009 4: derived 'Loop.a' reads itself on the same entity: Loop.a -> Tally.sum -> Loop.a",
            }));
        }

        [Test]
        public void ReadsThroughOtherEntitiesAreLeftToTheRuntime()
        {
            // 하위 지역의 합(역방향), 부모(Ref), 모든 지역(query), 다른 키 줄: 데이터에 따라 순환이 아니다
            Clean(
                "component(\"Loop\", parent=ref(\"Region\", optional=True, inverse=\"children\"), pop=field(int, default=1),\n" +
                "    total=derived(int, lambda h: h.pop + sum(c.Loop.total for c in h.entity.children)),\n" +
                "    up=derived(int, lambda h: h.parent.Loop.up + 1 if h.parent else 0),\n" +
                "    all=derived(int, lambda h: sum(r.Loop.all for r in query(Region) if r != h.entity)))\n" +
                "component(\"Tally\")\n" +
                "deftype(\"SkillDef\")\n" +
                "component(\"Skill\", key=\"SkillDef\", bonus=derived(int, lambda s: s.entity.Skill.Cooking.bonus))\n" +
                "deftype(\"Region\", components=[\"Loop\", \"Tally\", \"Skill\"])\n",
                "SkillDef(\"Cooking\")\n");
        }

        [Test]
        public void ReadingFieldsAndConstsIsNotACycle()
        {
            Clean("component(\"Loop\", base=const(int, default=1), now=field(int, default=0), a=derived(int, lambda h: h.base + h.now),\n" +
                  "    b=derived(int, lambda h: h.a * 2))\n" + "component(\"Tally\")\n" + Types);
        }
    }
}
