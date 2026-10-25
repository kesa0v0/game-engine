using System;
using System.Collections.Generic;

namespace Kesa.Engine.Bridge
{
    /// <summary>경계 값의 종류. 닫힌 목록이다 (spec-gaps G326).</summary>
    public enum BridgeKind
    {
        None,
        Bool,
        Int,
        Fixed,
        Str,
        List,
        Dict,
        Entity,
        Hidden,
        Text,
        Option,
    }

    /// <summary>
    /// 호스트 경계를 넘는 값 (spec-gaps G326): 화면 함수의 결과, 질문의 선택지와 데이터, 합법 수의 인자, 거절 사유의 인자.
    /// 커널 객체가 아니라 데이터이고 바뀌지 않는다. 같은 모양이면 같다(<see cref="Equals(object)"/>).
    /// 꺼내는 속성(<see cref="AsInt"/> 등)은 종류가 다르면 <see cref="InvalidOperationException"/>이다.
    /// </summary>
    public abstract class BridgeValue
    {
        public abstract BridgeKind Kind { get; }

        public bool AsBool => As<BridgeBool>(BridgeKind.Bool).Value;
        public long AsInt => As<BridgeInt>(BridgeKind.Int).Value;
        public string AsString => As<BridgeStr>(BridgeKind.Str).Value;
        public IReadOnlyList<BridgeValue> AsList => As<BridgeList>(BridgeKind.List).Items;
        public BridgeFixed AsFixed => As<BridgeFixed>(BridgeKind.Fixed);
        public BridgeDict AsDict => As<BridgeDict>(BridgeKind.Dict);
        public BridgeEntity AsEntity => As<BridgeEntity>(BridgeKind.Entity);
        public BridgeText AsText => As<BridgeText>(BridgeKind.Text);
        public BridgeOption AsOption => As<BridgeOption>(BridgeKind.Option);

        public bool IsNone => Kind == BridgeKind.None;

        /// <summary>사전의 문자열 키 칸. 없으면 <see cref="KeyNotFoundException"/>.</summary>
        public BridgeValue this[string key] => AsDict[key];

        private T As<T>(BridgeKind kind) where T : BridgeValue =>
            this as T ?? throw new InvalidOperationException("expected a " + kind + " value, got " + Kind);

        /// <summary>정규형 JSON (G326).</summary>
        public override string ToString() => BridgeJson.Write(this);

        public static BridgeValue Of(bool value) => value ? BridgeBool.True : BridgeBool.False;
        public static BridgeValue Of(long value) => new BridgeInt(value);
        public static BridgeValue Of(string value) => new BridgeStr(value);

        internal static bool Same(BridgeValue? a, BridgeValue? b) => a == null ? b == null : a.Equals(b);
    }

    public sealed class BridgeNone : BridgeValue
    {
        public static readonly BridgeNone Instance = new BridgeNone();
        private BridgeNone() { }
        public override BridgeKind Kind => BridgeKind.None;
        public override bool Equals(object? obj) => obj is BridgeNone;
        public override int GetHashCode() => 0;
    }

    public sealed class BridgeBool : BridgeValue
    {
        public static readonly BridgeBool True = new BridgeBool(true);
        public static readonly BridgeBool False = new BridgeBool(false);
        private BridgeBool(bool value) => Value = value;
        public bool Value { get; }
        public override BridgeKind Kind => BridgeKind.Bool;
        public override bool Equals(object? obj) => obj is BridgeBool b && b.Value == Value;
        public override int GetHashCode() => Value ? 1 : 2;
    }

    public sealed class BridgeInt : BridgeValue
    {
        public BridgeInt(long value) => Value = value;
        public long Value { get; }
        public override BridgeKind Kind => BridgeKind.Int;
        public override bool Equals(object? obj) => obj is BridgeInt i && i.Value == Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    /// <summary>고정소수점: 값 = <see cref="Raw"/> / <see cref="Scale"/> (커널의 <c>Fixed</c>와 같은 눈금).</summary>
    public sealed class BridgeFixed : BridgeValue
    {
        public const long Scale = 1_000_000;
        public BridgeFixed(long raw) => Raw = raw;
        public long Raw { get; }

        /// <summary>십진 표기: 커널 <c>Fixed</c>와 같은 글자(<c>2.5</c>, <c>-0.25</c>, <c>3.0</c>).</summary>
        public string Decimal
        {
            get
            {
                long whole = Raw / Scale;
                long frac = Raw % Scale;
                if (frac < 0) frac = -frac;
                string sign = Raw < 0 && whole == 0 ? "-" : "";
                string fracText = frac.ToString("D6", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
                if (fracText.Length == 0) fracText = "0";
                return sign + whole.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + fracText;
            }
        }

        public override BridgeKind Kind => BridgeKind.Fixed;
        public override bool Equals(object? obj) => obj is BridgeFixed f && f.Raw == Raw;
        public override int GetHashCode() => Raw.GetHashCode();
    }

    public sealed class BridgeStr : BridgeValue
    {
        public BridgeStr(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public override BridgeKind Kind => BridgeKind.Str;
        public override bool Equals(object? obj) => obj is BridgeStr s && s.Value == Value;
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    }

