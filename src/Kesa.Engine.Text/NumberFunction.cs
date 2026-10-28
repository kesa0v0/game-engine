using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Linguini.Bundle;
using Linguini.Shared.Types.Bundle;

namespace Kesa.Engine.Text
{
    /// <summary>
    /// 번역문의 <c>NUMBER($n, …)</c> (spec-gaps G334·G356~G358). 옵션 이름과 뜻은 <c>Intl.NumberFormat</c>을 따르되 일부만 받는다.
    /// 값은 소수 6자리의 정확한 수라 정수로 계산하고 0에서 먼 쪽으로 반올림한다. 기호는 그 언어의 <c>engine-number-…</c> 글이다.
    /// </summary>
    internal static class NumberFunction
    {
        public const string Name = "NUMBER";
        public const string DecimalKey = "engine-number-decimal";
        public const string GroupKey = "engine-number-group";
        public const string PercentKey = "engine-number-percent";
        public const string PercentArgument = "n";

        private const string MinFraction = "minimumFractionDigits";
        private const string MaxFraction = "maximumFractionDigits";
        private const string Grouping = "useGrouping";
        private const string Style = "style";
        private const string Sign = "signDisplay";
        private const int MaxDigits = 20;

        /// <summary>커널 고정소수점과 같은 소수 자리.</summary>
        private const int Places = 6;

        /// <summary>받는 옵션 이름과 값 (검사가 알려 줄 목록).</summary>
        public static readonly string Known =
            MinFraction + "·" + MaxFraction + " (0~" + MaxDigits + "), " + Grouping + " (\"true\"·\"false\"), " +
            Style + " (\"decimal\"·\"percent\"), " + Sign + " (\"auto\"·\"always\"·\"exceptZero\")";

        private sealed class Options
        {
            public int? Min;
            public int? Max;
            public bool Group = true;
            public bool Percent;
            public string Sign = "auto";
        }

        /// <summary>옵션 하나가 맞지 않으면 그 이유. 번역 검사(LOC012)와 실행이 함께 쓴다.</summary>
        public static string? Problem(string name, string value)
        {
            switch (name)
            {
                case MinFraction:
                case MaxFraction:
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int digits) && digits <= MaxDigits
                        ? null
                        : name + " must be a whole number from 0 to " + MaxDigits;
                case Grouping:
                    return value == "true" || value == "false" ? null : Grouping + " must be \"true\" or \"false\"";
                case Style:
                    return value == "decimal" || value == "percent" ? null : Style + " must be \"decimal\" or \"percent\"";
                case Sign:
                    return value == "auto" || value == "always" || value == "exceptZero"
                        ? null
                        : Sign + " must be \"auto\", \"always\" or \"exceptZero\"";
                default:
                    return "NUMBER has no option '" + name + "'";
            }
        }

        /// <summary>최소 자리가 최대 자리보다 크면 그 이유.</summary>
        public static string? RangeProblem(int min, int max) =>
            min > max ? MinFraction + " is larger than " + MaxFraction : null;

        public static void AddTo(FluentBundle bundle) =>
            bundle.AddFunctionOverriding(Name, (args, named) => Call(bundle, args, named));

