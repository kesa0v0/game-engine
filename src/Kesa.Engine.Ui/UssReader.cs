using System;
using System.Collections.Generic;
using System.Text;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// USS 글을 읽는다 (G359~G363). 잘못된 규칙이나 선언은 문제로 알리고 빼며 나머지는 읽는다(Unity도 모르는 것은 건너뛴다).
    /// 값은 글자 그대로 두고, Unity 값으로 바꾸는 것은 호스트가 한다.
    /// </summary>
    public static class UssReader
    {
        public static UssReadResult Read(string name, string text)
        {
            var reader = new Reader(name, StripComments(name, text, out var commentProblem));
            if (commentProblem != null) reader.Problems.Add(commentProblem);
            var rules = reader.ReadRules();
            return new UssReadResult(new UssSheet(name, rules), reader.Problems);
        }

        // 주석을 띄어쓰기로 바꾸되 줄바꿈은 남겨 줄 번호를 지킨다
        private static string StripComments(string name, string text, out UiProblem? problem)
        {
            problem = null;
            var result = new StringBuilder(text.Length);
            int line = 1;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\' && i + 1 < text.Length) { result.Append(c).Append(text[++i]); continue; }
                    if (c == quote || c == '\n') quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int start = line;
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? text.Length : end + 2;
                    for (int j = i; j < stop; j++)
                    {
                        if (text[j] == '\n') { result.Append('\n'); line++; }
                        else result.Append(' ');
                    }
                    if (end < 0) problem = new UiProblem(name + ":" + start, "the comment is not closed (*/)");
                    i = stop - 1;
                    continue;
                }
                if (c == '\n') line++;
                result.Append(c);
            }
            return result.ToString();
        }

        private sealed class Reader
        {
            private readonly string _name;
            private readonly string _text;
            private int _pos;
            private int _line = 1;

            public Reader(string name, string text)
            {
                _name = name;
                _text = text;
            }

            public List<UiProblem> Problems { get; } = new List<UiProblem>();

            private void Problem(int line, string message) => Problems.Add(new UiProblem(_name + ":" + line, message));

            public List<UssRule> ReadRules()
            {
                var rules = new List<UssRule>();
                while (true)
                {
                    SkipSpace();
                    if (_pos >= _text.Length) return rules;
                    int line = _line;
                    if (_text[_pos] == '@')
                    {
                        SkipAtRule(line);
                        continue;
                    }
                    if (_text[_pos] == '}')
                    {
                        Problem(line, "'}' without a rule to close");
                        Advance();
                        continue;
                    }
                    string? prelude = ReadUntil('{');
                    if (prelude == null)
                    {
                        Problem(line, "expected '{' after the selector");
                        return rules;
                    }
                    Advance();
                    int blockLine = _line;
                    string? block = ReadUntil('}');
                    if (block == null)
                    {
                        Problem(line, "the rule is not closed ('}')");
                        block = _text.Substring(_pos);
                        _pos = _text.Length;
                    }
                    else Advance();

                    var selectors = ReadSelectors(prelude, line);
                    var declarations = ReadDeclarations(block, blockLine);
                    if (selectors != null) rules.Add(new UssRule(line, selectors, declarations));
                }
            }

            private void SkipAtRule(int line)
            {
                int start = _pos + 1;
                int end = start;
                while (end < _text.Length && IsNameChar(_text[end])) end++;
                string keyword = _text.Substring(start, end - start);
                Problem(line, keyword == "import"
                    ? "@import is not supported yet (put each sheet in its own <Style src>)"
                    : "'@" + keyword + "' is not part of USS");
                while (_pos < _text.Length && _text[_pos] != ';' && _text[_pos] != '{') Advance();
                if (_pos < _text.Length && _text[_pos] == '{')
                {
                    int depth = 0;
                    while (_pos < _text.Length)
                    {
                        char c = _text[_pos];
                        Advance();
                        if (c == '{') depth++;
                        else if (c == '}' && --depth == 0) break;
                    }
                }
                else if (_pos < _text.Length) Advance();
            }

            // 따옴표 안을 건너뛰며 stop 앞까지 읽는다. 못 찾으면 null(위치는 그대로).
            private string? ReadUntil(char stop)
            {
                int start = _pos;
                int line = _line;
                char quote = '\0';
                for (int i = _pos; i < _text.Length; i++)
                {
                    char c = _text[i];
                    if (quote != '\0')
                    {
                        if (c == '\\') { i++; continue; }
                        if (c == quote) quote = '\0';
                    }
                    else if (c == '"' || c == '\'') quote = c;
                    else if (c == stop || (stop == '}' && c == '{'))
                    {
                        if (c == '{' && stop == '}')
                        {
                            Problem(CountLines(start, i, line), "a '{' inside a rule (rules cannot be nested in USS)");
                            continue;
                        }
                        string result = _text.Substring(start, i - start);
                        while (_pos < i) Advance();
                        return result;
                    }
                }
                return null;
            }

            private int CountLines(int from, int to, int line)
            {
                for (int i = from; i < to; i++) if (_text[i] == '\n') line++;
                return line;
            }

            private void Advance()
            {
                if (_text[_pos] == '\n') _line++;
                _pos++;
            }

            private void SkipSpace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) Advance();
            }

            // ---- 선택자 ----

            private List<UssSelector>? ReadSelectors(string prelude, int line)
            {
                var selectors = new List<UssSelector>();
                foreach (var piece in SplitTopLevel(prelude, ','))
                {
                    var selector = ReadSelector(piece.Text, line + piece.Line);
                    if (selector == null) return null;
                    selectors.Add(selector);
                }
                return selectors;
            }

            private UssSelector? ReadSelector(string text, int line)
            {
                var compounds = new List<UssCompound>();
                var parts = new List<UssSelectorPart>();
                var pending = UssCombinator.None;
                int specificity = 1;
                int i = 0;
                string whole = text.Trim();
                if (whole.Length == 0)
                {
                    Problem(line, "an empty selector");
                    return null;
                }
                while (i < text.Length)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c))
                    {
                        if (c == '\n') line++;
                        if (parts.Count > 0 && pending == UssCombinator.None) pending = UssCombinator.Descendant;
                        i++;
                        continue;
                    }
                    if (c == '>')
                    {
                        if (parts.Count == 0 && compounds.Count == 0 || pending == UssCombinator.Child)
                        {
                            Problem(line, "'" + whole + "': '>' needs a selector on each side");
                            return null;
                        }
                        pending = UssCombinator.Child;
                        i++;
                        continue;
                    }
                    if (pending != UssCombinator.None)
                    {
                        compounds.Add(new UssCompound(parts, pending));
                        parts = new List<UssSelectorPart>();
                        pending = UssCombinator.None;
                    }

                    if (c == '*')
                    {
                        if (parts.Count > 0) return BadSelector(line, whole, "'*' must start a selector part");
                        parts.Add(new UssSelectorPart(UssSelectorPartKind.Wildcard, "*"));
                        i++;
                        continue;
                    }
                    if (c == '.' || c == '#' || c == ':')
                    {
                        if (c == ':' && i + 1 < text.Length && text[i + 1] == ':')
                            return BadSelector(line, whole, "pseudo-elements ('::') are not part of USS");
                        int start = ++i;
                        while (i < text.Length && IsNameChar(text[i])) i++;
                        string id = text.Substring(start, i - start);
                        if (id.Length == 0) return BadSelector(line, whole, "'" + c + "' needs a name after it");
                        if (c == '.')
                        {
                            parts.Add(new UssSelectorPart(UssSelectorPartKind.Class, id));
                            specificity += 10;
                        }
                        else if (c == '#')
                        {
                            parts.Add(new UssSelectorPart(UssSelectorPartKind.Id, id));
                            specificity += 100;
                        }
                        else
                        {
                            if (i < text.Length && text[i] == '(')
                                return BadSelector(line, whole, "':" + id + "(…)' is not part of USS");
                            if (!Contains(UssProperties.PseudoClasses, id))
                                return BadSelector(line, whole, "':" + id + "' is not a USS pseudo-class" + UiSuggest.Hint(id, UssProperties.PseudoClasses));
                            parts.Add(new UssSelectorPart(UssSelectorPartKind.PseudoClass, id));
                            specificity += 10;
                        }
                        continue;
                    }
                    if (IsNameChar(c))
                    {
                        if (parts.Count > 0) return BadSelector(line, whole, "a type name must start a selector part");
                        int start = i;
                        while (i < text.Length && IsNameChar(text[i])) i++;
                        parts.Add(new UssSelectorPart(UssSelectorPartKind.Type, text.Substring(start, i - start)));
                        specificity += 1;
                        continue;
                    }
                    return BadSelector(line, whole, "'" + c + "' is not part of USS selectors (USS has type, .class, #name, *, :state, ' ' and '>')");
                }
                if (pending == UssCombinator.Child) return BadSelector(line, whole, "'>' needs a selector on each side");
                compounds.Add(new UssCompound(parts, UssCombinator.None));
                return new UssSelector(compounds, specificity);
            }

            private UssSelector? BadSelector(int line, string selector, string why)
            {
                Problem(line, "'" + selector + "': " + why);
                return null;
            }

            // ---- 선언 ----

            private List<UssDeclaration> ReadDeclarations(string block, int line)
            {
                var declarations = new List<UssDeclaration>();
                foreach (var piece in SplitTopLevel(block, ';'))
                {
                    string text = piece.Text;
                    int at = line + piece.Line;
                    int lead = 0;
                    while (lead < text.Length && char.IsWhiteSpace(text[lead])) if (text[lead++] == '\n') at++;
                    text = text.Substring(lead).TrimEnd();
                    if (text.Length == 0) continue;
                    var declaration = ReadDeclaration(text, at);
                    if (declaration != null) declarations.Add(declaration);
                }
                return declarations;
            }

            private UssDeclaration? ReadDeclaration(string text, int line)
            {
                int colon = text.IndexOf(':');
                if (colon < 0)
                {
                    Problem(line, "'" + text + "' is not 'name: value'");
                    return null;
                }
                string name = text.Substring(0, colon).Trim();
                string value = text.Substring(colon + 1).Trim();
                bool variable = name.StartsWith("--", StringComparison.Ordinal);
                if (name.Length == 0 || !AllNameChars(name))
                {
                    Problem(line, "'" + name + "' is not a property name");
                    return null;
                }
                if (!variable && !Contains(UssProperties.Names, name))
                {
                    Problem(line, "'" + name + "' is not a USS property" + UiSuggest.Hint(name, UssProperties.Names));
                    return null;
                }
                if (value.EndsWith("!important", StringComparison.Ordinal))
                {
                    Problem(line, name + ": USS has no !important");
                    return null;
                }
                if (value.Length == 0)
                {
                    Problem(line, name + ": the value is empty");
                    return null;
                }
                int pos = 0;
                var values = new List<UssValue>();
                string? why = ReadValues(value, ref pos, values, inFunction: false);
                if (why == null) why = CheckFunctions(values);
                if (why != null)
                {
                    Problem(line, name + ": " + why);
                    return null;
                }
                return new UssDeclaration(line, name, values);
            }

            // 값들을 읽는다. 함수 안이면 ')'에서 멈춘다. 잘못이면 그 이유.
            private static string? ReadValues(string text, ref int pos, List<UssValue> values, bool inFunction)
            {
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (char.IsWhiteSpace(c)) { pos++; continue; }
                    if (c == ')')
                    {
                        if (!inFunction) return "a ')' without '('";
                        pos++;
                        return null;
                    }
                    if (c == ',')
                    {
                        values.Add(new UssValue(UssValueKind.Comma, ","));
                        pos++;
                        continue;
                    }
                    if (c == '"' || c == '\'')
                    {
                        var s = new StringBuilder();
                        int i = pos + 1;
                        while (i < text.Length && text[i] != c)
                        {
                            if (text[i] == '\\' && i + 1 < text.Length) i++;
                            s.Append(text[i++]);
                        }
                        if (i >= text.Length) return "the string is not closed (" + c + ")";
                        values.Add(new UssValue(UssValueKind.String, s.ToString()));
                        pos = i + 1;
                        continue;
                    }
                    int start = pos;
                    while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != ',' && text[pos] != '(' && text[pos] != ')'
                        && text[pos] != '"' && text[pos] != '\'') pos++;
                    string word = text.Substring(start, pos - start);
                    if (pos < text.Length && text[pos] == '(')
                    {
                        if (word.Length == 0) return "a '(' without a function name";
                        pos++;
                        var arguments = new List<UssValue>();
                        int before = pos;
                        string? why = ReadValues(text, ref pos, arguments, inFunction: true);
                        if (why != null) return why;
                        if (pos <= before || text[pos - 1] != ')') return word + "(…) is not closed (')')";
                        values.Add(new UssValue(UssValueKind.Function, word, arguments));
                        continue;
                    }
                    values.Add(new UssValue(UssValueKind.Word, word));
                }
                return inFunction ? "a '(' is not closed (')')" : null;
            }

            private static string? CheckFunctions(IReadOnlyList<UssValue> values)
            {
                foreach (var v in values)
                {
                    if (v.Kind != UssValueKind.Function) continue;
                    if (v.Text == "resource") return "resource() points inside the game build; use url(\"<path in the mods>\") in mod USS";
                    if (v.Text == "url" && (v.Arguments.Count != 1 || v.Arguments[0].Kind == UssValueKind.Comma || v.Arguments[0].Kind == UssValueKind.Function))
                        return "url() takes one path";
                    if (v.Text == "url" && UiPaths.WhyNotModPath(v.Arguments[0].Text) is string notModPath)
                        return "url(): " + notModPath;
                    if (v.Text == "var" && (v.Arguments.Count == 0 || !v.Arguments[0].Text.StartsWith("--", StringComparison.Ordinal)))
                        return "var() takes a variable name that starts with '--'";
                    string? inner = CheckFunctions(v.Arguments);
                    if (inner != null) return inner;
                }
                return null;
            }

            // ---- 도움 ----

            private struct Piece
            {
                public string Text;

                /// <summary>조각이 시작하는 줄이 전체의 시작에서 몇 줄 뒤인가.</summary>
                public int Line;
            }

            // 괄호와 따옴표 밖의 구분자로 나눈다
            private static List<Piece> SplitTopLevel(string text, char separator)
            {
                var pieces = new List<Piece>();
                int depth = 0;
                int start = 0;
                int line = 0;
                int startLine = 0;
                char quote = '\0';
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '\n') line++;
                    if (quote != '\0')
                    {
                        if (c == '\\') { i++; continue; }
                        if (c == quote) quote = '\0';
                        continue;
                    }
                    if (c == '"' || c == '\'') quote = c;
                    else if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == separator && depth == 0)
                    {
                        pieces.Add(new Piece { Text = text.Substring(start, i - start), Line = startLine });
                        start = i + 1;
                        startLine = line;
                    }
                }
                pieces.Add(new Piece { Text = text.Substring(start), Line = startLine });
                return pieces;
            }

            private static bool IsNameChar(char c) =>
                (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c > 127;

            private static bool AllNameChars(string text)
            {
                foreach (char c in text) if (!IsNameChar(c)) return false;
                return true;
            }

            private static bool Contains(IReadOnlyCollection<string> set, string value) =>
                set is ICollection<string> c ? c.Contains(value) : new List<string>(set).Contains(value);
        }
    }
}