    public sealed class BridgeList : BridgeValue
    {
        public BridgeList(IReadOnlyList<BridgeValue> items) => Items = items;
        public IReadOnlyList<BridgeValue> Items { get; }
        public override BridgeKind Kind => BridgeKind.List;

        public override bool Equals(object? obj)
        {
            if (!(obj is BridgeList l) || l.Items.Count != Items.Count) return false;
            for (int i = 0; i < Items.Count; i++)
                if (!Items[i].Equals(l.Items[i])) return false;
            return true;
        }

        public override int GetHashCode() => Items.Count;
    }

    /// <summary>사전. 항목은 넣은 순서를 지킨다. 키는 문자열이나 정수다 (커널의 dict와 같음).</summary>
    public sealed class BridgeDict : BridgeValue
    {
        public BridgeDict(IReadOnlyList<KeyValuePair<BridgeValue, BridgeValue>> items) => Items = items;
        public IReadOnlyList<KeyValuePair<BridgeValue, BridgeValue>> Items { get; }
        public override BridgeKind Kind => BridgeKind.Dict;

        public new BridgeValue this[string key] =>
            TryGet(key, out var value) ? value : throw new KeyNotFoundException("no '" + key + "' in the dict");

        public bool TryGet(string key, out BridgeValue value)
        {
            foreach (var item in Items)
            {
                if (item.Key is BridgeStr s && s.Value == key)
                {
                    value = item.Value;
                    return true;
                }
            }
            value = BridgeNone.Instance;
            return false;
        }

        public override bool Equals(object? obj)
        {
            if (!(obj is BridgeDict d) || d.Items.Count != Items.Count) return false;
            for (int i = 0; i < Items.Count; i++)
                if (!Items[i].Key.Equals(d.Items[i].Key) || !Items[i].Value.Equals(d.Items[i].Value)) return false;
            return true;
        }

        public override int GetHashCode() => Items.Count;
    }

    /// <summary>
    /// 엔티티: 다시 보낼 id와 그릴 때 쓸 Def id (G326). 엔티티가 보이는 연결에게는 Def도 보인다(G197).
    /// <see cref="Def"/>가 null이면 값을 만들 때 이미 없던 엔티티라 Def를 모른다.
    /// </summary>
    public sealed class BridgeEntity : BridgeValue
    {
        public BridgeEntity(long id, string? def)
        {
            Id = id;
            Def = def;
        }

        public long Id { get; }
        public string? Def { get; }
        public override BridgeKind Kind => BridgeKind.Entity;

        /// <summary>같은 엔티티면 같다 (Def는 엔티티마다 바뀌지 않는다).</summary>
        public override bool Equals(object? obj) => obj is BridgeEntity e && e.Id == Id;

        public override int GetHashCode() => Id.GetHashCode();
    }

    /// <summary>보는 연결이 못 보는 엔티티 (G198): 누군가 있지만 누군지 모른다. 커맨드 인자로는 낼 수 없다.</summary>
    public sealed class BridgeHidden : BridgeValue
    {
        public static readonly BridgeHidden Instance = new BridgeHidden();
        private BridgeHidden() { }
        public override BridgeKind Kind => BridgeKind.Hidden;
        public override bool Equals(object? obj) => obj is BridgeHidden;
        public override int GetHashCode() => 3;
    }

    /// <summary>번역할 글: 키와 이름 있는 인자 (G142·G267). 인자는 이름의 서수 순. 엔티티 인자는 그 Def를 함께 싣는다.</summary>
    public sealed class BridgeText : BridgeValue
    {
        public BridgeText(string key, IReadOnlyList<KeyValuePair<string, BridgeValue>> args)
        {
            Key = key;
            var sorted = new List<KeyValuePair<string, BridgeValue>>(args);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            Args = sorted;
        }

        public string Key { get; }
        public IReadOnlyList<KeyValuePair<string, BridgeValue>> Args { get; }
        public override BridgeKind Kind => BridgeKind.Text;

        public override bool Equals(object? obj)
        {
            if (!(obj is BridgeText t) || t.Key != Key || t.Args.Count != Args.Count) return false;
            for (int i = 0; i < Args.Count; i++)
                if (t.Args[i].Key != Args[i].Key || !t.Args[i].Value.Equals(Args[i].Value)) return false;
            return true;
        }

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
    }

    /// <summary>질문의 선택지 하나 (G271): 고를 값과 보이는 모양. 적지 않은 칸은 null.</summary>
    public sealed class BridgeOption : BridgeValue
    {
        public BridgeOption(BridgeValue value, BridgeValue? label = null, BridgeValue? desc = null, string? style = null, BridgeValue? data = null)
        {
            Value = value;
            Label = label;
            Desc = desc;
            Style = style;
            Data = data;
        }

        public BridgeValue Value { get; }
        public BridgeValue? Label { get; }
        public BridgeValue? Desc { get; }
        public string? Style { get; }
        public BridgeValue? Data { get; }
        public override BridgeKind Kind => BridgeKind.Option;

        public override bool Equals(object? obj) =>
            obj is BridgeOption o && o.Style == Style && o.Value.Equals(Value) && Same(o.Label, Label) && Same(o.Desc, Desc) && Same(o.Data, Data);

        public override int GetHashCode() => Value.GetHashCode();
    }
}
