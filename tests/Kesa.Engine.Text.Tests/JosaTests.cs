using System.Collections.Generic;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Text.Tests
{
    /// <summary>조사 함수 <c>JOSA</c>와 Def 속성으로 고르기 <c>DEFATTR</c> (spec-gaps G268).</summary>
    public class JosaTests : TextTestBase
    {
        [TestCase("슬루프", "을", "슬루프를")]
        [TestCase("갈레온", "를", "갈레온을")]
        [TestCase("배", "이", "배가")]
        [TestCase("돛", "가", "돛이")]
        [TestCase("배", "은", "배는")]
        [TestCase("돛", "와", "돛과")]
        [TestCase("길", "으로", "길로")]
        [TestCase("집", "로", "집으로")]
        [TestCase("배", "으로", "배로")]
        [TestCase("철수", "아", "철수야")]
        [TestCase("영희", "이나", "영희나")]
        [TestCase("3", "을", "3을")]
        [TestCase("2", "을", "2를")]
        [TestCase("10", "를", "10을")]
        [TestCase("100", "와", "100과")]
        [TestCase("1000", "이", "1000이")]
        [TestCase("20000", "가", "20000이")]
        [TestCase("0", "는", "0은")]
        [TestCase("4", "은", "4는")]
        [TestCase("1.5", "을", "1.5를")]
        [TestCase("1.10", "을", "1.10을")]
        [TestCase("Sloop", "을", "Sloop을(를)")]
        [TestCase("Sloop", "로", "Sloop으로(로)")]
        [TestCase("슬루프", "에게", "슬루프에게")]
        public void TheParticleFollowsTheLastSound(string name, string particle, string expected)
        {
            Assert.That(Josa.Attach(name, particle), Is.EqualTo(expected));
        }

        [Test]
        public void TranslationsCallJosaAndDefAttrOnDefsEntitiesAndNumbers()
        {
            Mod("harbor", ", defaultLanguage=\"ko\"",
                schema: "component(\"Hull\", hp=field(int, default=1))\ndeftype(\"Ship\", components=[\"Hull\"])\n",
                defs: "Ship(\"Sloop\")\nShip(\"Galleon\")\n");
            Ftl("harbor", "ko",
                "def-harbor-Sloop = 슬루프\n    .gender = f\n" +
                "def-harbor-Galleon = 갈레온\n" +
                "harbor-bought = { JOSA($ship, \"을\") } 샀다\n" +
                "harbor-pronoun = { DEFATTR($ship, \"gender\") ->\n    [f] 그녀\n   *[other] 그것\n}\n" +
                "harbor-left = { JOSA($n, \"이\") } 남았다\n" +
                "harbor-who = { JOSA($who, \"은\") } { DEFATTR($who, \"nope\") }끝\n");
            var errors = new List<string>();
            var tr = LoadTextsClean().For(new[] { "ko" }, errors.Add);
            string Say(string key, string name, Value v, Dictionary<string, string>? defs = null) =>
                tr.Format(new TextValue(key, new Dictionary<string, Value> { [name] = v }, defs));

            Assert.That(Say("harbor-bought", "ship", new StrValue("harbor:Sloop")), Is.EqualTo("슬루프를 샀다"));
            Assert.That(Say("harbor-bought", "ship", new StrValue("harbor:Galleon")), Is.EqualTo("갈레온을 샀다"));
            Assert.That(Say("harbor-pronoun", "ship", new StrValue("harbor:Sloop")), Is.EqualTo("그녀"));
            Assert.That(Say("harbor-pronoun", "ship", new StrValue("harbor:Galleon")), Is.EqualTo("그것"), "no attribute: the default branch");
            Assert.That(Say("harbor-pronoun", "ship", new StrValue("plain")), Is.EqualTo("그것"));
            Assert.That(Say("harbor-left", "n", new IntValue(3)), Is.EqualTo("3이 남았다"));
            Assert.That(Say("harbor-who", "who", new EntityValue(new EntityId(7)),
                new Dictionary<string, string> { ["who"] = "harbor:Galleon" }), Is.EqualTo("갈레온은 끝"));
            Assert.That(errors, Is.Empty, "a missing attribute read by DEFATTR is not an error");
        }
    }
}
