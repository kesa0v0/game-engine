using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>경계 값과 그 정규형 JSON (spec-gaps G326).</summary>
    public class BridgeJsonTests
    {
        private static BridgeValue S(string s) => BridgeValue.Of(s);
        private static BridgeValue I(long n) => BridgeValue.Of(n);
        private static KeyValuePair<BridgeValue, BridgeValue> P(BridgeValue k, BridgeValue v) => new KeyValuePair<BridgeValue, BridgeValue>(k, v);
        private static KeyValuePair<string, BridgeValue> A(string k, BridgeValue v) => new KeyValuePair<string, BridgeValue>(k, v);

        [Test]
        public void EachKindHasOneWriting()
        {
            Assert.That(BridgeJson.Write(BridgeNone.Instance), Is.EqualTo("null"));
            Assert.That(BridgeJson.Write(BridgeValue.Of(true)), Is.EqualTo("true"));
            Assert.That(BridgeJson.Write(I(-7)), Is.EqualTo("-7"));
            Assert.That(BridgeJson.Write(new BridgeFixed(1_500_000)), Is.EqualTo("{\"fixed\":1500000}"));
            Assert.That(BridgeJson.Write(S("a\"b\\c\n\u0001")), Is.EqualTo("\"a\\\"b\\\\c\\n\\u0001\""));
            Assert.That(BridgeJson.Write(new BridgeEntity(4, "pandemic:Alice")), Is.EqualTo("{\"entity\":4,\"def\":\"pandemic:Alice\"}"));
            Assert.That(BridgeJson.Write(new BridgeEntity(4, null)), Is.EqualTo("{\"entity\":4}"), "a Def nobody knows any more");
            Assert.That(BridgeJson.Write(BridgeHidden.Instance), Is.EqualTo("{\"hidden\":true}"));
            Assert.That(BridgeJson.Write(new BridgeList(new[] { I(1), S("x") })), Is.EqualTo("[1,\"x\"]"));
            Assert.That(BridgeJson.Write(new BridgeDict(new[] { P(S("b"), I(1)), P(S("a"), I(2)) })),
                Is.EqualTo("{\"dict\":[[\"b\",1],[\"a\",2]]}"), "in the order the items went in");
            Assert.That(BridgeJson.Write(new BridgeOption(S("yes"), label: S("ok-key"))),
                Is.EqualTo("{\"option\":\"yes\",\"label\":\"ok-key\",\"desc\":null,\"style\":null,\"data\":null}"));
        }

        [Test]
        public void TextArgumentsAreInNameOrder()
        {
            var text = new BridgeText("brink-treat-first", new[] { A("z", I(1)), A("a", new BridgeEntity(2, "pandemic:Bob")) });
            Assert.That(BridgeJson.Write(text),
                Is.EqualTo("{\"text\":\"brink-treat-first\",\"args\":{\"a\":{\"entity\":2,\"def\":\"pandemic:Bob\"},\"z\":1}}"));
            Assert.That(text, Is.EqualTo(new BridgeText("brink-treat-first", new[] { A("a", new BridgeEntity(2, "pandemic:Bob")), A("z", I(1)) })));
        }

        [Test]
        public void WhatIsWrittenReadsBackTheSame()
        {
            var value = new BridgeDict(new[]
            {
                P(S("draw"), I(3)),
                P(S("known"), new BridgeList(new[] { S("pandemic:Atlanta"), S("한국어 \t") })),
                P(I(7), new BridgeList(new BridgeValue[] { BridgeHidden.Instance, new BridgeEntity(9, null), new BridgeFixed(-250_000), BridgeNone.Instance })),
                P(S("title"), new BridgeText("k", new[] { A("n", I(5)) })),
                P(S("pick"), new BridgeOption(BridgeNone.Instance, S("l"), new BridgeText("d", new KeyValuePair<string, BridgeValue>[0]), "purple-fire",
                    new BridgeDict(new KeyValuePair<BridgeValue, BridgeValue>[0]))),
            });
            string json = BridgeJson.Write(value);
            var back = BridgeJson.Read(json);
            Assert.That(back, Is.EqualTo(value));
            Assert.That(BridgeJson.Write(back), Is.EqualTo(json), "the same value is the same text");
        }

        [Test]
        public void ReadingAllowsSpaces()
        {
            Assert.That(BridgeJson.Read(" { \"entity\" : 3 , \"def\" : \"m:X\" } "), Is.EqualTo(new BridgeEntity(3, "m:X")));
            Assert.That(BridgeJson.Read("[ 1 , [ ] , { \"dict\" : [ ] } ]").AsList, Has.Count.EqualTo(3));
        }

        [TestCase("1.5", "whole numbers")]
        [TestCase("{\"float\":1}", "unknown value tag")]
        [TestCase("[1] 2", "after the value")]
        [TestCase("{\"dict\":[[[1],2]]}", "dict key")]
        [TestCase("{\"text\":\"k\",\"argz\":{}}", "'args'")]
        [TestCase("\"open", "unterminated")]
        public void BadTextIsAFormatError(string json, string message)
        {
            var e = Assert.Throws<FormatException>(() => BridgeJson.Read(json));
            Assert.That(e!.Message, Does.Contain(message));
        }

        [Test]
        public void AHostReadsTheFieldsItBindsTo()
        {
            var value = BridgeJson.Read("{\"dict\":[[\"draw\",3],[\"known\",[\"pandemic:Atlanta\"]],[\"player\",{\"entity\":2,\"def\":\"pandemic:Alice\"}]]}");
            Assert.That(value["draw"].AsInt, Is.EqualTo(3));
            Assert.That(value["known"].AsList[0].AsString, Is.EqualTo("pandemic:Atlanta"));
            Assert.That(value["player"].AsEntity.Def, Is.EqualTo("pandemic:Alice"));
            Assert.Throws<KeyNotFoundException>(() => _ = value["nope"]);
            var e = Assert.Throws<InvalidOperationException>(() => _ = value["draw"].AsString);
            Assert.That(e!.Message, Does.Contain("expected a Str value, got Int"));
        }

        [Test]
        public void AnEntityIsTheSameEntityWhateverDefItCarries()
        {
            Assert.That(new BridgeEntity(5, "m:A"), Is.EqualTo(new BridgeEntity(5, null)));
            Assert.That(new BridgeEntity(5, "m:A"), Is.Not.EqualTo(new BridgeEntity(6, "m:A")));
        }
    }
}
