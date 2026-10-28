// 값 변환은 OneJS(https://github.com/Singtaa/OneJS, MIT, Copyright (c) 2024 Singtaa)의 Runtime/Styling/UssCompiler.cs를 참고해 고쳐 옮김.
// 고지 전문은 이 패키지의 THIRD_PARTY_NOTICES.md.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Kesa.Engine.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kesa.Engine.Unity
{
    /// <summary>
    /// 해석기가 읽은 USS(<see cref="UssSheet"/>)를 진짜 <see cref="StyleSheet"/>로 만든다 (G359). 캐스케이드·상태·선택자 맞추기·<c>var()</c>는
    /// Unity가 한다. 값을 바꾸지 못한 선언은 빼고 문제로 돌려준다(G361·G364). 만든 시트는 같은 글을 Unity 에디터가 임포트한 것과 같은
    /// 모양이어야 한다(<c>SmokeTest</c>가 비교한다).
    /// </summary>
    public static class UssStyleSheets
    {
        /// <param name="images"><c>url()</c>의 모드 가상 경로로 그림을 찾는다(G363).</param>
        public static StyleSheet Build(UssSheet sheet, Func<string, Texture2D?> images, List<UiProblem> problems)
        {
            var builder = new UssBuilder();
            foreach (var rule in sheet.Rules)
            {
                builder.BeginRule(rule.Line);
                foreach (var selector in rule.Selectors)
                {
                    using (builder.BeginComplexSelector(selector.Specificity))
                    {
                        // Unity는 관계를 오른쪽 조각에 붙인다(왼쪽 조각과의 관계)
                        var previous = UssCombinator.None;
                        foreach (var compound in selector.Compounds)
                        {
                            var parts = new List<(string, string)>();
                            foreach (var part in compound.Parts) parts.Add((part.Kind.ToString(), part.Value));
                            builder.AddSimpleSelector(parts, previous switch
                            {
                                UssCombinator.Child => "Child",
                                UssCombinator.Descendant => "Descendent",
                                _ => "None",
                            });
                            previous = compound.Next;
                        }
                    }
                }
                foreach (var declaration in rule.Declarations)
                {
                    // 반쯤 쓴 속성이 시트에 남지 않게 값을 모두 바꾼 뒤에 쓴다
                    var writes = new List<Action<UssBuilder>>();
                    string? why = Convert(declaration.Values, writes, images);
                    if (why != null)
                    {
                        problems.Add(new UiProblem(sheet.Name + ":" + declaration.Line, declaration.Name + ": " + why));
                        continue;
                    }
                    builder.BeginProperty(declaration.Name, declaration.Line);
                    foreach (var write in writes) write(builder);
                    builder.EndProperty();
                }
                builder.EndRule();
            }
            var result = ScriptableObject.CreateInstance<StyleSheet>();
            result.name = sheet.Name;
            builder.BuildTo(result);
            return result;
        }

        private static string? Convert(IReadOnlyList<UssValue> values, List<Action<UssBuilder>> writes, Func<string, Texture2D?> images)
        {
            foreach (var value in values)
            {
                string? why = Convert(value, writes, images);
                if (why != null) return why;
            }
            return null;
        }

        private static string? Convert(UssValue value, List<Action<UssBuilder>> writes, Func<string, Texture2D?> images)
        {
            switch (value.Kind)
            {
                case UssValueKind.Comma:
                    writes.Add(b => b.AddComma());
                    return null;
                case UssValueKind.String:
                    writes.Add(b => b.AddString(value.Text));
                    return null;
                case UssValueKind.Function:
                    return ConvertFunction(value, writes, images);
                default:
                    return ConvertWord(value.Text, writes);
            }
        }

        private static string? ConvertFunction(UssValue value, List<Action<UssBuilder>> writes, Func<string, Texture2D?> images)
        {
            string name = value.Text;
            var args = value.Arguments;
            if (name == "url")
            {
                string path = args[0].Text;
                var texture = images(path);
                if (texture == null) return "url(\"" + path + "\") was not found in the mods (only images can be loaded for now)";
                writes.Add(b => b.AddAsset(texture));
                return null;
            }
            if (name == "rgb" || name == "rgba")
            {
                if (!TryRgb(args, out var color)) return name + "(…) takes three numbers 0-255 and an optional alpha 0-1";
                writes.Add(b => b.AddColor(color));
                return null;
            }
            if (name == "var")
            {
                writes.Add(b => b.AddFunction("var", args.Count));
                writes.Add(b => b.AddVariable(args[0].Text));
                for (int i = 1; i < args.Count; i++)
                {
                    string? why = Convert(args[i], writes, images);
                    if (why != null) return why;
                }
                return null;
            }
            if (!UssBuilder.IsFunction(name)) return "'" + name + "()' is not a USS function";
            writes.Add(b => b.AddFunction(name, args.Count));
            return Convert(args, writes, images);
        }

        private static string? ConvertWord(string word, List<Action<UssBuilder>> writes)
        {
            if (word.StartsWith("#", StringComparison.Ordinal))
            {
                if (!ColorUtility.TryParseHtmlString(word, out var hex)) return "'" + word + "' is not a colour";
                writes.Add(b => b.AddColor(hex));
                return null;
            }
            if (TryNumber(word, out float number, out string unit))
            {
                if (unit.Length == 0) writes.Add(b => b.AddFloat(number));
                else if (UssBuilder.IsUnit(unit)) writes.Add(b => b.AddDimension(number, unit));
                else return "'" + unit + "' is not a USS unit (px, %, s, ms, deg, grad, rad, turn)";
                return null;
            }
            if (UssBuilder.IsKeyword(word))
            {
                writes.Add(b => b.AddKeyword(word));
                return null;
            }
            // 색 이름(red 등)도 Unity 임포터처럼 낱말로 두고 Unity가 적용할 때 바꾼다
            writes.Add(b => b.AddEnum(word));
            return null;
        }

        private static bool TryNumber(string word, out float number, out string unit)
        {
            int end = 0;
            if (end < word.Length && (word[end] == '-' || word[end] == '+')) end++;
            int digits = end;
            while (end < word.Length && (char.IsDigit(word[end]) || word[end] == '.')) end++;
            unit = word.Substring(end);
            number = 0;
            return end > digits && float.TryParse(word.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static bool TryRgb(IReadOnlyList<UssValue> args, out Color color)
        {
            color = default;
            var numbers = new List<float>();
            for (int i = 0; i < args.Count; i++)
            {
                bool comma = i % 2 == 1;
                if (comma != (args[i].Kind == UssValueKind.Comma)) return false;
                if (comma) continue;
                if (args[i].Kind != UssValueKind.Word || !TryNumber(args[i].Text, out float n, out string unit) || unit.Length > 0) return false;
                numbers.Add(n);
            }
            if (numbers.Count != 3 && numbers.Count != 4) return false;
            color = new Color(numbers[0] / 255f, numbers[1] / 255f, numbers[2] / 255f, numbers.Count == 4 ? numbers[3] : 1f);
            return true;
        }
    }
}
