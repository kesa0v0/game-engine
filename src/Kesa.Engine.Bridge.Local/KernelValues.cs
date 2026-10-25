using System;
using System.Collections.Generic;
using Kesa.Engine.Numerics;
using Kesa.Engine.Runtime;
using Kesa.Engine.Script;

namespace Kesa.Engine.Bridge.Local
{
    /// <summary>
    /// 커널 값과 경계 값을 서로 바꾼다 (spec-gaps G326). 커널 쪽에서 나가는 값은 데이터 종류만 경계를 넘고, 함수 값 같은 것은
    /// <see cref="BridgeValueException"/>이다(그 호출의 오류). 들어오는 값(커맨드 인자)은 엔티티를 id로만 받는다.
    /// </summary>
    public static class KernelValues
    {
        /// <summary>
        /// 나가는 값. <paramref name="defOf"/>는 엔티티의 Def id를 주고, 이미 없어진 엔티티면 null이다. 글(<see cref="TextValue"/>)의
        /// 엔티티 인자는 <c>text()</c> 때 적어 둔 Def를 쓴다(G267): 그 뒤에 없어진 엔티티도 이름을 보인다.
        /// </summary>
        public static BridgeValue ToBridge(Value value, Func<EntityId, string?> defOf)
        {
            switch (value)
            {
                case NoneValue _: return BridgeNone.Instance;
                case BoolValue b: return BridgeValue.Of(b.Value);
                case IntValue i: return new BridgeInt(i.Value);
                case FixedValue f: return new BridgeFixed(f.Value.Raw);
                case StrValue s: return new BridgeStr(s.Value);
                case HiddenValue _: return BridgeHidden.Instance;
                case EntityValue e: return new BridgeEntity(e.Id.Value, defOf(e.Id));
                case ListValue l:
                    var items = new List<BridgeValue>(l.Items.Count);
                    foreach (var item in l.Items) items.Add(ToBridge(item, defOf));
                    return new BridgeList(items);
                case DictValue d:
                    var pairs = new List<KeyValuePair<BridgeValue, BridgeValue>>(d.Items.Count);
                    foreach (var item in d.Items)
                        pairs.Add(new KeyValuePair<BridgeValue, BridgeValue>(ToBridge(item.Key, defOf), ToBridge(item.Value, defOf)));
                    return new BridgeDict(pairs);
                case TextValue t:
                    var args = new List<KeyValuePair<string, BridgeValue>>(t.Args.Count);
                    foreach (var arg in t.Args)
                    {
                        var converted = arg.Value is EntityValue entity
                            ? new BridgeEntity(entity.Id.Value, t.EntityDefs.TryGetValue(arg.Key, out var def) ? def : null)
                            : ToBridge(arg.Value, defOf);
                        args.Add(new KeyValuePair<string, BridgeValue>(arg.Key, converted));
                    }
                    return new BridgeText(t.Key, args);
                case OptionValue o:
                    return new BridgeOption(ToBridge(o.Value, defOf), Optional(o.Label, defOf), Optional(o.Desc, defOf), o.Style,
                        Optional(o.Data, defOf));
            }
            throw new BridgeValueException("a " + value.KindName + " value cannot cross the host boundary (spec-gaps G326)");
        }

        private static BridgeValue? Optional(Value? value, Func<EntityId, string?> defOf) => value == null ? null : ToBridge(value, defOf);

        /// <summary>
        /// 들어오는 값(커맨드·화면 동작의 인자). 엔티티는 id만 쓴다. 숨김·글·선택지는 인자가 될 수 없어
        /// <see cref="BridgeValueException"/>이다.
        /// </summary>
        public static Value ToKernel(BridgeValue value)
        {
            switch (value)
            {
                case BridgeNone _: return NoneValue.Instance;
                case BridgeBool b: return b.Value ? BoolValue.True : BoolValue.False;
                case BridgeInt i: return new IntValue(i.Value);
                case BridgeFixed f: return new FixedValue(Fixed.FromRaw(f.Raw));
                case BridgeStr s: return new StrValue(s.Value);
                case BridgeEntity e: return new EntityValue(new EntityId(e.Id));
                case BridgeList l:
                    var items = new List<Value>(l.Items.Count);
                    foreach (var item in l.Items) items.Add(ToKernel(item));
                    return new ListValue(items);
                case BridgeDict d:
                    var entries = new List<DictItem>(d.Items.Count);
                    foreach (var item in d.Items) entries.Add(new DictItem(ToKernel(item.Key), ToKernel(item.Value), Diagnostics.SourceSpan.None));
                    return new DictValue(entries);
            }
            throw new BridgeValueException("a " + value.Kind + " value cannot be an argument");
        }

        /// <summary>인자 묶음을 들어오는 쪽으로.</summary>
        public static IReadOnlyDictionary<string, Value> ToKernel(IReadOnlyDictionary<string, BridgeValue> args)
        {
            var result = new SortedDictionary<string, Value>(StringComparer.Ordinal);
            foreach (var pair in args) result[pair.Key] = ToKernel(pair.Value);
            return result;
        }
    }
}
