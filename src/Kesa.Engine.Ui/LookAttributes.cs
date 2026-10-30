using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Bridge;

namespace Kesa.Engine.Ui
{
    /// <summary>그림 요소 속성의 값 바꾸기 (G413): 파일에 적힌 글과 그림 값에서 묶인 값을 <see cref="LookAttributeValue"/>로.</summary>
    internal static class LookAttributes
    {
        /// <summary>파일에 적힌 글. 틀리면 이유와 함께 null.</summary>
        public static LookAttributeValue? FromText(LookAttributeType type, string text, out string error)
        {
            error = "";
            switch (type)
            {
                case LookAttributeType.Path:
                    return Path(text, out error);
                case LookAttributeType.Text:
                    return LookAttributeValue.OfText(text);
                case LookAttributeType.Flag:
                    if (text == "true") return LookAttributeValue.OfFlag(true);
                    if (text == "false") return LookAttributeValue.OfFlag(false);
                    error = "must be true or false";
                    return null;
                case LookAttributeType.Color:
                    return Color(text, out error);
                case LookAttributeType.Integer:
                    if (long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) return LookAttributeValue.OfInteger(n);
                    error = "must be a whole number";
                    return null;
                default:
                    var parts = text.Split(',');
                    var raw = new long[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        BridgeValue number;
                        try
                        {
                            number = UiExprParser.ParseNumber(parts[i]);
                        }
                        catch (UiSyntaxException e)
                        {
                            error = "'" + parts[i].Trim() + "': " + e.Message;
                            return null;
                        }
                        if (!UiValues.TryNumber(number, out raw[i], out error)) return null;
                    }
                    return Numbers(type, raw, out error);
            }
        }

        /// <summary>그림 값에서 묶인 값. 틀리면 이유와 함께 null.</summary>
        public static LookAttributeValue? FromValue(LookAttributeType type, BridgeValue value, out string error)
        {
            error = "";
            switch (type)
            {
                case LookAttributeType.Text:
                    return LookAttributeValue.OfText(Plain(value));
                case LookAttributeType.Path:
                    if (value is BridgeStr path) return Path(path.Value, out error);
                    error = "a path is text, got a " + KindName(value);
                    return null;
                case LookAttributeType.Flag:
                    return UiValues.TryTruthy(value, out bool flag, out error) ? LookAttributeValue.OfFlag(flag) : null;
                case LookAttributeType.Color:
                    if (value is BridgeStr color) return Color(color.Value, out error);
                    error = "a color is text like '#ff8800', got a " + KindName(value);
                    return null;
                case LookAttributeType.Integer:
                    if (value is BridgeInt i) return LookAttributeValue.OfInteger(i.Value);
                    error = "a whole number is needed, got a " + KindName(value);
                    return null;
                case LookAttributeType.Number:
                    return UiValues.TryNumber(value, out long one, out error) ? LookAttributeValue.OfNumbers(type, new[] { one }) : null;
                default:
                    if (!(value is BridgeList list))
                    {
                        error = "a list of numbers is needed, got a " + KindName(value);
                        return null;
                    }
                    var raw = new long[list.Items.Count];
                    for (int k = 0; k < raw.Length; k++)
                        if (!UiValues.TryNumber(list.Items[k], out raw[k], out error)) return null;
                    return Numbers(type, raw, out error);
            }
        }

        /// <summary>번역기가 없을 때의 글자: 수와 글은 그대로, 나머지는 경계 JSON.</summary>
        public static string Plain(BridgeValue value) => value switch
        {
            BridgeNone _ => "",
            BridgeStr s => s.Value,
            BridgeInt i => i.Value.ToString(CultureInfo.InvariantCulture),
            BridgeFixed f => f.Decimal,
            _ => BridgeJson.Write(value),
        };

        private static LookAttributeValue? Numbers(LookAttributeType type, long[] raw, out string error)
        {
            error = "";
            int min = type == LookAttributeType.Number ? 1 : 2;
            int max = type == LookAttributeType.Number ? 1 : type == LookAttributeType.Vector2 ? 2 : 3;
            if (raw.Length < min || raw.Length > max)
            {
                error = min == max ? "needs " + min + " number" + (min == 1 ? "" : "s") : "needs " + min + " or " + max + " numbers";
                return null;
            }
            return LookAttributeValue.OfNumbers(type, raw);
        }

        private static LookAttributeValue? Path(string text, out string error)
        {
            error = "";
            if (text.Length == 0)
            {
                error = "the path is empty";
                return null;
            }
            var why = UiPaths.WhyNotModPath(text);
            if (why == null) return LookAttributeValue.OfPath(text);
            error = why;
            return null;
        }

        private static LookAttributeValue? Color(string text, out string error)
        {
            error = "";
            string s = text.Trim();
            if (s.Length == 7 || s.Length == 9)
            {
                if (s[0] == '#' && uint.TryParse(s.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint v))
                    return LookAttributeValue.OfColor(s.Length == 7 ? (v << 8) | 0xffu : v);
            }
            error = "'" + text + "' is not a color; write #rrggbb or #rrggbbaa";
            return null;
        }

        private static string KindName(BridgeValue value) => value.Kind.ToString().ToLowerInvariant();
    }
}
