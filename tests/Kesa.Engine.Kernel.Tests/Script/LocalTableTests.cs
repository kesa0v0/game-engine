using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Tests.Script
{
    /// <summary>지역 변수 표: 넣은 순서와 상관없이 이름 순(서수)으로 돌고, 지우기·복제가 서로 섞이지 않는다.</summary>
    public class LocalTableTests
    {
        private static Value I(long n) => new IntValue(n);

        private static List<string> Names(IEnumerable<KeyValuePair<string, Value>> pairs) => pairs.Select(p => p.Key).ToList();

        [Test]
        public void ItIteratesInOrdinalNameOrder()
        {
            var t = new LocalTable();
            foreach (var name in new[] { "zeta", "Alpha", "beta", "a", "b2", "b10" }) t[name] = I(name.Length);
            Assert.That(Names(t), Is.EqualTo(new[] { "Alpha", "a", "b10", "b2", "beta", "zeta" }));
            Assert.That(t.Keys, Is.EqualTo(new[] { "Alpha", "a", "b10", "b2", "beta", "zeta" }));
            Assert.That(t.Count, Is.EqualTo(6));
        }

        [Test]
        public void SettingAgainReplacesAndRemoveForgets()
        {
            var t = new LocalTable();
            t["x"] = I(1);
            t["y"] = I(2);
            t["z"] = I(3);
            t["x"] = I(10);
            Assert.That(t.Remove("x"), Is.True);
            Assert.That(t.Remove("x"), Is.False);
            Assert.That(t.TryGetValue("x", out _), Is.False);
            Assert.That(((IntValue)t["z"]).Value, Is.EqualTo(3));
            Assert.That(Names(t), Is.EqualTo(new[] { "y", "z" }));
            Assert.Throws<KeyNotFoundException>(() => _ = t["x"]);
        }

        [Test]
        public void AClonedTableIsSeparate()
        {
            var t = new LocalTable();
            t["a"] = I(1);
            var copy = t.Clone();
            copy["a"] = I(2);
            copy["b"] = I(3);
            Assert.That(((IntValue)t["a"]).Value, Is.EqualTo(1));
            Assert.That(t.ContainsKey("b"), Is.False);
            Assert.That(Names(copy), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void ANameBuiltAtRunTimeFindsTheSameLocal()
        {
            var t = new LocalTable();
            t["count"] = I(1);
            string built = new string(new[] { 'c', 'o', 'u', 'n', 't' });
            Assert.That(t.TryGetValue(built, out var v) && ((IntValue)v).Value == 1, Is.True);
        }

        [Test]
        public void AFixedLayoutNumbersItsNamesInOrdinalOrderAndRefusesOthers()
        {
            var layout = new FrameLayout(new[] { "zeta", "a", "Beta" });
            Assert.That(layout.SlotOf("Beta"), Is.EqualTo(0));
            Assert.That(layout.SlotOf("a"), Is.EqualTo(1));
            Assert.That(layout.SlotOf("zeta"), Is.EqualTo(2));
            var t = new LocalTable(layout);
            t.Set(2, I(1));
            t.Set(0, I(2));
            Assert.That(Names(t), Is.EqualTo(new[] { "Beta", "zeta" }));
            Assert.That(t.At(1), Is.Null);
            Assert.That(t.TrySet("other", I(3)), Is.False);
            Assert.That(t.Count, Is.EqualTo(2));
        }

        [Test]
        public void AChildTableCarriesTheParentValuesIntoItsOwnSlots()
        {
            var parent = new FrameLayout(new[] { "m", "x" });
            var site = new object();
            var child = parent.Child(site, new[] { "b", "x" }); // x는 바깥 이름과 같은 칸
            Assert.That(parent.Child(site, new[] { "ignored" }), Is.SameAs(child));
            var t = new LocalTable(parent);
            t["x"] = I(7);
            var c = t.ChildTable(child);
            Assert.That(Names(c), Is.EqualTo(new[] { "x" }));
            Assert.That(((IntValue)c["x"]).Value, Is.EqualTo(7));
            c["b"] = I(1);
            c["x"] = I(8);
            Assert.That(((IntValue)t["x"]).Value, Is.EqualTo(7));
            Assert.That(child.Names(), Is.EqualTo(new[] { "b", "m", "x" }));
        }
    }
}
