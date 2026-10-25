using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge.Local;
using Kesa.Engine.Diagnostics;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;
using NUnit.Framework;

namespace Kesa.Engine.Bridge.Tests
{
    /// <summary>커널 값과 경계 값 바꾸기 (spec-gaps G326).</summary>
    public class KernelValuesTests
    {
        private static readonly Func<EntityId, string?> Defs = id => id.Value == 1 ? "pandemic:Alice" : id.Value == 2 ? "pandemic:Bob" : null;

        private static DictValue Dict(params (Value Key, Value Value)[] items)
        {
            var list = new List<DictItem>();
            foreach (var (k, v) in items) list.Add(new DictItem(k, v, SourceSpan.None));
            return new DictValue(list);
        }

        [Test]
        public void AScreenValueCrossesWithTheDefsOfItsEntities()
        {
            var value = new ListValue(new Value[]
            {
                Dict((new StrValue("player"), new EntityValue(new EntityId(1))), (new StrValue("left"), new IntValue(4))),
                Dict((new StrValue("player"), HiddenValue.Instance), (new StrValue("rate"), new FixedValue(Fixed.FromRaw(2_500_000)))),
                new EntityValue(new EntityId(3)),
            });
            var bridged = KernelValues.ToBridge(value, Defs);
            Assert.That(BridgeJson.Write(bridged), Is.EqualTo(
                "[{\"dict\":[[\"player\",{\"entity\":1,\"def\":\"pandemic:Alice\"}],[\"left\",4]]}," +
                "{\"dict\":[[\"player\",{\"hidden\":true}],[\"rate\",{\"fixed\":2500000}]]}," +
                "{\"entity\":3}]"));
        }

        [Test]
        public void ATextNamesItsEntitiesByTheDefsWrittenDownWhenItWasMade()
        {
            // text() 때 적어 둔 Def를 쓴다 (G267): 지금 그 엔티티가 없어졌어도 이름을 보인다
            var text = new TextValue("lost", new SortedDictionary<string, Value>(StringComparer.Ordinal)
                {
                    ["who"] = new EntityValue(new EntityId(3)),
                    ["n"] = new IntValue(2),
                },
                new SortedDictionary<string, string>(StringComparer.Ordinal) { ["who"] = "pandemic:Carol" });
            var bridged = KernelValues.ToBridge(text, Defs).AsText;
            Assert.That(bridged.Key, Is.EqualTo("lost"));
            Assert.That(bridged.Args[1].Key, Is.EqualTo("who"));
            Assert.That(bridged.Args[1].Value.AsEntity.Def, Is.EqualTo("pandemic:Carol"));
        }

        [Test]
        public void AnOptionKeepsItsLooks()
        {
            var option = new OptionValue(new StrValue("Treat"), label: new StrValue("treat-key"), style: "danger");
            var bridged = KernelValues.ToBridge(option, Defs).AsOption;
            Assert.That(bridged.Value.AsString, Is.EqualTo("Treat"));
            Assert.That(bridged.Label!.AsString, Is.EqualTo("treat-key"));
            Assert.That(bridged.Style, Is.EqualTo("danger"));
            Assert.That(bridged.Desc, Is.Null);
        }

        [Test]
        public void AValueThatIsNotDataDoesNotCross()
        {
            var e = Assert.Throws<BridgeValueException>(() => KernelValues.ToBridge(new ListValue(new Value[] { new RangeValue(0, 1, 3) }), Defs));
            Assert.That(e!.Message, Does.Contain("cannot cross the host boundary"));
        }

        [Test]
        public void AnArgumentComesInWithItsEntityById()
        {
            var args = new Dictionary<string, BridgeValue>
            {
                ["player"] = new BridgeEntity(2, "pandemic:Bob"),
                ["to"] = BridgeValue.Of("pandemic:Lima"),
                ["rate"] = new BridgeFixed(1_000_000),
                ["picks"] = new BridgeList(new[] { BridgeValue.Of(1), BridgeValue.Of(true) }),
            };
            var kernel = KernelValues.ToKernel(args);
            Assert.That(((EntityValue)kernel["player"]).Id, Is.EqualTo(new EntityId(2)));
            Assert.That(((StrValue)kernel["to"]).Value, Is.EqualTo("pandemic:Lima"));
            Assert.That(((FixedValue)kernel["rate"]).Value, Is.EqualTo(Fixed.FromRaw(1_000_000)));
            Assert.That(((ListValue)kernel["picks"]).Items, Has.Count.EqualTo(2));
        }

        [Test]
        public void HiddenTextAndOptionsAreNotArguments()
        {
            Assert.Throws<BridgeValueException>(() => KernelValues.ToKernel(BridgeHidden.Instance));
            Assert.Throws<BridgeValueException>(() => KernelValues.ToKernel(new BridgeText("k", new KeyValuePair<string, BridgeValue>[0])));
            Assert.Throws<BridgeValueException>(() => KernelValues.ToKernel(new BridgeOption(BridgeValue.Of(1))));
        }
    }
}
