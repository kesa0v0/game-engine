using System;
using System.Collections.Generic;
using System.Linq;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 모드들의 입력 선언(<c>.input.xml</c>)을 로드 순서대로 모은 것 (G417·G423). 같은 이름은 뒤 모드가 이기고, 한 모드 안의 같은 이름은 문제다.
    /// 내장 <c>primary</c>·<c>secondary</c>는 선언하지 않는다.
    /// </summary>
    public sealed class InputLibrary
    {
        public const string Extension = ".input.xml";

        private const int MaxModifiers = 2;

        private InputLibrary(IReadOnlyDictionary<string, InputDeclaration> inputs, IReadOnlyList<UiProblem> problems)
        {
            Inputs = inputs;
            Problems = problems;
        }

        public IReadOnlyDictionary<string, InputDeclaration> Inputs { get; }

        /// <summary>읽다 난 문제. 틀린 선언만 빠지고 나머지는 쓴다.</summary>
        public IReadOnlyList<UiProblem> Problems { get; }

        /// <summary>
        /// <paramref name="sources"/>는 모드의 로드 순서대로다. 확장자가 <c>.input.xml</c>이 아닌 파일은 건너뛴다.
        /// </summary>
        /// <param name="keys">호스트가 아는 키 이름(Unity는 Input System <c>Key</c>). 대소문자를 가리지 않고 맞추고 이 철자로 바꾼다.</param>
        public static InputLibrary Load(IEnumerable<LookSource> sources, IEnumerable<string> keys)
        {
            var known = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
                if (!known.ContainsKey(key)) known.Add(key, key);
            var problems = new List<UiProblem>();
            var inputs = new SortedDictionary<string, InputDeclaration>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (!source.Path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;
                var root = LookReader.Parse(source, "Inputs", problems);
                if (root == null) continue;
                foreach (var node in root.Elements())
                {
                    string place = LookReader.Place(source.Path, node);
                    if (node.Name.LocalName != "Input")
                    {
                        problems.Add(new UiProblem(place, "an input file holds <Input name=\"…\" key=\"…\"/> elements, got <" + node.Name.LocalName + ">"));
                        continue;
                    }
                    if (node.HasElements) problems.Add(new UiProblem(place, "<Input> has no children"));
                    foreach (var attribute in node.Attributes())
                        if (!attribute.IsNamespaceDeclaration && attribute.Name.LocalName != "name" && attribute.Name.LocalName != "key")
                            problems.Add(new UiProblem(place, "<Input> takes only name and key, got '" + attribute.Name.LocalName + "'"));
                    string? name = LookReader.Name(node, place, "Input", problems);
                    if (name == null) continue;
                    if (name == LookBoard.Primary || name == LookBoard.Secondary)
                    {
                        problems.Add(new UiProblem(place, "'" + name + "' is a built-in input; it is not declared"));
                        continue;
                    }
                    string? text = node.Attribute("key")?.Value;
                    if (text == null)
                    {
                        problems.Add(new UiProblem(place, "<Input> needs a key, like key=\"R\" or key=\"SHIFT + secondary\""));
                        continue;
                    }
                    var chord = Chord(text, known, place, problems);
                    if (chord == null) continue;
                    if (inputs.TryGetValue(name, out var earlier) && earlier.Mod == source.Mod)
                    {
                        problems.Add(new UiProblem(place, "the input '" + name + "' is already declared at " + earlier.Place));
                        continue;
                    }
                    inputs[name] = new InputDeclaration(name, source.Mod, place, chord);
                }
            }
            return new InputLibrary(inputs, problems);
        }

        private static InputChord? Chord(string text, SortedDictionary<string, string> known, string place, List<UiProblem> problems)
        {
            string at = "key=\"" + text + "\": ";
            var parts = text.Split('+').Select(p => p.Trim()).ToList();
            if (parts.Any(p => p.Length == 0))
            {
                problems.Add(new UiProblem(place, at + "write modifiers and one key joined by '+', like \"SHIFT + R\""));
                return null;
            }
            var modifiers = new List<InputModifier>();
            foreach (var part in parts.Take(parts.Count - 1))
            {
                if (!TryModifier(part, out var modifier))
                {
                    problems.Add(new UiProblem(place, at + "'" + part + "' is not a modifier" + UiSuggest.Hint(part.ToUpperInvariant(), ModifierNames) +
                        " (can be: " + string.Join(", ", ModifierNames) + "; the key comes last)"));
                    return null;
                }
                if (modifiers.Contains(modifier))
                {
                    problems.Add(new UiProblem(place, at + part + " is written twice"));
                    return null;
                }
                modifiers.Add(modifier);
            }
            if (modifiers.Count > MaxModifiers)
            {
                problems.Add(new UiProblem(place, at + "at most " + MaxModifiers + " modifiers"));
                return null;
            }
            modifiers.Sort((a, b) => ((int)a).CompareTo((int)b));
            string last = parts[parts.Count - 1];
            if (TryModifier(last, out _))
            {
                problems.Add(new UiProblem(place, at + "ends with the modifier " + last + "; the last part is the key"));
                return null;
            }
            string key;
            if (string.Equals(last, LookBoard.Primary, StringComparison.OrdinalIgnoreCase)) key = LookBoard.Primary;
            else if (string.Equals(last, LookBoard.Secondary, StringComparison.OrdinalIgnoreCase)) key = LookBoard.Secondary;
            else if (!known.TryGetValue(last, out key!))
            {
                problems.Add(new UiProblem(place, at + "'" + last + "' is not a key name" +
                    UiSuggest.Hint(last, known.Values.Append(LookBoard.Primary).Append(LookBoard.Secondary))));
                return null;
            }
            return new InputChord(modifiers, key);
        }

        private static readonly string[] ModifierNames = { "SHIFT", "CTRL", "ALT" };

        private static bool TryModifier(string text, out InputModifier modifier)
        {
            switch (text.ToUpperInvariant())
            {
                case "SHIFT": modifier = InputModifier.Shift; return true;
                case "CTRL": modifier = InputModifier.Ctrl; return true;
                case "ALT": modifier = InputModifier.Alt; return true;
                default: modifier = default; return false;
            }
        }
    }
}
