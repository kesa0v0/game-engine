using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// UXML 읽기 (spec-gaps G330~G345). Unity 로더에 맡기지 않고 XML로 직접 읽어 <c>bind-…</c>·<c>on-…</c>·<c>for</c>·<c>params</c>·
    /// <c>seat</c>·<c>Include</c>를 요소 모델로 바꾸고, 식이 쓰는 변수가 선언된 것인지 본다. 함수가 있는지는 세션이 있어야 알아서
    /// <see cref="UiInterpreter"/>가 올릴 때 본다.
    /// </summary>
    public static class UxmlReader
    {
        private static readonly Regex ForPattern = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s+in\s+(.+?)\s*$", RegexOptions.CultureInvariant);

        private static readonly string[] Events = { "click" };

        /// <summary>
        /// <paramref name="name"/>은 오류에 붙일 파일 이름. <c>ui:</c> 접두사를 선언하지 않은 파일은 선언을 씌워 읽는다.
        /// </summary>
        public static UiReadResult Read(string name, string uxmlText)
        {
            var problems = new List<UiProblem>();
            // 접두사를 선언하지 않은 글은 선언을 씌워 다시 읽는다. 둘 다 안 되면 처음 오류가 줄 번호를 바로 가리킨다
            var xml = TryParse(uxmlText, out var original);
            if (xml == null)
            {
                xml = TryParse("<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + uxmlText + "</ui:UXML>", out _);
                if (xml == null)
                {
                    problems.Add(new UiProblem(name + ":" + original!.LineNumber, "the UXML is not valid XML: " + original.Message));
                    return new UiReadResult(null, problems);
                }
            }

            var styles = new List<string>();
            foreach (var style in xml.Descendants())
            {
                if (!style.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase)) continue;
                string? src = style.Attribute("src")?.Value;
                if (!string.IsNullOrEmpty(src) && !styles.Contains(src!)) styles.Add(src!);
            }

            var top = xml.Root!;
            XElement rootNode;
            var holders = new List<XElement>();
            if (top.Name.LocalName.Equals("UXML", StringComparison.OrdinalIgnoreCase))
            {
                holders.Add(top);
                var children = top.Elements().Where(e => !e.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase)).ToList();
                if (children.Count == 1)
                {
                    rootNode = children[0];
                    holders.Add(rootNode);
                }
                else
                {
                    rootNode = top;
                }
            }
            else
            {
                rootNode = top;
                holders.Add(top);
            }

            var declared = new List<string>();
            string? seat = null;
            foreach (var holder in holders)
            {
                var p = holder.Attribute("params");
                if (p != null && declared.Count == 0)
                {
                    foreach (var part in p.Value.Split(','))
                    {
                        string id = part.Trim();
                        if (id.Length == 0) continue;
                        if (!IsIdentifier(id)) problems.Add(new UiProblem(Place(name, holder), "params: '" + id + "' is not a name"));
                        else if (declared.Contains(id)) problems.Add(new UiProblem(Place(name, holder), "params: '" + id + "' is declared twice"));
                        else declared.Add(id);
                    }
                }
                var s = holder.Attribute("seat");
                if (s != null && seat == null) seat = s.Value.Trim();
            }
            if (seat != null && !declared.Contains(seat))
                problems.Add(new UiProblem(Place(name, holders[holders.Count - 1]), "seat: '" + seat + "' is not one of the params" + UiSuggest.Hint(seat, declared)));

            var scope = new SortedSet<string>(declared, StringComparer.Ordinal);
            var reader = new ElementReader(name, problems, rootNode == top && top.Name.LocalName.Equals("UXML", StringComparison.OrdinalIgnoreCase));
            var root = reader.Read(rootNode, scope, isRoot: true);
            if (problems.Count > 0) return new UiReadResult(null, problems);
            return new UiReadResult(new UiDocument(name, declared, seat, root, styles), problems);
        }

        private static XDocument? TryParse(string text, out XmlException? error)
        {
            try
            {
                error = null;
                return XDocument.Parse(text, LoadOptions.SetLineInfo);
            }
            catch (XmlException e)
            {
                error = e;
                return null;
            }
        }

        private static string Place(string name, XObject node) =>
            name + ":" + (node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0);

        private static bool IsIdentifier(string text)
        {
            if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_')) return false;
            foreach (char c in text)
                if (!(c < 128 && (char.IsLetterOrDigit(c) || c == '_'))) return false;
            return true;
        }

        private sealed class ElementReader
        {
            private readonly string _name;
            private readonly List<UiProblem> _problems;
            private readonly bool _syntheticRoot;

            public ElementReader(string name, List<UiProblem> problems, bool syntheticRoot)
            {
                _name = name;
                _problems = problems;
                _syntheticRoot = syntheticRoot;
            }

            public UiElement Read(XElement node, SortedSet<string> scope, bool isRoot)
            {
                string place = Place(_name, node);
                string kind = node.Name.LocalName;
                if (isRoot && _syntheticRoot) kind = "VisualElement";

                var attributes = new List<KeyValuePair<string, string>>();
                var bindings = new List<UiBinding>();
                var events = new List<UiEvent>();
                var includeArguments = new List<KeyValuePair<string, UiExpr>>();
                UiRepeat? repeat = null;
                string? includeSource = null;
                bool isInclude = kind.Equals("Include", StringComparison.OrdinalIgnoreCase);

                foreach (var attribute in node.Attributes())
                {
                    // 이름 공간이 붙은 속성(xsi:…)과 UXML 머리에만 있는 편집기 속성은 요소의 것이 아니다
                    if (attribute.IsNamespaceDeclaration || attribute.Name.NamespaceName.Length > 0) continue;
                    if (kind.Equals("UXML", StringComparison.OrdinalIgnoreCase) || (isRoot && _syntheticRoot))
                    {
                        if (attribute.Name.LocalName == "editor-extension-mode") continue;
                    }
                    string n = attribute.Name.LocalName;
                    string v = attribute.Value;

                    if (n == "for")
                    {
                        if (isInclude) Problem(place, "for cannot be on an Include");
                        else repeat = ReadRepeat(place, v, scope);
                    }
                    else if (n == "params" || n == "seat")
                    {
                        if (!isRoot) Problem(place, n + " can only be on the top element of a file");
                    }
                    else if (isInclude)
                    {
                        if (n == "src") includeSource = v;
                        else
                        {
                            var expr = ParseExpr(place, n, v, scope);
                            if (expr != null) includeArguments.Add(new KeyValuePair<string, UiExpr>(n, expr));
                        }
                    }
                    else if (n.StartsWith("bind-class-", StringComparison.Ordinal))
                    {
                        string className = n.Substring("bind-class-".Length);
                        var expr = ParseExpr(place, n, v, scope);
                        if (expr != null) bindings.Add(new UiBinding(UiBindKind.ClassToggle, className, expr, v));
                    }
                    else if (n == "bind-class")
                    {
                        var expr = ParseExpr(place, n, v, scope);
                        if (expr != null) bindings.Add(new UiBinding(UiBindKind.ClassNames, "", expr, v));
                    }
                    else if (n.StartsWith("bind-", StringComparison.Ordinal))
                    {
                        string property = n.Substring("bind-".Length);
                        if (!UiProperties.TryGet(property, out _))
                        {
                            Problem(place, n + ": '" + property + "' is not a property that can be bound" + UiSuggest.Hint(property, UiProperties.Names) +
                                " (can be: " + string.Join(", ", UiProperties.Names) + ")");
                            continue;
                        }
                        var expr = ParseExpr(place, n, v, scope);
                        if (expr != null) bindings.Add(new UiBinding(UiBindKind.Property, property, expr, v));
                    }
                    else if (n.StartsWith("on-", StringComparison.Ordinal))
                    {
                        string eventName = n.Substring("on-".Length);
                        if (Array.IndexOf(Events, eventName) < 0)
                        {
                            Problem(place, n + ": '" + eventName + "' is not an event that can be handled" + UiSuggest.Hint(eventName, Events) +
                                " (can be: " + string.Join(", ", Events) + ")");
                            continue;
                        }
                        var expr = ParseExpr(place, n, v, scope);
                        if (expr == null) continue;
                        if (!(expr is CallExpr call)) Problem(place, n + "=\"" + v + "\" must be a call of a command or a UI action, like 'mymod.name(args)'");
                        else events.Add(new UiEvent(eventName, call, v));
                    }
                    else
                    {
                        attributes.Add(new KeyValuePair<string, string>(n, v));
                    }
                }

                UiInclude? include = null;
                if (isInclude)
                {
                    if (includeSource == null) Problem(place, "Include needs a src");
                    else include = new UiInclude(includeSource, includeArguments);
                }

                var childScope = scope;
                if (repeat != null)
                {
                    childScope = new SortedSet<string>(scope, StringComparer.Ordinal) { repeat.Variable };
                }
                var children = new List<UiElement>();
                foreach (var child in node.Elements())
                {
                    if (child.Name.LocalName.Equals("Style", StringComparison.OrdinalIgnoreCase)) continue;
                    children.Add(Read(child, childScope, isRoot: false));
                }
                if (isInclude && children.Count > 0) Problem(place, "an Include has no children");

                return new UiElement(kind, place, attributes, bindings, events, repeat, include, children);
            }

            private UiRepeat? ReadRepeat(string place, string text, SortedSet<string> scope)
            {
                var match = ForPattern.Match(text);
                if (!match.Success)
                {
                    Problem(place, "for=\"" + text + "\" must be written 'variable in mymod.function(args)'");
                    return null;
                }
                string variable = match.Groups[1].Value;
                var source = ParseExpr(place, "for", match.Groups[2].Value, scope);
                return source == null ? null : new UiRepeat(variable, source, text);
            }

            private UiExpr? ParseExpr(string place, string attribute, string text, SortedSet<string> scope)
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
                bool ok = true;
                foreach (var variable in Variables(expr))
                {
                    if (scope.Contains(variable.Name)) continue;
                    Problem(place, attribute + "=\"" + text + "\": the variable '" + variable.Name + "' is not declared" +
                        UiSuggest.Hint(variable.Name, scope) +
                        " (declare it with params on the top element or for=\"" + variable.Name + " in …\")");
                    ok = false;
                }
                return ok ? expr : null;
            }

            private void Problem(string place, string message) => _problems.Add(new UiProblem(place, message));
        }

        /// <summary>식이 쓰는 변수들(호출의 인자 포함).</summary>
        internal static IEnumerable<VarExpr> Variables(UiExpr expr)
        {
            switch (expr)
            {
                case VarExpr v:
                    yield return v;
                    break;
                case CallExpr c:
                    foreach (var a in c.Arguments)
                        foreach (var inner in Variables(a))
                            yield return inner;
                    break;
            }
        }
    }
}
