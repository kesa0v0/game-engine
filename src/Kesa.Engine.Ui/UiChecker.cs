using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kesa.Engine.Ui
{
    /// <summary>
    /// 문서를 올리기 전 검사 (G330~G345·G481): 부르는 함수, 커맨드·화면 동작 이름, <c>Include</c>, 창 겹침.
    /// 세션 없이도 쓸 수 있어 <c>engine check</c>가 같은 규칙으로 본다.
    /// </summary>
    public sealed class UiChecker
    {
        private readonly Func<string, int, ValueTask<string?>> _checkCall;
        private readonly IReadOnlyDictionary<string, int>? _handlers;
        private readonly Func<string, string?>? _files;
        private readonly Dictionary<string, UiDocument?> _includes = new Dictionary<string, UiDocument?>(StringComparer.Ordinal);

        /// <param name="checkCall">그 함수를 그 인자 수로 부를 수 있는지. 안 되면 이유.</param>
        /// <param name="handlers">커맨드와 화면 동작의 이름과 매개변수 수. 나중에 채워도 된다. null이면 <c>on-…</c>의 이름을 보지 않는다.</param>
        /// <param name="files"><c>Include</c>·<c>Style</c>의 <c>src</c>로 글을 찾는다. 못 찾으면 null.</param>
        public UiChecker(Func<string, int, ValueTask<string?>> checkCall, IReadOnlyDictionary<string, int>? handlers, Func<string, string?>? files)
        {
            _checkCall = checkCall;
            _handlers = handlers;
            _files = files;
        }

        /// <summary>문서와 그것이 끼우는 파일들을 본다.</summary>
        public ValueTask CheckAsync(UiDocument document, List<UiProblem> problems) =>
            ValidateElementAsync(document.Root, problems, new List<string> { document.Name }, insideWindow: false);

        /// <summary>문서가 적은 스타일시트를 찾을 수 있는지. 게임에서는 화면을 막지 않고 오류 목록에만 간다(G364).</summary>
        public void CheckStyles(UiDocument document, List<UiProblem> problems)
        {
            foreach (var path in document.StyleSheets)
            {
                string? notModPath = UiPaths.WhyNotModPath(path);
                if (notModPath != null) problems.Add(new UiProblem(document.Root.Place, "Style " + notModPath));
                else if (_files?.Invoke(path) == null) problems.Add(new UiProblem(document.Root.Place, "Style '" + path + "' was not found"));
            }
        }

        private readonly SortedDictionary<string, string?> _callChecks = new SortedDictionary<string, string?>(StringComparer.Ordinal);

        private async ValueTask CheckCallAsync(string place, string attribute, UiExpr expression, List<UiProblem> problems)
        {
            if (!(expression is CallExpr call)) return;
            string key = call.Function + "/" + call.Arguments.Count;
            if (!_callChecks.TryGetValue(key, out var why))
            {
                why = await _checkCall(call.Function, call.Arguments.Count);
                _callChecks[key] = why;
            }
            if (why != null) problems.Add(new UiProblem(place, attribute + ": " + why));
        }

        private async ValueTask ValidateElementAsync(UiElement element, List<UiProblem> problems, List<string> stack, bool insideWindow)
        {
            foreach (var b in element.Bindings)
                await CheckCallAsync(element.Place, b.Kind == UiBindKind.Property ? "bind-" + b.Name : "bind-class", b.Expression, problems);
            if (element.Repeat != null) await CheckCallAsync(element.Place, element.Repeat.Attribute, element.Repeat.Source, problems);
            foreach (var e in element.Events) ValidateEvent(element.Place, e, problems);
            // Include를 건너면 읽기만으로는 창 안인지 모르므로 올리기 전에 본다
            if (element.Window != null && insideWindow)
                problems.Add(new UiProblem(element.Place, "a window cannot be inside another window (move it out of the window element above it)"));
            bool inside = insideWindow || element.Window != null;
            if (element.DragHandle && !inside) problems.Add(new UiProblem(element.Place, "window-drag is only inside a window (an element with window=\"…\" above it)"));

            if (element.Include != null) await ValidateIncludeAsync(element, problems, stack, inside);
            foreach (var child in element.Children) await ValidateElementAsync(child, problems, stack, inside);
        }

        private void ValidateEvent(string place, UiEvent e, List<UiProblem> problems)
        {
            if (_handlers == null) return;
            string name = e.Call.Function;
            if (!_handlers.TryGetValue(name, out int parameters))
            {
                var known = new List<string>(_handlers.Keys);
                known.Sort(StringComparer.Ordinal);
                problems.Add(new UiProblem(place, "on-" + e.Name + ": '" + name + "' is neither a command nor a UI action" + UiSuggest.Hint(name, known)));
                return;
            }
            if (e.Call.Arguments.Count > parameters)
                problems.Add(new UiProblem(place, "on-" + e.Name + ": '" + name + "' takes " + parameters + " argument(s), got " + e.Call.Arguments.Count));
        }

        private async ValueTask ValidateIncludeAsync(UiElement element, List<UiProblem> problems, List<string> stack, bool insideWindow)
        {
            var include = element.Include!;
            var included = LoadInclude(include.Source, element.Place, problems);
            if (included == null) return;
            if (stack.Contains(include.Source))
            {
                problems.Add(new UiProblem(element.Place, "Include '" + include.Source + "' includes itself (" + string.Join(" -> ", stack) + ")"));
                return;
            }
            var given = new List<string>();
            foreach (var arg in include.Arguments)
            {
                given.Add(arg.Key);
                await CheckCallAsync(element.Place, arg.Key, arg.Value, problems);
                if (!Contains(included.Parameters, arg.Key))
                    problems.Add(new UiProblem(element.Place, include.Source + " does not take '" + arg.Key + "'" + UiSuggest.Hint(arg.Key, included.Parameters)));
            }
            foreach (var name in included.Parameters)
                if (!given.Contains(name)) problems.Add(new UiProblem(element.Place, "Include '" + include.Source + "' needs the value '" + name + "'"));

            stack.Add(include.Source);
            await ValidateElementAsync(included.Root, problems, stack, insideWindow);
            stack.RemoveAt(stack.Count - 1);
        }

        private static bool Contains(IReadOnlyList<string> list, string item)
        {
            foreach (var name in list)
                if (name == item) return true;
            return false;
        }

        internal UiDocument? LoadInclude(string source, string place, List<UiProblem>? problems)
        {
            if (_includes.TryGetValue(source, out var cached))
            {
                if (cached == null) problems?.Add(new UiProblem(place, "Include '" + source + "' could not be read"));
                return cached;
            }
            string? notModPath = UiPaths.WhyNotModPath(source);
            if (notModPath != null)
            {
                problems?.Add(new UiProblem(place, "Include " + notModPath));
                return null;
            }
            string? text = _files?.Invoke(source);
            if (text == null)
            {
                problems?.Add(new UiProblem(place, "Include '" + source + "' was not found" + (_files == null ? " (no way to find files was given)" : "")));
                return null;
            }
            var read = UxmlReader.Read(source, text);
            if (!read.Succeeded)
            {
                problems?.AddRange(read.Problems);
                _includes[source] = null;
                return null;
            }
            _includes[source] = read.Document;
            return read.Document;
        }
    }
}
