using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 그림 정의(<c>.look.xml</c>, G409)와 공간 선언(<c>.space.xml</c>, G411) 읽기. UXML의 식·<c>for</c> 표기를 다시 쓰고(G390) 식의 이름은
    /// 그림 값의 키라 모드 함수 호출은 받지 않는다. 요소·공간 종류와 그 속성은 <see cref="LookKinds"/>의 등록으로 검사한다 (G413).
    /// </summary>
    internal static class LookReader
    {
        public const string LookExtension = ".look.xml";
        public const string SpaceExtension = ".space.xml";

        private static readonly Regex ForPattern = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s+in\s+(.+)$", RegexOptions.CultureInvariant);

        public static void ReadLooks(LookSource source, LookKinds kinds, List<LookDefinition> looks, List<UiProblem> problems)
        {
            var root = Parse(source, "Looks", problems);
            if (root == null) return;
            var reader = new ElementReader(source.Path, kinds, problems);
            foreach (var node in root.Elements())
            {
                string place = Place(source.Path, node);
                if (node.Name.LocalName != "Look")
                {
                    problems.Add(new UiProblem(place, "a look file holds <Look name=\"…\"> elements, got <" + node.Name.LocalName + ">" +
                        (node.Name.LocalName == "Space" ? " (spaces go in a " + SpaceExtension + " file)" : "")));
                    continue;
                }
                string? name = Name(node, place, "Look", problems);
                var timing = new SortedDictionary<string, LookAttributeValue>(StringComparer.Ordinal);
                var timingBindings = new List<LookBinding>();
                string? exit = null;
                foreach (var attribute in node.Attributes())
                {
                    string n = attribute.Name.LocalName;
                    if (attribute.IsNamespaceDeclaration || n == "name") continue;
                    if (n == "exit")
                    {
                        if (attribute.Value.Length == 0) problems.Add(new UiProblem(place, "exit=\"\" names no look"));
                        else exit = attribute.Value;
                        continue;
                    }
                    if (n == "bind-exit")
                    {
                        problems.Add(new UiProblem(place, "exit is written, not bound: exit=\"look-name\" (spec-gaps G431)"));
                        continue;
                    }
                    string target = n.StartsWith("bind-", StringComparison.Ordinal) ? n.Substring("bind-".Length) : n;
                    if (!LookDefinition.TimingNames.Contains(target))
                    {
                        problems.Add(new UiProblem(place, "<Look> takes name, exit and the effect times " + string.Join(", ", LookDefinition.TimingNames) +
                            UiSuggest.Hint(target, LookDefinition.TimingNames) + "; put '" + n + "' on an element inside it"));
                        continue;
                    }
                    if (target != n)
                    {
                        var expr = reader.ParseVariable(place, n, attribute.Value);
                        if (expr != null) timingBindings.Add(new LookBinding(target, LookDefinition.TimingType(target), expr, attribute.Value));
                        continue;
                    }
                    var value = LookAttributes.FromText(LookDefinition.TimingType(n), attribute.Value, out string error);
                    if (value == null) problems.Add(new UiProblem(place, n + "=\"" + attribute.Value + "\": " + error));
                    else if (LookDefinition.IsDuration(n) && value.Numbers[0] < 0) problems.Add(new UiProblem(place, n + "=\"" + attribute.Value + "\": a time is not negative"));
                    else timing[n] = value;
                }
                bool Has(string field) => timing.ContainsKey(field) || timingBindings.Any(b => b.Attribute == field);
                if (Has("rise") && Has("drift"))
                    problems.Add(new UiProblem(place, "<Look> has both rise and drift; rise=\"r\" is drift=\"0,r\", so use one"));
                var elements = node.Elements().Select(e => reader.Read(e, null)).ToList();
                if (name != null) looks.Add(new LookDefinition(name, source.Mod, place, elements, timing, timingBindings, exit));
            }
        }

        public static void ReadSpaces(LookSource source, LookKinds kinds, List<SpaceDeclaration> spaces, List<UiProblem> problems)
        {
            var root = Parse(source, "Spaces", problems);
            if (root == null) return;
            foreach (var node in root.Elements())
            {
                string place = Place(source.Path, node);
                if (node.Name.LocalName != "Space")
                {
                    problems.Add(new UiProblem(place, "a space file holds <Space name=\"…\" kind=\"…\"/> elements, got <" + node.Name.LocalName + ">" +
                        (node.Name.LocalName == "Look" ? " (looks go in a " + LookExtension + " file)" : "")));
                    continue;
                }
                string? name = Name(node, place, "Space", problems);
                string? kindName = node.Attribute("kind")?.Value;
                if (node.HasElements) problems.Add(new UiProblem(place, "<Space> has no children"));
                if (kindName == null)
                {
                    problems.Add(new UiProblem(place, "<Space> needs a kind (can be: " + string.Join(", ", kinds.SpaceNames) + ")"));
                    continue;
                }
                if (!kinds.TryGetSpace(kindName, out var kind))
                {
                    problems.Add(new UiProblem(place, "kind=\"" + kindName + "\": no space kind is registered with this name" +
                        UiSuggest.Hint(kindName, kinds.SpaceNames) + " (can be: " + string.Join(", ", kinds.SpaceNames) + ")"));
                    continue;
                }
                var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
                bool known = true;
                foreach (var attribute in node.Attributes())
                {
                    string n = attribute.Name.LocalName;
                    if (attribute.IsNamespaceDeclaration || n == "name" || n == "kind") continue;
                    if (!kind.Settings.Contains(n))
                    {
                        problems.Add(new UiProblem(place, n + ": the space kind '" + kindName + "' has no such setting" + UiSuggest.Hint(n, kind.Settings) +
                            " (can be: " + string.Join(", ", kind.Settings) + ")"));
                        known = false;
                        continue;
                    }
                    settings[n] = attribute.Value;
                }
                if (!known) continue;
                var messages = new List<string>();
                var layout = kind.Configure(settings, messages);
                foreach (var message in messages) problems.Add(new UiProblem(place, message));
                if (layout != null && name != null) spaces.Add(new SpaceDeclaration(name, kindName, source.Mod, place, layout));
            }
        }

        public static XElement? Parse(LookSource source, string rootName, List<UiProblem> problems)
        {
            XDocument xml;
            try
            {
                xml = XDocument.Parse(source.Text, LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                problems.Add(new UiProblem(source.Path + ":" + e.LineNumber, "not valid XML: " + e.Message));
                return null;
            }
            var root = xml.Root!;
            if (root.Name.LocalName != rootName)
            {
                problems.Add(new UiProblem(Place(source.Path, root), "the top element is <" + rootName + ">, got <" + root.Name.LocalName + ">"));
                return null;
            }
            return root;
        }

        public static string? Name(XElement node, string place, string element, List<UiProblem> problems)
        {
            string? name = node.Attribute("name")?.Value.Trim();
            if (string.IsNullOrEmpty(name))
            {
                problems.Add(new UiProblem(place, "<" + element + "> needs a name"));
                return null;
            }
            return name;
        }

        public static string Place(string path, XObject node) =>
            path + ":" + (node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0);

        private sealed class ElementReader
        {
            private readonly string _path;
            private readonly LookKinds _kinds;
            private readonly List<UiProblem> _problems;

            public ElementReader(string path, LookKinds kinds, List<UiProblem> problems)
            {
                _path = path;
                _kinds = kinds;
                _problems = problems;
            }

            /// <param name="parent">부모 요소의 종류 이름, 그림 정의 바로 아래면 null.</param>
            public LookElement Read(XElement node, string? parent)
            {
                string place = Place(_path, node);
                string kindName = node.Name.LocalName;
                var attributes = new List<KeyValuePair<string, LookAttributeValue>>();
                var bindings = new List<LookBinding>();
                LookRepeat? repeat = null;

                if (kindName == "Include") Problem(place, "Include in a look file is not supported yet");
                bool known = _kinds.TryGetElement(kindName, out var kind);
                if (!known && kindName != "Include")
                    Problem(place, "<" + kindName + ">: no look element is registered with this name" + UiSuggest.Hint(kindName, _kinds.ElementNames) +
                        " (can be: " + string.Join(", ", _kinds.ElementNames) + ")");
                if (known && kind.Parents != null && (parent == null || !kind.Parents.Contains(parent)))
                    Problem(place, "<" + kindName + "> goes directly inside " + string.Join(" or ", kind.Parents.Select(p => "<" + p + ">")) +
                        (parent == null ? ", not at the top of a look" : ", not inside <" + parent + ">"));

                foreach (var attribute in node.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length > 0 || !known) continue;
                    string n = attribute.Name.LocalName;
                    string v = attribute.Value;
                    if (n == "for")
                    {
                        repeat = ReadRepeat(place, v);
                        continue;
                    }
                    if (n.StartsWith("bind-", StringComparison.Ordinal))
                    {
                        string target = n.Substring("bind-".Length);
                        LookAttributeType type;
                        if (target == "display") type = LookAttributeType.Flag;
                        else if (!TryAttribute(kind, target, out type))
                        {
                            Problem(place, n + ": <" + kindName + "> has no attribute '" + target + "'" + Known(kind, target, withDisplay: true));
                            continue;
                        }
                        var expr = ParseVariable(place, n, v);
                        if (expr != null) bindings.Add(new LookBinding(target, type, expr, v));
                        continue;
                    }
                    if (!TryAttribute(kind, n, out var attributeType))
                    {
                        Problem(place, n + ": <" + kindName + "> has no attribute '" + n + "'" + Known(kind, n, withDisplay: false));
                        continue;
                    }
                    var value = LookAttributes.FromText(attributeType, v, out string error);
                    string? wrong = value != null ? kind.Check?.Invoke(n, value) : error;
                    if (wrong != null) Problem(place, n + "=\"" + v + "\": " + wrong);
                    else attributes.Add(new KeyValuePair<string, LookAttributeValue>(n, value!));
                }

                var children = node.Elements().Select(e => Read(e, kindName)).ToList();
                if (known && children.Count > 0 && !kind.AcceptsChildren) Problem(place, "<" + kindName + "> has no children");
                if (repeat != null && children.Count == 0) Problem(place, "for=\"" + repeat.Text + "\" repeats the children, but there are none");
                var element = new LookElement(kindName, place, attributes, bindings, repeat, children);
                if (known && kind.Validate != null)
                    foreach (var wrong in kind.Validate(element)) Problem(place, wrong);
                return element;
            }

            private static bool TryAttribute(LookElementKind kind, string name, out LookAttributeType type) =>
                kind.Attributes.TryGetValue(name, out type) || LookKinds.Common.TryGetValue(name, out type);

            private static string Known(LookElementKind kind, string wrong, bool withDisplay)
            {
                var names = kind.Attributes.Keys.Concat(LookKinds.Common.Keys).ToList();
                if (withDisplay) names.Add("display");
                names.Sort(StringComparer.Ordinal);
                return UiSuggest.Hint(wrong, names) + " (can be: " + string.Join(", ", names) + ")";
            }

            private LookRepeat? ReadRepeat(string place, string text)
            {
                var match = ForPattern.Match(text);
                if (!match.Success)
                {
                    Problem(place, "for=\"" + text + "\" must be written 'variable in key'");
                    return null;
                }
                var source = ParseVariable(place, "for", match.Groups[2].Value);
                return source == null ? null : new LookRepeat(match.Groups[1].Value, source, text);
            }

            public VarExpr? ParseVariable(string place, string attribute, string text)
            {
                UiExpr expr;
                try
                {
                    expr = UiExprParser.Parse(text);
                }
                catch (UiSyntaxException e)
                {
                    Problem(place, attribute + "=\"" + text + "\": " + e.Message);
                    return null;
                }
                if (expr is VarExpr variable) return variable;
                Problem(place, attribute + "=\"" + text + "\": a look reads the keys of the look value, like 'team' or 'gear.items'; " +
                    "it cannot call mod functions (compute it in the function that makes the look)");
                return null;
            }

            private void Problem(string place, string message) => _problems.Add(new UiProblem(place, message));
        }
    }
}