        private static IFluentType Call(FluentBundle bundle, IList<IFluentType> args, IDictionary<string, IFluentType> named)
        {
            if (args.Count != 1 || !(args[0] is FluentNumber number)) return new FluentErrType();
            if (!TryParse(number is ExactNumber exact ? exact.Exact : number.AsString(), out var raw)) return new FluentErrType();
            var options = Read(named);
            if (options == null) return new FluentErrType();

            // 퍼센트는 100을 곱하는 대신 소수점을 두 자리 옮긴다
            int places = options.Percent ? Places - 2 : Places;
            int min = options.Min ?? 0;
            int max = options.Max ?? System.Math.Max(min, options.Percent ? 0 : 3);
            if (RangeProblem(min, max) != null) return new FluentErrType();

            var digits = BigInteger.Abs(raw);
            int fraction = places;
            if (max < places)
            {
                var unit = BigInteger.Pow(10, places - max);
                digits = BigInteger.DivRem(digits, unit, out var rest);
                if (rest * 2 >= unit) digits += 1;
                fraction = max;
            }
            while (fraction > min && !digits.IsZero && digits % 10 == 0)
            {
                digits /= 10;
                fraction--;
            }
            if (digits.IsZero) fraction = min;
            while (fraction < min)
            {
                digits *= 10;
                fraction++;
            }

            var scale = BigInteger.Pow(10, fraction);
            string whole = (digits / scale).ToString(CultureInfo.InvariantCulture);
            string part = fraction == 0 ? "" : (digits % scale).ToString(CultureInfo.InvariantCulture).PadLeft(fraction, '0');
            bool negative = raw.Sign < 0 && !digits.IsZero;
            string sign = negative ? "-"
                : options.Sign == "always" || options.Sign == "exceptZero" && !digits.IsZero ? "+"
                : "";

            string shown = sign + (options.Group ? Grouped(whole, Symbol(bundle, GroupKey, ",")) : whole) +
                           (fraction == 0 ? "" : Symbol(bundle, DecimalKey, ".") + part);
            if (options.Percent) shown = Percent(bundle, shown);
            return new ExactNumber(shown, (negative ? "-" : "") + whole + (fraction == 0 ? "" : "." + part));
        }

        private static Options? Read(IDictionary<string, IFluentType> named)
        {
            var options = new Options();
            foreach (var pair in named)
            {
                string value = pair.Value.AsString();
                if (Problem(pair.Key, value) != null) return null;
                switch (pair.Key)
                {
                    case MinFraction:
                        options.Min = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case MaxFraction:
                        options.Max = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case Grouping:
                        options.Group = value == "true";
                        break;
                    case Style:
                        options.Percent = value == "percent";
                        break;
                    case Sign:
                        options.Sign = value;
                        break;
                }
            }
            return options;
        }

        /// <summary><c>-12.5</c> → 10^6 눈금의 정수. 소수가 6자리를 넘거나 지수 표기면 거절.</summary>
        private static bool TryParse(string text, out BigInteger raw)
        {
            raw = BigInteger.Zero;
            int start = text.StartsWith("-") ? 1 : 0;
            int dot = text.IndexOf('.');
            string whole = dot < 0 ? text.Substring(start) : text.Substring(start, dot - start);
            string part = dot < 0 ? "" : text.Substring(dot + 1);
            if (whole.Length == 0 || part.Length > Places || !AllDigits(whole) || !AllDigits(part)) return false;
            raw = BigInteger.Parse(whole + part.PadRight(Places, '0'), CultureInfo.InvariantCulture);
            if (start == 1) raw = -raw;
            return true;
        }

        private static bool AllDigits(string text)
        {
            foreach (char c in text)
                if (c < '0' || c > '9') return false;
            return true;
        }

        private static string Grouped(string whole, string separator)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < whole.Length; i++)
            {
                if (i > 0 && (whole.Length - i) % 3 == 0) sb.Append(separator);
                sb.Append(whole[i]);
            }
            return sb.ToString();
        }

        /// <summary>그 언어의 기호 글. 엔진 기본 번역이 늘 넣으므로 없을 때의 값은 만일을 위한 것이다.</summary>
        private static string Symbol(FluentBundle bundle, string key, string fallback) =>
            bundle.HasMessage(key) && bundle.TryGetMessage(key, null, out _, out var text) && text != null ? text : fallback;

        private static string Percent(FluentBundle bundle, string number)
        {
            var args = new Dictionary<string, IFluentType> { [PercentArgument] = new FluentString(number) };
            return bundle.HasMessage(PercentKey) && bundle.TryGetMessage(PercentKey, args, out _, out var text) && text != null
                ? text
                : number + "%";
        }
    }
}
