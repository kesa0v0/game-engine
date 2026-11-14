using System;
using System.Collections.Generic;
using System.Linq;
using Kesa.Engine.Bridge;

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

        /// <summary>내장 입력이거나 선언된 입력인가.</summary>
        public bool Knows(string input) => input == LookBoard.Primary || input == LookBoard.Secondary || Inputs.ContainsKey(input);

        /// <summary>
        /// 선언에 없는 입력을 받는 핸들러 (G417·G424). 키가 없어 그 핸들러는 눌릴 수 없다. <paramref name="spaces"/>를 주면 모양 종류가 붙은 입력의
        /// 대상이 공간 이름이 아닌 핸들러도 알린다 (G484: 끌기는 공간만 받는다).
        /// </summary>
        public IReadOnlyList<UiProblem> CheckHandlers(IEnumerable<BridgeWorldInput> handlers, IEnumerable<string>? spaces = null)
        {
            var spaceNames = spaces == null ? null : new SortedSet<string>(spaces, StringComparer.Ordinal);
            var problems = new List<UiProblem>();
            foreach (var handler in handlers)
            {
                string written = " (@world_input(\"" + handler.Input + "\", \"" + handler.Target + "\"))";
                if (!Knows(handler.Input))
                    problems.Add(new UiProblem(handler.Action, Unknown(handler.Input) + written));
                else if (spaceNames != null && Inputs.TryGetValue(handler.Input, out var declared) && declared.Kind != null &&
                         !spaceNames.Contains(handler.Target))
                    problems.Add(new UiProblem(handler.Action, "the input '" + handler.Input + "' is drawn (kind=\"" + declared.Kind +
                        "\"), so its handler takes a space, and no space is named '" + handler.Target + "'" +
                        UiSuggest.Hint(handler.Target, spaceNames) + written + " (spec-gaps G484)"));
            }
            return problems;
        }

        /// <summary>모양 종류가 붙은 입력이면 그 선언.</summary>
        internal InputDeclaration? Drawn(string input) => Inputs.TryGetValue(input, out var declared) && declared.Shape != null ? declared : null;

        /// <summary>포인터 단추로 내는 입력인가 (내장 <c>primary</c>·<c>secondary</c>와 그 조합, G485).</summary>
        public bool IsPointer(string input) =>
            input == LookBoard.Primary || input == LookBoard.Secondary || Inputs.TryGetValue(input, out var declared) && declared.Key.IsPointer;

        internal string Unknown(string input) =>
            "no " + Extension + " declares the input '" + input + "'" +
            UiSuggest.Hint(input, Inputs.Keys.Append(LookBoard.Primary).Append(LookBoard.Secondary)) + ", so no key reaches it";

        /// <summary>
        /// <paramref name="sources"/>는 모드의 로드 순서대로다. 확장자가 <c>.input.xml</c>이 아닌 파일은 건너뛴다.
        /// </summary>
        /// <param name="keys">호스트가 아는 키 이름(Unity는 Input System <c>Key</c>). 대소문자를 가리지 않고 맞추고 이 철자로 바꾼다.</param>
        /// <param name="kinds">모양 종류(<c>kind=</c>, G489)를 찾을 등록 자리. 없으면 <see cref="LookKinds.Standard"/>.</param>
        public static InputLibrary Load(IEnumerable<LookSource> sources, IEnumerable<string> keys, LookKinds? kinds = null)
        {
            kinds ??= LookKinds.Standard();
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
                    string? kindName = node.Attribute("kind")?.Value;
                    if (kindName == null)
                        foreach (var attribute in node.Attributes())
                            if (!attribute.IsNamespaceDeclaration && attribute.Name.LocalName != "name" && attribute.Name.LocalName != "key")
                                problems.Add(new UiProblem(place, "<Input> takes only name, key and kind, got '" + attribute.Name.LocalName + "'"));
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
                    GestureShape? shape = null;
                    if (kindName != null)
                    {
                        shape = Shape(node, kindName, chord, kinds, place, problems, out bool usable);
                        if (!usable) continue;
                    }
                    if (inputs.TryGetValue(name, out var earlier) && earlier.Mod == source.Mod)
                    {
                        problems.Add(new UiProblem(place, "the input '" + name + "' is already declared at " + earlier.Place));
                        continue;
                    }
                    inputs[name] = new InputDeclaration(name, source.Mod, place, chord, kindName, shape);
                }
            }
            return new InputLibrary(inputs, problems);
        }

        /// <summary>
        /// <c>kind=</c>의 모양 (G489): 이름 밖의 속성은 그 종류의 설정이다. 키는 포인터 단추여야 한다. 쓸 수 없는 선언이면 <paramref name="usable"/>이
        /// 거짓. 모르는 종류를 받아 주는 도구(<see cref="LookKinds.AllowUnknown"/>)에서는 모양 없이 남긴다.
        /// </summary>
        private static GestureShape? Shape(System.Xml.Linq.XElement node, string kindName, InputChord chord, LookKinds kinds, string place,
            List<UiProblem> problems, out bool usable)
        {
            usable = false;
            if (!chord.IsPointer)
            {
                problems.Add(new UiProblem(place, "kind=\"" + kindName + "\" is drawn with a pointer button, so its key ends with primary or secondary, got key=\"" +
                    chord + "\""));
                return null;
            }
            if (!kinds.TryGetGesture(kindName, out var kind))
            {
                if (kinds.AllowUnknown)
                {
                    problems.Add(LookReader.Unverified(place, "kind=\"" + kindName + "\"", "input kind", "settings", kindName, kinds.GestureNames));
                    usable = true;
                    return null;
                }
                problems.Add(new UiProblem(place, "kind=\"" + kindName + "\": no input kind is registered with this name" +
                    UiSuggest.Hint(kindName, kinds.GestureNames) + " (can be: " + string.Join(", ", kinds.GestureNames) + ")"));
                return null;
            }
            var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
            bool known = true;
            foreach (var attribute in node.Attributes())
            {
                string n = attribute.Name.LocalName;
                if (attribute.IsNamespaceDeclaration || n == "name" || n == "key" || n == "kind") continue;
                if (!kind.Settings.Contains(n))
                {
                    problems.Add(new UiProblem(place, n + ": the input kind '" + kindName + "' " + (kind.Settings.Count == 0
                        ? "has no settings"
                        : "has no such setting" + UiSuggest.Hint(n, kind.Settings) + " (can be: " + string.Join(", ", kind.Settings) + ")")));
                    known = false;
                    continue;
                }
                settings[n] = attribute.Value;
            }
            if (!known) return null;
            var messages = new List<string>();
            var shape = kind.Configure(settings, messages);
            foreach (var message in messages) problems.Add(new UiProblem(place, message));
            usable = shape != null;
            return shape;
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
