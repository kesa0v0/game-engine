using System.Linq;
using Kesa.Engine.Diagnostics;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Loading
{
    /// <summary>
    /// 비밀이 Flow 지역 변수에 남는 꼴의 로드 경고 (spec-gaps G465): 숨긴 정보가 있는 게임에서, 난수나 가려지는 컴포넌트로 만든 값을 멈춤 뒤에도 쓰면
    /// 봇의 시험 판이 그 값을 그대로 본다.
    /// </summary>
    public class SecretLocalsTests : LoaderTestBase
    {
        private const string Schema =
            "component(\"Face\", city=field(int, default=0))\ncomponent(\"Seat\", n=field(int, default=0))\n" +
            "deftype(\"Card\", components=[\"Face\"])\ndeftype(\"Side\", components=[\"Seat\"])\n";

        private const string Hidden = "@view_rule(Face)\ndef down(viewer, entity):\n    return False\n";

        private const string Header = "Side(\"Red\")\nCard(\"Card\")\n@command(free=True, acts_for=None)\ndef Start(p: Ref[Side]):\n";

        private static string[] Warnings(System.Collections.Generic.IEnumerable<Diagnostic> diags) =>
            diags.Where(d => d.Code == Codes.CmdSecretLocal).Select(d => d.Message.Split('\'')[1]).ToArray();

        [Test]
        public void ADeckDrawnIntoALocalAndUsedAfterAskIsWarned()
        {
            Mod("core", schema: Schema, defs: Hidden + Header +
                "    deck = random.sample([1, 2, 3], 3)\n    i = 0\n" +
                "    while i < 3:\n        r = ask(p, \"turn\", choices=[1], name=\"turn\")\n        p.Seat.n = deck[i]\n        i += 1\n");
            Load(out var diags);
            Assert.That(Warnings(diags), Is.EqualTo(new[] { "deck" }));
            Assert.That(diags.Single(d => d.Code == Codes.CmdSecretLocal).Severity, Is.EqualTo(Severity.Warning));
        }

        [Test]
        public void AHiddenValueKeptAcrossAPauseIsWarned()
        {
            Mod("core", schema: Schema, defs: Hidden + Header +
                "    c = [x for x in query(Card)][0]\n    city = c.Face.city\n" +
                "    ask(p, \"turn\", choices=[1], name=\"turn\")\n    p.Seat.n = city\n" +
                // 엔티티를 들고 멈춘 뒤 그때 읽는 것은 걸지 않는다: 거른 복사가 가린다
                "    p.Seat.n = c.Face.city\n");
            Load(out var diags);
            Assert.That(Warnings(diags), Is.EqualTo(new[] { "city" }));
        }

        [Test]
        public void AFreshRollUsedBeforeTheNextPauseIsFine()
        {
            Mod("core", schema: Schema, defs: Hidden + Header +
                "    while True:\n        r = ask(p, \"turn\", choices=[1], name=\"turn\")\n" +
                "        roll = random.randint(1, 6)\n        p.Seat.n = roll\n");
            Load(out var diags);
            Assert.That(Warnings(diags), Is.Empty);
        }

        [Test]
        public void WithoutViewRulesNothingIsWarned()
        {
            Mod("core", schema: Schema, defs: Header +
                "    deck = random.sample([1, 2, 3], 3)\n    ask(p, \"turn\", choices=[1], name=\"turn\")\n    p.Seat.n = deck[0]\n");
            Load(out var diags);
            Assert.That(Warnings(diags), Is.Empty);
        }
    }
}
