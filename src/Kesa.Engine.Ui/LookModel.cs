using System;
using System.Collections.Generic;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>그림 요소 속성의 값 종류 (G413). 적힌 글과 묶인 값을 이 종류로 바꿔 렌더러에 넘긴다.</summary>
    public enum LookAttributeType
    {
        /// <summary>모드 폴더의 가상 경로(G335).</summary>
        Path,

        /// <summary>정수.</summary>
        Integer,

        /// <summary>고정소수점 수 하나.</summary>
        Number,

        /// <summary>고정소수점 수 둘 (<c>"1,0.5"</c>, 묶으면 수 둘의 목록).</summary>
        Vector2,

        /// <summary>고정소수점 수 둘이나 셋.</summary>
        Vector,

        /// <summary><c>#rrggbb</c>나 <c>#rrggbbaa</c>.</summary>
        Color,

        /// <summary><c>true</c>·<c>false</c> (묶으면 모드 언어의 참거짓).</summary>
        Flag,

        /// <summary>화면 글자 (G427). 적힌 글은 그대로, 묶인 값은 호스트가 글자로 바꾼다(<c>text("키")</c>는 번역).</summary>
        Text,
    }

    /// <summary>렌더러에 넘기는 속성 값. 수는 고정소수점의 원래 정수값이고 실수로 바꾸는 것은 렌더러가 한다 (G345).</summary>
    public sealed class LookAttributeValue : IEquatable<LookAttributeValue>
    {
        private LookAttributeValue(LookAttributeType type, string? text, long[] numbers, bool flag, uint color)
        {
            Type = type;
            Text = text;
            Numbers = numbers;
            Flag = flag;
            Color = color;
        }

        public LookAttributeType Type { get; }

        /// <summary><see cref="LookAttributeType.Path"/>의 경로, <see cref="LookAttributeType.Text"/>의 글자.</summary>
        public string? Text { get; }

        /// <summary>
        /// <see cref="LookAttributeType.Integer"/>는 그 정수 하나, <see cref="LookAttributeType.Number"/>·<c>Vector</c>는 고정소수점의 원래 정수값들.
        /// </summary>
        public IReadOnlyList<long> Numbers { get; }

        public bool Flag { get; }

        /// <summary>RGBA, 빨강이 가장 높은 바이트.</summary>
        public uint Color { get; }

        public static LookAttributeValue OfPath(string path) => new LookAttributeValue(LookAttributeType.Path, path, Array.Empty<long>(), false, 0);

        public static LookAttributeValue OfText(string text) => new LookAttributeValue(LookAttributeType.Text, text, Array.Empty<long>(), false, 0);

        public static LookAttributeValue OfInteger(long value) => new LookAttributeValue(LookAttributeType.Integer, null, new[] { value }, false, 0);

        public static LookAttributeValue OfNumbers(LookAttributeType type, long[] raw) => new LookAttributeValue(type, null, raw, false, 0);

        public static LookAttributeValue OfFlag(bool value) => new LookAttributeValue(LookAttributeType.Flag, null, Array.Empty<long>(), value, 0);

        public static LookAttributeValue OfColor(uint rgba) => new LookAttributeValue(LookAttributeType.Color, null, Array.Empty<long>(), false, rgba);

        public bool Equals(LookAttributeValue? other)
        {
            if (other == null || other.Type != Type || other.Text != Text || other.Flag != Flag || other.Color != Color) return false;
            if (other.Numbers.Count != Numbers.Count) return false;
            for (int i = 0; i < Numbers.Count; i++)
                if (other.Numbers[i] != Numbers[i]) return false;
            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as LookAttributeValue);

        public override int GetHashCode() => (int)Type;

        public override string ToString() => Type switch
        {
            LookAttributeType.Path => Text!,
            LookAttributeType.Text => Text!,
            LookAttributeType.Flag => Flag ? "true" : "false",
            LookAttributeType.Color => "#" + Color.ToString("x8", System.Globalization.CultureInfo.InvariantCulture),
            LookAttributeType.Integer => Numbers[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => string.Join(",", Array.ConvertAll(ToArray(Numbers), BridgeFixedText)),
        };

        private static long[] ToArray(IReadOnlyList<long> list)
        {
            var array = new long[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }

        private static string BridgeFixedText(long raw) => new BridgeFixed(raw).Decimal;
    }

    /// <summary>그림 요소 종류 하나 (G413): 이름, 받는 속성과 값 종류, 자식을 받는지. 기본 요소도 게임의 요소도 이것으로 등록한다.</summary>
    public sealed class LookElementKind
    {
        /// <param name="check">적힌 속성 값(이름, 값)을 더 본다. 틀리면 이유, 맞으면 null. 묶인 값은 그릴 때 호스트가 본다.</param>
        public LookElementKind(string name, IReadOnlyDictionary<string, LookAttributeType> attributes, bool acceptsChildren,
            Func<string, LookAttributeValue, string?>? check = null)
        {
            Name = name;
            Attributes = attributes;
            AcceptsChildren = acceptsChildren;
            Check = check;
        }

        public Func<string, LookAttributeValue, string?>? Check { get; }

        public string Name { get; }

        /// <summary>이 종류만의 속성. 공통 속성(<see cref="LookKinds.Common"/>)은 해석기가 더한다.</summary>
        public IReadOnlyDictionary<string, LookAttributeType> Attributes { get; }

        public bool AcceptsChildren { get; }
    }

    /// <summary>
    /// 공간 자리를 놓을 위치 (G414): 고정소수점의 원래 정수값으로 Unity 축(x 오른쪽, y 위, z 앞)이다.
    /// </summary>
    public readonly struct LookPosition : IEquatable<LookPosition>
    {
        public LookPosition(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public long X { get; }
        public long Y { get; }
        public long Z { get; }

        public bool Equals(LookPosition other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is LookPosition p && Equals(p);

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 31) ^ (Z.GetHashCode() * 17);

        public override string ToString() => "(" + new BridgeFixed(X).Decimal + ", " + new BridgeFixed(Y).Decimal + ", " + new BridgeFixed(Z).Decimal + ")";
    }

    /// <summary>설정을 마친 공간 하나. 자리 값을 위치로 바꾼다.</summary>
    public abstract class SpaceLayout
    {
        /// <summary>바꾸지 못하면 모더가 읽을 이유와 함께 false.</summary>
        public abstract bool TryPlace(BridgeValue place, out LookPosition position, out string error);

        /// <summary>공간 한 단위의 길이(고정소수점 원래 정수값). 물체 안의 <c>size</c>·<c>offset</c>과 기본 크기가 이 단위다 (G413).</summary>
        public abstract long Unit { get; }

        /// <summary>
        /// 누른 곳의 자리 값 (G422): <paramref name="from"/>에서 <paramref name="direction"/>으로 가는 반직선이 공간의 바닥과 만나는 곳. 만나지 않거나
        /// 되돌릴 수 없는 공간이면 false.
        /// </summary>
        public virtual bool TryPlaceOnRay(LookPosition from, LookPosition direction, out BridgeValue? place)
        {
            place = null;
            return false;
        }
    }

    /// <summary>공간 종류 하나 (G394·G410): 이름, 받는 설정, 설정으로 공간을 만드는 일. 기본 종류도 게임의 종류도 이것으로 등록한다.</summary>
    public abstract class SpaceKind
    {
        public abstract string Name { get; }

        /// <summary>받는 설정 이름(진단의 "혹시 …?"에 쓴다).</summary>
        public abstract IReadOnlyCollection<string> Settings { get; }

        /// <summary>설정 글로 공간을 만든다. 틀린 설정은 <paramref name="problems"/>에 담고 null.</summary>
        public abstract SpaceLayout? Configure(IReadOnlyDictionary<string, string> settings, List<string> problems);
    }

    /// <summary><c>bind-속성</c> 하나. 식의 이름은 그림 값의 키와 <c>for</c> 변수다 (G390).</summary>
    public sealed class LookBinding
    {
        public LookBinding(string attribute, LookAttributeType type, VarExpr expression, string source)
        {
            Attribute = attribute;
            Type = type;
            Expression = expression;
            Source = source;
        }

        /// <summary>속성 이름. <c>display</c>는 보임이다.</summary>
        public string Attribute { get; }
        public LookAttributeType Type { get; }
        public VarExpr Expression { get; }
        public string Source { get; }
    }

    /// <summary><c>for="변수 in 키"</c>: 자식들을 목록의 항목마다 만든다.</summary>
    public sealed class LookRepeat
    {
        public LookRepeat(string variable, VarExpr source, string text)
        {
            Variable = variable;
            Source = source;
            Text = text;
        }

        public string Variable { get; }
        public VarExpr Source { get; }
        public string Text { get; }
    }

    /// <summary>그림 정의 안의 요소 하나.</summary>
    public sealed class LookElement
    {
        public LookElement(string kind, string place, IReadOnlyList<KeyValuePair<string, LookAttributeValue>> attributes,
            IReadOnlyList<LookBinding> bindings, LookRepeat? repeat, IReadOnlyList<LookElement> children)
        {
            Kind = kind;
            Place = place;
            Attributes = attributes;
            Bindings = bindings;
            Repeat = repeat;
            Children = children;
        }

        public string Kind { get; }

        /// <summary>진단에 붙이는 <c>파일:줄</c>.</summary>
        public string Place { get; }

        /// <summary>파일에 적은 고정 속성(이미 값 종류로 바꾼 것).</summary>
        public IReadOnlyList<KeyValuePair<string, LookAttributeValue>> Attributes { get; }

        public IReadOnlyList<LookBinding> Bindings { get; }
        public LookRepeat? Repeat { get; }
        public IReadOnlyList<LookElement> Children { get; }
    }

    /// <summary><c>&lt;Look name="…"&gt;</c> 하나 (G390).</summary>
    public sealed class LookDefinition
    {
        /// <summary>
        /// 효과로 띄울 때의 시간 칸 (G425): <c>fade-in</c>·<c>solid</c>·<c>fade-out</c>은 초, <c>rise</c>·<c>drift</c>는 초당 공간 단위,
        /// <c>grow</c>는 초당 크기 배율 변화. <c>move</c>(초)는 효과가 아니라 자리가 바뀐 물체를 옮기는 시간이다 (G432).
        /// </summary>
        public static readonly IReadOnlyList<string> TimingNames = new[] { "fade-in", "solid", "fade-out", "rise", "drift", "grow", "move" };

        /// <summary>시간 칸의 값 종류.</summary>
        public static LookAttributeType TimingType(string name) => name == "drift" ? LookAttributeType.Vector2 : LookAttributeType.Number;

        /// <summary>음수가 되지 않는 시간 칸(시간들).</summary>
        public static bool IsDuration(string name) => name == "fade-in" || name == "solid" || name == "fade-out" || name == "move";

        public LookDefinition(string name, string mod, string place, IReadOnlyList<LookElement> elements,
            IReadOnlyDictionary<string, LookAttributeValue>? timing = null, IReadOnlyList<LookBinding>? timingBindings = null, string? exit = null)
        {
            Exit = exit;
            Name = name;
            Mod = mod;
            Place = place;
            Elements = elements;
            Timing = timing ?? new SortedDictionary<string, LookAttributeValue>(StringComparer.Ordinal);
            TimingBindings = timingBindings ?? Array.Empty<LookBinding>();
        }

        /// <summary>적힌 시간 칸. 없는 칸은 0이다.</summary>
        public IReadOnlyDictionary<string, LookAttributeValue> Timing { get; }

        /// <summary><c>bind-fade-out="hold"</c>처럼 그림 값으로 덮는 시간 칸. 값이 <c>None</c>이면 적힌 값을 쓴다.</summary>
        public IReadOnlyList<LookBinding> TimingBindings { get; }

        /// <summary>이 그림의 물체가 판에서 사라지면 그 자리에 효과로 띄울 그림 (G431).</summary>
        public string? Exit { get; }

        public string Name { get; }

        /// <summary>정의를 든 모드(뒤 모드가 같은 이름을 통째로 이긴다).</summary>
        public string Mod { get; }

        public string Place { get; }
        public IReadOnlyList<LookElement> Elements { get; }
    }

    /// <summary><c>&lt;Space name="…" kind="…" …/&gt;</c> 하나 (G410·G411).</summary>
    public sealed class SpaceDeclaration
    {
        public SpaceDeclaration(string name, string kind, string mod, string place, SpaceLayout layout)
        {
            Name = name;
            Kind = kind;
            Mod = mod;
            Place = place;
            Layout = layout;
        }

        public string Name { get; }
        public string Kind { get; }
        public string Mod { get; }
        public string Place { get; }
        public SpaceLayout Layout { get; }
    }

    /// <summary>모드 하나의 그림·공간 파일 하나. <see cref="Path"/>의 확장자(<c>.look.xml</c>·<c>.space.xml</c>)가 종류를 정한다 (G409·G411).</summary>
    public sealed class LookSource
    {
        public LookSource(string mod, string path, string text)
        {
            Mod = mod;
            Path = path;
            Text = text;
        }

        public string Mod { get; }

        /// <summary>진단에 붙이는 경로(모드 폴더 기준).</summary>
        public string Path { get; }

        public string Text { get; }
    }
}
